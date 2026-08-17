using Godot;

/// Holds the tilted orthographic camera and trails the player across the ground
/// plane. The rig moves; the camera's local offset and angle never change, so
/// the framing stays identical no matter where the player is.
public partial class CameraRig : Node3D
{
    [Export] public NodePath? TargetPath { get; set; }

    /// Higher is tighter. Low values read as a lazy camera, which hides enemies
    /// entering from the direction of travel.
    [Export] public float FollowRate { get; set; } = 8.0f;

    private Node3D? _target;

    public override void _Ready()
    {
        _target = TargetPath != null ? GetNodeOrNull<Node3D>(TargetPath) : null;
        if (_target != null)
            GlobalPosition = Flatten(_target.GlobalPosition);
    }

    public override void _Process(double delta)
    {
        if (_target == null)
            return;

        float t = 1.0f - Mathf.Exp(-FollowRate * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(Flatten(_target.GlobalPosition), t);
    }

    private static Vector3 Flatten(Vector3 position) => new(position.X, 0.0f, position.Z);
}
