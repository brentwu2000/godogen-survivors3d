using Godot;

/// Checks the four cards that fight without being aimed.
///
///   godot --headless --script test/KitProbe.cs
///
/// The weapon is silenced with `SetPhysicsProcess(false)` rather than unequipped
/// — `Equip(null)` throws — and it has to be, because every measurement here is
/// "did this enemy take damage" and a rifle firing on its own is a second source
/// of exactly that. The zone probe learned the same lesson counting a spawn burst
/// the player was shooting.
public partial class KitProbe : SceneTree
{
    private Player? _player;
    private Horde? _horde;
    private RunKit? _kit;
    private Node? _scene;
    private RunGrowth? _growth;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    private float _beforeHealth;
    private float _walkedWithoutChill;

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
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _scene = scene;
            _player = scene.GetNodeOrNull<Player>("Player");
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _kit = scene.GetNodeOrNull<RunKit>("RunKit");
            _growth = scene.GetNodeOrNull<RunGrowth>("RunGrowth");

            if (_player == null || _horde == null || _kit == null || _growth == null)
            {
                GD.PushError($"PROBE FAILED — player={_player != null} horde={_horde != null} " +
                             $"kit={_kit != null} growth={_growth != null}");
                Quit(1);
                return true;
            }

            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            // And the zones. The player spawns inside one on some seeds, and a
            // woken zone spawns from its perimeter and shoves the crowd apart —
            // which read as a walker travelling 15.94 m in two thirds of a
            // second. Everything here counts enemies or measures how far one
            // moved, so anything else that spawns or pushes is noise.
            foreach (Node child in scene.GetNodeOrNull("DangerZones")?.GetChildren()
                                   ?? new Godot.Collections.Array<Node>())
            {
                if (child is DangerZone zone)
                    zone.SetPhysicsProcess(false);
            }
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageDeckOffersThem, "the deck carries four kit cards, capped low");
            case 1: return RunStage(StageOrbitIsWhereTheCrowdIs, "the ring sweeps where enemies actually stand");
            case 2: return RunStage(StageOrbitTicks, "the blades damage on an interval, not per frame");
            case 3: return RunStage(StageShockwaveFires, "the shockwave goes off on its own and pushes");
            case 4: return RunStage(StageChillIsLocal, "chill slows what is near and nothing far away");
            case 5: return RunStage(StageChillNeverStops, "stacking chill approaches a limit rather than crossing it");
            case 6: return RunStage(StageResetClearsEverything, "a run's modifiers do not survive into the next one");
            case 7: return RunStage(StageCardsAreVisible, "every card puts something on screen when it works");
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

    /// `Reset()` has to clear every field, including ones added after it was
    /// written.
    ///
    /// By reflection rather than by listing them, because a list here would have
    /// exactly the same hole as the list in `Reset` — it is the *enumerating by
    /// hand* that fails, and a test that enumerates by hand fails the same way on
    /// the same day.
    ///
    /// This is not hypothetical. The four kit fields were added and not listed,
    /// so three orbiting blades bought in one run carried into the next; the
    /// first sign of it was an enemy dying in a stage that had switched the
    /// blades off, and the swap-remove that followed made a walker appear to
    /// travel eleven metres a second.
    private bool? StageResetClearsEverything(int tick)
    {
        var fresh = new RunModifiers();
        var dirtied = new RunModifiers();

        System.Reflection.FieldInfo[] fields = typeof(RunModifiers).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Something no default would be. Every field here is a float or an int,
        // and both take this cleanly.
        foreach (System.Reflection.FieldInfo field in fields)
        {
            if (field.FieldType == typeof(float))
                field.SetValue(dirtied, 3.7f);
            else if (field.FieldType == typeof(int))
                field.SetValue(dirtied, 9);
        }

        dirtied.Reset();

        bool ok = true;
        var missed = new System.Collections.Generic.List<string>();

        foreach (System.Reflection.FieldInfo field in fields)
        {
            object? after = field.GetValue(dirtied);
            object? expected = field.GetValue(fresh);

            if (Equals(after, expected))
                continue;

            missed.Add($"{field.Name}={after}");
            ok = false;
        }

        GD.Print($"  {fields.Length} fields dirtied and reset; " +
                 (ok ? "all back to their defaults" : $"still dirty: {string.Join(", ", missed)}"));

        if (!ok)
        {
            GD.PushError($"  Reset() does not clear {missed.Count} field(s) — " +
                         "they carry from one run into the next");
        }

        return ok;
    }

    private static readonly GrowthOption[] Kit =
        { GrowthOption.Orbit, GrowthOption.Shockwave, GrowthOption.Chain, GrowthOption.Chill };

    private bool? StageDeckOffersThem(int tick)
    {
        bool ok = true;

        foreach (GrowthOption option in Kit)
        {
            if (RunGrowth.RarityOf(option) != GrowthRarity.Kit)
            {
                GD.PushError($"  {option} is not Kit rarity — it will draw as often as a stat");
                ok = false;
            }

            int cap = _growth!.CapFor(option);
            if (cap is <= 0 or > 5)
            {
                GD.PushError($"  {option} caps at {cap}; kit is meant to be 3 to 5");
                ok = false;
            }
        }

        // A kit card has to draw less often than a stat and more often than never.
        float kit = _growth!.WeightOf(GrowthOption.Orbit);
        float common = _growth.WeightOf(GrowthOption.Pierce);
        float weapon = _growth.WeightOf(GrowthOption.WeaponLevel);

        GD.Print($"  weights: kit {kit:F2}, common {common:F2}, weapon {weapon:F2}; " +
                 $"caps {string.Join("/", System.Array.ConvertAll(Kit, o => _growth.CapFor(o)))}");

        // And the weapon has to stay prominent. Four new entries at common weight
        // would have quietly cut how often it comes up, which turns every run
        // into a starting rifle with adjectives.
        if (weapon <= common * 2.0f)
        {
            GD.PushError($"  the weapon draws at {weapon:F2} against {common:F2} for a stat — " +
                         "it will be crowded out");
            ok = false;
        }

        return ok;
    }

    /// The radius is the card, and the number that makes it work is 1.5.
    ///
    /// Enemies stop at the horde's contact radius to bite. A ring outside that
    /// sweeps ground a walker crosses once on the way in and then never occupies
    /// — it would hit each enemy exactly once and then spin harmlessly while they
    /// ate the player. Checked as a relationship rather than as a constant, so it
    /// still means something if either number is retuned.
    private bool? StageOrbitIsWhereTheCrowdIs(int tick)
    {
        float ring = _kit!.OrbitRadius;
        float bite = _kit.OrbitBite;
        float contact = _horde!.ContactRadius;

        GD.Print($"  ring at {ring:F2} m with a {bite:F2} m bite; enemies stop at {contact:F2} m");

        // The blade's reach has to cover where an enemy comes to rest.
        bool covers = ring - bite <= contact;
        if (!covers)
        {
            GD.PushError($"  the ring's inner edge is {ring - bite:F2} m out and enemies stop at " +
                         $"{contact:F2} — the blades sweep empty ground");
        }

        return covers;
    }

    private bool? StageOrbitTicks(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _player!.Mods.Reset();
            _player.Mods.OrbitBlades = 3;

            // At the contact radius, which is where an enemy actually comes to
            // rest — not arbitrarily close. At 0.5 m the target sits exactly
            // 1.0 m from the ring, which is the bite distance to four decimal
            // places, so whether a blade reaches it depends on the spin landing
            // on precisely the right angle. That is not a test of the ring, it is
            // a test of floating point.
            _horde.Spawn(_player.GlobalPosition + new Vector3(_horde.ContactRadius, 0.0f, 0.0f), 2);
            _beforeHealth = _horde.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
            return null;
        }

        // A little over one interval: enough for exactly one or two ticks, not
        // enough for a per-frame implementation to be mistaken for a working one.
        if (tick < 25)
            return null;

        if (_horde!.Pool.Count == 0)
        {
            GD.PushError("  the target died in under half a second — the ring is damaging per frame");
            return false;
        }

        float taken = _beforeHealth - _horde.Pool.Health[0];
        float perTick = _kit!.OrbitDamage;
        int blades = _player!.Mods.OrbitBlades;

        GD.Print($"  {blades} blades over {25 / 60.0f:F2}s took {taken:F1} " +
                 $"(one tick of one blade is {perTick:F1}, interval {_kit.OrbitInterval:F2}s)");

        bool hurt = taken > 0.0f;

        // Bounded by what the interval and the spin allow. A blade sweeps
        // OrbitSpin x OrbitInterval radians per tick, so at most that many
        // blades' worth of passes can land on one enemy — per-frame damage at
        // 60 Hz would be twenty-five times this.
        float ticks = 25 / 60.0f / _kit.OrbitInterval + 1.0f;
        float ceiling = perTick * blades * ticks;
        bool metered = taken <= ceiling;

        if (!hurt)
            GD.PushError("  the ring did nothing to an enemy standing inside it");
        if (!metered)
            GD.PushError($"  {taken:F1} against a ceiling of {ceiling:F1} — the interval is not being respected");

        return hurt && metered;
    }

    private bool? StageShockwaveFires(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _player!.Mods.Reset();
            _player.Mods.PulseStacks = 4;   // shortest interval, so the test is short

            _horde.Spawn(_player.GlobalPosition + new Vector3(3.0f, 0.0f, 0.0f), 2);
            _beforeHealth = _horde.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
            _pulses = 0;
            _kit!.Pulsed += (_, _) => _pulses++;
            return null;
        }

        // Long enough for at least one pulse at four stacks.
        if (tick < 4 * 60)
            return null;

        float taken = _horde!.Pool.Count > 0 ? _beforeHealth - _horde.Pool.Health[0] : _beforeHealth;

        GD.Print($"  4 stacks over 4s: {_pulses} pulse(s), target took {taken:F1}");

        bool fired = _pulses > 0;
        bool hurt = taken > 0.0f;

        if (!fired)
            GD.PushError("  no pulse in four seconds — the shockwave never goes off");
        if (!hurt)
            GD.PushError("  a pulse fired and the enemy inside it took nothing");

        return fired && hurt;
    }

    private int _pulses;

    /// A card that works and cannot be seen is a card that does not work.
    ///
    /// **This is the bug that shipped.** `RunKit.Pulsed` was declared, invoked,
    /// and carried a comment explaining that the effect director draws it "because
    /// the effect director owns every particle in the game" — and nothing ever
    /// subscribed. The shockwave damaged, knocked back, and produced no light at
    /// all for its entire life. Every existing stage above passed the whole time,
    /// because every one of them asks what the card *did* and none asks whether
    /// anybody could tell.
    ///
    /// Counted rather than sampled. A puff lives about a third of a second, so a
    /// probe reading the live count a few ticks later sees whatever happens to
    /// still be alive; `TotalSpawned` is a running total and cannot be missed by
    /// looking at the wrong moment.
    ///
    /// The chill is checked differently because it is not an event: it is a mesh
    /// that exists whenever the card is held, so the question is whether the node
    /// is there and visible rather than whether something was emitted.
    private bool? StageCardsAreVisible(int tick)
    {
        EffectDirector? effects = _scene?.GetNodeOrNull<EffectDirector>("Effects");
        if (effects == null)
        {
            GD.PushError("  no EffectDirector in the scene");
            return false;
        }

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _player!.Mods.Reset();
            _player.Mods.PulseStacks = 4;
            _player.Mods.Chill = 0.5f;

            _horde.Spawn(_player.GlobalPosition + new Vector3(3.0f, 0.0f, 0.0f), 0);

            // Zeroed here, so what is counted is what this stage caused and not
            // the muzzle flashes of six stages of shooting before it.
            effects.Effects.ForgetTotals();
            return null;
        }

        if (tick < 4 * 60)
            return null;

        int spawned = effects.Effects.TotalSpawned;

        // The frost, which is geometry rather than an event.
        Node? frost = _kit?.GetNodeOrNull("Frost");
        bool frostShown = frost is MeshInstance3D { Visible: true };

        GD.Print($"  4 s with pulse and chill: {spawned} puff(s) emitted, "
               + $"frost {(frost == null ? "missing" : frostShown ? "visible" : "hidden")}");

        bool ok = true;

        if (spawned <= 0)
        {
            GD.PushError("  the shockwave fired and emitted nothing — "
                       + "`Pulsed` is not connected to anything");
            ok = false;
        }

        if (!frostShown)
        {
            GD.PushError("  chill is held and the ground shows nothing — "
                       + "the player cannot see where the slow applies");
            ok = false;
        }

        return ok;
    }

    /// Chill is about the ground the player is standing on, not a global debuff.
    private bool? StageChillIsLocal(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _player!.Mods.Reset();

            // One close, one past the chill radius but still inside the horde's
            // active radius. Beyond that the horde strides enemies rather than
            // stepping them every tick, and a distance measured over two thirds
            // of a second lands on whichever side of a stride it happens to.
            _horde.Spawn(_player.GlobalPosition + new Vector3(2.0f, 0.0f, 0.0f), 0);
            _horde.Spawn(_player.GlobalPosition
                         + new Vector3(_horde.ChillRadius + 3.5f, 0.0f, 0.0f), 0);
            return null;
        }

        if (tick == 2)
        {
            _nearStart = _horde!.Pool.Position[0];
            _farStart = _horde.Pool.Position[1];
            return null;
        }

        if (tick < 40)
            return null;

        if (tick == 40)
        {
            _nearWithout = Flat(_horde!.Pool.Position[0] - _nearStart);
            _farWithout = Flat(_horde.Pool.Position[1] - _farStart);

            // Now with chill, from the same starting places.
            _player!.Mods.Chill = 0.6f;
            _horde.Pool.Position[0] = _nearStart;
            _horde.Pool.Position[1] = _farStart;
            return null;
        }

        if (tick < 80)
            return null;

        float nearWith = Flat(_horde!.Pool.Position[0] - _nearStart);
        float farWith = Flat(_horde.Pool.Position[1] - _farStart);

        GD.Print($"  near the player: {_nearWithout:F2} m -> {nearWith:F2} m with chill");
        GD.Print($"  past the radius: {_farWithout:F2} m -> {farWith:F2} m with chill");

        bool slowedNear = nearWith < _nearWithout * 0.85f;
        bool untouchedFar = farWith > _farWithout * 0.95f;

        if (!slowedNear)
            GD.PushError($"  an enemy 2 m away moved {nearWith:F2} against {_nearWithout:F2} — chill did nothing");
        if (!untouchedFar)
            GD.PushError($"  an enemy past the radius was slowed too — chill is a global debuff");

        return slowedNear && untouchedFar;
    }

    private Vector3 _nearStart;
    private Vector3 _farStart;
    private float _nearWithout;
    private float _farWithout;

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    /// Stacking has to approach a limit, not cross it.
    ///
    /// Arithmetic rather than simulation: what matters is the shape of the
    /// series, and three stacks of an additive 17% is 51% while a fourth would be
    /// 68% and a sixth would stop a walker dead. An enemy that cannot move is not
    /// a threat that has been managed.
    private bool? StageChillNeverStops(int tick)
    {
        _player!.Mods.Reset();

        int cap = _growth!.CapFor(GrowthOption.Chill);
        for (int i = 0; i < cap + 6; i++)
            _growth.GrantForTesting(GrowthOption.Chill);

        float chill = _player.Mods.Chill;
        GD.Print($"  {cap + 6} stacks of chill (cap is {cap}) reach {chill:P1}");

        bool underOne = chill < 0.999f;
        if (!underOne)
            GD.PushError($"  chill reached {chill:P1} — an enemy stopped dead is a free kill, not a fight");

        // And it has to actually do something at the cap, or the card is a
        // rounding error the player paid three picks for.
        _player.Mods.Reset();
        for (int i = 0; i < cap; i++)
            _growth.GrantForTesting(GrowthOption.Chill);

        float atCap = _player.Mods.Chill;
        GD.Print($"  at the cap of {cap}: {atCap:P1}");

        bool worthIt = atCap > 0.25f;
        if (!worthIt)
            GD.PushError($"  {atCap:P1} at the cap — three picks for nothing the player can feel");

        return underOne && worthIt;
    }
}
