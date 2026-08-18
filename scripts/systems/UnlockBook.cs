using Godot;

/// What each unlock hands over when it opens.
public enum UnlockKind
{
    /// A resource path the shop may now stock. It still has to be bought.
    ShopStock,

    /// A `GrowthOption` that may now appear in a level-up offer.
    Growth,
}

/// One thing the player does not have yet, and the sentence that says how to get
/// it.
///
/// Deliberately not priced. Credits already buy every number in this game, and a
/// second currency-shaped progression would be the same axis wearing a hat — the
/// point of an unlock is that it cannot be bought, only done.
public sealed class Unlock
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// Shown to the player, word for word, next to the locked entry.
    ///
    /// This text is the tutorial. "Extract using only the bow" tells a player
    /// that the bow exists, that a run can be finished with one weapon, and that
    /// extracting is something you can plan for — three things no menu was going
    /// to teach them.
    public required string Condition { get; init; }

    public required UnlockKind Kind { get; init; }

    /// A resource path for ShopStock, or the `GrowthOption` name for Growth.
    public required string Grants { get; init; }

    /// Whether the run that just ended satisfies the condition.
    ///
    /// Takes the profile as well as the record, because some conditions are about
    /// a career rather than an evening — and the profile has already folded this
    /// run in by the time this is asked, so a streak of three reads as three on
    /// the run that completed it.
    public required System.Func<RunRecord, Profile, bool> Met { get; init; }
}

/// The unlock table.
///
/// Every condition here describes a way of playing rather than a threshold to
/// grind. That is the whole design rule: "bank 300" would be satisfied by playing
/// more, and a player who reads it learns nothing except that the game wants
/// them to keep going. "Kill the boss" and "walk out having searched six crates"
/// each name a thing to try tonight.
///
/// The other rule is that nothing here is strictly better than what it replaces.
/// If every unlock were an upgrade, the table would be a numbers curve with
/// achievements painted on it, and the first two hours would be the part of the
/// game where the player does not have the good weapon yet.
public static class UnlockBook
{
    public static readonly Unlock[] All =
    {
        new()
        {
            Id = "bow",
            Name = "Hunting Bow",
            Condition = "Extract without firing a gun",
            Kind = UnlockKind.ShopStock,
            Grants = "res://resources/weapons/hunting_bow.tres",

            // Named for what it asks rather than what it counts. A player who
            // reads it will take the knife out on purpose, which is the first
            // time this game asks them to choose a way to play rather than the
            // biggest number they can afford.
            //
            // The kill floor is not decoration. Hits alone would also be zero on
            // a run that walked to the pad and left, which is technically without
            // firing a gun and is not what the sentence means.
            Met = (run, _) => run.Survived
                              && run.Kills >= 25
                              && run.HitsByCategory[(int)WeaponCategory.Firearm] == 0,
        },

        // Was the fire axe, and moving it was the single most useful thing the
        // probe found. With four of six weapons locked, and two of the remaining
        // four being the starting kit, a new profile walked into a shop where
        // every weapon was either already owned or unbuyable — a dead screen on
        // day one, and no sink at all for the credits the first run pays. The axe
        // is now on the shelf from the start and the condition kept its card.
        new()
        {
            Id = "ignite",
            Name = "Ignite",
            Condition = "Kill 60 in a single run",
            Kind = UnlockKind.Growth,
            Grants = nameof(GrowthOption.Ignite),
            Met = (run, _) => run.Kills >= 60,
        },

        new()
        {
            Id = "service_rifle",
            Name = "Service Rifle",
            Condition = "Extract three runs in a row",
            Kind = UnlockKind.ShopStock,
            Grants = "res://resources/weapons/service_rifle.tres",
            Met = (_, profile) => profile.Streak >= 3,
        },

        new()
        {
            Id = "scythe",
            Name = "Reaper Scythe",
            Condition = "Kill the boss and walk out",
            Kind = UnlockKind.ShopStock,
            Grants = "res://resources/weapons/reaper_scythe.tres",
            Met = (run, _) => run.Survived && run.BossesKilled > 0,
        },

        new()
        {
            Id = "detonate",
            Name = "Detonate",
            Condition = "Kill 8 with one thrown item",
            Kind = UnlockKind.Growth,
            Grants = nameof(GrowthOption.Detonate),

            // The only condition here about a single moment rather than a run.
            // It is also the only one a player can fail to notice they met, so
            // the announcement carrying the condition text back matters most here.
            Met = (run, _) => run.BestThrowKills >= 8,
        },

        new()
        {
            Id = "thorns",
            Name = "Thorns",
            Condition = "Survive a run that took you below 15 health",
            Kind = UnlockKind.Growth,
            Grants = nameof(GrowthOption.Thorns),
            Met = (run, _) => run.Survived && run.LowestHealth <= 15.0f,
        },

        new()
        {
            Id = "lifesteal",
            Name = "Lifesteal",
            Condition = "Search 6 crates in one run",
            Kind = UnlockKind.Growth,
            Grants = nameof(GrowthOption.Lifesteal),
            Met = (run, _) => run.CratesLooted >= 6,
        },

        new()
        {
            Id = "fortune",
            Name = "Fortune",
            Condition = "Extract with a multiplier of 2.5 or better",
            Kind = UnlockKind.Growth,
            Grants = nameof(GrowthOption.Fortune),
            Met = (run, _) => run.Survived && run.Multiplier >= 2.5f,
        },
    };

    public static Unlock? Find(string id)
    {
        foreach (Unlock unlock in All)
        {
            if (unlock.Id == id)
                return unlock;
        }

        return null;
    }

    /// Whether a shop entry is available to this profile. Anything the table does
    /// not mention is unconditionally available — the table is a list of things
    /// held back, not a whitelist, so adding a weapon without an unlock row makes
    /// it purchasable rather than invisible.
    public static bool ShopAllows(Profile profile, string path)
    {
        foreach (Unlock unlock in All)
        {
            if (unlock.Kind == UnlockKind.ShopStock && unlock.Grants == path)
                return profile.HasUnlocked(unlock.Id);
        }

        return true;
    }

    /// The condition text for a locked shop entry, or null if it is not locked.
    /// The base screen prints this in place of a price: a row the player can see
    /// and cannot buy has to say why, or it reads as a bug.
    public static string? ShopLockReason(Profile profile, string path)
    {
        foreach (Unlock unlock in All)
        {
            if (unlock.Kind == UnlockKind.ShopStock
                && unlock.Grants == path
                && !profile.HasUnlocked(unlock.Id))
            {
                return unlock.Condition;
            }
        }

        return null;
    }

    public static bool GrowthAllows(Profile profile, GrowthOption option)
    {
        string name = option.ToString();
        foreach (Unlock unlock in All)
        {
            if (unlock.Kind == UnlockKind.Growth && unlock.Grants == name)
                return profile.HasUnlocked(unlock.Id);
        }

        return true;
    }

    /// Everything the finished run just opened. Returns the unlocks rather than
    /// applying them, so the caller decides when the profile changes and the
    /// debrief has something to announce.
    public static System.Collections.Generic.List<Unlock> NewlyMet(RunRecord run, Profile profile)
    {
        var opened = new System.Collections.Generic.List<Unlock>();

        foreach (Unlock unlock in All)
        {
            if (!profile.HasUnlocked(unlock.Id) && unlock.Met(run, profile))
                opened.Add(unlock);
        }

        return opened;
    }
}
