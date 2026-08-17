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
        //
        // The other axis is whether it does anything before it is sold. Using a
        // consumable costs exactly its Value, so the cheap heal is nearly free
        // to spend and the medkit is a real decision — and the two items worth
        // most are pure cargo, which is what makes carrying them a gamble rather
        // than a stockpile.
        ItemResource[] items =
        {
            new() { ItemName = "Scrap Metal", Rarity = ItemRarity.Common, Value = 10, Bulk = 2, Weight = 5.0f, MinStack = 1, MaxStack = 4 },
            new() { ItemName = "Canned Food", Rarity = ItemRarity.Common, Value = 14, Bulk = 1, Weight = 4.0f, MinStack = 1, MaxStack = 3,
                    Effect = ItemEffect.Heal, EffectAmount = 15.0f },
            new() { ItemName = "Rifle Rounds", Rarity = ItemRarity.Common, Value = 18, Bulk = 1, Weight = 3.5f, MinStack = 2, MaxStack = 6,
                    Effect = ItemEffect.Ammo, EffectAmount = 30.0f },
            new() { ItemName = "Adrenaline Shot", Rarity = ItemRarity.Uncommon, Value = 60, Bulk = 1, Weight = 1.4f,
                    Effect = ItemEffect.Adrenaline, EffectAmount = 8.0f },
            new() { ItemName = "Medkit", Rarity = ItemRarity.Uncommon, Value = 80, Bulk = 2, Weight = 1.6f,
                    Effect = ItemEffect.Heal, EffectAmount = 45.0f },
            // The first two things the backpack can do to the world rather than
            // to the person carrying it. A burst answers a crowd; a fire answers
            // a doorway — and both cost their sale price to find out.
            new() { ItemName = "Pipe Bomb", Rarity = ItemRarity.Uncommon, Value = 90, Bulk = 1, Weight = 1.3f,
                    Effect = ItemEffect.Explosive, EffectAmount = 55.0f, EffectRadius = 4.5f },
            new() { ItemName = "Molotov", Rarity = ItemRarity.Uncommon, Value = 70, Bulk = 1, Weight = 1.3f,
                    Effect = ItemEffect.Incendiary, EffectAmount = 22.0f, EffectRadius = 3.5f, EffectDuration = 7.0f },

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
