using Godot;

/// Today's run: one fixed arena, one fixed job, one attempt.
///
/// Everything else in this game is a reason to keep playing tonight. This is the
/// reason to come back tomorrow, and it works by being **the same run for
/// everyone and only playable once** — take either half away and it is a normal
/// run with a label on it.
///
/// It pays no credits. If the daily paid better than an ordinary run, the
/// ordinary run would become the practice mode, and a mode nobody plays for its
/// own sake is a mode that has eaten the game. What it pays is a record.
public static class DailyRun
{
    /// UTC, always.
    ///
    /// Local time gives a player unlimited attempts for the price of changing
    /// their clock, and makes "today" two different answers either side of a
    /// timezone — which for a challenge whose whole premise is that everyone got
    /// the same one is the premise failing quietly.
    public static string TodayKey()
    {
        Godot.Collections.Dictionary now = Time.GetDatetimeDictFromSystem(utc: true);
        return $"{now["year"]}-{(int)now["month"]:00}-{(int)now["day"]:00}";
    }

    /// A date to a seed. Any stable hash would do; this is the same xorshift the
    /// rest of the project uses, so one PRNG covers the whole codebase and
    /// nothing has its own idea of "random".
    public static ulong SeedFor(string dateKey)
    {
        ulong state = 0xCBF29CE484222325UL;
        foreach (char c in dateKey)
        {
            state ^= c;
            state *= 0x100000001B3UL;
        }

        // Fold it through the shift chain so neighbouring dates do not produce
        // neighbouring seeds — consecutive days should not be recognisably the
        // same map.
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;

        return state == 0 ? 0x9E3779B97F4A7C15UL : state;
    }

    /// The whole run, derived. Nothing here is stored, so a save file cannot
    /// disagree with the calendar about what today was.
    public readonly struct Setup
    {
        public required string DateKey { get; init; }
        public required ulong LevelSeed { get; init; }
        public required int Biome { get; init; }
        public required Contract Job { get; init; }
    }

    public static Setup For(string dateKey)
    {
        ulong seed = SeedFor(dateKey);

        // Derived from the same seed by advancing it, rather than from three
        // separate hashes of the date: one number decides the day, and adding a
        // fourth thing to fix later means advancing it once more rather than
        // inventing another hash and hoping it does not correlate.
        ulong biomeState = Next(seed);
        ulong jobState = Next(biomeState);

        // Every biome, including ones the player has not opened. The daily is
        // allowed to send them somewhere they have not earned — that is a reason
        // to play it, and it grants nothing they get to keep.
        int biome = (int)(biomeState % (ulong)Mathf.Max(1, BiomeBook.All.Length));

        // One card, taken from the same table the base screen rolls, so a daily
        // job is a job the player already knows how to read.
        Contract[] offer = ContractBook.Roll(jobState);
        Contract job = offer.Length > 0 ? offer[0] : new Contract(ContractKind.KillTotal, 0, 100, 0);

        return new Setup
        {
            DateKey = dateKey,
            LevelSeed = seed,
            Biome = biome,
            Job = job,
        };
    }

    public static Setup Today() => For(TodayKey());

    private static ulong Next(ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    /// What a finished daily is worth as a score.
    ///
    /// Not credits. A single number so two attempts on the same day are
    /// comparable and so a table of dates reads as a table — and weighted toward
    /// what the run was for rather than toward how long it lasted, because
    /// "survived longest" is a score every idle strategy wins.
    public static int Score(RunRecord run, bool jobMet)
    {
        if (!run.Survived)
            return 0;

        int score = run.Banked;
        score += run.Kills * 2;
        score += run.CratesLooted * 25;

        // The job is most of it. The daily's identity is that everyone got the
        // same card, so a score that barely moves on whether it was done is a
        // score about something else.
        if (jobMet)
            score += 500;

        return score;
    }
}
