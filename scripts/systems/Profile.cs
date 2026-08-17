using Godot;
using Godot.Collections;

/// Everything that survives a run: credits, the stash, and practice.
///
/// Stored as plain JSON rather than a Godot Resource so a corrupted or
/// hand-edited file fails as a parse error that can be reported, instead of as a
/// deserialised object with silently wrong fields.
public sealed class Profile
{
    private const int Version = 1;

    public int Credits { get; set; }

    /// Item name to count. Names rather than resource paths, so moving a .tres
    /// does not orphan a player's stash.
    public Dictionary<string, int> Stash { get; } = new();

    /// Indexed by WeaponCategory.
    public int[] Proficiency { get; } = new int[4];

    public string LoadoutWeapon { get; set; } = "res://resources/weapons/scavenged_rifle.tres";

    public int RunsSurvived { get; set; }
    public int RunsLost { get; set; }

    public void AddToStash(string itemName, int count)
    {
        if (count <= 0)
            return;

        Stash[itemName] = Stash.TryGetValue(itemName, out int existing) ? existing + count : count;
    }

    public string ToJson()
    {
        var proficiency = new Array<int>();
        foreach (int level in Proficiency)
            proficiency.Add(level);

        var root = new Dictionary
        {
            { "version", Version },
            { "credits", Credits },
            { "stash", Stash },
            { "proficiency", proficiency },
            { "loadout", LoadoutWeapon },
            { "runs_survived", RunsSurvived },
            { "runs_lost", RunsLost },
        };

        return Json.Stringify(root, "  ");
    }

    /// Returns null when the text is not a profile this build understands. A
    /// caller that gets null should start fresh rather than half-apply a file.
    public static Profile? FromJson(string text)
    {
        var json = new Json();
        if (json.Parse(text) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return null;

        var root = json.Data.AsGodotDictionary();
        if (!root.TryGetValue("version", out Variant version) || version.AsInt32() != Version)
            return null;

        var profile = new Profile();

        if (root.TryGetValue("credits", out Variant credits))
            profile.Credits = credits.AsInt32();

        if (root.TryGetValue("stash", out Variant stash) && stash.VariantType == Variant.Type.Dictionary)
        {
            foreach (var pair in stash.AsGodotDictionary())
                profile.Stash[pair.Key.AsString()] = pair.Value.AsInt32();
        }

        if (root.TryGetValue("proficiency", out Variant proficiency) && proficiency.VariantType == Variant.Type.Array)
        {
            var levels = proficiency.AsGodotArray();
            for (int i = 0; i < profile.Proficiency.Length && i < levels.Count; i++)
                profile.Proficiency[i] = levels[i].AsInt32();
        }

        if (root.TryGetValue("loadout", out Variant loadout))
            profile.LoadoutWeapon = loadout.AsString();

        if (root.TryGetValue("runs_survived", out Variant survived))
            profile.RunsSurvived = survived.AsInt32();

        if (root.TryGetValue("runs_lost", out Variant lost))
            profile.RunsLost = lost.AsInt32();

        return profile;
    }
}
