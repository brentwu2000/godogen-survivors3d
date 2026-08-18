using Godot;

/// Runs the play-test across a matrix of linger times and layouts, and prints the
/// table the balance conversation actually needs.
///
///   godot --headless --script test/BalanceSweep.cs
///   godot --headless --script test/BalanceSweep.cs -- seeds:3 lingers:0,90
///
/// Exit code is the verdict, and the verdict is deliberately not a specific
/// number: **at least one linger tier has to survive past 180 seconds.** A three
/// hundred second deadline is only a deadline if somebody can reach it, and a
/// clock nobody has ever seen the second half of is the same as no clock.
///
/// It spawns the real `AutoPlay` once per cell rather than re-implementing the
/// bot. Re-implementing would be far faster and would be measuring a second bot:
/// the sweep has to be looking at the thing a play-test looks at, or the table is
/// about a game nobody plays. Same rule that makes the level generator's
/// reachability check ask a real FlowField.
///
/// `--fixed-fps 60` on each child decouples simulated time from wall clock, which
/// is what makes twenty runs of up to five minutes each a thing you can wait for:
/// roughly eleven times real time on this machine.
public partial class BalanceSweep : SceneTree
{
    /// The linger tiers. 0 is "leave as soon as the route is done", and the rest
    /// walk further up the escalation curve before heading for the pad.
    private static readonly float[] DefaultLingers = { 0.0f, 60.0f, 120.0f, 180.0f };

    /// Layouts. Fixed rather than random, because a balance table that moves
    /// between runs is not a table — and cover density varies enough between
    /// seeds that one layout is an anecdote.
    private static readonly ulong[] DefaultSeeds =
    {
        0x51E5D0A7UL, 0x9E3779B9UL, 0xC17E4A9BUL, 0x2545F491UL, 0xBF58476DUL,
    };

    /// The tier that has to reach this, for the run clock to mean anything.
    private const float SurvivalTarget = 180.0f;

    private readonly struct Row
    {
        public readonly float Linger;
        public readonly ulong Seed;
        public readonly string Outcome;
        public readonly float Seconds;
        public readonly int Banked;
        public readonly int LowestHp;
        public readonly int Peak;

        public Row(float linger, ulong seed, string outcome, float seconds, int banked, int lowestHp, int peak)
        {
            Linger = linger;
            Seed = seed;
            Outcome = outcome;
            Seconds = seconds;
            Banked = banked;
            LowestHp = lowestHp;
            Peak = peak;
        }

        public bool Survived => Outcome == "Extracted";
    }

    public override void _Initialize()
    {
        float[] lingers = DefaultLingers;
        ulong[] seeds = DefaultSeeds;

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("seeds:") && int.TryParse(arg[6..], out int count))
                seeds = seeds[..Mathf.Clamp(count, 1, seeds.Length)];

            if (arg.StartsWith("lingers:"))
            {
                string[] parts = arg[8..].Split(',');
                var parsed = new System.Collections.Generic.List<float>();
                foreach (string part in parts)
                {
                    if (float.TryParse(part, out float value))
                        parsed.Add(value);
                }

                if (parsed.Count > 0)
                    lingers = parsed.ToArray();
            }
        }

        var rows = new System.Collections.Generic.List<Row>();
        GD.Print($"sweeping {lingers.Length} linger tiers x {seeds.Length} layouts = " +
                 $"{lingers.Length * seeds.Length} runs");

        foreach (float linger in lingers)
        {
            foreach (ulong seed in seeds)
            {
                Row? row = RunOne(linger, seed);
                if (row is { } value)
                    rows.Add(value);
                else
                    GD.PushError($"  linger {linger:F0} seed {seed}: produced no result");
            }
        }

        Report(rows, lingers);
    }

    /// One child process. `OS.Execute` blocks until it exits, which is what makes
    /// this a sequential sweep rather than twenty Godots fighting over the GPU.
    private static Row? RunOne(float linger, ulong seed)
    {
        var output = new Godot.Collections.Array();

        string[] args =
        {
            "--headless", "--fixed-fps", "60", "--path", ProjectSettings.GlobalizePath("res://"),
            "--script", "test/AutoPlay.cs", "--",
            $"linger:{linger:F0}", $"seed:{seed}",
        };

        // The exit code is ignored on purpose: a death is a failure for the
        // play-test and a data point for this, and treating it as an error here
        // would throw away exactly the rows the table exists to show.
        OS.Execute(OS.GetExecutablePath(), args, output, readStderr: true);

        foreach (Variant line in output)
        {
            foreach (string text in line.AsString().Split('\n'))
            {
                if (text.Contains("SWEEP "))
                    return Parse(text, linger, seed);
            }
        }

        return null;
    }

    private static Row Parse(string line, float linger, ulong seed)
    {
        string outcome = Field(line, "outcome");
        return new Row(
            linger, seed, outcome.Length > 0 ? outcome : "?",
            Number(line, "seconds"),
            Mathf.RoundToInt(Number(line, "banked")),
            Mathf.RoundToInt(Number(line, "lowestHp")),
            Mathf.RoundToInt(Number(line, "peak")));
    }

    private static string Field(string line, string key)
    {
        int at = line.IndexOf($"{key}=", System.StringComparison.Ordinal);
        if (at < 0)
            return "";

        int start = at + key.Length + 1;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private static float Number(string line, string key) =>
        float.TryParse(Field(line, key), out float value) ? value : 0.0f;

    private void Report(System.Collections.Generic.List<Row> rows, float[] lingers)
    {
        GD.Print("");
        GD.Print("linger   survived   median banked   median death   worst peak   median lowest HP");

        bool reachesTarget = false;

        foreach (float linger in lingers)
        {
            var tier = new System.Collections.Generic.List<Row>();
            foreach (Row row in rows)
            {
                if (Mathf.IsEqualApprox(row.Linger, linger))
                    tier.Add(row);
            }

            if (tier.Count == 0)
                continue;

            int survived = 0;
            var banked = new System.Collections.Generic.List<float>();
            var deaths = new System.Collections.Generic.List<float>();
            var lowest = new System.Collections.Generic.List<float>();
            int worstPeak = 0;

            foreach (Row row in tier)
            {
                worstPeak = Mathf.Max(worstPeak, row.Peak);
                lowest.Add(row.LowestHp);

                if (row.Survived)
                {
                    survived++;
                    banked.Add(row.Banked);

                    // A survived run "reached" however long it lasted, which is
                    // the number the target is about.
                    if (row.Seconds >= SurvivalTarget)
                        reachesTarget = true;
                }
                else
                {
                    deaths.Add(row.Seconds);
                }
            }

            GD.Print($"{linger,5:F0}s   {survived}/{tier.Count,-8}   " +
                     $"{Median(banked),13}   {Median(deaths),12}   {worstPeak,10}   {Median(lowest),16}");
        }

        GD.Print("");
        foreach (Row row in rows)
        {
            GD.Print($"  linger {row.Linger,3:F0}s seed {row.Seed,-12} {row.Outcome,-9} " +
                     $"{row.Seconds,6:F1}s  banked {row.Banked,5}  peak {row.Peak,4}  lowest HP {row.LowestHp,3}");
        }

        GD.Print("");
        GD.Print(reachesTarget
            ? $"SWEEP OK — at least one run reached {SurvivalTarget:F0}s and walked out"
            : $"SWEEP FAILED — nothing reached {SurvivalTarget:F0}s; the second half of the clock is fiction");

        Quit(reachesTarget ? 0 : 1);
    }

    /// Median rather than mean, because a single early death drags an average
    /// somewhere no individual run went.
    private static string Median(System.Collections.Generic.List<float> values)
    {
        if (values.Count == 0)
            return "-";

        values.Sort();
        float middle = values.Count % 2 == 1
            ? values[values.Count / 2]
            : (values[values.Count / 2 - 1] + values[values.Count / 2]) * 0.5f;

        return middle.ToString("F0");
    }
}
