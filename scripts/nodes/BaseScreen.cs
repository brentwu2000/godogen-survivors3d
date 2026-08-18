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

    private void Launch()
    {
        GameSession.LaunchedFromBase = true;
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

        text.AppendLine();

        for (int i = 0; i < _catalogue.All.Count; i++)
        {
            ShopCatalogue.Entry entry = _catalogue.All[i];
            bool owned = _profile.Owns(entry.Path);
            bool equipped = IsEquipped(entry);

            string state = equipped ? "[equipped]"
                : owned ? "[owned]"
                : entry.Price > 0 ? $"{entry.Price} cr"
                : "—";

            string risk = !Profile.IsStartingKit(entry.Path) && owned ? "  (lost if you die)" : "";

            text.AppendLine($"{(i == _cursor ? " >" : "  ")} {entry.Name,-18} {state,-12}{risk}");
        }

        _screen.Text = text.ToString();
        _side.Text = SideColumn();
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
