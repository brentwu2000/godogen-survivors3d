using Godot;

/// Verifies the flow field actually routes around geometry rather than walking
/// enemies into it.
///
///   godot --headless --script test/FlowFieldProbe.cs
///
/// One enemy is placed directly behind a long wall at z = 11 (spanning x -6..6),
/// with the player at the origin. The straight line between them goes through
/// the wall, so reaching the player at all proves the detour, and the peak |x|
/// says which way it went round.
///
/// The wall is built here rather than found in the level. The arena is generated
/// per run now, so a probe that points at whatever happens to be at z = 11 is
/// testing the layout and not the field — and passes or fails on the seed.
public partial class FlowFieldProbe : SceneTree
{
    /// Physics ticks, not render frames: the horde only steps in _PhysicsProcess,
    /// so counting render frames measures the wrong clock entirely — in headless
    /// they run several times faster and the run ends mid-journey.
    private const int MaxTicks = 1800;
    private const float ArriveDistance = 2.0f;
    private const float WallHalfWidth = 6.0f;

    private Horde? _horde;
    private Node3D? _player;
    private int _tick;
    private float _peakAbsX;
    private float _closest = float.MaxValue;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    /// The escape channel, checked on a field built here rather than on the live
    /// one.
    ///
    /// `FlowField` is a plain class, so this needs no scene, no horde and no
    /// tick — and a synthetic field is the only way to place a body *inside* a
    /// blocked region on purpose, which is the whole case being tested.
    ///
    /// The third assertion is the one that matters most and looks least like a
    /// test. `Sample` must still return zero inside a blocked cell. The first
    /// version of the escape wrote its directions straight into the route field,
    /// which took away the zero — and `Horde` reads that zero as "no route" and
    /// runs a fallback it has been tuned around. A walker that had been going
    /// round a pylon in 1800 ticks stopped dead thirty-three metres out.
    private bool CheckEscapes()
    {
        // Two-metre cells over a forty-metre square, and a ten-by-ten block in
        // the middle of it: big enough that its centre is several cells deep, so
        // "points outward" is a claim with somewhere to point.
        var field = new FlowField(Vector2.Zero, 20.0f, 2.0f);
        field.BlockBox(Vector2.Zero, new Vector2(5.0f, 5.0f));
        field.Rebuild(new Vector3(15.0f, 0.0f, 15.0f));

        var inside = new Vector3(0.0f, 0.0f, 0.0f);
        var edge = new Vector3(4.0f, 0.0f, 0.0f);
        var outside = new Vector3(12.0f, 0.0f, 12.0f);

        Vector2 fromCentre = field.EscapeFrom(inside);
        Vector2 fromEdge = field.EscapeFrom(edge);
        Vector2 fromOpen = field.EscapeFrom(outside);

        bool ok = true;

        if (fromCentre == Vector2.Zero || fromEdge == Vector2.Zero)
        {
            GD.PushError("  a blocked cell has no way out");
            ok = false;
        }

        if (fromOpen != Vector2.Zero)
        {
            GD.PushError($"  an open cell was handed an escape {fromOpen} — callers would follow it");
            ok = false;
        }

        // Walking the escape has to actually leave. Ten steps of one cell each is
        // twice the depth of the block, so a direction that merely pointed
        // *somewhere* rather than outward would still be inside at the end.
        Vector3 walk = inside;
        int steps = 0;
        while (field.IsBlockedAt(walk) && steps < 10)
        {
            Vector2 out2 = field.EscapeFrom(walk);
            if (out2 == Vector2.Zero)
                break;

            walk += new Vector3(out2.X, 0.0f, out2.Y) * 2.0f;
            steps++;
        }

        if (field.IsBlockedAt(walk))
        {
            GD.PushError($"  following the escape for {steps} steps ended still inside the block");
            ok = false;
        }

        // And the route field is untouched.
        if (field.Sample(inside) != Vector2.Zero || field.Sample(edge) != Vector2.Zero)
        {
            GD.PushError("  Sample no longer returns zero inside an obstacle — the horde's fallback is gone");
            ok = false;
        }

        GD.Print($"escapes: centre {fromCentre}, edge {fromEdge}, open {fromOpen}, "
               + $"out in {steps} step(s)");

        return ok;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_tick == 0)
        {
            if (!CheckEscapes())
            {
                GD.Print("PROBE FAILED — the escape channel is wrong");
                Quit(1);
                return true;
            }

            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Node3D>("Player");
            if (_horde == null || _player == null)
            {
                GD.PushError("PROBE FAILED — missing Horde or Player");
                Quit(1);
                return true;
            }

            // The player is armed and the subject spawns inside rifle range, so
            // without this the shot lands before the first step and the probe
            // measures an empty arena.
            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);

            // A known fixture in place of the generated layout: clear the cover,
            // build one wall, and rebake. Otherwise the straight line from the
            // subject to the player might be blocked by something else, or by
            // nothing at all, depending on the seed.
            var obstacles = scene.GetNodeOrNull<Node3D>("Obstacles");
            if (obstacles != null)
            {
                foreach (Node child in obstacles.GetChildren())
                {
                    obstacles.RemoveChild(child);
                    child.QueueFree();
                }

                var size = new Vector3(WallHalfWidth * 2.0f, 3.0f, 2.0f);
                var wall = new StaticBody3D { Name = "Wall", Position = new Vector3(0.0f, 1.5f, 11.0f) };
                wall.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh { Size = size } });
                wall.AddChild(new CollisionShape3D { Name = "Collision", Shape = new BoxShape3D { Size = size } });
                obstacles.AddChild(wall);
            }

            _horde.RebakeObstacles();

            // Clear the scene's own spawn so exactly one subject is measured.
            _horde.Pool.Clear();
            _horde.Spawn(new Vector3(0.0f, 0.0f, 15.0f));
            GD.Print("start (0, 15) — wall spans x -6..6 at z 10..12, player at origin");
        }

        _tick++;

        if (_horde!.Pool.Count == 0)
        {
            // Distinguishing this from "never arrived" matters: the subject
            // vanishing means something else in the scene removed it, and the
            // pathfinding was never exercised at all.
            GD.Print($"PROBE FAILED — subject disappeared at tick {_tick}; nothing was measured");
            Quit(1);
            return true;
        }

        {
            Vector3 p = _horde.Pool.Position[0];
            _peakAbsX = Mathf.Max(_peakAbsX, Mathf.Abs(p.X));
            _closest = Mathf.Min(_closest, p.DistanceTo(_player!.GlobalPosition));

            // Inside the wall footprint means the field failed to steer it out.
            if (Mathf.Abs(p.X) < WallHalfWidth && p.Z > 10.0f && p.Z < 12.0f)
            {
                GD.Print($"PROBE FAILED — entered the wall at {p.Snapped(Vector3.One * 0.01f)}");
                Quit(1);
                return true;
            }

            if (_closest <= ArriveDistance)
            {
                GD.Print($"reached player in {_tick} ticks, peak |x| = {_peakAbsX:F2}m");
                bool wentAround = _peakAbsX > WallHalfWidth;
                GD.Print(wentAround
                    ? "PROBE OK — routed around the wall"
                    : $"PROBE FAILED — arrived without ever leaving the wall's span (peak |x| {_peakAbsX:F2})");
                Quit(wentAround ? 0 : 1);
                return true;
            }
        }

        if (_tick < MaxTicks)
            return false;

        GD.Print($"PROBE FAILED — never arrived; closest {_closest:F2}m, peak |x| {_peakAbsX:F2}m");
        Quit(1);
        return true;
    }
}
