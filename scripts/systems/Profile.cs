using Godot;
using Godot.Collections;

/// Everything that survives a run: credits, the stash, and practice.
///
/// Stored as plain JSON rather than a Godot Resource so a corrupted or
/// hand-edited file fails as a parse error that can be reported, instead of as a
/// deserialised object with silently wrong fields.
public sealed class Profile
{
    /// Bumped when a field cannot be defaulted into an older file. Adding an
    /// optional key does not need it — every field here is read with a fallback,
    /// so v1 files kept loading when the sidearm slot appeared. Owned equipment
    /// does need it: a v1 file has no record of what the player bought, and
    /// guessing "nothing" would take away gear they paid for.
    private const int Version = 2;

    public int Credits { get; set; }

    /// Item name to count. Names rather than resource paths, so moving a .tres
    /// does not orphan a player's stash.
    public Dictionary<string, int> Stash { get; } = new();

    /// Indexed by WeaponCategory.
    public int[] Proficiency { get; } = new int[4];

    public string LoadoutWeapon { get; set; } = "res://resources/weapons/scavenged_rifle.tres";

    /// The sidearm. A new key rather than a new version: every field here is
    /// read with a default, so a file written before this existed still loads
    /// and simply arrives with the knife.
    public string LoadoutSecondary { get; set; } = "res://resources/weapons/combat_knife.tres";

    /// Resource paths the player owns. Bought once; lost by dying in it, unless
    /// it is starting kit, which is the shirt on their back and cannot be taken.
    public Array<string> Owned { get; } = new();

    /// Curiosities that have ever reached the stash, by name.
    ///
    /// Written at the door, when the run's takings are handed over — not at the
    /// locker when they are sold. Selling a curiosity is the ordinary way of
    /// turning loot into credits and must not quietly forfeit the set; a
    /// collection that punished that would be a trap dressed as content, and the
    /// player would find out two hours in.
    ///
    /// Names rather than paths, like the stash, so moving a `.tres` does not
    /// orphan somebody's collection.
    public Array<string> Collected { get; } = new();

    /// Sets whose bounty has already been paid, so it is paid once.
    public Array<string> ClaimedSets { get; } = new();

    /// Equipped gear by slot: armour, backpack, boots. An empty entry falls back
    /// to the starting piece, so a player can never arrive at a run with no
    /// backpack because the last one was lost.
    /// One entry per `GearSlot`, indexed by it.
    ///
    /// Grown from three to four with **no save version bump**. The reader stops
    /// at whichever of the file and the array is shorter, so a three-entry save
    /// loads with an empty trinket slot and everything else where it was — which
    /// is exactly right, because a player who has never owned a trinket has an
    /// empty trinket slot.
    public string[] EquippedGear { get; } = new string[System.Enum.GetValues<GearSlot>().Length];

    public int RunsSurvived { get; set; }
    public int RunsLost { get; set; }

    /// Personal bests. Deliberately not a fourth growth curve: this game already
    /// has three (practice, gear, in-run upgrades) and a fourth would make it
    /// impossible to tell which one is moving — the exact problem that made
    /// practice a once-per-run settlement in Phase 8. A record changes no number
    /// in the next run. It is only a target, and a target is what was missing.
    public int BestBank { get; set; }
    public int BestKills { get; set; }
    public float BestSeconds { get; set; }
    public float BestMultiplier { get; set; }

    /// Consecutive extractions. The one record with teeth: a single death takes
    /// it back to nothing, which stacks another layer onto "do I take the good
    /// rifle out" without inventing any new mechanic to do it.
    public int Streak { get; set; }
    public int BestStreak { get; set; }

    /// The record book: things the run already measured and nothing kept.
    ///
    /// `RunRecord` has carried these since the debrief was built and every one of
    /// them was read once, printed, and thrown away. A number a player can go
    /// beat is worth more than the same number shown once — and unlike the four
    /// above, these describe *how* a run went rather than how big it was, which
    /// is what makes them targets for different kinds of play rather than one
    /// leaderboard with four columns.
    public int MostCrates { get; set; }
    public int BestThrow { get; set; }
    public int BestBossKills { get; set; }

    /// The lowest health a survived run ever came back from. Starts at the
    /// sentinel because zero would read as a perfect score forever.
    public float NarrowestEscape { get; set; } = float.MaxValue;

    /// The quickest extraction, which is a different skill from the longest.
    public float FastestExtraction { get; set; } = float.MaxValue;

    public bool HasNarrowEscape => NarrowestEscape < float.MaxValue;
    public bool HasFastExtraction => FastestExtraction < float.MaxValue;

    /// Which three jobs are on the board. Stored as the seed they were rolled
    /// from rather than as three objects — one integer, no schema to migrate the
    /// next time a contract kind is added, and the same three cards come back
    /// after a restart, so quitting to the menu is not a free reroll.
    public int ContractSeed { get; set; }

    /// Index into the rolled offer, or -1 for "took none". Taking one is the
    /// commitment; without it the offer would be three things that happen to pay
    /// out rather than a decision made before leaving.
    public int ContractIndex { get; set; } = -1;

    public bool HasContract => ContractIndex >= 0;

    /// Unlock ids the player has opened. Ids rather than resource paths, because
    /// an unlock is allowed to grant something that is not a file — a growth
    /// option is an enum case — and one list that can hold both is one list.
    public Array<string> Unlocked { get; } = new();

    /// Bosses killed across every run. Not a personal best and not a record: it
    /// is the only fact an unlock condition needs that no run in progress can
    /// re-derive, since a profile written before the boss existed cannot say
    /// whether one was ever fought.
    public int BossesKilled { get; set; }

    /// Where the next run goes, as an index into `BiomeBook.All`. Stored rather
    /// than asked at launch, because terrain has to be known while the player is
    /// buying equipment — a choice made after the shop is a choice the loadout
    /// could not have been built for, which is the entire reason it exists.
    public int Biome { get; set; }

    /// The survivor last chosen. Zero is the Drifter, which every profile has.
    public int Character { get; set; }

    /// Whether this player has ever been shown the base screen.
    ///
    /// A brand-new player opening the game meets a shop with fifteen rows, three
    /// terrains, eight unlock conditions and a contract board — none of which
    /// means anything until they have played once. So the first launch goes
    /// straight into a run and the base is what they come back to, with a result
    /// in hand and every one of those numbers now describing something they did.
    ///
    /// A stored flag rather than "are the run counts zero", because zero counts
    /// are also what a probe writes when it wants a clean profile to drive the
    /// returning-player loop with.
    public bool HasSeenBase { get; set; }

    /// Daily results, date key to score. One entry per day, written once.
    ///
    /// A dictionary rather than "today's score and a best": the point of a daily
    /// is the row of dates, and a single slot cannot say you have played eleven
    /// days running.
    public Dictionary<string, int> Daily { get; } = new();

    /// Whether today's attempt has been spent. This is the one rule that makes a
    /// daily a daily — without it, it is an ordinary run on a fixed seed, and a
    /// player who does not like their result simply plays it again until they do.
    public bool DailyDone(string dateKey) => Daily.ContainsKey(dateKey);

    /// Records the attempt whether it was any good or not, including a zero for
    /// dying. Refusing to record a bad run would make dying the way to keep the
    /// attempt, which is the opposite of every other rule in this game.
    public void RecordDaily(string dateKey, int score)
    {
        if (!Daily.ContainsKey(dateKey))
            Daily[dateKey] = score;
    }

    /// Consecutive days played up to and including the given one. The number a
    /// player is actually protecting once they have a few.
    public int DailyStreak(string todayKey)
    {
        if (!Daily.ContainsKey(todayKey))
            return 0;

        int streak = 0;
        string key = todayKey;

        while (Daily.ContainsKey(key))
        {
            streak++;
            key = PreviousDay(key);
        }

        return streak;
    }

    /// Date arithmetic on the key format, without a calendar library.
    ///
    /// Godot's Time helpers round-trip a Unix timestamp, which is exactly what is
    /// wanted here: subtracting a day in seconds is correct across month ends and
    /// leap years, and doing it by hand is not.
    private static string PreviousDay(string key)
    {
        string[] parts = key.Split('-');
        if (parts.Length != 3)
            return "";

        var dict = new Dictionary
        {
            { "year", parts[0].ToInt() },
            { "month", parts[1].ToInt() },
            { "day", parts[2].ToInt() },
            { "hour", 12 },
            { "minute", 0 },
            { "second", 0 },
        };

        long stamp = Time.GetUnixTimeFromDatetimeDict(dict) - 86400;
        Dictionary then = Time.GetDatetimeDictFromUnixTime(stamp);
        return $"{then["year"]}-{(int)then["month"]:00}-{(int)then["day"]:00}";
    }

    public bool HasUnlocked(string id) => Unlocked.Contains(id);

    /// Returns whether this actually opened something. The caller announces on
    /// true, so a second call for an unlock already held has to be silent.
    public bool Open(string id)
    {
        if (HasUnlocked(id))
            return false;

        Unlocked.Add(id);
        return true;
    }

    /// The kit a profile starts with and never loses. Everything else in the
    /// shop is a wager: it comes back only if the player does.
    public static readonly string[] StartingKit =
    {
        "res://resources/weapons/scavenged_rifle.tres",
        "res://resources/weapons/combat_knife.tres",
        "res://resources/gear/worn_jacket.tres",
        "res://resources/gear/canvas_pack.tres",
        "res://resources/gear/scuffed_boots.tres",
    };

    public Profile()
    {
        foreach (string path in StartingKit)
            Owned.Add(path);

        EquippedGear[0] = "res://resources/gear/worn_jacket.tres";
        EquippedGear[1] = "res://resources/gear/canvas_pack.tres";
        EquippedGear[2] = "res://resources/gear/scuffed_boots.tres";

        // Non-zero, so a fresh profile has a board to look at rather than the
        // degenerate seed the roller has to guard against anyway.
        ContractSeed = 1;
    }

    /// Which personal bests a run beat. Returned rather than printed, because the
    /// only moment this matters is the debrief and only it knows how to say so.
    public readonly struct RecordsBeaten
    {
        public bool Bank { get; init; }
        public bool Kills { get; init; }
        public bool Seconds { get; init; }
        public bool Multiplier { get; init; }
        public bool Streak { get; init; }

        public bool Any => Bank || Kills || Seconds || Multiplier || Streak;
    }

    /// Folds a finished run into the records, and reports what it beat.
    ///
    /// Only an extraction counts. A run that ended on the floor was longer and
    /// killed more than most successful ones — rewarding that with a record would
    /// pay for exactly the behaviour the whole loop is built to discourage.
    public RecordsBeaten ApplyRecords(RunRecord run)
    {
        if (!run.Survived)
        {
            Streak = 0;
            return default;
        }

        var beaten = new RecordsBeaten
        {
            Bank = run.Banked > BestBank,
            Kills = run.Kills > BestKills,
            Seconds = run.Seconds > BestSeconds,
            Multiplier = run.Multiplier > BestMultiplier,
            Streak = Streak + 1 > BestStreak,
        };

        BestBank = Mathf.Max(BestBank, run.Banked);
        BestKills = Mathf.Max(BestKills, run.Kills);
        BestSeconds = Mathf.Max(BestSeconds, run.Seconds);
        BestMultiplier = Mathf.Max(BestMultiplier, run.Multiplier);

        // The record book. Not reported in RecordsBeaten: five "you beat a
        // record" lines on one debrief is five lines nobody reads, and these are
        // targets to go and find rather than news.
        MostCrates = Mathf.Max(MostCrates, run.CratesLooted);
        BestThrow = Mathf.Max(BestThrow, run.BestThrowKills);
        BestBossKills = Mathf.Max(BestBossKills, run.BossesKilled);
        NarrowestEscape = Mathf.Min(NarrowestEscape, run.LowestHealth);
        FastestExtraction = Mathf.Min(FastestExtraction, run.Seconds);

        Streak++;
        BestStreak = Mathf.Max(BestStreak, Streak);
        return beaten;
    }

    /// Opens whatever this profile's existing records already prove, for a save
    /// that predates unlocks. Only the conditions a stored best can answer — the
    /// rest stay shut, because inventing a "yes" for a condition nothing was ever
    /// measuring is worse than leaving one thing to go and earn.
    public void GrantEarnedUnlocks()
    {
        if (BestKills >= 60)
            Open("ignite");

        if (BestStreak >= 3)
            Open("service_rifle");

        if (BestMultiplier >= 2.5f)
            Open("fortune");

        if (BossesKilled > 0)
            Open("scythe");
    }

    /// The jobs currently on the board.
    public Contract[] ContractOffer() => ContractBook.Roll((ulong)(uint)ContractSeed);

    public Contract? AcceptedContract
    {
        get
        {
            Contract[] offer = ContractOffer();
            return ContractIndex >= 0 && ContractIndex < offer.Length ? offer[ContractIndex] : null;
        }
    }

    /// Puts three new jobs on the board and forgets whichever was taken.
    ///
    /// The seed advances by the same xorshift everything else in this project
    /// uses, so a profile's sequence of offers is reproducible from wherever it
    /// started — which is what lets a probe pin an offer instead of retrying
    /// until the card it wants shows up.
    public void RollContracts()
    {
        ulong state = (ulong)(uint)ContractSeed | 0x9E3779B97F4A7C15UL;
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;

        ContractSeed = (int)(uint)(state >> 24);
        ContractIndex = -1;
    }

    public bool Owns(string path) => Owned.Contains(path);

    public static bool IsStartingKit(string path) => System.Array.IndexOf(StartingKit, path) >= 0;

    public void Grant(string path)
    {
        if (!Owns(path))
            Owned.Add(path);
    }

    /// Returns whether anything was actually taken. Starting kit never is.
    ///
    /// Anything equipped falls back to the starting kit for its slot. Without
    /// this a sold piece stays equipped: the profile carries a path it no longer
    /// owns, the run loads it anyway, and the player has sold something and kept
    /// wearing it. Selling and losing a run are the two ways a profile can stop
    /// owning something, and only one of them used to have to think about it.
    public bool Revoke(string path)
    {
        if (IsStartingKit(path) || !Owns(path))
            return false;

        Owned.Remove(path);
        Unequip(path);
        return true;
    }

    /// Puts the starting kit back in whichever slot held `path`.
    public void Unequip(string path)
    {
        for (int slot = 0; slot < EquippedGear.Length; slot++)
        {
            if (EquippedGear[slot] == path)
                EquippedGear[slot] = StartingGearFor(slot);
        }

        if (LoadoutWeapon == path)
            LoadoutWeapon = "res://resources/weapons/scavenged_rifle.tres";

        if (LoadoutSecondary == path)
            LoadoutSecondary = "res://resources/weapons/combat_knife.tres";
    }

    /// The starting piece for a gear slot, or empty for a slot the starting kit
    /// does not fill — a sold trinket leaves the slot bare, which is correct:
    /// there was nothing there before it was bought.
    private static string StartingGearFor(int slot) => slot switch
    {
        0 => "res://resources/gear/worn_jacket.tres",
        1 => "res://resources/gear/canvas_pack.tres",
        2 => "res://resources/gear/scuffed_boots.tres",
        _ => "",
    };

    /// Notes that a curiosity has been seen. Harmless for anything else.
    public void Record(string itemName)
    {
        if (CollectionBook.SetOf(itemName) < 0 || Collected.Contains(itemName))
            return;

        Collected.Add(itemName);
    }

    public void AddToStash(string itemName, int count)
    {
        if (count <= 0)
            return;

        Stash[itemName] = Stash.TryGetValue(itemName, out int existing) ? existing + count : count;
    }

    public string ToJson()
    {
        var proficiency = new Array<int>();
        foreach (int level in Proficiency)
            proficiency.Add(level);

        var root = new Dictionary
        {
            { "version", Version },
            { "credits", Credits },
            { "stash", Stash },
            { "proficiency", proficiency },
            { "loadout", LoadoutWeapon },
            { "loadout_secondary", LoadoutSecondary },
            { "owned", Owned },
            { "collected", Collected },
            { "claimed_sets", ClaimedSets },
            { "gear", new Array<string> { EquippedGear[0] ?? "", EquippedGear[1] ?? "", EquippedGear[2] ?? "" } },
            { "runs_survived", RunsSurvived },
            { "runs_lost", RunsLost },
            { "best_bank", BestBank },
            { "best_kills", BestKills },
            { "best_seconds", BestSeconds },
            { "best_multiplier", BestMultiplier },
            { "streak", Streak },
            { "best_streak", BestStreak },
            { "contract_seed", ContractSeed },
            { "contract_index", ContractIndex },
            { "unlocked", Unlocked },
            { "bosses_killed", BossesKilled },
            { "biome", Biome },
            { "character", Character },
            { "seen_base", HasSeenBase },
            { "daily", Daily },
            { "most_crates", MostCrates },
            { "best_throw", BestThrow },
            { "best_boss_kills", BestBossKills },

            // The sentinels are written as-is. A profile that has never survived
            // has no narrowest escape, and writing zero would mean it loads back
            // as a perfect record nothing can ever beat.
            { "narrowest_escape", NarrowestEscape },
            { "fastest_extraction", FastestExtraction },
        };

        return Json.Stringify(root, "  ");
    }

    /// Returns null when the text is not a profile this build understands. A
    /// caller that gets null should start fresh rather than half-apply a file.
    public static Profile? FromJson(string text)
    {
        var json = new Json();
        if (json.Parse(text) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return null;

        var root = json.Data.AsGodotDictionary();
        if (!root.TryGetValue("version", out Variant version))
            return null;

        int fileVersion = version.AsInt32();

        // A version this build predates is still refused outright: reading a
        // newer file with older rules is how a save gets quietly rewritten with
        // half its contents missing. Older ones are migrated, because the file
        // is the one thing a player cannot afford to lose and "start fresh" is
        // the same outcome as a corrupted save.
        if (fileVersion > Version || fileVersion < 1)
            return null;

        var profile = new Profile();

        if (root.TryGetValue("credits", out Variant credits))
            profile.Credits = credits.AsInt32();

        if (root.TryGetValue("stash", out Variant stash) && stash.VariantType == Variant.Type.Dictionary)
        {
            foreach (var pair in stash.AsGodotDictionary())
                profile.Stash[pair.Key.AsString()] = pair.Value.AsInt32();
        }

        if (root.TryGetValue("proficiency", out Variant proficiency) && proficiency.VariantType == Variant.Type.Array)
        {
            var levels = proficiency.AsGodotArray();
            for (int i = 0; i < profile.Proficiency.Length && i < levels.Count; i++)
                profile.Proficiency[i] = levels[i].AsInt32();
        }

        if (root.TryGetValue("loadout", out Variant loadout))
            profile.LoadoutWeapon = loadout.AsString();

        if (root.TryGetValue("loadout_secondary", out Variant secondary))
            profile.LoadoutSecondary = secondary.AsString();

        // Absent in a v1 file, which is the whole reason for the version bump:
        // the constructor has already granted the starting kit, so a migrated
        // profile arrives owning exactly what it can never lose and nothing it
        // was never recorded as buying.
        if (root.TryGetValue("owned", out Variant owned) && owned.VariantType == Variant.Type.Array)
        {
            foreach (Variant entry in owned.AsGodotArray())
                profile.Grant(entry.AsString());
        }

        if (root.TryGetValue("collected", out Variant collected) && collected.VariantType == Variant.Type.Array)
        {
            foreach (Variant entry in collected.AsGodotArray())
                profile.Record(entry.AsString());
        }

        if (root.TryGetValue("claimed_sets", out Variant claimed) && claimed.VariantType == Variant.Type.Array)
        {
            foreach (Variant entry in claimed.AsGodotArray())
            {
                var name = entry.AsString();
                if (!profile.ClaimedSets.Contains(name))
                    profile.ClaimedSets.Add(name);
            }
        }

        if (root.TryGetValue("gear", out Variant gear) && gear.VariantType == Variant.Type.Array)
        {
            var slots = gear.AsGodotArray();
            for (int i = 0; i < profile.EquippedGear.Length && i < slots.Count; i++)
            {
                string path = slots[i].AsString();
                if (!string.IsNullOrEmpty(path))
                    profile.EquippedGear[i] = path;
            }
        }

        if (root.TryGetValue("runs_survived", out Variant survived))
            profile.RunsSurvived = survived.AsInt32();

        if (root.TryGetValue("runs_lost", out Variant lost))
            profile.RunsLost = lost.AsInt32();

        // Records and contracts arrived without a version bump, by the rule this
        // file already follows: a key that can be defaulted does not need one.
        // Zero is the honest value for a profile written before anything was
        // being recorded — the runs happened, but nothing measured them, and
        // inventing bests from RunsSurvived would be worse than admitting that.
        if (root.TryGetValue("best_bank", out Variant bestBank))
            profile.BestBank = bestBank.AsInt32();

        if (root.TryGetValue("best_kills", out Variant bestKills))
            profile.BestKills = bestKills.AsInt32();

        if (root.TryGetValue("best_seconds", out Variant bestSeconds))
            profile.BestSeconds = (float)bestSeconds.AsDouble();

        if (root.TryGetValue("best_multiplier", out Variant bestMultiplier))
            profile.BestMultiplier = (float)bestMultiplier.AsDouble();

        if (root.TryGetValue("streak", out Variant streak))
            profile.Streak = streak.AsInt32();

        if (root.TryGetValue("best_streak", out Variant bestStreak))
            profile.BestStreak = bestStreak.AsInt32();

        if (root.TryGetValue("contract_seed", out Variant contractSeed))
            profile.ContractSeed = contractSeed.AsInt32();

        if (root.TryGetValue("contract_index", out Variant contractIndex))
            profile.ContractIndex = contractIndex.AsInt32();

        if (root.TryGetValue("bosses_killed", out Variant bosses))
            profile.BossesKilled = bosses.AsInt32();

        if (root.TryGetValue("biome", out Variant biome))
            profile.Biome = biome.AsInt32();

        // Absent means a save written before the roster existed, and zero is the
        // Drifter — whose numbers are what every one of those saves was played
        // with. An old profile therefore loads as exactly the character it has
        // always been.
        if (root.TryGetValue("character", out Variant character))
            profile.Character = character.AsInt32();

        // Absent means a file written before the first-run path existed, and
        // those players have very much seen the base screen. Defaulting to false
        // would drop every existing player straight into a run on next launch,
        // past the shop they were on their way to.
        profile.HasSeenBase = !root.TryGetValue("seen_base", out Variant seen) || seen.AsBool();

        if (root.TryGetValue("daily", out Variant daily) && daily.VariantType == Variant.Type.Dictionary)
        {
            foreach (var pair in daily.AsGodotDictionary())
                profile.Daily[pair.Key.AsString()] = pair.Value.AsInt32();
        }

        if (root.TryGetValue("most_crates", out Variant crates))
            profile.MostCrates = crates.AsInt32();

        if (root.TryGetValue("best_throw", out Variant throwKills))
            profile.BestThrow = throwKills.AsInt32();

        if (root.TryGetValue("best_boss_kills", out Variant bossKills))
            profile.BestBossKills = bossKills.AsInt32();

        if (root.TryGetValue("narrowest_escape", out Variant escape))
            profile.NarrowestEscape = (float)escape.AsDouble();

        if (root.TryGetValue("fastest_extraction", out Variant fastest))
            profile.FastestExtraction = (float)fastest.AsDouble();

        if (root.TryGetValue("unlocked", out Variant unlocked) && unlocked.VariantType == Variant.Type.Array)
        {
            foreach (Variant entry in unlocked.AsGodotArray())
                profile.Open(entry.AsString());
        }
        else
        {
            // No key at all means a file written before unlocks existed, and the
            // difference between that and "unlocked nothing yet" matters: a
            // veteran profile loading into a game that has just taken four
            // weapons off its shop shelf would read as the save being corrupted.
            //
            // The records the profile already keeps are enough to hand back
            // whatever it has demonstrably earned. It is deliberately generous —
            // a player who has beaten a condition and cannot prove it should get
            // the benefit, because the alternative punishes them for having
            // played before the feature shipped.
            profile.GrantEarnedUnlocks();
        }

        return profile;
    }
}
