using Godot;

/// Checks that progress made of content behaves like progress.
///
///   godot --headless --script test/UnlockProbe.cs
///
/// Exit code is the verdict. An unlock system has three ways to fail silently and
/// this exists for all three: a condition nothing evaluates (the player does the
/// thing and nothing happens), a grant nothing reads (the unlock opens and the
/// shop still refuses), and a save that forgets (the unlock opens and is gone
/// next launch). All three look identical from inside a run — which is to say,
/// they look like a game with no unlocks in it.
public partial class UnlockProbe : SceneTree
{
    private Node? _scene;
    private MetaManager? _meta;
    private RunGrowth? _growth;

    private int _stage;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0)
        {
            _meta = _scene?.GetNodeOrNull<MetaManager>("MetaManager");
            _growth = _scene?.GetNodeOrNull<RunGrowth>("RunGrowth");

            if (_meta == null || _growth == null)
            {
                GD.PushError("PROBE FAILED - scene is missing a required node");
                Quit(1);
                return true;
            }

            _scene?.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
        }

        switch (_stage)
        {
            case 0: return RunStage(StageFreshProfileIsSmaller, "a fresh profile is offered less than a finished one");
            case 1: return RunStage(StageEveryConditionCanFire, "every condition is reachable, and only by its own run");
            case 2: return RunStage(StageOpeningChangesTheDeck, "opening one puts exactly it into the deck");
            case 3: return RunStage(StageShopSaysWhy, "a locked entry is listed, refused, and explains itself");
            case 4: return RunStage(StageSurvivesTheRoundTrip, "unlocks survive being written to disk and read back");
            case 5: return RunStage(StageOldSaveKeepsWhatItEarned, "a save from before unlocks keeps what it proved");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<bool> stage, string label)
    {
        bool verdict = stage();
        GD.Print($"{label}: {(verdict ? "ok" : "FAILED")}");
        _failed |= !verdict;
        _stage++;
        return false;
    }

    /// The claim the whole phase rests on: run one and run fifty are not the same
    /// game. If this passes trivially — if the locked set is empty — then every
    /// other stage here is checking the plumbing of a feature that does nothing.
    private bool StageFreshProfileIsSmaller()
    {
        var fresh = new Profile();

        int lockedWeapons = 0, lockedGrowth = 0;
        foreach (Unlock unlock in UnlockBook.All)
        {
            if (fresh.HasUnlocked(unlock.Id))
                continue;

            if (unlock.Kind == UnlockKind.ShopStock)
                lockedWeapons++;
            else
                lockedGrowth++;
        }

        var catalogue = new ShopCatalogue();
        int sellable = 0;
        foreach (ShopCatalogue.Entry entry in catalogue.All)
        {
            if (UnlockBook.ShopAllows(fresh, entry.Path))
                sellable++;
        }

        int deck = 0;
        foreach (GrowthOption option in System.Enum.GetValues<GrowthOption>())
        {
            if (UnlockBook.GrowthAllows(fresh, option))
                deck++;
        }

        int total = System.Enum.GetValues<GrowthOption>().Length;
        GD.Print($"  fresh profile: {sellable} of {catalogue.All.Count} shop rows buyable " +
                 $"({lockedWeapons} locked), {deck} of {total} growth options in the deck " +
                 $"({lockedGrowth} locked)");

        return lockedWeapons > 0 && lockedGrowth > 0 && sellable > 0 && deck > 0;
    }

    /// Each condition, against a run built to meet exactly it.
    ///
    /// The second half is the half that matters. A condition written as
    /// `run.Kills >= 0` would pass the first check for every unlock in the table
    /// and hand the player everything after one run — so each fixture is also
    /// asserted *not* to open anything else.
    private bool StageEveryConditionCanFire()
    {
        (string Id, RunRecord Run, Profile Profile)[] fixtures =
        {
            ("bow", Extraction(kills: 30, firearmHits: 0), Fresh()),
            ("ignite", Extraction(kills: 60), Fresh()),
            ("service_rifle", Extraction(), Streaked(3)),
            ("scythe", Extraction(bosses: 1), Fresh()),
            ("detonate", Extraction(throwKills: 8), Fresh()),
            ("thorns", Extraction(lowestHealth: 9.0f), Fresh()),
            ("lifesteal", Extraction(crates: 6), Fresh()),
            ("fortune", Extraction(multiplier: 2.8f), Fresh()),
        };

        bool ok = true;

        foreach ((string id, RunRecord run, Profile profile) in fixtures)
        {
            var opened = new System.Collections.Generic.List<string>();
            foreach (Unlock unlock in UnlockBook.NewlyMet(run, profile))
                opened.Add(unlock.Id);

            // Exactly one, with no exemptions. An earlier version let "bow" ride
            // along with any fixture, on the reasoning that most runs happen not
            // to fire a gun — and that exemption is precisely what hid a bow
            // condition reading the wrong field. Every fixture now carries
            // firearm hits, so the only run that opens the bow is the one built
            // not to.
            bool fired = opened.Contains(id);
            bool clean = opened.Count == 1;

            if (!fired || !clean)
            {
                GD.PushError($"  {id}: opened [{string.Join(", ", opened)}]");
                ok = false;
            }
        }

        GD.Print($"  {fixtures.Length} conditions, each met by its own run and nothing else's");
        return ok;
    }

    /// Opening one has to move exactly one card, and the shop entry it names has
    /// to be the one that becomes buyable. Granting an id nothing reads is the
    /// quietest of the three failures.
    private bool StageOpeningChangesTheDeck()
    {
        var profile = new Profile();

        bool thornsBefore = UnlockBook.GrowthAllows(profile, GrowthOption.Thorns);
        bool lifestealBefore = UnlockBook.GrowthAllows(profile, GrowthOption.Lifesteal);
        bool scytheBefore = UnlockBook.ShopAllows(profile, "res://resources/weapons/reaper_scythe.tres");

        profile.Open("thorns");
        profile.Open("scythe");

        bool thornsAfter = UnlockBook.GrowthAllows(profile, GrowthOption.Thorns);
        bool lifestealAfter = UnlockBook.GrowthAllows(profile, GrowthOption.Lifesteal);
        bool scytheAfter = UnlockBook.ShopAllows(profile, "res://resources/weapons/reaper_scythe.tres");

        GD.Print($"  thorns {thornsBefore}->{thornsAfter}, scythe {scytheBefore}->{scytheAfter}, " +
                 $"untouched lifesteal {lifestealBefore}->{lifestealAfter}");

        return !thornsBefore && thornsAfter
               && !scytheBefore && scytheAfter
               && !lifestealBefore && !lifestealAfter;
    }

    /// The catalogue still lists it, the reason is printed, and buying is refused.
    /// A locked row that vanishes from the list is content the player will never
    /// know to want.
    private bool StageShopSaysWhy()
    {
        var profile = new Profile();
        const string path = "res://resources/weapons/hunting_bow.tres";

        var catalogue = new ShopCatalogue();
        bool listed = false;
        foreach (ShopCatalogue.Entry entry in catalogue.All)
            listed |= entry.Path == path;

        string? reason = UnlockBook.ShopLockReason(profile, path, 2);
        bool allowed = UnlockBook.ShopAllows(profile, path);

        // Something not in the table at all must be freely sellable — the table
        // is a list of things held back, not a whitelist, so a weapon added later
        // without an unlock row has to reach the shelf rather than disappear.
        const string unmentioned = "res://resources/weapons/scavenged_rifle.tres";
        bool unmentionedFree = UnlockBook.ShopAllows(profile, unmentioned)
                               && UnlockBook.ShopLockReason(profile, unmentioned, 1) == null;

        GD.Print($"  bow listed={listed} buyable={allowed} reason=\"{reason}\", " +
                 $"an unmentioned weapon is free: {unmentionedFree}");

        return listed && !allowed && !string.IsNullOrEmpty(reason) && unmentionedFree;
    }

    private bool StageSurvivesTheRoundTrip()
    {
        var profile = new Profile();
        profile.Open("scythe");
        profile.Open("thorns");
        profile.BossesKilled = 2;

        Profile? read = Profile.FromJson(profile.ToJson());
        if (read == null)
        {
            GD.PushError("  the profile did not parse back");
            return false;
        }

        bool kept = read.HasUnlocked("scythe") && read.HasUnlocked("thorns");
        bool nothingExtra = read.Unlocked.Count == 2;
        bool bosses = read.BossesKilled == 2;

        GD.Print($"  wrote 2 unlocks and 2 bosses, read back {read.Unlocked.Count} and {read.BossesKilled}");
        return kept && nothingExtra && bosses;
    }

    /// A file written before unlocks existed must not read as a player who has
    /// unlocked nothing. That distinction is invisible in the JSON — both are an
    /// absent key — so it is decided by whether the key is there at all, and this
    /// is the only test of that.
    private bool StageOldSaveKeepsWhatItEarned()
    {
        var veteran = new Profile { BestKills = 90, BestStreak = 5, BestMultiplier = 3.0f };

        // Round-tripped through a document with the unlock key stripped, which is
        // exactly what an older build wrote.
        string json = veteran.ToJson()
            .Replace("\"unlocked\": []", "\"_removed\": []")
            .Replace("\"unlocked\":[]", "\"_removed\":[]");

        Profile? migrated = Profile.FromJson(json);
        if (migrated == null)
        {
            GD.PushError("  the migrated profile did not parse");
            return false;
        }

        bool earned = migrated.HasUnlocked("ignite")
                      && migrated.HasUnlocked("service_rifle")
                      && migrated.HasUnlocked("fortune");

        // Never fought one, so it stays shut. A migration that hands over
        // everything is a migration that deletes the feature for every existing
        // player, which is the failure this half exists to catch.
        bool unproven = !migrated.HasUnlocked("scythe");

        var beginner = new Profile();
        string beginnerJson = beginner.ToJson()
            .Replace("\"unlocked\": []", "\"_removed\": []")
            .Replace("\"unlocked\":[]", "\"_removed\":[]");
        Profile? migratedBeginner = Profile.FromJson(beginnerJson);
        bool nothingForNothing = migratedBeginner is { Unlocked.Count: 0 };

        GD.Print($"  a veteran save kept {migrated.Unlocked.Count} unlocks it had proved " +
                 $"(scythe still shut: {unproven}); an empty old save got {migratedBeginner?.Unlocked.Count}");

        return earned && unproven && nothingForNothing;
    }

    private static Profile Fresh() => new();

    private static Profile Streaked(int streak)
    {
        var profile = new Profile();
        for (int i = 0; i < streak; i++)
            profile.ApplyRecords(Extraction());

        return profile;
    }

    /// A clean extraction that meets nothing on its own except the bow's
    /// condition, which is the absence of gunfire. Each named argument is the one
    /// fact a single unlock is supposed to turn on.
    private static RunRecord Extraction(int kills = 10, int bosses = 0, int throwKills = 0,
                                        int crates = 0, float lowestHealth = 80.0f,
                                        float multiplier = 1.4f, int firearmHits = 400) =>
        new()
        {
            Outcome = RunState.Extracted,
            Seconds = 90.0f,
            Banked = 100,
            Multiplier = multiplier,
            KillsByType = Kills(kills),
            CratesLooted = crates,
            LowestHealth = lowestHealth,
            MaxHealth = 100.0f,
            BossesKilled = bosses,
            BestThrowKills = throwKills,
            ProficiencyGained = new int[4],

            // Non-zero by default, so every other fixture is a run that did fire
            // a gun. The default used to be an all-zero array, which meant every
            // fixture silently satisfied the bow as well and the stage's "and
            // nothing else's" half had a permanent exemption written into it.
            HitsByCategory = new[] { 0, 0, 0, firearmHits },
        };

    private static int[] Kills(int total) => new[] { total, 0, 0, 0, 0, 0 };
}
