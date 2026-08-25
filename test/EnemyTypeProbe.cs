using Godot;

/// Checks that a variant is actually a variant: that each one moves, hurts,
/// resists and dies by its own row of the table rather than by the walker's.
///
///   godot --headless --script test/EnemyTypeProbe.cs
///
/// Exit code is the verdict. The run director and the weapon handler are both
/// stopped — one would spawn into every count, the other would kill the subjects
/// mid-measurement.
public partial class EnemyTypeProbe : SceneTree
{
    private const int Walker = 0;
    private const int Runner = 1;
    private const int Brute = 2;
    private const int Bloater = 3;
    private const int Spitter = 4;

    private Horde? _horde;
    private Player? _player;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    // Filled by the EnemyKilled subscription, drained by the stage that cares.
    private int _kills;
    private int _lastKilledType = -1;

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
            _player = scene.GetNodeOrNull<Player>("Player");

            if (_horde == null || _player == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);

            _horde.EnemyKilled += OnEnemyKilled;

            if (!CheckTable())
            {
                Quit(1);
                return true;
            }
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageMoveSpeed, "per-variant move speed");
            case 1: return RunStage(StageContactDamage, "per-variant contact damage");
            case 2: return RunStage(StageKnockback, "brute resists knockback");
            case 3: return RunStage(StageBlast, "bloater blast, one level deep");
            case 4: return RunStage(StageSpitter, "spitter stands off and shoots");
            case 5: return RunStage(StageComposition, "composition follows intensity");
            case 6: return RunStage(StageDrawnHeight, "each variant stands at its designed height");
            case 7: return RunStage(StageHitFlash, "a hit that does not kill lights the target, briefly");
            case 8: return RunStage(StageSolidHeight, "and stands at it as a solid body too");
            case 9: return RunStage(StageNothingIsOneShot, "nothing in the roster dies to one round from the starting rifle");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private void OnEnemyKilled(int type, Vector3 position)
    {
        _kills++;
        _lastKilledType = type;
    }

    /// The same question of the path the game actually ships.
    ///
    /// **The stage above measured the sprite and passed while the bodies were
    /// wrong.** `SpriteScale` exists to cancel a sprite's fill fraction — a brute
    /// painting filling 71.5% of its frame needs 2.098 to come out three metres
    /// tall — and `BodyRenderer` was multiplying the *mesh* by it as well. A mesh
    /// has no fill fraction: it is built at `DesignHeightMeters` and a bake is
    /// refused unless it stands at that height, so it is already the right size.
    ///
    /// The result was that every variant whose art did not fill its frame was
    /// drawn at the wrong size on the solid path: the brute at 6.3 m instead of
    /// 3.0, the bloater at 3.8, and the boss at **seventeen metres** instead of
    /// five and a half. It never looked like a bug — a boss is supposed to be
    /// enormous, and the two variants the eye calibrates on, the walker and the
    /// spitter, both fill their frames and scale by exactly 1.0.
    ///
    /// Asked of `BodyRenderer` rather than recomputed here, so that putting
    /// `SpriteScale` back into the instance scale fails this rather than passing
    /// a test of arithmetic nobody changed.
    private bool? StageSolidHeight(int tick)
    {
        bool ok = true;

        foreach (EnemyTypeResource type in _horde!.Types)
        {
            float drawn = BodyRenderer.DrawnHeight(type);

            // What the body is *supposed* to measure, which is not always its
            // design height.
            //
            // A leaning variant genuinely stands shorter than it is long: the
            // runner is tipped twenty-six degrees at the hip, so 1.8 m of body
            // occupies 1.71 m of vertical space, and that is correct rather than
            // a scaling error. `BodyMeshLibrary.StandingHeight` is the library's
            // own answer for a spec and already accounts for it — it is what
            // `BodyShot` frames against.
            //
            // Comparing to the design height with a loose tolerance would have
            // worked too, and would have been a band wide enough to hide a real
            // error. This is exact.
            float expected = string.IsNullOrEmpty(type.BakedBodyPath)
                ? BodyMeshLibrary.StandingHeight(
                      BodyMeshLibrary.ForVariant(type.TypeName, type.DesignHeightMeters))
                : type.DesignHeightMeters;

            bool matches = Mathf.Abs(drawn - expected) <= 0.05f;
            float lean = expected - type.DesignHeightMeters;

            GD.Print($"  {type.TypeName,-8} body draws {drawn:F2} m against {expected:F2} m"
                   + (Mathf.Abs(lean) > 0.02f ? $" ({type.DesignHeightMeters:F1} m less {-lean:F2} of lean)" : "")
                   + (matches ? "" : " <-- off"));

            ok &= matches;
        }

        return ok;
    }

    /// How tall each variant actually draws, measured from the sprite rather than
    /// assumed from the scale.
    ///
    /// The quad is one size for every layer, so a variant that does not fill its
    /// frame draws shorter than its scale suggests, and the scale is expected to
    /// have paid that back. Nothing enforces that: re-fitting the art changes the
    /// fill, `BuildEnemyTypes.cs` holds a number written by hand, and a brute that
    /// quietly became 2.4 m tall looks exactly like a brute. This is the only
    /// place the two halves are compared.
    ///
    /// Only for variants drawn as billboards. A variant that names a baked body
    /// is drawn from geometry whose height is baked in, checked by `BakeProbe`
    /// against the same table and again by `BodyRenderer` before it will use it —
    /// and it has no sprite to measure, because the billboard path is a fallback
    /// that substitutes a placeholder rather than requiring 2D art for every
    /// authored creature.
    ///
    /// Skipped, never silently. A variant with neither a sprite nor a bake has
    /// nothing at all to draw it, which is worth failing over.
    private bool? StageDrawnHeight(int tick)
    {
        bool ok = true;
        float quad = _horde!.SpriteHeight;

        foreach (EnemyTypeResource type in _horde.Types)
        {
            Image? sprite = GD.Load<Texture2D>($"res://assets/sprites/enemies/{type.TypeName}.png")?.GetImage();
            if (sprite == null)
            {
                if (!string.IsNullOrEmpty(type.BakedBodyPath))
                {
                    GD.Print($"  {type.TypeName,-8} drawn from {type.BakedBodyPath} — "
                           + "no sprite to measure, height checked by BakeProbe");
                    continue;
                }

                GD.PushError($"  {type.TypeName} has neither a sprite nor a baked body");
                ok = false;
                continue;
            }

            float fill = VisibleFraction(sprite);
            float drawn = quad * type.SpriteScale * fill;
            bool matches = Mathf.Abs(drawn - type.DesignHeightMeters) <= 0.05f;

            GD.Print($"  {type.TypeName,-8} fills {fill * 100.0f:F1}% x scale {type.SpriteScale:F3} " +
                     $"= {drawn:F2} m, designed {type.DesignHeightMeters:F1} m {(matches ? "" : "<-- off")}");
            ok &= matches;
        }

        return ok;
    }

    /// Hit feedback is the one thing here nothing else can check: it is invisible
    /// to every other assertion, it is the difference between a weapon that feels
    /// broken and one that does not, and the decay is on a separate loop from the
    /// movement one specifically so that a distant target does not stay lit. That
    /// separation is exactly the kind of thing a later refactor folds back in.
    private bool? StageHitFlash(int tick)
    {
        const float damage = 1.0f;

        if (tick == 1)
        {
            _horde!.Pool.Clear();

            // Far enough out to be on the reduced update stride, which is where a
            // flash decayed inside the movement loop would get stuck.
            _horde.Spawn(_player!.GlobalPosition + new Vector3(30.0f, 0.0f, 0.0f), Brute);
            return null;
        }

        if (tick == 2)
        {
            _horde!.Damage(0, damage, Vector2.Zero);
            _lit = _horde.Pool.Count > 0 ? _horde.Pool.HitFlash[0] : -1.0f;
            return null;
        }

        // Long enough for the fade to finish, short enough that a fade an order
        // of magnitude slower would still be caught.
        if (tick < 20)
            return null;

        float faded = _horde!.Pool.Count > 0 ? _horde.Pool.HitFlash[0] : -1.0f;
        GD.Print($"  brute at 30 m: flash {_lit:F2} on the hit, {faded:F2} after {(tick - 2) / 60.0f:F2}s");

        return Mathf.IsEqualApprox(_lit, 1.0f) && faded <= 0.0f;
    }

    private float _lit;

    /// Fraction of the sprite's height occupied by pixels the shader will keep.
    /// The scissor threshold, not "any alpha at all" — a matte leaves the whole
    /// background at a nonzero alpha that draws nothing.
    private static float VisibleFraction(Image sprite)
    {
        if (sprite.IsCompressed())
            sprite.Decompress();
        sprite.Convert(Image.Format.Rgba8);

        int top = -1, bottom = -1;
        for (int y = 0; y < sprite.GetHeight(); y++)
        {
            for (int x = 0; x < sprite.GetWidth(); x++)
            {
                if (sprite.GetPixel(x, y).A < 0.5f)
                    continue;

                if (top < 0)
                    top = y;
                bottom = y;
                break;
            }
        }

        return top < 0 ? 0.0f : (bottom - top + 1) / (float)sprite.GetHeight();
    }

    /// The starting weapon must not delete a variant in one trigger pull.
    ///
    /// **It used to delete four of the nine.** The scavenged rifle does 12, and
    /// the walker had 10, the runner 4, the spitter 8 and the stalker 8 — between
    /// them the whole of the early crowd. What that produces is a first minute
    /// with no reading in it: every arrival is one round whatever it is, so the
    /// roster's differences do not begin until the brute, and the opening ninety
    /// seconds of every run in the game are the same ninety seconds.
    ///
    /// Read against the weapon rather than against a number typed in here, so
    /// re-tuning the rifle cannot silently reintroduce it — a constant copied out
    /// of the data is a claim about the data that stops being checked the moment
    /// it is copied.
    ///
    /// The upper bound is the other half and it matters as much. "Nothing is
    /// one-shot" is trivially satisfied by giving everything five hundred health,
    /// and a crowd that takes eight rounds each is a different game rather than a
    /// legible one. Six rounds is the ceiling for anything that arrives in
    /// numbers; the brute, the bulwark and the boss are exempt because being a
    /// wall is what they are for.
    private bool? StageNothingIsOneShot(int tick)
    {
        var rifle = GD.Load<WeaponResource>("res://resources/weapons/scavenged_rifle.tres");
        if (rifle == null)
        {
            GD.PushError("  the starting rifle did not load");
            return false;
        }

        float shot = rifle.BaseDamage;
        string[] walls = { "brute", "bulwark", "boss" };

        bool ok = true;
        var report = new System.Collections.Generic.List<string>();

        foreach (EnemyTypeResource type in _horde!.Types)
        {
            int rounds = Mathf.CeilToInt(type.MaxHealth / shot);
            report.Add($"{type.TypeName} {rounds}");

            if (rounds < 2)
            {
                GD.PushError($"  {type.TypeName} dies to one round of {shot:F0} "
                           + $"({type.MaxHealth:F0} health)");
                ok = false;
            }

            if (rounds > 6 && System.Array.IndexOf(walls, type.TypeName) < 0)
            {
                GD.PushError($"  {type.TypeName} takes {rounds} rounds — that is a wall, "
                           + "and it arrives in numbers");
                ok = false;
            }
        }

        GD.Print($"  rounds from the starting rifle ({shot:F0} damage): "
               + string.Join(", ", report));

        return ok;
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

    /// Pure data checks. A wrong row is reported as a wrong row, rather than as a
    /// puzzling combat result three stages later.
    private bool CheckTable()
    {
        EnemyTypeResource[] types = _horde!.Types;

        // Against `Horde.TypeNames` rather than against a number written here.
        //
        // The real property is that the table and the name list agree: the sprite
        // array is built from the names in order, so a row appearing in one and
        // not the other means every layer after the gap draws the wrong creature.
        // A literal count tests that *and* forbids ever adding a variant, which is
        // not a property anybody wanted — it failed the moment the stalker landed,
        // and this repository has already had a probe hide three new weapons by
        // hardcoding a list of six.
        if (types.Length != Horde.TypeNames.Length)
        {
            GD.PushError($"PROBE FAILED — {types.Length} variants loaded against "
                       + $"{Horde.TypeNames.Length} names in Horde.TypeNames");
            return false;
        }

        for (int i = 0; i < types.Length; i++)
        {
            if (types[i].TypeName == Horde.TypeNames[i])
                continue;

            GD.PushError($"PROBE FAILED — slot {i} is '{types[i].TypeName}' in the table and "
                       + $"'{Horde.TypeNames[i]}' in the name list");
            return false;
        }

        bool layersMatch = true;
        bool stridesSane = true;

        for (int i = 0; i < types.Length; i++)
        {
            layersMatch &= types[i].SpriteLayer == i;

            // A power of two, because the scheduler spreads work with a bit mask
            // — a stride of 3 would quietly never match for most indices.
            int stride = types[i].FarStride;
            stridesSane &= stride > 0 && (stride & (stride - 1)) == 0;
        }

        // The whole reason the stride is derived: the fast variant must not be
        // the one taking the longest catch-up steps.
        bool runnerTighter = types[Runner].FarStride < types[Walker].FarStride;

        GD.Print($"variants {types.Length}, strides " +
                 $"walker {types[Walker].FarStride} runner {types[Runner].FarStride} " +
                 $"brute {types[Brute].FarStride}");

        bool ok = layersMatch && stridesSane && runnerTighter;
        if (!ok)
        {
            GD.PushError($"PROBE FAILED — table: layers={layersMatch} strides={stridesSane} " +
                         $"runnerTighter={runnerTighter}");
        }
        else
        {
            GD.Print("variant table: ok");
        }

        return ok;
    }

    /// Each variant's speed is its own. Measured from velocity rather than
    /// displacement because the flow direction is a unit vector, which makes the
    /// expected magnitude exactly MoveSpeed with nothing to integrate.
    private bool? StageMoveSpeed(int tick)
    {
        if (tick == 1)
        {
            Reset();

            // Inside ActiveRadius so every one runs at full rate, 3m apart so
            // none of them is pushing on another, and clear of the contact ring.
            _horde!.Spawn(new Vector3(-10.0f, 0.0f, 0.0f), Walker);
            _horde.Spawn(new Vector3(-10.0f, 0.0f, 3.0f), Runner);
            _horde.Spawn(new Vector3(-10.0f, 0.0f, 6.0f), Brute);
            _horde.Spawn(new Vector3(-10.0f, 0.0f, 9.0f), Bloater);
            return null;
        }

        if (tick < 20)
            return null;

        EnemyPool pool = _horde!.Pool;
        EnemyTypeResource[] types = _horde.Types;
        bool ok = pool.Count == 4;

        for (int i = 0; i < pool.Count; i++)
        {
            float expected = types[pool.Type[i]].MoveSpeed;
            float actual = pool.Velocity[i].Length();
            ok &= Mathf.Abs(actual - expected) < 0.05f;
            GD.Print($"  {types[pool.Type[i]].TypeName}: {actual:F2} m/s (table {expected:F2})");
        }

        return ok;
    }

    /// A brute leaning on you costs more than a walker does. The exact figure
    /// matters less than the ratio being the one the table asked for.
    private bool? StageContactDamage(int tick)
    {
        const int Ticks = 60;

        if (tick == 1)
        {
            Reset();
            _horde!.Spawn(_player!.GlobalPosition + new Vector3(0.2f, 0.0f, 0.0f), Walker);
            return null;
        }

        if (tick == Ticks)
        {
            _walkerDamage = _player!.MaxHealth - _player.Health;
            Reset();
            _horde!.Spawn(_player.GlobalPosition + new Vector3(0.2f, 0.0f, 0.0f), Brute);
            return null;
        }

        if (tick < Ticks * 2)
            return null;

        float bruteDamage = _player!.MaxHealth - _player.Health;
        EnemyTypeResource[] types = _horde!.Types;

        // Both stages ran the same number of ticks, so the ratio of damage is the
        // ratio of the rates that actually reached the player — which is not the
        // ratio in the table, and the difference is the whole design of armour.
        //
        // This compared against the raw table ratio, on the reasoning that a
        // common factor cancels. Armour is not a common factor: it is subtracted
        // from the rate, deliberately, so that it answers a crowd of weak contacts
        // and never answers a brute. Flat mitigation does not cancel in a ratio,
        // it *widens* it — and the wider the gap the better armour is working.
        //
        // It went unnoticed because the player used to start a run with none. A
        // single point from a starting loadout turned 14:6 into 13:5, which is
        // 2.60 rather than 2.33, and the probe reported a damage table that had
        // not changed as broken. `GrowthProbe` was tripped by the same one point
        // on the same day.
        float armour = _player.Armour;
        float walkerRate = Mitigate(types[Walker].ContactDamagePerSecond, armour);
        float bruteRate = Mitigate(types[Brute].ContactDamagePerSecond, armour);

        float expectedRatio = bruteRate / walkerRate;
        float actualRatio = bruteDamage / Mathf.Max(0.001f, _walkerDamage);

        GD.Print($"  walker {_walkerDamage:F1} vs brute {bruteDamage:F1} over 1s " +
                 $"-> ratio {actualRatio:F2} (expected {expectedRatio:F2} from " +
                 $"{types[Walker].ContactDamagePerSecond:F0}/{types[Brute].ContactDamagePerSecond:F0} " +
                 $"less {armour:F0} armour)");

        // The absolute rates too, not just their ratio. A ratio alone passes if
        // both halves are wrong by the same factor, which is exactly what would
        // happen if contact damage stopped being applied every tick — and the
        // ratio is the thing this stage was already checking when it missed a
        // real change in mitigation.
        //
        // One tick of tolerance each way: the enemy is spawned 0.2 m from the
        // player and is inside the contact radius immediately, but the tick it
        // spawns on does no damage.
        float slack = bruteRate / 60.0f * 2.0f;
        bool walkerRight = Mathf.Abs(_walkerDamage - walkerRate) < slack;
        bool bruteRight = Mathf.Abs(bruteDamage - bruteRate) < slack;

        if (!walkerRight)
            GD.PushError($"  walker dealt {_walkerDamage:F2} over 1s, expected {walkerRate:F2}");
        if (!bruteRight)
            GD.PushError($"  brute dealt {bruteDamage:F2} over 1s, expected {bruteRate:F2}");

        return _walkerDamage > 0.0f
            && walkerRight
            && bruteRight
            && Mathf.Abs(actualRatio - expectedRatio) < 0.15f;
    }

    /// `Player.Mitigate`, mirrored.
    ///
    /// Copied rather than called because the method is private and making it
    /// public to satisfy a test would widen the player's surface for the
    /// convenience of one assertion. Twenty percent always gets through — armour
    /// that reached zero would turn the weakest variant into scenery.
    private static float Mitigate(float rate, float armour) =>
        rate <= 0.0f ? 0.0f : Mathf.Max(rate - armour, rate * 0.2f);

    private float _walkerDamage;

    /// The same shove moves a brute a fifth as far. Resistance is a multiplier,
    /// not a threshold, so it is visible rather than absolute.
    private bool? StageKnockback(int tick)
    {
        if (tick < 2)
        {
            Reset();
            return null;
        }

        var origin = new Vector3(-8.0f, 0.0f, 0.0f);
        _horde!.Spawn(origin, Walker);
        _horde.Spawn(origin + new Vector3(0.0f, 0.0f, 5.0f), Brute);

        EnemyPool pool = _horde.Pool;
        var push = new Vector2(1.0f, 0.0f);

        // One point of damage: enough to register the hit, never enough to kill
        // either of them and lose the position being measured.
        Vector3 walkerBefore = pool.Position[0];
        Vector3 bruteBefore = pool.Position[1];
        _horde.Damage(0, 1.0f, push);
        _horde.Damage(1, 1.0f, push);

        float walkerMoved = pool.Position[0].X - walkerBefore.X;
        float bruteMoved = pool.Position[1].X - bruteBefore.X;
        float expected = _horde.Types[Brute].KnockbackScale;
        float actual = bruteMoved / Mathf.Max(0.0001f, walkerMoved);

        GD.Print($"  walker pushed {walkerMoved:F3}m, brute {bruteMoved:F3}m -> " +
                 $"{actual:F2}x (table {expected:F2}x)");

        return Mathf.Abs(actual - expected) < 0.02f;
    }

    /// A bloater kills at arm's length after it dies — and its blast kills a
    /// second bloater without that one blasting in turn. The witness sits inside
    /// the second bloater's radius but outside the first's, so a chain reaction
    /// is the only thing that could reach it.
    private bool? StageBlast(int tick)
    {
        if (tick < 2)
        {
            Reset();
            return null;
        }

        float radius = _horde!.Types[Bloater].DeathBlastRadius;
        float blastDamage = _horde.Types[Bloater].DeathBlastDamage;

        // Well away from the player: this stage is about the horde, and a blast
        // landing on the player would confuse the damage accounting below.
        var origin = new Vector3(-30.0f, 0.0f, -30.0f);

        _horde.Spawn(origin, Bloater);                                        // 0
        _horde.Spawn(origin + new Vector3(1.5f, 0.0f, 0.0f), Bloater);        // 1, inside 0's blast
        _horde.Spawn(origin + new Vector3(4.0f, 0.0f, 0.0f), Walker);         // 2, witness

        // The witness is 4.0m from the first bloater and 2.5m from the second:
        // outside one radius, inside the other.
        bool geometryValid = radius > 2.5f && radius < 4.0f;

        _kills = 0;
        _horde.Damage(0, 999.0f, Vector2.Zero);

        int survivors = _horde.Pool.Count;
        bool witnessAlive = survivors == 1 && _horde.Pool.Type[0] == Walker;

        GD.Print($"  blast r={radius:F1}m dmg={blastDamage:F0}: {_kills} died, " +
                 $"{survivors} left, witness alive = {witnessAlive}");

        // Two kills exactly: the bloater that was shot and the one its blast
        // caught. A third would mean the second blast fired.
        return geometryValid && witnessAlive && _kills == 2;
    }

    /// A spitter holds its distance and does its damage from there. Kiting it is
    /// the wrong answer, which is the point of having it.
    private bool? StageSpitter(int tick)
    {
        if (tick == 1)
        {
            Reset();
            _player!.Heal(999.0f);
            _horde!.Spawn(_player.GlobalPosition + new Vector3(6.0f, 0.0f, 0.0f), Spitter);
            return null;
        }

        // Long enough for the standoff to settle and at least one shot to land.
        if (tick < 240)
            return null;

        EnemyPool pool = _horde!.Pool;
        if (pool.Count != 1)
        {
            GD.Print($"  expected the spitter to survive, pool has {pool.Count}");
            return false;
        }

        float distance = pool.Position[0].DistanceTo(_player!.GlobalPosition);
        float standoff = _horde.Types[Spitter].StandoffDistance;
        float damage = _player.MaxHealth - _player.Health;

        GD.Print($"  held at {distance:F1}m (standoff {standoff:F1}m), " +
                 $"player took {damage:F1}, shots alive {_horde.EnemyShots.Count}");

        // It should neither have closed to contact nor drifted out of range.
        bool heldPosition = distance > _horde.ContactRadius * 2.0f && distance <= standoff + 0.5f;
        return heldPosition && damage > 0.0f;
    }

    /// Intensity gates the roster. Early it is walkers only; at the deadline
    /// every variant is on the table.
    private bool? StageComposition(int tick)
    {
        if (tick < 2)
        {
            Reset();
            return null;
        }

        // Sized from the table, not from a literal.
        //
        // These were `new int[5]`, one slot per spawnable variant at the time, and
        // the stalker landing at index 6 walked straight off the end of them. The
        // count of variants is not a constant and writing it down as one is how a
        // probe becomes the thing that forbids adding content.
        int variants = _horde!.Types.Length;

        var early = new int[variants];
        _horde.SpawnIntensity = 0.0f;
        for (int i = 0; i < 200; i++)
        {
            _horde.Pool.Clear();
            _horde.SpawnByIntensity(new Vector3(-40.0f, 0.0f, 40.0f));
            early[_horde.Pool.Type[0]]++;
        }

        var late = new int[variants];
        _horde.SpawnIntensity = 1.0f;
        for (int i = 0; i < 400; i++)
        {
            _horde.Pool.Clear();
            _horde.SpawnByIntensity(new Vector3(-40.0f, 0.0f, 40.0f));
            late[_horde.Pool.Type[0]]++;
        }

        _horde.Pool.Clear();
        _horde.SpawnIntensity = 0.0f;

        bool earlyWalkersOnly = early[Walker] == 200;

        // Everything the director can actually roll has to turn up by the
        // deadline. Derived from the table rather than assumed to be "all but the
        // last one": the boss has a spawn weight of zero because `RunDirector`
        // places it by hand, and anything else added with a weight of zero would
        // be the same kind of thing.
        bool lateHasEveryone = true;
        var missing = new System.Collections.Generic.List<string>();

        for (int i = 0; i < late.Length; i++)
        {
            if (_horde.Types[i].SpawnWeight <= 0.0f)
                continue;

            if (late[i] > 0)
                continue;

            lateHasEveryone = false;
            missing.Add(_horde.Types[i].TypeName);
        }

        var names = new System.Collections.Generic.List<string>();
        foreach (EnemyTypeResource type in _horde.Types)
            names.Add(type.TypeName);

        GD.Print($"  intensity 0.0: {string.Join('/', early)}");
        GD.Print($"  intensity 1.0: {string.Join('/', late)}  ({string.Join('/', names)})");

        if (missing.Count > 0)
            GD.PushError($"  never rolled at full intensity: {string.Join(", ", missing)}");

        return earlyWalkersOnly && lateHasEveryone;
    }

    /// Clears the field and tops the player up, so each stage starts from the
    /// same place instead of inheriting the last one's survivors and wounds.
    private void Reset()
    {
        _horde!.Pool.Clear();
        _horde.EnemyShots.Clear();
        _player!.Heal(999.0f);
        _kills = 0;
        _lastKilledType = -1;
    }
}
