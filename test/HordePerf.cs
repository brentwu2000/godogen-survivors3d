using Godot;

/// Measures the horde under load: frame time, physics time and draw calls, with
/// the player moving so the flow field actually rebuilds.
///
///   godot --script test/HordePerf.cs -- 200
///   godot --script test/HordePerf.cs -- 500 mixed
///   godot --script test/HordePerf.cs -- 500 mixed fight
///
/// Not headless, and this is checked rather than said. **The comment below is
/// the comment this file always carried, and for one phase it was the only thing
/// enforcing it — so the documented intake command in `ART.md` was
/// `godot --headless --script test/HordePerf.cs`, which measures no rendering
/// whatsoever.** Under the dummy driver every triangle count on this machine
/// reported 6.90 ms and 145 fps to the decimal: 200 bodies at 460 triangles,
/// at 1,650, and at 23,822 — a factor of fifty-two — all identical, because
/// none of them were drawn. `avg draw calls 0` was printed underneath each one
/// and read as a MultiMesh triumph.
///
/// The same number is in `README`'s Performance section, and the whole tier
/// table in `ART.md` was reasoned from it. A perf probe that silently measures
/// nothing is worse than no perf probe, because its output is a number and
/// numbers get quoted.
///
/// Draw calls and frame time are the point. VSync is disabled, or every result
/// would read exactly 60 FPS regardless of headroom.
///
/// "mixed" fills the field from the late-run roster instead of walkers only. The
/// draw call count is the number that matters there: variants are layers of one
/// array, so a mixed horde has to cost the same one call a uniform one does.
///
/// **"fight" is the mode this file existed for eleven phases without having.**
/// Without it the instrument spawns five hundred bodies and measures a horde
/// standing still: nothing fires, nothing dies, nothing explodes and nothing
/// burns, so the puff pools are empty, the ground has no marks on it, the corpse
/// field is empty and the blast lights are off. Every effect system in the game
/// is therefore absent from the one measurement anybody quotes about the game's
/// cost — and each of them is bounded by a fixed pool, so the ceiling was
/// arithmetic rather than a number.
///
/// It fires the weapon every frame, detonates twice a second, sets a slice of the
/// field alight, and re-spawns whatever it killed so the body count under
/// measurement does not drain away. Hitstop is switched off: it does not change
/// how long a frame takes to draw, but an instrument that quietly runs the game
/// at a seventh speed is one more thing to have to explain about a number.
public partial class HordePerf : SceneTree
{
    /// Frames discarded before sampling starts.
    ///
    /// **One second was not enough and the symptom was a bimodal table.** Six
    /// runs alternating idle and fight came back at either ~1.35 ms or ~3.1 ms
    /// with no relation to which mode was running — a spread of 2.3x between two
    /// runs of the *same* command, which is far larger than anything either mode
    /// costs. That is a GPU that has not finished clocking up, and a number taken
    /// during it is a number about power management.
    ///
    /// `warmup:N` overrides it. The default stays at 60 so every row this file has
    /// already printed keeps meaning what it meant, and the longer figure is what
    /// a row worth quoting is taken at.
    private int _warmupFrames = 60;

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
    private bool _fight;

    private Player? _player;
    private WeaponHandler? _weapons;
    private EffectDirector? _effects;
    private CameraRig? _rig;

    /// Peak occupancy of each pool across the sample, so the row can say what was
    /// actually on screen rather than only how long it took. A busy-frame number
    /// taken with an empty effect pool would be the same defect as a rendering
    /// number taken headless, one layer up.
    private int _peakPuffs, _peakMarks, _peakCorpses, _kills, _killBase;

    public override void _Initialize()
    {
        if (!Display.Required(this, "HordePerf"))
            return;

        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length > 0 && int.TryParse(args[0], out int requested))
            _targetCount = requested;

        foreach (string arg in args)
        {
            _mixed |= arg == "mixed";
            _fight |= arg == "fight";

            if (arg.StartsWith("warmup:") && int.TryParse(arg[7..], out int warmup))
                _warmupFrames = Mathf.Max(0, warmup);

            // The densest biome is where the cover budget is actually spent, and
            // it is not the default — so the frame time recorded without this
            // flag is the frame time of the map with the least in it.
            if (arg.StartsWith("biome:") && int.TryParse(arg[6..], out int biome))
                GameSession.Biome = biome;
        }

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // A fixed layout, set before the scene enters the tree because the
        // generator runs in _Ready.
        //
        // This did not used to matter: every piece of cover was the same box with
        // the same material, so the arena contributed a constant to the draw call
        // count no matter what the seed did. Cover is now grouped into one
        // MultiMesh per kind, and which kinds a seed uses is a number this test
        // reports — so without a pinned seed the headline figure moves by a
        // couple every run, for reasons the measurement did not choose.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

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
            if (_fight)
            {
                _player = scene.GetNodeOrNull<Player>("Player");
                _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
                _effects = scene.GetNodeOrNull<EffectDirector>("Effects");
                _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");

                if (_weapons == null || _effects == null || _rig == null)
                {
                    GD.PushError("PERF FAILED — fight mode needs the weapon, the effects and the rig");
                    Quit(1);
                    return true;
                }

                // Off in an instrument. It does not change how long a frame takes
                // to draw, but a measurement taken with the game quietly running
                // at a seventh speed is one more thing to have to explain about a
                // number somebody is going to quote.
                _effects.Hitstop = false;

                // The player cannot die mid-sample: a dead player stops the
                // weapon, and half a sample of a corpse is not a busy frame.
                _player!.Heal(100000.0f);
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

        if (_fight)
            Fight();

        ulong now = Time.GetTicksUsec();
        if (_frame > _warmupFrames && _samples < SampleFrames)
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

        if (_fight)
        {
            GD.Print($"peak puffs      {_peakPuffs} of {_effects!.Effects.Capacity}");
            GD.Print($"peak marks      {_peakMarks} of {_effects.Marks.Capacity}");
            GD.Print($"peak corpses    {_peakCorpses} of {_horde!.Corpses.Capacity}");
            GD.Print($"kills sampled   {_kills}");
        }

        GD.Print("PERF DONE");

        Quit(0);
        return true;
    }

    /// Everything the idle measurement leaves out, driven every frame.
    ///
    /// The field is topped back up as it is killed. Without that the body count
    /// falls through the sample — a weapon that clears three a second takes a
    /// tenth of five hundred off the number under measurement — and the row would
    /// be a frame time for a horde that is smaller than the one it claims.
    private void Fight()
    {
        _weapons!.HoldFire = false;
        _weapons.ForceFire(CameraRig.Forward(_rig!.Yaw));

        // Twice a second, which is far more often than a run ever explodes and is
        // the point: this is a ceiling, not a typical frame.
        if (_frame % 30 == 0)
        {
            Vector3 at = _player!.GlobalPosition
                       + new Vector3(CameraRig.Forward(_rig.Yaw).X, 0.0f, CameraRig.Forward(_rig.Yaw).Y) * 6.0f;
            _horde.Detonate(at, 4.5f, 55.0f);
        }

        if (_frame % 120 == 0)
            _horde.Hazards.Add(_player!.GlobalPosition + new Vector3(3.0f, 0.0f, 3.0f), 3.5f, 22.0f, 7.0f);

        // A slice alight, so the burning-body pass has something to draw. Spread
        // by index rather than all at once, because forty at once and then none
        // is not what a molotov looks like.
        for (int i = _frame % 8; i < _horde.Pool.Count; i += 8)
            _horde.ApplyBurn(i, 1.0f, 1.5f);

        while (_horde.Pool.Count < _targetCount &&
               (_mixed ? _horde.SpawnByIntensity(RingPosition(_frame + _horde.Pool.Count))
                       : _horde.Spawn(RingPosition(_frame + _horde.Pool.Count))))
        {
        }

        if (_frame <= _warmupFrames || _samples >= SampleFrames)
            return;

        // Counted off the corpse field's running total, not off the pool.
        //
        // The first version differenced `Pool.Count` across the frame — and this
        // method tops the field back up in the same frame, so the difference was
        // always zero or negative and the row printed `kills sampled 0` under a
        // corpse count of seventeen. A number that disagrees with the line above
        // it is worse than no number.
        if (_killBase == 0)
            _killBase = _horde!.Corpses.TotalSpawned;
        _kills = _horde!.Corpses.TotalSpawned - _killBase;
        _peakPuffs = Mathf.Max(_peakPuffs, _effects!.Effects.Count);
        _peakMarks = Mathf.Max(_peakMarks, _effects.Marks.Count);
        _peakCorpses = Mathf.Max(_peakCorpses, _horde.Corpses.Count);
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
