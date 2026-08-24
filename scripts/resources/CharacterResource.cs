using Godot;

/// Who you are, as data.
///
/// One survivor, one set of numbers, and the shop selling everything else — so
/// every run in the game so far has started from the same body with the same
/// hundred health and the same twenty of carrying capacity. The choices a player
/// makes are all *inside* a run, and the meta layer around it grants only
/// upgrades, which is the same choice again with bigger numbers.
///
/// A character is the one decision made **before** the loadout, and it has to
/// change what the loadout is for. That is why the abilities here are not damage
/// or fire rate: those are what the shop already sells, and a character that sold
/// them again would be a difficulty setting with a name.
///
/// Nothing here is strictly better. Each survivor gives up something the others
/// keep, and the give-up has to be a thing the player will *feel* rather than
/// read — capacity, health and speed are all felt within thirty seconds.
[GlobalClass]
public partial class CharacterResource : Resource
{
    [Export] public string CharacterName { get; set; } = "";

    /// One line on the base screen. A character the player cannot choose between
    /// on sight is a menu.
    [Export] public string Blurb { get; set; } = "";

    // --- What the body is -----------------------------------------------------

    [Export] public float MaxHealth { get; set; } = 100.0f;
    [Export] public float MoveSpeed { get; set; } = 6.0f;
    [Export] public int CarryCapacity { get; set; } = 20;
    [Export] public float BodyHeight { get; set; } = 2.2f;

    // --- What the body looks like ---------------------------------------------
    //
    // Colour rather than shape, and that is a decision rather than laziness. The
    // player is the one body that must never be mistaken for the horde for even
    // a frame, and the thing carrying that is hue: blue against a crowd of
    // greens, greys and reds. Three survivors that were three *silhouettes* would
    // each have to win that fight separately, and two of them would lose it.

    [Export] public Color Torso { get; set; } = new(0.22f, 0.34f, 0.52f);
    [Export] public Color Limb { get; set; } = new(0.26f, 0.30f, 0.38f);
    [Export] public Color Head { get; set; } = new(0.72f, 0.60f, 0.48f);

    // --- What it can do that a weapon cannot ----------------------------------
    //
    // Every one of these is an existing `RunModifiers` field granted at the start
    // of a run. Deliberately: the kit cards, the gear and the trinkets all reach
    // the same numbers, so a character's ability is a *head start on a strategy*
    // rather than a mechanic nothing else in the game speaks to. It also means
    // none of this needed new systems, and a character that stacks with the deck
    // is a character the deck can be built around.

    /// Blades already orbiting when the run begins.
    [Export] public int StartingBlades { get; set; }

    /// Chill already on the ground. 0 to 1, and see `Horde.ChillRadius` for how
    /// far it reaches.
    [Export] public float StartingChill { get; set; }

    /// Multiplies what everything in the bag is worth when it is banked.
    [Export] public float LootValueScale { get; set; } = 1.0f;

    /// Extra metres on the reach for searching a crate.
    [Export] public float SearchRadiusBonus { get; set; }

    // --- Getting hold of one ---------------------------------------------------

    /// Extractions before this survivor is on the list. Zero for the starting
    /// one.
    ///
    /// Gated on runs rather than on credits, because a character is a way to
    /// play and not a purchase: buying one with money earned by the *other* way
    /// to play is a strange sentence, and it would make the second survivor a
    /// reward for being good at the first.
    [Export] public int OpensAfter { get; set; }
}
