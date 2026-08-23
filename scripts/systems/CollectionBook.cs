using Godot;

/// The sets of curiosities, and what completing one pays.
///
/// Loot in this game is a number: everything that comes back is worth credits and
/// the only question is how much bulk it cost to carry. That is a clean economy
/// and it gives the player nothing to *want* — a circuit board and a wedding ring
/// are the same object with different values.
///
/// A curiosity is worth credits like anything else, and is also one of three. The
/// set pays a bounty when all three have been seen, which turns a specific item
/// on the floor of a specific run into something the player recognises.
///
/// **Selling them is not a mistake.** The record is written the moment a piece
/// lands in the stash — at the door, not at the locker — so a player who sells
/// their whole stash for the credits has still completed the set. A collection
/// that punished the ordinary way of turning loot into money would be a trap
/// disguised as content, and the player would find out about it two hours in.
public static class CollectionBook
{
    public readonly record struct Set(string Name, string[] Pieces, int Bounty);

    /// Two sets of three. Small on purpose: a set of eight is a spreadsheet, and
    /// the point is recognition rather than accumulation.
    public static readonly Set[] All =
    {
        // Personal effects. Worth the least and the one people finish first,
        // because the pieces read as belonging to somebody.
        new("Someone's Life",
            new[] { "Wedding Ring", "Crayon Drawing", "Dog Tags" },
            400),

        // Industrial. Deeper, dearer, and the bounty is most of a tier-2 weapon.
        new("The Grid",
            new[] { "Fuse Coupling", "Turbine Blade", "Control Rod" },
            900),
    };

    /// Whether this item is a piece the player has not banked yet.
    ///
    /// **The question the rest of the game needed and could not ask.** A
    /// curiosity was an item with a value like any other, so the convenience keys
    /// treated it like one — and `[F] secure` picks by total value, which puts a
    /// 130-credit crayon drawing behind a 440-credit antiviral serum. The safe box
    /// is what survives dying, so the one item in the bag that cannot be bought
    /// again was the last thing put somewhere safe.
    ///
    /// `[R] drop worst` was the other suspect and turned out to be innocent: it
    /// picks by value *per bulk*, and every piece in the book is 65 to 160 a bulk
    /// against 18 for rifle rounds and 14 for canned food. The guard is kept
    /// anyway, because it costs nothing and a bag holding only expensive things
    /// is exactly when the wrong answer would be unrecoverable — but the probe
    /// that proves it has to build that bag on purpose, or it proves nothing.
    ///
    /// A piece already recorded is ordinary loot again, because it is: the set
    /// remembers it, and the copy in the bag is worth its credits and nothing
    /// more.
    public static bool Wanted(Profile? profile, string itemName)
    {
        if (SetOf(itemName) < 0)
            return false;

        return profile == null || !profile.Collected.Contains(itemName);
    }

    /// How many pieces of `itemName`'s set are recorded, and how many there are.
    /// Zero pieces for an item that belongs to no set.
    public static (int Found, int Total, string Name) Progress(Profile? profile, string itemName)
    {
        int set = SetOf(itemName);
        if (set < 0)
            return (0, 0, string.Empty);

        return (profile == null ? 0 : Found(profile, set), All[set].Pieces.Length, All[set].Name);
    }

    /// Which set a piece belongs to, or -1.
    public static int SetOf(string itemName)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (System.Array.IndexOf(All[i].Pieces, itemName) >= 0)
                return i;
        }

        return -1;
    }

    /// Whether every piece of a set has been seen.
    public static bool Complete(Profile profile, int set)
    {
        if (set < 0 || set >= All.Length)
            return false;

        foreach (string piece in All[set].Pieces)
        {
            if (!profile.Collected.Contains(piece))
                return false;
        }

        return true;
    }

    /// Pays out every set that is finished and has not been paid for.
    ///
    /// Called at the door alongside the stash handover, so the bounty arrives in
    /// the same breath as the run's takings rather than being discovered later on
    /// a screen. Returns what was paid, so the debrief can say so.
    public static int Claim(Profile profile)
    {
        int paid = 0;

        for (int i = 0; i < All.Length; i++)
        {
            if (!Complete(profile, i) || profile.ClaimedSets.Contains(All[i].Name))
                continue;

            profile.ClaimedSets.Add(All[i].Name);
            profile.Credits += All[i].Bounty;
            paid += All[i].Bounty;

            GD.Print($"set complete: {All[i].Name} — {All[i].Bounty} credits");
        }

        return paid;
    }

    /// How many pieces of a set have been seen. For the records wall.
    public static int Found(Profile profile, int set)
    {
        if (set < 0 || set >= All.Length)
            return 0;

        int found = 0;
        foreach (string piece in All[set].Pieces)
        {
            if (profile.Collected.Contains(piece))
                found++;
        }

        return found;
    }
}
