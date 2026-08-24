using Godot;

/// The list of places, in a fixed order, loaded once.
///
/// Order is the biome's identity — the profile stores an index and the base
/// screen shows a list — so new entries go on the end. Read off the resource
/// directory rather than kept as a second list beside it, for the same reason
/// the shop catalogue is: two copies of the stock disagree the first time one
/// changes.
public static class BiomeBook
{
    private static BiomeResource[]? _all;

    /// Names in the order they are listed, so a saved index means the same place
    /// after a restart. Sorted by filename rather than by directory order, which
    /// differs between the editor and an exported build.
    private static readonly string[] Order =
        { "rail_yard", "old_town", "the_flats", "ash_district", "cold_storage" };

    public static BiomeResource[] All
    {
        get
        {
            if (_all != null)
                return _all;

            var loaded = new System.Collections.Generic.List<BiomeResource>(Order.Length);
            foreach (string name in Order)
            {
                var biome = GD.Load<BiomeResource>($"res://resources/biomes/{name}.tres");
                if (biome != null)
                    loaded.Add(biome);
                else
                    GD.PushWarning($"BiomeBook: {name}.tres missing — run BuildBiomes.cs");
            }

            // A default rather than an empty list. Everything downstream reads
            // this to decide what to generate, and an arena with no layout rule
            // at all is a flat empty plane the player will read as a broken
            // level rather than as a missing file.
            if (loaded.Count == 0)
                loaded.Add(new BiomeResource { BiomeName = "Rail Yard" });

            _all = loaded.ToArray();
            return _all;
        }
    }

    public static BiomeResource Load(int index) =>
        All[Mathf.Clamp(index, 0, All.Length - 1)];

    /// How many extractions before a place is on the list.
    ///
    /// The first is open immediately; the rest are not, because terrain is only
    /// a decision once the player knows what the loadouts do — offered on run
    /// one it is three names with no basis to choose between them, which is a
    /// menu rather than a choice.
    public static int OpensAt(int index) => index <= 0 ? 0 : 2 + index * 3;

    public static bool Allows(Profile profile, int index) =>
        profile.RunsSurvived >= OpensAt(index);
}
