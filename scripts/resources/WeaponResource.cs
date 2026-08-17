using Godot;

public enum WeaponCategory
{
    MeleeShort,
    MeleeLong,
    BowCrossbow,
    Firearm,
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

    /// 0 means the weapon never reloads.
    [Export] public int MagazineSize { get; set; } = 30;

    /// How many enemies one shot passes through. 1 stops at the first.
    [Export] public int Penetration { get; set; } = 1;

    /// Metres per second for projectile weapons. Firearms are hitscan and ignore
    /// this — a rifle round crosses the whole arena inside one tick anyway.
    [Export] public float ProjectileSpeed { get; set; } = 24.0f;

    [Export] public float Knockback { get; set; }

    public bool IsMelee => Category is WeaponCategory.MeleeShort or WeaponCategory.MeleeLong;
    public bool IsProjectile => Category == WeaponCategory.BowCrossbow;
    public bool IsHitscan => Category == WeaponCategory.Firearm;

    /// Melee and bows grow their reach with practice; firearms do not — a rifle's
    /// range is the cartridge's, not the shooter's.
    public float GetEffectiveRange(int proficiency) => Category switch
    {
        WeaponCategory.MeleeShort or WeaponCategory.MeleeLong or WeaponCategory.BowCrossbow =>
            BaseRange * (1.0f + proficiency * 0.05f),
        _ => BaseRange,
    };

    /// Seconds between attacks.
    public float GetEffectiveAttackDelay(int proficiency)
    {
        float multiplier = Category switch
        {
            WeaponCategory.MeleeShort or WeaponCategory.MeleeLong or WeaponCategory.BowCrossbow =>
                1.0f + proficiency * 0.04f,
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
            BaseSpreadDegrees * (1.0f - proficiency * 0.08f));
    }

    public float GetEffectiveReloadTime(int proficiency) => Category == WeaponCategory.Firearm
        ? Mathf.Max(BaseReloadTime * 0.3f, BaseReloadTime * (1.0f - proficiency * 0.06f))
        : BaseReloadTime;

    /// Bows gain arrow velocity with practice; a faster arrow drops less and is
    /// easier to lead with.
    public float GetEffectiveProjectileSpeed(int proficiency) => Category == WeaponCategory.BowCrossbow
        ? ProjectileSpeed * (1.0f + proficiency * 0.03f)
        : ProjectileSpeed;
}
