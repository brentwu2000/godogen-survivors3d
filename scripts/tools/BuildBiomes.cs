using Godot;

/// Writes the biome table to resources/biomes/*.tres.
///
///   godot --headless --script scripts/tools/BuildBiomes.cs
///
/// Two, and they are built to be each other's bad matchup. The test that matters
/// is not "are they different" — any two sets of numbers are — but "does each one
/// punish something the other rewards", because a biome that is merely different
/// is a coin flip and a biome that is merely harder is a difficulty setting.
public partial class BuildBiomes : SceneTree
{
    private const string OutputDir = "res://resources/biomes";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        BiomeResource[] biomes =
        {
            // The original arena, named and kept as the middle answer. A player
            // who does not want to think about terrain should have somewhere to
            // go that does not punish them for it.
            new()
            {
                BiomeName = "Rail Yard",
                Blurb = "mixed cover, honest sight lines",
                TileWeights = new[] { 1.0f, 1.0f, 1.0f, 1.0f },
                CrateCount = 8,
                DepthRarityBias = 1.9f,
            },

            // Cover everywhere and almost nothing to shoot down. Rubble is many
            // small pieces, which is the tile that breaks a line of fire without
            // hiding anyone — so a pierce build spends the run hitting a bin
            // while a thorns build has walls to back into. Loot-rich, because the
            // walk between crates is short and something has to be the cost.
            new()
            {
                BiomeName = "Old Town",
                Blurb = "no line of fire; the crowd arrives close",
                TileWeights = new[] { 0.2f, 1.2f, 1.8f, 2.2f },

                // The first pass was 1.6x count at 0.75x size, and the probe put
                // 126 blocks on the map for a 36% cut in sight lines: a hundred
                // small pieces scattered over a 110 m arena is 4% ground coverage
                // and a shot goes straight past all of them. Density that reads
                // as "no line of fire" needs pieces big enough to stand behind,
                // not more of them.
                ClusterCountScale = 2.0f,
                ClusterSizeScale = 1.25f,

                // Narrow enough that the gap in a wall is worth standing in, and
                // no narrower. 2.2 was the first number and it is a gap that only
                // just exists: the navigation grid is 1.5 m cells and every
                // pathfinder over it inflates obstacles, so a doorway of about a
                // cell and a half is one that some consumer of that grid will
                // decide is not there. The player's body is 0.35 m — the geometry
                // was never the constraint.
                CorridorGap = 3.2f,

                CrateCount = 11,
                DepthRarityBias = 1.4f,

                // Close. Open ground with a tight ring is an ambush; enclosed
                // ground with a wide one is a map nothing ever reaches you on.
                SpawnRingScale = 0.78f,

                GroundTint = new Color(0.82f, 0.80f, 0.86f),
                PropTint = new Color(0.88f, 0.87f, 0.92f),
            },

            // Nowhere to stand and nothing in the way. Long shots land, and so
            // does everything coming at you — the piece that grants speed is the
            // one that matters here, and the piece that grants thorns is a piece
            // that never gets touched by more than one thing at a time. Fewer
            // crates, further out, and worth much more for the walk.
            new()
            {
                BiomeName = "The Flats",
                Blurb = "nothing in the way, in either direction",
                TileWeights = new[] { 3.4f, 0.9f, 0.15f, 0.4f },
                ClusterCountScale = 0.55f,
                ClusterSizeScale = 1.5f,
                CorridorGap = 5.0f,

                CrateCount = 7,
                DepthRarityBias = 3.0f,

                // 1.0, after measuring 1.3.
                //
                // The reasoning for pushing the ring out was that open ground
                // with a tight ring is an ambush rather than open ground. The
                // play-test disagreed with the conclusion: a wider ring means
                // fewer of them are near you at any moment, so The Flats came
                // back with a third of the payout *and* half the peak crowd of
                // the other two — which is not a trade, it is a smaller run, and
                // nobody would ever pick it.
                //
                // What actually makes open ground open is that there is nothing
                // to break contact behind, not that the crowd starts further
                // away. The ring stays where it is and the terrain does the work.
                SpawnRingScale = 1.0f,

                GroundTint = new Color(1.06f, 1.02f, 0.90f),
                PropTint = new Color(1.04f, 1.0f, 0.92f),
            },
        };

        foreach (BiomeResource biome in biomes)
        {
            string path = $"{OutputDir}/{biome.BiomeName.ToLower().Replace(' ', '_')}.tres";
            Error err = ResourceSaver.Save(biome, path);
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
