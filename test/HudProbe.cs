using Godot;

/// Checks that the readout is actually reading out.
///
///   godot --headless --script test/HudProbe.cs
///
/// Exit code is the verdict. This exists because Phase 12 was almost entirely
/// visual and therefore almost entirely unguarded: every bar, card and prompt was
/// verified by looking at a video once. A bar that stopped tracking its value
/// would keep rendering perfectly, at whatever width it was last given, and no
/// probe in the suite would notice.
///
/// It asserts geometry and visibility rather than colour, because a width is what
/// carries the meaning — a full-length bar in the wrong colour is a style
/// complaint, and a half-length bar on full health is a lie.
public partial class HudProbe : SceneTree
{
    private Node? _scene;
    private CanvasLayer? _hud;
    private Player? _player;
    private RunDirector? _director;
    private RunGrowth? _growth;
    private Horde? _horde;

    private int _stage;
    private int _stageTick;
    private bool _failed;

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
            _hud = _scene?.GetNodeOrNull<CanvasLayer>("Hud");
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");
            _growth = _scene?.GetNodeOrNull<RunGrowth>("RunGrowth");
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");

            if (_hud == null || _player == null || _director == null || _growth == null || _horde == null)
            {
                GD.PushError($"PROBE FAILED — hud={_hud != null} player={_player != null} " +
                             $"director={_director != null} growth={_growth != null} horde={_horde != null}");
                Quit(1);
                return true;
            }

            _director.SetPhysicsProcess(false);
            _player.GetNode<WeaponHandler>("WeaponHandler").SetPhysicsProcess(false);
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageBarsExist, "the readout is bars, and they are all there");
            case 1: return RunStage(StageHealthTracks, "the health bar is the health, at three widths");
            case 2: return RunStage(StageBagTracks, "the backpack bar is the backpack");
            case 3: return RunStage(StageCardsFollowTheOffer, "the level-up cards come and go with the offer");
            case 4: return RunStage(StageHoldBarClears, "the hold bar is gone once the run is over");
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

    private bool? StageBarsExist(int tick)
    {
        string[] bars = { "Health", "Bag", "Level", "Hold" };
        bool ok = true;

        foreach (string bar in bars)
        {
            if (_hud!.GetNodeOrNull<ColorRect>($"{bar}Back") == null
                || _hud.GetNodeOrNull<ColorRect>($"{bar}Fill") == null
                || _hud.GetNodeOrNull<Label>($"{bar}Text") == null)
            {
                GD.PushError($"  {bar} is missing a piece");
                ok = false;
            }
        }

        for (int i = 0; i < 3; i++)
        {
            if (_hud!.GetNodeOrNull<ColorRect>($"Card{i}") == null
                || _hud.GetNodeOrNull<Label>($"Card{i}Text") == null)
            {
                GD.PushError($"  Card{i} is missing a piece");
                ok = false;
            }
        }

        bool vignette = _hud!.GetNodeOrNull<ColorRect>("Vignette")?.Material is ShaderMaterial;
        GD.Print($"  four bars, three cards, vignette shader attached = {vignette}");
        return ok && vignette;
    }

    /// Three widths, because one proves nothing: a bar hard-coded to full length
    /// passes a full-health check, and a bar that never moves passes any single
    /// reading you happen to take.
    ///
    /// Never all the way to zero. Health reaching zero ends the run — which is
    /// correct, and which silently invalidated every stage after this one the
    /// first time this probe was written: the cards stopped being offered and the
    /// hold bar stopped being reachable, and both reported as HUD failures.
    private bool? StageHealthTracks(int tick)
    {
        ColorRect fill = _hud!.GetNode<ColorRect>("HealthFill");

        if (tick == 1)
        {
            _fullWidth = fill.Size.X;
            _player!.Heal(9999.0f);
            return null;
        }

        if (tick == 4)
        {
            _atFull = fill.Size.X;
            _player!.TakeDamage(_player.MaxHealth * 0.5f);
            return null;
        }

        if (tick == 8)
        {
            _atHalf = fill.Size.X;

            // Whittled down rather than set. Armour subtracts a flat amount from
            // anything incoming, so a single call for "all but two percent" lands
            // somewhere the probe did not choose — and one point too far ends the
            // run instead of testing the bar.
            //
            // `Armour + 1`, not 1. A one-point hit against armour is absorbed to
            // the twenty percent floor, so each call lands 0.2 and the loop needs
            // five times as many turns to get anywhere. It happens to fit in five
            // thousand today; it does not have to. Sending armour plus one lands
            // a point a call whatever the player is wearing, which is what the
            // loop is written as if it does.
            float target = _player!.MaxHealth * 0.02f;
            for (int i = 0; i < 5000 && _player.Health > target; i++)
                _player.TakeDamage(_player.Armour + 1.0f);

            return null;
        }

        if (tick < 12)
            return null;

        float atLow = fill.Size.X;
        bool full = Mathf.Abs(_atFull - _fullWidth) < 1.0f;
        bool half = Mathf.Abs(_atHalf - _fullWidth * 0.5f) < _fullWidth * 0.06f;

        // Nearly gone, but still drawn. A bar that snaps to zero early is one the
        // player reads as dead while they are still standing.
        bool low = atLow < _fullWidth * 0.05f && atLow > 0.5f;
        bool alive = _player!.IsAlive && _director!.State == RunState.Running;

        GD.Print($"  track {_fullWidth:F0}px: full {_atFull:F0} ({full}), " +
                 $"half {_atHalf:F0} ({half}), 2% {atLow:F0} ({low}); run still running = {alive}");

        _player.Heal(9999.0f);
        return full && half && low && alive;
    }

    private float _fullWidth, _atFull, _atHalf;

    private bool? StageBagTracks(int tick)
    {
        ColorRect fill = _hud!.GetNode<ColorRect>("BagFill");

        if (tick == 1)
        {
            _player!.Heal(9999.0f);
            _player.Backpack.Clear();
            return null;
        }

        if (tick == 4)
        {
            _bagEmpty = fill.Size.X;

            var scrap = GD.Load<ItemResource>("res://resources/items/scrap_metal.tres");
            if (scrap == null)
                return false;

            // Exactly half the capacity, in bulk. Bulk rather than count is the
            // whole point of this bar, so filling it by stack size would test the
            // wrong number.
            _player!.Backpack.TryAdd(scrap, _player.Backpack.Capacity / (2 * scrap.Bulk));
            return null;
        }

        if (tick < 8)
            return null;

        float half = fill.Size.X;
        Inventory bag = _player!.Backpack;
        float expected = _hud.GetNode<ColorRect>("BagBack").Size.X - 4.0f;
        expected *= bag.UsedBulk / (float)bag.Capacity;

        GD.Print($"  empty {_bagEmpty:F0}px, {bag.UsedBulk}/{bag.Capacity} bulk -> " +
                 $"{half:F0}px (expected {expected:F0})");

        return _bagEmpty < 1.0f && Mathf.Abs(half - expected) < 2.0f;
    }

    private float _bagEmpty;

    /// The offer is the only thing on screen the player must answer, and it does
    /// not pause anything — so a card that fails to appear costs them the pick,
    /// and one that fails to clear sits over the arena forever. The capture
    /// script demonstrated the second half by accident.
    private bool? StageCardsFollowTheOffer(int tick)
    {
        ColorRect card = _hud!.GetNode<ColorRect>("Card0");

        if (tick == 1)
        {
            _cardsBefore = card.Visible;

            // Earned the way the player earns it: kills, through the horde's own
            // event, rather than by poking RunGrowth's internals.
            for (int i = 0; i < 40 && !_growth!.HasOffer; i++)
            {
                _horde!.Spawn(_player!.GlobalPosition + new Vector3(12.0f, 0.0f, 12.0f), 0);
                while (_horde.Pool.Count > 0)
                    _horde.Damage(_horde.Pool.Count - 1, 9999.0f, Vector2.Zero);
            }

            return null;
        }

        if (tick == 6)
        {
            _cardsDuring = card.Visible;
            _offerText = _hud.GetNode<Label>("Card0Text").Text;
            _growth!.Choose(0);
            return null;
        }

        if (tick < 12)
            return null;

        bool cleared = !card.Visible || _growth!.HasOffer;
        GD.Print($"  cards before the offer = {_cardsBefore}, during = {_cardsDuring} " +
                 $"(\"{_offerText.Replace("\n", " ")}\"), cleared after choosing = {cleared}");

        return !_cardsBefore && _cardsDuring && cleared;
    }

    private bool _cardsBefore, _cardsDuring;
    private string _offerText = "";

    /// The guard on the bug only the proof video caught: a finished run left
    /// EXTRACTING pinned under the banner, which reads as a frozen interface.
    /// Nothing that judges by exit code could see it then, and this is that.
    private bool? StageHoldBarClears(int tick)
    {
        ExtractionZone? pad = _director!.PrimaryPad;
        if (pad == null)
            return false;

        if (tick == 1)
        {
            pad.Open = true;
            _player!.Heal(9999.0f);
            _player.GlobalPosition = pad.GlobalPosition;
            return null;
        }

        if (tick == 30)
        {
            _holdDuring = _hud!.GetNode<ColorRect>("HoldFill").Visible;
            return null;
        }

        if (_director.State == RunState.Running && tick < 60 * 12)
            return null;

        if (tick < 60 * 12 + 10 && _director.State == RunState.Running)
            return null;

        // A few frames past the end, so the readout has had a chance to update.
        if (_holdAfterAt == 0)
        {
            _holdAfterAt = tick + 10;
            return null;
        }

        if (tick < _holdAfterAt)
            return null;

        bool cleared = !_hud!.GetNode<ColorRect>("HoldFill").Visible;
        GD.Print($"  hold bar while extracting = {_holdDuring}, after the run ended ({_director.State}) = " +
                 $"{!cleared}");

        return _holdDuring && cleared;
    }

    private bool _holdDuring;
    private int _holdAfterAt;
}
