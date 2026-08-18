using Godot;

/// Checks that every sound exists, carries signal, and cannot pile up.
///
///   godot --headless --script test/AudioProbe.cs
///
/// Exit code is the verdict. Nothing here can hear anything — headless runs on a
/// dummy audio driver — so what it asserts is everything about a sound that is
/// still true with the speakers off: that the samples are not silence, that a
/// one-shot ends on zero rather than on a step, that the loop meets itself, and
/// that a hundred kills in one frame cannot become a hundred voices.
///
/// Those are exactly the failures that are invisible while playing: a clip that
/// silently failed to generate plays nothing, and a missing end taper is a click
/// nobody can point at.
public partial class AudioProbe : SceneTree
{
    private const int ExpectedRate = 22050;

    private SoundDirector? _sound;
    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        // A bare director rather than the whole scene: the gate is a property of
        // this class alone, and dragging a level in would make the test depend on
        // a seed it has no reason to care about.
        var node = new Node { Name = "Sound" };
        Node live = SceneBuildUtil.AttachScriptToRoot(node, "res://scripts/nodes/SoundDirector.cs");
        GetRoot().AddChild(live);
        _sound = live as SoundDirector;
    }

    public override bool _PhysicsProcess(double delta)
    {
        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageEveryClipLoads, "every sound the game asks for exists");
            case 1: return RunStage(StageNotSilent, "no clip is silence with a filename");
            case 2: return RunStage(StageOneShotsEndClean, "one-shots end on zero, not on a step");
            case 3: return RunStage(StageAmbienceLoops, "the horde layer loops without a seam");
            case 4: return RunStage(StageGateHoldsRepeats, "one frame of kills is one death sound");
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

    /// Every value of the enum has to resolve to a file. A gap here is a sound
    /// that is simply never heard, which looks identical to one that is playing
    /// too quietly to notice.
    private bool? StageEveryClipLoads(int tick)
    {
        bool ok = true;

        foreach (Sfx id in System.Enum.GetValues<Sfx>())
        {
            AudioStreamWav? clip = Load(Name(id));
            if (clip == null)
            {
                GD.PushError($"  {Name(id)} did not load — run BuildAudio.cs");
                ok = false;
                continue;
            }

            if (clip.MixRate != ExpectedRate || clip.Stereo || clip.Format != AudioStreamWav.FormatEnum.Format16Bits)
            {
                GD.PushError($"  {Name(id)} is {clip.MixRate}Hz stereo={clip.Stereo} {clip.Format}");
                ok = false;
            }
        }

        GD.Print($"  {System.Enum.GetValues<Sfx>().Length} clips, plus the horde loop = {Load("horde") != null}");
        return ok && Load("horde") != null;
    }

    /// Peak, not average: a clip can be mostly quiet and still be a good sound,
    /// but one that never leaves the noise floor is a generation that failed.
    private bool? StageNotSilent(int tick)
    {
        bool ok = true;

        foreach (Sfx id in System.Enum.GetValues<Sfx>())
        {
            float peak = Peak(Load(Name(id)));
            if (peak < 0.35f)
            {
                GD.PushError($"  {Name(id)} peaks at {peak:F3} — effectively silence");
                ok = false;
            }
        }

        GD.Print($"  quietest clip peaks at {QuietestPeak():F2}");
        return ok;
    }

    /// A one-shot that stops mid-cycle ends on a discontinuity, and a
    /// discontinuity is a click — on every shot, several times a second.
    private bool? StageOneShotsEndClean(int tick)
    {
        bool ok = true;
        float worst = 0.0f;
        string worstName = "";

        foreach (Sfx id in System.Enum.GetValues<Sfx>())
        {
            float[] samples = Samples(Load(Name(id)));
            if (samples.Length == 0)
                continue;

            float tail = Mathf.Abs(samples[^1]);
            if (tail > worst)
            {
                worst = tail;
                worstName = Name(id);
            }

            if (tail > 0.02f)
            {
                GD.PushError($"  {Name(id)} ends at {tail:F3} — that is a click");
                ok = false;
            }
        }

        GD.Print($"  loudest final sample: {worstName} at {worst:F4}");
        return ok;
    }

    /// The seam is the whole risk with a loop. A click once every two seconds,
    /// under everything else, is the kind of defect that gets described as "the
    /// audio feels off" and never located.
    private bool? StageAmbienceLoops(int tick)
    {
        AudioStreamWav? loop = Load("horde");
        if (loop == null)
            return false;

        float[] samples = Samples(loop);
        float seam = Mathf.Abs(samples[0] - samples[^1]);

        // Against the clip's own step size rather than an absolute figure: a
        // quiet loop is allowed a quiet seam, and a loud one is not.
        float typicalStep = 0.0f;
        for (int i = 1; i < samples.Length; i++)
            typicalStep = Mathf.Max(typicalStep, Mathf.Abs(samples[i] - samples[i - 1]));

        bool flagged = loop.LoopMode == AudioStreamWav.LoopModeEnum.Forward
                       && loop.LoopBegin == 0
                       && loop.LoopEnd == samples.Length;

        GD.Print($"  loop flag {loop.LoopMode} [{loop.LoopBegin}..{loop.LoopEnd}] of {samples.Length}; " +
                 $"seam step {seam:F4} vs largest interior step {typicalStep:F4}");

        return flagged && seam <= typicalStep;
    }

    /// Two plays with no frame between them. The second must be refused — that
    /// refusal is the only thing standing between a late-run horde and a wall of
    /// forty overlapping death sounds.
    private bool? StageGateHoldsRepeats(int tick)
    {
        if (_sound == null)
            return false;

        bool first = _sound.Play(Sfx.EnemyDeath);
        bool second = _sound.Play(Sfx.EnemyDeath);
        bool other = _sound.Play(Sfx.Impact);

        // Gunfire deliberately has no gate — the weapon's own cooldown is the
        // rate limit, and gating it would silence a fast weapon's shots.
        bool shot = _sound.Play(Sfx.RifleShot);
        bool shotAgain = _sound.Play(Sfx.RifleShot);

        GD.Print($"  death {first}/{second} (second must be false), impact {other}, " +
                 $"ungated rifle {shot}/{shotAgain}");

        return first && !second && other && shot && shotAgain;
    }

    private static string Name(Sfx id) => id switch
    {
        Sfx.RifleShot => "fire_rifle",
        Sfx.MeleeSwing => "fire_melee",
        Sfx.BowShot => "fire_bow",
        Sfx.Impact => "impact",
        Sfx.EnemyDeath => "enemy_death",
        Sfx.Explosion => "explosion",
        Sfx.LootDone => "loot_done",
        Sfx.LevelUp => "level_up",
        Sfx.Hurt => "hurt",
        Sfx.Heartbeat => "heartbeat",
        Sfx.ExtractTick => "extract_tick",
        Sfx.Extracted => "extracted",
        Sfx.Dry => "dry",
        _ => id.ToString().ToLower(),
    };

    private static AudioStreamWav? Load(string name) =>
        GD.Load<AudioStreamWav>($"res://assets/audio/{name}.tres");

    private static float[] Samples(AudioStreamWav? clip)
    {
        if (clip == null)
            return System.Array.Empty<float>();

        byte[] data = clip.Data;
        var samples = new float[data.Length / 2];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8)) / 32768.0f;

        return samples;
    }

    private static float Peak(AudioStreamWav? clip)
    {
        float peak = 0.0f;
        foreach (float sample in Samples(clip))
            peak = Mathf.Max(peak, Mathf.Abs(sample));

        return peak;
    }

    private static float QuietestPeak()
    {
        float quietest = float.MaxValue;
        foreach (Sfx id in System.Enum.GetValues<Sfx>())
            quietest = Mathf.Min(quietest, Peak(Load(Name(id))));

        return quietest;
    }
}
