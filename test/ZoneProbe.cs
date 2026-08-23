using Godot;

/// Checks the danger zones: where they are, when they wake, and what they pay.
///
///   godot --headless --script test/ZoneProbe.cs
///
/// `AutoPlay` cannot cover these and should not be made to. A zone is optional
/// by design — the bot walks its route to the crates and the pad and never
/// enters one, which is exactly what a player who does not fancy it would do.
/// That leaves the entire system untested by the only driver that plays the
/// game, so it is tested here by putting the player inside one on purpose.
public partial class ZoneProbe : SceneTree
{
    private Player? _player;
    private Horde? _horde;
    private LevelGenerator? _level;
    private DangerZone[] _zones = System.Array.Empty<DangerZone>();

    private int _stage;
    private int _stageTick;
    private bool _failed;

    private Vector3 _parked;
    private int _spawnedOnPerimeter;
    private int _spawnedElsewhere;
    private float _pausedProgress;

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
            _player = scene.GetNodeOrNull<Player>("Player");
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _level = scene.GetNodeOrNull<LevelGenerator>("Level");

            var container = scene.GetNodeOrNull<Node3D>("DangerZones");
            if (container != null)
            {
                var found = new System.Collections.Generic.List<DangerZone>();
                foreach (Node child in container.GetChildren())
                {
                    if (child is DangerZone zone)
                        found.Add(zone);
                }

                _zones = found.ToArray();
            }

            if (_player == null || _horde == null || _level == null)
            {
                GD.PushError("PROBE FAILED — scene is missing a player, horde or level");
                Quit(1);
                return true;
            }

            // The director would keep trickling enemies in and the readings below
            // count spawns. Its ambient rate is deliberately non-zero, which is
            // right for the game and noise for this.
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            // And the weapon, which was quietly subtracting from every count.
            //
            // The opening-burst stage read seven of eight for three runs and the
            // eighth was never missing — the player stands in the middle of the
            // wave it just woke, the rifle fires on its own, and in the three
            // ticks between waking and reading it had killed one. The stage was
            // measuring spawning minus shooting and being read as a spawn bug,
            // which nearly bought a retry loop the game does not need.
            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StagePlaced, "three zones, one of each kind, clear of the pads");
            case 1: return RunStage(StageDormant, "a zone does nothing until somebody walks in");
            case 2: return RunStage(StageWakes, "walking in wakes it and the first wave arrives");
            case 3: return RunStage(StageReadout, "the readout says which zone and how far through");
            case 4: return RunStage(StagePerimeter, "reinforcements come from the zone's edge, not from around the player");
            case 5: return RunStage(StageHoldPauses, "leaving a hold pauses the clock rather than resetting it");
            case 6: return RunStage(StagePays, "clearing it pays rounds and a cache, and it stops spawning");
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

    /// Fails a stage that has nothing to measure.
    ///
    /// Not a null-forgiving `!`, and the difference is not style. An exception
    /// thrown out of `_PhysicsProcess` does not stop a `SceneTree` script — Godot
    /// prints it and starts the next frame — so a null dereference here is not a
    /// crash, it is a probe that runs forever printing a stack trace sixty times
    /// a second. That is exactly what happened the first time the zones failed to
    /// reach the tree, and it cost more to diagnose than the bug it was hiding.
    private bool? Missing()
    {
        GD.PushError("  no Hold zone on this map — nothing to measure");
        return false;
    }

    /// The first Hold zone, which is what stages 4 and 5 need.
    private DangerZone? Holdout()
    {
        foreach (DangerZone zone in _zones)
        {
            if ((ZoneKind)zone.Kind == ZoneKind.Hold)
                return zone;
        }

        return null;
    }

    private bool? StagePlaced(int tick)
    {
        bool ok = _zones.Length == _level!.ZoneCount;
        if (!ok)
            GD.PushError($"  {_zones.Length} zones built, {_level.ZoneCount} planned");

        var kinds = new System.Collections.Generic.HashSet<int>();
        foreach (DangerZone zone in _zones)
            kinds.Add(zone.Kind);

        // One of each, so a run always offers a choice between two ways of being
        // paid rather than three of the same encounter.
        if (kinds.Count != _zones.Length)
        {
            GD.PushError($"  {kinds.Count} distinct kinds across {_zones.Length} zones");
            ok = false;
        }

        // Clear of the extraction pads. A zone overlapping the way out could be
        // finished by standing where the player was going anyway, which is a
        // reward for nothing.
        var pads = GetRoot().GetChild(GetRoot().GetChildCount() - 1).GetNodeOrNull<Node3D>("ExtractionZones");
        foreach (DangerZone zone in _zones)
        {
            foreach (Node child in pads?.GetChildren() ?? new Godot.Collections.Array<Node>())
            {
                if (child is not ExtractionZone pad || !zone.Contains(pad.GlobalPosition))
                    continue;

                GD.PushError($"  {zone.Title} contains {pad.Name} — it could be cleared by extracting");
                ok = false;
            }
        }

        // And clear of each other, so entering one never wakes two.
        for (int i = 0; i < _zones.Length; i++)
        {
            for (int j = i + 1; j < _zones.Length; j++)
            {
                if (!_zones[i].Contains(_zones[j].GlobalPosition))
                    continue;

                GD.PushError($"  {_zones[i].Title} overlaps {_zones[j].Title}");
                ok = false;
            }
        }

        foreach (DangerZone zone in _zones)
        {
            GD.Print($"  {zone.Title,-14} {(ZoneKind)zone.Kind,-7} tier {zone.Tier} at " +
                     $"({zone.GlobalPosition.X:F0}, {zone.GlobalPosition.Z:F0}), " +
                     $"{zone.GlobalPosition.Length():F0} m out, pays {zone.Rolls} rolls + {zone.Rounds} rounds");
        }

        return ok;
    }

    private bool? StageDormant(int tick)
    {
        // Well clear of everything, and left there for a while.
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _player!.GlobalPosition = new Vector3(0.0f, 0.0f, 0.0f);
            return null;
        }

        if (tick < 30)
            return null;

        bool allDormant = true;
        foreach (DangerZone zone in _zones)
        {
            if (zone.State == DangerZone.ZoneState.Dormant)
                continue;

            GD.PushError($"  {zone.Title} is {zone.State} without anybody having entered it");
            allDormant = false;
        }

        GD.Print($"  half a second at the origin: {_horde!.Pool.Count} enemies spawned, " +
                 $"{_zones.Length} zones still asleep");

        // And nothing spawned. A dormant zone that is quietly producing enemies
        // is a difficulty curve nobody chose, arriving from a place the player
        // has no reason to look at.
        if (_horde.Pool.Count != 0)
        {
            GD.PushError($"  {_horde.Pool.Count} enemies arrived while every zone was dormant");
            allDormant = false;
        }

        return allDormant;
    }

    private bool? StageWakes(int tick)
    {
        DangerZone? hold = Holdout();
        if (hold == null)
        {
            GD.PushError("  no Hold zone on this map");
            return false;
        }

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _parked = hold.GlobalPosition;
            _player!.GlobalPosition = _parked;
            return null;
        }

        if (tick < 4)
            return null;

        GD.Print($"  stepping into {hold.Title}: state {hold.State}, " +
                 $"{_horde!.Pool.Count} arrived against an opening burst of {hold.OpeningBurst}");

        bool woke = hold.State == DangerZone.ZoneState.Running;
        if (!woke)
            GD.PushError($"  {hold.Title} is {hold.State} with the player standing in it");

        // All of it, exactly. With the weapon off there is nothing to remove
        // an arrival, so a shortfall here is a spawn that was refused — which is
        // what happens when a perimeter point lands inside a wall and the zone
        // does not try another edge.
        bool burstArrived = _horde.Pool.Count >= hold.OpeningBurst;
        if (!burstArrived)
        {
            GD.PushError($"  {_horde.Pool.Count} arrived of {hold.OpeningBurst} — " +
                         "is the boundary blocked, or has the retry gone?");
        }

        return woke && burstArrived;
    }

    /// A zone with no readout is a zone the player cannot act on.
    ///
    /// Standing inside one, the bar has to name it and show how far through. The
    /// alternative — which is what the first version of this shipped as — is that
    /// eight enemies arrive, the ground turns orange, and nothing anywhere says
    /// what is being asked or how much longer it lasts.
    ///
    /// Read off the HUD's own nodes rather than off a property added for the
    /// test, because the failure being guarded against is the readout not being
    /// wired to the zones at all.
    private bool? StageReadout(int tick)
    {
        if (Holdout() is not DangerZone hold)
            return Missing();

        var hud = GetRoot().GetChild(GetRoot().GetChildCount() - 1).GetNodeOrNull<CanvasLayer>("Hud");
        if (hud == null)
        {
            GD.PushError("  no Hud");
            return false;
        }

        // The HUD updates in _Process; a couple of ticks so it has run at least
        // once with the player standing inside.
        if (tick == 1)
        {
            _player!.GlobalPosition = hold.GlobalPosition;
            return null;
        }

        if (tick < 5)
            return null;

        var back = hud.GetNodeOrNull<ColorRect>("HoldBack");
        var fill = hud.GetNodeOrNull<ColorRect>("HoldFill");
        var text = hud.GetNodeOrNull<Label>("HoldText");

        string shown = text?.Text ?? "";
        bool visible = back is { Visible: true };
        bool named = shown.Contains(hold.Title.ToUpper());

        GD.Print($"  the hold bar reads \"{shown}\", visible {visible}, " +
                 $"fill {fill?.Size.X ?? 0.0f:F0}px at {hold.Progress * 100.0f:F0}% through");

        if (!visible)
            GD.PushError("  the readout is not showing anything while the player stands in a live zone");
        if (!named)
            GD.PushError($"  the readout says \"{shown}\" rather than naming {hold.Title}");

        return visible && named;
    }

    /// Where reinforcements come from is the whole mechanic.
    ///
    /// A ring around the player surrounds whoever it is aimed at, so retreating
    /// only means running into more of them. A perimeter has a far side — backing
    /// out the way you came in is a move that works, and the rectangle on the
    /// ground is the thing that tells you where that is.
    private bool? StagePerimeter(int tick)
    {
        if (Holdout() is not DangerZone hold)
            return Missing();

        if (tick == 1)
        {
            // Standing in one corner, so "near the edge" and "near the player"
            // are different places and the test can tell them apart.
            _player!.GlobalPosition = hold.GlobalPosition
                + new Vector3(hold.HalfExtent.X * 0.8f, 0.0f, hold.HalfExtent.Y * 0.8f);

            _horde!.Pool.Clear();
            _spawnedOnPerimeter = 0;
            _spawnedElsewhere = 0;
            return null;
        }

        if (tick < 40)
        {
            for (int i = 0; i < _horde!.Pool.Count; i++)
            {
                Vector3 at = _horde.Pool.Position[i];
                float dx = Mathf.Abs(at.X - hold.GlobalPosition.X) / hold.HalfExtent.X;
                float dz = Mathf.Abs(at.Z - hold.GlobalPosition.Z) / hold.HalfExtent.Y;

                // On the boundary in the Chebyshev sense — one axis at the edge,
                // the other inside it. Measured generously, because they start
                // walking the moment they arrive.
                if (Mathf.Max(dx, dz) > 0.85f)
                    _spawnedOnPerimeter++;
                else
                    _spawnedElsewhere++;
            }

            _horde.Pool.Clear();
            return null;
        }

        int total = _spawnedOnPerimeter + _spawnedElsewhere;
        float share = total == 0 ? 0.0f : _spawnedOnPerimeter / (float)total;

        GD.Print($"  {total} sightings: {share * 100.0f:F0}% within a fifth of the edge, " +
                 $"with the player parked in a corner");

        bool fromEdge = total > 0 && share > 0.9f;
        if (!fromEdge)
            GD.PushError($"  only {share * 100.0f:F0}% arrived at the perimeter — are they using the player ring?");

        return fromEdge;
    }

    private bool? StageHoldPauses(int tick)
    {
        if (Holdout() is not DangerZone hold)
            return Missing();

        // Inside for a while, then well outside for the same again. The progress
        // must not move during the second half — and must not go backwards
        // either, which is the failure this is really guarding against. A hold
        // that resets on leaving has exactly one correct answer, and the answer
        // is to stand still, which is the least interesting thing this game can
        // ask for.
        if (tick == 1)
        {
            _player!.GlobalPosition = hold.GlobalPosition;
            return null;
        }

        if (tick < 40)
            return null;

        if (tick == 40)
        {
            _pausedProgress = hold.Progress;
            _player!.GlobalPosition = hold.GlobalPosition + new Vector3(0.0f, 0.0f, hold.HalfExtent.Y * 4.0f);
            return null;
        }

        if (tick < 80)
            return null;

        float after = hold.Progress;
        GD.Print($"  {_pausedProgress * 100.0f:F0}% held after 40 ticks inside, " +
                 $"{after * 100.0f:F0}% after 40 more outside");

        bool advanced = _pausedProgress > 0.0f;
        bool paused = Mathf.Abs(after - _pausedProgress) < 0.001f;

        if (!advanced)
            GD.PushError("  standing in a hold did not advance it at all");
        if (!paused)
        {
            GD.PushError(after < _pausedProgress
                ? $"  leaving reset the hold from {_pausedProgress:F3} to {after:F3}"
                : $"  the hold advanced from {_pausedProgress:F3} to {after:F3} with nobody in it");
        }

        return advanced && paused;
    }

    private bool? StagePays(int tick)
    {
        if (Holdout() is not DangerZone hold)
            return Missing();
        var weapons = _player!.GetNodeOrNull<WeaponHandler>("WeaponHandler");
        Node? crates = GetRoot().GetChild(GetRoot().GetChildCount() - 1).GetNodeOrNull("LootContainers");

        if (tick == 1)
        {
            // Read rather than reset. `AddReserve` clamps to the weapon's
            // maximum, so a reserve already full would take none of the reward
            // and the stage would fail for a reason that has nothing to do with
            // zones — but it does not clamp at zero going down, so emptying it
            // with a negative leaves a reserve of −9,939 and a stage that passes
            // for the wrong reason. The starting reserve is well under the
            // maximum; there is room.
            _reserveBefore = weapons?.Reserve ?? 0;
            _cratesBefore = crates?.GetChildCount() ?? 0;

            // Straight to the end of the clock rather than waiting out a real
            // minute of ticks.
            hold.HoldSeconds = 0.35f;
            _player.GlobalPosition = hold.GlobalPosition;
            return null;
        }

        if (hold.State != DangerZone.ZoneState.Cleared && tick < 120)
            return null;

        int gained = (weapons?.Reserve ?? 0) - _reserveBefore;
        int dropped = (crates?.GetChildCount() ?? 0) - _cratesBefore;

        GD.Print($"  {hold.Title} finished as {hold.State}: reserve {_reserveBefore} -> " +
                 $"{weapons?.Reserve ?? 0} (+{gained}), {dropped} cache dropped");

        bool cleared = hold.State == DangerZone.ZoneState.Cleared;
        bool paidRounds = gained > 0;
        bool paidCache = dropped == 1;

        if (!cleared)
            GD.PushError($"  the hold ran out and is {hold.State}");
        if (!paidRounds)
            GD.PushError("  no ammunition — the next zone is now strictly harder than this one was");
        if (!paidCache)
            GD.PushError($"  {dropped} caches dropped, expected exactly one");

        // And it must stop. A cleared zone still producing enemies is a place the
        // player has already paid for that goes on charging them.
        int before = _horde!.Pool.Count;
        _horde.Pool.Clear();
        for (int i = 0; i < 30; i++)
            hold._PhysicsProcess(1.0 / 60.0);

        bool quiet = _horde.Pool.Count == 0;
        GD.Print($"  half a second after clearing: {_horde.Pool.Count} more arrived (was holding {before})");
        if (!quiet)
            GD.PushError($"  a cleared zone spawned {_horde.Pool.Count} more");

        return cleared && paidRounds && paidCache && quiet;
    }

    private int _reserveBefore;
    private int _cratesBefore;
}
