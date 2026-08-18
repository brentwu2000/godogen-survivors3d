using Godot;

/// Synthesises every sound the game makes into assets/audio/*.tres.
///
///   godot --headless --script scripts/tools/BuildAudio.cs
///
/// Generated rather than sourced, for the same reason the scenes are: a recipe in
/// code can be re-tuned and re-run, and it carries no licence to track. Nothing
/// here is trying to sound recorded — a survivors-like needs a hundred shots a
/// minute to read as separate events, which wants short, dry, distinct noises far
/// more than it wants realism.
///
/// Saved as `.tres` AudioStreamWav rather than `.wav` on purpose. A .wav goes
/// through Godot's importer, whose loop setting lives in a generated `.import`
/// file this tool does not own — and the horde ambience is unusable if its loop
/// flag silently comes back Disabled. A resource carries its own loop points.
public partial class BuildAudio : SceneTree
{
    private const string OutputDir = "res://assets/audio";

    /// Everything here is noise bursts and low sines; nothing has content above
    /// about 8 kHz, so the extra bytes of 44.1k would buy silence.
    private const int Rate = 22050;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        (string Name, float[] Samples, bool Loop)[] clips =
        {
            ("fire_rifle", RifleShot(), false),
            ("fire_melee", MeleeSwing(), false),
            ("fire_bow", BowShot(), false),
            ("impact", Impact(), false),
            ("enemy_death", EnemyDeath(), false),
            ("explosion", Explosion(), false),
            ("loot_done", LootDone(), false),
            ("level_up", LevelUp(), false),
            ("hurt", Hurt(), false),
            ("heartbeat", Heartbeat(), false),
            ("extract_tick", ExtractTick(), false),
            ("extracted", Extracted(), false),
            ("dry", DryClick(), false),
            ("horde", HordeAmbience(), true),

            // The music bed. Four loops of identical length at one tempo, mixed
            // by fading each in and out rather than by switching tracks — a cut
            // between two pieces of music is heard as a glitch, and a crossfade
            // between two pieces that do not share a tempo is heard as a worse
            // one. Layers that were written to sit on top of each other can be
            // added and removed at any bar and it still sounds deliberate.
            //
            // Texture, not melody. A synthesised tune is both unpleasant and
            // finite — the player hears it forty times an hour — while a drone,
            // a pulse and a noise bed are things a run can be underneath for
            // five minutes without anyone deciding to mute the game.
            ("music_bed", MusicBed(), true),
            ("music_pulse", MusicPulse(), true),
            ("music_tension", MusicTension(), true),
            ("music_boss", MusicBoss(), true),
        };

        foreach ((string name, float[] samples, bool loop) in clips)
        {
            var stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = Rate,
                Stereo = false,
                Data = ToPcm16(samples),
            };

            if (loop)
            {
                stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                stream.LoopBegin = 0;
                stream.LoopEnd = samples.Length;
            }

            string path = $"{OutputDir}/{name}.tres";
            Error err = ResourceSaver.Save(stream, path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }

            GD.Print($"Saved {path} ({samples.Length / (float)Rate:F2}s{(loop ? ", looping" : "")})");
        }

        return true;
    }

    // ---- the sounds ----------------------------------------------------------

    /// A crack over a short low thump. The crack is what places it in time and the
    /// thump is what gives it weight; either alone reads as a click or as a pop.
    private static float[] RifleShot()
    {
        float[] b = Buffer(0.18f);
        var rng = new Rng(0x9E3779B97F4A7C15UL);
        float phase = 0.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            phase += Mathf.Tau * Sweep(t, 240.0f, 55.0f, 34.0f) / Rate;
            b[i] = rng.Bipolar() * Mathf.Exp(-42.0f * t) * 0.85f
                 + Mathf.Sin(phase) * Mathf.Exp(-26.0f * t) * 0.7f;
        }

        Lowpass(b, 7000.0f, 900.0f);
        Finish(b);
        return b;
    }

    /// Air rather than impact: a band of noise swept past the ear. A swing that
    /// hits nothing still has to be audible, or a melee player cannot tell a
    /// weapon on cooldown from one that is out of range.
    private static float[] MeleeSwing()
    {
        float[] b = Buffer(0.26f);
        var rng = new Rng(0xC2B2AE3D27D4EB4FUL);

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;

            // Rise then fall, so the swing passes rather than starts loud.
            float envelope = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t / 0.26f, 0.0f, 1.0f));
            b[i] = rng.Bipolar() * envelope * envelope;
        }

        // The sweep is the whole sound — a fixed band is a hiss, a moving one is
        // something travelling through the air.
        Bandsweep(b, 700.0f, 2600.0f);
        Finish(b, 0.7f);
        return b;
    }

    /// A string releasing: pitched, dropping fast, with a click of the loose.
    private static float[] BowShot()
    {
        float[] b = Buffer(0.22f);
        var rng = new Rng(0x165667B19E3779F9UL);
        float phase = 0.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            phase += Mathf.Tau * Sweep(t, 760.0f, 170.0f, 26.0f) / Rate;
            b[i] = Mathf.Sin(phase) * Mathf.Exp(-16.0f * t) * 0.8f
                 + rng.Bipolar() * Mathf.Exp(-90.0f * t) * 0.5f;
        }

        Finish(b);
        return b;
    }

    /// Deliberately tiny. This plays once per landed hit and a wide melee arc
    /// lands five at a time — anything with a tail turns a swing into a smear.
    private static float[] Impact()
    {
        float[] b = Buffer(0.06f);
        var rng = new Rng(0x27D4EB2F165667C5UL);

        for (int i = 0; i < b.Length; i++)
            b[i] = rng.Bipolar() * Mathf.Exp(-70.0f * (i / (float)Rate));

        // Thin it out: the low end belongs to the weapon, and leaving it here
        // makes every hit compete with the shot that caused it.
        Highpass(b, 1200.0f);
        Finish(b, 0.55f);
        return b;
    }

    /// Wet and falling. Pitched downward so a kill is distinguishable from a hit
    /// without being louder than one.
    private static float[] EnemyDeath()
    {
        float[] b = Buffer(0.3f);
        var rng = new Rng(0x9E3779B185EBCA87UL);
        float phase = 0.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            phase += Mathf.Tau * Sweep(t, 190.0f, 48.0f, 12.0f) / Rate;
            b[i] = (Mathf.Sin(phase) * 0.6f + rng.Bipolar() * 0.5f) * Mathf.Exp(-11.0f * t);
        }

        Lowpass(b, 2200.0f, 400.0f);
        Finish(b, 0.6f);
        return b;
    }

    /// The only long sound in the set. A pipe bomb is the most expensive thing in
    /// the backpack, and it has to land like it.
    private static float[] Explosion()
    {
        float[] b = Buffer(0.85f);
        var rng = new Rng(0xFF51AFD7ED558CCDUL);
        float phase = 0.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            phase += Mathf.Tau * Sweep(t, 90.0f, 28.0f, 6.0f) / Rate;
            b[i] = rng.Bipolar() * Mathf.Exp(-4.5f * t)
                 + Mathf.Sin(phase) * Mathf.Exp(-5.0f * t) * 0.9f;
        }

        Lowpass(b, 5000.0f, 180.0f);
        SoftClip(b, 1.6f);
        Finish(b);
        return b;
    }

    /// Two rising notes. The container is already empty by the time this plays —
    /// what it confirms is that the seconds spent standing still bought something.
    private static float[] LootDone() => Notes(new[] { (660.0f, 0.10f), (990.0f, 0.18f) }, 0.55f);

    /// A triad, because the offer it announces is the only thing that stops the
    /// player from just steering. Three notes so it cannot be mistaken for loot.
    private static float[] LevelUp() => Notes(new[] { (523.0f, 0.13f), (659.0f, 0.13f), (784.0f, 0.30f) }, 0.5f);

    /// Low and detuned. Two close frequencies beat against each other, which is
    /// unpleasant on purpose — this is the sound the player should want to stop.
    private static float[] Hurt()
    {
        float[] b = Buffer(0.24f);
        var rng = new Rng(0xD6E8FEB86659FD93UL);

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            b[i] = (Mathf.Sin(Mathf.Tau * 158.0f * t) + Mathf.Sin(Mathf.Tau * 171.0f * t)) * 0.35f * Mathf.Exp(-12.0f * t)
                 + rng.Bipolar() * Mathf.Exp(-45.0f * t) * 0.4f;
        }

        Lowpass(b, 3000.0f, 700.0f);
        Finish(b, 0.75f);
        return b;
    }

    /// Two thumps, played on a shortening interval as health drops. A number
    /// falling in the corner is information; this is pressure.
    private static float[] Heartbeat()
    {
        float[] b = Buffer(0.55f);

        Thump(b, 0.0f, 62.0f);
        Thump(b, 0.19f, 54.0f);

        Lowpass(b, 400.0f, 160.0f);
        Finish(b, 0.85f);
        return b;

        static void Thump(float[] buffer, float at, float frequency)
        {
            int start = (int)(at * Rate);
            float phase = 0.0f;

            for (int i = start; i < buffer.Length; i++)
            {
                float t = (i - start) / (float)Rate;
                phase += Mathf.Tau * Sweep(t, frequency * 1.8f, frequency, 40.0f) / Rate;
                buffer[i] += Mathf.Sin(phase) * Mathf.Exp(-18.0f * t);
            }
        }
    }

    /// Clean and high, so it cuts through a horde. The extraction hold is the one
    /// timer the player cannot afford to lose track of while looking elsewhere.
    private static float[] ExtractTick() => Notes(new[] { (1180.0f, 0.09f) }, 0.4f);

    /// The payoff. Held long enough to still be sounding while the banner reads.
    private static float[] Extracted() => Notes(new[] { (392.0f, 0.16f), (494.0f, 0.16f), (587.0f, 0.16f), (784.0f, 0.55f) }, 0.5f);

    /// A firing pin on nothing. Short and unmusical — being dry is a fact to
    /// notice, not an event to celebrate.
    private static float[] DryClick()
    {
        float[] b = Buffer(0.05f);
        var rng = new Rng(0x94D049BB133111EBUL);

        for (int i = 0; i < b.Length; i++)
            b[i] = rng.Bipolar() * Mathf.Exp(-160.0f * (i / (float)Rate));

        Highpass(b, 2000.0f);
        Finish(b, 0.45f);
        return b;
    }

    /// One looping layer for the whole horde, mixed by how many are close. Several
    /// hundred AudioStreamPlayers is the thing this architecture has spent every
    /// phase avoiding, and a crowd does not sound like N copies of one voice
    /// anyway — it sounds like a single low mass that swells.
    ///
    /// Every component is periodic over the loop length or crossfaded into itself,
    /// because a loop that clicks once a bar is worse than no ambience at all.
    private static float[] HordeAmbience()
    {
        const float seconds = 2.0f;
        int length = (int)(seconds * Rate);
        int fade = Rate / 8;

        // Extra tail to fold back over the head — the noise has no period of its
        // own, so the seam has to be manufactured.
        var raw = new float[length + fade];
        var rng = new Rng(0xBF58476D1CE4E5B9UL);

        for (int i = 0; i < raw.Length; i++)
            raw[i] = rng.Bipolar();

        Lowpass(raw, 220.0f, 220.0f);

        var b = new float[length];
        for (int i = 0; i < length; i++)
        {
            b[i] = i < fade
                ? Mathf.Lerp(raw[length + i], raw[i], i / (float)fade)
                : raw[i];

            // Whole numbers of cycles across the loop, so the drone meets itself
            // at the seam: 0.5 Hz * 96 = 48 Hz, * 111 = 55.5 Hz.
            float t = i / (float)Rate;
            b[i] += (Mathf.Sin(Mathf.Tau * 48.0f * t) + Mathf.Sin(Mathf.Tau * 55.5f * t)) * 0.22f;

            // One slow swell per loop, and again a whole cycle so it does not step.
            b[i] *= 0.75f + 0.25f * Mathf.Sin(Mathf.Tau * t / seconds);
        }

        // No end taper: tapering a loop is exactly the click it is meant to avoid.
        Normalise(b, 0.8f);
        return b;
    }

    // ---- synthesis helpers ---------------------------------------------------

    // ---- the music bed -------------------------------------------------------
    //
    // One tempo and one length for all four layers, so any subset can be playing
    // and they stay in phase forever. 80 BPM, 16 bars of 4/4 — 48 seconds, which
    // is long enough not to be recognisable as a loop inside a 300-second run and
    // short enough that four of them are under a megabyte at this sample rate.

    private const float MusicBpm = 80.0f;
    private const float MusicBeat = 60.0f / MusicBpm;
    private const float MusicSeconds = MusicBeat * 4.0f * 16.0f;

    /// Always on: two detuned low sines a fifth apart, breathing.
    ///
    /// Detuning is the whole trick. Two oscillators at exactly the same pitch are
    /// one louder oscillator; a couple of cents apart they beat against each
    /// other slowly, and a drone that moves is one the ear stops resolving into a
    /// tone it can get tired of.
    private static float[] MusicBed()
    {
        float[] b = Buffer(MusicSeconds);
        float a = 0.0f, c = 0.0f, d = 0.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;

            // A slow swell across the loop, so the bed has a shape rather than
            // being a held pad. Sine over the whole length means the seam is at
            // the quietest point, where a discontinuity would be inaudible even
            // if the loop were not sample-exact.
            float breath = 0.55f + 0.45f * Mathf.Sin(Mathf.Tau * t / MusicSeconds);

            a += Mathf.Tau * 55.0f / Rate;
            c += Mathf.Tau * 55.35f / Rate;
            d += Mathf.Tau * 82.5f / Rate;

            b[i] = (Mathf.Sin(a) * 0.5f + Mathf.Sin(c) * 0.45f + Mathf.Sin(d) * 0.18f) * breath;
        }

        Normalise(b, 0.55f);
        return b;
    }

    /// A heartbeat on the beat: a short low thud every crotchet.
    ///
    /// The layer that turns the bed into time passing. It is deliberately not a
    /// drum kit — a kick and snare pattern is a genre, and this has to sit under
    /// gunfire without competing with it for the same band.
    private static float[] MusicPulse()
    {
        float[] b = Buffer(MusicSeconds);
        int beats = (int)(MusicSeconds / MusicBeat);

        for (int beat = 0; beat < beats; beat++)
        {
            int start = (int)(beat * MusicBeat * Rate);
            float phase = 0.0f;

            // Every fourth beat lands harder, which is what makes four beats read
            // as a bar rather than as four beats.
            float weight = beat % 4 == 0 ? 1.0f : 0.55f;

            for (int i = 0; i < (int)(0.28f * Rate) && start + i < b.Length; i++)
            {
                float t = i / (float)Rate;
                phase += Mathf.Tau * Sweep(t, 92.0f, 44.0f, 30.0f) / Rate;
                b[start + i] += Mathf.Sin(phase) * Mathf.Exp(-11.0f * t) * weight;
            }
        }

        Normalise(b, 0.6f);
        return b;
    }

    /// Filtered noise that swells on the half-bar, and a high shimmer.
    ///
    /// This is the layer that says the run has stopped being comfortable. Noise
    /// rather than a note, because a rising tone is an alarm and an alarm cannot
    /// stay on for two minutes.
    private static float[] MusicTension()
    {
        float[] b = Buffer(MusicSeconds);
        var rng = new Rng(0x2545F4914F6CDD1DUL);
        float low = 0.0f, high = 0.0f, shimmer = 0.0f;

        float bar = MusicBeat * 4.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;

            // Position inside the half-bar, so the swell resets twice a bar and
            // the layer has a pulse of its own that agrees with the drum's.
            float phase = Mathf.PosMod(t, bar * 0.5f) / (bar * 0.5f);
            float swell = phase * phase;

            // One-pole low-pass, then a one-pole high-pass over the top: a band
            // around a few hundred hertz, which is where the ear hears unease and
            // where nothing else in this mix is sitting.
            float noise = rng.Bipolar();
            low += (noise - low) * 0.06f;
            high += (low - high) * 0.006f;

            shimmer += Mathf.Tau * 1320.0f / Rate;

            b[i] = (low - high) * swell * 1.2f
                 + Mathf.Sin(shimmer) * swell * 0.05f;
        }

        Normalise(b, 0.5f);
        return b;
    }

    /// The boss layer: a low half-time thud on the bar, and a dissonant tone.
    ///
    /// A minor second against the bed's root. It is the one interval nobody hears
    /// as music by accident, so the layer is unmistakably an arrival rather than
    /// the same music louder.
    private static float[] MusicBoss()
    {
        float[] b = Buffer(MusicSeconds);
        int bars = (int)(MusicSeconds / (MusicBeat * 4.0f));
        float grind = 0.0f;

        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;

            // 58.3 Hz against the bed's 55: a semitone up, beating hard.
            grind += Mathf.Tau * 58.3f / Rate;
            b[i] = Mathf.Sin(grind) * 0.35f * (0.6f + 0.4f * Mathf.Sin(Mathf.Tau * t / 7.0f));
        }

        for (int bar = 0; bar < bars; bar++)
        {
            int start = (int)(bar * MusicBeat * 4.0f * Rate);
            float phase = 0.0f;

            for (int i = 0; i < (int)(0.9f * Rate) && start + i < b.Length; i++)
            {
                float t = i / (float)Rate;
                phase += Mathf.Tau * Sweep(t, 70.0f, 28.0f, 9.0f) / Rate;
                b[start + i] += Mathf.Sin(phase) * Mathf.Exp(-3.4f * t) * 0.9f;
            }
        }

        SoftClip(b, 1.3f);
        Normalise(b, 0.7f);
        return b;
    }

    private static float[] Buffer(float seconds) => new float[(int)(seconds * Rate)];

    /// Frequency gliding from `from` to `to` with a time constant. Callers feed
    /// this into a phase accumulator rather than into sin(tau*f*t) — the latter
    /// is a common and wrong shortcut that sweeps at twice the intended rate.
    private static float Sweep(float t, float from, float to, float rate) =>
        Mathf.Lerp(from, to, 1.0f - Mathf.Exp(-rate * t));

    /// Sequence of pure tones with a soft attack, laid end to end. Enough for
    /// every UI sound in the game; anything richer would compete with the horde.
    private static float[] Notes((float Frequency, float Seconds)[] notes, float peak)
    {
        float total = 0.0f;
        foreach ((float _, float seconds) in notes)
            total += seconds;

        float[] b = Buffer(total);
        int offset = 0;

        foreach ((float frequency, float seconds) in notes)
        {
            int count = (int)(seconds * Rate);
            for (int i = 0; i < count && offset + i < b.Length; i++)
            {
                float t = i / (float)Rate;

                // 4 ms attack. A tone starting at full amplitude clicks, and a
                // click on every pickup is what makes a UI sound tiring.
                float attack = Mathf.Min(1.0f, t / 0.004f);
                b[offset + i] = Mathf.Sin(Mathf.Tau * frequency * t) * attack * Mathf.Exp(-6.0f * t);
            }

            offset += count;
        }

        Finish(b, peak);
        return b;
    }

    /// One-pole lowpass with a cutoff that travels across the buffer. Cheap, and
    /// the movement is what turns static noise into an event.
    private static void Lowpass(float[] buffer, float cutoffStart, float cutoffEnd)
    {
        float y = 0.0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float cutoff = Mathf.Lerp(cutoffStart, cutoffEnd, i / (float)buffer.Length);
            float a = 1.0f - Mathf.Exp(-Mathf.Tau * cutoff / Rate);
            y += a * (buffer[i] - y);
            buffer[i] = y;
        }
    }

    private static void Highpass(float[] buffer, float cutoff)
    {
        float y = 0.0f;
        float a = 1.0f - Mathf.Exp(-Mathf.Tau * cutoff / Rate);

        for (int i = 0; i < buffer.Length; i++)
        {
            y += a * (buffer[i] - y);
            buffer[i] -= y;
        }
    }

    /// Lowpass minus lowpass: a moving band. Two poles either side of a travelling
    /// centre, which is as much filter as a whoosh needs.
    private static void Bandsweep(float[] buffer, float centreStart, float centreEnd)
    {
        var low = (float[])buffer.Clone();
        Lowpass(low, centreStart * 2.4f, centreEnd * 2.4f);
        Lowpass(buffer, centreStart * 0.5f, centreEnd * 0.5f);

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = low[i] - buffer[i];
    }

    private static void SoftClip(float[] buffer, float drive)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (float)System.Math.Tanh(buffer[i] * drive);
    }

    private static void Normalise(float[] buffer, float peak)
    {
        float max = 0.0f;
        foreach (float sample in buffer)
            max = Mathf.Max(max, Mathf.Abs(sample));

        if (max <= 0.0001f)
            return;

        float gain = peak / max;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= gain;
    }

    /// Normalise, then taper the tail to zero. A one-shot cut off mid-cycle ends
    /// on a step, and a step is a click — audible on every single shot.
    private static void Finish(float[] buffer, float peak = 0.9f)
    {
        Normalise(buffer, peak);

        int fade = Mathf.Min(buffer.Length, Rate / 200);
        for (int i = 0; i < fade; i++)
            buffer[buffer.Length - 1 - i] *= i / (float)fade;
    }

    private static byte[] ToPcm16(float[] samples)
    {
        var data = new byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            int value = Mathf.RoundToInt(Mathf.Clamp(samples[i], -1.0f, 1.0f) * 32767.0f);
            data[i * 2] = (byte)(value & 0xFF);
            data[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return data;
    }

    /// The same xorshift the rest of the project uses. Deterministic, so a rebuild
    /// produces byte-identical audio and a diff means someone changed a recipe.
    private sealed class Rng
    {
        private ulong _state;

        public Rng(ulong seed) => _state = seed;

        public float Bipolar()
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return (_state >> 40) / 8388608.0f - 1.0f;
        }
    }
}
