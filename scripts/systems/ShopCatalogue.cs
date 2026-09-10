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

        /// The resource this row was read from, held so the summary can be
        /// composed from it rather than stored.
        public required Resource Source { get; init; }

        /// The line the base screen prints under the cursor.
        ///
        /// **Composed on read, from a resource that is already in hand.** It was
        /// composed once when the catalogue was built, for a good reason that
        /// stopped being the whole reason: composing it in `Redraw` used to mean
        /// a `GD.Load` of the selected resource on every keypress, which is a
        /// file system call per cursor move for a string that cannot change while
        /// the screen is open.
        ///
        /// Except that it can, and it did the moment the summary had words in it.
        /// The Console fitting switches language without leaving the room, and a
        /// string built at construction stays in the language the catalogue was
        /// built in — so the shop would have kept saying "+2 armour" under a
        /// Chinese cursor until the player launched a run and came back.
        ///
        /// Holding the resource answers both: no load, no directory scan, and
        /// nothing cached that a language can invalidate.
        public string Summary => Describe(Source);
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
                Source = weapon,
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
                Source = piece,
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

    /// Whichever of the two shelves this row came from.
    private static string Describe(Resource source) => source switch
    {
        WeaponResource weapon => Describe(weapon),
        GearResource gear => Describe(gear),
        _ => "",
    };

    /// One line of what a piece actually does, read off the resource.
    ///
    /// Every non-zero field and nothing else. A curated sentence per item would
    /// read better and would go stale the first time a number changed, and a shop
    /// that describes equipment it no longer sells is worse than a terse one.
    private static string Describe(GearResource gear)
    {
        var parts = new System.Collections.Generic.List<string>();

        Add(parts, "stat.health", gear.HealthBonus);
        Add(parts, "stat.armour", gear.ArmourBonus);
        Add(parts, "stat.speed", gear.MoveSpeedBonus);
        Add(parts, "stat.carry", gear.CarryBonus);
        Add(parts, "stat.safebox", gear.SafeBoxBonus);
        Add(parts, "stat.pierce", gear.PierceBonus);
        Add(parts, "stat.area", gear.AreaBonus);
        Add(parts, "stat.thorns", gear.ThornsBonus);
        Add(parts, "stat.regen", gear.RegenBonus);
        Add(parts, "stat.knockback", gear.KnockbackBonus);
        Add(parts, "stat.dodge", gear.DodgeBonus);

        // The ceilings are half of what a piece is, and the half a player cannot
        // discover by wearing it for ten seconds.
        var caps = new System.Collections.Generic.List<string>();
        Cap(caps, "stat.health", gear.HealthUpgradeCap);
        Cap(caps, "stat.armour", gear.ArmourUpgradeCap);
        Cap(caps, "stat.speed", gear.SpeedUpgradeCap);
        Cap(caps, "stat.search", gear.SearchUpgradeCap);
        Cap(caps, "stat.pierce", gear.PierceUpgradeCap);
        Cap(caps, "stat.crit", gear.CritUpgradeCap);
        Cap(caps, "stat.area", gear.AreaUpgradeCap);
        Cap(caps, "stat.thorns", gear.ThornsUpgradeCap);
        Cap(caps, "stat.regen", gear.RegenUpgradeCap);
        Cap(caps, "stat.knockback", gear.KnockbackUpgradeCap);
        Cap(caps, "stat.dodge", gear.DodgeUpgradeCap);
        Cap(caps, "stat.fortune", gear.FortuneUpgradeCap);

        if (caps.Count > 0)
            parts.Add(Strings.Get("shop.upgrades", string.Join(" ", caps)));

        return parts.Count > 0 ? string.Join("   ", parts) : Strings.Get("shop.nothing");
    }

    private static string Describe(WeaponResource weapon)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            Strings.Get("shop.damage", $"{weapon.BaseDamage:F0}"),
            Strings.Get("shop.rate", $"{weapon.BaseAttackSpeed:F1}"),
            Strings.Get("shop.range", $"{weapon.BaseRange:F1}"),
        };

        // `trait.burst` and not `Burst.ToString().ToLower()`. The enum name was
        // doing double duty as a label, which reads fine in English and is a
        // word nobody can translate — it is not in the table, so nothing would
        // have reported it missing either.
        if (weapon.Trait != WeaponTrait.None)
            parts.Add(Strings.Get($"trait.{weapon.Trait.ToString().ToLower()}"));

        return string.Join("   ", parts);
    }

    /// **The signed amount is one argument and the label is the other.** English
    /// puts the number first — "+2 armour" — and Chinese puts it last, 「護甲 +2」,
    /// so the two cannot be one interpolation with a word swapped in it. Same
    /// reason `CharacterResource.AbilityLine` is assembled from seven keys.
    private static void Add(System.Collections.Generic.List<string> parts, string key, float amount)
    {
        if (amount != 0.0f)
        {
            parts.Add(Strings.Get("shop.stat",
                                  $"{(amount > 0.0f ? "+" : "")}{amount:0.##}",
                                  Strings.Get(key)));
        }
    }

    private static void Cap(System.Collections.Generic.List<string> caps, string key, int value)
    {
        // -1 is "no opinion" and 0 is "none allowed". Printing them the same way
        // would hide the entire cost side of a sidegrade: the bandolier's zero
        // fortune is the reason its five pierce is a trade.
        if (value >= 0)
            caps.Add(Strings.Get("shop.cap", Strings.Get(key), value));
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

    /// Every item on disk, by name, loaded once.
    ///
    /// This used to walk the items directory and `GD.Load` every `.tres` on each
    /// call, and its only caller is `StashValue` — which the base screen asks
    /// for while it is being drawn. A directory scan per frame is the cheap half
    /// of the cost: in an *exported* build the repeated load of a resource whose
    /// script is a C# class races the script bridge and prints a "Handle is not
    /// initialized" backtrace every frame, which is how a perfectly playable
    /// screen comes to look like a broken one.
    ///
    /// Static because the item table is content, not state — nothing writes to
    /// it during a run, and a per-instance cache would be rebuilt by every
    /// screen that happens to construct a catalogue.
    private static System.Collections.Generic.Dictionary<string, ItemResource>? _items;

    private static ItemResource? FindItem(string itemName)
    {
        if (_items == null)
        {
            _items = new System.Collections.Generic.Dictionary<string, ItemResource>();
            foreach (string path in Files("res://resources/items"))
            {
                var item = GD.Load<ItemResource>(path);
                if (item != null)
                    _items[item.ItemName] = item;
            }
        }

        return _items.TryGetValue(itemName, out ItemResource? found) ? found : null;
    }
}
