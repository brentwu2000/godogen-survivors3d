using Godot;

/// Checks that the things a fight leaves behind are actually emitted.
///
///   godot --headless --script test/ImpactProbe.cs
///
/// `WeaponFeelProbe` asks whether two weapons look different from each other.
/// This asks a different question: whether the four pieces of feedback that are
/// *not* a muzzle flash happen at all. Every one of them was a system that
/// worked and drew nothing — the shockwave that was invisible for a whole phase
/// is the same defect, and it was found by looking rather than by a probe,
/// because there was no probe that could have found it.
///
/// Nothing here looks at the screen. Emissions are counted at the pool, which is
/// what a probe can honestly assert; whether a stain reads as a stain is a
/// question for `EffectShot`, and the two are deliberately separate.
public partial class ImpactProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private EffectDirector? _effects;
    private CameraRig? _rig;

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

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        Fresh.Profile(scene);
        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stageTick == 0 && _stage == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Player>("Player");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
            _effects = scene.GetNodeOrNull<EffectDirector>("Effects");
            _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");

            if (_horde == null || _player == null || _weapons == null || _effects == null || _rig == null)
            {
                GD.PushError("PROBE FAILED — the scene is missing something this needs");
                Quit(1);
                return true;
            }

            // Nothing may arrive on its own. Every stage below counts emissions
            // over a window, and an ambient spawn walking into the player is
            // another source of kills and another source of puffs.
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _weapons.HoldFire = true;
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageAKillLeavesAMark, "a kill stains the floor, downrange of the shot");
            case 1: return RunStage(StageTheFloorForgets, "the floor clears itself and never overflows");
            case 2: return RunStage(StageBothChannelsCarry, "a fight puts puffs on both the additive and the blending pass");
            case 3: return RunStage(StageACritSaysSo, "a crit emits more than the same shot without one");
            case 4: return RunStage(StageBurningIsDrawn, "a burning body is on fire and an unburnt one is not");
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

    /// Puts one enemy a fixed distance out along the view and clears everything
    /// that counts, so a stage reads only what it caused.
    private void Reset(float away)
    {
        Vector2 forward = CameraRig.Forward(_rig!.Yaw);
        _horde!.Pool.Clear();
        _horde.Hazards.Clear();
        _horde.Spawn(_player!.GlobalPosition + new Vector3(forward.X, 0.0f, forward.Y) * away, 0);
        _effects!.Effects.ForgetTotals();
        _effects.Effects.Clear();
        _effects.Marks.Clear();
    }

    private Vector3 _killedAt;
    private Vector2 _shove;

    /// **A kill used to leave nothing, and the direction it left nothing in was
    /// also nothing.** The stain is the only feedback in the game that outlives
    /// the frame it was caused in, and it is placed downrange of the shove so a
    /// cleared area reads as a fight that moved through it rather than as a
    /// texture.
    private bool? StageAKillLeavesAMark(int tick)
    {
        if (tick == 1)
        {
            Reset(3.0f);
            return null;
        }

        // A tick for the spawn to settle before anything is asked of it.
        if (tick == 2)
        {
            if (_horde!.Pool.Count == 0)
            {
                GD.PushError("  nothing spawned to kill");
                return false;
            }

            _killedAt = _horde.Pool.Position[0];
            _shove = new Vector2(1.0f, 0.0f);

            // Enough damage to kill a walker outright, so the mark comes from the
            // death and not from a hit that happened to land.
            _horde.Damage(0, 999.0f, _shove * 0.6f);
            return null;
        }

        if (tick < 6)
            return null;

        MarkField marks = _effects!.Marks;
        if (marks.TotalSpawned != 1)
        {
            GD.PushError($"  {marks.TotalSpawned} marks for one kill, wanted 1");
            return false;
        }

        // Downrange, not on the body: the mark is offset along the knockback, so
        // its X must have moved the way the shove pointed.
        float drift = marks.Position[0].X - _killedAt.X;
        if (drift <= 0.05f)
        {
            GD.PushError($"  the stain landed {drift:F2} m along a shove of +X — it is not following the shot");
            return false;
        }

        // On the ground rather than in the air. The whole point of a flat mark is
        // that it is *on* the floor, and the arena has a metre and a half of
        // relief for it to get wrong.
        float above = marks.Position[0].Y - Terrain.Height(marks.Position[0].X, marks.Position[0].Z);
        if (above is < 0.0f or > 0.25f)
        {
            GD.PushError($"  the stain sits {above:F2} m off the ground");
            return false;
        }

        return true;
    }

    /// The floor is a budget, and the reason to assert it is that the failure is
    /// silent and slow: a mark that never expires turns a three-hundred-second run
    /// into a paved arena, and the last thing anybody would suspect is the thing
    /// that looked right for the first thirty seconds.
    private bool? StageTheFloorForgets(int tick)
    {
        MarkField marks = _effects!.Marks;

        if (tick == 1)
        {
            Reset(3.0f);
            return null;
        }

        // Well past the pool, so the oldest-out rule is exercised rather than
        // merely fitted inside.
        const int Kills = 140;

        if (tick <= Kills + 1)
        {
            // One mark per kill is rate-limited by design, so these are spawned
            // straight into the field: what is under test is the ceiling and the
            // expiry, not the interval.
            marks.Spawn(_player!.GlobalPosition + new Vector3(tick * 0.1f, 0.0f, 0.0f),
                        1.0f, Colors.Black, 0.35f, MarkShape.Splat, 0.0f);

            if (marks.Count > marks.Capacity)
            {
                GD.PushError($"  {marks.Count} marks in a field of {marks.Capacity}");
                return false;
            }

            return null;
        }

        // 0.35 s of life at 60 Hz is 21 ticks; forty is past every one of them.
        if (tick < Kills + 42)
            return null;

        if (marks.Count != 0)
        {
            GD.PushError($"  {marks.Count} marks still on the floor after every one expired");
            return false;
        }

        return marks.TotalSpawned >= Kills;
    }

    /// Both passes have to be carrying something, because a channel that is never
    /// written is a channel nobody notices has broken. The split is invisible from
    /// outside — one pool, two uploads — so this is the only place it is stated.
    private bool? StageBothChannelsCarry(int tick)
    {
        if (tick == 1)
        {
            Reset(3.0f);
            _weapons!.HoldFire = false;
            return null;
        }

        if (tick == 2)
        {
            _weapons!.ForceFire(CameraRig.Forward(_rig!.Yaw));
            return null;
        }

        if (tick < 4)
            return null;

        _weapons!.HoldFire = true;

        EffectPool pool = _effects!.Effects;
        int soft = 0;
        int additive = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool.Soft[i])
                soft++;
            else
                additive++;
        }

        if (soft == 0 || additive == 0)
        {
            GD.PushError($"  {additive} additive and {soft} blended — one of the two passes is dead");
            return false;
        }

        return true;
    }

    /// Crit was bought and never seen. The flash is the whole of what the card
    /// looks like, so the assertion is that one shot with the roll guaranteed
    /// emits strictly more than the same shot with it impossible.
    ///
    /// **One shot each, not a window of firing, and the first version was a
    /// window.** Firing for thirty ticks with every shot critting killed the
    /// target in half the shots, so the crit reading came back *lower* — 10
    /// against 12 — and the probe was measuring how long the target survived
    /// rather than what the crit drew. Nothing about that is visible in the
    /// number; it just looks like the effect is missing.
    private bool? StageACritSaysSo(int tick)
    {
        if (tick == 1)
        {
            Reset(3.0f);
            _player!.Mods.CritChance = 0.0f;
            _weapons!.HoldFire = false;
            return null;
        }

        if (tick == 2)
        {
            _effects!.Effects.ForgetTotals();
            _weapons!.ForceFire(CameraRig.Forward(_rig!.Yaw));
            return null;
        }

        if (tick == 3)
        {
            _plain = _effects!.Effects.TotalSpawned;
            Reset(3.0f);
            _player!.Mods.CritChance = 1.0f;
            return null;
        }

        if (tick == 4)
        {
            _effects!.Effects.ForgetTotals();
            _weapons!.ForceFire(CameraRig.Forward(_rig!.Yaw));
            return null;
        }

        if (tick < 5)
            return null;

        int crit = _effects!.Effects.TotalSpawned;
        _weapons!.HoldFire = true;
        _player!.Mods.CritChance = 0.0f;

        if (_plain == 0)
        {
            GD.PushError("  nothing was emitted at all — the shot never landed");
            return false;
        }

        if (crit <= _plain)
        {
            GD.PushError($"  {crit} puffs on a critting shot against {_plain} on an ordinary one");
            return false;
        }

        return true;
    }

    private int _plain;

    /// **Ignite is an unlockable card and it had no picture.** The horde carries
    /// `Pool.Burn` and nothing drew it, so the one growth option opened by killing
    /// sixty in a run bought a number. The assertion is symmetric on purpose: a
    /// burning body emits and an identical one that is not burning does not, which
    /// is the difference a probe can see and "there are some orange puffs" is not.
    private bool? StageBurningIsDrawn(int tick)
    {
        const int Window = 40;

        if (tick == 1)
        {
            Reset(3.0f);
            return null;
        }

        // A few ticks of slack before the counter is zeroed, and it is load-bearing.
        //
        // `_Process` and `_PhysicsProcess` interleave, so the effects director gets
        // frames between the previous stage's last physics tick and this stage's
        // first — and the kill that ended the previous stage lands in them. Zeroing
        // at tick 1 counted three of somebody else's puffs against a field that was
        // definitively not on fire, which is a probe reporting its own bookkeeping
        // as a defect in the game.
        if (tick <= 6)
        {
            _effects!.Effects.ForgetTotals();
            return null;
        }

        // Cold first, so the reading that has to be zero is taken before anything
        // has been set alight.
        if (tick <= Window)
            return null;

        if (tick == Window + 1)
        {
            _cold = _effects!.Effects.TotalSpawned;

            if (_horde!.Pool.Count == 0)
            {
                GD.PushError("  nothing left alive to set on fire");
                return false;
            }

            // Long enough to outlast the window, and gentle enough not to kill the
            // thing before it has been looked at.
            _horde.ApplyBurn(0, 0.5f, 5.0f);
            _effects!.Effects.ForgetTotals();
            return null;
        }

        if (tick <= Window * 2 + 1)
            return null;

        int alight = _effects!.Effects.TotalSpawned;

        if (_cold != 0)
        {
            GD.PushError($"  {_cold} puffs from a field with nothing burning in it");
            return false;
        }

        if (alight == 0)
        {
            GD.PushError("  a body burning for forty ticks drew nothing");
            return false;
        }

        return true;
    }

    private int _cold;
}
