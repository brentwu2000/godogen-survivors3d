using Godot;

/// Drives the main scene with synthetic input and reports whether turn-and-
/// advance is really wired the way it claims to be.
///
///   godot --headless --script test/MovementProbe.cs
///
/// Headless is fine — this measures transforms, not pixels. Exit code is the
/// verdict, so it can gate a build.
///
/// This used to hold `move_right` and `move_down` and check that the player went
/// right and forward. Under the old eight-way scheme that was the whole contract.
/// It is now a test of something that no longer exists: the horizontal keys turn
/// the view, so holding both would make the player reverse in a circle, and the
/// probe would have passed or failed for reasons unrelated to whether anything
/// worked.
///
/// What replaces it is the one property that makes the scheme worth having —
/// **the direction of travel follows the view**. Three stages, and they have to
/// be in this order: advancing at rest, turning without advancing, then advancing
/// again to see that the heading came with the camera. The third stage is the
/// only one that can catch the bug this scheme is prone to, which is a forward
/// vector computed once and cached.
///
/// Deliberately does NOT use SceneBuildUtil.Run: that helper quits as soon as
/// its callback returns, which is right for a builder and fatal for a probe that
/// needs to live across frames.
public partial class MovementProbe : SceneTree
{
    private const int SettleFrames = 10;
    private const int DriveFrames = 60;
    private const int TurnFrames = 45;

    /// How far the player has to get for a stage to count as movement. Well under
    /// what 60 frames at 6 m/s would cover in the open, because the spawn is not
    /// guaranteed to be in the open — a probe that demanded the full distance
    /// would be testing the level generator's choice of spawn point.
    private const float MovedEnough = 1.5f;

    /// How far off the target of leg three sits, and how close counts as there.
    ///
    /// 2.0 m is inside the 2.29 m turning circle and outside the 1.8 m a crate
    /// can be searched from, which is exactly the band the sweep lost two seeds
    /// in. Three seconds is four times what turning ninety degrees and walking
    /// two metres takes; the failure being tested for never arrives at all.
    private const float OrbitRange = 2.0f;
    private const float OrbitArrived = 1.0f;
    private const int OrbitFrames = 180;

    private Player? _player;
    private CameraRig? _rig;

    private Vector3 _legStart;
    private float _yawBefore;
    private float _yawAfterTurn;
    private Vector2 _firstLeg;
    private Vector2 _secondLeg;
    private float _turnDrift;
    private Vector2 _orbitTarget;
    private float _orbitClosest = float.MaxValue;
    private int _orbitEnded;

    private int _frame;
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

        // Only the add happens here. Nodes are not inside the tree yet during
        // _Initialize, so any global transform read now returns identity.
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

    public override bool _Process(double delta)
    {
        _frame++;

        if (_frame == 1)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _player = scene.GetNodeOrNull<Player>("Player");
            _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");
            if (_player == null || _rig == null)
            {
                GD.PushError($"PROBE FAILED — player={_player != null} rig={_rig != null}");
                Quit(1);
                return true;
            }

            if (!_player.TurnToSteer)
            {
                GD.PushError("PROBE FAILED — TurnToSteer is off, so this measures the old scheme");
                Quit(1);
                return true;
            }

            return false;
        }

        // Leg one: advance from rest. The heading must not drift — `[W]` alone
        // is not a turn, and a rig that yawed here would mean the vertical axis
        // was leaking into the horizontal one.
        if (_frame == SettleFrames)
        {
            _legStart = _player!.GlobalPosition;
            _yawBefore = _rig!.Yaw;
            Input.ActionPress("move_up");
        }

        if (_frame == SettleFrames + DriveFrames)
        {
            Input.ActionRelease("move_up");
            _firstLeg = Flat(_player!.GlobalPosition - _legStart);

            // Turn right, and only turn. Nothing should translate: turning in
            // place is how the player aims, and a scheme where it also shuffled
            // you sideways would make standing and shooting impossible.
            _legStart = _player.GlobalPosition;
            Input.ActionPress("move_right");
        }

        if (_frame == SettleFrames + DriveFrames + TurnFrames)
        {
            Input.ActionRelease("move_right");
            _yawAfterTurn = _rig!.Yaw;
            _turnDrift = Flat(_player!.GlobalPosition - _legStart).Length();

            // Leg two: advance again, along whatever the turn left in front.
            _legStart = _player.GlobalPosition;
            Input.ActionPress("move_up");
        }

        if (_frame == SettleFrames + 2 * DriveFrames + TurnFrames)
        {
            Input.ActionRelease("move_up");
            _secondLeg = Flat(_player!.GlobalPosition - _legStart);

            // Leg three: something two metres away and ninety degrees off the
            // heading, driven through `BotDrive` rather than through raw keys.
            //
            // **This is the one leg that is about the driver rather than the
            // scheme.** Turn-and-advance traces arcs of radius `v/ω` — 2.29 m at
            // 6 m/s and 150°/s — so a driver that advances while it turns settles
            // onto that circle around anything nearer than its diameter and stays
            // there. Two `BalanceSweep` seeds spent sixty seconds orbiting a crate
            // 2.3 m out with the flow field pointing straight at it, and every
            // diagnostic said the route was correct because it was.
            //
            // Ninety degrees off is the geometry at its worst without being the
            // degenerate 180. From the cleared spawn, so this measures the driver
            // and not the seed's choice of obstacle.
            _player.GlobalPosition = Vector3.Zero;
            _player.Velocity = Vector3.Zero;
            Vector2 forward = CameraRig.Forward(_rig!.Yaw);
            _orbitTarget = new Vector2(-forward.Y, forward.X) * OrbitRange;
            _orbitClosest = float.MaxValue;
        }

        if (_frame <= SettleFrames + 2 * DriveFrames + TurnFrames)
            return false;

        if (_orbitEnded == 0)
        {
            var at = new Vector2(_player!.GlobalPosition.X, _player.GlobalPosition.Z);
            Vector2 toTarget = _orbitTarget - at;
            _orbitClosest = Mathf.Min(_orbitClosest, toTarget.Length());

            float radius = _player.MoveSpeed * (1.0f + _player.AdrenalineBoost)
                           / Mathf.DegToRad(_rig!.TurnRateDegrees);
            BotDrive.Steer(toTarget, _rig.Yaw, toTarget.Length(), radius);

            if (_orbitClosest > OrbitArrived
                && _frame < SettleFrames + 2 * DriveFrames + TurnFrames + OrbitFrames)
            {
                return false;
            }

            BotDrive.Release();
            _orbitEnded = _frame;
            return false;
        }

        // The rig lerps toward its own yaw, and leg three ends mid-turn. Reading
        // the lag and the transform before that has caught up would fail the two
        // checks at the bottom on a rig doing exactly what it is supposed to.
        if (_frame < _orbitEnded + SettleFrames * 3)
            return false;

        return Report();
    }

    private bool Report()
    {
        float turned = Mathf.RadToDeg(Mathf.Wrap(_yawAfterTurn - _yawBefore, -Mathf.Pi, Mathf.Pi));
        Vector2 forwardBefore = CameraRig.Forward(_yawBefore);
        Vector2 forwardAfter = CameraRig.Forward(_yawAfterTurn);

        GD.Print($"leg 1: moved {_firstLeg.Length():F2}m, {Off(_firstLeg, forwardBefore):F1}° off the view");
        GD.Print($"turn:  yaw {Mathf.RadToDeg(_yawBefore):F1}° -> {Mathf.RadToDeg(_yawAfterTurn):F1}° " +
                 $"({turned:F1}°), drifted {_turnDrift:F3}m");
        GD.Print($"leg 2: moved {_secondLeg.Length():F2}m, {Off(_secondLeg, forwardAfter):F1}° off the view");

        // Right is clockwise from above, which is a *decreasing* yaw. A sign
        // error here is the difference between a camera that follows the key and
        // one that runs away from it, and it is invisible to any test that only
        // asks whether the yaw changed.
        Check(turned < -20.0f, $"[D] turned {turned:F1}° — right must turn clockwise, so negative");

        Check(_firstLeg.Length() > MovedEnough, $"advancing from rest moved {_firstLeg.Length():F2}m");
        Check(_secondLeg.Length() > MovedEnough, $"advancing after the turn moved {_secondLeg.Length():F2}m");

        // The heart of it. Both legs must run along the view, which means the
        // second one ran somewhere the first one did not.
        Check(Off(_firstLeg, forwardBefore) < 20.0f, "leg 1 did not follow the view");
        Check(Off(_secondLeg, forwardAfter) < 20.0f, "leg 2 did not follow the view");

        // A forward vector computed once and cached would pass everything above:
        // both legs would run along the *old* heading and both would be "aligned"
        // with a stale number. This is the check that fails when that happens.
        float between = Off(_firstLeg, _secondLeg);
        GD.Print($"the two legs are {between:F1}° apart, and the view turned {Mathf.Abs(turned):F1}°");
        Check(between > 20.0f, $"both legs ran the same way ({between:F1}° apart) — is the heading cached?");

        GD.Print($"leg 3: closed to {_orbitClosest:F2}m of a target {OrbitRange:F1}m away and 90° off");
        Check(_orbitClosest <= OrbitArrived,
            $"never got closer than {_orbitClosest:F2}m — a driver that advances while turning "
            + "orbits anything inside v/w and cannot close");

        // Turning is not translation. A small drift is allowed: the player is a
        // CharacterBody3D with momentum, so leg one's velocity is still bleeding
        // off through the start of the turn.
        Check(_turnDrift < 1.2f, $"turning in place moved the player {_turnDrift:F2}m");

        // And the rig is still doing its original job.
        float lag = Flat(_rig!.GlobalPosition - _player!.GlobalPosition).Length();
        GD.Print($"camera rig lag {lag:F3}m, yaw {Mathf.RadToDeg(_rig.Rotation.Y):F1}°");
        Check(lag < 1.0f, $"the camera fell {lag:F2}m behind");
        Check(Mathf.Abs(Mathf.Wrap(_rig.Rotation.Y - _rig.Yaw, -Mathf.Pi, Mathf.Pi)) < 0.01f,
            "the rig's transform does not match its own Yaw");

        GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
        Quit(_failed ? 1 : 0);
        return true;
    }

    private void Check(bool ok, string complaint)
    {
        if (ok)
            return;

        GD.PushError($"  {complaint}");
        _failed = true;
    }

    /// Angle between two ground directions, in degrees. Zero-length inputs come
    /// back as 180 so a stage that did not move fails rather than divides.
    private static float Off(Vector2 a, Vector2 b)
    {
        if (a.LengthSquared() < 0.0001f || b.LengthSquared() < 0.0001f)
            return 180.0f;

        return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(a.Normalized().Dot(b.Normalized()), -1.0f, 1.0f)));
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
}
