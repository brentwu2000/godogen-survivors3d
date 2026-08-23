using Godot;

/// Builds the touch controls and installs them as the player's input source.
///
/// Present in every build and hidden unless the device has a touchscreen, so
/// there is one scene rather than two — the thing the input abstraction was
/// written for in Phase 1 and then never used, because nothing had ever
/// instantiated a `VirtualStick`.
///
/// Layout: the stick owns the left half and the four decisions own the bottom
/// right. There is no aim stick; see `TouchStickInput` for why the second thumb
/// is worth more as four buttons.
public partial class TouchHud : CanvasLayer
{
    /// Forces the controls on where there is no touchscreen, for probes and
    /// screenshots. `--touch` on the command line does the same.
    [Export] public bool ForceOn { get; set; }

    [Export] public float ButtonRadius { get; set; } = 62.0f;

    private const float ScreenWidth = 1920.0f;
    private const float ScreenHeight = 1080.0f;

    private VirtualStick _stick = null!;
    private TouchButton[] _buttons = System.Array.Empty<TouchButton>();
    private Player? _player;
    private WeaponHandler? _weapons;

    public bool Active { get; private set; }

    /// Exposed so a probe can drive the same objects the player touches rather
    /// than a copy of them.
    public VirtualStick Stick => _stick;
    public TouchButton Button(TouchAction action) => _buttons[(int)action];

    public override void _Ready()
    {
        Active = ForceOn
                 || DisplayServer.IsTouchscreenAvailable()
                 || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "touch") >= 0;

        Build();
        Visible = Active;

        _player = GetParent()?.GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        if (!Active || _player == null)
            return;

        _player.SetInputSource(new TouchStickInput(_stick, _buttons));
    }

    private void Build()
    {
        // The stick takes the left half of the screen, not a small ring in the
        // corner: the origin is wherever the thumb lands, so the target is the
        // area a thumb can reach rather than a spot it has to find.
        _stick = new VirtualStick
        {
            Name = "MoveStick",
            Position = new Vector2(0.0f, ScreenHeight * 0.32f),
            Size = new Vector2(ScreenWidth * 0.45f, ScreenHeight * 0.68f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_stick);

        (TouchAction Action, string Glyph)[] layout =
        {
            (TouchAction.Secure, "F"),
            (TouchAction.Use, "Q"),
            (TouchAction.Throw, "G"),
            (TouchAction.Swap, "⇄"),
            (TouchAction.Drop, "↓"),
        };

        _buttons = new TouchButton[layout.Length];

        // An arc rather than a grid. The thumb pivots from the base of the hand,
        // so the reachable set is a curve — a 2x2 block puts one of the four
        // somewhere the thumb has to stretch for.
        var pivot = new Vector2(ScreenWidth - 120.0f, ScreenHeight - 120.0f);
        const float arc = 250.0f;

        for (int i = 0; i < layout.Length; i++)
        {
            float angle = Mathf.Pi + Mathf.Pi * 0.5f * (i / (float)(layout.Length - 1));
            var centre = pivot + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arc;

            var button = new TouchButton
            {
                Name = layout[i].Action.ToString(),
                Glyph = layout[i].Glyph,
                Radius = ButtonRadius,
                Position = centre - new Vector2(ButtonRadius, ButtonRadius),
            };

            AddChild(button);
            _buttons[(int)layout[i].Action] = button;
        }
    }

    /// Greys out what would do nothing. Four buttons that are always lit teach
    /// the player that tapping them is a coin flip; four that dim as the bag
    /// empties are a readout of what they are carrying.
    public override void _Process(double delta)
    {
        if (!Active || _player == null)
            return;

        _buttons[(int)TouchAction.Secure].Enabled = _player.Backpack.EntryCount > 0;
        _buttons[(int)TouchAction.Use].Enabled = _player.HasUsableItem;
        _buttons[(int)TouchAction.Throw].Enabled = _player.ThrowableCount > 0;
        _buttons[(int)TouchAction.Swap].Enabled = _weapons?.OtherSlotReady ?? false;

        // Only worth offering when the bag has something in it. A drop button on
        // an empty backpack is a button that does nothing, on a device where
        // every button costs a thumb.
        _buttons[(int)TouchAction.Drop].Enabled = _player.Backpack.EntryCount > 0;
    }
}
