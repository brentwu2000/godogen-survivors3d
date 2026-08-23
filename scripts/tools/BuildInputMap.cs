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

        // `fire` is gone. Nothing has read it since the weapon began firing on
        // its own — the game has no manual shot — and an action nobody polls is a
        // key reserved against a keyboard that has run out of them.
        Define("interact", Keys(Key.E));

        // The second verb, for the three fittings in the shelter that have one.
        //
        // C, and the choice matters more than it looks. Every fitting used to
        // carry its own key: `menu_reroll` on R, `menu_daily` on D, `menu_biome`
        // on B. `menu_daily` on **D is also `move_right`** — so turning right at
        // the map table spent the day's one attempt, permanently, with no
        // confirmation and no way back. And the armoury's second verb had no
        // binding at all, which is a verb the player is told about and cannot
        // press.
        //
        // One key for "the other thing here" fixes both: there is nothing to
        // collide with because there is only one of it, and no fitting can have a
        // verb without a key because they all share the same one.
        Define("interact_second", Keys(Key.C));
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

        // Retired, and *removed* rather than simply not written.
        //
        // `ProjectSettings.Save()` writes what `SetSetting` set and leaves
        // everything else in the file exactly where it was. An action this tool
        // stops defining does not disappear — it stays bound, stays pollable, and
        // stays a key the player can press to do something the game no longer
        // means to offer. Assigning `default` is what actually clears one.
        //
        // `menu_sell` is why this matters beyond tidiness: it was on S, and S is
        // `move_down`. On a screen with no movement that was harmless. In a room
        // it sells the stash every time the player walks backwards.
        foreach (string retired in new[]
                 { "fire", "menu_sell", "menu_launch", "menu_reroll", "menu_biome", "menu_daily" })
        {
            ProjectSettings.SetSetting($"input/{retired}", default);
        }

        // The shelter still scrolls a list at the armoury and the board, and
        // that rides Godot's own ui_up/ui_down/ui_accept, which every build has.
        //
        // `menu_sell`, `menu_launch`, `menu_reroll`, `menu_biome` and `menu_daily`
        // are gone. Every one of them was a verb that worked from anywhere in the
        // base, which is what made the room unnecessary; now the verb is `[E]` and
        // the thing it applies to is wherever you are standing.

        // Landscape, locked. The touch layer is a stick on the left half and four
        // buttons in an arc bottom-right, laid out for a wide screen; a build
        // that let the device rotate would put the stick under the player's palm.
        // Set here rather than in the editor for the same reason the actions are:
        // this file is generated, and a hand-edit to it is a change nothing
        // records the reason for.
        ProjectSettings.SetSetting("display/window/handheld/orientation", "landscape");

        // Nothing outside movement may share a physical key with movement.
        //
        // This is checked rather than trusted, here, at the moment the map is
        // written. `menu_daily` sat on D — which is `move_right` — for four
        // phases, and the symptom was that turning right at the map table spent
        // the day's one attempt with no confirmation. Nothing failed; a run
        // simply started.
        //
        // `Input.ActionPress` moves an *action*, so no amount of driving the game
        // from a script can reproduce a two-actions-one-key collision. The only
        // place it is visible is the table itself.
        if (!NoKeyIsOverloaded())
            return false;

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

    /// Movement keys, and everything else that must not touch them.
    private static readonly string[] Movement =
        { "move_up", "move_down", "move_left", "move_right" };

    /// Fails the build if any non-movement action shares a key with movement.
    ///
    /// The *keys* are read back out of `ProjectSettings` rather than taken from
    /// the calls above, because the calls are the thing that could be wrong and a
    /// guard that re-states the code it guards proves nothing. Only the list of
    /// action names comes from `Define`, which is unavoidable — and harmless,
    /// since an action this tool never defined is not one it can get wrong.
    private static bool NoKeyIsOverloaded()
    {
        var claimed = new System.Collections.Generic.Dictionary<Key, string>();

        foreach (string action in Movement)
        {
            foreach (Key key in KeysOf(action))
                claimed[key] = action;
        }

        bool ok = true;

        foreach (string action in Defined)
        {
            if (System.Array.IndexOf(Movement, action) >= 0)
                continue;

            foreach (Key key in KeysOf(action))
            {
                if (!claimed.TryGetValue(key, out string? movement))
                    continue;

                GD.PushError($"'{action}' is bound to {key}, which is also '{movement}'. " +
                             "Pressing it while moving fires both.");
                ok = false;
            }
        }

        return ok;
    }

    /// Every action this tool defined, in the order it defined them.
    private static readonly System.Collections.Generic.List<string> Defined = new();

    private static System.Collections.Generic.List<Key> KeysOf(string action)
    {
        var keys = new System.Collections.Generic.List<Key>();

        if (ProjectSettings.GetSetting($"input/{action}").AsGodotDictionary() is not { } entry
            || !entry.ContainsKey("events"))
        {
            return keys;
        }

        foreach (Variant item in entry["events"].AsGodotArray())
        {
            if (item.As<InputEvent>() is InputEventKey key)
                keys.Add(key.PhysicalKeycode);
        }

        return keys;
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
        Defined.Add(action);

        ProjectSettings.SetSetting($"input/{action}", new Dictionary
        {
            { "deadzone", 0.2f },
            { "events", events },
        });
    }
}
