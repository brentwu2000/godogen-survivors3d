using Godot;

/// Writes the loot table to resources/items/*.tres.
///
///   godot --headless --script scripts/tools/BuildItems.cs
public partial class BuildItems : SceneTree
{
    private const string OutputDir = "res://resources/items";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        // Value per bulk is what the player is really choosing between: scrap is
        // 5/unit, the medkit 40/unit, the serum 220/unit. Rarity only sets how
        // often the choice comes up.
        ItemResource[] items =
        {
            new() { ItemName = "Scrap Metal", Rarity = ItemRarity.Common, Value = 10, Bulk = 2, Weight = 5.0f, MinStack = 1, MaxStack = 4 },
            new() { ItemName = "Canned Food", Rarity = ItemRarity.Common, Value = 14, Bulk = 1, Weight = 4.0f, MinStack = 1, MaxStack = 3 },
            new() { ItemName = "Rifle Rounds", Rarity = ItemRarity.Common, Value = 18, Bulk = 1, Weight = 3.5f, MinStack = 2, MaxStack = 6 },
            new() { ItemName = "Medkit", Rarity = ItemRarity.Uncommon, Value = 80, Bulk = 2, Weight = 1.6f },
            new() { ItemName = "Circuit Board", Rarity = ItemRarity.Uncommon, Value = 120, Bulk = 1, Weight = 1.2f },
            new() { ItemName = "Antiviral Serum", Rarity = ItemRarity.Rare, Value = 440, Bulk = 2, Weight = 0.35f },
        };

        foreach (ItemResource item in items)
        {
            string path = $"{OutputDir}/{item.ItemName.ToLower().Replace(' ', '_')}.tres";
            Error err = ResourceSaver.Save(item, path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }
            GD.Print($"Saved {path}");
        }

        return true;
    }
}
