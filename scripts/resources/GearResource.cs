using Godot;

public enum GearSlot
{
    Armour,
    Backpack,
    Boots,

    /// The slot that is about the kit rather than about the body.
    ///
    /// Appended, never inserted: the equipped-gear array is indexed by this enum
    /// and saved by index, so a value in the middle would silently move every
    /// existing player's boots into their backpack slot.
    Trinket,
}

/// A piece of equipment: where a run starts, and how far it can climb.
///
/// Gear is the only axis that does both. Practice raises the starting point and
/// cannot be lost; gear raises the starting point *and* the ceiling, and is left
/// on the ground when the player dies wearing it. That asymmetry is the whole
/// reason taking the good set out is a decision rather than a formality.
///
/// The three equipped pieces are summed. A slot that grants nothing in a
/// category leaves it at zero rather than carrying its own idea of a default —
/// summing defaults would make three pieces of empty gear better than one.
[GlobalClass]
public partial class GearResource : Resource
{
    [Export] public string GearName { get; set; } = "";
    [Export] public GearSlot Slot { get; set; } = GearSlot.Armour;

    /// Shop tier. 1 is starting kit — owned from the first run, never lost.
    [Export] public int Tier { get; set; } = 1;

    /// Credits to buy one. Zero means it is not for sale.
    [Export] public int Price { get; set; }

    // --- Starting point -----------------------------------------------------

    [Export] public float HealthBonus { get; set; }

    /// Flat mitigation. Subtracted from an incoming rate or amount rather than
    /// scaling it, so armour is the answer to a crowd of weak contacts and never
    /// the answer to a brute — which is the trade that makes it worth choosing.
    [Export] public float ArmourBonus { get; set; }

    [Export] public float MoveSpeedBonus { get; set; }
    [Export] public int CarryBonus { get; set; }
    [Export] public int SafeBoxBonus { get; set; }

    // --- Ceiling ------------------------------------------------------------
    //
    // How many in-run upgrades of each kind this piece permits. Reaching a cap
    // takes that option out of the pool, so the ceiling is something the player
    // watches happen rather than a number hidden in a formula.

    [Export] public int HealthUpgradeCap { get; set; }
    [Export] public int ArmourUpgradeCap { get; set; }
    [Export] public int SpeedUpgradeCap { get; set; }
    [Export] public int SearchUpgradeCap { get; set; }

    // --- Rules --------------------------------------------------------------
    //
    // What the piece grants before the first level-up, and which rules it lets
    // the run stack further than the default.
    //
    // This is what turns the shop from a list of numbers into a decision. Two
    // pieces in the same slot at the same price should not be better and worse;
    // they should be answers to different questions — and the only way to say
    // that in data is to let a piece change a *rule* rather than a stat. A
    // bandolier that carries less loot and pierces two enemies is not a worse
    // backpack, it is a different run.

    /// Enemies a shot passes through before stopping, on top of the weapon's own.
    [Export] public int PierceBonus { get; set; }

    /// Multiplies every effect radius: melee arcs, blasts, burning ground.
    [Export] public float AreaBonus { get; set; }

    /// Fraction of contact damage returned to whatever is touching the player.
    [Export] public float ThornsBonus { get; set; }

    /// Health per second, always.
    [Export] public float RegenBonus { get; set; }

    /// Extra shove on every hit.
    [Export] public float KnockbackBonus { get; set; }

    /// Chance to take nothing from an incoming tick.
    [Export] public float DodgeBonus { get; set; }

    /// What a trinket adds to the kit a run starts with.
    ///
    /// Separate from the rule bonuses above because these are not rules — a
    /// blade is an object in the world, and starting a run with one is a
    /// different kind of head start from starting with more pierce.
    [Export] public int OrbitBonus { get; set; }
    [Export] public int ShockwaveBonus { get; set; }
    [Export] public float ChainBonus { get; set; }
    [Export] public float ChillBonus { get; set; }

    /// Named for the growth option each one raises, because the pairing is the
    /// point: a piece that grants a rule the run cannot then stack is a piece
    /// whose identity stops mattering after the first minute.
    // --- What it makes the run become --------------------------------------

    /// Which growth line this piece pulls the deck toward, or None.
    ///
    /// **This is the field that makes a loadout a decision rather than a sum.**
    /// Everything above is a number added to a number: more health, more pierce,
    /// a blade already turning. An assessment of the game called the armour,
    /// backpack and boot choices "numerical efficiency trades", and it was right
    /// — nothing the player bought changed how the run was *played*, only how
    /// easily it went.
    ///
    /// A piece that favours a line makes that line's cards likelier to be
    /// offered, exactly as taking one of its cards does (see
    /// `RunGrowth.LineAffinity`). So the shop is where a build starts: a scope
    /// that leans Gunnery means the run will keep handing you Gunnery, and two
    /// pieces leaning the same way is a run committed before it begins.
    ///
    /// It is a *tilt*, never a lock. The deck still offers other lines — about
    /// nine offers in ten hold more than one — so a player who buys into Ordnance
    /// and draws a map with nowhere to hold can still turn.
    [Export] public GrowthLine Favours { get; set; } = GrowthLine.None;

    /// How hard it pulls. One piece at 1.0 is worth about one pick of that line.
    ///
    /// Deliberately the same currency as a pick, so a player can reason about it:
    /// wearing two Ward pieces means the run starts as though two Ward cards had
    /// already been taken. Anything much above 1.5 on a single piece makes the
    /// shop choice overwhelm the run's own choices, which is the opposite of the
    /// point.
    [Export] public float FavourStrength { get; set; } = 1.0f;

    [Export] public int OrbitUpgradeCap { get; set; } = -1;
    [Export] public int ShockwaveUpgradeCap { get; set; } = -1;
    [Export] public int ChainUpgradeCap { get; set; } = -1;
    [Export] public int ChillUpgradeCap { get; set; } = -1;

    [Export] public int PierceUpgradeCap { get; set; } = -1;
    [Export] public int CritUpgradeCap { get; set; } = -1;
    [Export] public int AreaUpgradeCap { get; set; } = -1;
    [Export] public int ThornsUpgradeCap { get; set; } = -1;
    [Export] public int RegenUpgradeCap { get; set; } = -1;
    [Export] public int KnockbackUpgradeCap { get; set; } = -1;
    [Export] public int DodgeUpgradeCap { get; set; } = -1;
    [Export] public int FortuneUpgradeCap { get; set; } = -1;
}
