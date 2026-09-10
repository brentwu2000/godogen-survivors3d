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

        BuildPortrait();

        _profile = SaveSystem.Load();

        // The language, before anything is drawn.
        //
        // `Resolve` answers the bootstrap case — a save that has never chosen
        // gets the operating system's language rather than a question, because a
        // player who cannot read English cannot find the room where the setting
        // lives. It also holds English headless whatever the save says, which is
        // what keeps the four probes that read rendered strings measuring the
        // game rather than the translation.
        Strings.Use(Strings.Resolve(_profile.Language));

        _catalogue = new ShopCatalogue();
        _shelter = GetParent()?.GetNodeOrNull<Shelter>("Shelter");

        // **A missing shelter is a broken scene, and it used to be a second
        // screen.** This class carried a whole flat layout for the case — every
        // page's content in two columns at once, plus a `SideColumn` that
        // restated the records, the daily and the contract board — reachable only
        // if `Base.tscn` had no `Shelter` sibling. `BuildBase.cs` adds one
        // unconditionally, so nothing had rendered it for as long as the room has
        // existed, and it was quietly diverging: a phase that changed a page
        // changed the page.
        //
        // Dead code that draws a screen is worse than dead code that does not.
        // It reads as a supported layout, so it asks to be kept working — and the
        // localisation phase would have translated sixty lines of it before
        // anybody asked whether a player can reach it.
        if (_shelter == null)
            GD.PushError("Base.tscn has no Shelter sibling — the base screen has no page to draw");
        else
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
        // The roster takes every key it uses while it is open, and returns
        // rather than falling through: [E] means "take this survivor" here and
        // "buy what the cursor is on" underneath, and a screen that did both on
        // one press would sell something every time somebody chose a character.
        if (_choosing)
        {
            if (Input.IsActionJustPressed("ui_down"))
                PickMove(1);
            else if (Input.IsActionJustPressed("ui_up"))
                PickMove(-1);
            else if (Input.IsActionJustPressed("interact"))
                PickConfirm();
            else if (Input.IsActionJustPressed("interact_second"))
                _choosing = false;
            else
                return;

            Redraw();
            return;
        }

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
            case Fitting.Console: CycleLanguage(); break;

            // Records has nothing to press. Saying so is better than a key that
            // silently does nothing, which the player reads as a bug in the key.
            case Fitting.Records: _message = Strings.Get("msg.records.nothing"); break;

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

            // Who walks out, at the door they walk out of.
            //
            // The armoury would have been the other candidate — the survivor and
            // the loadout are one decision, because the Warden's fourteen bulk
            // changes what is worth buying and the Courier's twenty-eight changes
            // it the other way — but both of the armoury's keys already mean
            // something. The gate is not a consolation: it is the last thing
            // before launching and it is literally the question "who is going".
            case Fitting.Gate: OpenRoster(); break;

            default:
                (_, _, string second) = Shelter.Prompt(Focus);
                if (second.Length == 0)
                    _message = Strings.Get("msg.nothing");

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
            _message = Strings.Get("msg.locked", Strings.Noun(entry.Name), reason);
            return false;
        }

        if (entry.Price <= 0)
        {
            _message = Strings.Get("msg.notforsale", Strings.Noun(entry.Name));
            return false;
        }

        if (_profile.Credits < entry.Price)
        {
            // Nothing is deducted and nothing is granted. A partial purchase is
            // the one outcome a shop must never have.
            _message = Strings.Get("msg.cannotafford", Strings.Noun(entry.Name), entry.Price, _profile.Credits);
            return false;
        }

        _profile.Credits -= entry.Price;
        _profile.Grant(entry.Path);
        _message = Strings.Get("msg.bought", Strings.Noun(entry.Name), entry.Price);
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
            _message = Strings.Get("msg.worn", Strings.Noun(entry.Name));
            return;
        }

        var wanted = GD.Load<WeaponResource>(entry.Path);

        // A two-handed weapon cannot be a sidearm, and saying so is better than
        // quietly putting it there: the slot exists so that a pair is one of each,
        // and a loadout holding two Primaries is the dominance this whole design
        // is built to refuse.
        if (wanted != null && wanted.Slot != WeaponSlot.Sidearm)
        {
            _message = Strings.Get("msg.bothhands", Strings.Noun(entry.Name));
            return;
        }

        if (!TryBuy(entry))
            return;

        // Out of the primary if it was there, or the player would be carrying one
        // weapon in two slots and have nothing to swap to.
        if (_profile.LoadoutWeapon == entry.Path)
            _profile.LoadoutWeapon = "res://resources/weapons/scavenged_rifle.tres";

        _profile.LoadoutSecondary = entry.Path;
        _message = Join(_message, Strings.Get("msg.sidearm", Strings.Noun(entry.Name)));
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
            _message = Strings.Get("msg.notowned", Strings.Noun(entry.Name));
            return;
        }

        if (Profile.IsStartingKit(entry.Path))
        {
            _message = Strings.Get("msg.startingkit", Strings.Noun(entry.Name));
            return;
        }

        bool wasEquipped = IsEquipped(entry);
        int refund = entry.Price / 2;

        if (!_profile.Revoke(entry.Path))
        {
            _message = Strings.Get("msg.cannotsell", Strings.Noun(entry.Name));
            return;
        }

        _profile.Credits += refund;
        _message = Strings.Get("msg.sold", Strings.Noun(entry.Name), refund)
                 + (wasEquipped ? Strings.Get("msg.sold.slot") : "");
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
            _message = Join(_message, Strings.Get("msg.wearing", Strings.Noun(entry.Name)));
            return;
        }

        var weapon = GD.Load<WeaponResource>(entry.Path);
        if (weapon == null)
            return;

        // Where it goes is a fact about the weapon now, not a guess from its
        // category. `IsMelee` was the proxy and it was wrong in the case this
        // design is about: a fire axe is melee and is a two-handed Primary.
        if (weapon.Slot == WeaponSlot.Sidearm)
            _profile.LoadoutSecondary = entry.Path;
        else
            _profile.LoadoutWeapon = entry.Path;

        _message = Join(_message, Strings.Get("msg.carrying", Strings.Noun(entry.Name)));
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
        _message = Strings.Get("msg.contract.took", offer[index].Describe());
        Persist();
    }

    /// A new board, for money. Free rerolls mean spinning until the easiest card
    /// appears, and a job nobody had to weigh is a delayed handout.
    private void Reroll()
    {
        if (_profile.Credits < ContractBook.RerollCost)
        {
            _message = Strings.Get("msg.reroll.poor", ContractBook.RerollCost, _profile.Credits);
            return;
        }

        _profile.Credits -= ContractBook.RerollCost;
        _profile.RollContracts();
        _message = Strings.Get("msg.reroll", ContractBook.RerollCost);
        Persist();
    }

    /// The stash is sold at face value. The extraction multiplier was earned by
    /// walking out with it and is not paid a second time.
    private void SellStash()
    {
        int value = ShopCatalogue.StashValue(_profile);
        if (value <= 0)
        {
            _message = Strings.Get("msg.stash.empty");
            return;
        }

        _profile.Credits += value;
        _profile.Stash.Clear();
        _message = Strings.Get("msg.stash.sold", value);
        Persist();
    }

    // --- The roster screen -----------------------------------------------------
    //
    // **A screen of its own now, and the note this replaces argued against one.**
    // It said choosing a survivor is not a separate act from equipping one —
    // "the Warden's fourteen bulk changes what is worth buying and the Courier's
    // twenty-eight changes it the other way, so the two are one decision made in
    // two rooms" — and that reasoning still holds and is why this opens *at the
    // gate*, on top of the shop screen, and closes back onto it.
    //
    // What changed is what there is to choose between. Cycling was right for
    // three lines of text; there are five survivors with illustrations now, and
    // pressing [C] four times to see the fifth is not a choice, it is a
    // carousel. The same note admitted this about the biomes two paragraphs
    // later: "one more and pressing [B] five times to get back to where you
    // started is worse than a list."

    /// True while the roster is open over the shop screen.
    private bool _choosing;

    /// Which survivor the cursor is on, which is not yet which one is chosen.
    /// Confirming is a keypress, so a player can look at all five without
    /// committing — the previous version changed the choice on every press.
    private int _pick;

    private TextureRect _portrait = null!;
    private ColorRect _portraitBack = null!;

    /// The illustration panel, built in code rather than in `Base.tscn`.
    ///
    /// The scene is hand-authored and a node added to it is a node someone has
    /// to keep in step with this file; the two are one thing. It also sidesteps
    /// the serialisation trap `godot.md` documents, which nothing here would hit
    /// and which has cost this project two days on other nodes.
    ///
    /// Sized and placed against the gap the scene already leaves: `ScreenBack`
    /// ends at x=928 and `SideBack` begins at x=1440, so a 255-wide card centred
    /// in that gap sits between them without moving either.
    private void BuildPortrait()
    {
        _portraitBack = new ColorRect
        {
            Name = "PortraitBack",
            Color = new Color(0.04f, 0.05f, 0.07f, 0.86f),
            OffsetLeft = 1044.0f,
            OffsetTop = 32.0f,
            OffsetRight = 1324.0f,
            OffsetBottom = 608.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };

        _portrait = new TextureRect
        {
            Name = "Portrait",
            // 264 x 560, which is the portraits' own 292 x 619 aspect to
            // within a pixel. `KeepAspectCovered` crops whatever does not fit,
            // and at 254 wide it was cropping four per cent off each side —
            // which is where the name is.
            OffsetLeft = 1052.0f,
            OffsetTop = 40.0f,
            OffsetRight = 1316.0f,
            OffsetBottom = 600.0f,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,

            // Aspect kept and the card cropped rather than squashed. The five
            // portraits are cut from one sheet at one size, so this never
            // actually crops — it is here so that a sixth at a different aspect
            // arrives letterboxed instead of stretched, which is the failure
            // nobody notices in a screenshot.
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };

        // Added to this node, which *is* the panel: `BaseScreen` is the script
        // on `Base.tscn`'s `Panel` Control, not a child of it. Added after
        // `ScreenBack` and `Screen` and therefore drawn over them, which is what
        // an overlay is.
        AddChild(_portraitBack);
        AddChild(_portrait);
    }

    /// Opens the roster at whoever is currently chosen.
    private void OpenRoster()
    {
        _choosing = true;
        _pick = _profile.Character;
        _message = "";
    }

    /// Opens the roster with the cursor somewhere in particular.
    ///
    /// For `BaseShot`, which photographs this screen and cannot press [C]: the
    /// shelter decides which fitting the player is standing at, and a capture
    /// script that had to walk to the gate first would be a capture script
    /// testing the shelter. Public because a screenshot of a screen nobody can
    /// reach is the failure this exists to avoid — the roster went three
    /// versions without a picture of it for exactly that reason.
    public void ShowRoster(int pick)
    {
        _choosing = true;
        _pick = Mathf.Clamp(pick, 0, CharacterBook.All.Length - 1);
        _message = "";
        Redraw();
    }

    /// Moves the cursor, over locked survivors rather than around them.
    ///
    /// A locked entry is shown and is landed on, because "AKIRA opens after 8
    /// extractions" is information and a name that cannot be reached is not.
    /// Confirming on one is what refuses.
    private void PickMove(int step)
    {
        int count = CharacterBook.All.Length;
        _pick = (_pick + step + count) % count;
    }

    /// Takes the survivor under the cursor, or says why not.
    private void PickConfirm()
    {
        if (!CharacterBook.Allows(_profile, _pick))
        {
            CharacterResource locked = CharacterBook.Load(_pick);
            _message = Strings.Get("ui.roster.refused", locked.CharacterName, locked.OpensAfter);
            return;
        }

        _profile.Character = _pick;
        _choosing = false;
        _message = Strings.Get("ui.roster.taken", CharacterBook.Load(_pick).CharacterName);
        Persist();
    }

    /// Puts the roster on the screen and the illustration beside it.
    ///
    /// Loaded on every keypress and that is deliberate: `PortraitPath` is a path
    /// rather than a `Texture2D` in the `.tres` precisely so the roster can be
    /// read without pulling five illustrations into memory, and Godot's resource
    /// cache means the second press of a key costs a dictionary lookup.
    private void DrawRoster()
    {
        CharacterResource shown = CharacterBook.Load(_pick);

        var art = string.IsNullOrEmpty(shown.PortraitPath)
            ? null
            : GD.Load<Texture2D>(shown.PortraitPath);

        // A survivor with no illustration shows no panel rather than an empty
        // one. Five have one; the sixth is the case worth not crashing on.
        if (art == null && !string.IsNullOrEmpty(shown.PortraitPath))
            GD.PushWarning($"BaseScreen: {shown.CharacterName} names {shown.PortraitPath} "
                         + "and it did not load");

        _portrait.Texture = art;
        _portrait.Visible = art != null;
        _portraitBack.Visible = art != null;

        // Greyed rather than hidden, so a locked survivor is a thing the player
        // can see they have not earned. The illustration is the reward as much as
        // the numbers are.
        _portrait.Modulate = CharacterBook.Allows(_profile, _pick)
            ? Colors.White
            : new Color(0.42f, 0.44f, 0.50f);

        _screen.Text = Unix(RosterScreen());
        _side.Text = Unix(PromptColumn());
    }

    /// The roster, drawn into the same label the shop uses.
    ///
    /// Five rows of numbers rather than five paragraphs, because what a player is
    /// comparing at this moment is three numbers and an ability — the blurb says
    /// what the survivor is *for* and belongs on the one under the cursor, not on
    /// all five at once.
    private string RosterScreen()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"{Strings.Get("ui.roster.header")}     "
                      + $"{Strings.Get("ui.roster.keys.look")}     "
                      + $"{Strings.Get("ui.roster.keys.take")}     "
                      + $"{Strings.Get("ui.roster.keys.back")}");
        text.AppendLine();

        CharacterResource[] all = CharacterBook.All;

        for (int i = 0; i < all.Length; i++)
        {
            CharacterResource one = all[i];
            bool open = CharacterBook.Allows(_profile, i);
            bool here = i == _pick;

            // The cursor is a character in the text rather than a colour, for the
            // same reason the shop's is: this label has one font and one colour,
            // and a second colour would be a theme override per line.
            string mark = here ? ">" : " ";
            string tick = i == _profile.Character ? "*" : " ";

            // `Strings.Pad` rather than `{one.Role,-18}`, and the difference is
            // the whole of constraint 2 in `UI.md`. C#'s alignment specifier
            // counts *characters*; a Han glyph occupies two monospace cells, so
            // every column after a translated one shifts by however many hanzi
            // are in it. "近戰／狂戰" is five characters and ten cells.
            text.AppendLine($"{mark}{tick} {i + 1:00}  "
                          + Strings.Pad(one.CharacterName, 6) + " "
                          + Strings.Pad(Strings.Get(one.Role), 18) + " "
                          + (open
                              ? Strings.Get("ui.roster.stats",
                                            $"{one.MaxHealth,3:F0}",
                                            $"{one.MoveSpeed:F1}",
                                            $"{one.CarryCapacity,2}")
                              : Strings.Get("ui.roster.locked", one.OpensAfter)));
        }

        CharacterResource shown = CharacterBook.Load(_pick);

        text.AppendLine();
        text.AppendLine($"   {shown.CharacterName} — {Strings.Get(shown.Blurb)}");
        text.AppendLine(shown.AbilityLine.Length > 0
            ? "   " + Strings.Get("ui.roster.starts", shown.AbilityLine)
            : "   " + Strings.Get("ui.roster.starts.nothing"));

        // The one thing on this screen that is not about choosing.
        //
        // **CC BY 4.0 requires the credit to reach the player, not the
        // repository**, and these five illustrations and the bodies under them
        // are that licence's subject. `ART.md §6` carried this as an unscheduled
        // dependency for three phases on the grounds that the game had no
        // credits surface; the roster screen is where the assets themselves are
        // on display, which makes it the honest place rather than a convenient
        // one. `assets/models/SOURCE.md` carries the full notice.
        if (_message.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"   {_message}");
        }

        text.AppendLine();
        text.AppendLine("   " + Strings.Get("ui.roster.credit.1"));
        text.AppendLine("   " + Strings.Get("ui.roster.credit.2"));

        return text.ToString();
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
            _message = Strings.Get("msg.biome", Strings.Noun(BiomeBook.Load(next).BiomeName));
            Persist();
            return;
        }

        _message = Strings.Get("msg.biome.none");
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
            _message = Strings.Get("msg.daily.done", _profile.Daily[today.DateKey]);
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

        // And who is going. Same reasoning: a probe or a capture script never
        // touches this screen, so it gets index zero — the Drifter, whose numbers
        // are the ones every probe was written against.
        GameSession.Character = _profile.Character;

        Persist();
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    private void Persist() => SaveSystem.Save(_profile);

    private void Redraw()
    {
        if (_choosing)
        {
            DrawRoster();
            return;
        }

        _portrait.Visible = false;
        _portraitBack.Visible = false;

        var text = new System.Text.StringBuilder();

        text.AppendLine(Strings.Get("base.header", _profile.Credits,
                                    ShopCatalogue.StashValue(_profile),
                                    _profile.RunsSurvived, _profile.RunsLost));

        // Practice is listed but has no price. It is the one axis the shop
        // cannot reach, and saying so on the screen is cheaper than a player
        // wondering why it is missing.
        text.AppendLine(Strings.Get("base.practice", _profile.Proficiency[0], _profile.Proficiency[1],
                                    _profile.Proficiency[2], _profile.Proficiency[3],
                                    _profile.Proficiency[4]));

        // Where the run is going, on the shop screen rather than at launch.
        // Terrain has to be known while equipment is being bought, or the
        // loadout could not have been built for it — which is the whole reason
        // the two exist.
        BiomeResource here = BiomeBook.Load(_profile.Biome);
        string more = BiomeBook.All.Length > 1 && !BiomeBook.Allows(_profile, _profile.Biome + 1)
            ? "   " + Strings.Get("base.next", BiomeBook.OpensAt(_profile.Biome + 1))
            : "";

        text.AppendLine(Strings.Get("base.heading", Strings.Noun(here.BiomeName), Strings.Get(here.Blurb), more));

        // Who is going, next to where they are going. The two lines are read
        // together because the choice is made together.
        CharacterResource who = CharacterBook.Load(_profile.Character);

        // `Strings.Get(who.Blurb)` and not `who.Blurb`. **The field holds a key
        // rather than a sentence**, and has since the roster was extracted — the
        // roster screen resolves it and this line was not changed with it, so the
        // base screen has been drawing "playing as RIN — character.rin.blurb"
        // ever since, in English as well as in Chinese.
        //
        // Nothing caught it because nothing could: the string is present and
        // non-empty, `Get` is never called so there is no `«key»` to notice, and
        // the only reader is an eye on the one screen the probes do not render.
        text.AppendLine(Strings.Get("base.playing", who.CharacterName, Strings.Get(who.Blurb)));
        text.AppendLine("             " + Strings.Get("base.stats", $"{who.MaxHealth:F0}",
                                                      $"{who.MoveSpeed:F1}", who.CarryCapacity));

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
            text.AppendLine("     " + Strings.Get("base.above", first));

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

            string state = locked != null ? Strings.Get("base.state.locked")
                : equipped ? Strings.Get("base.state.equipped")
                : owned ? Strings.Get("base.state.owned")
                : entry.Price > 0 ? Strings.Get("base.state.price", entry.Price)
                : Strings.Get("base.state.none");

            string note = locked != null ? $"  {locked}"
                : !Profile.IsStartingKit(entry.Path) && owned ? "  " + Strings.Get("base.note.lost")
                : "";

            // Padded by cells, not by characters. `[已裝上]` is six cells and
            // eight would be counted by `{state,-12}`, so the note column would
            // start in a different place on every translated row.
            text.AppendLine($"{(i == _cursor ? " >" : "  ")} "
                          + Strings.Pad(Strings.Noun(entry.Name), 18) + " " + Strings.Pad(state, 12) + note);
        }

        if (last < _catalogue.All.Count)
            text.AppendLine("     " + Strings.Get("base.below", _catalogue.All.Count - last));

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
        _screen.Text = Unix(Page(text.ToString()));
        _side.Text = Unix(PromptColumn());
    }

    private static string Unix(string text) => text.Replace("\r\n", "\n");

    /// Two things that happened to one key, in one line.
    ///
    /// The separator is a key rather than a literal "; ", because a Chinese
    /// clause joined by a Latin semicolon and a space reads as a typo. It is the
    /// same call the roster made the other way round: the comma between ability
    /// values stayed Latin, because that is a list of numbers and takes the Latin
    /// comma even in Chinese typesetting, while this joins two sentences.
    private static string Join(string first, string second) =>
        first.Length > 0 ? first + Strings.Get("msg.join") + second : second;

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
        Fitting.Console => ConsolePage(),
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
        text.AppendLine(Strings.Get("base.walk.header", _profile.Credits,
                                    ShopCatalogue.StashValue(_profile),
                                    _profile.RunsSurvived, _profile.RunsLost));
        text.AppendLine();
        text.AppendLine("  " + Strings.Get("base.walk"));
        return text.ToString();
    }

    /// The settings fitting, which today has one row.
    ///
    /// Named for what it is rather than for its one current setting, because the
    /// second one always arrives — and a fitting called "LANGUAGE" would have to
    /// be renamed or joined by another the first time anything else is settable.
    ///
    /// Each language is written **in itself**. A player who cannot read the
    /// language the game is currently in cannot find theirs in a list that names
    /// it in that language, which is the same bootstrap problem the OS default
    /// answers one step earlier.
    private string ConsolePage()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine(Strings.Get("ui.console.title"));
        text.AppendLine();

        string[] locales = Strings.AllLocales;
        for (int i = 0; i < locales.Length; i++)
        {
            string mark = locales[i] == Strings.Locale ? ">" : " ";
            text.AppendLine($" {mark} " + Strings.Pad(Strings.NameOf(locales[i]), 16) + locales[i]);
        }

        text.AppendLine();
        text.AppendLine("   " + Strings.Get("ui.console.next"));
        text.AppendLine("   " + Strings.Get("ui.console.hint"));

        return text.ToString();
    }

    /// Steps to the next language and remembers it.
    ///
    /// Persisted immediately rather than on launch: this is a setting rather than
    /// a loadout choice, and a player who changes it and closes the game has
    /// changed it.
    private void CycleLanguage()
    {
        string next = Strings.Next(Strings.Locale);
        Strings.Use(next);
        _profile.Language = Strings.Locale;
        _message = Strings.Get("ui.console.switched", Strings.NameOf(Strings.Locale));
        Persist();
    }

    private string LockerPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.Get("fitting.locker.title"));
        text.AppendLine();

        int worth = ShopCatalogue.StashValue(_profile);
        text.AppendLine("  " + Strings.Get("locker.worth", worth));
        text.AppendLine();

        if (_profile.Stash.Count == 0)
        {
            text.AppendLine("  " + Strings.Get("locker.empty"));
            return text.ToString();
        }

        foreach (var entry in _profile.Stash)
            text.AppendLine("  " + Strings.Get("locker.row", Strings.PadLeft($"{entry.Value}", 3), entry.Key));

        return text.ToString();
    }

    private string RecordsPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.Get("fitting.records.title"));
        text.AppendLine();
        text.AppendLine("  " + Strings.Get("records.best", _profile.BestBank, _profile.BestKills,
                                           $"{_profile.BestSeconds:F0}"));
        text.AppendLine("  " + Strings.Get("records.streak", $"{_profile.BestMultiplier:F2}",
                                           _profile.BestStreak, _profile.Streak));
        text.AppendLine("  " + Strings.Get("records.crates", _profile.MostCrates,
                                           _profile.BestThrow, _profile.BestBossKills));
        text.AppendLine("  " + Strings.Get("records.escape",
            _profile.HasNarrowEscape ? Strings.Get("records.hp", $"{_profile.NarrowestEscape:F0}")
                                     : Strings.Get("base.state.none"),
            _profile.HasFastExtraction ? Strings.Get("records.secs", $"{_profile.FastestExtraction:F0}")
                                       : Strings.Get("base.state.none")));
        text.AppendLine();
        text.AppendLine("  " + Strings.Get("records.practice", _profile.Proficiency[0],
                                           _profile.Proficiency[1], _profile.Proficiency[2],
                                           _profile.Proficiency[3], _profile.Proficiency[4]));
        text.AppendLine();

        // The sets, with pieces ticked. On the records wall rather than the
        // locker, because this is the one screen that is about what has been done
        // rather than about what to buy — and a collection listed next to a price
        // reads as something to shop for.
        text.AppendLine(Strings.Get("records.curiosities"));

        for (int set = 0; set < CollectionBook.All.Length; set++)
        {
            CollectionBook.Set entry = CollectionBook.All[set];
            bool claimed = _profile.ClaimedSets.Contains(entry.Name);

            text.AppendLine("  " + Strings.Get("records.set", Strings.Noun(entry.Name),
                                               CollectionBook.Found(_profile, set), entry.Pieces.Length)
                          + "  " + (claimed ? Strings.Get("records.set.paid")
                                            : Strings.Get("records.set.bounty", entry.Bounty)));

            foreach (string piece in entry.Pieces)
            {
                text.AppendLine("    " + Strings.Get("records.piece",
                    _profile.Collected.Contains(piece) ? "x" : " ", Strings.Noun(piece)));
            }
        }

        text.AppendLine();
        text.AppendLine("  " + Strings.Get("records.keep"));

        return text.ToString();
    }

    private string BoardPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.Get("board.title"));
        text.AppendLine();

        Contract[] offer = _profile.ContractOffer();
        for (int i = 0; i < offer.Length; i++)
        {
            bool taken = _profile.ContractIndex == i;

            // `Strings.Pad` rather than `{x,-34}`. A translated job description
            // is Han and C#'s alignment counts characters, so the reward column
            // would sit somewhere different on every row — the same drift the
            // roster was rebuilt to stop.
            text.AppendLine($"{(i == _contractCursor ? " >" : "  ")} "
                          + Strings.Get("board.row", Strings.Pad(offer[i].Describe(), 34),
                                        Strings.PadLeft($"{offer[i].Reward}", 4))
                          + (taken ? "   " + Strings.Get("board.taking") : ""));
        }

        text.AppendLine();

        // Two lines rather than one string with a newline in it. The English
        // sentence broke where English breaks; a translator needs the break
        // wherever their sentence breaks, and cannot move it inside a cell.
        if (_profile.HasContract)
            text.AppendLine("  " + Strings.Get("board.note"));
        else
        {
            text.AppendLine("  " + Strings.Get("board.none.1"));
            text.AppendLine("  " + Strings.Get("board.none.2"));
        }

        return text.ToString();
    }

    private string MapPage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.Get("fitting.map.title"));
        text.AppendLine();

        BiomeResource here = BiomeBook.Load(_profile.Biome);
        text.AppendLine("  " + Strings.Get("map.heading", Strings.Noun(here.BiomeName)));
        text.AppendLine("  " + Strings.Get(here.Blurb));

        if (BiomeBook.All.Length > 1 && !BiomeBook.Allows(_profile, _profile.Biome + 1))
            text.AppendLine("  " + Strings.Get("map.next", BiomeBook.OpensAt(_profile.Biome + 1)));

        text.AppendLine();

        DailyRun.Setup today = DailyRun.Today();
        bool done = _profile.DailyDone(today.DateKey);
        int streak = _profile.DailyStreak(today.DateKey);

        text.AppendLine("  " + Strings.Get("map.today", today.DateKey));
        text.AppendLine("    " + Strings.Get("map.today.job",
                                             Strings.Noun(BiomeBook.Load(today.Biome).BiomeName),
                                             today.Job.Describe()));
        text.AppendLine(done
            ? "    " + Strings.Get("map.today.done", _profile.Daily[today.DateKey])
              + (streak > 1 ? "      " + Strings.Get("map.today.streak", streak) : "")
            : "    " + Strings.Get("map.today.terms"));

        return text.ToString();
    }

    private string GatePage()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.Get("fitting.gate.title"));
        text.AppendLine();

        BiomeResource here = BiomeBook.Load(_profile.Biome);
        text.AppendLine("  " + Strings.Noun(here.BiomeName));
        text.AppendLine(_profile.HasContract
            ? "  " + Strings.Get("gate.contract",
                                 _profile.ContractOffer()[_profile.ContractIndex].Describe())
            : "  " + Strings.Get("gate.nocontract"));
        text.AppendLine();
        text.AppendLine("  " + Strings.Get("gate.warning"));

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

        text.AppendLine(Strings.Get("prompt.credits", _profile.Credits));
        text.AppendLine(Strings.Get("prompt.stash", ShopCatalogue.StashValue(_profile)));
        text.AppendLine();

        if (title.Length == 0)
        {
            text.AppendLine(Strings.Get("prompt.walk"));
            text.AppendLine();
            text.AppendLine(Strings.Get("prompt.fittings.1"));
            text.AppendLine(Strings.Get("prompt.fittings.2"));
        }
        else
        {
            text.AppendLine(title);
            if (first.Length > 0)
                text.AppendLine("  " + Strings.Get("prompt.first", first));

            // The armoury's second verb changes with the row, so the prompt has
            // to as well — a fitting whose key does two things and says one is
            // worse than one that does nothing.
            if (Focus == Fitting.Armoury && _cursor >= 0 && _cursor < _catalogue.All.Count)
            {
                second = _catalogue.All[_cursor].Slot == null
                    ? Strings.Get("fitting.armoury.sidearm")
                    : Strings.Get("fitting.armoury.second");
            }

            if (second.Length > 0)
                text.AppendLine("  " + Strings.Get("prompt.second", second));

            if (Focus is Fitting.Armoury or Fitting.Board)
                text.AppendLine("  " + Strings.Get("prompt.choose"));
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


    private bool IsEquipped(ShopCatalogue.Entry entry) => entry.Slot is { } slot
        ? _profile.EquippedGear[(int)slot] == entry.Path
        : _profile.LoadoutWeapon == entry.Path || _profile.LoadoutSecondary == entry.Path;
}
