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

        /// The line the base screen prints under the cursor, composed once when
        /// the catalogue is built.
        ///
        /// It used to be composed in `Redraw`, which meant a `GD.Load` of the
        /// selected resource on every keypress — cheap, cached, and still a file
        /// system call per cursor move for a string that cannot change while the
        /// screen is open.
        public required string Summary { get; init; }
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
                Summary = Describe(weapon),
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
                Summary = Describe(piece),
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

    /// One line of what a piece actually does, read off the resource.
    ///
    /// Every non-zero field and nothing else. A curated sentence per item would
    /// read better and would go stale the first time a number changed, and a shop
    /// that describes equipment it no longer sells is worse than a terse one.
    private static string Describe(GearResource gear)
    {
        var parts = new System.Collections.Generic.List<string>();

        Add(parts, "health", gear.HealthBonus);
        Add(parts, "armour", gear.ArmourBonus);
        Add(parts, "speed", gear.MoveSpeedBonus);
        Add(parts, "carry", gear.CarryBonus);
        Add(parts, "safe box", gear.SafeBoxBonus);
        Add(parts, "pierce", gear.PierceBonus);
        Add(parts, "area", gear.AreaBonus);
        Add(parts, "thorns", gear.ThornsBonus);
        Add(parts, "regen", gear.RegenBonus);
        Add(parts, "knockback", gear.KnockbackBonus);
        Add(parts, "dodge", gear.DodgeBonus);

        // The ceilings are half of what a piece is, and the half a player cannot
        // discover by wearing it for ten seconds.
        var caps = new System.Collections.Generic.List<string>();
        Cap(caps, "health", gear.HealthUpgradeCap);
        Cap(caps, "armour", gear.ArmourUpgradeCap);
        Cap(caps, "speed", gear.SpeedUpgradeCap);
        Cap(caps, "search", gear.SearchUpgradeCap);
        Cap(caps, "pierce", gear.PierceUpgradeCap);
        Cap(caps, "crit", gear.CritUpgradeCap);
        Cap(caps, "area", gear.AreaUpgradeCap);
        Cap(caps, "thorns", gear.ThornsUpgradeCap);
        Cap(caps, "regen", gear.RegenUpgradeCap);
        Cap(caps, "knockback", gear.KnockbackUpgradeCap);
        Cap(caps, "dodge", gear.DodgeUpgradeCap);
        Cap(caps, "fortune", gear.FortuneUpgradeCap);

        if (caps.Count > 0)
            parts.Add($"upgrades: {string.Join(" ", caps)}");

        return parts.Count > 0 ? string.Join("   ", parts) : "grants nothing";
    }

    private static string Describe(WeaponResource weapon)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            $"{weapon.BaseDamage:F0} dmg",
            $"{weapon.BaseAttackSpeed:F1}/s",
            $"{weapon.BaseRange:F1} m",
        };

        if (weapon.Trait != WeaponTrait.None)
            parts.Add(weapon.Trait.ToString().ToLower());

        return string.Join("   ", parts);
    }

    private static void Add(System.Collections.Generic.List<string> parts, string name, float amount)
    {
        if (amount != 0.0f)
            parts.Add($"{(amount > 0.0f ? "+" : "")}{amount:0.##} {name}");
    }

    private static void Cap(System.Collections.Generic.List<string> caps, string name, int value)
    {
        // -1 is "no opinion" and 0 is "none allowed". Printing them the same way
        // would hide the entire cost side of a sidegrade: the bandolier's zero
        // fortune is the reason its five pierce is a trade.
        if (value >= 0)
            caps.Add($"{name} {value}");
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
