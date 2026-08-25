using Godot;

/// The survivors, in a fixed order, loaded once.
///
/// Order is the character's identity — the profile stores an index — so new
/// entries go on the end. Read off the resource directory rather than kept as a
/// second list beside it, the same as `BiomeBook` and for the same reason: two
/// copies of a roster disagree the first time one changes.
public static class CharacterBook
{
    private static CharacterResource[]? _all;

    /// Sorted by this list rather than by directory order, which differs between
    /// the editor and an exported build.
    private static readonly string[] Order = { "drifter", "courier", "warden" };

    public static CharacterResource[] All
    {
        get
        {
            if (_all != null)
                return _all;

            var loaded = new System.Collections.Generic.List<CharacterResource>(Order.Length);
            foreach (string name in Order)
            {
                var one = GD.Load<CharacterResource>($"res://resources/characters/{name}.tres");
                if (one != null)
                    loaded.Add(one);
                else
                    GD.PushWarning($"CharacterBook: {name}.tres missing — run BuildCharacters.cs");
            }

            // A default rather than an empty list, and its numbers are the ones
            // `Player` shipped with. Everything downstream reads this to build the
            // survivor, and a run with no character at all is a player with zero
            // health — which reads as a physics bug rather than as a missing file.
            if (loaded.Count == 0)
                loaded.Add(new CharacterResource { CharacterName = "Drifter" });

            WarnAboutAnythingNotInOrder();

            _all = loaded.ToArray();
            return _all;
        }
    }

    /// Says so when a character file exists that `Order` does not name.
    ///
    /// `Order` is hand-written and that is deliberate — directory order differs
    /// between the editor and an exported build, and a roster whose numbering
    /// moved between the two would put the player in a different survivor after
    /// an export. The cost of that decision is the failure mode this project
    /// keeps paying for: **a hand-written list of a growing thing's members goes
    /// stale in the direction that hides the bug.** Drop a fourth `.tres` into the
    /// directory and it is simply absent, with nothing anywhere reporting it.
    ///
    /// So the list stays and the silence goes. A warning rather than a throw,
    /// because a character nobody can select is a smaller problem than a game
    /// that will not start.
    private static void WarnAboutAnythingNotInOrder()
    {
        using var directory = DirAccess.Open("res://resources/characters");
        if (directory == null)
            return;

        foreach (string file in directory.GetFiles())
        {
            if (!file.EndsWith(".tres") && !file.EndsWith(".tres.remap"))
                continue;

            string name = file.Replace(".remap", "").Replace(".tres", "");
            if (System.Array.IndexOf(Order, name) < 0)
            {
                GD.PushWarning($"CharacterBook: {name}.tres exists and is not in Order — "
                             + "it will never be offered. Add it to the list in CharacterBook.");
            }
        }
    }

    public static CharacterResource Load(int index) =>
        All[Mathf.Clamp(index, 0, All.Length - 1)];

    /// The index of a survivor by its file name, or -1.
    ///
    /// For the balance sweep, which asks for a character by name because an index
    /// into a hand-ordered list is not a thing anybody can type correctly twice.
    public static int IndexOf(string fileName)
    {
        int at = System.Array.IndexOf(Order, fileName.ToLowerInvariant());
        return at >= 0 && at < All.Length ? at : -1;
    }

    /// Whether the profile has earned this one yet.
    public static bool Allows(Profile profile, int index) =>
        index >= 0 && index < All.Length && profile.RunsSurvived >= All[index].OpensAfter;
}
