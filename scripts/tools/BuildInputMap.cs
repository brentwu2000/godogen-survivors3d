using Godot;
using Godot.Collections;

/// Writes the [input] section of project.godot. Run once, or after changing the
/// action list:
///
///   godot --headless --script scripts/tools/BuildInputMap.cs
///
/// Godot serializes the InputEvent objects itself, so the project file never
/// depends on hand-written Object(...) literals — those drift between versions
/// and a malformed one drops the action without an error.
public partial class BuildInputMap : SceneTree
{
    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        // Movement is polled as a vector, so both WASD and the arrow cluster map
        // to the same actions. The virtual sticks feed the input layer directly
        // rather than through InputMap.
        Define("move_up", Keys(Key.W, Key.Up));
        Define("move_down", Keys(Key.S, Key.Down));
        Define("move_left", Keys(Key.A, Key.Left));
        Define("move_right", Keys(Key.D, Key.Right));

        Define("fire", new Array<InputEvent>
        {
            new InputEventMouseButton { ButtonIndex = MouseButton.Left },
            new InputEventKey { PhysicalKeycode = Key.Space },
        });
        Define("interact", Keys(Key.E));
        Define("reload", Keys(Key.R));
        Define("secure", Keys(Key.F));

        Error err = ProjectSettings.Save();
        if (err != Error.Ok)
        {
            GD.PushError($"Could not save project settings: {err}");
            return false;
        }

        GD.Print("Input actions written to project.godot");
        return true;
    }

    private static Array<InputEvent> Keys(params Key[] keys)
    {
        var events = new Array<InputEvent>();
        foreach (Key key in keys)
            events.Add(new InputEventKey { PhysicalKeycode = key });
        return events;
    }

    private static void Define(string action, Array<InputEvent> events)
    {
        ProjectSettings.SetSetting($"input/{action}", new Dictionary
        {
            { "deadzone", 0.2f },
            { "events", events },
        });
    }
}
