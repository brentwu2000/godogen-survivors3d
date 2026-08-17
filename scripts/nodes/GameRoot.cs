using Godot;

/// Root of the main scene. Phase 0 keeps this to a liveness marker so the build
/// gate proves the C# assembly actually loads at runtime, not just that it
/// compiled.
public partial class GameRoot : Node3D
{
    public override void _Ready()
    {
        GD.Print($"GameRoot ready — physics tick {Engine.PhysicsTicksPerSecond} Hz");
    }
}
