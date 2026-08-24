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

            _all = loaded.ToArray();
            return _all;
        }
    }

    public static CharacterResource Load(int index) =>
        All[Mathf.Clamp(index, 0, All.Length - 1)];

    /// Whether the profile has earned this one yet.
    public static bool Allows(Profile profile, int index) =>
        index >= 0 && index < All.Length && profile.RunsSurvived >= All[index].OpensAfter;
}
