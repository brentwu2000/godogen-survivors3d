using Godot;

/// Photographs the end-of-run report.
///
///   godot --script test/DebriefShot.cs
///
/// Not headless — the null rendering driver has nothing to capture. Its own
/// script because it has to play a small run first: the screen is composed
/// entirely from what happened, so there is nothing to photograph until
/// something has.
///
/// Everything it stages is compressed for the camera, exactly like the proof
/// video: a real run reaches these numbers over five minutes, and a report with
/// one crate and two kills on it does not show whether the layout works.
public partial class DebriefShot : SceneTree
{
    private const string OutputPath = "res://screenshots/debrief.png";

    private Player _player = null!;
    private Horde _horde = null!;
    private RunDirector _director = null!;
    private MetaManager _meta = null!;
    private DebriefScreen _debrief = null!;
    private RunLog _log = null!;
    private Node _scene = null!;

    private int _frame;
    private int _captureAt = int.MaxValue;

    public override void _Initialize()
    {
        // A comment saying "not headless" is not a check. See test/Display.cs:
        // without one, running this headless does not fail — it spins a core
        // forever, silently, and looks from outside exactly like a slow test.
        if (!Display.Required(this, "DebriefShot"))
            return;

        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // A capture does not spend the player's save, and does not get swapped
        // out from under itself when the run ends.
        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0xC17E4A9BUL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _Process(double delta)
    {
        if (_frame++ == 0)
        {
            _player = _scene.GetNode<Player>("Player");
            _horde = _scene.GetNode<Horde>("Horde");
            _director = _scene.GetNode<RunDirector>("RunDirector");
            _meta = _scene.GetNode<MetaManager>("MetaManager");
            _debrief = _scene.GetNode<DebriefScreen>("Debrief");
            _log = _scene.GetNode<RunLog>("RunLog");

            Stage();
            return false;
        }

        if (_frame == 3)
        {
            // Ended by hand rather than by walking to the pad: this is a picture
            // of the report, and five seconds of standing on a plate is not part
            // of it.
            _director.CallDeferred("emit_signal", RunDirector.SignalName.RunEnded,
                                   (int)RunState.Extracted, Banked());
            _captureAt = _frame + 6;
            return false;
        }

        if (_frame < _captureAt)
            return false;

        _debrief.Show(_meta, _log);

        // One more frame, so the labels the Show call just set have been drawn.
        if (_frame == _captureAt)
            return false;

        Image image = GetRoot().GetTexture().GetImage();
        Error err = image.SavePng(ProjectSettings.GlobalizePath(OutputPath));
        GD.Print(err == Error.Ok ? $"Wrote {OutputPath}" : $"SavePng failed: {err}");
        return true;
    }

    /// A run worth reporting: a full roster killed, crates emptied, a contract
    /// taken, items spent, and a bag worth carrying out.
    private void Stage()
    {
        _meta.Profile.ContractSeed = 4242;
        _meta.Profile.ContractIndex = 0;
        _meta.Profile.Credits = 1840;
        _meta.Profile.Streak = 2;

        _horde.Pool.Clear();
        _horde.SpawnIntensity = 1.0f;

        // Killed through the real path so the log counts them the way it would in
        // a run — and re-reading the count each time, because a bloater's death
        // blast removes whatever is standing near it.
        for (int type = 0; type < _horde.Types.Length; type++)
        {
            int wanted = 30 + type * 7;
            for (int i = 0; i < wanted; i++)
            {
                _horde.Spawn(_player.GlobalPosition + new Vector3(25.0f + i * 0.1f, 0.0f, 25.0f), type);
                while (_horde.Pool.Count > 0)
                    _horde.Damage(_horde.Pool.Count - 1, 9999.0f, Vector2.Zero);
            }
        }

        foreach (string name in new[] { "medkit", "adrenaline_shot", "pipe_bomb" })
        {
            var item = GD.Load<ItemResource>($"res://resources/items/{name}.tres");
            if (item != null)
                _player.Backpack.TryAdd(item, 1);
        }

        _player.TakeDamage(_player.MaxHealth * 0.7f);
        _player.TryUseBest();
        _player.TryThrow();

        var serum = GD.Load<ItemResource>("res://resources/items/antiviral_serum.tres");
        var board = GD.Load<ItemResource>("res://resources/items/circuit_board.tres");
        if (serum != null)
            _player.Backpack.TryAdd(serum, 1);
        if (board != null)
            _player.Backpack.TryAdd(board, 2);

        _player.TrySecureBest();

        // Crates report through their own signal, which the log is already
        // listening to; standing on three of them would take nine seconds of
        // camera time to produce one line of text.
        var crates = _scene.GetNodeOrNull("LootContainers");
        if (crates != null)
        {
            int emptied = 0;
            foreach (Node child in crates.GetChildren())
            {
                if (child is LootContainer crate && emptied < 4)
                {
                    crate.EmitSignal(LootContainer.SignalName.Emptied, 90 + emptied * 40);
                    emptied++;
                }
            }
        }
    }

    private int Banked() =>
        Mathf.RoundToInt((_player.Backpack.TotalValue + _player.SafeBox.TotalValue)
                         * _director.ExtractionMultiplier);
}
