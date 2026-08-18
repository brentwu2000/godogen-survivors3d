using Godot;

/// Plays a whole run the way a person would: pressing movement actions to steer,
/// waiting out the search timers, and walking to the pad.
///
///   godot --headless --script test/AutoPlay.cs
///   godot --headless --script test/AutoPlay.cs -- linger:120
///   godot --headless --script test/AutoPlay.cs -- linger:120 noloot
///   godot --script test/AutoPlay.cs -- shots      (also writes frames)
///   godot --headless --script test/AutoPlay.cs -- profile   (uses the real save)
///
/// The other probes teleport, which proves the systems work but says nothing
/// about whether the loop is reachable at the speeds and distances the game
/// actually uses. This one only ever touches the input layer, so a route that is
/// blocked, a walk that is too slow, or a horde that kills on the way all show
/// up here and nowhere else.
public partial class AutoPlay : SceneTree
{
    private const float ArriveDistance = 1.2f;
    private const float AxisDeadzone = 0.25f;
    private const int LegTimeoutTicks = 60 * 60;   // one minute per leg

    private Player _player = null!;
    private Horde _horde = null!;
    private RunDirector _director = null!;
    private ExtractionZone _extraction = null!;
    private LootContainer[] _crates = System.Array.Empty<LootContainer>();
    private LootContainer[] _allCrates = System.Array.Empty<LootContainer>();

    private Vector3[] _route = System.Array.Empty<Vector3>();
    private string[] _routeLabels = System.Array.Empty<string>();
    private int _leg;
    private int _legTicks;
    private int _tick;
    private bool _bound;
    private bool _wantShots;
    private int _shots;

    private float _lowestHealth = float.MaxValue;
    private int _secured;
    private int _endedState = -1;
    private int _endedBanked;
    private bool _announcedPad;
    private bool _wasLingering;

    /// Seconds of run time to stay alive before heading for the pad. The default
    /// of zero extracts as soon as the route is done; a larger value is how the
    /// difficulty curve gets measured rather than assumed.
    private float _lingerSeconds;
    private int _peakEnemies;
    private float _lastReport;

    private RunGrowth? _growth;
    private WeaponHandler? _weapons;

    /// Run time at which the weapon hit its ceiling, or -1 if it never did. The
    /// design target is around 60% of the run: soon enough that the last stretch
    /// is the horde growing alone, late enough that the climb was most of it.
    private float _ceilingAt = -1.0f;
    private int _picksTaken;

    public override void _Initialize()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _wantShots = System.Array.IndexOf(args, "shots") >= 0;
        _noLoot = System.Array.IndexOf(args, "noloot") >= 0;
        foreach (string arg in args)
        {
            if (arg.StartsWith("linger:") && float.TryParse(arg[7..], out float seconds))
                _lingerSeconds = seconds;

            if (arg.StartsWith("seed:") && ulong.TryParse(arg[5..], out ulong seed))
                _seed = seed;

            // Set before the scene enters the tree, because the level generator
            // and the horde both read GameSession in _Ready — which is the point
            // of it living there rather than on a node.
            if (arg.StartsWith("biome:") && int.TryParse(arg[6..], out int biome))
                GameSession.Biome = biome;
        }

        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // Ephemeral unless asked otherwise, and set before the scene enters the
        // tree because the meta manager reads it in _Ready.
        //
        // Two reasons. A play-test should not spend the player's save — these
        // runs had been banking credits into it. And a balance number measured
        // against whatever practice happens to be on disk is not a balance
        // number: practice moves the starting point, so the same route reads
        // differently on a veteran's profile than on a new one.
        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (System.Array.IndexOf(args, "profile") < 0)
        {
            if (meta != null)
                meta.Ephemeral = true;
            else
                GD.PushWarning("AutoPlay: no MetaManager — the run will use the profile on disk");
        }

        // Take a job, so the run has to satisfy something as well as survive.
        //
        // Contracts had never been driven through a whole run at real speed:
        // every check on them was arithmetic over a hand-built record. That
        // proves a target is judged correctly and says nothing about whether it
        // is reachable — a job asking for twelve brutes is a rule if the run can
        // produce twelve brutes and a joke if it cannot, and only playing one
        // finds out which.
        //
        // "contract:N" takes the Nth card on the board; the board itself is
        // pinned so the run is repeatable.
        _contractIndex = -1;
        foreach (string arg in args)
        {
            if (arg.StartsWith("contract:") && int.TryParse(arg[9..], out int index))
                _contractIndex = index;
        }

        // One layout by default, because a balance number that moves when the
        // map does is not a balance number. "seed:N" walks a different map, and
        // comparing several is how a layout that only works once gets caught.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = _seed;

        GetRoot().AddChild(scene);
    }

    private ulong _seed = 0x51E5D0A7UL;
    private int _contractIndex = -1;
    private MetaManager? _meta;

    public override bool _PhysicsProcess(double delta)
    {
        if (!_bound)
        {
            if (!Bind())
            {
                Quit(1);
                return true;
            }
            _bound = true;
        }

        _tick++;
        _legTicks++;
        _lowestHealth = Mathf.Min(_lowestHealth, _player.Health);
        _peakEnemies = Mathf.Max(_peakEnemies, _horde.Pool.Count);
        TakeGrowthPick();

        // A line every ten seconds of run time: enough to see the curve without
        // burying the result.
        if (_director.Elapsed - _lastReport >= 10.0f)
        {
            _lastReport = _director.Elapsed;
            GD.Print($"  t={_director.Elapsed:F0}s  HP {_player.Health:F0}  enemies {_horde.Pool.Count}  " +
                     $"speed x{_horde.SpeedScale:F2}  bag {_player.Backpack.TotalValue}  " +
                     $"lv {_weapons?.Level ?? 0}/{_weapons?.MaxLevel ?? 0}");
        }

        if (!_player.IsAlive)
        {
            Release();
            GD.Print($"AUTOPLAY FAILED — killed at leg {_leg} ({Label()}) after {_tick / 60.0f:F1}s, " +
                     $"{_horde.Pool.Count} enemies on the field");

            // A death is a balance result, not just a failure. Without the growth
            // line the interesting question — how far up the curve the player got
            // before it stopped mattering — is the one the report leaves out.
            GD.Print(Growth());
            Sweep("Died", _tick / 60.0f, _player.SafeBox.TotalValue);
            Quit(1);
            return true;
        }

        if (_endedState >= 0)
            return Finish();

        if (_leg >= _route.Length)
            return false;   // waiting for the extraction signal

        // Once the crates are done, orbit until the linger deadline instead of
        // extracting immediately — that is what turns this into a difficulty
        // measurement rather than a route check.
        bool lingering = _leg == _route.Length - 1 && _director.Elapsed < _lingerSeconds;

        // Losing beats arriving — but only while the destination is optional.
        //
        // A player at half health with eighteen things on them walks away from a
        // crate. They do not walk away from the *exit*: at that point the crowd
        // is the reason to leave and retreating is choosing to stay in it. The
        // first version had no such distinction, broke contact at 128 s on the
        // way to the pad, and died forty metres from an open extraction it had
        // been walking to.
        //
        // Every balance number in this project comes out of this file, so what
        // the bot cannot do is indistinguishable from what the game does not
        // allow — and the fix belongs here rather than in the difficulty curve.
        bool discretionary = lingering || _leg < _route.Length - 1;
        if (discretionary && ShouldBreakContact())
        {
            Steer(RetreatPoint());
            _retreatTicks++;
            return false;
        }

        Vector3 target = lingering ? OrbitPoint() : _route[_leg];
        float distance = _player.GlobalPosition.DistanceTo(target);

        if (lingering)
        {
            Steer(target);
            _wasLingering = true;
            return false;
        }

        // The leg clock keeps running while orbiting, so without this reset the
        // walk to the pad starts already past its own timeout.
        if (_wasLingering)
        {
            _wasLingering = false;
            _legTicks = 0;
            GD.Print($"  linger over at {_director.Elapsed:F0}s, heading for the pad from " +
                     $"{_player.GlobalPosition.DistanceTo(_route[_leg]):F1}m out");
        }

        if (distance > ArriveDistance)
        {
            Steer(target);

            if (_legTicks > LegTimeoutTicks)
            {
                Release();
                GD.Print($"AUTOPLAY FAILED — could not reach {Label()} in 60s (still {distance:F1}m away)");
                Quit(1);
                return true;
            }

            return false;
        }

        // Standing on the target: stop and let the hold timers run.
        Release();

        if (_leg < _crates.Length)
        {
            if (!_crates[_leg].Looted)
                return false;

            // Secure the best find before moving on — that is the whole point of
            // carrying a safe box rather than trusting the walk home.
            int value = _player.TrySecureBest();
            if (value > 0)
                _secured += value;

            GD.Print($"  looted {Label()} at {_tick / 60.0f:F1}s — bag {_player.Backpack.TotalValue}, " +
                     $"secured {_player.SafeBox.TotalValue}, HP {_player.Health:F0}");
        }
        else
        {
            if (!_announcedPad)
            {
                _announcedPad = true;
                GD.Print($"  reached the pad at {_tick / 60.0f:F1}s, holding");
            }
            return false;   // hold until the zone fires
        }

        _leg++;
        _legTicks = 0;
        return false;
    }

    /// Everything the player does with a key that is not movement: answering a
    /// level-up, spending a consumable, swapping off a dry weapon.
    ///
    /// One tap at a time, pressed on one tick and released on the next, because
    /// IsActionJustPressed needs an edge. Nothing here reaches past the input
    /// layer — a route that cannot be played with the keys is not a route.
    private void TakeGrowthPick()
    {
        if (_ceilingAt < 0.0f && _weapons is { Weapon: not null, AtCeiling: true })
            _ceilingAt = _director.Elapsed;

        if (_weapons is { Weapon.MagazineSize: > 0 })
            _lowestReserve = Mathf.Min(_lowestReserve, _weapons.Reserve);

        if (_weapons?.IsDry == true)
        {
            _everDry = true;
            if (_dryAt < 0.0f)
                _dryAt = _director.Elapsed;
        }

        if (_pickHeld != null)
        {
            Input.ActionRelease(_pickHeld);
            _pickHeld = null;
            return;
        }

        if (_growth is { HasOffer: true })
        {
            TakeCard();
            return;
        }

        // Spend something only when it is both carried and wanted. "Wanted" is
        // not the same as "below the cap": topping a nearly full reserve burns
        // the sale price of a stack to gain nothing, and pressing the key with
        // an empty bag is a no-op that would starve every branch below this one.
        bool hurt = _player.Health < _player.MaxHealth * 0.6f;
        bool lowOnAmmo = _weapons is { Weapon.MagazineSize: > 0 } && _weapons.Reserve < 30;
        if ((hurt || lowOnAmmo) && CarriesUsable())
        {
            Tap("use");
            return;
        }

        // Throw when the crowd is worth a throw. A player who dies holding two
        // pipe bombs has been measured as a player who had no pipe bombs, and
        // the balance table then says the item does nothing.
        if (_player.ThrowableCount > 0 && CrowdedAhead() >= ThrowThreshold)
        {
            Tap("throw");
            _thrown++;
            return;
        }

        // Off a dry weapon, and back onto the gun once there are rounds for it.
        bool holdingMelee = _weapons?.Weapon is { MagazineSize: 0 };
        if (_weapons is { OtherSlotReady: true } && (_weapons.IsDry || holdingMelee))
            Tap("swap");
    }

    private void TakeCard()
    {
        // Weapon first while healthy, survival first when not. A bot that always
        // takes damage measures a player who never notices they are dying, and
        // reports the run as harder than it is.
        bool hurt = _player.Health < _player.MaxHealth * 0.6f;
        int index = hurt
            ? IndexOf(GrowthOption.MaxHealth, GrowthOption.Armour, GrowthOption.WeaponLevel)
            : IndexOf(GrowthOption.WeaponLevel, GrowthOption.Armour, GrowthOption.MaxHealth);

        Tap($"pick_{index + 1}");
        _picksTaken++;

        // Every pick, with what was on the table. Which cards were refused is as
        // much of the balance picture as which were taken — a deck that keeps
        // dealing the same three is a deck the player never really chose from.
        GD.Print($"  pick {_picksTaken} at {_director.Elapsed:F0}s: {_growth!.Offer[index]} " +
                 $"from [{string.Join(", ", _growth.Offer)}]");
    }

    private void Tap(string action)
    {
        _pickHeld = action;
        Input.ActionPress(action);
    }

    /// Direction to walk toward `target`, routed around cover.
    ///
    /// Its own field rather than the horde's: the horde rebuilds around the
    /// player every few ticks, so borrowing it would mean the bot and the
    /// enemies fighting over which way the arrows point.
    private Vector2 Navigate(Vector3 target)
    {
        Vector3 delta = target - _player.GlobalPosition;
        var straight = new Vector2(delta.X, delta.Z).Normalized();

        if (_navField == null)
        {
            _navField = new FlowField(Vector2.Zero, _horde.ArenaExtent, 1.5f);

            // Inflated by roughly a body's radius, so the route it returns is one
            // the player can physically walk rather than one that scrapes every
            // corner and catches on the collision shape.
            Node? obstacles = _player.GetParent()?.GetNodeOrNull("Obstacles");
            if (obstacles != null)
            {
                foreach (Node child in obstacles.GetChildren())
                {
                    if (child is not Node3D body ||
                        body.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is not BoxShape3D box)
                    {
                        continue;
                    }

                    // 0.55, not 0.9. The player's collision radius is 0.35, and
                    // 0.9 was picked for an arena with a dozen widely spaced
                    // blocks where over-inflating cost nothing. In a dense biome
                    // it closes gaps that exist: at a 1.5 m cell size, 0.9 either
                    // side turns a 2.2 m doorway into no doorway, and the bot
                    // reported "could not reach extraction" on a map the player
                    // walks through without touching anything.
                    _navField.BlockBox(
                        new Vector2(body.Position.X, body.Position.Z),
                        new Vector2(box.Size.X * 0.5f + 0.55f, box.Size.Z * 0.5f + 0.55f));
                }
            }
        }

        if (target.DistanceSquaredTo(_navTarget) > 0.01f)
        {
            _navTarget = target;
            _navField.Rebuild(target);
        }

        Vector2 flow = _navField.Sample(_player.GlobalPosition);

        // Zero means the field has no route from here — standing inside an
        // inflated footprint, usually. Straight on is the honest fallback: it is
        // what the bot did before, and it gets it back out of the margin.
        return flow == Vector2.Zero ? straight : flow;
    }

    private FlowField? _navField;
    private Vector3 _navTarget = new(float.MaxValue, 0.0f, float.MaxValue);

    /// Enemies inside the blast if the throw went out now. Counted where the
    /// item would land rather than around the player, because that is the only
    /// number that decides whether throwing is worth its sale price.
    private const int ThrowThreshold = 6;

    private int CrowdedAhead()
    {
        Vector2 aim = _player.Facing == Vector2.Zero ? Vector2.Down : _player.Facing;
        Vector3 landing = _player.GlobalPosition + new Vector3(aim.X, 0.0f, aim.Y) * _player.ThrowRange;

        const float radius = 4.5f;
        int count = 0;

        for (int i = 0; i < _horde.Pool.Count; i++)
        {
            Vector3 delta = _horde.Pool.Position[i] - landing;
            if (delta.X * delta.X + delta.Z * delta.Z < radius * radius)
                count++;
        }

        return count;
    }

    private int _thrown;

    /// Whether the bag holds anything that does something when used. The player
    /// can see this; the bot has to look, or it presses a dead key forever.
    private bool CarriesUsable()
    {
        for (int i = 0; i < _player.Backpack.EntryCount; i++)
        {
            if (_player.Backpack.ItemAt(i).IsUsable)
                return true;
        }

        return false;
    }

    private string? _pickHeld;
    private bool _everDry;
    private float _dryAt = -1.0f;
    private int _lowestReserve = int.MaxValue;

    /// Circles instead of searching during the linger. The two behaviours answer
    /// different questions: looting measures the game with its supply line in
    /// it, and refusing to loot measures how long the starting reserve lasts on
    /// its own — which is the only way to tell whether ammo is a resource.
    private bool _noLoot;

    /// First preference present in the offer, or 0 — something always gets
    /// taken, because the offer does not go away on its own.
    private int IndexOf(params GrowthOption[] preferences)
    {
        foreach (GrowthOption wanted in preferences)
        {
            for (int i = 0; i < _growth!.Offer.Length; i++)
            {
                if (_growth.Offer[i] == wanted)
                    return i;
            }
        }

        return 0;
    }

    /// The growth line of the report. The ceiling time is stated as a fraction
    /// of the run, because the target is a shape — climb for most of it, then
    /// hold while the horde keeps going — and not a number of seconds.
    private string Growth()
    {
        float run = _director.RunSeconds;
        string ceiling = _ceilingAt < 0.0f
            ? "never reached"
            : $"reached at {_ceilingAt:F0}s ({_ceilingAt / run * 100.0f:F0}% of the run, target ~60%)";

        // How close the reserve came to zero is the question, not whether it hit
        // it. A run that never drops below its starting load has ammo that is
        // only money; one that runs down and is refilled by looting has a supply
        // line, which is the point of putting rounds in crates.
        string ammo = _everDry
            ? $"ran dry at {_dryAt:F0}s"
            : $"lowest reserve {(_lowestReserve == int.MaxValue ? 0 : _lowestReserve)}, never dry";

        return $"  growth: level {_growth?.Level ?? 0}, {_picksTaken} picks, " +
               $"weapon {_weapons?.Level ?? 0}/{_weapons?.MaxLevel ?? 0}, ceiling {ceiling}\n" +
               $"  armour {_player.Armour:F0}, speed {_player.MoveSpeed:F2}, " +
               $"search x{_player.SearchSpeed:F2}, max HP {_player.MaxHealth:F0}\n" +
               $"  holding {_weapons?.Weapon?.WeaponName ?? "(none)"} " +
               $"{_weapons?.Ammo ?? 0}/{_weapons?.Reserve ?? 0}, {ammo}\n" +
               $"  threw {_thrown}, still carrying {_player.ThrowableCount}";
    }

    private bool Bind()
    {
        Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
        Player? player = scene.GetNodeOrNull<Player>("Player");
        Horde? horde = scene.GetNodeOrNull<Horde>("Horde");
        RunDirector? director = scene.GetNodeOrNull<RunDirector>("RunDirector");
        Node? crateParent = scene.GetNodeOrNull("LootContainers");

        if (player == null || horde == null || director == null || director.PrimaryPad == null || crateParent == null)
        {
            GD.PushError("AUTOPLAY FAILED — scene is missing a required node");
            return false;
        }

        _player = player;
        _horde = horde;
        _director = director;
        _extraction = director.PrimaryPad!;
        _growth = scene.GetNodeOrNull<RunGrowth>("RunGrowth");
        _weapons = player.GetNodeOrNull<WeaponHandler>("WeaponHandler");
        _meta = scene.GetNodeOrNull<MetaManager>("MetaManager");

        // Taken here rather than in _Initialize: the meta layer loads the profile
        // in its own _Ready, so anything written before the scene enters the tree
        // is overwritten by the time the run starts.
        if (_contractIndex >= 0 && _meta != null)
        {
            _meta.Profile.ContractSeed = 4242;
            Contract[] offer = _meta.Profile.ContractOffer();
            if (_contractIndex < offer.Length)
            {
                _meta.Profile.ContractIndex = _contractIndex;
                GD.Print($"  contract taken: {offer[_contractIndex].Describe()} for {offer[_contractIndex].Reward}");
            }
        }

        var found = new System.Collections.Generic.List<LootContainer>();
        foreach (Node child in crateParent.GetChildren())
        {
            if (child is LootContainer crate)
                found.Add(crate);
        }

        // Two crates then the pad: enough to prove looting is worth a detour
        // without turning the test into a full clear. The rest stay on the map
        // as the supply the linger phase can go and find.
        //
        // Chosen by what they are worth against what they cost to reach, not by
        // tree order. `found[0]` and `found[1]` are whichever two the generator
        // happened to place first, which meant the bot's route was uncorrelated
        // with the value on the map — and the depth bias, which is the entire
        // reason to walk away from the spawn, was invisible to every balance
        // number this file has ever produced. The Flats exists to reward going
        // deep and measured as the worst biome in the game.
        _allCrates = found.ToArray();
        _crates = BestCrates(_player.GlobalPosition, 2);

        var route = new System.Collections.Generic.List<Vector3>();
        var labels = new System.Collections.Generic.List<string>();
        foreach (LootContainer crate in _crates)
        {
            route.Add(crate.GlobalPosition);
            labels.Add(crate.Name);
        }
        route.Add(_extraction.GlobalPosition);
        labels.Add("extraction");

        _route = route.ToArray();
        _routeLabels = labels.ToArray();

        // Extraction is normally gated behind the first stretch of the run; open
        // it so a short test can finish the loop.
        _director.ExtractionOpensAt = 0.0f;
        _director.RunEnded += (state, banked) => { _endedState = state; _endedBanked = banked; };

        GD.Print($"route: {string.Join(" -> ", _routeLabels)}");
        return true;
    }

    private string Label() => _leg < _routeLabels.Length ? _routeLabels[_leg] : "done";

    // ---- what is worth walking to --------------------------------------------

    /// How much a metre of walking is worth giving up in rarity bias.
    ///
    /// The trade the level generator built and nothing was ever making: a crate's
    /// `RarityBias` runs from 1 at the spawn to the biome's depth figure at the
    /// edge — 3.0 in The Flats — and the cost of collecting it is the walk there
    /// and back through whatever is in between.
    ///
    /// 0.01 per metre: a 50 m walk has to buy half a bias step. Measured rather
    /// than guessed — the first value was 0.02 and the bot chose crates at 11 m
    /// with a bias of 1.18 over crates at 45 m with a bias of 1.74, because
    /// 0.02 × 34 m of extra walking is more than the 0.56 of bias it bought. At
    /// 3.4 m/s that walk costs ten seconds of a three-hundred-second run for a
    /// materially better loot table, which is a trade a player takes every time.
    ///
    /// Too high and the bot never leaves the spawn; too low and it crosses the
    /// map for a rounding error and dies on the way.
    private const float BiasPerMetre = 0.01f;

    /// The n crates with the best value for the walk, nearest-first within the
    /// set so the route does not cross itself.
    ///
    /// <param name="announce">
    /// Off for the linger target, which asks this every frame. On for the route,
    /// which asks once and is worth a line in the log — knowing which crates the
    /// bot judged worth the walk is most of reading a balance result.
    /// </param>
    private LootContainer[] BestCrates(Vector3 from, int count, bool announce = true)
    {
        var open = new System.Collections.Generic.List<LootContainer>();
        foreach (LootContainer crate in _allCrates)
        {
            if (!crate.Looted)
                open.Add(crate);
        }

        open.Sort((a, b) => Worth(from, b).CompareTo(Worth(from, a)));

        var chosen = open.GetRange(0, Mathf.Min(count, open.Count));
        chosen.Sort((a, b) => from.DistanceSquaredTo(a.GlobalPosition)
                                 .CompareTo(from.DistanceSquaredTo(b.GlobalPosition)));

        if (announce && chosen.Count > 0)
        {
            var summary = new System.Collections.Generic.List<string>();
            foreach (LootContainer crate in chosen)
            {
                summary.Add($"{crate.Name} (x{crate.RarityBias:F2} at " +
                            $"{from.DistanceTo(crate.GlobalPosition):F0}m)");
            }

            GD.Print($"  worth the walk: {string.Join(", ", summary)}");
        }

        return chosen.ToArray();
    }

    private static float Worth(Vector3 from, LootContainer crate) =>
        crate.RarityBias - from.DistanceTo(crate.GlobalPosition) * BiasPerMetre;

    // ---- breaking contact ----------------------------------------------------

    /// Health below which the bot stops going where it was going.
    ///
    /// Not a panic threshold. At 45% a player still has choices, and the point of
    /// retreating is to spend the seconds *before* the choices run out — a bot
    /// that only ran at 10% would be measuring the same death slightly later.
    private const float RetreatBelow = 0.45f;

    /// And the level it will return to the route at. The gap is what stops the
    /// bot oscillating on the boundary: without it, one healing tick sends it
    /// back into the crowd that just hurt it.
    private const float ResumeAbove = 0.7f;

    private const float ContactRadius = 9.0f;
    private const int ContactCount = 5;

    /// Ticks of retreating before it gives up and presses on regardless.
    ///
    /// A bot that can retreat forever never finishes a run, and a measurement
    /// that never ends is worse than a death — the sweep would hang rather than
    /// report. Twelve seconds is long enough to break contact in the open and
    /// short enough that a cornered bot still resolves.
    private const int RetreatCap = 60 * 12;

    private bool _retreating;
    private int _retreatTicks;

    private bool ShouldBreakContact()
    {
        if (_retreatTicks > RetreatCap)
            return false;

        float fraction = _player.Health / Mathf.Max(1.0f, _player.MaxHealth);

        // Hysteresis, same shape as the music mix and for the same reason: the
        // crowd count around the player is not a smooth number.
        if (_retreating)
        {
            if (fraction >= ResumeAbove || Nearby(ContactRadius) < ContactCount / 2)
            {
                _retreating = false;
                GD.Print($"  back on route at {_director.Elapsed:F0}s, HP {_player.Health:F0}, " +
                         $"{_retreatTicks / 60.0f:F1}s spent breaking contact");
            }

            return _retreating;
        }

        if (fraction < RetreatBelow && Nearby(ContactRadius) >= ContactCount)
        {
            _retreating = true;
            GD.Print($"  breaking contact at {_director.Elapsed:F0}s, HP {_player.Health:F0}, " +
                     $"{Nearby(ContactRadius)} within {ContactRadius:F0}m");
        }

        return _retreating;
    }

    /// Away from where the crowd is, not away from the nearest enemy.
    ///
    /// The nearest one is often the one already touching, and running directly
    /// from it is as likely to run into the rest. The centroid of what is close
    /// is the direction with the fewest things in it.
    private Vector3 RetreatPoint()
    {
        Vector3 at = _player.GlobalPosition;
        Vector3 crowd = Vector3.Zero;
        int count = 0;

        for (int i = 0; i < _horde.Pool.Count; i++)
        {
            Vector3 position = _horde.Pool.Position[i];
            if (position.DistanceSquaredTo(at) < ContactRadius * ContactRadius * 4.0f)
            {
                crowd += position;
                count++;
            }
        }

        if (count == 0)
            return at;

        Vector3 away = at - crowd / count;
        away.Y = 0.0f;

        if (away.LengthSquared() < 0.01f)
            away = new Vector3(1.0f, 0.0f, 0.0f);

        // Clamped inside the arena, or the bot retreats into a wall and stands
        // there being eaten while the probe reports it is moving.
        float extent = _horde.ArenaExtent - 4.0f;
        Vector3 goal = at + away.Normalized() * 14.0f;
        return new Vector3(Mathf.Clamp(goal.X, -extent, extent), 0.0f, Mathf.Clamp(goal.Z, -extent, extent));
    }

    private int Nearby(float radius)
    {
        Vector3 at = _player.GlobalPosition;
        float radiusSqr = radius * radius;
        int count = 0;

        for (int i = 0; i < _horde.Pool.Count; i++)
        {
            if (_horde.Pool.Position[i].DistanceSquaredTo(at) < radiusSqr)
                count++;
        }

        return count;
    }

    /// Where to be while waiting out the timer: the crate most worth the walk
    /// from here, or a wide circuit once they are all empty.
    ///
    /// Looting during the linger is not decoration. Ammo comes out of crates, so
    /// a bot that loots its route once and then circles measures a game where
    /// the reserve can only ever run down — which is a different game from the
    /// one with a supply line in it.
    ///
    /// The nearest crate was the old rule and it is the one thing a player never
    /// does with three minutes to spend. Nearest means never leaving the middle,
    /// and the middle is where the generator puts the cheapest loot in every
    /// biome — so the linger phase, the part of the run that exists to measure
    /// whether staying pays, was systematically collecting the reason it does not.
    private Vector3 OrbitPoint()
    {
        if (_noLoot)
            return Circuit();

        LootContainer[] best = BestCrates(_player.GlobalPosition, 1, announce: false);
        return best.Length > 0 ? best[0].GlobalPosition : Circuit();
    }

    /// Kiting is what a competent player does with nothing left to search, so
    /// measuring difficulty against a stationary target would flatter the design.
    private Vector3 Circuit()
    {
        const float radius = 16.0f;
        float angle = _director.Elapsed * 0.55f;
        return new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
    }

    /// Presses the same digital actions a keyboard would. Anything subtler would
    /// be testing a control scheme the game does not have.
    ///
    /// The direction comes from a flow field of its own, baked from the same
    /// obstacles the horde uses. Straight-line steering was enough on a hand-made
    /// arena with five blocks in known places; on a generated one it walks into
    /// the first wall between it and the crate and reports the route as blocked.
    /// A player looks at the screen and goes around, and the closest thing to
    /// that this project already owns is the field.
    private void Steer(Vector3 target)
    {
        Vector2 direction = Navigate(target);

        Set("move_right", direction.X > AxisDeadzone);
        Set("move_left", direction.X < -AxisDeadzone);
        Set("move_down", direction.Y > AxisDeadzone);
        Set("move_up", direction.Y < -AxisDeadzone);
    }

    private static void Set(string action, bool pressed)
    {
        if (pressed)
        {
            if (!Input.IsActionPressed(action))
                Input.ActionPress(action);
        }
        else if (Input.IsActionPressed(action))
        {
            Input.ActionRelease(action);
        }
    }

    private static void Release()
    {
        foreach (string action in new[]
                 { "move_up", "move_down", "move_left", "move_right", "pick_1", "pick_2", "pick_3" })
        {
            if (Input.IsActionPressed(action))
                Input.ActionRelease(action);
        }
    }

    /// One machine-readable line, printed on every outcome including a death.
    ///
    /// `BalanceSweep` runs this script twenty times and reads these. It could
    /// have re-implemented the bot instead and been much faster, and it would
    /// then be measuring a second bot — the sweep has to be looking at the same
    /// thing a play-test looks at, or the table it prints is about something
    /// nobody plays. Same rule as the reachability check asking a real FlowField.
    private void Sweep(string outcome, float seconds, int banked) =>
        GD.Print($"SWEEP outcome={outcome} seconds={seconds:F1} banked={banked} " +
                 $"lowestHp={_lowestHealth:F0} maxHp={_player.MaxHealth:F0} " +
                 $"peak={_peakEnemies} ended={_horde.Pool.Count} linger={_lingerSeconds:F0} seed={_seed}");

    private bool Finish()
    {
        Release();

        var state = (RunState)_endedState;
        GD.Print($"run: {state} at {_tick / 60.0f:F1}s");
        GD.Print($"  banked {_endedBanked} (secured {_player.SafeBox.TotalValue}, bag {_player.Backpack.TotalValue})");
        GD.Print(Growth());
        GD.Print($"  lowest HP {_lowestHealth:F0}/{_player.MaxHealth:F0}, " +
                 $"enemies at the end {_horde.Pool.Count}, peak {_peakEnemies}");

        if (_meta?.ContractTaken is { } contract)
        {
            RunRecord? run = _meta.LastRun;
            GD.Print($"  contract \"{contract.Describe()}\" for {contract.Reward}: " +
                     $"{(_meta.ContractMet ? "MET" : "failed")}" +
                     (run != null ? $" ({contract.Progress(run)})" : ""));
        }

        Sweep(state.ToString(), _tick / 60.0f, _endedBanked);

        bool ok = state == RunState.Extracted && _endedBanked > 0;
        GD.Print(ok ? "AUTOPLAY OK" : "AUTOPLAY FAILED");
        Quit(ok ? 0 : 1);
        return true;
    }

    public override bool _Process(double delta)
    {
        if (!_wantShots || !_bound || _shots >= 4)
            return false;

        // A frame every four seconds: enough to see the run progress without
        // producing a folder of near-identical stills.
        if (_tick > 0 && _tick % 240 == 0)
        {
            Image image = GetRoot().GetTexture().GetImage();
            image.SavePng(ProjectSettings.GlobalizePath($"res://screenshots/autoplay_{_shots}.png"));
            _shots++;
        }

        return false;
    }
}
