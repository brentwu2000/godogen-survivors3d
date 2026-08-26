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

    /// Within this of the target, routing stops being a question and the straight
    /// line is the answer. See `Navigate`, which is where it earns its keep.
    ///
    /// 2.6 m: past a crate's 1.8 m `SearchRadius` with room for the last step,
    /// and far short of the 7.5 m at which a bot once leaned on a wall — so the
    /// case `EscapeFrom` was added for still reaches it.
    private const float FinalApproach = 2.6f;

    private const int LegTimeoutTicks = 60 * 60;   // one minute per leg

    private Player _player = null!;
    private CameraRig? _rig;
    private Horde _horde = null!;
    private RunDirector _director = null!;
    private ExtractionZone _extraction = null!;
    private LootContainer[] _crates = System.Array.Empty<LootContainer>();
    private LootContainer[] _allCrates = System.Array.Empty<LootContainer>();

    private Vector3[] _route = System.Array.Empty<Vector3>();
    private string[] _routeLabels = System.Array.Empty<string>();

    /// The zone this run will attempt, and which leg of the route it is.
    ///
    /// Opt-in through `--zone`, and that is the whole point of the flag. A zone
    /// is optional in the game, so a bot that always took one and a bot that
    /// never did would each be measuring half a game. Running the same seed both
    /// ways is the only way to answer the question the design actually poses:
    /// what does a zone cost, and does it pay for itself?
    private DangerZone? _zone;
    private int _zoneLeg = -1;

    /// Where the bot was ten seconds ago, and how far it got since. Only a
    /// timeout reads these.
    private Vector3 _stuckFrom;
    private float _stuckDrift;
    private bool _attemptZone;

    /// Which tier of zone to attempt, or -1 for whichever is nearest.
    ///
    /// **This flag is the difference between a measurement and a sample.** Taking
    /// the nearest zone means taking whatever tier the generator happened to put
    /// closest, which is usually tier 0 — so the zone arm of the balance table was
    /// bimodal, two seeds paying heavily and three barely noticing, and the spread
    /// was read as variance in what a zone costs. It was variance in *which zone*.
    ///
    /// A tier that the seed does not contain is reported and the nearest is taken
    /// instead. Falling back silently would put a tier-0 run in the tier-1 column,
    /// which is the same failure one level further down.
    private int _zoneTier = -1;
    private string _weaponWanted = "";
    private string _weaponCarried = "none";

    /// A weapon's name with the spaces out, or `none`. The `SWEEP` line is
    /// whitespace-separated, so a name with a space in it splits into two fields
    /// and every column after it shifts by one.
    private static string Named(WeaponResource? weapon) =>
        weapon == null ? "none" : weapon.WeaponName.Replace(" ", "");
    private bool _soloWeapon;
    private string _gearWanted = "";
    private string _gearWorn = "kit";
    private bool _zoneCleared;

    /// The crate a cleared zone drops, which the bot has to actually pick up.
    ///
    /// Without this the measurement was nonsense in a way that looked like a
    /// balance result: the bot cleared a tier-1 Hold, took the ammunition, walked
    /// past the cache it had just earned, and banked 320 against the 406 it makes
    /// by ignoring the zone entirely. Read as "zones do not pay", when what it
    /// actually showed was a bot leaving the payment on the floor.
    private LootContainer? _zoneCache;

    /// A hold is a minute and the walk there is not free, so the ordinary sixty
    /// second leg timeout would call a working zone a stuck bot.
    private const int ZoneLegTimeoutTicks = 150 * 60;
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

    /// Whether the linger ends on a clock or on how the run is going.
    ///
    /// **The fixed linger is why the balance table cannot price a defensive
    /// weapon.** A run that is calmer is a run a player stays in, and staying is
    /// what turns calm into credits — but the bot leaves at whatever second the
    /// flag says, healthy or not, so the entire value of keeping the field small
    /// is held constant by the measurement. H4c watched the Service Rifle finish
    /// on 93 health against the starting rifle's 70 and bank less than it, which
    /// is not a result about the rifle.
    private bool _lingerAuto;

    /// Leave when health falls to this share of its maximum.
    ///
    /// 0.6 rather than something desperate. The question is when a player decides
    /// a run has stopped going well, and that decision is made with a margin left
    /// — a bot that leaves at 10% is measuring how close to death it can be
    /// steered, which is a different experiment and one nobody plays.
    private float _bailFraction = 0.6f;

    /// Latched. Health comes back — regen, a medkit, the crowd thinning — and a
    /// bot that un-decided every time it did would oscillate between orbiting and
    /// walking to the pad, arriving at neither. Leaving is a decision about the
    /// run, not a state of the health bar.
    private bool _bailed;

    /// The last moment the bot was still out there, which under `linger:auto` is
    /// an outcome rather than an input. -1 means it never got as far as orbiting
    /// — the route was unfinished when the run ended.
    private float _stayedUntil = -1.0f;

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
            if (arg == "linger:auto")
                _lingerAuto = true;
            else if (arg.StartsWith("linger:") && float.TryParse(arg[7..], out float seconds))
                _lingerSeconds = seconds;

            if (arg.StartsWith("bail:") && float.TryParse(arg[5..], out float fraction))
                _bailFraction = Mathf.Clamp(fraction, 0.05f, 0.95f);

            if (arg.StartsWith("seed:") && ulong.TryParse(arg[5..], out ulong seed))
                _seed = seed;

            // Set before the scene enters the tree, because the level generator
            // and the horde both read GameSession in _Ready — which is the point
            // of it living there rather than on a node.
            if (arg.StartsWith("biome:") && int.TryParse(arg[6..], out int biome))
                GameSession.Biome = biome;

            // Who is going. Set here for the same reason the biome is: `Player`
            // reads `GameSession.Character` in its own `_Ready` to build the body
            // and apply the numbers, so anything written after the scene enters
            // the tree is a survivor the run has already started without.
            //
            // By name rather than by index. `CharacterBook.Order` is hand-written
            // and an index into it is not a thing anybody types correctly twice.
            if (arg.StartsWith("character:"))
            {
                string wanted = arg[10..];
                int at = CharacterBook.IndexOf(wanted);

                if (at < 0)
                    GD.Print($"  no survivor named {wanted} — playing the default");
                else
                    GameSession.Character = at;
            }
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
        bool lingering = _leg == _route.Length - 1 && StillWorthStaying();

        // Losing beats arriving — but only while the destination is optional.
        //
        // (`StillWorthStaying` is below; it decides whether the orbit continues.)
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

        Vector3 wanted = lingering ? OrbitPoint() : _route[_leg];
        Vector3 routed = Reachable(wanted);

        // The field owns the route and the physics owns the last two metres.
        //
        // Obstacles are inflated by 0.55 m so the route the field returns is one a
        // body can follow without catching every corner. **That margin is not a
        // wall** — the player's collision radius is 0.35, so ground the field calls
        // blocked is usually ground the player can stand on. On one of the twelve
        // sweep layouts a crate sits 2.4 m inside it, and a bot that stopped where
        // the field stopped stood 0.6 m outside the crate's own 1.8 m reach until
        // something killed it.
        //
        // So once the routed point is reached, steer at what was actually wanted
        // and let the collision shape decide how close that gets. Resolved once
        // either way, because a bot routed toward one point while measuring its
        // distance to another walks to the first and never arrives at the second.
        Vector3 target = _player.GlobalPosition.DistanceTo(routed) <= ArriveDistance ? wanted : routed;
        float distance = _player.GlobalPosition.DistanceTo(target);

        if (lingering)
        {
            Steer(target);
            _wasLingering = true;

            // The last moment it was still out there, written every tick rather
            // than once on the way out. Set only when the orbit *ends* it would
            // stay at -1 for a run that died while lingering, which is a run that
            // stayed as long as it possibly could reported as one that never
            // stayed at all — and the two would land in the same column.
            _stayedUntil = _director.Elapsed;
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
            // How far the bot has actually moved lately, so a timeout can say
            // whether it was blocked or merely slow. "Could not reach it in sixty
            // seconds" is the same sentence for a bot pressed against a wall and
            // one circling the pad two metres out, and they are different bugs.
            if (_legTicks % 600 == 0)
            {
                _stuckDrift = _stuckFrom.DistanceTo(_player.GlobalPosition);
                _stuckFrom = _player.GlobalPosition;
            }

            Steer(target);

            if (_legTicks > (_leg == _zoneLeg ? ZoneLegTimeoutTicks : LegTimeoutTicks))
            {
                Release();
                Vector2 flow = Navigate(target);
                GD.Print($"AUTOPLAY FAILED — could not reach {Label()} in 60s (still {distance:F1}m away), " +
                         $"{_horde.Pool.Count} enemies on the field");
                GD.Print($"  stuck at ({_player.GlobalPosition.X:F1}, {_player.GlobalPosition.Z:F1}) " +
                         $"heading for ({target.X:F1}, {target.Z:F1}); " +
                         $"flow ({flow.X:F2}, {flow.Y:F2}), moved {_stuckDrift:F2} m in the last ten seconds");

                // Where that vector came from, which is the whole diagnosis.
                //
                // `Navigate` has two answers and they fail differently. `Sample`
                // pointing the wrong way is a routing problem; `EscapeFrom`
                // pointing anywhere at all means the bot is standing inside an
                // inflated footprint, and a bot that escapes and is then pulled
                // straight back in is a third thing again — an oscillation, which
                // reads as "stuck" and is not the same bug as either.
                //
                // And whether the *target* is blocked, because a `Rebuild` from a
                // cell inside an obstacle produces a field with no source: every
                // cell unreachable, `Sample` zero everywhere, and the bot walking
                // the straight line into whatever is in the way. That is a
                // generator problem wearing a pathing problem's clothes.
                if (_navField != null)
                {
                    Vector2 escape = _navField.EscapeFrom(_player.GlobalPosition);
                    Vector2 sampled = _navField.Sample(_player.GlobalPosition);

                    GD.Print($"  source: escape ({escape.X:F2}, {escape.Y:F2}), "
                           + $"sample ({sampled.X:F2}, {sampled.Y:F2}); "
                           + $"standing in a footprint = {_navField.IsBlockedAt(_player.GlobalPosition)}, "
                           + $"target in one = {_navField.IsBlockedAt(target)}");
                }

                ReportNearbyCover();

                // A stuck run is a result, not a missing row.
                //
                // It used to leave without a `SWEEP` line, so `BalanceSweep` logged
                // "no result" and dropped it — and the table then read "4/4
                // survived" for an arm in which one of five runs never got home.
                // A failure rate is exactly the kind of thing a balance table is
                // for, and it was the one number it could not show.
                GD.Print(Growth());
                Sweep("Stuck", _tick / 60.0f, _player.SafeBox.TotalValue);
                Quit(1);
                return true;
            }

            return false;
        }

        // Standing on the target: stop and let the hold timers run.
        Release();

        if (_leg == _zoneLeg && _zone != null)
        {
            // Standing in it, which for a Hold is the whole encounter and for the
            // other two is where the weapon can reach what has to die. Nothing
            // else to press: the rifle fires on its own and the zone counts.
            if (_zone.State != DangerZone.ZoneState.Cleared)
            {
                // The zone leg is discretionary, so `ShouldBreakContact` above
                // can still pull the bot out — and a Hold pauses when it does,
                // which is the designed behaviour and worth having in the
                // measurement rather than engineered around.
                if (_legTicks > ZoneLegTimeoutTicks)
                {
                    GD.Print($"  gave up on {_zone.Title} at {_director.Elapsed:F0}s, " +
                             $"{_zone.Progress * 100.0f:F0}% through");
                    _leg++;
                    _legTicks = 0;
                }

                return false;
            }

            if (!_zoneCleared)
            {
                _zoneCleared = true;
                _zoneCache = NearestCache(_zone.GlobalPosition);
                GD.Print($"  cleared {_zone.Title} at {_director.Elapsed:F0}s — " +
                         $"HP {_player.Health:F0}, reserve {_weapons?.Reserve ?? 0}, " +
                         $"cache {(_zoneCache == null ? "missing" : "on the ground")}");
            }

            // Standing on the cache and searching it. The zone drops it where the
            // player is already standing, so there is nowhere to walk to — but it
            // still takes its search seconds, with whatever is left of the wave
            // arriving during them.
            if (_zoneCache is { Looted: false })
            {
                MakeRoomFor(_zoneCache);
                return false;
            }

            if (_zoneCache != null)
            {
                int value = _player.TrySecureBest();
                if (value > 0)
                    _secured += value;

                GD.Print($"  looted the {_zone.Title} cache — bag {_player.Backpack.TotalValue}, " +
                         $"secured {_player.SafeBox.TotalValue}");

                _zoneCache = null;
            }
        }
        else if (_leg < _crates.Length)
        {
            if (!_crates[_leg].Looted)
            {
                // Standing next to a crate that never opens is not a run, and it
                // used to be indistinguishable from one.
                //
                // The walk here is guarded by a leg timeout; **the wait was not.**
                // So a bot parked just outside a crate's 1.8 m reach stood there
                // until something killed it, and the sweep recorded `Died` — a
                // balance result — for a run that never got past its first
                // objective. The stuck path at least says so.
                if (_legTicks > LegTimeoutTicks)
                {
                    Release();
                    float reach = _crates[_leg].SearchRadius + _player.Mods.SearchRadiusBonus;
                    float away = _player.GlobalPosition.DistanceTo(_crates[_leg].GlobalPosition);

                    GD.Print($"AUTOPLAY FAILED — stood at {Label()} for 60s without opening it, "
                           + $"{_horde.Pool.Count} enemies on the field");
                    GD.Print($"  {away:F1} m from it against a reach of {reach:F1} m — "
                           + (away > reach
                               ? "out of range, so the walk stopped short of where it had to stop"
                               : "in range, so the search itself is not completing"));

                    GD.Print(Growth());
                    Sweep("Stuck", _tick / 60.0f, _player.SafeBox.TotalValue);
                    Quit(1);
                    return true;
                }

                MakeRoomFor(_crates[_leg]);
                return false;
            }

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
        // Unchanged, and that is a measured decision rather than an oversight.
        //
        // This list was written in Phase 8 when the deck was five options; pierce,
        // crit, fire rate and area arrived in Phase 18 and it never learned about
        // them. That looks like staleness, and two corrections were tried across
        // eight seeds at a 180 s linger:
        //
        //   original list, random fallback   4 of 8 walk out
        //   damage options as the fallback   3 of 8
        //   damage options first             2 of 8
        //
        // Monotone in the direction of "more damage, fewer survivors", and small
        // — four against three is one seed. Nothing beat the list that was already
        // here, so it stayed. The mechanism is at least coherent: damage converts
        // into survival only if you can use the range it buys, and this bot cannot
        // dodge or kite. It walks to a point and stands there, and its max health
        // came out at 100-124 on the damage-first runs against 136-148 here.
        //
        // A real player's ordering is almost certainly the opposite. That gap is
        // the most useful thing to know about every balance number in this file,
        // and it is worth more written down than papered over.
        bool hurt = _player.Health < _player.MaxHealth * 0.6f;

        int index;
        if (hurt)
        {
            index = IndexOf(GrowthOption.MaxHealth, GrowthOption.Armour, GrowthOption.WeaponLevel);
        }
        else if (_line != GrowthLine.None && FirstOfLine(_line) >= 0)
        {
            // Playing a line, which is the one thing this bot has never been able
            // to do.
            //
            // `GearResource.Favours` makes a line's cards **likelier to be
            // offered** and the list above decides which is **taken**, so every
            // measurement this file has produced about Ordnance or Retinue was
            // really about "a run with a tilted deck and a Phase 8 pick order".
            // H4g went looking for the knot rewarding an Ordnance build and could
            // not put one on the field.
            //
            // The survival override above stays in front of it on purpose. A bot
            // that ignores its health while dying is not measuring a line, it is
            // measuring the bot — and the run ends before the line has compounded
            // into anything.
            //
            // And the weapon card is not refused, it is only outranked: H3
            // excludes weapon level from every line deliberately, so a bot that
            // took nothing but its line would go the whole run on a starting
            // rifle and measure that instead. 94% of offers hold more than one
            // line, so this is a lean rather than a monoculture.
            index = FirstOfLine(_line);
        }
        else
        {
            index = IndexOf(GrowthOption.WeaponLevel, GrowthOption.Armour, GrowthOption.MaxHealth);
        }

        if (_line != GrowthLine.None && RunGrowth.LineOf(_growth!.Offer[index]) == _line)
            _picksInLine++;

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

        EnsureField();

        // A blocked destination has no route to it, so do not ask for one.
        //
        // `Rebuild` seeds its flood from the target's cell; started from a blocked
        // one it produces a field with no source, and `Sample` then hands back
        // whatever is left in the array rather than an honest zero. That is how a
        // bot ended up walking due east toward something due north. Inside the
        // margin the straight line is the right answer — see the final-approach
        // note above.
        if (_navField!.IsBlockedAt(target))
            return straight;

        if (target.DistanceSquaredTo(_navTarget) > 0.01f)
        {
            _navTarget = target;
            _navField.Rebuild(target);
        }

        // Close enough that there is nothing left to route around.
        //
        // **This is the oscillation the note below names and nothing acted on.**
        // `EscapeFrom` outranks the flow whenever the bot stands inside an
        // inflated footprint, which is right when the goal is across the map and
        // wrong when it is two metres away: a crate placed against a prop puts its
        // own approach inside that prop's margin, so escaping means walking away
        // from the thing the bot is trying to touch — and the flow then pulls it
        // straight back in. Neither answer is wrong on its own and together they
        // are a loop.
        //
        // Seed 3432918353 spent sixty seconds doing exactly that: 2.0 m from
        // Crate5 against a reach of 1.8, `sample (0.00, 0.00)`, standing in a
        // footprint, and 5.15 m of travel in the last ten seconds — motion with no
        // progress. It banked nothing, and it did it on **every** arm of every
        // balance table this project has printed, so one layout in twelve has been
        // contributing a zero to the survival column for reasons that had nothing
        // to do with the weapon being measured.
        //
        // Inside this radius the straight line is the answer for the same reason
        // it is for a blocked target: routing is a question about walls, there is
        // no room for one at this range, and the collider resolves any real
        // overlap. Comfortably past a crate's 1.8 m reach so the final step can be
        // taken, and comfortably short of the 7.5 m wall-lean the escape below was
        // written for, so that case still gets escaped from.
        if (delta.LengthSquared() <= FinalApproach * FinalApproach)
            return straight;

        // Out of a wall first, if that is where the bot is standing.
        //
        // "Straight on is the honest fallback" was wrong, and specifically wrong
        // in the one case it was written for. Inside an inflated footprint the
        // target is usually on the *other side* of the thing being stood against,
        // so the straight line points into it — the bot leaned on the south face
        // of an eight-metre wall for sixty seconds, seven and a half metres from
        // the extraction pad, while the sweep recorded the run as having no result
        // at all. `EscapeFrom` is the field's answer to "which way is out".
        //
        Vector2 escape = _navField.EscapeFrom(_player.GlobalPosition);
        if (escape != Vector2.Zero)
            return escape;

        Vector2 flow = _navField.Sample(_player.GlobalPosition);

        // Zero here now means a genuinely unreachable target rather than a body
        // in a margin, and straight on is the honest answer to that.
        return flow == Vector2.Zero ? straight : flow;
    }

    /// The closest point to `wanted` the bot can actually stand on.
    ///
    /// **Used for the distance test as well as the steering, and that is the
    /// whole of why it is a method rather than a line inside `Navigate`.** A bot
    /// routed toward one point while measuring its distance to another walks to
    /// the first and never arrives at the second, which is a timeout that reads
    /// exactly like a blocked route.
    ///
    /// Four metres of search. The crate's own reach is 1.8 m and the grid is 1.5,
    /// so anything the margin swallowed is one or two cells from open ground;
    /// past four the target is not against cover, it is inside it, and walking to
    /// the far side of a building to stand near a crate is not what a player does.
    private Vector3 Reachable(Vector3 wanted)
    {
        EnsureField();
        return _navField!.NearestOpen(wanted, 4.0f);
    }

    private void EnsureField()
    {
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

    /// The first card in the offer belonging to a line, or -1.
    private int FirstOfLine(GrowthLine line)
    {
        for (int i = 0; i < _growth!.Offer.Length; i++)
        {
            if (RunGrowth.LineOf(_growth.Offer[i]) == line)
                return i;
        }

        return -1;
    }

    /// Which growth line the bot plays, or None for the Phase 8 ordering.
    private GrowthLine _line = GrowthLine.None;
    private int _picksInLine;

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

    /// Every obstacle within ten metres of a stuck bot, with its footprint.
    ///
    /// "It did not get there" is not a diagnosis. What decides whether this is a
    /// pathing bug or a level-generation bug is whether the thing in the way is
    /// something the flow field was told about, and the only way to know is to
    /// list what is actually there.
    private void ReportNearbyCover()
    {
        Node? obstacles = _player.GetParent()?.GetNodeOrNull("Obstacles");
        if (obstacles == null)
        {
            GD.Print("  no Obstacles node to inspect");
            return;
        }

        Vector3 at = _player.GlobalPosition;
        int listed = 0;

        foreach (Node child in obstacles.GetChildren())
        {
            if (child is not Node3D body)
                continue;

            float away = new Vector2(body.Position.X - at.X, body.Position.Z - at.Z).Length();
            if (away > 10.0f)
                continue;

            var box = body.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape as BoxShape3D;
            GD.Print($"    {body.Name} at ({body.Position.X:F1}, {body.Position.Z:F1}), " +
                     $"{away:F1} m away, {(box == null ? "no box" : $"{box.Size.X:F1} x {box.Size.Z:F1} m")}");
            listed++;
        }

        if (listed == 0)
            GD.Print("    nothing within ten metres — whatever is in the way is not an obstacle");
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
        _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");
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

        // `GetCmdlineUserArgs`, not `GetCmdlineArgs`. Everything after a bare
        // `--` goes to the first and is *absent* from the second, so a flag
        // written the documented way is silently invisible to the obvious API —
        // no error, no warning, just a run that quietly ignores what it was
        // asked to do. Both are read, because `--zone` before the separator is
        // an equally reasonable thing to type.
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("tier:") && int.TryParse(argument[5..], out int tier))
                _zoneTier = tier;

            if (argument.StartsWith("weapon:"))
                _weaponWanted = argument[7..];

            if (argument.StartsWith("gear:"))
                _gearWanted = argument[5..];

            // The control for "what is a pair worth". `solo` is the game before
            // both slots fired.
            if (argument == "solo")
                _soloWeapon = true;

            if (argument.StartsWith("line:")
                && System.Enum.TryParse(argument[5..], ignoreCase: true, out GrowthLine wantedLine))
            {
                _line = wantedLine;
            }

            if (argument == "--zone")
                _attemptZone = true;
        }

        // What the bot carries into the run.
        //
        // **Every balance number this project has ever printed is about the
        // Scavenged Rifle and the Combat Knife.** A play-test runs on a fresh
        // ephemeral profile — deliberately, because practice moves the starting
        // point and a number measured against whatever is on disk is not a
        // number — and a fresh profile is the starting kit. So the table has
        // never seen a weapon the player bought, and the dominant build H4b was
        // written against was not something it could have shown.
        //
        // Equipped rather than bought. The shop is a separate question and its
        // prices are not what this measures; what is wanted is the run a player
        // has after they have paid, which is this one.
        if (_weaponWanted.Length > 0)
        {
            string path = $"res://resources/weapons/{_weaponWanted}.tres";
            var carried = GD.Load<WeaponResource>(path);

            if (carried == null)
            {
                GD.Print($"  no weapon file named {_weaponWanted} — carrying the starting kit");
            }
            else
            {
                // Through the profile, not straight into the slot.
                //
                // `Equip` alone puts the weapon in the player's hands and leaves
                // the profile saying something else — which was invisible until a
                // weapon started leaning the growth deck, and then every
                // `weapon:` run in the sweep was tilted by whatever the *profile*
                // was carrying. The scythe and the service rifle came back with
                // identical offers, which is the correct output of a flag that
                // was only pretending to change the loadout.
                //
                // Into the slot the weapon's own type asks for, so a Primary
                // cannot land in the sidearm slot the way the shop refuses to put
                // it there.
                if (carried.Slot == WeaponSlot.Sidearm)
                    _meta!.Profile.LoadoutSecondary = path;
                else
                    _meta!.Profile.LoadoutWeapon = path;

                _meta.Profile.Grant(path);
                _weapons?.Equip(carried.Slot == WeaponSlot.Sidearm ? 1 : 0, carried);
            }
        }

        // Read back rather than assumed, and reported in the `SWEEP` line, for
        // the reason `zoneTier` is: a run that could not carry what it was asked
        // to carry must not land in that weapon's column. C3 already paid for
        // this lesson once with a fallback zone tier.
        if (_soloWeapon && _weapons != null)
            _weapons.LiveSlots = 1;

        // **The pair, not the active slot.** `Weapon` means "the one in hand",
        // which is the Primary — so every `weapon:` run naming a Sidearm reported
        // the Scavenged Rifle, and four arms carrying four different Sidearms
        // came back under one identical label. `BalanceSweep.ReportWeapons`
        // suppresses a breakdown of a single name, so the four-way comparison the
        // Sidearm shelf was built to be measured by printed nothing at all and
        // looked like a table that had simply chosen not to say much.
        //
        // Exactly the failure the `zoneTier` note above is about, arriving in the
        // column next door: the read-back has to describe the run, and a loadout
        // is two things now.
        _weaponCarried = $"{Named(_weapons?.WeaponIn(0))}+{Named(_weapons?.WeaponIn(1))}";

        // What it is wearing.
        //
        // The last thing the sweep could not express. A weapon and a survivor
        // were reachable; **a build was not** — and in this game a build is
        // mostly gear, because gear grants rules before the first level-up *and*
        // tilts the deck toward a growth line (`GearResource.Favours`). So one
        // piece is both halves of "play this as an Ordnance run", which is what
        // makes this the argument that unblocks the two questions D2d and H4f had
        // to leave open.
        //
        // Fitted after `AddChild`, never before: `MetaManager._Ready` assigns a
        // fresh `Profile` when it is ephemeral, so anything written earlier is
        // discarded. `TrinketProbe` records the same trap.
        if (_gearWanted.Length > 0 && _meta != null)
        {
            foreach (string name in _gearWanted.Split(','))
            {
                if (name.Length == 0)
                    continue;

                string path = $"res://resources/gear/{name}.tres";
                var piece = GD.Load<GearResource>(path);

                if (piece == null)
                {
                    GD.Print($"  no gear file named {name} — leaving that slot alone");
                    continue;
                }

                _meta.Profile.EquippedGear[(int)piece.Slot] = path;

                // Owned as well as equipped. `ApplyGear` skips a slot naming a
                // piece the profile does not own, because a piece lost on the
                // last run is still named in the slot until the base screen
                // replaces it — so equipping without granting is a loadout that
                // silently does nothing.
                _meta.Profile.Grant(path);
            }

        }

        // One re-apply for the gear *and* the weapons, after both have been
        // written into the profile.
        //
        // `ApplyGear` adds rather than assigns — health, armour, carry — so
        // calling it once per flag would give a run wearing a Plate Carrier and
        // carrying a rifle fifty health instead of twenty-five. It also clears
        // the deck's lean at the top, so the weapon half of that lean has to be
        // written before it runs rather than after.
        if (_meta != null && (_gearWanted.Length > 0 || _weaponWanted.Length > 0))
        {
            _player.Mods.Reset();
            _meta.ReapplyGearForTesting();
        }

        // Read back off the profile, same rule again.
        var worn = new System.Collections.Generic.List<string>();
        foreach (string path in _meta?.Profile.EquippedGear ?? System.Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(path))
                continue;

            var piece = GD.Load<GearResource>(path);
            if (piece != null && piece.Tier > 1)
                worn.Add(piece.GearName.Replace(" ", ""));
        }

        _gearWorn = worn.Count > 0 ? string.Join("+", worn) : "kit";

        foreach (string argument in OS.GetCmdlineArgs())
        {
            if (argument == "--zone")
                _attemptZone = true;
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
        if (_attemptZone)
        {
            Vector3 from = _crates.Length > 0
                ? _crates[^1].GlobalPosition
                : _player.GlobalPosition;

            _zone = NearestZone(scene, from, _zoneTier);

            if (_zoneTier >= 0 && _zone != null && _zone.Tier != _zoneTier)
            {
                GD.Print($"  no tier {_zoneTier} zone on this seed — taking tier {_zone.Tier}");
            }

            if (_zone != null)
            {
                _zoneLeg = route.Count;
                route.Add(_zone.GlobalPosition);
                labels.Add(_zone.Title);
                GD.Print($"  attempting {_zone.Title}: tier {_zone.Tier}, " +
                         $"pays {_zone.Rolls} rolls + {_zone.Rounds} rounds");
            }
            else
            {
                GD.Print("  --zone given but the map has none");
            }
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

    /// Throws away the worst thing carried while a crate still has something.
    ///
    /// The bot has to be able to make the decision the game now asks for. A crate
    /// keeps what would not fit, so with a full backpack `Looted` never becomes
    /// true and the leg waits for something that will not happen — the bot stood
    /// at crates until the leg timeout, reached the zone eighteen seconds late
    /// with an empty reserve, and died. It was a bot that could not play the game
    /// rather than a game that was too hard.
    ///
    /// One unit per tick, not a loop. Dropping is a keypress and the player can
    /// only make one a frame; a bot that emptied its bag instantly would measure
    /// a game nobody can play, which is the failure this whole file exists to
    /// avoid.
    ///
    /// It stops when the crate is worth less per bulk than what is being carried
    /// — otherwise the bot would throw away a circuit board for a box of rounds
    /// and call it progress.
    private void MakeRoomFor(LootContainer crate)
    {
        if (crate.RemainingBulk <= 0 || _player.Backpack.FreeBulk > 0)
            return;

        int worst = _player.Backpack.LeastValuableIndex();
        if (worst < 0)
            return;

        ItemResource carried = _player.Backpack.ItemAt(worst);
        float carriedRate = carried.Value / (float)Mathf.Max(1, carried.Bulk);
        float waitingRate = crate.RemainingValue / (float)crate.RemainingBulk;

        if (waitingRate <= carriedRate)
            return;

        _player.TryDropWorst();
    }

    /// The unlooted crate closest to a point, within a few metres of it.
    ///
    /// By position rather than by name, because the cache is created by the zone
    /// at the moment it clears and the bot has no reference to it. A radius
    /// rather than "the nearest": if the zone somehow dropped nothing, the
    /// nearest crate is one on the far side of the map and the bot would walk off
    /// to it in the middle of a fight.
    private LootContainer? NearestCache(Vector3 at)
    {
        LootContainer? best = null;
        float bestDistance = 6.0f;

        Node? crates = _player.GetParent()?.GetNodeOrNull("LootContainers");
        foreach (Node child in crates?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (child is not LootContainer crate || crate.Looted)
                continue;

            float distance = at.DistanceTo(crate.GlobalPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = crate;
        }

        return best;
    }

    /// The zone nearest to where the looting ends.
    ///
    /// Nearest rather than richest. The tiers differ by less than the walk does
    /// on a 55 metre map, and a bot that crossed the whole arena for one extra
    /// loot roll would be measuring the walk rather than the zone.
    /// The nearest zone, preferring one of `tier` if the seed has one.
    ///
    /// Two passes rather than one with a filter: the fallback has to be the
    /// nearest zone *of any tier*, and a single pass that skipped the wrong tier
    /// would return nothing at all on a seed that happens to have none.
    private static DangerZone? NearestZone(Node scene, Vector3 from, int tier = -1)
    {
        DangerZone? best = null;
        DangerZone? bestOfTier = null;
        float bestDistance = float.MaxValue;
        float bestOfTierDistance = float.MaxValue;

        foreach (Node child in scene.GetNodeOrNull("DangerZones")?.GetChildren()
                               ?? new Godot.Collections.Array<Node>())
        {
            if (child is not DangerZone zone)
                continue;

            float distance = from.DistanceTo(zone.GlobalPosition);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = zone;
            }

            if (zone.Tier == tier && distance < bestOfTierDistance)
            {
                bestOfTierDistance = distance;
                bestOfTier = zone;
            }
        }

        return bestOfTier ?? best;
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
        // Re-read the node rather than the list captured at bind time. Two supply
        // caches and the boss's reward are added to the tree mid-run, and they are
        // the three richest crates on the map — a bot working from the opening
        // census walks past all of them and measures a game without them in it.
        var open = new System.Collections.Generic.List<LootContainer>();
        Node? crates = _player.GetParent()?.GetNodeOrNull("LootContainers");
        if (crates != null)
        {
            foreach (Node child in crates.GetChildren())
            {
                if (child is LootContainer { Looted: false } crate)
                    open.Add(crate);
            }
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

    /// Past this much of the clock the bot leaves whatever its health is.
    ///
    /// Not a difficulty rule — a deadline rule. Extraction needs a five second
    /// hold and the pad can be fifty metres away, so a bot that only ever left on
    /// health would spend the tail of a good run walking and time out on the pad
    /// with the run already over. 0.8 of a 300 second clock leaves a minute.
    private const float LingerCeiling = 0.8f;

    /// Whether the orbit continues.
    ///
    /// Two modes, and the second exists because the first cannot price a weapon
    /// that sells safety. `linger:60` is the original: orbit until the clock says
    /// stop, which measures how much a run is worth after exactly sixty seconds
    /// of pressure. `linger:auto` measures something a player actually decides —
    /// stay while the run is going well, leave when it stops.
    ///
    /// The difference matters most for exactly the weapons the table read worst.
    /// A run that keeps the field below the cap is a run a player stays in, and
    /// under a fixed linger that advantage has nowhere to go: it comes back as
    /// health left over at the exit, which no column converts into anything.
    private bool StillWorthStaying()
    {
        if (!_lingerAuto)
            return _director.Elapsed < _lingerSeconds;

        if (_bailed)
            return false;

        if (_director.Intensity >= LingerCeiling)
        {
            _bailed = true;
            GD.Print($"  leaving at {_director.Elapsed:F0}s — out of clock, "
                   + $"{_player.Health:F0}/{_player.MaxHealth:F0} health");
            return false;
        }

        if (_player.Health <= _player.MaxHealth * _bailFraction)
        {
            _bailed = true;
            GD.Print($"  leaving at {_director.Elapsed:F0}s — down to "
                   + $"{_player.Health:F0}/{_player.MaxHealth:F0} health");
            return false;
        }

        return true;
    }

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

        // The boss is deliberately *not* a target here, and that is a limitation
        // rather than a decision.
        //
        // It guards the richest thing in the run — a cache biased at 3.2 — so the
        // reward for staying past 40% of the clock is behind it, and this bot
        // cannot collect it. Three versions were tried and all three were worse
        // than ignoring it: walk to the boss (dead in twenty seconds, because it
        // does 26 contact damage a second and the target was its exact position);
        // hold at 13 m (dead anyway, because it arrives at the same moment the
        // horde reaches its cap); engage only while healthy with fewer than 25
        // things close (turned a seed that banked 1735 into a death at 114 s).
        //
        // Fighting it wants kiting and cover, which is real combat AI rather than
        // a target-selection rule. So the boss cache is content no measurement in
        // this file can reach, and the payout for staying past two minutes is
        // *unverified* rather than verified-as-bad. Written down instead of
        // patched until the number came out right.

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
    ///
    /// The four-key decomposition that used to live here is gone: under
    /// turn-and-advance the horizontal keys turn the view rather than strafing,
    /// so a direction no longer decomposes into independent axes. `BotDrive`
    /// owns the conversion and the reason.
    private void Steer(Vector3 target) => BotDrive.Steer(Navigate(target), _rig?.Yaw ?? 0.0f);

    private static void Release()
    {
        BotDrive.Release();

        foreach (string action in new[] { "pick_1", "pick_2", "pick_3" })
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
                 $"peak={_peakEnemies} ended={_horde.Pool.Count} " +

                 // Under `linger:auto` the linger is an outcome, so what is
                 // reported is how long it *stayed*, not what it was told. -1
                 // means it never got as far as orbiting — the route was still
                 // unfinished when the run ended, and averaging that in with a
                 // deliberate departure would be a third thing in one column.
                 $"linger={(_lingerAuto ? _stayedUntil : _lingerSeconds):F0} " +
                 $"stayed={_stayedUntil:F0} " +
                 $"zone={(_zone == null ? "none" : _zoneCleared ? _zone.Title.Replace(" ", "") : "failed")} " +

                 // The tier actually attempted, not the tier asked for. A sweep
                 // that grouped by the request would put a fallback run in the
                 // wrong column and the table would be wrong in exactly the way
                 // the flag was added to fix.
                 $"zoneTier={(_zone?.Tier ?? -1)} " +

                 // The weapon the run actually started with, on the same rule.
                 $"weapon={_weaponCarried} slots={_weapons?.LiveSlots ?? 0} gear={_gearWorn} " +

                 // And who played it. Read back from the book rather than echoed
                 // from the flag, so a name that did not resolve lands in the
                 // Drifter's column where the run actually happened.
                 $"character={CharacterBook.Load(GameSession.Character).CharacterName.Replace(" ", "")} " +

                 // How this run's crowd arrived: the share drawn, and how many
                 // knots actually landed. Both, because a share of 0.30 that sent
                 // zero knots is a feature that is configured and does nothing —
                 // the failure this project has found in a shockwave nobody could
                 // see and a touch layer nobody had ever executed.
                 $"knotShare={_director.PlannedKnotShare:F2} knots={_director.KnotsSent} " +

                 // The line asked for and how much of the deck actually went into
                 // it. Both, because "played Ordnance" and "was offered Ordnance
                 // twice in twenty picks" are different runs and the second one
                 // is not evidence about a line.
                 $"line={_line} inLine={_picksInLine} " +

                 // When the gun first had nothing left, or -1 for never.
                 //
                 // Tracked since the reserve was tuned and printed only in the
                 // run's own summary, so no table has ever carried it. It is the
                 // one column that can price a melee weapon: what melee buys is
                 // that it cannot run out, and whether that is worth anything
                 // depends entirely on whether a firearm ever does. The reserve
                 // is calibrated to empty if and only if the player stops
                 // looting, and this bot always loots — so the advantage may be
                 // one the design has already neutralised, which is a finding
                 // rather than a guess only once it is a number.
                 $"dryAt={_dryAt:F0} " +
                 $"level={_growth?.Level ?? 0} picks={_picksTaken} " +
                 $"weaponLv={_weapons?.Level ?? 0} weaponMax={_weapons?.MaxLevel ?? 0} " +
                 $"ceilingAt={(_ceilingAt < 0.0f ? -1.0f : _ceilingAt):F0} " +
                 $"seed={_seed}");

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
