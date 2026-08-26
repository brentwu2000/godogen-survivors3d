using Godot;

/// How much of the growth deck each weapon can actually spend.
///
///   godot --headless --script test/DeckMatrix.cs
///
/// Exit code is the verdict, but this is an **instrument, not a probe**, and it
/// is on `sweep.ps1`'s skip list for the same reason `BalanceSweep` is: every
/// weapon fired under every option is two hundred and seventy-six trials of four
/// seconds each, and it takes about a quarter of an hour. Run it when a weapon
/// is added, when a growth option is added, and when a trait changes what a
/// weapon consumes. See WEAPONS.md.
///
/// WEAPONS.md step 4 asked for this by name: "the stage that counts how many
/// growth options measurably move each weapon's output. It is the stage that
/// would have caught the melee gap two phases before the balance table did."
///
/// The gap it means: `Pierce` does nothing for a blade, and neither does spread,
/// reload or projectile speed. A weapon that cannot spend a third of the deck is
/// not balanced by its stat line, it is balanced by a stat line plus a tax the
/// table cannot see — and the balance table found that by playing four hundred
/// runs, which is an expensive way to learn that a card was a blank.
///
/// **Measured by firing, not by reading the code.** A list in here of "which
/// modifiers a melee weapon consults" is the same list `WeaponHandler` already
/// holds, written twice — and the copy in the test is the one that goes stale in
/// the direction that hides the bug. So each trial equips the weapon, applies the
/// option, and lets the real firing path run at a wall.
///
/// **Three things are invisible here by construction, and the printout must be
/// read knowing it.** Nothing on the wall can die, so `Detonate` — a chance to
/// explode on a kill — can never register. The wall is frozen, so `Chill` has no
/// speed to take. And the player is not the subject, so health, armour, move
/// speed, search, dodge, thorns, regen, lifesteal, reach and fortune are
/// *supposed* to be silent. All ten are named in the "moved no weapon" line every
/// run, and it is a name **missing** from that line that is the finding, not a
/// name present in it.
public partial class DeckMatrix : SceneTree
{
    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private RunGrowth? _growth;
    private RunKit? _kit;

    /// How long each trial fires for.
    ///
    /// Four seconds, and the length is load-bearing. At ninety ticks the Fire Axe
    /// came back unable to use `FireRate`: its swing is 1.176 s, so a window of a
    /// second and a half fits two swings at any rate the deck can buy, and the
    /// matrix reported the slowest weapons in the game as immune to the card that
    /// speeds them up. The window has to be long enough that a rate change lands
    /// on a different *count* of attacks for the slowest weapon in the table, not
    /// just a different schedule.
    private const int TrialTicks = 240;

    /// How many of an option each trial takes. Three, because the question is
    /// "can the deck move this weapon at all" and one pick of a rare option is
    /// small enough to lose in the noise of a single reload. Deliberately past
    /// some ceilings: `GrantForTesting` does not consult `IsAvailable`, so this
    /// overstates magnitude and cannot overstate *whether*.
    private const int TrialStacks = 3;

    /// The fraction of the baseline a trial has to move to count as moving it.
    private const float MovedBy = 0.02f;

    /// Health handed to every target so nothing on the wall can die.
    ///
    /// A death swap-removes, which would make the damage total a sum over a set
    /// that changed size mid-trial — and it would change size at a different
    /// moment in every trial, which is a measurement of who died rather than of
    /// what the option did.
    private const float Indestructible = 1_000_000.0f;

    /// The common attack floor in the shipped deck: proficiency, crit, rate and
    /// chain. The matrix above that is diagnostic, not a demand that a defensive,
    /// search or loot card alter a weapon's damage. An earlier floor of eight
    /// failed the Bolt Launcher for correctly refusing penetration, which is a
    /// design assertion the deck does not make.
    private const int DeckFloor = 4;

    private WeaponResource[] _deck = System.Array.Empty<WeaponResource>();
    private GrowthOption[] _options = System.Array.Empty<GrowthOption>();
    private int _deckWeapon;
    private int _deckOption = -1;
    private int _trialStart;
    private float _trialBaseline;
    private int _deckMinimum = int.MaxValue;
    private string _deckWorst = "";
    private int _deckBest = -1;
    private string _deckRichest = "";
    private bool _deckOk = true;
    private int _tick;
    private bool _started;

    private readonly System.Collections.Generic.List<string> _movers = new();
    private readonly System.Collections.Generic.HashSet<GrowthOption> _everMoved = new();

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        _tick++;

        if (!_started)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Player>("Player");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
            _growth = scene.GetNodeOrNull<RunGrowth>("RunGrowth");
            _kit = scene.GetNodeOrNull<RunKit>("RunKit");

            if (_horde == null || _player == null || _weapons == null || _growth == null || _kit == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null} " +
                             $"weapons={_weapons != null} growth={_growth != null} kit={_kit != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            _deck = LoadWeapons();
            _options = System.Enum.GetValues<GrowthOption>();

            if (_deck.Length == 0)
            {
                GD.PushError("PROBE FAILED — no weapons loaded; run BuildWeapons.cs");
                Quit(1);
                return true;
            }

            // Still, so a trial measures the weapon rather than how far the wall
            // walked into its reach.
            _horde.SpeedScale = 0.0f;
            _weapons.LiveSlots = 1;

            // And the routing off with it. The flow field rebuilds across the
            // whole arena every eighth tick and this run is tens of thousands of
            // ticks long, so it would otherwise spend a large part of its life
            // computing routes for a wall that has been told not to move. Nothing
            // here reads a route, and `SpeedScale` guarantees nobody could follow
            // one.
            _horde.FieldRebuildInterval = int.MaxValue;

            GD.Print($"{_deck.Length} weapons x {_options.Length} options, "
                   + $"{TrialStacks} stacks each, {TrialTicks} ticks a trial");

            // The tick *after* this one, so the first trial's setup lands on an
            // `into` of zero. Off by one here and `BeginTrial` is never reached at
            // all: every trial reports zero damage, every option looks inert, and
            // the run ends with a table of noughts that reads like a finding
            // rather than like a bug. That happened.
            _trialStart = _tick + 1;
            _started = true;
            return false;
        }

        int into = _tick - _trialStart;

        if (into == 0)
        {
            BeginTrial();
            return false;
        }

        if (into < TrialTicks)
            return false;

        float dealt = DamageDealt();

        if (_deckOption < 0)
        {
            _trialBaseline = dealt;
        }
        else if (_trialBaseline > 0.0f
                 && Mathf.Abs(dealt - _trialBaseline) / _trialBaseline > MovedBy)
        {
            _movers.Add(_options[_deckOption].ToString());
            _everMoved.Add(_options[_deckOption]);
        }

        _deckOption++;

        if (_deckOption >= _options.Length)
        {
            ReportWeapon();
            _deckOption = -1;
            _deckWeapon++;
        }

        if (_deckWeapon >= _deck.Length)
        {
            bool ok = Finish();
            GD.Print(ok ? "PROBE OK" : "PROBE FAILED");
            Quit(ok ? 0 : 1);
            return true;
        }

        _trialStart = _tick + 1;
        return false;
    }

    /// Sets the field up for one (weapon, option) pair and starts it firing.
    private void BeginTrial()
    {
        WeaponResource weapon = _deck[_deckWeapon];

        _player!.Mods.Reset();
        _growth!.ResetForTesting();

        // Resetting the modifiers removes the cards but cannot rewind the kit's
        // own clocks, so without this a short trial inherits part of the previous
        // trial's orbit or pulse interval — and whether a card appears to deal
        // damage depends on which weapon happened to be measured before it.
        _kit!.ResetForTesting();

        _weapons!.Equip(0, weapon);
        _weapons.SetProficiency(weapon.Category, 0);

        // The same random stream every trial, so the difference between two of
        // them is the option and not the draw. See `ReseedForTesting`.
        _weapons.ReseedForTesting(0xD1B54A32D192ED03UL);
        _weapons.Projectiles.Clear();
        _weapons.HoldFire = false;

        if (_deckOption >= 0)
        {
            for (int i = 0; i < TrialStacks; i++)
                _growth.GrantForTesting(_options[_deckOption]);
        }

        _horde!.Pool.Clear();

        // A ladder rather than a cluster, so every reach in the table has
        // something to hit: 1.2 m is inside a knife, 13 m is inside a pistol but
        // outside a shotgun. Spaced wider than the separation force so a wall
        // that cannot move is not quietly pushing itself apart either.
        float[] lane = { 1.2f, 2.4f, 4.0f, 7.0f, 10.0f, 13.0f };
        foreach (float x in lane)
            _horde.Spawn(_player.GlobalPosition + new Vector3(x, 0.0f, 0.0f), 0);

        for (int i = 0; i < _horde.Pool.Count; i++)
            _horde.Pool.Health[i] = Indestructible;
    }

    /// What the wall has lost since the trial began.
    private float DamageDealt()
    {
        float dealt = 0.0f;
        for (int i = 0; i < _horde!.Pool.Count; i++)
            dealt += Indestructible - _horde.Pool.Health[i];

        return dealt;
    }

    private void ReportWeapon()
    {
        WeaponResource weapon = _deck[_deckWeapon];
        int moved = _movers.Count;

        GD.Print($"  {weapon.WeaponName} ({weapon.Category}): {moved}/{_options.Length} — "
               + string.Join(" ", _movers));

        if (moved < _deckMinimum)
        {
            _deckMinimum = moved;
            _deckWorst = weapon.WeaponName;
        }

        if (moved > _deckBest)
        {
            _deckBest = moved;
            _deckRichest = weapon.WeaponName;
        }

        if (moved < DeckFloor)
        {
            GD.PushError($"  {weapon.WeaponName} responds to {moved} of {_options.Length} options — "
                       + $"the shared attack floor is {DeckFloor}");
            _deckOk = false;
        }

        _movers.Clear();
    }

    private bool Finish()
    {
        GD.Print($"thinnest deck: {_deckWorst} at {_deckMinimum} of {_options.Length}; "
               + $"richest: {_deckRichest} at {_deckBest}");

        // The spread between them, which is the thing step 4 actually asked for.
        // An absolute floor says "every weapon can be upgraded at all"; the melee
        // gap was never that, it was that one *class* of weapon could spend much
        // less of the same deck than its neighbours while the balance table
        // priced them as equals. A ratio catches that, and it catches it whichever
        // direction the deck grows in — a new card only firearms can use widens
        // this without changing anybody's absolute count.
        //
        // Half, because the deck is not obliged to serve every weapon equally.
        // Below half, the thing being sold as a choice of weapon is partly a
        // choice of how much of the run's growth to opt out of, and the shop does
        // not say so anywhere.
        bool balanced = _deckBest <= 0 || _deckMinimum * 2 >= _deckBest;
        if (!balanced)
        {
            GD.PushError($"{_deckWorst} responds to {_deckMinimum} options where {_deckRichest} "
                       + $"responds to {_deckBest} — less than half the same deck, which is the "
                       + $"melee gap in the shape it was found in");
        }

        // And the other direction: an option that moves *nothing* is a card that
        // is drawn, taken, and does not exist. This is the only place with the
        // whole matrix in front of it, so it is the only place that can ask. Read
        // it against the three deliberate blind spots at the top of this file.
        var silent = new System.Collections.Generic.List<string>();
        foreach (GrowthOption option in _options)
        {
            if (!_everMoved.Contains(option))
                silent.Add(option.ToString());
        }

        GD.Print($"moved no weapon at all: {(silent.Count > 0 ? string.Join(" ", silent) : "none")}");
        return _deckOk && balanced;
    }

    /// The weapon table, from the directory rather than from a list. A
    /// hand-written list of a growing thing's members goes stale in the direction
    /// that hides the bug — this exists because a whole class of weapon was
    /// under-served and nobody noticed for two phases.
    private static WeaponResource[] LoadWeapons()
    {
        var loaded = new System.Collections.Generic.List<WeaponResource>();

        using var directory = DirAccess.Open("res://resources/weapons");
        if (directory == null)
            return loaded.ToArray();

        foreach (string file in directory.GetFiles())
        {
            if (!file.EndsWith(".tres") && !file.EndsWith(".tres.remap"))
                continue;

            var one = GD.Load<WeaponResource>(
                $"res://resources/weapons/{file.Replace(".remap", "")}");

            if (one != null)
                loaded.Add(one);
        }

        return loaded.ToArray();
    }
}
