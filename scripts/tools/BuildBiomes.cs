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
                StructureSet = System.Array.ConvertAll(PropLibrary.YardStructureSet, kind => (int)kind),
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

                // Setts rather than poured bays. Small and dark-jointed, which
                // also does something the layout cannot: a floor with a fine grain
                // makes the arena read as *tight* even where it happens to be open.
                GroundSlabMetres = 2.4f,
                GroundSeamDarkness = 0.42f,
                GroundSlabVariation = 0.14f,
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

                // Barely a floor at all. Nine metres between joints and a seam
                // you have to look for — the point of this place is that there is
                // nothing to measure yourself against in any direction.
                GroundSlabMetres = 9.0f,
                GroundSeamDarkness = 0.16f,
                GroundSeamWidth = 0.018f,
                GroundSlabVariation = 0.06f,
            },

            // A street grid. The third question, and it is a genuinely third
            // one: Old Town has no line of fire and The Flats is nothing but
            // line of fire, and both of those are answers a build can be
            // constructed around before the run starts. A street is a line of
            // fire *that has a direction* — fifty metres one way and eight the
            // other — so the same build is right or wrong depending on which way
            // it is facing, and the decision moves from the loadout screen into
            // the run.
            //
            // Corridor-heavy with a wide gap, which sounds like a contradiction
            // and is the point: the walls are long and the ways through them are
            // easy, so nothing is ever sealed and everything is channelled.
            new()
            {
                BiomeName = "Ash District",
                Blurb = "long streets, blind corners; the crowd comes down one",
                TileWeights = new[] { 0.9f, 0.55f, 3.0f, 1.1f },

                // Few clusters, large. A street is made of long blocks, and many
                // small ones would give Old Town's answer with the city's props
                // on it — which is the exact reskin this biome exists not to be.
                ClusterCountScale = 0.95f,
                ClusterSizeScale = 1.65f,

                // Wide. A junction the crowd pours through is what makes holding
                // a street a decision rather than a chokepoint puzzle.
                CorridorGap = 4.6f,

                CrateCount = 9,
                DepthRarityBias = 2.1f,

                // Slightly in. The crowd should arrive already committed to a
                // street rather than trickling in from every direction at once.
                SpawnRingScale = 0.88f,

                // Cars, buses, hoardings, a kiosk — and a tower block and a
                // broken overpass on the horizon instead of a water tower.
                PropSet = System.Array.ConvertAll(PropLibrary.CitySet, kind => (int)kind),
                StructureSet = System.Array.ConvertAll(PropLibrary.CityStructureSet, kind => (int)kind),

                GroundTint = new Color(0.86f, 0.86f, 0.90f),
                PropTint = new Color(0.94f, 0.93f, 0.96f),

                // Later in the evening than the yard, and dirtier. The sun is
                // low and orange because it is going down behind the blocks, and
                // the fog is warm rather than near-black — a city burns, and the
                // haze over one is lit from underneath by whatever is still
                // alight. It is the same trick as the yard's dusk, one hour on.
                SunAngleDegrees = new Vector2(-28.0f, 62.0f),
                SunColour = new Color(1.0f, 0.78f, 0.55f),
                SunEnergy = 1.05f,

                AmbientColour = new Color(0.34f, 0.36f, 0.48f),
                AmbientEnergy = 0.5f,

                FogColour = new Color(0.10f, 0.07f, 0.06f),

                // Slightly further out than the default. Streets are the one
                // place a long shot is supposed to land, and fog at 35 m would
                // take back exactly what the layout gives.
                FogBegin = 13.0f,
                FogEnd = 44.0f,

                // A pylon and a wrecked coach; no grain silo on a high street.
                // The coach is the one landmark that is cover before it is a
                // beacon, which suits a place made of blocked streets.
                LandmarkSet = new[] { (int)LandmarkKind.Pylon, (int)LandmarkKind.Coach },

                // Patched road. Wide dark joints and real variation between
                // panels, because a street is resurfaced a bit at a time.
                GroundSlabMetres = 3.2f,
                GroundSeamDarkness = 0.5f,
                GroundSeamWidth = 0.038f,
                GroundSlabVariation = 0.17f,
            },

            // An interior, and the fourth question: **rooms**.
            //
            // Every other biome is a field with things standing in it, and the
            // things are what the layout varies. A building varies the *space* —
            // the cover is nearly all long partitions, so the arena is a set of
            // rooms with doorways between them, and the fight is about which room
            // you are in rather than what you are standing behind. Nothing that
            // works in the open works the same way in a corridor, and that is the
            // point of it existing.
            //
            // The dangerous version of this biome is the one that seals: mostly
            // walls with a narrow gap is a maze, and a maze is where the crowd
            // gets stuck and the run gets safer. `BiomeProbe` already asks that
            // question of Old Town; the gap here is wide for the same reason.
            new()
            {
                BiomeName = "Cold Storage",
                Blurb = "rooms and doorways; nothing has a long shot",
                TileWeights = new[] { 0.35f, 0.7f, 3.6f, 1.4f },

                // Many partitions, and long. The count is up and the size is up,
                // which is the combination none of the other three uses: Old Town
                // is many *small* pieces and The Flats is few large ones.
                ClusterCountScale = 1.5f,
                ClusterSizeScale = 1.45f,

                // Wide. See above — a doorway is not a chokepoint puzzle.
                CorridorGap = 4.2f,

                // Rooms are where things are kept. More crates than anywhere, and
                // worth the least each, because the walk between them is short and
                // the danger is being cornered rather than being caught in the
                // open.
                CrateCount = 12,
                DepthRarityBias = 1.3f,

                // Tight. Indoors, the crowd should already be in the building.
                SpawnRingScale = 0.74f,

                PropSet = System.Array.ConvertAll(PropLibrary.LabSet, kind => (int)kind),

                // Cold and low. The tints are the only lighting control a biome
                // has until E3, so an interior has to be sold by the palette —
                // and a blue-grey floor under blue-grey props is as close as two
                // multipliers get to "the sun is not in here".
                GroundTint = new Color(0.72f, 0.76f, 0.84f),
                PropTint = new Color(0.80f, 0.84f, 0.90f),

                // The one that makes this an interior rather than a field with
                // partitions on it, and it is three numbers.
                //
                // **The sun points almost straight down.** Nothing says "there is
                // a ceiling" like shadows that fall under things instead of away
                // from them — a low sun rakes across an arena and paints long
                // shadows, which is a statement about a horizon, and a horizon is
                // the thing a room does not have.
                //
                // **It is cold and weak.** Strip lighting, not daylight.
                //
                // **The fog closes at twenty-four metres.** That is the wall. The
                // arena is still a hundred and ten metres across and the player
                // can still walk all of it, but they can only ever see the room
                // they are in, so the map is discovered rather than surveyed.
                SunAngleDegrees = new Vector2(-78.0f, 15.0f),
                SunColour = new Color(0.80f, 0.88f, 0.95f),
                SunEnergy = 0.85f,

                // Up, and blue. Indoors the ambient is most of the light there
                // is: with the sun overhead every vertical face is in shadow, and
                // at the outdoor 0.55 the partitions came out as silhouettes.
                AmbientColour = new Color(0.40f, 0.48f, 0.58f),
                AmbientEnergy = 0.78f,

                // Not black — a lit room full of dust. Black fog indoors reads as
                // the level having an edge, which is the exact failure the sky was
                // added to fix.
                // Twenty-eight rather than twenty-four.
                //
                // The four metres are what let the full-height partitions be
                // *seen* rather than merely be there. At 24 m the nearest wall
                // that breaks the horizon was already black, so the arena closed
                // down without ever showing what was closing it — which is the
                // same picture as an empty field at night, only darker.
                FogColour = new Color(0.09f, 0.11f, 0.13f),
                FogBegin = 7.0f,
                FogEnd = 28.0f,

                // None of the three. All of them are outdoor objects — a
                // transmission tower, a grain silo and a crushed coach — and the
                // fog stops the view at twenty-four metres anyway, so a beacon
                // sited at two thirds of the arena would be invisible from
                // everywhere except directly underneath it.
                //
                // The gantry cranes and the vent stack are this place's beacons,
                // and they are `PropKind` scenery rather than glTF: they already
                // fill the Tall and Sign roles, so the arena is not short of
                // landmarks, only of the wrong ones.
                LandmarkSet = new[] { -1 },

                // The roof, and it is the difference between an interior and a
                // blue field at night.
                //
                // Eight metres: clear of the camera at 5.7, low enough that the
                // beams are legible from the ground, and high enough that the
                // gantry cranes at ten still read as tall equipment rather than
                // as things poking through the ceiling. They do intersect it,
                // and that is correct — a gantry runs *in* a building.
                // Nine, so the partitions clear it. They are authored at 7.6 m
                // and the generator scales cover by up to 1.15, which is 8.74 —
                // and a wall poking through the roof is only invisible because
                // the roof is opaque from below, which is not a reason.
                CeilingHeight = 9.0f,
                CeilingColour = new Color(0.15f, 0.17f, 0.19f),

                // Tile, and this is doing as much work as the fog is.
                //
                // A 1.2 m grid under the player's feet is the single clearest
                // statement that this is a floor somebody laid rather than
                // ground somebody stands on — and against the nine-metre bays of
                // The Flats it makes the same arena feel a different size without
                // moving a wall. Faint joints: it was clean once.
                GroundSlabMetres = 1.2f,
                GroundSeamDarkness = 0.24f,
                GroundSeamWidth = 0.022f,
                GroundSlabVariation = 0.05f,
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
