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
            case 8: return RunStage(StageChill, "the emitter takes speed away, and gives it back");
            case 9: return RunStage(StageMark, "a marked body takes more from everything, until it does not");
            case 10: return RunStage(StageShatter, "a heavy hit shatters chill and spends it");
            case 11: return RunStage(StageSpreadReaction, "a cleave spends one wound and spreads it across the sweep");
            case 12: return RunStage(StageCookOff, "a blast cooks off burn once, and spends it");
            case 13: return RunStage(StageConduct, "chain leaves shock, and a chilled hit conducts it once");
            case 14: return RunStage(StageSidearmFiresItself, "the second slot fires its own weapon, not the first one's");
            case 15: return RunStage(StageOverheat, "the pulse rifle locks hot and resumes only below its safe line");
            case 16: return RunStage(StageBeam, "the arc lance holds a beam and shocks only after dwelling");
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
            summary.Add($"{weapon.WeaponName}=" +
                        (weapon.FiringModel != WeaponFiringModel.Standard
                            ? weapon.FiringModel.ToString()
                            : weapon.Trait.ToString()));
            seen.Add(weapon.Trait);

            if (weapon.Trait == WeaponTrait.None
                && weapon.FiringModel == WeaponFiringModel.Standard)
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

    /// The tick a status weapon's effect must be gone by, from the weapon's own
    /// card rather than from a number typed beside it.
    ///
    /// Every stage here that asserts a status is *spent* has to wait for the card
    /// to run out, and the obvious way to write that wait is to read the card,
    /// convert to ticks, and type the answer. Then the card changes and the stage
    /// fails while naming the trait — which is a test reporting a balance
    /// decision as a bug in the mechanic it happens to sit next to.
    ///
    /// A second and a half of margin, because these run at 60 Hz against a
    /// duration in whole seconds and the tick the status is applied on is not the
    /// tick the stage started counting from.
    private static int ExpiryTick(WeaponResource weapon) =>
        Mathf.RoundToInt((weapon.TraitCount + 1.5f) * 60.0f);

    private float _speedBefore;
    private float _speedChilled;
    private float _hordeSpeed = 1.0f;

    /// The emitter's shot has to take speed off the body it hit, and the body has
    /// to get it back.
    ///
    /// Read off `Pool.Velocity` rather than off distance covered, and that choice
    /// is the whole reason this stage is short. Distance is the honest-looking
    /// measurement and it is contaminated three ways: the flow field re-routes
    /// around props, the separation force pushes neighbours apart, and anything
    /// that reaches `ContactRadius` has its velocity zeroed and stops looking
    /// slowed at all — it looks *stopped*, which passes a naive check for the
    /// wrong reason. Velocity is what the chill actually multiplies, so it is
    /// what is asserted.
    ///
    /// **The recovery half is the important half.** A chill that never expires is
    /// a permanent slow, it looks exactly like a chill that works, and the design
    /// notes name it as the failure mode a reaction-shaped status has. So the
    /// stage runs past the two seconds on the card and requires the speed back.
    private bool? StageChill(int tick)
    {
        var emitter = GD.Load<WeaponResource>("res://resources/weapons/hand_emitter.tres");
        if (emitter == null)
        {
            GD.PushError("  hand_emitter.tres did not load — run BuildWeapons.cs");
            return false;
        }

        if (tick == 1)
        {
            _weapons!.Equip(0, emitter);
            _weapons.SetProficiency(WeaponCategory.Firearm, 99);
            _horde!.Pool.Clear();

            // The horde crawls for the length of this stage, and that is what
            // keeps the reading honest rather than what weakens it.
            //
            // Everything measured here is a *ratio* — chilled speed against
            // unchilled, recovered against unchilled — and `SpeedScale` divides
            // out of all three. What it does change is how much ground the walker
            // covers before the last reading, and that matters enormously: a body
            // inside `ContactRadius` has its velocity zeroed, so it reports 0.00
            // and the stage says "the chill never wore off" about a walker that
            // simply arrived.
            //
            // That is not hypothetical. The wait here is derived from the card,
            // the card went from two seconds to three, and 7.5 m at 2.4 m/s is
            // exactly enough to reach the player in the extra time — so a correct
            // weapon change turned this stage red and blamed the trait. The
            // stage's own comment had warned about contact and then left the
            // margin as a distance that only worked for one duration.
            _hordeSpeed = _horde.SpeedScale;
            _horde.SpeedScale = 0.2f;

            // Inside the emitter's 8 m, and now unable to cross the gap however
            // long the card gets.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(7.5f, 0.0f, 0.0f), 0);
            return null;
        }

        // Two ticks for the mover to give it a velocity at all — it spawns at
        // rest, and a baseline of zero would make any later reading a "slow".
        if (tick == 3)
        {
            _speedBefore = _horde!.Pool.Count > 0 ? _horde.Pool.Velocity[0].Length() : 0.0f;
            _weapons!.ForceFire(new Vector2(1.0f, 0.0f));
            return null;
        }

        if (tick == 5)
        {
            _speedChilled = _horde!.Pool.Count > 0 ? _horde.Pool.Velocity[0].Length() : 0.0f;
            return null;
        }

        // The card's own duration plus a margin, at 60 Hz.
        //
        // **Read off the resource, not typed.** This was `tick < 145`, which is
        // two seconds and change — correct for a card that said two, and silently
        // wrong the moment the emitter's chill went to three. The stage read
        // `TraitCount` for the assertion and hardcoded the same number for the
        // timing, so it went red on a weapon change that was working exactly as
        // intended, and it named the trait rather than the clock.
        if (tick < ExpiryTick(emitter))
            return null;

        float recovered = _horde!.Pool.Count > 0 ? _horde.Pool.Velocity[0].Length() : 0.0f;
        float taken = _speedBefore > 0.0f ? 1.0f - _speedChilled / _speedBefore : 0.0f;

        GD.Print($"  walker at {_speedBefore:F2} m/s -> {_speedChilled:F2} chilled " +
                 $"({taken:P0} taken, card says {emitter.TraitAmount:P0}) -> " +
                 $"{recovered:F2} after {emitter.TraitCount} s");

        bool slowed = Mathf.Abs(taken - emitter.TraitAmount) < 0.05f;
        bool spent = recovered > _speedBefore * 0.95f;

        _horde.SpeedScale = _hordeSpeed;

        if (!slowed)
            GD.PushError($"  chill took {taken:P0}, the card says {emitter.TraitAmount:P0}");

        if (!spent)
        {
            // Named apart, because a body that reached the player reports zero
            // and so does a chill that never ended, and they are opposite bugs.
            GD.PushError(recovered <= 0.01f
                ? "  the walker is not moving at all — it reached contact, so this is the stage's "
                + "geometry rather than the chill. Widen the gap or slow the horde further."
                : "  the chill never wore off — a permanent slow, not a status");
        }

        return _speedBefore > 0.0f && slowed && spent;
    }

    private float _plainHit;
    private float _markedHit;

    /// A mark makes the *next* thing to land hurt more, whatever fires it.
    ///
    /// The follow-up damage is applied by calling `Horde.Damage` directly rather
    /// than by firing a second weapon, and that is deliberate: the claim is "more
    /// from every source", so the cleanest evidence is a source that is not a
    /// weapon at all. Firing a rifle for the second reading would put spread, a
    /// crit roll and a second trait between the mark and the number, and a stage
    /// that goes red for one of those reads as the mark being broken.
    ///
    /// The pistol still applies the mark through the real firing path, which is
    /// the half that could actually be miswired.
    private bool? StageMark(int tick)
    {
        var pistol = GD.Load<WeaponResource>("res://resources/weapons/sidearm_pistol.tres");
        if (pistol == null)
        {
            GD.PushError("  sidearm_pistol.tres did not load — run BuildWeapons.cs");
            return false;
        }

        const float Probe = 10.0f;

        if (tick == 1)
        {
            _weapons!.Equip(0, pistol);
            _weapons.SetProficiency(WeaponCategory.Firearm, 99);
            _horde!.Pool.Clear();

            // A brute, for the reason the bleed stage uses one: four separate
            // hits land here and none of them may kill, or the reading becomes a
            // health value belonging to whoever swap-filled the slot.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(7.0f, 0.0f, 0.0f), 2);
            return null;
        }

        // Unmarked baseline.
        if (tick == 2)
        {
            float before = _horde!.Pool.Health[0];
            _horde.Damage(0, Probe, Vector2.Zero);
            _plainHit = before - _horde.Pool.Health[0];
            return null;
        }

        if (tick == 3)
        {
            _weapons!.ForceFire(new Vector2(1.0f, 0.0f));
            return null;
        }

        if (tick == 5)
        {
            float before = _horde!.Pool.Health[0];
            _horde.Damage(0, Probe, Vector2.Zero);
            _markedHit = before - _horde.Pool.Health[0];
            return null;
        }

        // The card's own duration plus a margin. See `ExpiryTick`.
        if (tick < ExpiryTick(pistol))
            return null;

        float after = _horde!.Pool.Health[0];
        _horde.Damage(0, Probe, Vector2.Zero);
        float expiredHit = after - _horde.Pool.Health[0];

        float lift = _plainHit > 0.0f ? _markedHit / _plainHit - 1.0f : 0.0f;

        GD.Print($"  {Probe:F0} damage lands as {_plainHit:F1} plain, {_markedHit:F1} marked " +
                 $"({lift:P0}, card says {pistol.TraitAmount:P0}), " +
                 $"{expiredHit:F1} after {pistol.TraitCount} s");

        bool lifted = Mathf.Abs(lift - pistol.TraitAmount) < 0.02f;
        bool spent = Mathf.IsEqualApprox(expiredHit, _plainHit);

        if (!lifted)
            GD.PushError($"  the mark added {lift:P0}, the card says {pistol.TraitAmount:P0}");
        if (!spent)
            GD.PushError("  the mark outlived its own timer — a permanent multiplier");

        return _plainHit > 0.0f && lifted && spent;
    }

    /// A reaction is the pair, so apply with the emitter and consume with the
    /// axe through their real firing paths. The second axe hit must lose the
    /// bonus: that is the assertion that this is stored setup, not a permanent
    /// multiplier on every heavy hit after it.
    private bool? StageShatter(int tick)
    {
        var emitter = GD.Load<WeaponResource>("res://resources/weapons/hand_emitter.tres");
        var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres");
        if (emitter == null || axe == null)
        {
            GD.PushError("  shatter weapons did not load — run BuildWeapons.cs");
            return false;
        }

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _horde.Spawn(_player!.GlobalPosition + new Vector3(1.2f, 0.0f, 0.0f), 2);
            _weapons!.Equip(0, emitter);
            _weapons.SetProficiency(WeaponCategory.Firearm, 0);
            _weapons.ForceFire(Vector2.Right);
            return null;
        }

        if (tick == 3)
        {
            _weapons!.Equip(0, axe);
            _weapons.SetProficiency(WeaponCategory.MeleeLong, 0);
            float before = _horde!.Pool.Health[0];
            _weapons.ForceFire(Vector2.Right);
            _markedHit = before - _horde.Pool.Health[0];
            return null;
        }

        if (tick == 5)
        {
            float before = _horde!.Pool.Health[0];
            _weapons!.ForceFire(Vector2.Right);
            _plainHit = before - _horde.Pool.Health[0];
            return null;
        }

        if (tick < 7)
            return null;

        float expected = axe.BaseDamage * (1.0f + emitter.TraitAmount * 1.5f);
        bool burst = Mathf.Abs(_markedHit - expected) < 0.2f;
        bool spent = Mathf.Abs(_plainHit - axe.BaseDamage) < 0.2f
                  && _horde!.Pool.Chill[0] <= 0.0f
                  && _horde.Pool.ChillRemaining[0] <= 0.0f;

        GD.Print($"  chilled axe hit {_markedHit:F1} (expected {expected:F1}); next hit {_plainHit:F1}, "
               + $"chill left {_horde!.Pool.Chill[0]:F2}");

        if (!burst)
            GD.PushError("  the heavy hit did not scale its burst from the chill");
        if (!spent)
            GD.PushError("  shatter left chill behind or repeated on the next hit");

        return burst && spent;
    }

    private bool? StageSpreadReaction(int tick)
    {
        var knife = GD.Load<WeaponResource>("res://resources/weapons/combat_knife.tres");
        var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres");
        if (knife == null || axe == null)
        {
            GD.PushError("  spread weapons did not load — run BuildWeapons.cs");
            return false;
        }

        if (tick == 1)
        {
            _horde!.Pool.Clear();

            // Only the first brute is inside the knife's 1.6 m reach. All three
            // sit inside the axe's 3 m, 100-degree sweep.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(1.2f, 0.0f, 0.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(2.5f, 0.0f, 0.5f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(2.5f, 0.0f, -0.5f), 2);

            _weapons!.Equip(0, knife);
            _weapons.SetProficiency(WeaponCategory.MeleeShort, 0);
            _weapons.ForceFire(Vector2.Right);
            return null;
        }

        if (tick == 3)
        {
            _weapons!.Equip(0, axe);
            _weapons.SetProficiency(WeaponCategory.MeleeLong, 0);
            _weapons.ForceFire(Vector2.Right);
            return null;
        }

        if (tick < 5)
            return null;

        bool sourceSpent = _horde!.Pool.Bleed[0] <= 0.0f
                        && _horde.Pool.BleedRemaining[0] <= 0.0f;
        bool neighboursOpened = _horde.Pool.Bleed[1] >= knife.TraitAmount
                             && _horde.Pool.Bleed[2] >= knife.TraitAmount;

        GD.Print($"  wound after cleave: source {_horde.Pool.Bleed[0]:F1}/s, "
               + $"neighbours {_horde.Pool.Bleed[1]:F1}/{_horde.Pool.Bleed[2]:F1}/s");

        if (!sourceSpent)
            GD.PushError("  spread copied the wound without spending its source");
        if (!neighboursOpened)
            GD.PushError("  the cleave did not carry the wound across its sweep");

        return sourceSpent && neighboursOpened;
    }

    private bool? StageCookOff(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _horde.Hazards.Clear();
            _horde.Spawn(_player!.GlobalPosition + new Vector3(2.0f, 0.0f, 0.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(3.8f, 0.0f, 0.0f), 2);
            _horde.Hazards.Add(_horde.Pool.Position[0], 1.0f, 14.0f, 3.0f);
            return null;
        }

        if (tick == 3)
        {
            float sourceBefore = _horde!.Pool.Health[0];
            float neighbourBefore = _horde.Pool.Health[1];
            _horde.Detonate(_horde.Pool.Position[0], 0.5f, 5.0f);
            _markedHit = neighbourBefore - _horde.Pool.Health[1];
            _plainHit = sourceBefore - _horde.Pool.Health[0];
            bool spentNow = _horde.Pool.Burn[0] <= 0.0f && _horde.Pool.BurnRemaining[0] <= 0.0f;

            _horde.Hazards.Clear();
            float beforeAgain = _horde.Pool.Health[1];
            _horde.Detonate(_horde.Pool.Position[0], 0.5f, 5.0f);
            _ammoBefore = Mathf.RoundToInt((beforeAgain - _horde.Pool.Health[1]) * 100.0f);

            if (!spentNow)
                GD.PushError("  cook off left the burn tag behind");
            return null;
        }

        if (tick < 5)
            return null;

        float repeated = _ammoBefore / 100.0f;
        bool burst = _markedHit > 20.0f && _plainHit > _markedHit;
        bool spent = repeated < 0.01f && _horde!.Pool.Burn[0] <= 0.0f;

        GD.Print($"  first blast: source took {_plainHit:F1}, neighbour cook-off {_markedHit:F1}; "
               + $"second blast reached neighbour for {repeated:F1}, burn left {_horde.Pool.Burn[0]:F1}");

        if (!burst)
            GD.PushError("  burning the source did not create the secondary radius");
        if (!spent)
            GD.PushError("  cook off repeated without a new burn application");

        return burst && spent;
    }

    private bool? StageConduct(int tick)
    {
        var rifle = GD.Load<WeaponResource>("res://resources/weapons/scavenged_rifle.tres");
        if (rifle == null)
            return false;

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _weapons!.Equip(0, rifle);
            _weapons.SetProficiency(WeaponCategory.Firearm, 0);
            _player!.Mods.ChainChance = 1.0f;

            _horde.Spawn(_player.GlobalPosition + new Vector3(2.0f, 0.0f, 0.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(4.2f, 0.0f, 1.2f), 2);
            _weapons.ForceFire(Vector2.Right);
            _plainHit = _horde.Pool.ShockRemaining[1];

            // A clean three-body wall for the consumer half.
            _horde.Pool.Clear();
            _horde.Spawn(_player.GlobalPosition + new Vector3(2.0f, 0.0f, 0.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(4.0f, 0.0f, 1.0f), 2);
            _horde.Spawn(_player.GlobalPosition + new Vector3(4.0f, 0.0f, -1.0f), 2);
            for (int i = 0; i < 3; i++)
                _horde.Pool.Health[i] = 1000.0f;

            _horde.ApplyShock(0, 3.0f);
            _horde.ApplyChill(0, 0.4f, 3.0f);
            _player.Mods.ChainChance = 0.0f;

            float beforeOne = _horde.Pool.Health[1];
            float beforeTwo = _horde.Pool.Health[2];
            _weapons.ForceFire(Vector2.Right);
            _markedHit = beforeOne - _horde.Pool.Health[1];
            _speedBefore = beforeTwo - _horde.Pool.Health[2];

            // With Shock spent, the same hit cannot conduct again.
            beforeOne = _horde.Pool.Health[1];
            beforeTwo = _horde.Pool.Health[2];
            _weapons.ForceFire(Vector2.Right);
            _speedChilled = (beforeOne - _horde.Pool.Health[1]) + (beforeTwo - _horde.Pool.Health[2]);
            return null;
        }

        if (tick < 3)
            return null;

        float expected = rifle.BaseDamage * 0.40f;
        bool applied = _plainHit > 2.5f;
        bool jumped = Mathf.Abs(_markedHit - expected) < 0.2f
                   && Mathf.Abs(_speedBefore - expected) < 0.2f;
        bool spent = _horde!.Pool.ShockRemaining[0] <= 0.0f && _speedChilled < 0.01f;

        GD.Print($"  chain shock {_plainHit:F1}s; conduct {_markedHit:F1}/{_speedBefore:F1} "
               + $"(expected {expected:F1}), repeat {_speedChilled:F1}, shock left {_horde.Pool.ShockRemaining[0]:F1}s");

        if (!applied)
            GD.PushError("  the Chain card dealt damage but left no Shock");
        if (!jumped)
            GD.PushError("  a hit on chilled Shock did not reach two neighbours");
        if (!spent)
            GD.PushError("  Conduct did not spend Shock before the next hit");

        return applied && jumped && spent;
    }

    /// The second slot has to fire the weapon that is *in* it.
    ///
    /// **This is the one thing in the file that cannot be checked with
    /// `ForceFire`,** and that is exactly why the bug it defends against survived
    /// four phases. `ForceFire` fires `_slots[_active]`, every other stage here
    /// equips into slot 0, and `Fire` read the active slot's weapon while taking
    /// the slot as an argument — so the sidearm fired the *primary's* damage,
    /// trait, category and penetration, on its own cooldown and out of its own
    /// magazine. Every readout agreed with itself, every probe was green, and
    /// three of the four Sidearms measured identically in the balance table
    /// because all three were a Scavenged Rifle.
    ///
    /// So this stage does the one thing none of the others do: it lets the
    /// handler tick and fire on its own, with two *different* weapons loaded, and
    /// reads which one arrived. A knife at 1.6 m against a bow at 14 — the target
    /// stands at 9 m, where only the second slot can reach it. Under the bug the
    /// primary's reach came out of the sidearm and the target took damage; with
    /// the fix, only the weapon that can reach it does.
    private bool? StageSidearmFiresItself(int tick)
    {
        var knife = GD.Load<WeaponResource>("res://resources/weapons/combat_knife.tres");
        var bow = GD.Load<WeaponResource>("res://resources/weapons/hunting_bow.tres");
        if (knife == null || bow == null)
        {
            GD.PushError("  combat_knife.tres or hunting_bow.tres did not load");
            return false;
        }

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _weapons!.Projectiles.Clear();

            // Slot 0 is the blade and slot 1 reaches. Deliberately the opposite
            // way round from the game, so a stage that passes cannot be passing
            // because slot 0 happened to be the one that could do the job.
            _weapons.Equip(0, knife);
            _weapons.Equip(1, bow);
            _weapons.SetProficiency(WeaponCategory.MeleeShort, 0);
            _weapons.SetProficiency(WeaponCategory.BowCrossbow, 0);
            _weapons.LiveSlots = 2;

            // A brute at 9 m: outside the knife's 1.6 m by a long way, inside the
            // bow's 14. Give this measurement wall explicit health: the handler
            // may loose more than one arrow before the read tick as neighbouring
            // stages change timing, and a dead target takes the reading with it.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(9.0f, 0.0f, 0.0f), 2);

            // Health it cannot run out of, rather than a window tuned to fit
            // inside sixty hit points. The first version let the handler fire for
            // two seconds and the brute died on the third arrow — so the stage
            // reported "nothing reached 9 m" when what had actually happened was
            // that too much did. The window below is still short, but nothing now
            // depends on it staying short.
            if (_horde.Pool.Count > 0)
                _horde.Pool.Health[0] = 100_000.0f;

            // The one stage that lets the handler decide for itself. Everything
            // else here holds fire and drives `ForceFire`, which is what made the
            // second slot's firing path unreachable from a test.
            _weapons.HoldFire = false;
            _beforeAuto = _horde.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
            return null;
        }

        // Long enough for one arrow to loose and cross 9 m at 26 m/s, and short
        // enough that a second one never leaves. Two arrows plus a ricochet came
        // to exactly the brute's sixty health, and a target that dies takes the
        // reading with it — the same trap `DamageAtRange` carries a warning about.
        if (tick < 60)
            return null;

        _weapons!.HoldFire = true;

        bool alive = _horde!.Pool.Count > 0;
        float now = alive ? _horde.Pool.Health[0] : 0.0f;
        float taken = _beforeAuto - now;

        // The arrow is what reached it, so the hit must look like an arrow: the
        // bow's damage is well above the knife's, and the knife could not have
        // contributed at all from 9 m.
        bool reached = alive && taken >= bow.BaseDamage * 0.8f;

        GD.Print($"  knife in slot 0 (1.6 m), bow in slot 1 ({bow.BaseRange:F0} m), target at 9 m: "
               + $"took {taken:F1} (bow does {bow.BaseDamage:F1}, knife {knife.BaseDamage:F1})");

        if (!alive)
        {
            GD.PushError("  the measurement wall died — the firing stage has no health reading");
            return false;
        }

        if (!reached)
        {
            GD.PushError("  nothing reached 9 m — the second slot is not firing its own weapon, "
                       + "or it is not firing at all");
        }

        return reached;
    }

    private float _beforeAuto;

    private int _pulseShots;
    private int _pulseShotsWhileLocked;
    private bool _pulseWasHot;

    private bool? StageOverheat(int tick)
    {
        var pulse = GD.Load<WeaponResource>("res://resources/weapons/pulse_rifle.tres");
        if (pulse == null)
            return false;

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _weapons!.Equip(0, pulse);
            _weapons.SetProficiency(WeaponCategory.Tech, 0);
            _weapons.LiveSlots = 1;
            _weapons.HoldFire = false;
            _pulseShots = 0;
            _pulseShotsWhileLocked = -1;
            _pulseWasHot = false;
            _weapons.Fired += (weapon, _, _) =>
            {
                if (weapon.WeaponName == pulse.WeaponName)
                    _pulseShots++;
            };

            _horde.Spawn(_player!.GlobalPosition + new Vector3(8.0f, 0.0f, 0.0f), 2);
            if (_horde.Pool.Count > 0)
                _horde.Pool.Health[0] = 100_000.0f;
            return null;
        }

        if (_weapons!.OverheatedIn(0))
        {
            _pulseWasHot = true;
            if (_pulseShotsWhileLocked < 0)
                _pulseShotsWhileLocked = _pulseShots;
            else if (_pulseShots != _pulseShotsWhileLocked)
            {
                GD.PushError("  the pulse rifle fired while its overheat lock was active");
                return false;
            }
        }

        if (tick < 300)
            return null;

        _weapons.HoldFire = true;
        bool resumed = _pulseWasHot && _pulseShots > _pulseShotsWhileLocked;
        bool noAmmo = pulse.MagazineSize == 0
                      && _weapons.AmmoIn(0) == 0
                      && _weapons.ReserveIn(0) == 0;

        GD.Print($"  {_pulseShotsWhileLocked} shots to lock, {_pulseShots} after vent cycle, " +
                 $"heat {_weapons.HeatIn(0):P0}, ammo {_weapons.AmmoIn(0)}/{_weapons.ReserveIn(0)}");

        if (!resumed)
            GD.PushError("  the pulse rifle never resumed after cooling below its safe line");
        if (!noAmmo)
            GD.PushError("  the pulse rifle paid for firing with ammunition instead of heat");

        return resumed && noAmmo;
    }

    private float _beamStartHealth;
    private float _beamEarlyDamage;
    private bool _beamEarlyShockFree;
    private int _beamTicks;

    private bool? StageBeam(int tick)
    {
        var lance = GD.Load<WeaponResource>("res://resources/weapons/arc_lance.tres");
        if (lance == null)
            return false;

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _weapons!.Equip(0, lance);
            _weapons.SetProficiency(WeaponCategory.Tech, 0);
            _weapons.LiveSlots = 1;
            _weapons.HoldFire = false;
            _beamTicks = 0;
            _weapons.Fired += (weapon, _, _) =>
            {
                if (weapon.WeaponName == lance.WeaponName)
                    _beamTicks++;
            };
            _horde.Spawn(_player!.GlobalPosition + new Vector3(8.0f, 0.0f, 0.0f), 2);
            if (_horde.Pool.Count == 0)
                return false;
            _horde.Pool.Health[0] = 100_000.0f;
            _beamStartHealth = _horde.Pool.Health[0];
            return null;
        }

        if (tick == 30)
        {
            _beamEarlyDamage = _beamStartHealth - _horde!.Pool.Health[0];
            _beamEarlyShockFree = _horde.Pool.ShockRemaining[0] <= 0.0f;
        }

        if (tick < 60)
            return null;

        _weapons!.HoldFire = true;
        float totalDamage = _beamStartHealth - _horde!.Pool.Health[0];
        bool continuous = _beamTicks >= 7 && totalDamage > _beamEarlyDamage;
        bool dwelled = _beamEarlyShockFree && _horde.Pool.ShockRemaining[0] > 0.0f;
        bool noAmmo = lance.MagazineSize == 0 && _weapons.AmmoIn(0) == 0;

        GD.Print($"  {_beamTicks} beam ticks dealt {totalDamage:F1}; at 0.5s " +
                 $"damage {_beamEarlyDamage:F1}, shock={(!_beamEarlyShockFree)}; " +
                 $"at 1.0s shock {_horde.Pool.ShockRemaining[0]:F1}s");

        if (!continuous)
            GD.PushError("  the arc lance resolved like isolated shots instead of a held beam");
        if (!dwelled)
            GD.PushError("  beam shock did not wait for continuous dwell on one target");

        return continuous && dwelled && noAmmo;
    }
}
