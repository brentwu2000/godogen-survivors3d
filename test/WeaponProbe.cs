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
