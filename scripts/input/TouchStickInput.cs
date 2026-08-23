using Godot;

/// Touch input: one stick to move, four buttons to decide.
///
/// **There is no aim stick.** Aiming with the right thumb was the original plan
/// and it costs the whole thumb, which is the entire touch budget for everything
/// that is not walking. It buys very little: firing is automatic, the weapon
/// already picks the nearest target in range, and the survivors-like contract
/// this game is built on is that the player steers and the weapon handles
/// itself. On a phone that thumb is worth more as four decisions than as an
/// override for a system that is right most of the time.
///
/// So the run's discrete actions — secure, use, throw, swap — get the right
/// side, and the level-up offer is answered by tapping the card it is already
/// drawing rather than by a fifth button nobody would find.
public sealed class TouchStickInput : IInputSource
{
    private readonly VirtualStick _moveStick;
    private readonly TouchButton[] _buttons;

    public Vector2 Move { get; private set; }

    /// Always zero: touch never overrides auto-targeting. Kept because the
    /// interface is the same one the desktop build uses, and the mouse does.
    public Vector2 Aim => Vector2.Zero;

    public bool SecurePressed { get; private set; }
    public bool UsePressed { get; private set; }
    public bool SwapPressed { get; private set; }
    public bool ThrowPressed { get; private set; }
    public bool DropPressed { get; private set; }

    /// Order matches TouchAction.
    public TouchStickInput(VirtualStick moveStick, TouchButton[] buttons)
    {
        _moveStick = moveStick;
        _buttons = buttons;
    }

    public void Update(Vector3 playerPosition)
    {
        Move = _moveStick.Value;

        // Consumed, not sampled. A button press lasts one frame for the same
        // reason `IsActionJustPressed` does — holding "use" down would empty the
        // backpack in half a second.
        SecurePressed = Take(TouchAction.Secure);
        UsePressed = Take(TouchAction.Use);
        SwapPressed = Take(TouchAction.Swap);
        ThrowPressed = Take(TouchAction.Throw);
        DropPressed = Take(TouchAction.Drop);
    }

    private bool Take(TouchAction action)
    {
        int index = (int)action;
        return index >= 0 && index < _buttons.Length && _buttons[index].ConsumePress();
    }
}

public enum TouchAction
{
    Secure,
    Use,
    Throw,
    Swap,

    /// Appended, never inserted. This enum indexes the button array the touch
    /// readout builds, so putting a new value in the middle silently renumbers
    /// every button after it — the player would press Use and get Throw.
    Drop,
}
