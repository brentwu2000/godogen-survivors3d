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
            case 5: return RunStage(StageSpread, "the shotgun is a distance, not a damage number");
            case 6: return RunStage(StageCharge, "waiting is worth something, and firing spends it");
            case 7: return RunStage(StageBlast, "the bolt detonates where it connects");
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

    /// The shotgun's damage is a function of range, and that is the weapon.
    ///
    /// Measured at two distances rather than by counting pellets: eight separate
    /// rolls inside a cone is an implementation, and what has to be true is that
    /// standing close is worth more. A spread that fired one shot for the full
    /// damage would pass any count-based check and be a different weapon.
    private bool? StageSpread(int tick)
    {
        var shotgun = GD.Load<WeaponResource>("res://resources/weapons/pump_shotgun.tres");
        if (shotgun == null)
        {
            GD.PushError("  pump_shotgun.tres did not load");
            return false;
        }

        if (tick == 1)
        {
            _weapons!.Equip(0, shotgun);
            _weapons.SetProficiency(WeaponCategory.Firearm, 0);
            return null;
        }

        // Point blank, then most of the way out. A brute, so nothing dies and
        // the health left is a clean reading.
        float close = DamageAtRange(shotgun, 2.0f);
        float far = DamageAtRange(shotgun, 11.0f);

        GD.Print($"  {shotgun.TraitCount} pellets at {shotgun.TraitAmount:P0}: " +
                 $"{close:F1} damage at 2 m, {far:F1} at 11 m");

        bool closeHurts = close > shotgun.BaseDamage;
        bool fallsOff = far < close * 0.8f;

        if (!closeHurts)
            GD.PushError($"  {close:F1} at point blank against a base damage of {shotgun.BaseDamage:F1} — " +
                         "are the pellets firing as one shot?");

        if (!fallsOff)
            GD.PushError($"  {far:F1} at 11 m against {close:F1} at 2 m — the cone is not spreading");

        return closeHurts && fallsOff;
    }

    /// Fires once at a lone enemy placed `metres` away and returns what it took.
    ///
    /// The target has to survive, or the reading is its health rather than the
    /// damage — which is not a hypothetical. The charge stage first ran against a
    /// brute, one-shot it for far more than its 60 HP, and read back exactly 60:
    /// a charge that had worked perfectly, reported as a multiplier of 1.6.
    /// `type` exists so a stage measuring a big number can pick something big
    /// enough to take it.
    private float DamageAtRange(WeaponResource weapon, float metres, int type = 2)
    {
        _horde!.Pool.Clear();
        _horde.Spawn(_player!.GlobalPosition + new Vector3(metres, 0.0f, 0.0f), type);

        if (_horde.Pool.Count == 0)
            return 0.0f;

        float before = _horde.Pool.Health[0];
        _weapons!.ForceFire(new Vector2(1.0f, 0.0f));

        if (_horde.Pool.Count == 0)
        {
            GD.PushError($"  the target died to one shot at {metres:F0} m — the reading is its " +
                         $"{before:F0} HP, not the damage. Use a tougher type.");
            return before;
        }

        return before - _horde.Pool.Health[0];
    }

    /// Waiting multiplies the shot, and the shot spends the wait.
    private bool? StageCharge(int tick)
    {
        var rifle = GD.Load<WeaponResource>("res://resources/weapons/marksman_rifle.tres");
        if (rifle == null)
        {
            GD.PushError("  marksman_rifle.tres did not load");
            return false;
        }

        if (tick == 1)
        {
            _weapons!.Equip(0, rifle);
            _weapons.SetProficiency(WeaponCategory.Firearm, 0);
            _weapons.ForceFire(new Vector2(1.0f, 0.0f));   // spend whatever was banked
            return null;
        }

        // TraitCount seconds of physics ticks, plus a couple for the boundary.
        if (tick < rifle.TraitCount * 60 + 4)
            return null;

        // The boss, because a charged marksman shot is 3.5 times a 34 damage
        // base and a brute has 60 HP.
        bool charged = _weapons!.IsCharged;
        float first = DamageAtRange(rifle, 6.0f, type: 5);

        // Immediately again: the charge was just spent, so this one is plain.
        bool spent = !_weapons.IsCharged;
        float second = DamageAtRange(rifle, 6.0f, type: 5);

        GD.Print($"  after {rifle.TraitCount}s idle: charged={charged}, hit for {first:F1}; " +
                 $"straight after: charged={spent switch { true => "false", false => "true" }}, hit for {second:F1}");

        bool multiplied = first > second * 2.0f;

        if (!charged)
            GD.PushError($"  not charged after {rifle.TraitCount}s of waiting");
        if (!spent)
            GD.PushError("  still charged immediately after firing — the shot did not spend it");
        if (!multiplied)
            GD.PushError($"  {first:F1} then {second:F1} — the charge multiplied nothing");

        return charged && spent && multiplied;
    }

    /// The bolt hurts what it hit and what was standing next to it.
    private bool? StageBlast(int tick)
    {
        var launcher = GD.Load<WeaponResource>("res://resources/weapons/bolt_launcher.tres");
        if (launcher == null)
        {
            GD.PushError("  bolt_launcher.tres did not load");
            return false;
        }

        if (tick == 1)
        {
            _weapons!.Equip(0, launcher);
            _horde!.Pool.Clear();

            // One in the line of fire and one well off it, inside the blast.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(8.0f, 0.0f, 0.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(8.0f, 0.0f, 2.6f), 2);
            _weapons.ForceFire(new Vector2(1.0f, 0.0f));

            _targetBefore = _horde.Pool.Health[0];
            _bystanderBefore = _horde.Pool.Health[1];
            return null;
        }

        // The bolt has to fly. At 19 m/s, eight metres is under half a second.
        if (tick < 40)
            return null;

        float target = _targetBefore - _horde!.Pool.Health[0];
        float bystander = _bystanderBefore - _horde.Pool.Health[1];

        GD.Print($"  bolt at 8 m: the target took {target:F1}, a bystander 2.6 m off it took {bystander:F1} " +
                 $"(blast {launcher.TraitAmount:F0} m)");

        bool hit = target > 0.0f;
        bool splashed = bystander > 0.0f;

        if (!hit)
            GD.PushError("  the bolt did not connect at all");
        if (!splashed)
            GD.PushError("  nothing beside the target was touched — the bolt did not detonate");

        // The direct hit has to be worth more than the splash, or the weapon is
        // strictly worse than a rifle at what it is aimed at.
        bool direct = target > bystander;
        if (!direct)
            GD.PushError($"  the bystander took {bystander:F1} against the target's {target:F1}");

        return hit && splashed && direct;
    }

    private float _targetBefore;
    private float _bystanderBefore;

    /// The blanket check. A weapon that came out of BuildWeapons without one is
    /// a weapon that is only a number, which is the state this phase existed to
    /// leave behind.
    private bool? StageEveryWeaponHasOne(int tick)
    {
        // The directory, not a list of names.
        //
        // This held six hardcoded names, and three weapons were added without it
        // noticing — it went green while none of the new traits had ever been
        // loaded, let alone fired. A blanket check that has to be edited to cover
        // new content is a blanket check that covers the content somebody
        // remembered.
        using var directory = DirAccess.Open("res://resources/weapons");
        if (directory == null)
        {
            GD.PushError("  cannot open res://resources/weapons");
            return false;
        }

        bool ok = true;
        var summary = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<WeaponTrait>();
        int found = 0;

        foreach (string file in directory.GetFiles())
        {
            // Godot hands exported resources back as `.tres.remap`, so the
            // extension has to be trimmed rather than matched — a filter on
            // `.tres` finds nothing at all in a build.
            if (!file.EndsWith(".tres") && !file.EndsWith(".tres.remap"))
                continue;

            string path = $"res://resources/weapons/{file.Replace(".remap", "")}";
            var weapon = GD.Load<WeaponResource>(path);
            if (weapon == null)
            {
                GD.PushError($"  {file} did not load — run BuildWeapons.cs");
                ok = false;
                continue;
            }

            found++;
            summary.Add($"{weapon.WeaponName}={weapon.Trait}");
            seen.Add(weapon.Trait);

            if (weapon.Trait == WeaponTrait.None)
            {
                GD.PushError($"  {weapon.WeaponName} has no trait — it is only a number");
                ok = false;
            }
        }

        GD.Print($"  {found} weapons: {string.Join("  ", summary)}");

        // And every trait the enum defines has to be on something. A trait with
        // no weapon is code nobody runs, which is how `Spread` could have shipped
        // subtly broken with a green suite.
        foreach (WeaponTrait trait in System.Enum.GetValues<WeaponTrait>())
        {
            if (trait == WeaponTrait.None || seen.Contains(trait))
                continue;

            GD.PushError($"  no weapon uses {trait} — the trait is unreachable");
            ok = false;
        }

        return ok && found > 0;
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
        if (tick < 120)
            return null;

        int hits = _weapons!.HitsThisRun(WeaponCategory.Firearm);
        int spent = _ammoBefore - _weapons.Ammo;

        GD.Print($"  {rifle.TraitCount} queued shots cost {spent} rounds, and {hits} landed on the "
               + "wall (the initiating shot is free through ForceFire, not in the game)");

        // The burst is the *rounds*, not the bodies.
        //
        // This used to require `hits > TraitCount`, and the Service Rifle
        // satisfied it through **penetration** rather than through its burst: a
        // pierce of 2 against a wall of brutes doubles every shot's hit count, so
        // two queued rounds landed four hits and the stage read that as evidence
        // of the trait. H4b moved the rifle's penetration to 1, the burst carried
        // on working exactly as it always had, and the stage went red — it had
        // never been measuring the thing it names.
        //
        // What the trait promises is that its extra shots are not free, so what is
        // asserted is that the queue costs a round each and that every round it
        // spends reaches the wall. Counted against `TraitCount` read from the
        // resource rather than against a total written in here.
        //
        // The initiating shot is deliberately absent from `spent`: `ForceFire`
        // documents itself as ignoring cooldown and ammo, because a probe that had
        // to wait for a cooldown and for auto-targeting to agree with it would be
        // measuring those instead. A real trigger pull goes through the ordinary
        // path, which decrements — so the game spends `1 + TraitCount` and this
        // stage can only see `TraitCount`. Worth knowing before reading the number
        // as a missing round.
        return spent >= rifle.TraitCount && hits >= spent;
    }

    private int _ammoBefore;
}
