using Godot;

/// Checks that the game can be played with two thumbs.
///
///   godot --headless --script test/TouchProbe.cs
///
/// Exit code is the verdict. This exists because the touch layer was written in
/// Phase 1 and **never once executed**: nothing had ever instantiated a
/// `VirtualStick`, `TouchStickInput` was never constructed, `SetInputSource` was
/// never called, and six of the actions it exposed were hardcoded to false. It
/// compiled for sixteen phases.
///
/// Events are pushed through the viewport rather than handed to `_GuiInput`
/// directly, because half of what can be wrong here is layout: a control the
/// finger never reaches, a `MouseFilter` that swallows the touch, a rect off the
/// bottom of the screen. Calling the handler would skip exactly those.
public partial class TouchProbe : SceneTree
{
    private Node? _scene;
    private TouchHud? _touch;
    private Player? _player;
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

        // Forced on: there is no touchscreen on the machine this runs on, and the
        // point is to exercise the layer that only appears when there is one.
        var touch = scene.GetNodeOrNull<TouchHud>("TouchHud");
        if (touch != null)
            touch.ForceOn = true;

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
            _touch = _scene?.GetNodeOrNull<TouchHud>("TouchHud");
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _growth = _scene?.GetNodeOrNull<RunGrowth>("RunGrowth");
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");

            if (_touch == null || _player == null || _growth == null || _horde == null)
            {
                GD.PushError($"PROBE FAILED — touch={_touch != null} player={_player != null} " +
                             $"growth={_growth != null} horde={_horde != null}");
                Quit(1);
                return true;
            }

            _scene?.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageInstalled, "the touch layer is actually wired to the player");
            case 1: return RunStage(StageStickMoves, "a drag on the stick moves the player");
            case 2: return RunStage(StageButtonLatches, "a button fires once per press, not once per frame");
            case 3: return RunStage(StageDisabledIsDead, "a button with nothing to do cannot be pressed");
            case 4: return RunStage(StageCardTakesPick, "the level-up card can be answered by tapping it");
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

    /// The failure this catches is the one the whole layer shipped with for
    /// sixteen phases: everything present, nothing connected.
    private bool? StageInstalled(int tick)
    {
        bool active = _touch!.Active;
        bool visible = _touch.Visible;
        bool source = _player!.InputSourceName == nameof(TouchStickInput);

        GD.Print($"  active={active} visible={visible} player input source = {_player.InputSourceName}");
        return active && visible && source;
    }

    /// End to end: a synthetic finger on the stick has to come out as movement,
    /// through the same interface the keyboard uses.
    private bool? StageStickMoves(int tick)
    {
        VirtualStick stick = _touch!.Stick;
        Vector2 origin = stick.GlobalPosition + stick.Size * 0.5f;

        if (tick == 1)
        {
            _startPosition = _player!.GlobalPosition;

            // Printed always, not only on failure: half of what can be wrong with
            // a touch layout is that the control is not where the finger is, and
            // that is invisible from a boolean.
            GD.Print($"  viewport {GetRoot().GetVisibleRect().Size}, stick rect {stick.GetGlobalRect()}, " +
                     $"touching at {origin}, inside = {stick.GetGlobalRect().HasPoint(origin)}, " +
                     $"filter {stick.MouseFilter}");

            Touch(origin, index: 0, pressed: true);
            return null;
        }

        if (tick == 2)
        {
            // Straight right, past the radius, so the stick clamps to full
            // deflection rather than leaving the result dependent on the drag
            // distance the probe happened to pick.
            Drag(origin + new Vector2(stick.Radius * 1.5f, 0.0f), index: 0);
            return null;
        }

        if (tick < 30)
            return null;

        Vector3 moved = _player!.GlobalPosition - _startPosition;
        Touch(origin, index: 0, pressed: false);

        bool deflected = Mathf.IsEqualApprox(stick.Value.X, 1.0f) && Mathf.Abs(stick.Value.Y) < 0.01f;
        bool wentRight = moved.X > 1.0f && Mathf.Abs(moved.Z) < 0.5f;

        GD.Print($"  stick value {stick.Value} (clamped = {deflected}); " +
                 $"player moved {moved.X:F2}m x, {moved.Z:F2}m z");

        return deflected && wentRight;
    }

    private Vector3 _startPosition;

    /// Held for many frames, read once. A button that reported "held" would empty
    /// the backpack in half a second, which is the same reason the keyboard path
    /// uses IsActionJustPressed rather than IsActionPressed.
    private bool? StageButtonLatches(int tick)
    {
        TouchButton button = _touch!.Button(TouchAction.Secure);
        Vector2 centre = button.GlobalPosition + button.Size * 0.5f;

        if (tick == 1)
        {
            // Something to secure, or the button is disabled and the press is
            // correctly ignored — which would make this stage pass for the
            // wrong reason.
            var scrap = GD.Load<ItemResource>("res://resources/items/scrap_metal.tres");
            if (scrap == null)
                return false;

            _player!.Backpack.Clear();
            _player.Backpack.TryAdd(scrap, 4);
            return null;
        }

        if (tick == 3)
        {
            Touch(centre, index: 1, pressed: true);
            return null;
        }

        if (tick < 12)
            return null;

        // Several physics ticks have passed with the finger down. Exactly one of
        // them should have secured anything.
        int secured = _player!.SafeBox.EntryCount;
        Touch(centre, index: 1, pressed: false);

        GD.Print($"  held for {tick - 3} ticks; safe box entries = {secured}, " +
                 $"bag entries = {_player.Backpack.EntryCount}");

        return secured == 1;
    }

    private bool? StageDisabledIsDead(int tick)
    {
        TouchButton button = _touch!.Button(TouchAction.Throw);
        Vector2 centre = button.GlobalPosition + button.Size * 0.5f;

        if (tick == 1)
        {
            _player!.Backpack.Clear();   // nothing throwable
            return null;
        }

        if (tick == 3)
        {
            Touch(centre, index: 2, pressed: true);
            return null;
        }

        if (tick < 8)
            return null;

        bool enabled = button.Enabled;
        bool latched = button.ConsumePress();
        Touch(centre, index: 2, pressed: false);

        GD.Print($"  nothing to throw: button enabled = {enabled}, press registered = {latched}");
        return !enabled && !latched;
    }

    /// The one action in a run that never reached the input abstraction:
    /// RunGrowth polls pick_1/2/3 straight off the keyboard, so on a phone the
    /// offer could not be answered at all and the cards would sit there forever.
    private bool? StageCardTakesPick(int tick)
    {
        var hud = _scene!.GetNode<CanvasLayer>("Hud");
        var card = hud.GetNode<ColorRect>("Card0");

        if (tick == 1)
        {
            // Earned the way a player earns it, through the horde's own event.
            for (int i = 0; i < 60 && !_growth!.HasOffer; i++)
            {
                _horde!.Spawn(_player!.GlobalPosition + new Vector3(14.0f, 0.0f, 14.0f), 0);
                while (_horde.Pool.Count > 0)
                    _horde.Damage(_horde.Pool.Count - 1, 9999.0f, Vector2.Zero);
            }

            return null;
        }

        if (tick == 4)
        {
            _hadOffer = _growth!.HasOffer && card.Visible;
            _picksBefore = _growth.PendingPicks;
            GD.Print($"  card rect {card.GetGlobalRect()} visible={card.Visible} filter={card.MouseFilter}; " +
                     $"tapping {card.GlobalPosition + card.Size * 0.5f}");
            Touch(card.GlobalPosition + card.Size * 0.5f, index: 3, pressed: true);
            return null;
        }

        if (tick < 10)
            return null;

        // Picks taken, not "the cards went away". Sixty kills earns several
        // levels at once, so answering one offer immediately puts the next one up
        // — which made the obvious assertion report a working feature as broken.
        int after = _growth!.PendingPicks;
        GD.Print($"  offer showing before the tap = {_hadOffer}; " +
                 $"pending picks {_picksBefore} -> {after}");

        return _hadOffer && after == _picksBefore - 1;
    }

    private bool _hadOffer;
    private int _picksBefore;

    // ---- synthetic fingers ---------------------------------------------------

    /// Fed to the input singleton, not handed to the control.
    ///
    /// Layout is half of what can be wrong with a touch UI — a control the finger
    /// never reaches, a filter that swallows the press, a rect off the bottom of
    /// the screen — and calling  directly would skip exactly those.
    ///
    ///  rather than : the latter does not
    /// run the GUI dispatch here, so a touch lands nowhere and every stage fails
    /// with the control sitting correctly under the finger. The events arrive on
    /// the following frame, which is why each stage leaves a tick between
    /// pressing and reading.
    private static void Touch(Vector2 at, int index, bool pressed) =>
        Input.ParseInputEvent(new InputEventScreenTouch { Index = index, Position = at, Pressed = pressed });

    private static void Drag(Vector2 at, int index) =>
        Input.ParseInputEvent(new InputEventScreenDrag { Index = index, Position = at });
}
