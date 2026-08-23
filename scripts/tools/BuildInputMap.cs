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

        // Turning the view without moving. `[A]`/`[D]` already turn — these exist
        // for the player who wants to look around while walking straight, and for
        // a left hand that is busy. Z and X because they are under the same hand
        // as the movement keys and neither is a verb.
        Define("view_left", Keys(Key.Z));
        Define("view_right", Keys(Key.X));

        Define("fire", new Array<InputEvent>
        {
            new InputEventMouseButton { ButtonIndex = MouseButton.Left },
            new InputEventKey { PhysicalKeycode = Key.Space },
        });
        Define("interact", Keys(Key.E));
        Define("reload", Keys(Key.R));
        Define("secure", Keys(Key.F));
        Define("use", Keys(Key.Q));
        Define("swap", Keys(Key.Tab));
        Define("throw", Keys(Key.G));

        // Level-up picks. Real actions rather than raw key reads, so a play-test
        // can press them the way a player does instead of reaching past the
        // input layer to call the method directly.
        Define("pick_1", Keys(Key.Key1));
        Define("pick_2", Keys(Key.Key2));
        Define("pick_3", Keys(Key.Key3));

        // The base screen. Moving and confirming ride Godot's own ui_up/ui_down/
        // ui_accept, which every build already has; only the two verbs this
        // project invented need defining.
        Define("menu_sell", Keys(Key.S));
        Define("menu_launch", Keys(Key.L));

        // Shares the R key with `reload`, which is only read during a run. Two
        // actions on one key is safe as long as no screen polls both, and the
        // mnemonic is worth more than a spare letter.
        Define("menu_reroll", Keys(Key.R));

        // Base screen only, so these are free to be letters a run already uses.
        Define("menu_biome", Keys(Key.B));
        Define("menu_daily", Keys(Key.D));

        // Landscape, locked. The touch layer is a stick on the left half and four
        // buttons in an arc bottom-right, laid out for a wide screen; a build
        // that let the device rotate would put the stick under the player's palm.
        // Set here rather than in the editor for the same reason the actions are:
        // this file is generated, and a hand-edit to it is a change nothing
        // records the reason for.
        ProjectSettings.SetSetting("display/window/handheld/orientation", "landscape");

        // The tick rate, pinned — and pinned through `SetInitialValue` first,
        // which is not decoration. `ProjectSettings.Save()` omits any setting
        // whose value equals its default, so writing 60 over a default of 60
        // writes nothing and a line hand-added to project.godot disappears the
        // next time this tool runs. Telling the settings system that the default
        // is something else is the only way to make it serialize the value.
        //
        // It matters because every balance number this project has came out of a
        // fixed-step simulation. A machine that ran at a different tick rate
        // would produce different damage totals from the same seed, and the
        // difference would look like a design change.
        ProjectSettings.SetInitialValue("physics/common/physics_ticks_per_second", 30);
        ProjectSettings.SetSetting("physics/common/physics_ticks_per_second", 60);

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
