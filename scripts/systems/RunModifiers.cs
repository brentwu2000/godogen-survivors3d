using Godot;

/// Everything an in-run upgrade can change, in one place.
///
/// The alternative was what the first five options did: reach into whichever
/// system owns the number and add to it. That works for "+12 max health" and
/// stops working the moment an upgrade is a *rule* rather than a number — a
/// chance to crit is read by the weapon, a chance to ignite is read by the
/// horde, and a chance to shrug off a hit is read by the player. Scattering
/// those means three systems each holding a field nobody can find from the
/// upgrade that granted it.
///
/// Plain fields, no logic. Owned by the player because the player is what a run
/// is; read by the weapon, the horde and the loot containers at the point of
/// use. Everything here is zero on a fresh run, so the modifier layer costs
/// nothing until something grants it.
public sealed class RunModifiers
{
    // ---- weapon ----

    /// Extra targets a shot passes through, on top of the weapon's own.
    public int Pierce;

    /// Chance for a hit to deal CritMultiplier instead of its normal damage.
    public float CritChance;
    public float CritMultiplier = 2.0f;

    /// Multiplies the delay between attacks. Below 1 is faster.
    public float AttackDelayScale = 1.0f;

    /// Multiplies melee arc radius, throwable blast radius and burn radius —
    /// one "area" number, because a player who took it expects everything they
    /// throw and swing to get bigger.
    public float AreaScale = 1.0f;

    /// Added to the weapon's knockback.
    public float Knockback;

    // ---- on kill ----

    /// Chance a kill leaves burning ground where the body fell.
    public float IgniteChance;

    /// Chance a kill detonates. Small radius: this is chip damage that chains
    /// through a crowd, not a second pipe bomb.
    public float DetonateChance;

    /// Health returned per kill.
    public float Lifesteal;

    // ---- survival ----

    /// Health per second, always on.
    public float Regen;

    /// Chance to take no contact damage this tick.
    public float Dodge;

    /// Damage per second dealt back to whatever is touching the player.
    public float Thorns;

    // ---- economy ----

    /// Extra metres a loot container can be searched from.
    public float SearchRadiusBonus;

    /// Multiplies the value of everything pulled out of a crate.
    public float LootValueScale = 1.0f;

    /// Blades circling the player. Each one is a real object with a reach of its
    /// own; see `RunKit`.
    public int OrbitBlades;

    /// Stacks of the shockwave, which is a pulse rather than a number — more
    /// stacks means a shorter wait, a wider ring and a harder push.
    public int PulseStacks;

    /// Chance a hit arcs to a second enemy nearby, and the fraction of the
    /// damage that arrives there.
    public float ChainChance;

    /// How much slower an enemy moves near the player, 0 to just under 1.
    ///
    /// Never reaches 1. Compounds multiplicatively per stack so it approaches a
    /// limit rather than crossing it — an enemy stopped dead is not a threat
    /// managed, it is a free kill standing still.
    public float Chill;

    /// Rolls each contract, record and payout untouched — this is the one thing
    /// a run upgrade may not reach, for the same reason practice is not for
    /// sale: what is earned outside the run stays outside it.
    /// Back to a fresh run.
    ///
    /// Every field named explicitly, which is the readable way to write it and
    /// the one that silently misses anything added later. The four kit fields
    /// below were added and not listed, and the symptom was not a compile error
    /// or a warning — it was three orbiting blades surviving into a run that had
    /// not bought any, killing an enemy in a test that had switched them off.
    ///
    /// `KitProbe` walks this by reflection now, so a fifth field cannot repeat it.
    public void Reset()
    {
        Pierce = 0;
        CritChance = 0.0f;
        CritMultiplier = 2.0f;
        AttackDelayScale = 1.0f;
        AreaScale = 1.0f;
        Knockback = 0.0f;
        IgniteChance = 0.0f;
        DetonateChance = 0.0f;
        Lifesteal = 0.0f;
        Regen = 0.0f;
        Dodge = 0.0f;
        Thorns = 0.0f;
        SearchRadiusBonus = 0.0f;
        LootValueScale = 1.0f;

        OrbitBlades = 0;
        PulseStacks = 0;
        ChainChance = 0.0f;
        Chill = 0.0f;
    }
}
