using Godot;

/// Writes the equipment table to resources/gear/*.tres.
///
///   godot --headless --script scripts/tools/BuildGear.cs
///
/// The worn set grants nothing and permits a little. That is deliberate: it is
/// what the player already had on, so every number it adds is zero and its whole
/// contribution is the size of the climb it allows.
///
/// Above it, each slot offers two pieces at one tier and they are **not** better
/// and worse. Tier 2 used to be tier 1 plus numbers, which meant the shop had one
/// correct answer per slot and the only question was what you could afford — a
/// budget screen wearing a shop's clothes. Now each pair trades: the piece that
/// grants a rule pays for it in the stat its neighbour is best at, so "which one"
/// is a real question and its answer is what the run is going to be about.
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
            // --- Tier 1: the shirt on your back ------------------------------

            new()
            {
                GearName = "Worn Jacket",
                Slot = GearSlot.Armour,
                Tier = 1,
                HealthUpgradeCap = 4,
                ArmourUpgradeCap = 3,
            },

            new()
            {
                GearName = "Canvas Pack",
                Slot = GearSlot.Backpack,
                Tier = 1,
                SearchUpgradeCap = 2,
            },

            new()
            {
                GearName = "Scuffed Boots",
                Slot = GearSlot.Boots,
                Tier = 1,
                SpeedUpgradeCap = 3,
            },

            // --- Trinkets: a run that starts with something in the air -------
            //
            // The fourth slot, and the only one that is not about the body. Every
            // other piece of gear moves a number the player already had; these
            // start the run holding a piece of kit, which means a build can be
            // decided at the shop rather than discovered in the deck.
            //
            // Filenames come from the name lowercased, so none of these has an
            // apostrophe in it. "Rabbit's Foot" becomes `rabbit's_foot.tres`,
            // which is a path that works until something quotes it.

            // One more blade, and room for two more from the deck.
            new()
            {
                GearName = "Whetstone",
                Slot = GearSlot.Trinket,
                Tier = 2,
                Price = 550,
                OrbitBonus = 1,
                OrbitUpgradeCap = 7,
            },

            // A pulse to start with — and a smaller player. The shockwave is the
            // strongest of the four in a crowd, so the one that hands it over
            // early is the one that takes something back.
            new()
            {
                GearName = "Cracked Capacitor",
                Slot = GearSlot.Trinket,
                Tier = 2,
                Price = 550,
                HealthBonus = -25.0f,
                ShockwaveBonus = 1,
                ShockwaveUpgradeCap = 6,
            },

            new()
            {
                GearName = "Copper Coil",
                Slot = GearSlot.Trinket,
                Tier = 2,
                Price = 650,
                ChainBonus = 0.18f,
                ChainUpgradeCap = 6,
            },

            // Tier 3 and the dearest, because chill is the one that changes where
            // the player can afford to stand rather than how fast things die.
            new()
            {
                GearName = "Frost Cell",
                Slot = GearSlot.Trinket,
                Tier = 3,
                Price = 900,
                ChillBonus = 0.17f,
                ChillUpgradeCap = 5,
            },

            // No kit at all. A trinket that competes with the kit trinkets on
            // their own terms would make the slot a single decision with four
            // wrong answers; this one is for a player who would rather come home
            // richer than fight differently.
            new()
            {
                GearName = "Lucky Bone",
                Slot = GearSlot.Trinket,
                Tier = 2,
                Price = 700,
                SafeBoxBonus = 1,
                FortuneUpgradeCap = 6,
            },

            new()
            {
                GearName = "Tourniquet",
                Slot = GearSlot.Trinket,
                Tier = 2,
                Price = 600,
                RegenBonus = 0.6f,
                RegenUpgradeCap = 6,
            },

            // --- Armour: absorb it, or make it cost them ---------------------

            // Soaks. The straightforward one, and the reason it is not simply
            // best is the speed: everything in this game that goes wrong goes
            // wrong because the player could not leave, and this piece is the
            // one that makes leaving slower.
            new()
            {
                GearName = "Plate Carrier",
                Slot = GearSlot.Armour,
                Tier = 2,
                Price = 900,
                HealthBonus = 25.0f,
                ArmourBonus = 1.0f,
                MoveSpeedBonus = -0.35f,
                HealthUpgradeCap = 7,
                ArmourUpgradeCap = 5,
                SpeedUpgradeCap = 0,
            },

            // Does not soak at all — it returns. Worth wearing exactly when the
            // answer to a crowd is to stand in it, and actively worse than the
            // jacket when the answer is a brute, because thorns scale with how
            // many things are touching you and a brute is one thing.
            new()
            {
                GearName = "Stitched Vest",
                Slot = GearSlot.Armour,
                Tier = 2,
                Price = 900,
                HealthBonus = 10.0f,
                MoveSpeedBonus = 0.15f,
                ThornsBonus = 0.35f,
                DodgeBonus = 0.06f,
                HealthUpgradeCap = 4,
                ArmourUpgradeCap = 1,
                ThornsUpgradeCap = 6,
                DodgeUpgradeCap = 5,
            },

            // --- Backpack: carry loot, or carry ammunition -------------------

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
                FortuneUpgradeCap = 5,
            },

            // Carries almost nothing and shoots through people. A run in this
            // banks less by design; the trade is that a line of walkers is one
            // shot, which is a different fight rather than a smaller wallet.
            new()
            {
                GearName = "Bandolier",
                Slot = GearSlot.Backpack,
                Tier = 2,
                Price = 1000,
                CarryBonus = 2,
                PierceBonus = 1,
                PierceUpgradeCap = 5,
                CritUpgradeCap = 6,
                SearchUpgradeCap = 1,
                FortuneUpgradeCap = 0,
            },

            // --- Boots: outrun it, or refuse to move -------------------------

            new()
            {
                GearName = "Running Shoes",
                Slot = GearSlot.Boots,
                Tier = 2,
                Price = 700,
                MoveSpeedBonus = 0.6f,
                SpeedUpgradeCap = 5,
            },

            // No speed at all, and everything that makes standing still
            // survivable: it pushes what it hits, heals continuously, and swings
            // wider. The piece that makes a melee run possible, and the piece
            // that makes a kiting run impossible.
            new()
            {
                GearName = "Tread Boots",
                Slot = GearSlot.Boots,
                Tier = 2,
                Price = 700,
                RegenBonus = 0.7f,
                KnockbackBonus = 1.2f,
                AreaBonus = 0.2f,
                SpeedUpgradeCap = 1,
                RegenUpgradeCap = 5,
                KnockbackUpgradeCap = 5,
                AreaUpgradeCap = 5,
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
