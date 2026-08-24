using Godot;

/// Between runs: what came back, what it is worth, and what to take next time.
///
/// This is where credits stop being a number that only goes up. Everything on
/// sale moves where a run starts and how far it can climb, and everything above
/// starting kit is left behind if the player dies wearing it — so the screen's
/// real question is not "can I afford this" but "am I willing to lose it".
///
/// **The screen is now a panel on a room.** It used to show all of it at once
/// and answer eight verb keys from anywhere, which made every decision cost the
/// same: selling the stash, changing terrain and launching the run were three
/// keys on one page. What it draws is now decided by where the player is
/// standing in `Shelter`, and there are two verbs — `[E]` and `[C]` — that mean
/// whatever the fitting under the player means.
///
/// The logic below is unchanged. Buying, equipping, contracts, rerolls, selling,
/// terrain and the daily are the same methods they have always been; the room
/// chooses which of them a keypress reaches, and nothing about what they do had
/// any reason to move.
public partial class BaseScreen : Control
{
    /// The room, when there is one.
    ///
    /// Optional so the screen still works with no shelter around it — which is
    /// what `BaseLoopProbe` has always driven, and a probe that has to build a
    /// room to test a purchase is a probe testing two things.
    private Shelter? _shelter;

    /// What the player is standing at. `None` when there is no room, which shows
    /// everything — the flat screen, unchanged, as a fallback.
    private Fitting Focus => _shelter?.Focus ?? Fitting.None;
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
        _shelter = GetParent()?.GetNodeOrNull<Shelter>("Shelter");

        if (_shelter != null)
            _shelter.FocusChanged += _ => Redraw();

        // A player who has never played goes straight into a run.
        //
        // Everything on this screen is an answer to a question they have not been
        // asked yet: a fifteen-row shop, three terrains, a contract board and
        // eight unlock conditions describing a game they have not seen. Ninety
        // seconds of playing it first turns all of that from a menu into a set of
        // decisions about something they now understand.
        //
        // Deferred rather than immediate: `ChangeSceneToFile` during `_Ready`
        // frees the node currently being readied.
        if (!_profile.HasSeenBase)
        {
            _profile.HasSeenBase = true;
            Persist();
            CallDeferred(nameof(Launch));
            return;
        }

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
        else if (Input.IsActionJustPressed("pick_1"))
            TakeContract(0);
        else if (Input.IsActionJustPressed("pick_2"))
            TakeContract(1);
        else if (Input.IsActionJustPressed("pick_3"))
            TakeContract(2);
        else if (Input.IsActionJustPressed("interact"))
            First();
        else if (Input.IsActionJustPressed("interact_second"))
            Second();
        else
            return;

        Redraw();
    }

    /// `[E]`, meaning whatever the fitting under the player means.
    ///
    /// With no room the fallback is the shop, which is what the flat screen's
    /// `ui_accept` did — so a probe driving the screen without a shelter still
    /// buys things by pressing one key.
    private void First()
    {
        switch (Focus)
        {
            case Fitting.Armoury: Choose(); break;
            case Fitting.Locker: SellStash(); break;
            case Fitting.Board: TakeContract(_contractCursor); break;
            case Fitting.Map: CycleBiome(); break;
            case Fitting.Gate: Launch(); break;

            // Records has nothing to press. Saying so is better than a key that
            // silently does nothing, which the player reads as a bug in the key.
            case Fitting.Records: _message = "nothing to do here — this is the record book"; break;

            default: Choose(); break;
        }
    }

    /// `[C]`, for the three fittings that have a second thing to do.
    private void Second()
    {
        switch (Focus)
        {
            // Two meanings, one key, decided by the row. `[C]` is defined as
            // "the other thing you can do here", and for a weapon that is
            // carrying it in the other hand while for a piece of gear there is no
            // other hand to carry it in.
            case Fitting.Armoury:
                if (_cursor >= 0 && _cursor < _catalogue.All.Count && _catalogue.All[_cursor].Slot == null)
                    ChooseAsSidearm();
                else
                    SellOne();

                break;
            case Fitting.Board: Reroll(); break;
            case Fitting.Map: LaunchDaily(); break;

            default:
                (_, _, string second) = Shelter.Prompt(Focus);
                if (second.Length == 0)
                    _message = "nothing else to do here";

                break;
        }
    }

    private void Move(int delta)
    {
        if (Focus == Fitting.Board)
        {
            int contracts = _profile.ContractOffer().Length;
            if (contracts > 0)
                _contractCursor = Mathf.PosMod(_contractCursor + delta, contracts);

            return;
        }

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
        if (!TryBuy(entry))
            return;

        Equip(entry);
        Persist();
    }

    /// Owns it afterwards, or says why not.
    ///
    /// Lifted out of `Choose` when the armoury grew a second verb: buying and
    /// then carrying as a sidearm asks the same three questions in the same order
    /// as buying and then equipping, and two copies of "can the player have this"
    /// is two places for a locked item to become purchasable.
    private bool TryBuy(ShopCatalogue.Entry entry)
    {
        if (_profile.Owns(entry.Path))
            return true;

        // Checked before the price, because a locked entry is not expensive,
        // it is unavailable — and "you cannot afford it" would send the
        // player off to earn credits that will not help.
        if (UnlockBook.ShopLockReason(_profile, entry.Path, entry.Tier) is { } reason)
        {
            _message = $"{entry.Name} is locked — {reason.ToLower()}";
            return false;
        }

        if (entry.Price <= 0)
        {
            _message = $"{entry.Name} is not for sale";
            return false;
        }

        if (_profile.Credits < entry.Price)
        {
            // Nothing is deducted and nothing is granted. A partial purchase is
            // the one outcome a shop must never have.
            _message = $"{entry.Name} costs {entry.Price}; you have {_profile.Credits}";
            return false;
        }

        _profile.Credits -= entry.Price;
        _profile.Grant(entry.Path);
        _message = $"bought {entry.Name} for {entry.Price}";
        return true;
    }

    /// Carries the selected weapon in the second slot, whatever kind it is.
    ///
    /// `Equip` sends melee to the sidearm slot and everything else to the
    /// primary, which was a sound default while there was one interesting
    /// firearm. With a shotgun, a marksman rifle and a bolt launcher on the
    /// shelf it is a rule that quietly forbids the builds worth having — the
    /// marksman rifle charges while holstered, so *carrying it as a sidearm and
    /// swapping in for the shot* is the whole weapon, and there was no way to
    /// ask for that.
    private void ChooseAsSidearm()
    {
        if (_cursor < 0 || _cursor >= _catalogue.All.Count)
            return;

        ShopCatalogue.Entry entry = _catalogue.All[_cursor];

        if (entry.Slot != null)
        {
            _message = $"{entry.Name} is worn, not carried";
            return;
        }

        if (!TryBuy(entry))
            return;

        // Out of the primary if it was there, or the player would be carrying one
        // weapon in two slots and have nothing to swap to.
        if (_profile.LoadoutWeapon == entry.Path)
            _profile.LoadoutWeapon = "res://resources/weapons/scavenged_rifle.tres";

        _profile.LoadoutSecondary = entry.Path;
        _message = $"{_message}{(_message.Length > 0 ? "; " : "")}carrying {entry.Name} as sidearm";
        Persist();
    }

    /// Sells the selected piece back at half what it cost.
    ///
    /// This is the armoury's second verb, and until now it had **no key bound to
    /// it at all** — a verb the screen described and the player could not press.
    /// Half rather than full, because a shop that buys back at cost turns every
    /// purchase into a free trial and removes the only question the screen asks.
    ///
    /// Starting kit cannot be sold. It is the floor a dead run comes back to, and
    /// a player who sold it would have no way to start the next one.
    private void SellOne()
    {
        if (_cursor < 0 || _cursor >= _catalogue.All.Count)
            return;

        ShopCatalogue.Entry entry = _catalogue.All[_cursor];

        if (!_profile.Owns(entry.Path))
        {
            _message = $"you do not own {entry.Name}";
            return;
        }

        if (Profile.IsStartingKit(entry.Path))
        {
            _message = $"{entry.Name} is starting kit — it is what a dead run comes back to";
            return;
        }

        bool wasEquipped = IsEquipped(entry);
        int refund = entry.Price / 2;

        if (!_profile.Revoke(entry.Path))
        {
            _message = $"{entry.Name} cannot be sold";
            return;
        }

        _profile.Credits += refund;
        _message = $"sold {entry.Name} back for {refund}" +
                   (wasEquipped ? " — back to starting kit in that slot" : "");
        Persist();
    }

    /// Which contract `[E]` takes at the board. Moved by the same up/down as the
    /// shop list, because the board is a list too and two cursors that behave
    /// differently is one more thing to learn than the room needs.
    private int _contractCursor;

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
    /// Cycling rather than a submenu: the list is on screen with what each place
    /// costs you, and a menu for a choice this small is a menu for its own sake.
    ///
    /// It was written when there were three and there are five now, which is
    /// about where cycling stops being obviously right — one more and pressing
    /// [B] five times to get back to where you started is worse than a list.
    /// Left as it is until then rather than pre-emptively rebuilt.
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
            text.AppendLine($"           {_catalogue.All[_cursor].Summary}");

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
        // With a room, each fitting draws its own page and nothing else. The
        // flat screen showed all of it at once, which is why every decision on it
        // cost the same — a shop, a contract board, a record book and a launch
        // button competing for one reader.
        _screen.Text = Unix(_shelter == null ? text.ToString() : Page(text.ToString()));
        _side.Text = Unix(_shelter == null ? SideColumn() : PromptColumn());
    }

    private static string Unix(string text) => text.Replace("\r\n", "\n");

    /// The page for whatever the player is standing at.
    ///
    /// `shopPage` is passed in rather than rebuilt because it is the expensive
    /// one — a windowed list over the whole catalogue with an unlock reason per
    /// row — and building it to throw it away at five of the six fittings is work
    /// nobody sees.
    private string Page(string shopPage) => Focus switch
    {
        Fitting.Armoury => shopPage,
        Fitting.Locker => LockerPage(),
        Fitting.Records => RecordsPage(),
        Fitting.Board => BoardPage(),
        Fitting.Map => MapPage(),
        Fitting.Gate => GatePage(),
        _ => WalkingPage(),
    };

    /// What the room says when the player is not standing at anything.
    ///
    /// Deliberately almost empty. This is the state the player is in while
    /// walking across the room, and a page of text during it would be read as
    /// something they are supposed to act on.
    private string WalkingPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"credits {_profile.Credits}      stash worth {ShopCatalogue.StashValue(_profile)}" +
                        $"      runs {_profile.RunsSurvived} out / {_profile.RunsLost} lost");
        text.AppendLine();
        text.AppendLine("  walk to a fitting");
        return text.ToString();
    }

    private string LockerPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("LOCKER");
        text.AppendLine();

        int worth = ShopCatalogue.StashValue(_profile);
        text.AppendLine($"  everything carried out of a run and not spent: {worth} credits");
        text.AppendLine();

        if (_profile.Stash.Count == 0)
        {
            text.AppendLine("  empty — nothing has come back yet");
            return text.ToString();
        }

        foreach (var entry in _profile.Stash)
            text.AppendLine($"  {entry.Value,3} x {entry.Key}");

        return text.ToString();
    }

    private string RecordsPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("RECORDS");
        text.AppendLine();
        text.AppendLine($"  banked {_profile.BestBank}      killed {_profile.BestKills}      " +
                        $"lasted {_profile.BestSeconds:F0}s");
        text.AppendLine($"  multiplier x{_profile.BestMultiplier:F2}      " +
                        $"streak {_profile.BestStreak} (now {_profile.Streak})");
        text.AppendLine($"  crates {_profile.MostCrates}      best throw {_profile.BestThrow}      " +
                        $"bosses {_profile.BestBossKills}");
        text.AppendLine($"  narrowest escape {(_profile.HasNarrowEscape ? $"{_profile.NarrowestEscape:F0} HP" : "—")}" +
                        $"      fastest out {(_profile.HasFastExtraction ? $"{_profile.FastestExtraction:F0}s" : "—")}");
        text.AppendLine();
        text.AppendLine($"  practice   knife {_profile.Proficiency[0]}   long {_profile.Proficiency[1]}   " +
                        $"bow {_profile.Proficiency[2]}   firearm {_profile.Proficiency[3]}");
        text.AppendLine();

        // The sets, with pieces ticked. On the records wall rather than the
        // locker, because this is the one screen that is about what has been done
        // rather than about what to buy — and a collection listed next to a price
        // reads as something to shop for.
        text.AppendLine("CURIOSITIES");

        for (int set = 0; set < CollectionBook.All.Length; set++)
        {
            CollectionBook.Set entry = CollectionBook.All[set];
            bool claimed = _profile.ClaimedSets.Contains(entry.Name);

            text.AppendLine($"  {entry.Name}  {CollectionBook.Found(_profile, set)}/{entry.Pieces.Length}" +
                            (claimed ? "  — paid" : $"  — {entry.Bounty} cr"));

            foreach (string piece in entry.Pieces)
                text.AppendLine($"    [{(_profile.Collected.Contains(piece) ? "x" : " ")}] {piece}");
        }

        text.AppendLine();
        text.AppendLine("  selling one does not lose it — the record is kept at the door");

        return text.ToString();
    }

    private string BoardPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("CONTRACT BOARD");
        text.AppendLine();

        Contract[] offer = _profile.ContractOffer();
        for (int i = 0; i < offer.Length; i++)
        {
            bool taken = _profile.ContractIndex == i;
            text.AppendLine($"{(i == _contractCursor ? " >" : "  ")} {offer[i].Describe(),-34} " +
                            $"{offer[i].Reward,4} cr" + (taken ? "   <- taking this" : ""));
        }

        text.AppendLine();
        text.AppendLine(_profile.HasContract
            ? "  a contract is a promise about how you play, not a bonus"
            : "  none taken — a run without one still pays,\n  it just does not ask anything of you");

        return text.ToString();
    }

    private string MapPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("MAP TABLE");
        text.AppendLine();

        BiomeResource here = BiomeBook.Load(_profile.Biome);
        text.AppendLine($"  heading for  {here.BiomeName}");
        text.AppendLine($"  {here.Blurb}");

        if (BiomeBook.All.Length > 1 && !BiomeBook.Allows(_profile, _profile.Biome + 1))
            text.AppendLine($"  the next opens after {BiomeBook.OpensAt(_profile.Biome + 1)} extractions");

        text.AppendLine();

        DailyRun.Setup today = DailyRun.Today();
        bool done = _profile.DailyDone(today.DateKey);
        int streak = _profile.DailyStreak(today.DateKey);

        text.AppendLine($"  TODAY  {today.DateKey}");
        text.AppendLine($"    {BiomeBook.Load(today.Biome).BiomeName} — {today.Job.Describe()}");
        text.AppendLine(done
            ? $"    done: {_profile.Daily[today.DateKey]} points" +
              (streak > 1 ? $"      {streak} days running" : "")
            : "    one attempt, no credits, no risk");

        return text.ToString();
    }

    private string GatePage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("GATE");
        text.AppendLine();

        BiomeResource here = BiomeBook.Load(_profile.Biome);
        text.AppendLine($"  {here.BiomeName}");
        text.AppendLine(_profile.HasContract
            ? $"  carrying a contract: {_profile.ContractOffer()[_profile.ContractIndex].Describe()}"
            : "  no contract");
        text.AppendLine();
        text.AppendLine("  everything above starting kit is lost if you do not come back");

        return text.ToString();
    }

    /// The right-hand column: what the two keys do here, and nothing else.
    ///
    /// The flat screen listed eight keys permanently, which is a reference card
    /// rather than a prompt — the player read it once and then ignored a fifth of
    /// the screen forever. Two lines that change as you walk get read.
    private string PromptColumn()
    {
        var text = new System.Text.StringBuilder();
        (string title, string first, string second) = Shelter.Prompt(Focus);

        text.AppendLine($"credits {_profile.Credits}");
        text.AppendLine($"stash   {ShopCatalogue.StashValue(_profile)}");
        text.AppendLine();

        if (title.Length == 0)
        {
            text.AppendLine("[W][S] walk   [A][D] turn");
            text.AppendLine();
            text.AppendLine("armoury, locker, records,");
            text.AppendLine("board, map table, gate");
        }
        else
        {
            text.AppendLine(title);
            if (first.Length > 0)
                text.AppendLine($"  [E] {first}");

            // The armoury's second verb changes with the row, so the prompt has
            // to as well — a fitting whose key does two things and says one is
            // worse than one that does nothing.
            if (Focus == Fitting.Armoury && _cursor >= 0 && _cursor < _catalogue.All.Count)
                second = _catalogue.All[_cursor].Slot == null ? "carry as sidearm" : "sell it back";

            if (second.Length > 0)
                text.AppendLine($"  [C] {second}");

            if (Focus is Fitting.Armoury or Fitting.Board)
                text.AppendLine("  up/down choose");
        }

        if (_message.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(_message);
        }

        return text.ToString();
    }

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
