using Godot;

/// Writes the starting equipment to resources/gear/*.tres.
///
///   godot --headless --script scripts/tools/BuildGear.cs
///
/// The worn set grants nothing and permits a little. That is deliberate: it is
/// what the player already had on, so every number it adds is zero and its whole
/// contribution is the size of the climb it allows. Better sets bought later
/// move the start as well as the ceiling.
public partial class BuildGear : SceneTree
{
    private const string OutputDir = "res://resources/gear";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        GearResource[] gear =
        {
            // Armour governs how much punishment the run can learn to take.
            new()
            {
                GearName = "Worn Jacket",
                Slot = GearSlot.Armour,
                Tier = 1,
                HealthUpgradeCap = 4,
                ArmourUpgradeCap = 3,
            },

            // The backpack is the only piece that touches what comes home.
            new()
            {
                GearName = "Canvas Pack",
                Slot = GearSlot.Backpack,
                Tier = 1,
                SearchUpgradeCap = 2,
            },

            // Boots are the escape axis — speed is what turns a bad position
            // into a survivable one.
            new()
            {
                GearName = "Scuffed Boots",
                Slot = GearSlot.Boots,
                Tier = 1,
                SpeedUpgradeCap = 3,
            },
        };

        foreach (GearResource piece in gear)
        {
            string path = $"{OutputDir}/{piece.GearName.ToLower().Replace(' ', '_')}.tres";
            Error err = ResourceSaver.Save(piece, path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }
            GD.Print($"Saved {path}");
        }

        return true;
    }
}
