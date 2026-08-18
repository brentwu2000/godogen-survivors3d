using Godot;

/// Between runs: what came back, what it is worth, and what to take next time.
///
/// This is where credits stop being a number that only goes up. Everything on
/// sale moves where a run starts and how far it can climb, and everything above
/// starting kit is left behind if the player dies wearing it — so the screen's
/// real question is not "can I afford this" but "am I willing to lose it".
public partial class BaseScreen : Control
{
    private Label _screen = null!;
    private Label _side = null!;
    private Profile _profile = null!;
    private ShopCatalogue _catalogue = null!;
    private int _cursor;
    private string _message = "";

    public override void _Ready()
    {
        _screen = GetNode<Label>("Screen");
        _side = GetNode<Label>("Side");

        foreach (Label label in new[] { _screen, _side })
        {
            label.AddThemeFontSizeOverride("font_size", 18);
            label.AddThemeColorOverride("font_color", new Color(0.94f, 0.93f, 0.88f));
        }

        _profile = SaveSystem.Load();
        _catalogue = new ShopCatalogue();
        Redraw();
    }

    /// Polled rather than event-driven, like the rest of the input in this
    /// project. It also keeps the screen drivable from a script: Input.ActionPress
    /// moves the poll state but never enters the event pipeline, so a menu built
    /// on _UnhandledInput is one no probe can press a key on.
    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("ui_down"))
            Move(1);
        else if (Input.IsActionJustPressed("ui_up"))
            Move(-1);
        else if (Input.IsActionJustPressed("ui_accept"))
            Choose();
        else if (Input.IsActionJustPressed("menu_sell"))
            SellStash();
        else if (Input.IsActionJustPressed("menu_launch"))
            Launch();
        else if (Input.IsActionJustPressed("pick_1"))
            TakeContract(0);
        else if (Input.IsActionJustPressed("pick_2"))
            TakeContract(1);
        else if (Input.IsActionJustPressed("pick_3"))
            TakeContract(2);
        else if (Input.IsActionJustPressed("menu_reroll"))
            Reroll();
        else if (Input.IsActionJustPressed("menu_biome"))
            CycleBiome();
        else if (Input.IsActionJustPressed("menu_daily"))
            LaunchDaily();
        else
            return;

        Redraw();
    }

    private void Move(int delta)
    {
        int count = _catalogue.All.Count;
        if (count > 0)
            _cursor = Mathf.PosMod(_cursor + delta, count);
    }

    /// One key for both: buy what is not owned, equip what is. A shop where
    /// buying and wearing are separate keys is a shop where the player buys
    /// something and walks out without it.
    private void Choose()
    {
        if (_cursor < 0 || _cursor >= _catalogue.All.Count)
            return;

        ShopCatalogue.Entry entry = _catalogue.All[_cursor];

        if (!_profile.Owns(entry.Path))
        {
            // Checked before the price, because a locked entry is not expensive,
            // it is unavailable — and "you cannot afford it" would send the
            // player off to earn credits that will not help.
            if (UnlockBook.ShopLockReason(_profile, entry.Path, entry.Tier) is { } reason)
            {
                _message = $"{entry.Name} is locked — {reason.ToLower()}";
                return;
            }

            if (entry.Price <= 0)
            {
                _message = $"{entry.Name} is not for sale";
                return;
            }

            if (_profile.Credits < entry.Price)
            {
                // Nothing is deducted and nothing is granted. A partial purchase
                // is the one outcome a shop must never have.
                _message = $"{entry.Name} costs {entry.Price}; you have {_profile.Credits}";
                return;
            }

            _profile.Credits -= entry.Price;
            _profile.Grant(entry.Path);
            _message = $"bought {entry.Name} for {entry.Price}";
        }

        Equip(entry);
        Persist();
    }

    private void Equip(ShopCatalogue.Entry entry)
    {
        if (entry.Slot is { } slot)
        {
            _profile.EquippedGear[(int)slot] = entry.Path;
            _message = $"{_message}{(_message.Length > 0 ? "; " : "")}wearing {entry.Name}";
            return;
        }

        var weapon = GD.Load<WeaponResource>(entry.Path);
        if (weapon == null)
            return;

        // Melee goes to the sidearm slot and everything else to the primary.
        // Two rifles and no fallback is a loadout that ends the moment the
        // reserve does, and nothing on this screen warns about that.
        if (weapon.IsMelee)
            _profile.LoadoutSecondary = entry.Path;
        else
            _profile.LoadoutWeapon = entry.Path;

        _message = $"{_message}{(_message.Length > 0 ? "; " : "")}carrying {entry.Name}";
    }

    /// Commits to one of the three jobs on the board.
    ///
    /// Taking one is the point. Three jobs that all pay out if they happen to be
    /// satisfied are three things that happen to a player; picking one before
    /// leaving is a plan, and a plan is what makes the twentieth run different
    /// from the fifth.
    private void TakeContract(int index)
    {
        Contract[] offer = _profile.ContractOffer();
        if (index < 0 || index >= offer.Length)
            return;

        _profile.ContractIndex = index;
        _message = $"took the contract: {offer[index].Describe()}";
        Persist();
    }

    /// A new board, for money. Free rerolls mean spinning until the easiest card
    /// appears, and a job nobody had to weigh is a delayed handout.
    private void Reroll()
    {
        if (_profile.Credits < ContractBook.RerollCost)
        {
            _message = $"a new board costs {ContractBook.RerollCost}; you have {_profile.Credits}";
            return;
        }

        _profile.Credits -= ContractBook.RerollCost;
        _profile.RollContracts();
        _message = $"new contracts for {ContractBook.RerollCost}";
        Persist();
    }

    /// The stash is sold at face value. The extraction multiplier was earned by
    /// walking out with it and is not paid a second time.
    private void SellStash()
    {
        int value = ShopCatalogue.StashValue(_profile);
        if (value <= 0)
        {
            _message = "nothing in the stash";
            return;
        }

        _profile.Credits += value;
        _profile.Stash.Clear();
        _message = $"sold the stash for {value}";
        Persist();
    }

    /// Steps to the next place the player has opened, skipping the rest.
    ///
    /// Cycling rather than a submenu: there are three, the list is on screen with
    /// what each one costs you, and a menu for a three-way choice is a menu.
    private void CycleBiome()
    {
        int count = BiomeBook.All.Length;
        for (int step = 1; step <= count; step++)
        {
            int next = (_profile.Biome + step) % count;
            if (!BiomeBook.Allows(_profile, next))
                continue;

            _profile.Biome = next;
            _message = $"heading for {BiomeBook.Load(next).BiomeName}";
            Persist();
            return;
        }

        _message = "nowhere else to go yet";
    }

    /// Today's run, once.
    ///
    /// The refusal is the feature. Without it this is an ordinary run on a fixed
    /// seed, and a player who does not like their result simply plays it again
    /// until they do — at which point "everyone got the same one" stops meaning
    /// anything, because everyone also got as many tries as they wanted.
    private void LaunchDaily()
    {
        DailyRun.Setup today = DailyRun.Today();

        if (_profile.DailyDone(today.DateKey))
        {
            _message = $"today's run is done — {_profile.Daily[today.DateKey]} points. " +
                       $"the next one is tomorrow";
            return;
        }

        GameSession.LaunchedFromBase = true;
        GameSession.Biome = today.Biome;
        GameSession.DailyKey = today.DateKey;
        GameSession.DailySeed = today.LevelSeed;
        GameSession.DailyJob = today.Job;

        Persist();
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    private void Launch()
    {
        // Cleared, not merely unset elsewhere. These are statics that outlive a
        // scene by design, so an ordinary run launched after a daily would still
        // be one unless something says otherwise — and it would silently score
        // itself into today's slot.
        GameSession.DailyKey = "";

        GameSession.LaunchedFromBase = true;

        // Handed over here rather than read from the profile by the level, so
        // that a probe or capture script — which never touches this screen —
        // gets the default rather than whatever the player last chose.
        GameSession.Biome = _profile.Biome;

        Persist();
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    private void Persist() => SaveSystem.Save(_profile);

    private void Redraw()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"BASE     credits {_profile.Credits}     " +
                        $"stash worth {ShopCatalogue.StashValue(_profile)}     " +
                        $"runs {_profile.RunsSurvived} out / {_profile.RunsLost} lost");

        // Practice is listed but has no price. It is the one axis the shop
        // cannot reach, and saying so on the screen is cheaper than a player
        // wondering why it is missing.
        text.AppendLine($"practice   knife {_profile.Proficiency[0]}   long {_profile.Proficiency[1]}   " +
                        $"bow {_profile.Proficiency[2]}   firearm {_profile.Proficiency[3]}   (not for sale)");

        // Where the run is going, on the shop screen rather than at launch.
        // Terrain has to be known while equipment is being bought, or the
        // loadout could not have been built for it — which is the whole reason
        // the two exist.
        BiomeResource here = BiomeBook.Load(_profile.Biome);
        string more = BiomeBook.All.Length > 1 && !BiomeBook.Allows(_profile, _profile.Biome + 1)
            ? $"   (next opens after {BiomeBook.OpensAt(_profile.Biome + 1)} extractions)"
            : "";

        text.AppendLine($"heading for  {here.BiomeName} — {here.Blurb}   [B] change{more}");

        // What the cursor is on, in the piece's own numbers.
        //
        // Two pieces in a slot at the same price are the whole point of the shop
        // and the list cannot say that: "Plate Carrier 900 / Stitched Vest 900"
        // is a coin flip until something says one soaks and the other returns.
        //
        // Above the list rather than below it. Fifteen rows already reach the
        // bottom of a 1080p screen, so a line appended after them is a line
        // nobody sees — which is how the first version of this shipped its
        // description into the void.
        if (_cursor >= 0 && _cursor < _catalogue.All.Count)
            text.AppendLine($"           {Describe(_catalogue.All[_cursor])}");

        text.AppendLine();

        // A window rather than the whole list.
        //
        // Fifteen rows already ran off the bottom of a 1080p screen, and the
        // catalogue only grows — a shop that silently stops listing its last item
        // is worse than one that admits there is more. The window follows the
        // cursor and clamps at both ends, so the first and last rows are always
        // reachable and the count of hidden rows is stated.
        (int first, int last) = Window(_catalogue.All.Count);

        if (first > 0)
            text.AppendLine($"     ... {first} above");

        for (int i = first; i < last; i++)
        {
            ShopCatalogue.Entry entry = _catalogue.All[i];
            bool owned = _profile.Owns(entry.Path);
            bool equipped = IsEquipped(entry);

            // Listed, never hidden. Content the player cannot see does not make
            // them want it; content they can see and cannot have does — and the
            // condition printed where the price would go is the only place the
            // game ever explains how to get it.
            string? locked = owned ? null : UnlockBook.ShopLockReason(_profile, entry.Path, entry.Tier);

            string state = locked != null ? "[locked]"
                : equipped ? "[equipped]"
                : owned ? "[owned]"
                : entry.Price > 0 ? $"{entry.Price} cr"
                : "—";

            string note = locked != null ? $"  {locked.ToLower()}"
                : !Profile.IsStartingKit(entry.Path) && owned ? "  (lost if you die)"
                : "";

            text.AppendLine($"{(i == _cursor ? " >" : "  ")} {entry.Name,-18} {state,-12}{note}");
        }

        if (last < _catalogue.All.Count)
            text.AppendLine($"     ... {_catalogue.All.Count - last} below");

        // Newlines normalised on the way into the Label.
        //
        // `StringBuilder.AppendLine` writes `Environment.NewLine`, which on
        // Windows is "\r\n", and Godot's Label treats the carriage return as a
        // line break of its own — so every line of both columns has been drawn
        // double-spaced since the first screen was built. It reads as a design
        // choice rather than as a bug, which is why it survived four phases of
        // "the list runs off the bottom" being treated as a content problem.
        _screen.Text = Unix(text.ToString());
        _side.Text = Unix(SideColumn());
    }

    private static string Unix(string text) => text.Replace("\r\n", "\n");

    /// The slice of the catalogue to draw, following the cursor.
    ///
    /// Twenty-four rows, which is what fits under the header now that the text is
    /// not double-spaced. It was twelve, chosen when every line was drawn twice
    /// as tall as it should have been — a window sized to a bug rather than to the
    /// screen. Kept as a window rather than removed, because the catalogue does
    /// only grow.
    ///
    /// Clamped at both ends rather than centred unconditionally, so the top of the
    /// list is not permanently half a screen of empty space when the cursor is on
    /// the first item.
    private (int First, int Last) Window(int count)
    {
        const int Rows = 24;

        if (count <= Rows)
            return (0, count);

        int first = Mathf.Clamp(_cursor - Rows / 2, 0, count - Rows);
        return (first, first + Rows);
    }

    /// One line of what a row actually does, read off the resource.
    ///
    /// Every non-zero field, and nothing else. A curated sentence per piece would
    /// read better and would go stale the first time a number changed — and a
    /// shop that describes equipment it no longer sells is worse than a terse one.
    private static string Describe(ShopCatalogue.Entry entry)
    {
        var parts = new System.Collections.Generic.List<string>();

        // Asked of the entry, not attempted by loading. GD.Load<T> on the wrong
        // type throws an InvalidCastException rather than returning null, so
        // "try gear, fall back to weapon" takes the whole screen down on the
        // first rifle — and the catalogue already knows which is which.
        if (!entry.IsWeapon && GD.Load<GearResource>(entry.Path) is { } gear)
        {
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

            // The ceilings are half of what a piece is, and the half a player
            // cannot discover by wearing it for ten seconds.
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

        if (entry.IsWeapon && GD.Load<WeaponResource>(entry.Path) is { } weapon)
        {
            parts.Add($"{weapon.BaseDamage:F0} dmg");
            parts.Add($"{weapon.BaseAttackSpeed:F1}/s");
            parts.Add($"{weapon.BaseRange:F1} m");
            if (weapon.Trait != WeaponTrait.None)
                parts.Add(weapon.Trait.ToString().ToLower());

            return string.Join("   ", parts);
        }

        return "";
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

    /// The right-hand column: what to chase, what to take, and which keys do it.
    ///
    /// Records sit next to contracts on purpose. Neither changes a number in the
    /// next run — one is a target, the other is a job — and putting the pair
    /// beside the shop, which changes every number, is what makes the two kinds
    /// of progress legible as different things.
    private string SideColumn()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine("PERSONAL BEST");
        text.AppendLine($"  banked {_profile.BestBank}      killed {_profile.BestKills}      " +
                        $"lasted {_profile.BestSeconds:F0}s");
        text.AppendLine($"  multiplier x{_profile.BestMultiplier:F2}      " +
                        $"streak {_profile.BestStreak} (now {_profile.Streak})");

        // The record book: things the run has always measured and nothing kept.
        // Sentinel values are printed as a dash rather than as a number, because
        // "narrowest escape 3.4e+38 HP" is worse than saying it has not happened.
        text.AppendLine($"  crates {_profile.MostCrates}      " +
                        $"best throw {_profile.BestThrow}      " +
                        $"bosses {_profile.BestBossKills}");
        text.AppendLine($"  narrowest escape {(_profile.HasNarrowEscape ? $"{_profile.NarrowestEscape:F0} HP" : "—")}" +
                        $"      fastest out {(_profile.HasFastExtraction ? $"{_profile.FastestExtraction:F0}s" : "—")}");
        text.AppendLine();

        DailyRun.Setup today = DailyRun.Today();
        bool done = _profile.DailyDone(today.DateKey);
        int streak = _profile.DailyStreak(today.DateKey);

        text.AppendLine($"TODAY  {today.DateKey}");
        text.AppendLine($"  {BiomeBook.Load(today.Biome).BiomeName} — {today.Job.Describe()}");
        text.AppendLine(done
            ? $"  done: {_profile.Daily[today.DateKey]} points" +
              (streak > 1 ? $"      {streak} days running" : "")
            : "  [D] play it — one attempt, no credits, no risk");
        text.AppendLine();

        text.AppendLine("CONTRACTS");
        Contract[] offer = _profile.ContractOffer();
        for (int i = 0; i < offer.Length; i++)
        {
            bool taken = _profile.ContractIndex == i;
            text.AppendLine($"  [{i + 1}] {offer[i].Describe(),-32} {offer[i].Reward,4} cr" +
                            (taken ? "   <- taking this" : ""));
        }

        if (!_profile.HasContract)
        {
            text.AppendLine();
            text.AppendLine("  none taken — a run without one still pays,");
            text.AppendLine("  it just does not ask anything of you");
        }

        text.AppendLine();
        text.AppendLine("KEYS");
        text.AppendLine("  up/down choose          enter buy or equip");
        text.AppendLine("  [1][2][3] take a contract");
        text.AppendLine($"  [R] reroll contracts ({ContractBook.RerollCost} cr)");
        text.AppendLine("  [B] change terrain      [D] today's run");
        text.AppendLine("  [S] sell stash          [L] launch");

        if (_message.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(_message);
        }

        return text.ToString();
    }

    private bool IsEquipped(ShopCatalogue.Entry entry) => entry.Slot is { } slot
        ? _profile.EquippedGear[(int)slot] == entry.Path
        : _profile.LoadoutWeapon == entry.Path || _profile.LoadoutSecondary == entry.Path;
}
