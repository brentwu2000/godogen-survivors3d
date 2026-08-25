using Godot;

public enum WeaponCategory
{
    MeleeShort,
    MeleeLong,
    BowCrossbow,
    Firearm,
}

/// A weapon's signature behaviour — the thing that makes it that weapon rather
/// than a row of larger numbers.
///
/// Six weapons that differ only in damage, range and magazine size are six
/// difficulty settings for one weapon. The player's choice at the shop is
/// supposed to be "which way do I want to fight", and it cannot be while the
/// answer to every question is the same shot with a different number on it.
/// Which hand a weapon occupies, and therefore which of the two slots it can go
/// in.
///
/// **Both slots fire, so without this the correct loadout is the two highest
/// outputs in the shop** — which is the dominance `WeaponProbe` was given a stage
/// to prevent, arriving one level up. A Primary takes two hands and a Sidearm
/// one, so a pair is always one of each and "carry two heavies" is not a
/// sentence the loadout screen can express.
///
/// It replaces `IsMelee` as the thing that decided where a bought weapon went.
/// That was a proxy and it was wrong in exactly the case this design cares
/// about: a fire axe is melee and is not a sidearm.
public enum WeaponSlot
{
    /// Two hands. The build's damage and the decision.
    Primary,

    /// One hand. Small, always working, and there to cover what the primary
    /// cannot — a reload, an empty reserve, or something already touching you.
    Sidearm,
}

public enum WeaponTrait
{
    None,

    /// Fires TraitCount extra shots TraitAmount seconds apart. Turns a rifle
    /// from a metronome into something with a rhythm, and makes the reload
    /// matter — a burst empties a magazine faster than it looks like it should.
    Burst,

    /// Hits leave TraitAmount damage per second on the target for TraitCount
    /// seconds. Rewards touching many things once instead of one thing many
    /// times, which is exactly the opposite of what a knife's numbers suggest.
    Bleed,

    /// A projectile that jumps to another target TraitCount times after a hit.
    /// The bow's answer to a crowd, without giving it penetration — a bounce
    /// picks a *new* target, so it curves through a group rather than lining up.
    Ricochet,

    /// The swing also strikes everything within reach behind the swinger, at
    /// TraitAmount of the damage. Being surrounded stops being purely a problem.
    Cleave,

    /// TraitCount separate shots in one pull, each at TraitAmount of the damage
    /// and each rolling its own line inside the cone.
    ///
    /// Separate shots, not one shot that hits several times — which is the whole
    /// weapon. Each pellet rolls independently, so at range most of them miss and
    /// at contact all of them land, and the player learns a distance rather than
    /// a number.
    Spread,

    /// Waiting TraitCount seconds without firing multiplies the next shot by
    /// TraitAmount.
    ///
    /// The only weapon in the game that rewards *not* attacking. Everything else
    /// wants the trigger held, and a horde game where the correct input is always
    /// the same input has one weapon with several skins.
    Charge,

    /// The projectile detonates for TraitAmount metres where it connects.
    ///
    /// It stops there whatever its penetration says: a bolt that punched through
    /// and detonated at the end of its flight would put the blast behind the
    /// crowd, which is both wrong and impossible to aim.
    Blast,
}

/// Weapon stats as data, so balance lives in .tres files rather than in code.
///
/// Proficiency is applied here rather than at the call site: every consumer then
/// asks the same question ("what is my effective spread") and cannot forget to
/// apply the bonus, which is how the melee and ranged paths drift apart.
[GlobalClass]
public partial class WeaponResource : Resource
{
    [Export] public string WeaponName { get; set; } = "";
    [Export] public WeaponCategory Category { get; set; } = WeaponCategory.Firearm;

    /// Which of the two slots this can be carried in. See `WeaponSlot`.
    [Export] public WeaponSlot Slot { get; set; } = WeaponSlot.Primary;

    /// Which growth line carrying this pulls the deck toward, or None.
    ///
    /// **Gear has done this since H4a and a weapon is the larger commitment of
    /// the two.** `RunGrowth.FavourLine` makes a line's cards likelier to be
    /// offered by exactly the amount one pick of that line would, so a loadout
    /// stops being a stat block and becomes the first two picks of a build.
    ///
    /// Named for what the weapon *does*, never for what would be convenient to
    /// balance: a shotgun's cone is one hit becoming several, which is
    /// Ordnance's definition, and a knife's bleed is damage that happens without
    /// you, which is Retinue's. A lean the player cannot read off the weapon is
    /// a lean that reads as the deck being unfair.
    [Export] public GrowthLine Favours { get; set; } = GrowthLine.None;

    /// How hard it pulls, before the slot's own halving. One is worth about one
    /// pick of that line — deliberately the same currency `GearResource` uses, so
    /// a player can add a loadout up without a second mental model.
    [Export] public float FavourStrength { get; set; } = 1.0f;

    [Export] public float BaseDamage { get; set; } = 10.0f;

    /// Attacks per second.
    [Export] public float BaseAttackSpeed { get; set; } = 1.0f;

    /// Melee reach, or maximum effective range for ranged weapons.
    [Export] public float BaseRange { get; set; } = 5.0f;

    /// Total swept angle of the melee swing, in degrees — 360 is an all-round
    /// sweep. Ignored by ranged weapons.
    [Export] public float SwingArcDegrees { get; set; } = 60.0f;

    /// Cone half-angle in degrees at zero proficiency. Firearms only.
    [Export] public float BaseSpreadDegrees { get; set; }

    /// The tightest the cone ever gets, as a fraction of `BaseSpreadDegrees`.
    ///
    /// A fifth for a rifle, where practice should make the shot nearly exact.
    /// Much higher for a shotgun: eight pellets inside four degrees is a slug,
    /// and a practised shotgun that quietly stops being a shotgun is a weapon
    /// whose identity was a bug in a shared formula.
    [Export] public float SpreadFloorFraction { get; set; } = 0.2f;

    [Export] public float BaseReloadTime { get; set; } = 2.0f;

    /// 0 means the weapon never reloads — and, with it, never runs dry. Melee
    /// and bows are deliberately in that group: running out has to be a change
    /// of tactics, never a dead end.
    [Export] public int MagazineSize { get; set; } = 30;

    /// Rounds carried at the start of a run, outside the magazine. Looted ammo
    /// tops this up, which is the only reason a common lootable is worth keeping
    /// instead of selling.
    [Export] public int StartingReserve { get; set; }

    /// What the reserve can hold, or **0 for no limit**.
    ///
    /// It was a hard cap on every weapon, and the reasoning was that a cap stops
    /// ammo being a pure hoard: past it, rounds are only worth their sale price.
    /// That is a real trade and it is the wrong one to force on the player — what
    /// it actually produced was a rifle at 240 of 240 walking past ammunition it
    /// could not pick up, which reads as the game refusing loot rather than as an
    /// economy.
    ///
    /// The interesting decision was always "is this round worth a slot in the
    /// bag", and that decision is made by `CarryCapacity`, which is unchanged.
    /// Ammo still competes for space with everything else worth carrying out; it
    /// simply no longer stops being takeable.
    ///
    /// Kept as a field rather than deleted so a weapon can still declare one — a
    /// launcher that could stockpile forty charges would be a different game — and
    /// zero is the sentinel because "no maximum" has to be expressible.
    [Export] public int MaxReserve { get; set; }

    /// Whether this weapon caps what it can carry.
    public bool CapsReserve => MaxReserve > 0;

    /// `rounds` clamped to whatever this weapon will hold.
    public int FitReserve(int rounds) => CapsReserve ? Mathf.Min(rounds, MaxReserve) : rounds;

    /// How many enemies one shot passes through. 1 stops at the first.
    [Export] public int Penetration { get; set; } = 1;

    /// Metres per second for projectile weapons. Firearms are hitscan and ignore
    /// this — a rifle round crosses the whole arena inside one tick anyway.
    [Export] public float ProjectileSpeed { get; set; } = 24.0f;

    [Export] public float Knockback { get; set; }

    /// The weapon's signature. What TraitAmount and TraitCount mean depends on
    /// it — see WeaponTrait — because one pair of numbers shared by every trait
    /// is far less to carry than a field per behaviour that only one weapon has.
    [Export] public WeaponTrait Trait { get; set; } = WeaponTrait.None;
    [Export] public float TraitAmount { get; set; }
    [Export] public int TraitCount { get; set; }

    /// Shop tier. 1 is starting kit — owned from the first run, never lost.
    [Export] public int Tier { get; set; } = 1;

    /// Credits to buy one. Zero means it is not for sale.
    [Export] public int Price { get; set; }

    /// The ceiling. Every curve below stops here, and so does in-run growth —
    /// this is what a better weapon actually buys: a longer climb, not a bigger
    /// number bolted on the end.
    [Export] public int MaxLevel { get; set; } = 8;

    /// Levels the weapon itself is worth on arrival, before practice. Gear moves
    /// the starting point; practice moves it too, but only halfway (see
    /// WeaponHandler.StartLevel) so there is always a climb left to make.
    [Export] public int TierStartBonus { get; set; }

    public bool IsMelee => Category is WeaponCategory.MeleeShort or WeaponCategory.MeleeLong;
    public bool IsProjectile => Category == WeaponCategory.BowCrossbow;
    public bool IsHitscan => Category == WeaponCategory.Firearm;

    /// Every curve below is read at a level clamped to MaxLevel, so a ceiling is
    /// one rule applied in one place rather than five caps that drift apart.
    public int ClampLevel(int level) => Mathf.Clamp(level, 0, MaxLevel);

    /// Damage grows with level, unlike everything else here, which grows reach
    /// and rate. Without it a weapon gets faster and further forever while each
    /// hit stays exactly as hard — the one axis a growth system cannot leave
    /// flat, because it is the one the player is actually watching.
    public float GetEffectiveDamage(int proficiency) =>
        BaseDamage * (1.0f + ClampLevel(proficiency) * 0.06f);

    /// Melee and bows grow their reach with practice; firearms do not — a rifle's
    /// range is the cartridge's, not the shooter's.
    public float GetEffectiveRange(int proficiency) => Category switch
    {
        WeaponCategory.MeleeShort or WeaponCategory.MeleeLong or WeaponCategory.BowCrossbow =>
            BaseRange * (1.0f + ClampLevel(proficiency) * 0.05f),
        _ => BaseRange,
    };

    /// Seconds between attacks.
    public float GetEffectiveAttackDelay(int proficiency)
    {
        float multiplier = Category switch
        {
            WeaponCategory.MeleeShort or WeaponCategory.MeleeLong or WeaponCategory.BowCrossbow =>
                1.0f + ClampLevel(proficiency) * 0.04f,
            _ => 1.0f,
        };
        return 1.0f / Mathf.Max(0.01f, BaseAttackSpeed * multiplier);
    }

    /// Firearms tighten with practice, down to a floor of 20% of base — practice
    /// should not turn a shotgun into a laser.
    public float GetEffectiveSpreadDegrees(int proficiency)
    {
        if (Category != WeaponCategory.Firearm)
            return 0.0f;

        return Mathf.Max(
            BaseSpreadDegrees * SpreadFloorFraction,
            BaseSpreadDegrees * (1.0f - ClampLevel(proficiency) * 0.08f));
    }

    public float GetEffectiveReloadTime(int proficiency) => Category == WeaponCategory.Firearm
        ? Mathf.Max(BaseReloadTime * 0.3f, BaseReloadTime * (1.0f - ClampLevel(proficiency) * 0.06f))
        : BaseReloadTime;

    /// Bows gain arrow velocity with practice; a faster arrow drops less and is
    /// easier to lead with.
    public float GetEffectiveProjectileSpeed(int proficiency) => Category == WeaponCategory.BowCrossbow
        ? ProjectileSpeed * (1.0f + ClampLevel(proficiency) * 0.03f)
        : ProjectileSpeed;
}
