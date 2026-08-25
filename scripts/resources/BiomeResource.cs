using Godot;

/// A place to fight in, as data.
///
/// The arena was one layout rule with a seed on it: every run drew a different
/// map and every map asked the same question. That is fine until the loadouts
/// have identities — and once a run can be built around standing still or around
/// shooting through a line of six, a single terrain means those two builds are
/// permanently being compared on the same ground.
///
/// So a biome changes the **question**, not the texture. The two shipped here are
/// built to be each other's bad matchup: one has cover everywhere and no sight
/// lines, the other has sight lines and nowhere to hide.
///
/// Nothing here is a difficulty setting. If one biome were simply harder, it
/// would be a difficulty setting with scenery, and every player would pick the
/// other one forever. Each has to cost something the other does not.
[GlobalClass]
public partial class BiomeResource : Resource
{
    [Export] public string BiomeName { get; set; } = "";

    /// One line on the base screen, so the choice is made knowing what it is.
    [Export] public string Blurb { get; set; } = "";

    // --- Layout -------------------------------------------------------------

    /// Relative chance of each tile kind, indexed to match `LevelGenerator.Tile`:
    /// open, yard, corridor, rubble. Weights rather than a fixed rotation so a
    /// biome is a tendency and a seed is still a map.
    [Export] public float[] TileWeights { get; set; } = { 1.0f, 1.0f, 1.0f, 1.0f };

    /// Pieces per cluster and how big they are, multiplying the base layout.
    /// The pair is what decides sight lines: many small pieces break a line of
    /// fire without hiding anything, few large ones do the opposite.
    [Export] public float ClusterCountScale { get; set; } = 1.0f;
    [Export] public float ClusterSizeScale { get; set; } = 1.0f;

    /// How wide the way through a corridor wall is. Narrow makes the gap a
    /// chokepoint worth holding; wide makes it a suggestion.
    [Export] public float CorridorGap { get; set; } = 3.0f;

    // --- What is out there --------------------------------------------------

    [Export] public int CrateCount { get; set; } = 8;

    /// How much better the far crates are. A biome with fewer crates has to pay
    /// more for the walk, or its only difference is that it has less in it.
    [Export] public float DepthRarityBias { get; set; } = 1.9f;

    /// Multiplies the ring enemies arrive in. Wide open ground with a tight
    /// spawn ring is not open ground — it is an ambush — so the two move
    /// together.
    [Export] public float SpawnRingScale { get; set; } = 1.0f;

    // --- Furniture ----------------------------------------------------------

    /// Which prop fills each `PropRole`, in role order.
    ///
    /// This is what makes a biome a *place* rather than a colour grade. Before it
    /// existed the appearance of a biome was two tints over one shared set of
    /// five props, so a laboratory would have been a rail yard built out of
    /// shipping containers and lit differently — and a player reads furniture
    /// long before they read a tint.
    ///
    /// Stored as ints because Godot exports an enum array as one; `Prop` casts on
    /// the way out so nothing else has to know. Short or empty falls back to
    /// `PropLibrary.DefaultSet` per role rather than in one lump, so a biome that
    /// names only its cover still gets landmarks.
    [Export] public int[] PropSet { get; set; } = System.Array.Empty<int>();

    /// Large block-filling masses. Unlike furniture there may be several kinds
    /// in the same role, and an indoor biome may deliberately have none.
    [Export] public int[] StructureSet { get; set; } = System.Array.Empty<int>();

    /// What this biome puts in a given role.
    public PropKind Prop(PropRole role)
    {
        if (role == PropRole.Structure)
            throw new System.ArgumentException("Structure is a set role; use Structures()", nameof(role));

        int index = (int)role;

        if (PropSet != null && index < PropSet.Length)
        {
            var kind = (PropKind)PropSet[index];

            // Defined first, and that check is not paranoia.
            //
            // `PropLibrary.RoleOf` maps anything it does not recognise to
            // `Heap` — a reasonable default for a switch, and it means a
            // hand-edited `.tres` with `999` in the Heap slot passes the role
            // test below and is returned as a real kind. `PropRenderer` then
            // indexes its arrays at 999 and throws. Negative values do the same.
            if (!System.Enum.IsDefined(typeof(PropKind), kind))
            {
                GD.PushWarning($"{BiomeName}: {PropSet[index]} is not a prop kind");
            }

            // A kind in the wrong slot is a `.tres` written by hand or a role
            // inserted in the middle of the enum, and both put a fourteen-metre
            // water tower in the cover pool. Refused here rather than drawn:
            // `BiomeProbe` fails on it, and until someone runs the probe the
            // biome quietly uses the default instead of the arena being wrong.
            else if (PropLibrary.RoleOf(kind) == role)
            {
                return kind;
            }
            else
            {
                GD.PushWarning($"{BiomeName}: {kind} is a {PropLibrary.RoleOf(kind)}, not a {role}");
            }
        }

        return PropLibrary.DefaultSet[index];
    }

    /// Which of the three imported landmarks stand in this place.
    ///
    /// A separate list from `PropSet` because they are a separate system: the
    /// glTF landmarks are three authored models placed one per third of the
    /// compass *inside* the arena, and `PropKind`'s Tall and Sign are procedural
    /// scenery on a ring outside it. Both needed to stop being global, and they
    /// could not share a field.
    ///
    /// The reason this exists at all is that a grain silo was standing in the
    /// middle of a laboratory. Nothing in the code was wrong — `BuildLandmarks`
    /// placed one of each, which is exactly what it says it does, and it had been
    /// right for as long as every biome was outdoors.
    ///
    /// Empty means all three, so a `.tres` written before this existed is
    /// unchanged. A biome that wants none says so with a list of one entry set to
    /// -1, which is ugly and is the only way an exported int array can express
    /// "deliberately nothing" as distinct from "not set".
    [Export] public int[] LandmarkSet { get; set; } = System.Array.Empty<int>();

    /// The landmarks to place here, in the order they should be sited.
    public LandmarkKind[] Landmarks()
    {
        if (LandmarkSet == null || LandmarkSet.Length == 0)
            return System.Enum.GetValues<LandmarkKind>();

        var kinds = new System.Collections.Generic.List<LandmarkKind>(LandmarkSet.Length);
        var seen = new System.Collections.Generic.HashSet<int>();

        foreach (int entry in LandmarkSet)
        {
            // Out of range is how "none" is written, and it is also how a typo
            // looks. Both end the same way — the entry is skipped — and a biome
            // that meant to name one and misspelled it gets a place with no
            // landmarks rather than a crash, which `BiomeProbe` will report as a
            // place with no landmarks.
            if (entry < 0 || entry >= System.Enum.GetValues<LandmarkKind>().Length)
                continue;

            // A landmark listed twice would be sited twice, and the whole point
            // of three of them is that each one answers "which way am I facing".
            if (seen.Add(entry))
                kinds.Add((LandmarkKind)entry);
        }

        return kinds.ToArray();
    }

    /// Every kind this biome can place, landmarks included. The order is role
    /// order, which is what `PropRenderer` allocates against.
    public PropKind[] Kinds()
    {
        var roles = System.Array.FindAll(System.Enum.GetValues<PropRole>(), role => role != PropRole.Structure);
        var furniture = new PropKind[roles.Length];

        for (int i = 0; i < roles.Length; i++)
            furniture[i] = Prop(roles[i]);

        PropKind[] structures = Structures();
        var kinds = new PropKind[furniture.Length + structures.Length];
        furniture.CopyTo(kinds, 0);
        structures.CopyTo(kinds, furniture.Length);
        return kinds;
    }

    public PropKind[] Structures()
    {
        if (StructureSet == null || StructureSet.Length == 0)
            return System.Array.Empty<PropKind>();

        var result = new System.Collections.Generic.List<PropKind>(StructureSet.Length);
        foreach (int entry in StructureSet)
        {
            var kind = (PropKind)entry;
            if (System.Enum.IsDefined(typeof(PropKind), kind)
                && PropLibrary.RoleOf(kind) == PropRole.Structure)
                result.Add(kind);
            else
                GD.PushWarning($"{BiomeName}: {entry} is not a structure kind");
        }
        return result.ToArray();
    }

    // --- Look ---------------------------------------------------------------

    /// Ground tint and prop tint. Parameters, not a second asset pipeline: a
    /// biome that needed its own textures would be a content cost per biome, and
    /// the point of this resource is that the next one is a row of numbers.
    [Export] public Color GroundTint { get; set; } = new(1.0f, 1.0f, 1.0f);
    [Export] public Color PropTint { get; set; } = new(1.0f, 1.0f, 1.0f);

    // --- Roof ---------------------------------------------------------------
    //
    // Zero means open sky, which is every biome but one.
    //
    // **This is what an interior actually is, and the first attempt at Cold
    // Storage did not have it.** That version changed the sun to point straight
    // down, made it cold and weak, closed the fog at twenty-four metres and laid
    // a 1.2 m tile grid on the floor — every one of which is right, and together
    // they produced a place that reads as an outdoor arena at night. The
    // giveaway was overhead: an unobstructed black sky filling the top of the
    // frame, with air dust drifting against it like stars, and a far boundary
    // that spans the view like a horizon rather than converging into corners.
    //
    // No amount of floor dressing fixes that. A room is a room because there is
    // something above you.

    /// Metres from the ground to the underside of the roof, or 0 for open sky.
    ///
    /// Must clear the camera, which sits 5.7 m up. Below about seven and the
    /// view passes through the roof and the arena is drawn from inside the
    /// slab — which looks like the ceiling has vanished rather than like the
    /// camera is in the wrong place.
    [Export] public float CeilingHeight { get; set; }

    [Export] public Color CeilingColour { get; set; } = new(0.16f, 0.18f, 0.20f);

    // --- Ground -----------------------------------------------------------------
    //
    // `ground.gdshader` already draws slabs with a seam between them, and every
    // biome used the same four metres. That is one of the strongest scale cues
    // in the frame — the arena reads as large partly because the floor has a
    // known size to pace out — and handing it to the biome is the cheapest
    // difference available: no textures, no draw calls, no geometry.
    //
    // A 1.2 m tile grid indoors against a 9 m poured bay in the open is most of
    // what makes one feel like a room and the other like a field, before a single
    // prop is placed.
    //
    // Defaults are the shader's own, so a biome that says nothing is unchanged.

    /// How big one slab is, in metres.
    [Export] public float GroundSlabMetres { get; set; } = 4.0f;

    /// How dark the joint between slabs is, and how wide. Wide and dark is
    /// patched asphalt; narrow and faint is a floor laid in one go.
    [Export] public float GroundSeamDarkness { get; set; } = 0.34f;
    [Export] public float GroundSeamWidth { get; set; } = 0.028f;

    /// How much each slab varies in brightness. Small on purpose everywhere — the
    /// eye is meant to find the edges, not any one slab.
    [Export] public float GroundSlabVariation { get; set; } = 0.09f;

    // --- Light ----------------------------------------------------------------
    //
    // Every default below is the number `BuildMain` hard-coded, to the digit.
    // That is deliberate and it is the only way this change could be made safely:
    // three biomes and forty-odd probes were tuned against one lighting rig, and
    // a biome resource that "improved" the defaults would have re-lit the entire
    // game as a side effect of adding a fourth place. A biome that says nothing
    // about light looks exactly as it did.
    //
    // Applied by `LevelGenerator`, which is the node that already knows what this
    // place is. `BuildMain` still builds the environment; this overrides it after
    // the scene loads rather than replacing it, so a scene opened in the editor
    // still looks like the game.

    /// Where the sun is, as pitch and yaw in degrees.
    ///
    /// Pitch near -90 is overhead, which is what an interior wants: a ceiling
    /// does not have a low warm sun coming through it, and the single strongest
    /// signal that a space is enclosed is that the shadows fall straight down.
    [Export] public Vector2 SunAngleDegrees { get; set; } = new(-55.0f, -35.0f);

    [Export] public Color SunColour { get; set; } = new(1.0f, 0.94f, 0.83f);
    [Export] public float SunEnergy { get; set; } = 1.25f;

    /// Cool, because the sun is warm. Shadowed faces picking up sky colour rather
    /// than simply being darker is the cheapest thing that makes geometry look
    /// lit — and indoors it is most of the light there is.
    [Export] public Color AmbientColour { get; set; } = new(0.42f, 0.50f, 0.62f);
    [Export] public float AmbientEnergy { get; set; } = 0.55f;

    /// What distance looks like here.
    ///
    /// Pulling `FogEnd` in is how a room is made: the horizon stops existing at
    /// twenty-eight metres and the arena becomes the part of it you can see. It
    /// is also the one lever that changes difficulty without changing a single
    /// number in the enemy table, so it moves with the layout rather than for
    /// looks — a place that hides the crowd has to give something back.
    [Export] public Color FogColour { get; set; } = new(0.05f, 0.05f, 0.07f);
    [Export] public float FogBegin { get; set; } = 10.0f;
    [Export] public float FogEnd { get; set; } = 35.0f;

    /// Normalised tile weights, so callers can roll against them directly.
    public float WeightOf(int tile) =>
        tile >= 0 && tile < TileWeights.Length ? Mathf.Max(0.0f, TileWeights[tile]) : 0.0f;

    public float WeightTotal
    {
        get
        {
            float total = 0.0f;
            foreach (float weight in TileWeights)
                total += Mathf.Max(0.0f, weight);

            return total;
        }
    }
}
