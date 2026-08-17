using Godot;

/// What the base screen can sell, read off the resource files rather than kept
/// in a second list beside them.
///
/// A shop with its own copy of the stock is a shop that disagrees with the game
/// the first time a price changes. Everything here comes from the .tres.
public sealed class ShopCatalogue
{
    public sealed class Entry
    {
        public required string Path { get; init; }
        public required string Name { get; init; }
        public required int Price { get; init; }
        public required int Tier { get; init; }

        /// Null for a weapon, set for gear. The slot decides what equipping it
        /// displaces.
        public GearSlot? Slot { get; init; }

        public bool IsWeapon => Slot == null;
    }

    public System.Collections.Generic.List<Entry> Weapons { get; } = new();
    public System.Collections.Generic.List<Entry> Gear { get; } = new();

    /// Everything, weapons first, in the order the base screen lists it.
    public System.Collections.Generic.List<Entry> All { get; } = new();

    public ShopCatalogue()
    {
        LoadWeapons("res://resources/weapons");
        LoadGear("res://resources/gear");

        All.AddRange(Weapons);
        All.AddRange(Gear);
    }

    private void LoadWeapons(string directory)
    {
        foreach (string path in Files(directory))
        {
            var weapon = GD.Load<WeaponResource>(path);
            if (weapon == null)
                continue;

            Weapons.Add(new Entry
            {
                Path = path,
                Name = weapon.WeaponName,
                Price = weapon.Price,
                Tier = weapon.Tier,
            });
        }

        Weapons.Sort((a, b) => a.Tier != b.Tier ? a.Tier - b.Tier : string.CompareOrdinal(a.Name, b.Name));
    }

    private void LoadGear(string directory)
    {
        foreach (string path in Files(directory))
        {
            var piece = GD.Load<GearResource>(path);
            if (piece == null)
                continue;

            Gear.Add(new Entry
            {
                Path = path,
                Name = piece.GearName,
                Price = piece.Price,
                Tier = piece.Tier,
                Slot = piece.Slot,
            });
        }

        Gear.Sort((a, b) => a.Slot != b.Slot
            ? (int)a.Slot! - (int)b.Slot!
            : a.Tier - b.Tier);
    }

    /// Sorted, so the list a player sees does not depend on directory order —
    /// which differs between the editor and an exported build.
    private static System.Collections.Generic.List<string> Files(string directory)
    {
        var paths = new System.Collections.Generic.List<string>();

        using var dir = DirAccess.Open(directory);
        if (dir == null)
        {
            GD.PushWarning($"ShopCatalogue: {directory} missing");
            return paths;
        }

        foreach (string file in dir.GetFiles())
        {
            // Exported projects rewrite .tres to .remap; strip it or nothing
            // loads outside the editor.
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (name.EndsWith(".tres"))
                paths.Add($"{directory}/{name}");
        }

        paths.Sort(string.CompareOrdinal);
        return paths;
    }

    /// What the stash is worth if sold. Face value: the multiplier was earned by
    /// walking out with it and is not paid twice.
    public static int StashValue(Profile profile)
    {
        int total = 0;

        foreach (var pair in profile.Stash)
        {
            ItemResource? item = FindItem(pair.Key);
            if (item != null)
                total += item.Value * pair.Value;
        }

        return total;
    }

    private static ItemResource? FindItem(string itemName)
    {
        foreach (string path in Files("res://resources/items"))
        {
            var item = GD.Load<ItemResource>(path);
            if (item != null && item.ItemName == itemName)
                return item;
        }

        return null;
    }
}
