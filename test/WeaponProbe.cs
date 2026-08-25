using Godot;

/// Exercises each weapon category against a controlled line-up and checks the
/// mechanic that distinguishes it: penetration for firearms, the arc for melee,
/// travel time for projectiles, and the proficiency curves for all of them.
///
///   godot --headless --script test/WeaponProbe.cs
///
/// Exit code is the verdict.
public partial class WeaponProbe : SceneTree
{
    private Horde? _horde;
    private WeaponHandler? _weapons;
    private Node3D? _player;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // A fixed layout, set before the scene enters the tree because the
        // generator runs in _Ready. Without it every run of this script would
        // face a different map, and a number that changes for reasons the test
        // did not choose is not a measurement.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Node3D>("Player");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

            if (_horde == null || _player == null || _weapons == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null} weapons={_weapons != null}");
                Quit(1);
                return true;
            }

            // The run director spawns on a timer. Every stage below asserts an
            // exact survivor count, so leaving it running turns this into a race
            // against the spawn curve rather than a test of the weapon.
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            if (!CheckProficiencyCurves())
            {
                Quit(1);
                return true;
            }
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageFirearm, "firearm penetration");
            case 1: return RunStage(StageMelee, "melee arc");
            case 2: return RunStage(StageBow, "bow projectile");
            case 3: return RunStage(StageNothingDominates, "no weapon beats a sibling everywhere");
            case 4: return RunStage(StageEverySlotIsStocked, "both shelves have something on them");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// Nothing in a category is a strictly better version of a sibling.
    ///
    /// The gear table has lived by this rule since the loadout rework — the piece
    /// that grants a rule pays for it in the stat its neighbour is best at, and
    /// `LoadoutProbe` has a stage that says so. **The weapon table never had one.**
    /// A shop where one weapon per category is simply correct makes every other row
    /// a step on the way to it, and then the deck's five lines, the biome that
    /// refuses a build and the survivor chosen before the loadout are all arguing
    /// about a decision the player already settled in the armoury.
    ///
    /// Within a category only. A knife's 1.6 m against a rifle's 20 is not a
    /// comparison.
    ///
    /// **A magazine of zero is not a small magazine.** It means the weapon never
    /// reloads and can never run dry, which is the strongest thing that can be said
    /// about ammunition — compared as a number it reads as the worst, and the bow
    /// and all three melee weapons would be scored as though their defining
    /// advantage were a defect. Reload time goes the same way: a knife's default
    /// 2.0 s is a field nothing ever reads.
    ///
    /// Read from the directory rather than from a list of pairs. `LoadoutProbe`'s
    /// version names its three by hand, which is the rule this project keeps
    /// relearning: a hand-written list of a growing thing's members goes stale in
    /// the direction that hides the bug.
    private bool? StageNothingDominates(int tick)
    {
        using var directory = DirAccess.Open("res://resources/weapons");
        if (directory == null)
        {
            GD.PushError("  cannot open res://resources/weapons");
            return false;
        }

        var table = new System.Collections.Generic.List<WeaponResource>();

        foreach (string file in directory.GetFiles())
        {
            // Godot hands exported resources back as `.tres.remap`, so the
            // extension is trimmed rather than matched.
            if (!file.EndsWith(".tres") && !file.EndsWith(".tres.remap"))
                continue;

            var one = GD.Load<WeaponResource>(
                $"res://resources/weapons/{file.Replace(".remap", "")}");

            if (one != null)
                table.Add(one);
        }

        if (table.Count < 2)
        {
            GD.PushError($"  {table.Count} weapons loaded — nothing to compare");
            return false;
        }

        bool ok = true;
        int pairs = 0;

        for (int i = 0; i < table.Count; i++)
        {
            for (int j = i + 1; j < table.Count; j++)
            {
                WeaponResource a = table[i], b = table[j];

                // Same slot *and* same category.
                //
                // Category was the whole grouping while a loadout was one weapon,
                // and it still carries the reason it was chosen: a knife's 1.6 m
                // against a rifle's 20 is not a comparison. Slot is the half that
                // arrived with the pair — a Primary and a Sidearm are not
                // alternatives, they are both carried, so they never compete and
                // scoring them against each other would report a trade where
                // there is no choice being made.
                if (a.Category != b.Category || a.Slot != b.Slot)
                    continue;

                pairs++;

                (float[] mine, float[] theirs) = Axes(a, b);

                bool aWins = false, bWins = false;
                for (int axis = 0; axis < mine.Length; axis++)
                {
                    aWins |= mine[axis] > theirs[axis];
                    bWins |= theirs[axis] > mine[axis];
                }

                if (aWins && !bWins)
                {
                    GD.PushError($"  {b.WeaponName} is beaten by {a.WeaponName} on every "
                               + "axis and better on none — a tier, not a choice");
                    ok = false;
                }
                else if (bWins && !aWins)
                {
                    GD.PushError($"  {a.WeaponName} is beaten by {b.WeaponName} on every "
                               + "axis and better on none — a tier, not a choice");
                    ok = false;
                }
            }
        }

        GD.Print($"  {table.Count} weapons, {pairs} same-slot same-category pairs, each a trade");
        return ok;
    }

    /// Neither shelf is empty, and the starting kit is a legal pair.
    ///
    /// A slot type nothing occupies is a slot type that does not exist, and it
    /// would fail silently: the shop would simply never offer a sidearm, the
    /// player would carry one weapon in a game built around two, and every
    /// number above would still pass. The starting kit is checked with it
    /// because that is the pair every player begins with and the one a fresh
    /// profile cannot get wrong.
    private bool? StageEverySlotIsStocked(int tick)
    {
        using var directory = DirAccess.Open("res://resources/weapons");
        if (directory == null)
        {
            GD.PushError("  cannot open res://resources/weapons");
            return false;
        }

        int primaries = 0, sidearms = 0;

        foreach (string file in directory.GetFiles())
        {
            if (!file.EndsWith(".tres") && !file.EndsWith(".tres.remap"))
                continue;

            var one = GD.Load<WeaponResource>(
                $"res://resources/weapons/{file.Replace(".remap", "")}");

            if (one == null)
                continue;

            if (one.Slot == WeaponSlot.Sidearm)
                sidearms++;
            else
                primaries++;
        }

        var kitPrimary = GD.Load<WeaponResource>("res://resources/weapons/scavenged_rifle.tres");
        var kitSidearm = GD.Load<WeaponResource>("res://resources/weapons/combat_knife.tres");

        bool kitIsAPair = kitPrimary is { Slot: WeaponSlot.Primary }
                          && kitSidearm is { Slot: WeaponSlot.Sidearm };

        GD.Print($"  {primaries} primaries, {sidearms} sidearms; the starting kit is one of each "
               + $"= {kitIsAPair}");

        return primaries > 0 && sidearms > 0 && kitIsAPair;
    }

    /// The two weapons as comparable numbers, higher being better.
    ///
    /// Taken as a pair rather than one at a time because two of the axes only
    /// exist when both sides have them: a ricochet count of 1 against a blast
    /// radius of 4 is two different weapons, not a worse one and a better one, and
    /// scoring them against each other would let anything carrying a large trait
    /// number appear to win an axis it does not share.
    private static (float[] Mine, float[] Theirs) Axes(WeaponResource a, WeaponResource b)
    {
        bool sameTrait = a.Trait == b.Trait;
        (float amountSign, float countSign) = TraitSigns(a.Trait);

        static float Magazine(WeaponResource w) =>
            w.MagazineSize == 0 ? float.PositiveInfinity : w.MagazineSize;

        static float Reserve(WeaponResource w) =>
            w.MagazineSize == 0 ? float.PositiveInfinity : w.StartingReserve;

        static float Reload(WeaponResource w) =>
            w.MagazineSize == 0 ? 0.0f : w.BaseReloadTime;

        float[] Score(WeaponResource w) => new[]
        {
            w.BaseDamage,
            w.BaseAttackSpeed,
            w.BaseRange,
            w.Penetration,
            w.Knockback,
            w.SwingArcDegrees,
            Magazine(w),
            Reserve(w),

            // Where less is better.
            -w.BaseSpreadDegrees,
            -Reload(w),

            // What the shop is really selling on the long curves: where the
            // weapon starts and how far it can climb.
            w.MaxLevel,
            w.TierStartBonus,

            sameTrait ? amountSign * w.TraitAmount : 0.0f,
            sameTrait ? countSign * w.TraitCount : 0.0f,
        };

        return (Score(a), Score(b));
    }

    /// Which way each trait's two numbers point.
    ///
    /// `TraitAmount` and `TraitCount` mean something different for every trait,
    /// and for two of them more is worse. A burst's amount is the gap between its
    /// extra shots, so a tighter burst is a *smaller* number; a charge's count is
    /// how many seconds the weapon must sit idle before the multiplier is ready.
    ///
    /// **This is what stopped the first version of this stage finding the thing it
    /// was written to find.** Scored as plain magnitudes, the Service Rifle's
    /// 0.07-second burst read as a loss against the Scavenged Rifle's 0.09 — one
    /// axis, pointing the wrong way, and a weapon that beats the starting kit on
    /// every one of the other thirteen came back as a fair trade.
    private static (float Amount, float Count) TraitSigns(WeaponTrait trait) => trait switch
    {
        WeaponTrait.Burst => (-1.0f, +1.0f),
        WeaponTrait.Charge => (+1.0f, -1.0f),
        _ => (+1.0f, +1.0f),
    };

    /// Pure data checks — no simulation needed, so a wrong curve is reported as a
    /// curve problem rather than as a mysterious combat result later.
    private bool CheckProficiencyCurves()
    {
        var rifle = GD.Load<WeaponResource>("res://resources/weapons/scavenged_rifle.tres");
        var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres");
        var bow = GD.Load<WeaponResource>("res://resources/weapons/hunting_bow.tres");
        if (rifle == null || axe == null || bow == null)
        {
            GD.PushError("PROBE FAILED — weapon resources missing; run BuildWeapons.cs");
            return false;
        }

        float spread0 = rifle.GetEffectiveSpreadDegrees(0);
        float spread5 = rifle.GetEffectiveSpreadDegrees(5);

        // The 20% floor is now the second line of defence, not the first: every
        // curve is read at a level clamped to the weapon's MaxLevel, and this
        // rifle's ceiling of 8 binds before the floor at 10 ever would. The floor
        // still has to hold for a weapon whose ceiling is high enough to reach
        // it, so it is tested on one — the invariant is "however long the climb,
        // practice never turns a shotgun into a laser", not "the rifle gets
        // there".
        var deepCeiling = new WeaponResource
        {
            Category = WeaponCategory.Firearm,
            BaseSpreadDegrees = rifle.BaseSpreadDegrees,
            MaxLevel = 40,
        };

        float spreadFloor = deepCeiling.GetEffectiveSpreadDegrees(99);
        bool spreadOk = spread5 < spread0
            && Mathf.IsEqualApprox(spreadFloor, rifle.BaseSpreadDegrees * 0.2f);

        bool rifleRangeFlat = Mathf.IsEqualApprox(rifle.GetEffectiveRange(9), rifle.BaseRange);
        bool axeRangeGrows = axe.GetEffectiveRange(9) > axe.BaseRange;
        bool axeFaster = axe.GetEffectiveAttackDelay(9) < axe.GetEffectiveAttackDelay(0);
        bool bowFaster = bow.GetEffectiveProjectileSpeed(9) > bow.ProjectileSpeed;
        bool reloadFloor = rifle.GetEffectiveReloadTime(99) >= rifle.BaseReloadTime * 0.3f - 0.001f;

        GD.Print($"spread   {spread0:F2} -> {spread5:F2} deg, " +
                 $"{rifle.GetEffectiveSpreadDegrees(rifle.MaxLevel):F2} at this rifle's ceiling " +
                 $"({rifle.MaxLevel}), floor {spreadFloor:F2} (base {rifle.BaseSpreadDegrees:F2})");
        GD.Print($"axe range {axe.BaseRange:F2} -> {axe.GetEffectiveRange(9):F2} m, " +
                 $"delay {axe.GetEffectiveAttackDelay(0):F3} -> {axe.GetEffectiveAttackDelay(9):F3} s");
        GD.Print($"rifle range flat: {rifleRangeFlat}, bow speed {bow.ProjectileSpeed:F1} -> {bow.GetEffectiveProjectileSpeed(9):F1} m/s");

        bool ok = spreadOk && rifleRangeFlat && axeRangeGrows && axeFaster && bowFaster && reloadFloor;
        if (!ok)
        {
            GD.PushError($"PROBE FAILED — curves: spread={spreadOk} rifleFlat={rifleRangeFlat} " +
                         $"axeRange={axeRangeGrows} axeSpeed={axeFaster} bowSpeed={bowFaster} reload={reloadFloor}");
        }
        else
        {
            GD.Print("proficiency curves: ok");
        }

        return ok;
    }

    /// One rifle shot with Penetration = 1 should remove exactly the nearest
    /// enemy from a column of five.
    private bool? StageFirearm(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            for (int i = 0; i < 5; i++)
                _horde.Spawn(new Vector3(2.0f + i, 0.0f, 0.0f));

            _weapons!.SetProficiency(WeaponCategory.Firearm, 99);  // minimum spread
            _weapons.Equip(GD.Load<WeaponResource>("res://resources/weapons/scavenged_rifle.tres"));
            return null;
        }

        // One shot fires on the first tick after equipping; sample before the
        // cooldown lets a second one through.
        if (tick < 3)
            return null;

        int remaining = _horde!.Pool.Count;
        bool nearestGone = true;
        for (int i = 0; i < remaining; i++)
        {
            if (_horde.Pool.Position[i].X < 2.5f)
                nearestGone = false;
        }

        GD.Print($"  5 enemies, 1 shot, penetration 1 -> {remaining} left, nearest removed: {nearestGone}");
        return remaining == 4 && nearestGone;
    }

    /// A 100-degree axe sweep aimed at +X must hit the enemy in front and leave
    /// the one behind untouched.
    private bool? StageMelee(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _horde.Spawn(new Vector3(2.0f, 0.0f, 0.0f));    // in front
            _horde.Spawn(new Vector3(-2.2f, 0.0f, 0.0f));   // behind

            _weapons!.SetProficiency(WeaponCategory.MeleeLong, 0);

            // The axe with its cleave switched off, on a copy. The invariant here
            // is that SwingArcDegrees is the *full* sweep and not the half-angle —
            // the Phase 3 bug where 100 became 200 and reached behind the swinger.
            // The axe now cleaves on purpose, so testing the arc with it as-is
            // asks two questions at once and gets the wrong answer to both; that
            // it reaches behind is TraitProbe's business.
            var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres").Duplicate() as WeaponResource;
            axe!.Trait = WeaponTrait.None;
            _weapons.Equip(axe);
            return null;
        }

        if (tick < 3)
            return null;

        int remaining = _horde!.Pool.Count;
        bool behindAlive = false;
        for (int i = 0; i < remaining; i++)
        {
            if (_horde.Pool.Position[i].X < 0.0f)
                behindAlive = true;
        }

        // 16 damage against 10 health kills outright, so the front one is gone
        // and the rear one must still be standing.
        GD.Print($"  front + behind, 100 deg sweep at +X -> {remaining} left, behind survived: {behindAlive}");
        return remaining == 1 && behindAlive;
    }

    /// The bow must put a projectile in flight, and that projectile must land.
    private bool? StageBow(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _horde.Spawn(new Vector3(10.0f, 0.0f, 0.0f));

            _weapons!.SetProficiency(WeaponCategory.BowCrossbow, 0);
            _weapons.Equip(GD.Load<WeaponResource>("res://resources/weapons/hunting_bow.tres"));
            return null;
        }

        if (tick == 3)
        {
            bool inFlight = _weapons!.Projectiles.Count > 0;
            GD.Print($"  projectiles in flight two ticks after firing: {_weapons.Projectiles.Count}");
            if (!inFlight)
                return false;
        }

        // 10m at 26 m/s is under half a second; 60 ticks is ample.
        if (tick < 60)
            return null;

        GD.Print($"  target at 10m after 1s -> {_horde!.Pool.Count} left");
        return _horde.Pool.Count == 0;
    }
}
