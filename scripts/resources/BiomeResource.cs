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

    // --- Look ---------------------------------------------------------------

    /// Ground tint and prop tint. Parameters, not a second asset pipeline: a
    /// biome that needed its own textures would be a content cost per biome, and
    /// the point of this resource is that the next one is a row of numbers.
    [Export] public Color GroundTint { get; set; } = new(1.0f, 1.0f, 1.0f);
    [Export] public Color PropTint { get; set; } = new(1.0f, 1.0f, 1.0f);

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
