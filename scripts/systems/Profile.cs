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

    /// Equipped gear by slot: armour, backpack, boots. An empty entry falls back
    /// to the starting piece, so a player can never arrive at a run with no
    /// backpack because the last one was lost.
    public string[] EquippedGear { get; } = new string[3];

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

        Streak++;
        BestStreak = Mathf.Max(BestStreak, Streak);
        return beaten;
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
    public bool Revoke(string path)
    {
        if (IsStartingKit(path) || !Owns(path))
            return false;

        Owned.Remove(path);
        return true;
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

        return profile;
    }
}
