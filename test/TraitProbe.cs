using Godot;

/// Checks that each weapon's signature does something no other weapon does.
///
///   godot --headless --script test/TraitProbe.cs
///
/// Exit code is the verdict. Six weapons that differ only in damage, range and
/// magazine size are six difficulty settings for one weapon, so the traits are
/// the point of the shop — and a trait wired to nothing looks exactly like a
/// weapon that is simply worse, which is the kind of bug that gets balanced
/// around instead of fixed.
public partial class TraitProbe : SceneTree
{
    private Node? _scene;
    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;

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

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

            if (_horde == null || _player == null || _weapons == null)
            {
                GD.PushError("PROBE FAILED — scene is missing a required node");
                Quit(1);
                return true;
            }

            _scene?.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            // Auto-fire held, not the whole node stopped. Left firing, every
            // measurement becomes "the trait plus however many extra attacks
            // fitted in the window" — the knife appeared to bleed five times its
            // card. Stopping the node instead breaks the opposite way: arrows are
            // stepped here and so is the burst queue, so ricochet and burst both
            // measured a trait that could never happen.
            _weapons.HoldFire = true;
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageEveryWeaponHasOne, "every weapon carries a signature");
            case 1: return RunStage(StageBleed, "the knife opens a wound that outlives the swing");
            case 2: return RunStage(StageCleave, "the axe hits what is behind it");
            case 3: return RunStage(StageRicochet, "an arrow turns to a new target");
            case 4: return RunStage(StageBurst, "the rifle spends its burst, and its ammo with it");
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

    /// The blanket check. A weapon that came out of BuildWeapons without one is
    /// a weapon that is only a number, which is the state this phase existed to
    /// leave behind.
    private bool? StageEveryWeaponHasOne(int tick)
    {
        string[] names =
        {
            "combat_knife", "fire_axe", "hunting_bow",
            "scavenged_rifle", "service_rifle", "reaper_scythe",
        };

        bool ok = true;
        var summary = new System.Collections.Generic.List<string>();

        foreach (string name in names)
        {
            var weapon = GD.Load<WeaponResource>($"res://resources/weapons/{name}.tres");
            if (weapon == null)
            {
                GD.PushError($"  {name}.tres did not load — run BuildWeapons.cs");
                ok = false;
                continue;
            }

            summary.Add($"{weapon.WeaponName}={weapon.Trait}");
            if (weapon.Trait == WeaponTrait.None)
                ok = false;
        }

        GD.Print($"  {string.Join("  ", summary)}");
        return ok;
    }

    private bool? StageBleed(int tick)
    {
        var knife = GD.Load<WeaponResource>("res://resources/weapons/combat_knife.tres");
        if (knife == null)
            return false;

        if (tick == 1)
        {
            _weapons!.Equip(0, knife);
            _horde!.Pool.Clear();

            // A brute: enough health that the swing itself cannot kill it, so
            // anything that happens afterwards is the wound and not the hit.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(1.0f, 0.0f, 0.0f), 2);
            return null;
        }

        if (tick == 2)
        {
            _weapons!.ForceFire(new Vector2(1.0f, 0.0f));
            _afterSwing = _horde!.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
            return null;
        }

        // A second of ticking, no further swings.
        if (tick < 64)
            return null;

        float now = _horde!.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
        float bled = _afterSwing - now;

        GD.Print($"  brute at {_afterSwing:F1} HP after one swing, {now:F1} a second later " +
                 $"(bled {bled:F1}, card says {knife.TraitAmount:F0}/s)");

        return bled > knife.TraitAmount * 0.6f;
    }

    private float _afterSwing;

    private bool? StageCleave(int tick)
    {
        var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres");
        if (axe == null)
            return false;

        if (tick == 1)
        {
            _weapons!.Equip(0, axe);
            _horde!.Pool.Clear();

            // One in front, one directly behind. A swing aimed at +X must reach
            // both, and only the trait can explain the second.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(1.5f, 0.0f, 0.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(-1.5f, 0.0f, 0.0f), 2);
            return null;
        }

        if (tick < 3)
            return null;

        float frontBefore = _horde!.Pool.Health[0];
        float behindBefore = _horde.Pool.Health[1];
        _weapons!.ForceFire(new Vector2(1.0f, 0.0f));

        float frontHit = frontBefore - _horde.Pool.Health[0];
        float behindHit = behindBefore - _horde.Pool.Health[1];

        GD.Print($"  swing at +X: front took {frontHit:F1}, behind took {behindHit:F1} " +
                 $"(cleave is {axe.TraitAmount:P0} of the hit)");

        return frontHit > 0.0f && behindHit > 0.0f && behindHit < frontHit;
    }

    private bool? StageRicochet(int tick)
    {
        var bow = GD.Load<WeaponResource>("res://resources/weapons/hunting_bow.tres");
        if (bow == null)
            return false;

        if (tick == 1)
        {
            _weapons!.Equip(0, bow);
            _horde!.Pool.Clear();
            _weapons.Projectiles.Clear();

            // One in the line of fire, one well off to the side. A piercing shot
            // carries straight on and never touches the second; only a bounce
            // turns to face it.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(6.0f, 0.0f, 0.0f), 0);
            _horde.Spawn(_player.GlobalPosition + new Vector3(7.0f, 0.0f, 4.0f), 0);
            _weapons.ForceFire(new Vector2(1.0f, 0.0f));
            return null;
        }

        if (tick < 90)
            return null;

        int left = _horde!.Pool.Count;
        GD.Print($"  one shot at a target with a bystander 4 m off the line: {2 - left} of 2 hit");

        return left == 0;
    }

    private bool? StageBurst(int tick)
    {
        var rifle = GD.Load<WeaponResource>("res://resources/weapons/service_rifle.tres");
        if (rifle == null)
            return false;

        if (tick == 1)
        {
            _weapons!.Equip(0, rifle);
            _horde!.Pool.Clear();

            // A wall of brutes down the lane, so nothing dies and every shot has
            // something to land on.
            for (int i = 0; i < 6; i++)
                _horde.Spawn(_player!.GlobalPosition + new Vector3(4.0f + i * 1.5f, 0.0f, 0.0f), 2);

            _ammoBefore = _weapons.Ammo;
            _weapons.ForceFire(new Vector2(1.0f, 0.0f));
            return null;
        }

        // Long enough for the queued shots to come out at their spacing.
        if (tick < 40)
            return null;

        int hits = _weapons!.HitsThisRun(WeaponCategory.Firearm);
        int spent = _ammoBefore - _weapons.Ammo;

        GD.Print($"  one trigger pull on a {rifle.TraitCount}-round burst: {hits} hits, {spent} rounds spent");

        // The trait's cost is the ammo. A burst that fires extra shots for free
        // is a damage buff with a sound effect.
        return hits > rifle.TraitCount && spent >= rifle.TraitCount;
    }

    private int _ammoBefore;
}
