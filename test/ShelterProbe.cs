using Godot;

/// Checks the room between runs: the fittings, the reach, and the keyboard.
///
///   godot --headless --script test/ShelterProbe.cs
///
/// The keyboard half of this is the reason the file exists, and it does not press
/// any keys. `Input.ActionPress` moves an *action*, never a key — so a script can
/// press `menu_daily` and `move_right` separately all day and never reproduce the
/// bug where they were the same physical key. The collision is only visible in
/// the binding table, so that is where it is checked.
///
/// That bug shipped: `menu_daily` sat on D, D is `move_right`, and turning right
/// at the map table spent the day's one attempt permanently, with no confirmation
/// and no way back. Nothing errored. A run simply started.
public partial class ShelterProbe : SceneTree
{
    /// The movement actions everything else must stay off.
    private static readonly string[] Movement =
        { "move_up", "move_down", "move_left", "move_right" };

    /// Actions that were retired when the room replaced the screen. Each was a
    /// verb that worked from anywhere in the base, which is exactly what made the
    /// room unnecessary.
    private static readonly string[] Retired =
        { "fire", "reload", "menu_sell", "menu_launch", "menu_reroll", "menu_biome", "menu_daily" };

    private Shelter? _shelter;
    private Player? _player;
    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Base.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Base.tscn — run scenes/BuildBase.cs first");
            Quit(1);
            return;
        }

        // A profile that has seen the base, or `BaseScreen._Ready` launches
        // straight into a run and there is no room to measure.
        Profile profile = SaveSystem.Load();
        profile.HasSeenBase = true;
        SaveSystem.Save(profile);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _shelter = scene.GetNodeOrNull<Shelter>("Shelter");
            _player = scene.GetNodeOrNull<Player>("Player");

            if (_shelter == null || _player == null)
            {
                GD.PushError($"PROBE FAILED — shelter={_shelter != null} player={_player != null}");
                Quit(1);
                return true;
            }
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageBindings, "no verb shares a key with movement");
            case 1: return RunStage(StageRetired, "the screen's eight verb keys are gone");
            case 2: return RunStage(StageEveryFittingHasItsKeys, "every fitting's verbs have a key to press");
            case 3: return RunStage(StageStations, "six fittings, inside the room, out of each other's reach");
            case 4: return RunStage(StageStandingSelects, "standing at one selects it, and walking off deselects");
            case 5: return RunStage(StageWalls, "the room is closed");
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

    /// The one that cannot be caught by playing.
    private bool? StageBindings(int tick)
    {
        var claimed = new System.Collections.Generic.Dictionary<Key, string>();
        foreach (string action in Movement)
        {
            foreach (Key key in KeysOf(action))
                claimed[key] = action;
        }

        bool ok = true;
        int checkedActions = 0;

        foreach (StringName name in InputMap.GetActions())
        {
            var action = name.ToString();
            if (action.StartsWith("ui_") || System.Array.IndexOf(Movement, action) >= 0)
                continue;

            checkedActions++;

            foreach (Key key in KeysOf(action))
            {
                if (!claimed.TryGetValue(key, out string? movement))
                    continue;

                GD.PushError($"  '{action}' is on {key}, which is '{movement}' — " +
                             "pressing it while moving fires both");
                ok = false;
            }
        }

        GD.Print($"  {checkedActions} non-movement actions checked against " +
                 $"{claimed.Count} movement keys");
        return ok;
    }

    private bool? StageRetired(int tick)
    {
        bool ok = true;

        foreach (string action in Retired)
        {
            if (!InputMap.HasAction(action))
                continue;

            GD.PushError($"  '{action}' is still bound — it was a verb that worked from " +
                         "anywhere in the base, which is what the room replaces");
            ok = false;
        }

        GD.Print($"  {Retired.Length} retired actions, {(ok ? "all" : "not all")} gone");
        return ok;
    }

    /// Every verb the room offers has to be pressable.
    ///
    /// The armoury's second verb had no binding at all — a thing the screen
    /// described and the player could not do. Checked against the prompt table
    /// rather than a list written here, so a fitting that grows a verb is covered
    /// the moment it does.
    private bool? StageEveryFittingHasItsKeys(int tick)
    {
        bool ok = true;

        foreach (Fitting fitting in System.Enum.GetValues<Fitting>())
        {
            if (fitting == Fitting.None)
                continue;

            (string title, string first, string second) = Shelter.Prompt(fitting);

            if (title.Length == 0)
            {
                GD.PushError($"  {fitting} has no prompt — the player is told nothing about it");
                ok = false;
            }

            if (first.Length > 0 && !InputMap.HasAction("interact"))
            {
                GD.PushError($"  {fitting} offers '{first}' and there is no 'interact' action");
                ok = false;
            }

            if (second.Length > 0 && !InputMap.HasAction("interact_second"))
            {
                GD.PushError($"  {fitting} offers '{second}' and there is no 'interact_second' action");
                ok = false;
            }
        }

        GD.Print($"  [E] {(InputMap.HasAction("interact") ? KeyList("interact") : "UNBOUND")}, " +
                 $"[C] {(InputMap.HasAction("interact_second") ? KeyList("interact_second") : "UNBOUND")}");
        return ok;
    }

    private bool? StageStations(int tick)
    {
        System.Collections.Generic.IReadOnlyDictionary<Fitting, Vector3> stations = _shelter!.Stations;

        bool ok = stations.Count == System.Enum.GetValues<Fitting>().Length - 1;
        if (!ok)
            GD.PushError($"  {stations.Count} stations for {System.Enum.GetValues<Fitting>().Length - 1} fittings");

        foreach ((Fitting fitting, Vector3 at) in stations)
        {
            // Inside the walls, with room to stand. A fitting the player cannot
            // reach is a fitting that does not exist.
            bool inside = Mathf.Abs(at.X) < _shelter.HalfWidth - 0.5f
                          && Mathf.Abs(at.Z) < _shelter.HalfDepth - 0.5f;

            if (!inside)
            {
                GD.PushError($"  {fitting} at ({at.X:F1}, {at.Z:F1}) is outside the room");
                ok = false;
            }

            GD.Print($"  {fitting,-8} at ({at.X,6:F1}, {at.Z,6:F1})");
        }

        // And each has to be selectable on its own. Two fittings within one
        // reach of each other is a spot where the prompt flickers between them
        // and pressing [E] does whichever won this frame.
        foreach ((Fitting fitting, Vector3 at) in stations)
        {
            if (_shelter.Nearest(at) == fitting)
                continue;

            GD.PushError($"  standing exactly on {fitting} selects {_shelter.Nearest(at)} instead");
            ok = false;
        }

        return ok;
    }

    private bool? StageStandingSelects(int tick)
    {
        // Teleported rather than walked, and that is deliberate. Whether the
        // player can walk is `MovementProbe`'s question; this one is whether
        // being somewhere is what selects it.
        var order = new System.Collections.Generic.List<Fitting>(_shelter!.Stations.Keys);

        int index = (tick - 1) / 3;
        if (index < order.Count)
        {
            if ((tick - 1) % 3 == 0)
                _player!.GlobalPosition = _shelter.Stations[order[index]];

            if ((tick - 1) % 3 == 2)
            {
                if (_shelter.Focus != order[index])
                {
                    GD.PushError($"  standing at {order[index]} reads as {_shelter.Focus}");
                    _failed = true;
                }
            }

            return null;
        }

        // And the middle of nowhere is nothing. A room where every square selects
        // something has no walking in it.
        if (tick == order.Count * 3 + 1)
        {
            _player!.GlobalPosition = new Vector3(_shelter.HalfWidth - 1.0f, 0.0f, -_shelter.HalfDepth + 4.0f);
            return null;
        }

        if (tick < order.Count * 3 + 4)
            return null;

        bool clear = _shelter.Focus == Fitting.None;
        GD.Print($"  {order.Count} fittings selected by standing on them; " +
                 $"an empty corner reads as {_shelter.Focus}");

        if (!clear)
            GD.PushError($"  an empty corner selects {_shelter.Focus}");

        return clear && !_failed;
    }

    /// Four walls with colliders, so the player cannot leave.
    private bool? StageWalls(int tick)
    {
        var walls = _shelter!.GetNodeOrNull<StaticBody3D>("Walls");
        if (walls == null)
        {
            GD.PushError("  the room has no collision at all — the player walks out into nothing");
            return false;
        }

        int shapes = 0;
        foreach (Node child in walls.GetChildren())
        {
            if (child is CollisionShape3D { Shape: BoxShape3D })
                shapes++;
        }

        GD.Print($"  {shapes} wall colliders");

        if (shapes != 4)
            GD.PushError($"  {shapes} walls, expected 4");

        return shapes == 4;
    }

    private static System.Collections.Generic.List<Key> KeysOf(string action)
    {
        var keys = new System.Collections.Generic.List<Key>();

        if (!InputMap.HasAction(action))
            return keys;

        foreach (InputEvent bound in InputMap.ActionGetEvents(action))
        {
            if (bound is InputEventKey key)
                keys.Add(key.PhysicalKeycode);
        }

        return keys;
    }

    private static string KeyList(string action) => string.Join("/", KeysOf(action));
}
