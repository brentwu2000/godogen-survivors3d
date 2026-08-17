using Godot;

/// A floating on-screen stick. The knob origin is wherever the finger lands
/// inside this control's rect, not a fixed spot, so the player never has to look
/// down to find it.
///
/// Each stick owns one finger by index — that is what keeps the movement stick
/// and the aim stick from stealing each other's input on a real touchscreen.
public partial class VirtualStick : Control
{
    [Export] public float Radius { get; set; } = 96.0f;

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
                _fingerIndex = touch.Index;
                _origin = touch.Position;
                Value = Vector2.Zero;
                AcceptEvent();
                break;

            case InputEventScreenTouch touch when !touch.Pressed && touch.Index == _fingerIndex:
                _fingerIndex = -1;
                Value = Vector2.Zero;
                AcceptEvent();
                break;

            case InputEventScreenDrag drag when drag.Index == _fingerIndex:
                Value = ((drag.Position - _origin) / Radius).LimitLength(1.0f);
                AcceptEvent();
                break;
        }
    }
}
