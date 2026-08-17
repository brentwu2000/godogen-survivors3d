using Godot;

/// Touch input: left stick moves, right stick aims and fires.
///
/// Holding the aim stick off-center is the fire input — a separate fire button
/// would need a third finger, which is one more than a phone comfortably gives.
public sealed class TouchStickInput : IInputSource
{
    private const float FireDeadzone = 0.35f;

    private readonly VirtualStick _moveStick;
    private readonly VirtualStick _aimStick;

    public Vector2 Move { get; private set; }
    public Vector2 Aim { get; private set; }
    public bool FireHeld { get; private set; }

    // Reserved for the HUD buttons that land with the loot UI; touch has no
    // keyboard fallback for these.
    public bool InteractPressed { get; private set; }
    public bool ReloadPressed { get; private set; }
    public bool SecurePressed { get; private set; }
    public bool UsePressed { get; private set; }
    public bool SwapPressed { get; private set; }
    public bool ThrowPressed { get; private set; }

    public TouchStickInput(VirtualStick moveStick, VirtualStick aimStick)
    {
        _moveStick = moveStick;
        _aimStick = aimStick;
    }

    public void Update(Vector3 playerPosition)
    {
        Move = _moveStick.Value;

        Vector2 aim = _aimStick.Value;
        FireHeld = aim.Length() >= FireDeadzone;
        Aim = FireHeld ? aim.Normalized() : Vector2.Zero;
    }
}
