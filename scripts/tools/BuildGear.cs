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

            // Tier 2 moves both ends: a little on arrival, and noticeably more
            // room to climb. Priced so one good run buys one piece, which is
            // what makes the second run different from the first.
            new()
            {
                GearName = "Plate Carrier",
                Slot = GearSlot.Armour,
                Tier = 2,
                Price = 900,
                HealthBonus = 25.0f,
                ArmourBonus = 1.0f,
                HealthUpgradeCap = 7,
                ArmourUpgradeCap = 5,
            },

            // The only piece that changes what comes home, and the only one that
            // pays for itself in a single extraction.
            new()
            {
                GearName = "Trekking Pack",
                Slot = GearSlot.Backpack,
                Tier = 2,
                Price = 1200,
                CarryBonus = 8,
                SafeBoxBonus = 2,
                SearchUpgradeCap = 4,
            },

            new()
            {
                GearName = "Running Shoes",
                Slot = GearSlot.Boots,
                Tier = 2,
                Price = 700,
                MoveSpeedBonus = 0.6f,
                SpeedUpgradeCap = 5,
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
