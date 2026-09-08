using Godot;

/// Desktop input: WASD/arrows to move. The mouse turns the camera and does not
/// aim.
///
/// **`Aim` is `Zero` and the projection that used to fill it is gone.** It cast
/// the cursor onto the player's ground plane, which is the right idea under a
/// camera that cannot turn and wrong under one the mouse drives — `CameraRig`
/// says so in as many words: the world sweeps beneath a stationary cursor as the
/// view comes round, so the player spins while the hand holding the mouse is
/// still. With the cursor captured it is worse than wrong, because the projection
/// is of a pointer parked wherever it was grabbed.
///
/// It survived because its only consumer, `Player.UpdateFacing`, returned early
/// before reaching it while `TurnToSteer` was on. Switching to strafing made that
/// branch live and the survivor started facing a stale cursor — 168° off her
/// direction of travel, measured by `GaitShot`, with the weapon firing there
/// because `Facing` is what the weapon reads.
///
/// The field stays on the interface. A right stick or a touch aim pad is a real
/// aim device and this is where one would arrive; a mouse position under
/// mouse-look is not one.
public sealed class KeyboardMouseInput : IInputSource
{
    private readonly Camera3D _camera;

    public Vector2 Move { get; private set; }

    /// No aim device on this platform. See the note above.
    public Vector2 Aim => Vector2.Zero;
    public bool SecurePressed => Input.IsActionJustPressed("secure");
    public bool UsePressed => Input.IsActionJustPressed("use");
    public bool SwapPressed => Input.IsActionJustPressed("swap");
    public bool ThrowPressed => Input.IsActionJustPressed("throw");
    public bool DropPressed => Input.IsActionJustPressed("drop");

    public KeyboardMouseInput(Camera3D camera)
    {
        _camera = camera;
    }

    public void Update(Vector3 playerPosition)
    {
        Move = Input.GetVector("move_left", "move_right", "move_up", "move_down");
    }
}
