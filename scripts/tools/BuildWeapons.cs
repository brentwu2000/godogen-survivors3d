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
                Favours = GrowthLine.Retinue,

                // The only Sidearm the table has until step 3 of WEAPONS.md, and
                // a shelf with one option on it is not a choice — recorded rather
                // than hidden, because the shop will show it as one.
                Slot = WeaponSlot.Sidearm,
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

            // Long melee: one heavy chop that shoves what it hits.
            //
            // It used to be "a wide sweep, slow, the answer to being surrounded",
            // which is the Reaper Scythe's job and the scythe did it better on
            // every number there is — more damage, faster, longer, a 160-degree
            // arc against 100, more knockback, twice the ceiling and half again
            // the cleave. Eight axes to nothing. A tier-1 weapon that a tier-2
            // weapon strictly replaces is not a cheap option, it is the part of
            // the game before the player has the real one.
            //
            // So the two melee weapons answer opposite questions now. The scythe
            // is the crowd: a wide arc and three quarters of its damage carrying
            // through to whatever stands behind. The axe is the *single heavy
            // thing* — the most damage per swing of any melee weapon and the
            // hardest shove in the game — which is the brute, the bulwark and
            // anything the player needs to get out of a doorway. Its cleave drops
            // to a quarter because a chop is not a sweep, and its arc to 70
            // degrees for the same reason.
            //
            // Slower than the scythe on purpose. 26 x 0.85 is 22 a second against
            // the scythe's 28.6, so the axe loses the damage race outright and
            // wins every exchange it picks — which is the trade, and it is
            // available for 250 credits on the first run.
            new()
            {
                WeaponName = "Fire Axe",
                Favours = GrowthLine.Ordnance,
                Trait = WeaponTrait.Cleave,
                TraitAmount = 0.25f,
                TraitCount = 0,
                Category = WeaponCategory.MeleeLong,
                Price = 250,
                BaseDamage = 26.0f,
                BaseAttackSpeed = 0.85f,
                BaseRange = 3.0f,
                SwingArcDegrees = 70.0f,
                MagazineSize = 0,
                Knockback = 0.95f,
            },

            // Bow: travel time and a pierce, so lining shots up along a lane
            // matters more than raw rate of fire.
            new()
            {
                WeaponName = "Hunting Bow",
                Favours = GrowthLine.Gunnery,
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
                Favours = GrowthLine.Gunnery,
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
            //
            // **That sentence was written first and the numbers did not follow
            // it.** This weapon shipped ahead of the scavenged rifle on damage,
            // rate, range, spread, reload, magazine, reserve, penetration,
            // knockback, burst tightness, burst count, ceiling and starting
            // bonus — thirteen for thirteen, for 1400 credits and no trade at
            // all. Every other decision the game asks about a build is made
            // downstream of the armoury, so one weapon that is simply correct
            // makes the deck's five lines, the biome that refuses a build and
            // the survivor chosen before the loadout all arguments about a
            // question already settled.
            //
            // It is the weapon that **never stops**: the largest magazine and
            // reserve in the game, the fastest reload, the tightest burst. It
            // pays for that in the two things the starting rifle keeps — a
            // heavier round and a longer reach — and in penetration, which
            // belongs to the marksman rifle and the bow and is on sale from the
            // bandolier for anyone who wants it here.
            //
            // Two costs, not three. The first attempt also opened the cone to 10
            // degrees, which at the six metres a target is usually engaged from
            // is a metre of lateral error against a 0.7 m body — about a third of
            // its shots on the floor, on top of a lighter round and a shorter
            // reach. That is not a trade, it is a downgrade with a bigger
            // magazine, and it showed up as the rifle firing for the whole of a
            // measurement window because its target kept surviving. The cone sits
            // level with the starting rifle's 9 degrees: this weapon is not more
            // accurate, and it is not less.
            //
            // Effective damage, magazine and reload together: 40 rounds at 7/s
            // is 5.7 s of fire against 1.8 s of reload, so 76% of the time is
            // spent shooting where the scavenged rifle manages 69%. 11 x 7.0 x
            // 0.76 is 58.5 against 12 x 6.0 x 0.69 = 50. Seventeen per cent more
            // damage over a minute, from a weapon that is worse in every single
            // exchange. That is what uptime is worth and it is the whole pitch.
            new()
            {
                WeaponName = "Service Rifle",
                Favours = GrowthLine.Gunnery,
                Trait = WeaponTrait.Burst,
                TraitAmount = 0.07f,
                TraitCount = 2,
                Category = WeaponCategory.Firearm,
                Tier = 2,
                Price = 1400,
                MaxLevel = 16,
                TierStartBonus = 2,
                BaseDamage = 11.0f,
                BaseAttackSpeed = 7.0f,
                BaseRange = 16.0f,
                BaseSpreadDegrees = 9.0f,
                BaseReloadTime = 1.8f,
                MagazineSize = 40,
                StartingReserve = 320,
                Penetration = 1,
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
                Favours = GrowthLine.Ordnance,
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
                Favours = GrowthLine.Gunnery,
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
                Favours = GrowthLine.Ordnance,
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
                Favours = GrowthLine.Ordnance,
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
