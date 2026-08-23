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

    private Player? _player;
    private CameraRig? _rig;

    private Vector3 _legStart;
    private float _yawBefore;
    private float _yawAfterTurn;
    private Vector2 _firstLeg;
    private Vector2 _secondLeg;
    private float _turnDrift;

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

        if (_frame < SettleFrames + 2 * DriveFrames + TurnFrames)
            return false;

        Input.ActionRelease("move_up");
        _secondLeg = Flat(_player!.GlobalPosition - _legStart);

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
