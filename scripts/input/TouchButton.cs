using Godot;

/// A thumb-sized round button that latches one press until somebody reads it.
///
/// Latching rather than exposing "is held" is what makes it behave like
/// `Input.IsActionJustPressed` on the keyboard path — the game reads input once
/// per physics tick, and a held finger must not spend the whole backpack.
///
/// Drawn rather than themed. It is a circle, a label and a dimmed state; a
/// StyleBox and a theme would be three resources to serialise for something that
/// is nine lines of `_Draw`.
public partial class TouchButton : Control
{
    [Export] public string Glyph { get; set; } = "";
    [Export] public float Radius { get; set; } = 62.0f;

    /// Greyed out and unpressable. The four actions are only sometimes available
    /// — nothing to throw, no second weapon — and a button that does nothing when
    /// tapped is worse than one that says so.
    public bool Enabled { get; set; } = true;

    private bool _latched;
    private int _fingerIndex = -1;

    public override void _Ready()
    {
        // The rect is the touch target; the circle is only what it looks like.
        // A radius-sized square is easier to hit than the inscribed circle, which
        // is the right trade on a device where the finger is bigger than the art.
        CustomMinimumSize = new Vector2(Radius * 2.0f, Radius * 2.0f);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Enabled)
            return;

        switch (@event)
        {
            case InputEventScreenTouch touch when touch.Pressed && _fingerIndex < 0:
                _fingerIndex = touch.Index;
                _latched = true;
                QueueRedraw();
                AcceptEvent();
                break;

            case InputEventScreenTouch touch when !touch.Pressed && touch.Index == _fingerIndex:
                _fingerIndex = -1;
                QueueRedraw();
                AcceptEvent();
                break;

            // The mouse path exists so the layout can be driven and photographed
            // on a desktop. A touch build never sees these.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                _latched = true;
                _fingerIndex = 0;
                QueueRedraw();
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                _fingerIndex = -1;
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    /// True once per press. Reading clears it.
    public bool ConsumePress()
    {
        bool pressed = _latched;
        _latched = false;
        return pressed;
    }

    public bool Held => _fingerIndex >= 0;

    public override void _Draw()
    {
        var centre = new Vector2(Radius, Radius);
        float alpha = Enabled ? 1.0f : 0.35f;

        DrawCircle(centre, Radius, new Color(0.05f, 0.06f, 0.08f, 0.55f * alpha));
        DrawArc(centre, Radius - 3.0f, 0.0f, Mathf.Tau, 32,
                new Color(0.86f, 0.88f, 0.92f, (Held ? 0.95f : 0.55f) * alpha), 3.0f);

        if (Glyph.Length == 0)
            return;

        Font font = ThemeDB.FallbackFont;
        const int size = 26;
        Vector2 extent = font.GetStringSize(Glyph, HorizontalAlignment.Left, -1, size);
        font.DrawString(GetCanvasItem(), centre + new Vector2(-extent.X * 0.5f, extent.Y * 0.32f),
                        Glyph, HorizontalAlignment.Left, -1, size,
                        new Color(0.96f, 0.95f, 0.90f, alpha));
    }
}
