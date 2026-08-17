using Godot;

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
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
}
