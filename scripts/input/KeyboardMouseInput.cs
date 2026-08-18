using Godot;

/// Desktop input: WASD/arrows to move, mouse position to aim.
public sealed class KeyboardMouseInput : IInputSource
{
    private readonly Camera3D _camera;

    public Vector2 Move { get; private set; }
    public Vector2 Aim { get; private set; }
    public bool SecurePressed => Input.IsActionJustPressed("secure");
    public bool UsePressed => Input.IsActionJustPressed("use");
    public bool SwapPressed => Input.IsActionJustPressed("swap");
    public bool ThrowPressed => Input.IsActionJustPressed("throw");

    public KeyboardMouseInput(Camera3D camera)
    {
        _camera = camera;
    }

    public void Update(Vector3 playerPosition)
    {
        Move = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        Aim = AimFromMouse(playerPosition);
    }

    /// Projects the cursor onto the player's ground plane. Returns Zero when the
    /// ray runs parallel to the plane or the cursor sits on top of the player,
    /// so callers fall back to auto-targeting instead of snapping to a garbage
    /// direction.
    private Vector2 AimFromMouse(Vector3 playerPosition)
    {
        if (_camera == null || !_camera.IsInsideTree())
            return Vector2.Zero;

        Vector2 mouse = _camera.GetViewport().GetMousePosition();
        Vector3 origin = _camera.ProjectRayOrigin(mouse);
        Vector3 direction = _camera.ProjectRayNormal(mouse);

        if (Mathf.Abs(direction.Y) < 0.0001f)
            return Vector2.Zero;

        float distance = (playerPosition.Y - origin.Y) / direction.Y;
        if (distance < 0.0f)
            return Vector2.Zero;

        Vector3 hit = origin + direction * distance;
        var flat = new Vector2(hit.X - playerPosition.X, hit.Z - playerPosition.Z);
        return flat.LengthSquared() < 0.01f ? Vector2.Zero : flat.Normalized();
    }
}
