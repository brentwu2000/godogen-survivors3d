using Godot;

/// A floating on-screen stick. The knob origin is wherever the finger lands
/// inside this control's rect, not a fixed spot, so the player never has to look
/// down to find it.
///
/// Each stick owns one finger by index — that is what keeps it from stealing the
/// action buttons' input on a real touchscreen.
///
/// It draws itself only while a finger is on it. A permanent ring in the corner
/// is chrome the player stops seeing and the camera never stops rendering; a ring
/// that appears under the thumb is feedback.
public partial class VirtualStick : Control
{
    [Export] public float Radius { get; set; } = 110.0f;

    /// Deflection in -1..1 per axis, clamped to the unit circle.
    public Vector2 Value { get; private set; } = Vector2.Zero;

    public bool Active => _fingerIndex >= 0;

    private int _fingerIndex = -1;
    private Vector2 _origin;

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventScreenTouch touch when touch.Pressed && _fingerIndex < 0:
                Begin(touch.Index, touch.Position);
                break;

            case InputEventScreenTouch touch when !touch.Pressed && touch.Index == _fingerIndex:
                End();
                break;

            case InputEventScreenDrag drag when drag.Index == _fingerIndex:
                Drag(drag.Position);
                break;

            // The mouse path exists so the layout can be driven and photographed
            // on a desktop. A touch build never sees these.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                Begin(0, click.Position);
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                End();
                break;

            case InputEventMouseMotion motion when _fingerIndex >= 0:
                Drag(motion.Position);
                break;
        }
    }

    private void Begin(int finger, Vector2 at)
    {
        _fingerIndex = finger;
        _origin = at;
        Value = Vector2.Zero;
        QueueRedraw();
        AcceptEvent();
    }

    private void End()
    {
        _fingerIndex = -1;
        Value = Vector2.Zero;
        QueueRedraw();
        AcceptEvent();
    }

    private void Drag(Vector2 at)
    {
        Value = ((at - _origin) / Radius).LimitLength(1.0f);
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        if (_fingerIndex < 0)
            return;

        DrawArc(_origin, Radius, 0.0f, Mathf.Tau, 40, new Color(0.86f, 0.88f, 0.92f, 0.35f), 3.0f);
        DrawCircle(_origin + Value * Radius, 34.0f, new Color(0.90f, 0.92f, 0.96f, 0.45f));
    }
}
