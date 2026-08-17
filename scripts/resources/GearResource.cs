using Godot;

public enum GearSlot
{
    Armour,
    Backpack,
    Boots,
}

/// A piece of equipment: where a run starts, and how far it can climb.
///
/// Gear is the only axis that does both. Practice raises the starting point and
/// cannot be lost; gear raises the starting point *and* the ceiling, and is left
/// on the ground when the player dies wearing it. That asymmetry is the whole
/// reason taking the good set out is a decision rather than a formality.
///
/// The three equipped pieces are summed. A slot that grants nothing in a
/// category leaves it at zero rather than carrying its own idea of a default —
/// summing defaults would make three pieces of empty gear better than one.
[GlobalClass]
public partial class GearResource : Resource
{
    [Export] public string GearName { get; set; } = "";
    [Export] public GearSlot Slot { get; set; } = GearSlot.Armour;

    /// Shown in the shop and the loadout; also what a tier is worth in credits.
    [Export] public int Tier { get; set; } = 1;

    // --- Starting point -----------------------------------------------------

    [Export] public float HealthBonus { get; set; }

    /// Flat mitigation. Subtracted from an incoming rate or amount rather than
    /// scaling it, so armour is the answer to a crowd of weak contacts and never
    /// the answer to a brute — which is the trade that makes it worth choosing.
    [Export] public float ArmourBonus { get; set; }

    [Export] public float MoveSpeedBonus { get; set; }
    [Export] public int CarryBonus { get; set; }
    [Export] public int SafeBoxBonus { get; set; }

    // --- Ceiling ------------------------------------------------------------
    //
    // How many in-run upgrades of each kind this piece permits. Reaching a cap
    // takes that option out of the pool, so the ceiling is something the player
    // watches happen rather than a number hidden in a formula.

    [Export] public int HealthUpgradeCap { get; set; }
    [Export] public int ArmourUpgradeCap { get; set; }
    [Export] public int SpeedUpgradeCap { get; set; }
    [Export] public int SearchUpgradeCap { get; set; }
}
