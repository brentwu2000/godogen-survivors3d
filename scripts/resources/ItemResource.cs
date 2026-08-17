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

    /// Damage in a radius where it lands. The first thing the backpack can do to
    /// the world rather than to the person carrying it.
    Explosive,

    /// A burning patch that keeps damaging for a while. Area denial rather than
    /// a burst — it answers a doorway, not a crowd.
    Incendiary,
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

    /// Health restored, rounds added, seconds of speed, blast damage, or damage
    /// per second, depending on Effect.
    [Export] public float EffectAmount { get; set; }

    /// Metres. Thrown effects only.
    [Export] public float EffectRadius { get; set; }

    /// Seconds a patch keeps burning. Incendiary only.
    [Export] public float EffectDuration { get; set; }

    /// Acts on the player. Spent with the use key.
    public bool IsSupply => Effect is ItemEffect.Heal or ItemEffect.Ammo or ItemEffect.Adrenaline;

    /// Acts on the world. Spent with the throw key, because a heal and a
    /// grenade sharing a button is a grenade thrown at the wrong moment.
    public bool IsThrowable => Effect is ItemEffect.Explosive or ItemEffect.Incendiary;

    public bool IsUsable => Effect != ItemEffect.None;
}
