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

    [Export] public float BaseReloadTime { get; set; } = 2.0f;

    /// 0 means the weapon never reloads — and, with it, never runs dry. Melee
    /// and bows are deliberately in that group: running out has to be a change
    /// of tactics, never a dead end.
    [Export] public int MagazineSize { get; set; } = 30;

    /// Rounds carried at the start of a run, outside the magazine. Looted ammo
    /// tops this up, which is the only reason a common lootable is worth keeping
    /// instead of selling.
    [Export] public int StartingReserve { get; set; }

    /// What the reserve can hold. A cap is what stops ammo from being a pure
    /// hoard — past it, rounds are only worth their sale price.
    [Export] public int MaxReserve { get; set; } = 300;

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
            BaseSpreadDegrees * 0.2f,
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
