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
///
/// It used to carry `FireHeld`, `InteractPressed` and `ReloadPressed` as well.
/// Nothing read any of them: firing is automatic by design — the survivors-like
/// contract is that the player steers and the weapon handles itself — and
/// reloading happens on its own when the magazine empties. An interface member
/// nobody reads is a promise nobody checks, and every implementation still had
/// to invent an answer for it. Building the touch layer is what made that cost
/// visible, because on a phone a fire button is a whole thumb.
public interface IInputSource
{
    /// Movement intent, magnitude 0..1.
    Vector2 Move { get; }

    /// Explicit aim direction, normalized — or Vector2.Zero when the player is
    /// not aiming, in which case auto-targeting decides where shots go.
    Vector2 Aim { get; }

    /// Moves the best item from the backpack into the safe box. Its own action
    /// because it is used mid-fight, under time pressure.
    bool SecurePressed { get; }

    /// Spends the cheapest carried item that would currently help.
    bool UsePressed { get; }

    /// Switches to the other weapon slot.
    bool SwapPressed { get; }

    /// Throws the cheapest carried item that acts on the world.
    bool ThrowPressed { get; }

    /// Throws away the worst thing in the backpack, by value per bulk.
    ///
    /// Its own verb rather than a menu, because the moment it is for is standing
    /// over a cache with a full bag and something arriving. A player who has to
    /// open an inventory to decide is a player who has already been bitten.
    bool DropPressed { get; }

    /// Called once per frame before the properties are read. Sources that need
    /// to project a screen position into the world (mouse aim) use the player's
    /// position as the ground plane height.
    void Update(Vector3 playerPosition);
}
