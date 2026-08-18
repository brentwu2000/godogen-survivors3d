using Godot;

/// Checks that today's run is the same run for everyone and playable once.
///
///   godot --headless --script test/DailyProbe.cs
///
/// Exit code is the verdict. A daily has exactly three properties and all three
/// fail silently. If the derivation is not stable, two players get different maps
/// and neither has any way to notice. If the attempt is not spent, a player who
/// dislikes their result plays it again and the score table quietly becomes a
/// record of persistence. And if it settles into the profile, the mode with no
/// risk becomes the best way to earn, which is not a bug anyone reports.
public partial class DailyProbe : SceneTree
{
    private int _stage;
    private bool _failed;

    public override void _Initialize()
    {
        // No scene. Everything here is derivation and bookkeeping — loading a
        // level to test a hash function would make the probe slower and no more
        // truthful.
    }

    public override bool _Process(double delta)
    {
        switch (_stage)
        {
            case 0: return RunStage(StageSameDaySameRun, "one date derives one run, every time");
            case 1: return RunStage(StageDifferentDaysDiffer, "consecutive days are not the same run");
            case 2: return RunStage(StageOnlyOneAttempt, "the second attempt at a day does not count");
            case 3: return RunStage(StageDeathStillSpendsIt, "dying spends the attempt too");
            case 4: return RunStage(StageStreakCountsBackwards, "a streak is consecutive days and a gap ends it");
            case 5: return RunStage(StageScoreRewardsTheJob, "the score is mostly about the card everyone got");
            case 6: return RunStage(StageSurvivesTheRoundTrip, "results survive being written and read back");
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

    /// All three of seed, biome and job, not just the seed.
    ///
    /// "Everyone gets the same run" is three claims. A stable seed with a biome
    /// picked from the profile would give every player the same layout in a
    /// different place, which is a different run wearing the same map.
    private bool StageSameDaySameRun()
    {
        const string date = "2026-03-14";

        DailyRun.Setup a = DailyRun.For(date);
        DailyRun.Setup b = DailyRun.For(date);

        bool same = a.LevelSeed == b.LevelSeed
                    && a.Biome == b.Biome
                    && a.Job.Kind == b.Job.Kind
                    && a.Job.Subject == b.Job.Subject
                    && a.Job.Target == b.Job.Target;

        GD.Print($"  {date}: seed {a.LevelSeed}, {BiomeBook.Load(a.Biome).BiomeName}, " +
                 $"\"{a.Job.Describe()}\"");

        return same;
    }

    /// Neighbouring dates must not produce neighbouring runs.
    ///
    /// A hash that simply sums the digits would give consecutive days seeds one
    /// apart, and this project's PRNG is an xorshift over the seed — so two
    /// adjacent seeds can produce recognisably similar maps. The derivation folds
    /// the hash through the shift chain for exactly this reason, and this is the
    /// only thing that checks it did.
    private bool StageDifferentDaysDiffer()
    {
        string[] week =
        {
            "2026-03-14", "2026-03-15", "2026-03-16", "2026-03-17",
            "2026-03-18", "2026-03-19", "2026-03-20",
        };

        var seeds = new System.Collections.Generic.HashSet<ulong>();
        var biomes = new System.Collections.Generic.HashSet<int>();
        var jobs = new System.Collections.Generic.HashSet<string>();

        foreach (string date in week)
        {
            DailyRun.Setup setup = DailyRun.For(date);
            seeds.Add(setup.LevelSeed);
            biomes.Add(setup.Biome);
            jobs.Add(setup.Job.Describe());
        }

        GD.Print($"  seven days: {seeds.Count} distinct seeds, {biomes.Count} places, {jobs.Count} jobs");

        // Every seed distinct is the hard requirement. Biomes and jobs are drawn
        // from small sets, so repeats across a week are expected — but all seven
        // days landing on one place would mean the derivation is not using the
        // date past the first byte.
        return seeds.Count == week.Length && biomes.Count > 1 && jobs.Count > 1;
    }

    private bool StageOnlyOneAttempt()
    {
        var profile = new Profile();
        const string date = "2026-03-14";

        bool freshIsOpen = !profile.DailyDone(date);
        profile.RecordDaily(date, 1200);
        bool nowDone = profile.DailyDone(date);

        // A better second score must not overwrite. That is the direction the bug
        // would go: "keep the best" is the intuitive rule and it turns one attempt
        // into unlimited attempts with extra steps.
        profile.RecordDaily(date, 9999);
        int kept = profile.Daily[date];

        GD.Print($"  open before: {freshIsOpen}, recorded 1200 then 9999, kept {kept}");
        return freshIsOpen && nowDone && kept == 1200;
    }

    /// The rule that decides whether the mode is honest.
    ///
    /// If a death did not spend the day, dying on purpose would be the way to
    /// keep the attempt — a reroll button wearing a corpse. Every other rule in
    /// this game makes death expensive, and this one has to as well.
    private bool StageDeathStillSpendsIt()
    {
        var profile = new Profile();
        const string date = "2026-03-14";

        var died = new RunRecord
        {
            Outcome = RunState.Died,
            Seconds = 40.0f,
            KillsByType = new[] { 12, 0, 0, 0, 0, 0 },
            HitsByCategory = new int[4],
            ProficiencyGained = new int[4],
        };

        int score = DailyRun.Score(died, jobMet: false);
        profile.RecordDaily(date, score);

        GD.Print($"  a run that ended on the floor scored {score} and the day is " +
                 $"{(profile.DailyDone(date) ? "spent" : "still open")}");

        return score == 0 && profile.DailyDone(date);
    }

    private bool StageStreakCountsBackwards()
    {
        var profile = new Profile();

        foreach (string date in new[] { "2026-03-12", "2026-03-13", "2026-03-14" })
            profile.RecordDaily(date, 100);

        int run = profile.DailyStreak("2026-03-14");

        // A gap: the older block is unreachable from today, so the streak is one.
        var gapped = new Profile();
        foreach (string date in new[] { "2026-03-10", "2026-03-11", "2026-03-14" })
            gapped.RecordDaily(date, 100);

        int broken = gapped.DailyStreak("2026-03-14");

        // Across a month boundary, which is the arithmetic worth checking: the
        // day before the first of March is not "March 0".
        var monthly = new Profile();
        foreach (string date in new[] { "2026-02-27", "2026-02-28", "2026-03-01" })
            monthly.RecordDaily(date, 100);

        int crossed = monthly.DailyStreak("2026-03-01");

        int unplayed = profile.DailyStreak("2026-03-20");

        GD.Print($"  three in a row: {run}; with a gap: {broken}; across a month end: {crossed}; " +
                 $"a day not played: {unplayed}");

        return run == 3 && broken == 1 && crossed == 3 && unplayed == 0;
    }

    /// The score has to be mostly about the shared card.
    ///
    /// Everyone got the same job, so that is the only axis on which two players'
    /// days are comparable at all. A score dominated by banked credits would rank
    /// the run that ignored the card and looted, which makes the card decoration.
    private bool StageScoreRewardsTheJob()
    {
        var run = new RunRecord
        {
            Outcome = RunState.Extracted,
            Seconds = 120.0f,
            Banked = 400,
            KillsByType = new[] { 60, 0, 0, 0, 0, 0 },
            CratesLooted = 4,
            HitsByCategory = new int[4],
            ProficiencyGained = new int[4],
        };

        int missed = DailyRun.Score(run, jobMet: false);
        int met = DailyRun.Score(run, jobMet: true);

        var died = new RunRecord
        {
            Outcome = RunState.Died,
            Banked = 400,
            KillsByType = new[] { 60, 0, 0, 0, 0, 0 },
            HitsByCategory = new int[4],
            ProficiencyGained = new int[4],
        };

        int dead = DailyRun.Score(died, jobMet: true);

        GD.Print($"  same run: {missed} without the job, {met} with it; " +
                 $"the same numbers on a corpse: {dead}");

        // The job worth more than a third of the total, and nothing at all paid
        // for a run that did not walk out.
        return met > missed && met - missed > met / 3 && dead == 0;
    }

    private bool StageSurvivesTheRoundTrip()
    {
        var profile = new Profile();
        profile.RecordDaily("2026-03-13", 820);
        profile.RecordDaily("2026-03-14", 1340);
        profile.MostCrates = 9;
        profile.BestThrow = 11;
        profile.BestBossKills = 1;
        profile.NarrowestEscape = 4.0f;
        profile.FastestExtraction = 61.5f;

        Profile? read = Profile.FromJson(profile.ToJson());
        if (read == null)
        {
            GD.PushError("  the profile did not parse back");
            return false;
        }

        bool days = read.Daily.Count == 2
                    && read.Daily["2026-03-13"] == 820
                    && read.Daily["2026-03-14"] == 1340;

        bool book = read.MostCrates == 9 && read.BestThrow == 11 && read.BestBossKills == 1
                    && Mathf.IsEqualApprox(read.NarrowestEscape, 4.0f)
                    && Mathf.IsEqualApprox(read.FastestExtraction, 61.5f);

        // A profile that has never survived must not load back claiming a perfect
        // narrowest escape — the sentinel has to round-trip as a sentinel.
        Profile? fresh = Profile.FromJson(new Profile().ToJson());
        bool sentinel = fresh is { HasNarrowEscape: false, HasFastExtraction: false };

        GD.Print($"  {read.Daily.Count} days and the record book came back " +
                 $"({read.MostCrates} crates, {read.NarrowestEscape:F0} HP, {read.FastestExtraction:F0}s); " +
                 $"a fresh profile still has no escape record: {sentinel}");

        return days && book && sentinel;
    }
}
