using Godot;

/// Device-facing input, resolved to world-space intent.
///
/// Gameplay code reads Move/Aim and never touches a device. That is what lets
/// the same Player script drive both the desktop build and the touch build —
/// the only thing that swaps is which implementation is installed.
///
/// Both vectors are in the world XZ plane, already corrected for the camera's
/// yaw, so +Y on the vector means "away from the camera" regardless of how the
/// rig is rotated.
public interface IInputSource
{
    /// Movement intent, magnitude 0..1.
    Vector2 Move { get; }

    /// Explicit aim direction, normalized — or Vector2.Zero when the player is
    /// not aiming, in which case auto-targeting decides where shots go.
    Vector2 Aim { get; }

    bool FireHeld { get; }
    bool InteractPressed { get; }
    bool ReloadPressed { get; }

    /// Moves the best item from the backpack into the safe box. Separate from
    /// Interact because it is used mid-fight, under time pressure, and must not
    /// compete with whatever Interact grows into.
    bool SecurePressed { get; }

    /// Called once per frame before the properties are read. Sources that need
    /// to project a screen position into the world (mouse aim) use the player's
    /// position as the ground plane height.
    void Update(Vector3 playerPosition);
}
