using Godot;

/// Writes the starting weapon set to resources/weapons/*.tres.
///
///   godot --headless --script scripts/tools/BuildWeapons.cs
///
/// Balance lives in the .tres files once written; this only seeds them. Re-run
/// it to reset a weapon to its designed baseline.
public partial class BuildWeapons : SceneTree
{
    private const string OutputDir = "res://resources/weapons";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        WeaponResource[] weapons =
        {
            // Short melee: small reach, fast, no knockback. Proficiency widens the
            // arc and shortens the gap, which is where it becomes a horde weapon.
            new()
            {
                WeaponName = "Combat Knife",
                Category = WeaponCategory.MeleeShort,
                BaseDamage = 6.0f,
                BaseAttackSpeed = 3.2f,
                BaseRange = 1.6f,
                SwingArcDegrees = 45.0f,
                MagazineSize = 0,
                Knockback = 0.05f,
            },

            // Long melee: wide sweep, slow, knocks the front rank back — the
            // answer to being surrounded rather than to a single target.
            new()
            {
                WeaponName = "Fire Axe",
                Category = WeaponCategory.MeleeLong,
                Price = 250,
                BaseDamage = 16.0f,
                BaseAttackSpeed = 1.1f,
                BaseRange = 3.0f,
                SwingArcDegrees = 100.0f,
                MagazineSize = 0,
                Knockback = 0.55f,
            },

            // Bow: travel time and a pierce, so lining shots up along a lane
            // matters more than raw rate of fire.
            new()
            {
                WeaponName = "Hunting Bow",
                Category = WeaponCategory.BowCrossbow,
                Price = 400,
                BaseDamage = 22.0f,
                BaseAttackSpeed = 1.0f,
                BaseRange = 14.0f,
                BaseReloadTime = 0.0f,
                MagazineSize = 0,
                Penetration = 2,
                ProjectileSpeed = 26.0f,
                Knockback = 0.1f,
            },

            // Firearm: hitscan, fast, inaccurate until practised, and the only
            // category that has to stop and reload.
            new()
            {
                WeaponName = "Scavenged Rifle",
                Category = WeaponCategory.Firearm,
                BaseDamage = 12.0f,
                BaseAttackSpeed = 6.0f,
                BaseRange = 18.0f,
                BaseSpreadDegrees = 9.0f,
                BaseReloadTime = 2.2f,
                MagazineSize = 30,

                // Eight magazines in, which at the rate this fires is a couple
                // of minutes of shooting. Looted rounds are what carry a long
                // run past that, and the cap is what keeps them from becoming a
                // hoard rather than a decision.
                StartingReserve = 240,
                MaxReserve = 360,
                Penetration = 1,
                Knockback = 0.08f,
            },

            // Tier 2. What credits buy is not a bigger number but a longer
            // curve: a ceiling of 16 against the scavenged rifle's 8, which also
            // means twice as much of the player's practice finally counts.
            new()
            {
                WeaponName = "Service Rifle",
                Category = WeaponCategory.Firearm,
                Tier = 2,
                Price = 1400,
                MaxLevel = 16,
                TierStartBonus = 2,
                BaseDamage = 15.0f,
                BaseAttackSpeed = 7.0f,
                BaseRange = 20.0f,
                BaseSpreadDegrees = 6.0f,
                BaseReloadTime = 1.8f,
                MagazineSize = 40,
                StartingReserve = 320,
                MaxReserve = 480,
                Penetration = 2,
                Knockback = 0.1f,
            },

            // The answer to being surrounded, bought rather than found. Wider
            // sweep than the axe and a ceiling to match, but no reach until the
            // practice is there — a long weapon is a promise, not a gift.
            new()
            {
                WeaponName = "Reaper Scythe",
                Category = WeaponCategory.MeleeLong,
                Tier = 2,
                Price = 1100,
                MaxLevel = 16,
                TierStartBonus = 1,
                BaseDamage = 22.0f,
                BaseAttackSpeed = 1.3f,
                BaseRange = 3.4f,
                SwingArcDegrees = 160.0f,
                MagazineSize = 0,
                Knockback = 0.7f,
            },
        };

        foreach (WeaponResource weapon in weapons)
        {
            string path = $"{OutputDir}/{weapon.WeaponName.ToLower().Replace(' ', '_')}.tres";
            Error err = ResourceSaver.Save(weapon, path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }
            GD.Print($"Saved {path}");
        }

        return true;
    }
}
