using Godot;

/// Checks that the minimap remembers rather than reveals.
///
///   godot --headless --script test/MinimapProbe.cs
///
/// One property carries the whole feature: the map must start dark, fill in only
/// where the player has walked, and never hand over the half of the arena the fog
/// exists to hide. A map that quietly showed everything would look identical in a
/// screenshot taken after a minute of play — by then most of it has been walked —
/// and would have removed the decision the run is made of.
public partial class MinimapProbe : SceneTree
{
    private Minimap? _map;
    private Player? _player;
    private Node3D? _zones;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    private float _atStart;
    private float _afterWalking;

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
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _map = scene.GetNodeOrNull<Minimap>("Hud/Minimap");
            _player = scene.GetNodeOrNull<Player>("Player");
            _zones = scene.GetNodeOrNull<Node3D>("DangerZones");

            if (_map == null || _player == null)
            {
                GD.PushError($"PROBE FAILED — minimap={_map != null} player={_player != null}");
                Quit(1);
                return true;
            }

            // Otherwise the horde walks the player around and the "did standing
            // still reveal anything" question answers itself.
            scene.GetNodeOrNull<Horde>("Horde")?.SetPhysicsProcess(false);
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageStartsMostlyDark, "the map starts dark, with a patch where the player stands");
            case 1: return RunStage(StageDormantZonesStayHidden, "an unvisited zone is not on it");
            case 2: return RunStage(StageWalkingRevealsIt, "walking fills it in");
            case 3: return RunStage(StageStandingStillDoesNot, "standing still does not");
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

    private bool? StageStartsMostlyDark(int tick)
    {
        if (tick < 3)
            return null;

        _atStart = _map!.Explored;

        // A 30 m sight radius on a 110 m map is about 22% of the area if the
        // player were in the middle of it, and they start near the middle.
        // Anything near 100% is a map that was handed over rather than walked.
        GD.Print($"  after half a second at the spawn: {_atStart * 100.0f:F0}% explored");

        bool started = _atStart > 0.01f;
        bool notGiven = _atStart < 0.40f;

        if (!started)
            GD.PushError("  nothing is explored at all — is the map remembering anything?");
        if (!notGiven)
            GD.PushError($"  {_atStart * 100.0f:F0}% explored before walking anywhere — the map was revealed, not discovered");

        return started && notGiven;
    }

    private bool? StageWalkingRevealsIt(int tick)
    {
        // Teleported along a line across the arena. Whether the player can walk
        // is `MovementProbe`'s question; this one is whether being somewhere is
        // what puts it on the map.
        if (tick <= 20)
        {
            float t = tick / 20.0f;
            _player!.GlobalPosition = new Vector3(Mathf.Lerp(-40.0f, 40.0f, t), 0.0f, Mathf.Lerp(-30.0f, 30.0f, t));
            return null;
        }

        _afterWalking = _map!.Explored;
        GD.Print($"  after crossing the arena: {_afterWalking * 100.0f:F0}% explored");

        bool grew = _afterWalking > _atStart + 0.10f;
        if (!grew)
            GD.PushError($"  {_atStart * 100.0f:F0}% -> {_afterWalking * 100.0f:F0}% — walking did not reveal anything");

        // And still not all of it. A diagonal across a square leaves the two far
        // corners unseen, and a map that had them anyway is not tracking sight.
        bool stillDark = _afterWalking < 0.92f;
        if (!stillDark)
            GD.PushError($"  {_afterWalking * 100.0f:F0}% after one diagonal — the corners should still be dark");

        return grew && stillDark;
    }

    private bool? StageStandingStillDoesNot(int tick)
    {
        if (tick == 1)
        {
            _player!.GlobalPosition = new Vector3(40.0f, 0.0f, 30.0f);
            return null;
        }

        if (tick < 60)
            return null;

        float now = _map!.Explored;
        GD.Print($"  a second in one place: {_afterWalking * 100.0f:F0}% -> {now * 100.0f:F0}%");

        // Allowed to rise a little on the first tick or two — the taper means a
        // cell at the edge of sight brightens over a couple of samples — but not
        // to keep climbing.
        bool settled = now < _afterWalking + 0.03f;
        if (!settled)
            GD.PushError($"  standing still added {(now - _afterWalking) * 100.0f:F0}% — is it revealing on a timer?");

        return settled;
    }

    /// A zone nobody has walked near must not be drawn.
    ///
    /// This is the one the map is most likely to get wrong, because the zones are
    /// in the scene tree from the first frame and drawing all of them is one line
    /// shorter than drawing the seen ones.
    private bool? StageDormantZonesStayHidden(int tick)
    {
        var far = new System.Collections.Generic.List<DangerZone>();

        foreach (Node child in _zones?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (child is DangerZone { State: DangerZone.ZoneState.Dormant } zone)
                far.Add(zone);
        }

        GD.Print($"  {far.Count} zones dormant, before the player has walked anywhere");

        // Before the crossing, not after — which is the difference between a
        // real check and a vacuous one. Run after the diagonal, every zone had
        // been walked past and the stage passed by having nothing to test.
        if (far.Count == 0)
        {
            GD.PushError("  no dormant zones to test against");
            return false;
        }
        int drawn = 0;
        foreach (DangerZone zone in far)
        {
            if (_map!.WouldDraw(zone))
                drawn++;
        }

        GD.Print($"  of those, {drawn} would be drawn — the rest are somewhere the player has not been");

        if (drawn == far.Count)
        {
            GD.PushError($"  all {far.Count} dormant zones are drawn from the spawn — is the map ignoring what has been seen?");
            _failed = true;
        }

        // Every dormant zone the map draws has to be one the player has seen.
        bool honest = true;
        foreach (DangerZone zone in far)
        {
            if (!_map!.WouldDraw(zone) || _map.HasSeen(zone.GlobalPosition))
                continue;

            GD.PushError($"  {zone.Title} is drawn and has never been walked near");
            honest = false;
        }

        return honest;
    }
}
