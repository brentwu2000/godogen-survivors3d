using Godot;

/// Root of the main scene, and the one thing that owns the mouse.
///
/// It began as a liveness marker so the build gate proved the C# assembly
/// actually loads at runtime rather than only that it compiled, and that print
/// is still the first line of every capture in this repository.
public partial class GameRoot : Node3D
{
    /// Whether the mouse is captured, which is the same question as whether the
    /// mouse is steering the camera.
    ///
    /// **Captured has to be a state somebody owns, and it has to be releasable.**
    /// A game that grabs the cursor and has no way to give it back is a game the
    /// player closes with the task manager, and this one runs in a window on a
    /// machine with an editor open behind it. `[Esc]` releases; a click inside the
    /// window takes it again. That is the convention every third-person game on
    /// this platform uses, and a convention is worth more here than a better idea
    /// would be.
    private static bool Captured => Input.MouseMode == Input.MouseModeEnum.Captured;

    public override void _Ready()
    {
        GD.Print($"GameRoot ready — physics tick {Engine.PhysicsTicksPerSecond} Hz");

        // Not in a headless or scripted run. Every probe in `test/` drives this
        // scene through `Input.ActionPress`, and capturing the cursor from a
        // `--script` process warps the developer's pointer to the middle of the
        // screen with nothing on screen to explain why. `DisplayServer` is the
        // dummy one headless, and a captured mouse there is meaningless anyway.
        if (DisplayServer.GetName() != "headless")
            Grab();
    }

    /// Takes the mouse, which starts the camera following it.
    private static void Grab() => Input.MouseMode = Input.MouseModeEnum.Captured;

    /// Gives it back, and leaves it visible where the window is.
    private static void Release() => Input.MouseMode = Input.MouseModeEnum.Visible;

    public override void _UnhandledInput(InputEvent @event)
    {
        // `ui_cancel` rather than a scancode, so a rebind moves with it.
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (Captured)
                Release();

            return;
        }

        // A click takes it back. Unhandled, so a click that some Control consumed
        // — a button on the HUD, a menu — does not silently recapture the cursor
        // the player just asked for.
        if (@event is InputEventMouseButton { Pressed: true } && !Captured)
            Grab();
    }

    /// Hands the mouse back on the way out.
    ///
    /// A scene change out of a run — the debrief, the base — leaves the cursor
    /// captured otherwise, and the next screen is a menu. `_ExitTree` rather than
    /// a call from whatever changes the scene, because there are three of those
    /// and this way none of them has to remember.
    public override void _ExitTree()
    {
        if (Captured)
            Release();
    }
}
