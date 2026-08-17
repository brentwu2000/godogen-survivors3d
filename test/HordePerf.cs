using Godot;

/// Measures the horde under load: frame time, physics time and draw calls, with
/// the player moving so the flow field actually rebuilds.
///
///   godot --script test/HordePerf.cs -- 200
///   godot --script test/HordePerf.cs -- 500 mixed
///
/// Not headless — draw calls and frame time are the point. VSync is disabled, or
/// every result would read exactly 60 FPS regardless of headroom.
///
/// "mixed" fills the field from the late-run roster instead of walkers only. The
/// draw call count is the number that matters there: variants are layers of one
/// array, so a mixed horde has to cost the same one call a uniform one does.
public partial class HordePerf : SceneTree
{
    private const int WarmupFrames = 60;
    private const int SampleFrames = 240;

    private Horde? _horde;
    private int _frame;
    private int _samples;

    // Wall clock, not Performance.Monitor.TimeFps / TimeProcess: under a --script
    // SceneTree those monitors return a frozen value (an unchanging 1.0 FPS),
    // which reads as a catastrophic result rather than as no result at all.
    private readonly double[] _frameMs = new double[SampleFrames];
    private ulong _lastTick;
    private double _drawCallSum;
    private int _gc0, _gc1, _gc2;

    private int _targetCount = 200;
    private bool _mixed;

    public override void _Initialize()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length > 0 && int.TryParse(args[0], out int requested))
            _targetCount = requested;

        foreach (string arg in args)
            _mixed |= arg == "mixed";

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        GetRoot().AddChild(scene);
    }

    public override bool _Process(double delta)
    {
        _frame++;

        if (_frame == 1)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            if (_horde == null)
            {
                GD.PushError("PERF FAILED — no Horde node");
                Quit(1);
                return true;
            }

            // Top up past whatever the scene spawned on its own.
            _horde.SpawnIntensity = _mixed ? 1.0f : 0.0f;
            int added = 0;
            while (_horde.Pool.Count < _targetCount &&
                   (_mixed ? _horde.SpawnByIntensity(RingPosition(added)) : _horde.Spawn(RingPosition(added))))
            {
                added++;
            }

            GD.Print($"enemies: {_horde.Pool.Count} (requested {_targetCount}, " +
                     $"{(_mixed ? "mixed roster" : "walkers only")})");

            if (_mixed)
            {
                var byType = new int[_horde.Types.Length];
                for (int i = 0; i < _horde.Pool.Count; i++)
                    byType[_horde.Pool.Type[i]]++;
                GD.Print($"composition: {string.Join('/', byType)}");
            }
            Input.ActionPress("move_right");
            _gc0 = System.GC.CollectionCount(0);
            _gc1 = System.GC.CollectionCount(1);
            _gc2 = System.GC.CollectionCount(2);
            return false;
        }

        // Swing the player around so the field is rebuilt against a moving target
        // rather than measuring a stationary best case.
        if (_frame % 45 == 0)
        {
            Input.ActionRelease("move_right");
            Input.ActionRelease("move_left");
            Input.ActionPress((_frame / 45) % 2 == 0 ? "move_right" : "move_left");
        }

        ulong now = Time.GetTicksUsec();
        if (_frame > WarmupFrames && _samples < SampleFrames)
        {
            _frameMs[_samples++] = (now - _lastTick) / 1000.0;
            _drawCallSum += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        }
        _lastTick = now;

        if (_samples < SampleFrames)
            return false;

        int worstIndex = 0;
        for (int i = 1; i < _samples; i++)
        {
            if (_frameMs[i] > _frameMs[worstIndex])
                worstIndex = i;
        }
        GD.Print($"worst frame at sample {worstIndex} of {_samples}");
        GD.Print($"GC collections during sampling  gen0={System.GC.CollectionCount(0) - _gc0} " +
                 $"gen1={System.GC.CollectionCount(1) - _gc1} gen2={System.GC.CollectionCount(2) - _gc2}");

        System.Array.Sort(_frameMs);
        double sum = 0.0;
        foreach (double ms in _frameMs)
            sum += ms;

        double mean = sum / _samples;
        double median = _frameMs[_samples / 2];
        double p95 = _frameMs[(int)(_samples * 0.95)];
        double worst = _frameMs[_samples - 1];

        GD.Print($"frame mean      {mean:F2} ms  ({1000.0 / mean:F0} fps)");
        GD.Print($"frame median    {median:F2} ms  ({1000.0 / median:F0} fps)");
        GD.Print($"frame p95       {p95:F2} ms");
        GD.Print($"frame worst     {worst:F2} ms");
        GD.Print($"avg draw calls  {_drawCallSum / _samples:F0}");
        GD.Print("PERF DONE");

        Quit(0);
        return true;
    }

    /// Spread the extra spawns over a ring rather than stacking them, so
    /// separation is not fighting a single pile on the first tick.
    private static Vector3 RingPosition(int index)
    {
        float angle = index * 2.399963f; // golden angle, keeps successive points apart
        float radius = 14.0f + (index % 40) * 0.6f;
        return new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
    }
}
