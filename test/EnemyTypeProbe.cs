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

    /// How tall each variant actually draws, measured from the sprite rather than
    /// assumed from the scale.
    ///
    /// The quad is one size for every layer, so a variant that does not fill its
    /// frame draws shorter than its scale suggests, and the scale is expected to
    /// have paid that back. Nothing enforces that: re-fitting the art changes the
    /// fill, `BuildEnemyTypes.cs` holds a number written by hand, and a brute that
    /// quietly became 2.4 m tall looks exactly like a brute. This is the only
    /// place the two halves are compared.
    private bool? StageDrawnHeight(int tick)
    {
        bool ok = true;
        float quad = _horde!.SpriteHeight;

        foreach (EnemyTypeResource type in _horde.Types)
        {
            Image? sprite = GD.Load<Texture2D>($"res://assets/sprites/enemies/{type.TypeName}.png")?.GetImage();
            if (sprite == null)
            {
                GD.PushError($"  {type.TypeName}.png did not load");
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

        // Five that turn up on their own plus the boss, which the director places
        // by hand. The count is asserted rather than merely tolerated because a
        // sixth row appearing unannounced would mean the sprite array and the
        // table had drifted apart, and every layer after the gap would draw the
        // wrong creature.
        if (types.Length != 6)
        {
            GD.PushError($"PROBE FAILED — expected 6 variants, got {types.Length}");
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

        // Both stages ran the same number of ticks, so the ratio of damage is
        // the ratio of the table's rates.
        float expectedRatio = types[Brute].ContactDamagePerSecond / types[Walker].ContactDamagePerSecond;
        float actualRatio = bruteDamage / Mathf.Max(0.001f, _walkerDamage);

        GD.Print($"  walker {_walkerDamage:F1} vs brute {bruteDamage:F1} over 1s " +
                 $"-> ratio {actualRatio:F2} (table {expectedRatio:F2})");

        return _walkerDamage > 0.0f && Mathf.Abs(actualRatio - expectedRatio) < 0.15f;
    }

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

        var early = new int[5];
        _horde!.SpawnIntensity = 0.0f;
        for (int i = 0; i < 200; i++)
        {
            _horde.Pool.Clear();
            _horde.SpawnByIntensity(new Vector3(-40.0f, 0.0f, 40.0f));
            early[_horde.Pool.Type[0]]++;
        }

        var late = new int[5];
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
        bool lateHasEveryone = true;
        for (int i = 0; i < late.Length; i++)
            lateHasEveryone &= late[i] > 0;

        GD.Print($"  intensity 0.0: {string.Join('/', early)}");
        GD.Print($"  intensity 1.0: {string.Join('/', late)}  (walker/runner/brute/bloater/spitter)");

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
