using Godot;

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
}

public enum ItemEffect
{
    /// Pure cargo. Worth carrying out and nothing else — which is what makes
    /// the serum a gamble rather than a resource.
    None,

    Heal,

    /// Refills the magazine reserve of whatever is equipped.
    Ammo,

    /// A timed burst of speed. The only consumable that buys position rather
    /// than health, and the only answer to being surrounded that is not a wall.
    Adrenaline,
}

/// A lootable item. Value is what makes extracting worth the walk, and Bulk is
/// what stops the answer being "carry everything".
[GlobalClass]
public partial class ItemResource : Resource
{
    [Export] public string ItemName { get; set; } = "";
    [Export] public ItemRarity Rarity { get; set; } = ItemRarity.Common;

    /// Extraction payout per unit.
    [Export] public int Value { get; set; } = 10;

    /// Backpack slots consumed per unit.
    [Export] public int Bulk { get; set; } = 1;

    /// Relative chance of appearing in a container roll.
    [Export] public float Weight { get; set; } = 1.0f;

    [Export] public int MinStack { get; set; } = 1;
    [Export] public int MaxStack { get; set; } = 1;

    /// What using one does. Every usable item is worth something at extraction
    /// too, so spending it costs exactly its Value — the backpack holds health
    /// and money in the same slots, and the choice between them is the point.
    [Export] public ItemEffect Effect { get; set; } = ItemEffect.None;

    /// Health restored, rounds added, or seconds of speed, depending on Effect.
    [Export] public float EffectAmount { get; set; }

    public bool IsUsable => Effect != ItemEffect.None;
}
