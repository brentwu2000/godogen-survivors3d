using Godot;

/// Checks that the music has the run's shape rather than a volume knob.
///
///   godot --headless --script test/MusicProbe.cs
///
/// Exit code is the verdict, which for audio is an awkward thing to claim — a
/// probe cannot hear. What it can check is every property the mix depends on and
/// none of which is audible as itself: that the four loops are the same length
/// (unequal lengths drift apart over a run and the layers stop agreeing about
/// where the bar is), that they all start together and never stop, that layers
/// arrive and leave with the run's state, and that a threshold does not flicker.
///
/// The thing this cannot check is whether it sounds good. That was checked by
/// listening, which is the only way, and this exists so that a change three
/// phases from now does not silently undo it.
public partial class MusicProbe : SceneTree
{
    private Node? _scene;
    private MusicDirector? _music;
    private RunDirector? _director;
    private Horde? _horde;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    // Indices into the layer table. Written out rather than referenced, because
    // the enum is private to the director — and a probe that could see it would
    // be testing the same list twice.
    private const int Bed = 0;
    private const int Pulse = 1;
    private const int Tension = 2;
    private const int Boss = 3;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            _music = _scene?.GetNodeOrNull<MusicDirector>("Music");
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");

            if (_music == null || _director == null || _horde == null)
            {
                GD.PushError("PROBE FAILED - scene is missing a required node");
                Quit(1);
                return true;
            }

            _director.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageLayersAgree, "four layers, one length, all playing from the first frame");
            case 1: return RunStage(StageQuietRunIsQuiet, "an opening run is the bed and nothing else");
            case 2: return RunStage(StageIntensityAddsLayers, "layers arrive as the run gets worse");
            case 3: return RunStage(StageCrowdRaisesTension, "being swarmed early sounds like being swarmed");
            case 4: return RunStage(StageBossArrivesInSound, "the boss layer follows the boss");
            case 5: return RunStage(StageThresholdDoesNotFlicker, "a value sitting on a threshold does not chatter");
            case 6: return RunStage(StageRunEndSilences, "the music stops when the run does");
            case 7: return RunStage(StageLayersHaveContent, "every layer is audible, and the pulse actually pulses");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// The property everything else rests on.
    ///
    /// Layers of different lengths loop at different moments, so after a minute
    /// the pulse is no longer on the bed's beat and the mix is two pieces of
    /// music played at once. It would take a while to become obvious and would
    /// never be reported as "the loops are different lengths".
    private bool? StageLayersAgree(int tick)
    {
        int count = _music!.LayerCount;
        double first = _music.LengthOf(0);

        bool sameLength = true;
        bool allPlaying = true;
        var lengths = new System.Collections.Generic.List<string>();

        for (int i = 0; i < count; i++)
        {
            double length = _music.LengthOf(i);
            lengths.Add($"{length:F2}s");

            sameLength &= Mathf.Abs(length - first) < 0.01;
            allPlaying &= _music.Playing(i);
        }

        GD.Print($"  {count} layers: {string.Join(", ", lengths)}, all playing: {allPlaying}");

        return count == 4 && first > 1.0 && sameLength && allPlaying;
    }

    private bool? StageQuietRunIsQuiet(int tick)
    {
        _director!.SetElapsedForTesting(0.0f);
        _horde!.Pool.Clear();

        // Every read of a target has to be preceded by a decision. The first
        // version of this stage read them straight after moving the clock and
        // got the previous stage's answer — which for the opening-quiet check
        // meant reading the untouched initial array, all zeros, and failing on
        // the bed being absent when the bed had simply never been asked for.
        _music!.ForceDecideForTesting();

        GD.Print($"  at the start: bed {_music.TargetOf(Bed):F0}, pulse {_music.TargetOf(Pulse):F0}, " +
                 $"tension {_music.TargetOf(Tension):F0}, boss {_music.TargetOf(Boss):F0}");

        // The bed is unconditional; nothing else should be asked for yet. A game
        // that opens with every layer up has nowhere to go.
        return _music.TargetOf(Bed) > 0.5f
               && _music.TargetOf(Pulse) < 0.5f
               && _music.TargetOf(Tension) < 0.5f
               && _music.TargetOf(Boss) < 0.5f;
    }

    private bool? StageIntensityAddsLayers(int tick)
    {
        _horde!.Pool.Clear();

        int atStart = Active();

        _director!.SetElapsedForTesting(_director.RunSeconds * 0.3f);
        int middle = Active();

        _director.SetElapsedForTesting(_director.RunSeconds * 0.85f);
        int late = Active();

        GD.Print($"  active layers: {atStart} at the start, {middle} at 30%, {late} at 85%");

        return middle > atStart && late > middle;
    }

    /// Intensity is the clock and the clock is not the whole story. Being swarmed
    /// at forty seconds should sound like it, and a quiet moment at four minutes
    /// should not sound like a fight that is not happening.
    private bool? StageCrowdRaisesTension(int tick)
    {
        _director!.SetElapsedForTesting(0.0f);
        _horde!.Pool.Clear();

        _music!.ForceDecideForTesting();
        float quiet = _music.TargetOf(Tension);

        Player? player = _scene?.GetNodeOrNull<Player>("Player");
        Vector3 at = player?.GlobalPosition ?? Vector3.Zero;

        for (int i = 0; i < 40; i++)
        {
            float angle = i * 0.61f;
            _horde.Spawn(at + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * (3.0f + i % 9), 0);
        }

        // Re-decided on the next _Process, which physics ticks do not run. Ask
        // for one directly rather than waiting a frame and hoping.
        _music.ForceDecideForTesting();
        float swarmed = _music.TargetOf(Tension);

        GD.Print($"  tension at 0% intensity: {quiet:F2} alone, {swarmed:F2} with 40 enemies close");

        return quiet < 0.2f && swarmed > 0.5f;
    }

    private bool? StageBossArrivesInSound(int tick)
    {
        // Guarded to the first tick. Without it the whole setup — including the
        // "before" reading — re-ran every tick until the wait expired, so
        // "before the boss" was captured four ticks after the boss had arrived
        // and the stage reported that the layer was already playing.
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _music!.ForceDecideForTesting();
            _beforeBoss = _music.TargetOf(Boss);

            // Through the director, so the layer follows the thing rather than a
            // flag a probe set. A boss layer wired to its own boolean would play
            // over an empty field the first time the spawn failed.
            _director!.SetElapsedForTesting(_director.RunSeconds * (_director.BossAt + 0.02f));
            _director.SetPhysicsProcess(true);
            return null;
        }

        if (tick < 5)
            return null;

        float before = _beforeBoss;

        _director!.SetPhysicsProcess(false);
        _music!.ForceDecideForTesting();
        float during = _music.TargetOf(Boss);

        int index = _horde!.FirstOfType(_director.BossType);
        if (index >= 0)
            _horde.Damage(index, 99999.0f, Vector2.Zero);

        _music.ForceDecideForTesting();
        float after = _music.TargetOf(Boss);

        GD.Print($"  boss layer: {before:F0} before, {during:F0} while it is alive, {after:F0} once it is down");

        return before < 0.5f && during > 0.5f && after < 0.5f;
    }

    private float _beforeBoss;

    /// The bug this design exists to avoid.
    ///
    /// `Intensity` is smooth, but the crowd count is not, and a single threshold
    /// with a value hovering on it turns a slow fade into a layer that breathes
    /// in and out once a second. Hysteresis means the layer comes in at one value
    /// and does not leave until well below it — so a value parked exactly on the
    /// rise point must stay wherever it already was.
    private bool? StageThresholdDoesNotFlicker(int tick)
    {
        _horde!.Pool.Clear();

        // Below the release point first, so the layer is definitely out.
        _director!.SetElapsedForTesting(_director.RunSeconds * (_director.RunSeconds > 0.0f ? 0.0f : 0.0f));
        _music!.ForceDecideForTesting();

        // Now park exactly on the rise threshold and stay there.
        _director.SetElapsedForTesting(_director.RunSeconds * _music.PulseAt);

        var seen = new System.Collections.Generic.HashSet<float>();
        for (int i = 0; i < 40; i++)
        {
                _music.ForceDecideForTesting();
            seen.Add(_music.TargetOf(Pulse));
        }

        GD.Print($"  forty decisions with intensity parked on the threshold: " +
                 $"{seen.Count} distinct target(s)");

        // One value, whichever it is. Two means the mix is chattering.
        return seen.Count == 1;
    }

    private bool? StageRunEndSilences(int tick)
    {
        _director!.EndForTesting(RunState.Extracted);
        _music!.ForceDecideForTesting();

        bool allDown = true;
        for (int i = 0; i < _music.LayerCount; i++)
            allDown &= _music.TargetOf(i) < 0.01f;

        // Still playing, just silent. Stopping them would put the layers out of
        // phase with each other if anything ever restarted one.
        bool stillPlaying = true;
        for (int i = 0; i < _music.LayerCount; i++)
            stillPlaying &= _music.Playing(i);

        GD.Print($"  after the run ended: every layer asked for silence: {allDown}, " +
                 $"streams still running: {stillPlaying}");

        return allDown && stillPlaying;
    }

    /// Reads the samples back and checks each layer is a sound.
    ///
    /// Everything above this stage would pass unchanged if a synthesis function
    /// returned an array of zeros: the loop would be the right length, it would
    /// play, and it would fade in and out on cue. Silence is the one failure the
    /// mixing logic cannot see, and it is exactly what a bad edit to a waveform
    /// produces.
    ///
    /// The second half separates a drone from a pulse. Both are non-silent; only
    /// one has an energy that rises and falls across the loop, and a pulse layer
    /// that came out as a held tone would be a mix with no sense of time in it.
    private bool? StageLayersHaveContent(int tick)
    {
        bool ok = true;
        var report = new System.Collections.Generic.List<string>();

        for (int i = 0; i < _music!.LayerCount; i++)
        {
            (float rms, float variation) = Analyse(i);
            report.Add($"{ClipNames[i]} rms {rms:F3} var {variation:F2}");

            if (rms < 0.005f)
            {
                GD.PushError($"  {ClipNames[i]} is silent");
                ok = false;
            }
        }

        (float _, float bedVariation) = Analyse(Bed);
        (float _, float pulseVariation) = Analyse(Pulse);

        GD.Print($"  {string.Join("   ", report)}");

        // The pulse's energy swings far more across the loop than the bed's. Both
        // are audible; only one is rhythmic.
        return ok && pulseVariation > bedVariation * 2.0f;
    }

    private static readonly string[] ClipNames =
        { "music_bed", "music_pulse", "music_tension", "music_boss" };

    /// Overall level, and how much it moves — the ratio of the loudest window to
    /// the mean window, which is high for something that hits and low for
    /// something that is held.
    private (float Rms, float Variation) Analyse(int layer)
    {
        var stream = GD.Load<AudioStreamWav>($"res://assets/audio/{ClipNames[layer]}.tres");
        if (stream == null)
            return (0.0f, 0.0f);

        byte[] data = stream.Data;
        int count = data.Length / 2;
        if (count == 0)
            return (0.0f, 0.0f);

        // 20 ms windows, which is short enough to see a beat and long enough not
        // to be measuring the waveform itself.
        int window = stream.MixRate / 50;
        double total = 0.0;
        double loudest = 0.0;
        double sumOfWindows = 0.0;
        int windows = 0;

        for (int start = 0; start + window <= count; start += window)
        {
            double energy = 0.0;
            for (int i = start; i < start + window; i++)
            {
                short sample = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                double v = sample / 32768.0;
                energy += v * v;
            }

            double rms = System.Math.Sqrt(energy / window);
            loudest = System.Math.Max(loudest, rms);
            sumOfWindows += rms;
            total += energy;
            windows++;
        }

        float overall = (float)System.Math.Sqrt(total / (windows * window));
        float mean = windows > 0 ? (float)(sumOfWindows / windows) : 0.0f;
        float variation = mean > 0.0001f ? (float)(loudest / mean) : 0.0f;

        return (overall, variation);
    }

    private int Active()
    {
        _music!.ForceDecideForTesting();

        int count = 0;
        for (int i = 0; i < _music.LayerCount; i++)
        {
            if (_music.TargetOf(i) > 0.5f)
                count++;
        }

        return count;
    }
}
