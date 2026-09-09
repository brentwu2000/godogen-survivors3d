using Godot;

/// The silhouettes a shot can cross the screen as.
///
/// **Every projectile in this game was one sprite.** Per-shot tint and scale
/// arrived a phase ago and fixed half of it — an arrow is brown and small, a
/// launched charge is orange and big — but the *shape* was the same 160×40 streak
/// for all of them, so at the distance a shot is actually read the pump shotgun's
/// pellet and the marksman rifle's round were one object at two sizes. Shape is
/// the channel that survives fog, motion blur and a crowded frame; colour is the
/// one that does not.
///
/// The order is the layer order in the texture array, so it is the file order in
/// `Paths` and nothing may be inserted in the middle without both moving.
public enum BoltShape : byte
{
    /// The default round: a short bright capsule with a stub of a tail.
    Slug = 0,

    /// Shaft, head and fletching. The only shot in the game that should look
    /// hand-made.
    Arrow = 1,

    /// One long thin lance. Length is the tell at thirty metres, and the marksman
    /// rifle is sold entirely on thirty metres.
    Lance = 2,

    /// A speck. A shotgun's shot is the *spread*, so the individual pellet has to
    /// be the least interesting thing on screen or eight of them read as a beam.
    Pellet = 3,

    /// A finned charge with something in it, big enough to watch travel.
    Charge = 4,

    /// The horde's own, and the only one the player is on the receiving end of:
    /// a ragged glob with a dribble behind it.
    Spit = 5,
}

/// Where the shapes live, in layer order.
///
/// One list rather than two, because the player's shots and the horde's are drawn
/// by two separate renderers that must agree on what layer four is. They were
/// loading the same single file, which made that agreement free and invisible;
/// six files makes it a thing to get wrong exactly once.
public static class Bolts
{
    public static readonly string[] Paths =
    {
        "res://assets/sprites/bolts/slug.png",
        "res://assets/sprites/bolts/arrow.png",
        "res://assets/sprites/bolts/lance.png",
        "res://assets/sprites/bolts/pellet.png",
        "res://assets/sprites/bolts/charge.png",
        "res://assets/sprites/bolts/spit.png",
    };

    /// Width and height of every one of them, which the array format requires to
    /// match. Wide, because a bolt is drawn pointing along its own travel and the
    /// long axis is the one carrying the silhouette.
    public const int Width = 160;
    public const int Height = 40;
}
