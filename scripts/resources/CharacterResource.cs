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

    /// What this survivor is, in two or three words: `ASSAULT`, `RECON / TECH`.
    ///
    /// Separate from `Blurb`, which says what playing them is like. The role is
    /// the label on their design sheet and on the select screen's card; the blurb
    /// is the sentence underneath. One is identity and the other is advice.
    [Export] public string Role { get; set; } = "";

    /// The character-select illustration, or empty for a survivor with none.
    ///
    /// A path rather than a `Texture2D`, for the reason every other asset
    /// reference in these resources is: a `.tres` holding a resource reference
    /// loads the texture whenever the roster is read, which on this screen is
    /// every keypress. `BaseScreen` loads the one it is showing.
    [Export] public string PortraitPath { get; set; } = "";

    // --- What the body is -----------------------------------------------------

    [Export] public float MaxHealth { get; set; } = 100.0f;
    [Export] public float MoveSpeed { get; set; } = 6.0f;
    [Export] public int CarryCapacity { get; set; } = 20;
    [Export] public float BodyHeight { get; set; } = 2.2f;

    /// Authored low-poly body baked into the same vertex rig as the horde.
    /// Empty keeps the procedural body as a deliberate fallback.
    [Export] public string BakedBodyPath { get; set; } = "";

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

    /// A chance, per physics tick, that contact damage does not land. 0 to 1.
    [Export] public float StartingDodge { get; set; }

    /// A fraction of contact damage returned to whatever touched the player.
    [Export] public float StartingThorns { get; set; }

    /// Health per second, from the first second.
    [Export] public float StartingRegen { get; set; }

    /// Grants this survivor's head start to a run's modifiers.
    ///
    /// **One method, because this list was hand-copied into four places.** The
    /// fields above, `Player.ApplyCharacter` and three separate stages of
    /// `CharacterProbe` each enumerated the same four abilities — so a fifth was
    /// four edits, and the probe stages that check "the default carries no
    /// ability" and "this survivor gains something" would have gone on passing
    /// while ignoring it. That is the failure this project keeps paying for: a
    /// hand-written list of a growing thing's members goes stale in the
    /// direction that hides the bug.
    ///
    /// Added rather than assigned, except chill which takes the maximum: a
    /// trinket that granted a blade before this ran would otherwise be silently
    /// thrown away.
    public void GrantTo(RunModifiers mods)
    {
        mods.OrbitBlades += StartingBlades;
        mods.Chill = Mathf.Max(mods.Chill, StartingChill);
        mods.LootValueScale *= LootValueScale;
        mods.SearchRadiusBonus += SearchRadiusBonus;
        mods.Dodge += StartingDodge;
        mods.Thorns += StartingThorns;
        mods.Regen += StartingRegen;
    }

    /// Whether this survivor grants anything at all.
    ///
    /// Measured by granting to a fresh `RunModifiers` and asking whether
    /// anything moved, rather than by testing each field again — which is the
    /// same list a third time and the same way for it to go stale.
    public bool HasAbility
    {
        get
        {
            var mods = new RunModifiers();
            GrantTo(mods);

            return mods.OrbitBlades > 0 || mods.Chill > 0.0f
                || Mathf.Abs(mods.LootValueScale - 1.0f) > 0.001f
                || mods.SearchRadiusBonus > 0.0f || mods.Dodge > 0.0f
                || mods.Thorns > 0.0f || mods.Regen > 0.0f;
        }
    }

    /// The ability in words, for the select screen and for a probe's output.
    ///
    /// Empty when there is none, which is RIN and is correct rather than a gap.
    public string AbilityLine
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();

            if (StartingBlades > 0)
                parts.Add(StartingBlades == 1 ? "1 blade" : $"{StartingBlades} blades");

            if (StartingChill > 0.0f)
                parts.Add($"chill {StartingChill:F2}");

            if (Mathf.Abs(LootValueScale - 1.0f) > 0.001f)
                parts.Add($"loot x{LootValueScale:F2}");

            if (SearchRadiusBonus > 0.0f)
                parts.Add($"reach +{SearchRadiusBonus:F1} m");

            if (StartingDodge > 0.0f)
                parts.Add($"dodge {StartingDodge:F2}");

            if (StartingThorns > 0.0f)
                parts.Add($"thorns {StartingThorns:F2}");

            if (StartingRegen > 0.0f)
                parts.Add($"regen {StartingRegen:F1}/s");

            return string.Join(", ", parts);
        }
    }

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
