using Godot;

/// Which clip, not which situation. Callers name the sound they want and this
/// class owns everything about how often it is allowed to happen.
public enum Sfx
{
    RifleShot,
    MeleeSwing,
    BowShot,
    Impact,
    EnemyDeath,
    Explosion,
    LootDone,
    LevelUp,
    Hurt,
    Heartbeat,
    ExtractTick,
    Extracted,
    Dry,
}

/// Everything the run makes a noise about, mixed through one fixed pool.
///
/// The architectural rule is the same one the horde renderer follows: the cost of
/// a crowd must not scale with the crowd. A hundred kills a second cannot become
/// a hundred AudioStreamPlayers, so there is a fixed set of voices, a minimum gap
/// per sound, and one looping layer that stands in for the horde as a mass rather
/// than as individuals.
///
/// It subscribes where an event exists and polls where one would have to fire
/// every physics tick to be correct — contact damage and extraction progress are
/// rates, not moments, and turning them into events would mean inventing a
/// threshold inside the system that produces them rather than next to the
/// feedback that consumes them.
public partial class SoundDirector : Node
{
    /// Enough for a burst of overlapping one-shots without ever growing. Godot
    /// mixes idle players for free, so the ceiling costs nothing to raise a
    /// little and everything to not have.
    [Export] public int Voices { get; set; } = 14;

    /// Set against the captured mix, which is the only place the sum of fourteen
    /// voices can actually be measured.
    ///
    /// Godot's movie writer emits **32-bit integer** PCM. Read as 16-bit it looks
    /// like a wall of full-scale noise at 0.58 RMS with thousands of clipped
    /// samples a second, uniformly, for the entire recording — a completely
    /// convincing picture of a mix blowing up, and entirely an artefact of the
    /// wrong sample width. Read correctly the same file peaks at 0.23 with zero
    /// clipped samples. A constant reading is the tell: real gameplay audio moves.
    ///
    /// Measured properly, -6 dB still touched full scale for 89 samples in one
    /// second of the captured run — an explosion landing on top of gunfire. Not
    /// audible, but there is no limiter on the bus and the late horde is louder
    /// than anything a capture happens to catch, so the mix keeps a few dB of
    /// headroom rather than sitting exactly on the ceiling.
    [Export] public float MasterVolumeDb { get; set; } = -9.0f;

    /// Nearby enemies that make the ambience as loud as it gets. Not the whole
    /// arena: two hundred enemies on the far side of a wall are not pressure, and
    /// a layer that is already at full volume cannot report that they arrived.
    [Export] public float AmbienceRadius { get; set; } = 26.0f;
    [Export] public int AmbienceFullAt { get; set; } = 40;

    /// Below this fraction of maximum health the heartbeat starts, and it speeds
    /// up all the way down. A health bar is information; this is the part that
    /// makes leaving feel like the right idea before the bar is empty.
    [Export] public float HeartbeatBelow { get; set; } = 0.35f;

    /// Damage that has to accumulate before it is worth a sound. Contact damage
    /// from one walker is about 6 a second, so this is roughly "something is
    /// actually on me" rather than "I clipped a corner".
    [Export] public float HurtThreshold { get; set; } = 7.0f;

    private static readonly string[] ClipNames =
    {
        "fire_rifle", "fire_melee", "fire_bow", "impact", "enemy_death", "explosion",
        "loot_done", "level_up", "hurt", "heartbeat", "extract_tick", "extracted", "dry",
    };

    /// Shortest gap between two of the same sound, in seconds.
    ///
    /// This is the whole mixing strategy. A wide melee arc lands five hits in one
    /// frame and the late horde dies in double figures per second; without a gate
    /// those stack into a single loud smear that says nothing about how many.
    /// One clip per gate window still reads as "lots", and stays a sound.
    private static readonly float[] MinInterval =
    {
        0.0f,   // rifle shot — the weapon's own cooldown is the gate
        0.0f,   // melee swing — likewise
        0.0f,   // bow shot
        0.05f,  // impact
        0.07f,  // enemy death
        0.09f,  // explosion
        0.0f,   // loot done
        0.0f,   // level up
        0.30f,  // hurt
        0.0f,   // heartbeat — scheduled, never spammed
        0.0f,   // extract tick — likewise
        0.0f,   // extracted
        0.6f,   // dry
    };

    private readonly AudioStream?[] _clips = new AudioStream?[ClipNames.Length];
    private readonly float[] _lastPlayed = new float[ClipNames.Length];
    private AudioStreamPlayer[] _voices = System.Array.Empty<AudioStreamPlayer>();
    private AudioStreamPlayer? _ambience;
    private int _nextVoice;

    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private RunGrowth? _growth;
    private RunDirector? _director;

    private float _clock;
    private float _ambienceDb = -80.0f;
    private float _pendingHurt;
    private float _heartbeatAt;
    private float _tickAt;
    private bool _wasDry;
    private bool _hadOffer;
    private int _hazardCount;
    private ulong _rng = 0x2545F4914F6CDD1DUL;

    public override void _Ready()
    {
        // Before a single voice exists. The mix has held its headroom by
        // arithmetic — see `AudioBus`, which is where the one effect on the
        // master bus lives and why it is installed from code.
        AudioBus.Install();

        // Negative infinity rather than zero as the never-played marker. Zero is
        // a real value of the clock — the first second of a run — and a sentinel
        // that collides with live data gates nothing during exactly the window
        // where a scene full of spawns is loudest.
        for (int i = 0; i < _lastPlayed.Length; i++)
            _lastPlayed[i] = float.NegativeInfinity;

        for (int i = 0; i < ClipNames.Length; i++)
        {
            _clips[i] = GD.Load<AudioStream>($"res://assets/audio/{ClipNames[i]}.tres");
            if (_clips[i] == null)
                GD.PushWarning($"SoundDirector: missing {ClipNames[i]} — run BuildAudio.cs");
        }

        _voices = new AudioStreamPlayer[Voices];
        for (int i = 0; i < Voices; i++)
        {
            _voices[i] = new AudioStreamPlayer { Name = $"Voice{i}" };
            AddChild(_voices[i]);
        }

        var loop = GD.Load<AudioStream>("res://assets/audio/horde.tres");
        if (loop != null)
        {
            _ambience = new AudioStreamPlayer { Name = "Ambience", Stream = loop, VolumeDb = -80.0f };
            AddChild(_ambience);
            _ambience.Play();
        }

        Node? root = GetParent();
        _horde = root?.GetNodeOrNull<Horde>("Horde");
        _player = root?.GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
        _growth = root?.GetNodeOrNull<RunGrowth>("RunGrowth");
        _director = root?.GetNodeOrNull<RunDirector>("RunDirector");

        if (_horde != null)
        {
            _horde.EnemyKilled += OnEnemyKilled;
            _horde.Exploded += OnExploded;
            _hazardCount = _horde.Hazards.Count;
        }

        if (_weapons != null)
        {
            _weapons.Fired += OnFired;
            _weapons.Hit += OnHit;
        }

        if (_director != null)
        {
            _director.RunEnded += OnRunEnded;
            _director.BossArrived += OnBossArrived;
        }

        // On arrival, not once at _Ready — the boss cache and the supply drops
        // are added mid-run, and a crate that made no sound when it was emptied
        // is a crate the player is not sure they emptied.
        Node? crates = root?.GetNodeOrNull("LootContainers");
        if (crates != null)
        {
            foreach (Node child in crates.GetChildren())
                Watch(child);

            crates.ChildEnteredTree += Watch;
        }
    }

    private void Watch(Node child)
    {
        if (child is LootContainer container)
            container.Emptied += OnContainerEmptied;
    }

    /// Unsubscribing matters here because the horde and the weapon hold plain C#
    /// events: a delegate to a freed node is a use-after-free the moment the
    /// scene changes, and this node is the only listener that outlives nothing.
    public override void _ExitTree()
    {
        if (_horde != null)
        {
            _horde.EnemyKilled -= OnEnemyKilled;
            _horde.Exploded -= OnExploded;
        }

        if (_weapons != null)
        {
            _weapons.Fired -= OnFired;
            _weapons.Hit -= OnHit;
        }
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;

        StepAmbience((float)delta);
        StepHurt();
        StepHeartbeat();
        StepExtraction();
        StepWeaponState();
        StepHazards();
    }

    /// One voice for the whole crowd, mixed by how much of it is close. The count
    /// is read every frame because it is a linear scan over a few hundred entries
    /// that already sit in a contiguous array — cheaper than the bookkeeping that
    /// would avoid it.
    private void StepAmbience(float delta)
    {
        if (_ambience == null || _horde == null || _player == null)
            return;

        Vector3 at = _player.GlobalPosition;
        float radiusSqr = AmbienceRadius * AmbienceRadius;
        int near = 0;

        for (int i = 0; i < _horde.Pool.Count; i++)
        {
            Vector3 p = _horde.Pool.Position[i];
            float dx = p.X - at.X;
            float dz = p.Z - at.Z;
            if (dx * dx + dz * dz < radiusSqr)
                near++;
        }

        float loudness = Mathf.Clamp(near / (float)Mathf.Max(1, AmbienceFullAt), 0.0f, 1.0f);
        float target = near == 0 ? -80.0f : Mathf.Lerp(-34.0f, -10.0f, loudness) + MasterVolumeDb;

        // Exponential decay rather than a fixed step, like every other damped
        // value in the project (godot.md:50) — a horde that walks off screen
        // should fade, not cut.
        _ambienceDb = Mathf.Lerp(_ambienceDb, target, 1.0f - Mathf.Exp(-3.0f * delta));
        _ambience.VolumeDb = _ambienceDb;
    }

    private void StepHurt()
    {
        if (_player == null)
            return;

        _pendingHurt += _player.ConsumeDamageTaken();
        if (_pendingHurt < HurtThreshold)
            return;

        // Louder for a big single hit than for a slow grind, capped so a brute
        // and a bloater are distinguishable from a crowd without being painful.
        float volume = Mathf.Min(6.0f, (_pendingHurt / HurtThreshold - 1.0f) * 4.0f);
        _pendingHurt = 0.0f;
        Play(Sfx.Hurt, volume);
    }

    private void StepHeartbeat()
    {
        if (_player is not { IsAlive: true } player || player.MaxHealth <= 0.0f)
            return;

        float fraction = player.Health / player.MaxHealth;
        if (fraction >= HeartbeatBelow)
        {
            _heartbeatAt = 0.0f;
            return;
        }

        if (_clock < _heartbeatAt)
            return;

        // Faster the closer to death, so the tempo is the reading rather than the
        // presence of the sound.
        float urgency = 1.0f - Mathf.Clamp(fraction / HeartbeatBelow, 0.0f, 1.0f);
        _heartbeatAt = _clock + Mathf.Lerp(1.5f, 0.55f, urgency);
        Play(Sfx.Heartbeat, Mathf.Lerp(-6.0f, 2.0f, urgency), pitch: Mathf.Lerp(0.95f, 1.15f, urgency));
    }

    private void StepExtraction()
    {
        if (_director is not { State: RunState.Running })
            return;

        foreach (ExtractionZone pad in _director.Pads)
        {
            if (pad is not { Open: true, PlayerInside: true } || pad.Progress is <= 0.0f or >= 1.0f)
                continue;

            if (_clock < _tickAt)
                return;

            // Rising in pitch as the bar fills: the player is usually looking at
            // the horde, not at the bar, and this is what tells them how long is
            // left without asking them to look away.
            _tickAt = _clock + 0.55f;
            Play(Sfx.ExtractTick, -4.0f, pitch: Mathf.Lerp(0.9f, 1.4f, pad.Progress));
            return;
        }

        _tickAt = 0.0f;
    }

    /// The click of a weapon that has nothing left. Edge-triggered, because dry is
    /// a state that persists — a sound per frame of it would be a buzz.
    private void StepWeaponState()
    {
        bool dry = _weapons?.IsDry ?? false;
        if (dry && !_wasDry)
            Play(Sfx.Dry);
        _wasDry = dry;

        bool offer = _growth?.HasOffer ?? false;
        if (offer && !_hadOffer)
            Play(Sfx.LevelUp, 1.0f);
        _hadOffer = offer;
    }

    /// Burning ground has no event of its own — it is a patch that exists rather
    /// than a moment that happens. The count going up is the moment.
    private void StepHazards()
    {
        if (_horde == null)
            return;

        int count = _horde.Hazards.Count;
        if (count > _hazardCount)
            Play(Sfx.MeleeSwing, -1.0f, pitch: 0.55f);
        _hazardCount = count;
    }

    /// Three clips for nine weapons, pitched and levelled by what the weapon is.
    ///
    /// **The clip set is not the problem and more clips would not fix it.** Every
    /// firearm played `RifleShot` at exactly -3 dB and exactly unit pitch, so a
    /// pump shotgun and a marksman rifle were the same sound at the same volume —
    /// and weight is most of what a player hears in a weapon. Pitch and level are
    /// free, already supported by `Play`, and carry the difference between a light
    /// automatic and something that kicks.
    ///
    /// Derived from the weapon's own numbers so a new one in
    /// `resources/weapons/` sounds like itself without being listed here.
    private void OnFired(WeaponResource weapon, Vector3 origin, Vector2 direction)
    {
        // How heavy one pull is, against the heaviest thing in the game. A slow
        // weapon that hits hard sits at the bottom of this and sounds it.
        float weight = Mathf.Clamp(weapon.BaseDamage / 34.0f, 0.0f, 1.0f);

        Sfx clip = weapon.Category switch
        {
            WeaponCategory.MeleeShort or WeaponCategory.MeleeLong => Sfx.MeleeSwing,
            WeaponCategory.BowCrossbow => Sfx.BowShot,
            _ => Sfx.RifleShot,
        };

        // Down a fifth across the range. Deeper than that and a heavy weapon
        // stops reading as the same family of sound as the light one, which is
        // the point at which "different weapon" becomes "different game".
        float pitch = Mathf.Lerp(1.22f, 0.74f, weight);
        float volume = Mathf.Lerp(-7.0f, -1.0f, weight);

        // A melee swing is a swing whatever it is swinging, so its pitch comes
        // from reach rather than damage — a scythe sweeping 3.4 m is a lower
        // noise than a knife at 1.6 m, and both are quiet.
        if (clip == Sfx.MeleeSwing)
        {
            pitch = Mathf.Lerp(1.15f, 0.80f, Mathf.Clamp(weapon.BaseRange / 3.4f, 0.0f, 1.0f));
            volume = -4.0f;
        }

        Play(clip, volume, pitch);

        // A launched charge gets a second, much lower voice under the shot. One
        // clip cannot be both a report and a thump, and the bolt launcher is the
        // only weapon in the set that should be felt leaving.
        if (weapon.Trait == WeaponTrait.Blast)
            Play(Sfx.Explosion, -12.0f, 0.5f);
    }

    /// Louder and lower the harder it landed.
    ///
    /// A knife tick and a thirty-four damage marksman round used to be the same
    /// impact at the same level, which threw away the only per-hit feedback the
    /// audio had. Chain jumps do a fraction of the damage and now sound like it,
    /// which is what stops a chained volley from turning into a wall of noise.
    private void OnHit(Vector3 where, WeaponCategory category, float damage)
    {
        float weight = Mathf.Clamp(damage / 30.0f, 0.0f, 1.0f);
        Play(Sfx.Impact, Mathf.Lerp(-9.0f, -2.0f, weight), Mathf.Lerp(1.18f, 0.82f, weight));
    }

    private void OnEnemyKilled(int type, Vector3 position) => Play(Sfx.EnemyDeath, -5.0f);

    /// The explosion clip dropped nearly two octaves. A fifteenth synthesised
    /// waveform would be the tidy answer, but a low, slow blast is already the
    /// shape of a roar, and a sound the player has heard forty times arriving
    /// wrong-sized is more unsettling than an unfamiliar one.
    private void OnBossArrived() => Play(Sfx.Explosion, 2.0f, 0.32f);

    private void OnExploded(Vector3 position) => Play(Sfx.Explosion, 2.0f);

    /// Only on the last haul. A crate that pays out three times as the player
    /// drops things and comes back would otherwise chime three times, and the
    /// chime means "that one is finished".
    private void OnContainerEmptied(int value, bool finished)
    {
        if (finished)
            Play(Sfx.LootDone);
    }

    private void OnRunEnded(int state, int banked)
    {
        // The ambience stops with the run. Leaving it under an EXTRACTED banner
        // is the audio version of the stale hold bar that only the video caught.
        if (_ambience != null)
            _ambience.Stop();

        if ((RunState)state == RunState.Extracted)
            Play(Sfx.Extracted, 3.0f);
        else
            Play(Sfx.Explosion, 0.0f, pitch: 0.55f);
    }

    /// Plays a clip on a free voice, subject to that clip's minimum gap.
    ///
    /// Public so a probe can drive it directly; every in-game caller goes through
    /// the handlers above.
    public bool Play(Sfx id, float volumeDb = 0.0f, float pitch = 1.0f)
    {
        int index = (int)id;
        if (index < 0 || index >= _clips.Length || _clips[index] == null)
            return false;

        if (_clock - _lastPlayed[index] < MinInterval[index])
            return false;

        _lastPlayed[index] = _clock;

        AudioStreamPlayer voice = TakeVoice();
        voice.Stream = _clips[index];
        voice.VolumeDb = MasterVolumeDb + volumeDb;

        // A few percent of detune, so a stream of identical shots reads as many
        // shots rather than as one sample on repeat.
        voice.PitchScale = Mathf.Max(0.05f, pitch * (1.0f + (NextFloat() - 0.5f) * 0.08f));
        voice.Play();
        return true;
    }

    /// An idle voice if there is one, otherwise the oldest in the rotation. Never
    /// allocates: running out of voices costs the quietest thing playing, which is
    /// the correct answer during the only moments it can happen.
    private AudioStreamPlayer TakeVoice()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            int at = (_nextVoice + i) % _voices.Length;
            if (!_voices[at].Playing)
            {
                _nextVoice = (at + 1) % _voices.Length;
                return _voices[at];
            }
        }

        AudioStreamPlayer stolen = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Length;
        return stolen;
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
