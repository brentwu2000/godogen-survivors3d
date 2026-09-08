using Godot;

/// Which bodies are drawn from a bake, decided by what is on disk.
///
/// `resources/bodies/<slot>.res` is the body for that slot, and the slot names
/// are the ones the game already uses: `walker` … `boss` from
/// `EnemyTypeResource.TypeName`, and the five survivors from
/// `CharacterResource.CharacterName` lowercased — so `rin` through `yuna`. Drop a
/// file in and it is drawn;
/// take it out and the procedural body comes back.
///
/// **This exists because pointing a `BakedBodyPath` at a model is a code edit,
/// and the models arrive faster than the code should change.** Nine variants and
/// three survivors is twelve string literals in two build tools, each of which
/// has to be edited, compiled and re-run before anybody can *look* at the model
/// — and looking at it is the decision. Two rounds of authored humanoids were
/// judged too late, and part of why is that seeing one in the game was a
/// twenty-minute errand.
///
/// The explicit field still wins where it is set, and it is what a bake with a
/// name of its own needs: a fixture, a second candidate for the same slot, or a
/// shared body two slots draw from. What changed is that leaving it empty no
/// longer means "procedural" — it means "whatever is on the shelf".
///
/// Nothing here validates. `BodyRenderer` already refuses a bake whose standing
/// height disagrees with the design height and says so, `BakedBody.Build`
/// refuses one whose arrays disagree, and both fall back to the procedural body
/// — so a wrong file on the shelf is a warning and a body that still draws,
/// which is the behaviour a convention needs to be safe.
public static class BodyBakes
{
    public const string Shelf = "res://resources/bodies";

    /// Where a slot's bake would be, whether or not anything is there.
    ///
    /// Lowercased because the survivors are `Drifter` and the enemies are
    /// `walker`, and a convention that is case-sensitive on Windows and not on
    /// the export target is a convention that works until it ships.
    public static string PathFor(string slot) => $"{Shelf}/{slot.ToLowerInvariant()}.res";

    /// The path this slot should load, or empty for the procedural body.
    ///
    /// `named` is the resource's own `BakedBodyPath`. It is returned unchanged
    /// even when the file is missing, because a path somebody typed is a claim
    /// worth reporting rather than quietly replacing — that warning lives in
    /// `BodyRenderer` and predates this file.
    public static string Resolve(string named, string slot)
    {
        if (!string.IsNullOrEmpty(named))
            return named;

        string shelved = PathFor(slot);
        return ResourceLoader.Exists(shelved) ? shelved : string.Empty;
    }

    /// One line, once per process, naming every slot drawn from the shelf.
    ///
    /// A convention needs to say what it found. The failure mode of picking a
    /// file up by its name is picking up a file nobody meant — a candidate left
    /// over from an intake, or a slot spelled the way the modelling tool spelled
    /// it — and that is invisible by construction, because the whole point is
    /// that no code mentions the file.
    ///
    /// Called by `Horde` on the run that builds the bodies, so it costs one
    /// `DirAccess` listing per process and appears above the run's own output.
    public static void Announce()
    {
        if (_announced)
            return;

        _announced = true;

        using DirAccess? shelf = DirAccess.Open(Shelf);
        if (shelf == null)
            return;

        var found = new System.Collections.Generic.List<string>();
        foreach (string file in shelf.GetFiles())
        {
            // `.res` and not `.res.remap`. An exported build renames every
            // resource it converts, and a listing that only knows the source
            // extension reports an empty shelf in the one place nobody can
            // attach a debugger to.
            string name = file.TrimSuffix(".remap");
            if (!name.EndsWith(".res", System.StringComparison.Ordinal))
                continue;

            if (Slots.Contains(name.GetBaseName()))
                found.Add(name.GetBaseName());
        }

        found.Sort();

        GD.Print(found.Count == 0
            ? "bodies: every slot procedural; nothing on the shelf at " + Shelf
            : $"bodies: {string.Join(", ", found)} from bakes, the rest procedural");
    }

    private static bool _announced;

    /// The height this slot is designed at, or -1 if the name is not a slot.
    ///
    /// **Read from the resource rather than from its `.tres`, because the file
    /// does not always say.** A `.tres` omits any property equal to its class
    /// default, so `walker.tres` has no `DesignHeightMeters` line at all (2.0 is
    /// the default) and neither does `drifter.tres` (2.2 is). Anything that
    /// greps the file for a height finds nothing for exactly the two slots most
    /// likely to be baked first, and a bake at the wrong height is a body with
    /// its feet through the floor.
    ///
    /// The horde is checked first and the survivors second; no name is in both,
    /// and `Slots` is the list either way.
    public static float DesignHeight(string slot)
    {
        string name = slot.ToLowerInvariant();

        if (ResourceLoader.Exists($"res://resources/enemies/{name}.tres")
            && ResourceLoader.Load<EnemyTypeResource>($"res://resources/enemies/{name}.tres")
               is { } type)
            return type.DesignHeightMeters;

        if (ResourceLoader.Exists($"res://resources/characters/{name}.tres")
            && ResourceLoader.Load<CharacterResource>($"res://resources/characters/{name}.tres")
               is { } who)
            return who.BodyHeight;

        return -1.0f;
    }

    /// Every slot name, so a file that is not one can be told apart from a file
    /// that is. Hand-written rather than read from the two `resources/`
    /// directories, because this has to name the *slots* and those directories
    /// are what a build tool wrote last — if a slot is added, this list failing
    /// to mention it is a compile-free mistake either way, and `BodyProbe`
    /// checks the two against each other.
    public static readonly System.Collections.Generic.HashSet<string> Slots = new()
    {
        "walker", "runner", "brute", "bloater", "spitter",
        "boss", "stalker", "bulwark", "lantern",
        "rin", "mika", "akira", "sora", "yuna",
    };
}
