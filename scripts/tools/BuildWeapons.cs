using Godot;

/// Writes the starting weapon set to resources/weapons/*.tres.
///
/// Every weapon carries a trait as well as its numbers. Six weapons separated
/// only by damage, range and magazine size are six difficulty settings for one
/// weapon: the choice at the shop is meant to be "which way do I want to fight",
/// and it cannot be while the answer to every question is the same shot with a
/// bigger number on it.
///
///   knife    bleeds     — rewards touching many things once, which is the exact
///                         opposite of what its damage number suggests
///   axe      cleaves     — hits behind at half damage; being surrounded stops
///                         being purely a problem
///   scythe   cleaves     — the same, at three quarters, which is what the tier
///                         is buying rather than a larger number
///   bow      ricochets   — one jump to a *new* target, so it curves through a
///                         group instead of needing them lined up
///   rifles   burst       — extra shots on a short delay; the magazine empties
///                         faster than it looks like it should, and that is the
///                         cost the trait is paid for
///
///   godot --headless --script scripts/tools/BuildWeapons.cs
///
/// Balance lives in the .tres files once written; this only seeds them. Re-run
/// it to reset a weapon to its designed baseline.
public partial class BuildWeapons : SceneTree
{
    // No `MaxReserve` on any weapon, and its absence is deliberate.
    //
    // Every one of them carried a cap — 360 on the scavenged rifle, 90 on the
    // bolt launcher — on the reasoning that a cap stops ammunition being a pure
    // hoard. What it produced was a player at 240 of 240 walking past rounds they
    // could not pick up, which reads as the game refusing loot rather than as an
    // economy.
    //
    // The decision worth keeping is "is this round worth a slot in the bag", and
    // that one is made by `CarryCapacity` and is untouched. Zero means no limit;
    // the field is still there for a weapon that genuinely needs one.

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
                Trait = WeaponTrait.Bleed,
                TraitAmount = 4.0f,
                TraitCount = 3,
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
                Trait = WeaponTrait.Cleave,
                TraitAmount = 0.5f,
                TraitCount = 0,
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
                Trait = WeaponTrait.Ricochet,
                TraitAmount = 0.0f,
                TraitCount = 1,
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
                Trait = WeaponTrait.Burst,
                TraitAmount = 0.09f,
                TraitCount = 1,
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
                Penetration = 1,
                Knockback = 0.08f,
            },

            // Tier 2. What credits buy is not a bigger number but a longer
            // curve: a ceiling of 16 against the scavenged rifle's 8, which also
            // means twice as much of the player's practice finally counts.
            new()
            {
                WeaponName = "Service Rifle",
                Trait = WeaponTrait.Burst,
                TraitAmount = 0.07f,
                TraitCount = 2,
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
                Penetration = 2,
                Knockback = 0.1f,
            },

            // Eight pellets, each rolling its own line. The only weapon whose
            // damage is a *distance* rather than a number: at fifteen metres most
            // of the cone goes past, at three every pellet lands, so the player
            // learns a range instead of a stat.
            //
            // `SpreadFloorFraction` is 0.8 rather than the usual 0.2. Practice
            // tightens every other firearm toward a point, and a practised
            // shotgun that quietly became a slug would be a weapon losing its
            // identity to a formula it shares with the rifles.
            new()
            {
                WeaponName = "Pump Shotgun",
                Trait = WeaponTrait.Spread,
                TraitAmount = 0.34f,
                TraitCount = 8,
                Category = WeaponCategory.Firearm,
                Tier = 2,
                Price = 1300,
                MaxLevel = 12,
                TierStartBonus = 1,
                BaseDamage = 16.0f,
                BaseAttackSpeed = 1.4f,
                BaseRange = 13.0f,
                BaseSpreadDegrees = 20.0f,
                SpreadFloorFraction = 0.8f,
                BaseReloadTime = 2.6f,
                MagazineSize = 6,
                StartingReserve = 60,
                Penetration = 1,
                Knockback = 0.9f,
            },

            // Three seconds of not shooting for three and a half times the
            // damage. The only thing in the game that pays for restraint —
            // everything else wants the trigger held, and a horde game where the
            // right input is always the same input has one weapon with skins.
            //
            // The charge ticks while holstered, which is the build it is really
            // for: carry it as a sidearm, fight with the other weapon, swap in
            // for the shot that has to land.
            new()
            {
                WeaponName = "Marksman Rifle",
                Trait = WeaponTrait.Charge,
                TraitAmount = 3.5f,
                TraitCount = 3,
                Category = WeaponCategory.Firearm,
                Tier = 3,
                Price = 2200,
                MaxLevel = 14,
                TierStartBonus = 2,
                BaseDamage = 34.0f,
                BaseAttackSpeed = 0.9f,
                BaseRange = 30.0f,
                BaseSpreadDegrees = 2.0f,
                BaseReloadTime = 2.4f,
                MagazineSize = 8,
                StartingReserve = 72,
                Penetration = 3,
                Knockback = 0.4f,
            },

            // A bolt that detonates where it connects. Direct hit *and* splash,
            // so it is not strictly worse than a rifle against one target — and
            // the blast hurts the player inside it, which is the range the weapon
            // asks them to learn.
            new()
            {
                WeaponName = "Bolt Launcher",
                Trait = WeaponTrait.Blast,
                TraitAmount = 4.0f,
                TraitCount = 0,
                Category = WeaponCategory.BowCrossbow,
                Tier = 3,
                Price = 2000,
                MaxLevel = 12,
                TierStartBonus = 1,
                BaseDamage = 26.0f,
                BaseAttackSpeed = 1.1f,
                BaseRange = 24.0f,
                BaseReloadTime = 2.2f,
                MagazineSize = 5,
                StartingReserve = 40,
                ProjectileSpeed = 19.0f,
                Penetration = 1,
                Knockback = 0.6f,
            },

            // The answer to being surrounded, bought rather than found. Wider
            // sweep than the axe and a ceiling to match, but no reach until the
            // practice is there — a long weapon is a promise, not a gift.
            new()
            {
                WeaponName = "Reaper Scythe",
                Trait = WeaponTrait.Cleave,
                TraitAmount = 0.75f,
                TraitCount = 0,
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
