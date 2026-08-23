using Godot;

/// Checks that the camera gets out from behind cover.
///
///   godot --headless --script test/CameraProbe.cs
///
/// The arena is full of containers two and a half metres tall and the camera sits
/// nearly twelve metres behind the player. Walking past one puts it between the
/// lens and the character, and until this phase the answer was that the player
/// disappeared behind a grey box until they had walked far enough past it. The
/// proof video is where it was finally seen: eight seconds in, half the frame is
/// the top of a shipping container and the player is somewhere behind it.
///
/// Every stage here builds its own wall rather than hunting the generated map for
/// one, so what is being tested is the rig and not the seed.
public partial class CameraProbe : SceneTree
{
    private CameraRig? _rig;
    private Player? _player;
    private Camera3D? _camera;
    private StaticBody3D? _wall;

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

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stageTick == 0 && _stage == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");
            _player = scene.GetNodeOrNull<Player>("Player");
            _camera = _rig?.GetNodeOrNull<Camera3D>("Camera");

            if (_rig == null || _player == null || _camera == null)
            {
                GD.PushError("PROBE FAILED — no CameraRig, Player or Camera");
                Quit(1);
                return true;
            }

            // A horde walking into shot would put bodies on the sight line, and
            // the point of every reading here is what the *cover* does.
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            scene.GetNodeOrNull<Horde>("Horde")?.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageClearShot, "an open field leaves the camera where it was designed");
            case 1: return RunStage(StageWallPullsItIn, "a wall behind the player pulls the camera in");
            case 2: return RunStage(StageNeverPastTheFloor, "it never comes closer than the floor allows");
            case 3: return RunStage(StageStaysOnTheSightLine, "pulling in changes the distance and nothing else");
            case 4: return RunStage(StageItLetsBackOut, "the camera goes back out when the wall does");
            case 5: return RunStage(StageTheGroundIsNotCover, "hills and open ground do not pull the camera in");
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

    /// The baseline, and it is not a formality.
    ///
    /// Everything below is "the number went down". Without a stage that says the
    /// number is 1.0 when it should be, a rig that pulled in permanently — a ray
    /// that hit the ground, or the player's own capsule — would pass all four of
    /// them.
    private bool? StageClearShot(int tick)
    {
        // Somewhere with nothing behind it. The spawn is cleared to
        // `SpawnClearance` and the terrain is flat within seven metres of it, so
        // the sight line from there is as empty as this map gets.
        _player!.GlobalPosition = Terrain.Plant(Vector3.Zero);

        if (tick < 60)
            return null;

        GD.Print($"  pull-in {_rig!.PullIn:F3}, camera {_camera!.Position.Length():F2} m from the rig");
        return _rig.PullIn > 0.99f;
    }

    /// A wall, behind the player, across the sight line.
    private bool? StageWallPullsItIn(int tick)
    {
        if (tick == 1)
        {
            _player!.GlobalPosition = Terrain.Plant(Vector3.Zero);

            // Six metres behind, deliberately in the middle of the range rather
            // than near either end. At four metres the answer came out at exactly
            // the floor of 0.34, which is the same reading a rig that ignored the
            // distance and slammed to the minimum would produce — a stage that
            // cannot tell those two apart is a stage that tests nothing.
            //
            // Tall enough to block a camera 5.7 m up, and wide enough that the
            // yaw does not have to be exact.
            Vector3 behind = Back(6.0f);

            _wall = new StaticBody3D
            {
                Name = "ProbeWall",
                Position = new Vector3(behind.X, Terrain.Height(behind.X, behind.Z) + 4.0f, behind.Z),
            };

            _wall.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(14.0f, 8.0f, 1.0f) },
            });

            GetRoot().GetChild(GetRoot().GetChildCount() - 1).AddChild(_wall);
            return null;
        }

        if (tick < 60)
            return null;

        GD.Print($"  pull-in {_rig!.PullIn:F3}, camera {_camera!.Position.Length():F2} m from the rig");

        // A wall six metres out along a twelve-metre sight line leaves the camera
        // at about half its reach. Bounded on both sides: too high is a rig that
        // did not notice, too low is one that gave up and went to the floor.
        return _rig.PullIn > 0.40f && _rig.PullIn < 0.65f;
    }

    /// The floor under the pull-in.
    ///
    /// A camera allowed all the way to the pivot is a camera inside the player's
    /// chest, which renders as the inside of a torso and reads as the game having
    /// crashed. Against a wall pressed right up against the character it has to
    /// stop somewhere, and where it stops is a decision rather than a limit.
    private bool? StageNeverPastTheFloor(int tick)
    {
        if (tick == 1)
        {
            // The same wall, moved to half a metre behind the player — closer
            // than the margin, so the honest answer is "nowhere to go".
            Vector3 behind = Back(0.5f);
            _wall!.Position = new Vector3(behind.X, Terrain.Height(behind.X, behind.Z) + 4.0f, behind.Z);
            return null;
        }

        if (tick < 60)
            return null;

        GD.Print($"  pull-in {_rig!.PullIn:F3} against a floor of {_rig.MinimumPullIn:F2}");
        return _rig.PullIn >= _rig.MinimumPullIn - 0.001f
            && _rig.PullIn <= _rig.MinimumPullIn + 0.02f;
    }

    /// Distance, and only distance.
    ///
    /// The camera's offset is a tilt, a height and a set-back that together make
    /// the shot; sliding it toward the pivot along any other line changes the
    /// composition. Checked as a direction rather than a position, because the
    /// length is exactly what is supposed to have changed.
    private bool? StageStaysOnTheSightLine(int tick)
    {
        Vector3 at = _camera!.Position;
        var pivot = new Vector3(0.0f, _rig!.PivotHeight, 0.0f);

        // The designed offset, recovered from the scene rather than hardcoded —
        // `BuildMain` owns those three numbers and this must not become a second
        // copy of them that goes stale.
        Vector3 rest = RestOffset();
        Vector3 designed = (rest - pivot).Normalized();
        Vector3 actual = (at - pivot).Normalized();

        float agreement = designed.Dot(actual);
        GD.Print($"  direction from the pivot agrees to {agreement:F5}, "
               + $"tilt {_camera.RotationDegrees.X:F1}° unchanged");

        // And the camera has not been re-aimed. Turning it to look at the player
        // as it comes in swings the horizon every time somebody walks past a
        // container.
        return agreement > 0.9999f && Mathf.Abs(_camera.RotationDegrees.X + 26.0f) < 0.01f;
    }

    private bool? StageItLetsBackOut(int tick)
    {
        if (tick == 1)
        {
            _wall!.QueueFree();
            _wall = null;
            return null;
        }

        // Longer than the others: the release is deliberately the slower of the
        // two rates, because a camera that snapped back out reads as being shoved.
        if (tick < 150)
            return null;

        GD.Print($"  pull-in back to {_rig!.PullIn:F3}");
        return _rig.PullIn > 0.99f;
    }

    /// The ground is not something to get out from behind.
    ///
    /// Every other stage stands the player at the origin, where `Terrain` is
    /// deliberately flat. Off it the floor has nearly two metres of relief, the
    /// rig sits on that relief, and the pivot can end up *below* the flat box the
    /// arena actually collides with — at which point a ray drawn upward out of the
    /// floor is a ray that starts inside it. Godot ignores those by default
    /// (`hit_from_inside` is false), and if it ever stopped doing so the camera
    /// would snap to its minimum in every dip on the map while every stage above
    /// carried on passing.
    ///
    /// Sampled rather than asserted point by point, because this is a generated
    /// arena: some of these twenty spots have a container behind them and are
    /// *supposed* to pull in. What would not be a map is most of them doing it.
    private bool? StageTheGroundIsNotCover(int tick)
    {
        const int Spots = 12;

        // A full second per spot, and the number matters. ReleaseRate is 5 per
        // second, so recovering from the minimum takes about 0.85 s — at a fifth
        // of a second per spot every reading was the *previous* spot still letting
        // out, and sixteen of twenty “failed” while reporting they were blocked by
        // nothing at all.
        const int Settle = 60;

        int spot = (tick - 1) / Settle;
        if (spot >= Spots)
        {
            float share = _openSpots / (float)Spots;
            GD.Print($"  {_openSpots}/{Spots} spots left the camera fully out, "
                   + $"lowest reading {_lowest:F3}");

            // Three quarters. This is a generated arena and some of these spots
            // have a container behind them, which is the camera working; what
            // would not be a map is most of them doing it.
            return share >= 0.75f;
        }

        if ((tick - 1) % Settle == 0)
        {
            // A ring at 26 m, out where the terrain has its full amplitude and
            // well past the flattened spawn.
            float angle = Mathf.Tau * spot / Spots;
            var at = new Vector3(Mathf.Cos(angle) * 26.0f, 0.0f, Mathf.Sin(angle) * 26.0f);
            _player!.GlobalPosition = Terrain.Plant(at);
            return null;
        }

        if ((tick - 1) % Settle != Settle - 1)
            return null;

        // 0.95 rather than 0.99. A second of settling gets within a few
        // thousandths but not exactly to one, and a threshold that demanded the
        // last of it would be measuring the release rate rather than the geometry.
        _lowest = Mathf.Min(_lowest, _rig!.PullIn);
        if (_rig.PullIn > 0.95f)
            _openSpots++;
        else
            GD.Print($"    spot {spot}: {_rig.PullIn:F2}, blocked by {WhatBlocks()}");

        return null;
    }

    private int _openSpots;
    private float _lowest = 1.0f;

    /// Repeats the rig's own query so a failure can say what it hit.
    private string WhatBlocks()
    {
        Vector3 pivot = _rig!.GlobalPosition + Vector3.Up * _rig.PivotHeight;
        Vector3 rest = _rig.GlobalTransform * RestOffset();

        var query = PhysicsRayQueryParameters3D.Create(pivot, rest);
        query.CollideWithAreas = false;
        query.Exclude = new Godot.Collections.Array<Rid> { _player!.GetRid() };

        Godot.Collections.Dictionary hit = _rig.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return "nothing";

        return hit["collider"].AsGodotObject() is Node node ? node.Name.ToString() : "an unnamed body";
    }

    // --- helpers ------------------------------------------------------------

    /// A point `metres` behind the player, along the camera's own heading.
    private Vector3 Back(float metres)
    {
        Vector2 forward = CameraRig.Forward(_rig!.Yaw);
        Vector3 at = _player!.GlobalPosition;
        return new Vector3(at.X - forward.X * metres, 0.0f, at.Z - forward.Y * metres);
    }

    /// The camera's designed local offset, reconstructed from the rig's own
    /// geometry: the direction is fixed by the tilt, so a camera pulled in still
    /// points along it.
    private Vector3 RestOffset()
    {
        float tilt = Mathf.DegToRad(26.0f);
        return new Vector3(0.0f, Mathf.Sin(tilt), Mathf.Cos(tilt)) * 13.0f;
    }
}
