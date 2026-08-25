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
    /// Twelve layouts, and the first five are the original five.
    ///
    /// The order matters: `seeds:5` has to reproduce the table every earlier
    /// tuning decision was made against, or a number that moved because the
    /// sample changed reads as a number that moved because the game did.
    ///
    /// Five was enough while a run was one of two arms. Splitting the zone arm by
    /// tier makes it three, and not every layout has a zone of every tier — the
    /// first tiered table had six tier-0 rows against four tier-1 rows, and a
    /// median of four runs is a number one unlucky seed can move by a third.
    private static readonly ulong[] DefaultSeeds =
    {
        0x51E5D0A7UL, 0x9E3779B9UL, 0xC17E4A9BUL, 0x2545F491UL, 0xBF58476DUL,
        0x94D049BBUL, 0x1B873593UL, 0x85EBCA6BUL, 0xCC9E2D51UL, 0x27D4EB2FUL,
        0x165667B1UL, 0xD6E8FEB1UL,
    };

    /// The tier that has to reach this, for the run clock to mean anything.
    private const float SurvivalTarget = 180.0f;

    /// The linger tier that is a decision rather than a duration.
    private const float AutoLinger = -1.0f;

    private readonly struct Row
    {
        /// Whether this run attempted a danger zone.
        ///
        /// A run is one of two experiments rather than one measurement — a zone
        /// is optional in the game, and the interesting number is the
        /// *difference* between taking one and walking past.
        public readonly bool Zone;

        /// The tier of the zone this run actually attempted, or -1 for none.
        ///
        /// **The tier attempted, never the tier requested.** Not every seed has a
        /// tier-1 zone; `AutoPlay` says so and takes the nearest instead, and a
        /// table that grouped by the request would file that run under tier 1 and
        /// report a cost nobody paid.
        public readonly int ZoneTier;

        /// What the run climbed to. Separate from the outcome numbers because
        /// they answer a different question: banked says whether the run paid,
        /// these say whether the deck was ever spent.
        public readonly int Level;
        public readonly int Picks;
        public readonly int WeaponLevel;
        public readonly int WeaponMax;

        /// Seconds at which the weapon hit its ceiling, or -1. A run that never
        /// reaches it is a weapon curve the player never sees the end of.
        public readonly float CeilingAt;

        public readonly float Linger;
        public readonly ulong Seed;
        public readonly string Outcome;
        public readonly float Seconds;
        public readonly int Banked;
        public readonly int LowestHp;
        public readonly int Peak;

        /// The weapon the run actually started with, as `AutoPlay` reported it.
        /// Never the one the sweep asked for — see `zoneTier`, which learned this
        /// the expensive way.
        public readonly string Weapon;

        /// Who played it, as `AutoPlay` reported it. Same rule as `Weapon`.
        public readonly string Character;

        /// What it was wearing above the starting kit, as reported. Same rule.
        public readonly string Gear;

        /// The line it played and how many picks went into it. The second is what
        /// says whether it really did — a run offered its line twice in twenty
        /// picks played the Phase 8 order with a label on it.
        public readonly string Line;
        public readonly int PicksInLine;

        /// How many weapon slots fired.
        public readonly int Slots;

        /// The share of this run's arrivals that came as a knot, 0 for a
        /// scattered run. Printed per row rather than summarised, because the
        /// question it answers is a 2x2 — does a loadout that wants a crowd do
        /// better on the runs that deliver one — and that is a split of the rows
        /// rather than another column of medians.
        public readonly float KnotShare;

        public Row(bool zone, int zoneTier, int level, int picks, int weaponLevel, int weaponMax,
                   float ceilingAt, float linger, ulong seed, string outcome, float seconds,
                   int banked, int lowestHp, int peak, string weapon, string character, string gear, float knotShare,
                   string line, int picksInLine, int slots)
        {
            Slots = slots;
            KnotShare = knotShare;
            Line = line;
            PicksInLine = picksInLine;
            Weapon = weapon;
            Character = character;
            Gear = gear;
            Zone = zone;
            ZoneTier = zoneTier;
            Level = level;
            Picks = picks;
            WeaponLevel = weaponLevel;
            WeaponMax = weaponMax;
            CeilingAt = ceilingAt;
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

    /// Arm sentinels. Anything else is a tier to ask `AutoPlay` for.
    private const int NoZone = -1;
    private const int AnyTier = 99;

    public override void _Initialize()
    {
        float[] lingers = DefaultLingers;
        ulong[] seeds = DefaultSeeds;

        // Which arms to run. `off` is the default so the existing table keeps
        // meaning what it meant; `both` is what answers whether a zone pays; and
        // `tiers` is what answers *which* zone pays.
        //
        // `zones:both` was the honest answer to the wrong question. Its zone arm
        // took whichever zone was nearest, which is usually tier 0, so its results
        // were bimodal — two seeds paying heavily and three barely noticing — and
        // the spread read as variance in what a zone costs. It was variance in
        // which zone was taken.
        int[] arms = { NoZone };

        // One empty entry: whatever the profile equips, which is the starting kit.
        string[] weapons = { "" };

        // One empty entry: whoever `GameSession` defaults to, which is the
        // Drifter — the survivor every number in this file was tuned against.
        string[] characters = { "" };

        // One empty entry: the starting kit, which grants nothing.
        string[] loadouts = { "" };

        // One empty entry: the Phase 8 pick order, which plays no line at all.
        string[] lines = { "" };

        // How many weapon slots fire. Two is the game; one is the game before
        // both slots did, and it is the only honest control for "what is a pair
        // worth" — same code, same layouts, one variable.
        string[] slotArms = { "" };

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("seeds:") && int.TryParse(arg[6..], out int count))
                seeds = seeds[..Mathf.Clamp(count, 1, seeds.Length)];

            if (arg == "zones:on")
                arms = new[] { AnyTier };

            if (arg == "zones:both")
                arms = new[] { NoZone, AnyTier };

            if (arg == "zones:tiers")
                arms = new[] { NoZone, 0, 1 };

            // What the bot carries, as a dimension of the table.
            //
            // **Every balance number this project has printed was taken on the
            // starting kit.** A play-test runs on a fresh ephemeral profile, and
            // a fresh profile owns the Scavenged Rifle and the Combat Knife — so
            // the sweep has never once measured a weapon the player bought, and
            // the dominant build H4b was written against was not something it
            // could have shown. An empty entry is the starting kit, and it stays
            // the default so every table this file has already printed keeps
            // meaning what it meant.
            if (arg.StartsWith("weapons:"))
            {
                string[] named = arg[8..].Split(',');
                var parsed = new System.Collections.Generic.List<string>();
                foreach (string part in named)
                {
                    if (part.Length > 0)
                        parsed.Add(part);
                }

                if (parsed.Count > 0)
                    weapons = parsed.ToArray();
            }

            // Who plays it.
            //
            // `CharacterProbe` asks the design question — is the roster a ladder
            // — by comparing the table against itself. Nothing has ever asked the
            // empirical one: **do three survivors actually produce different
            // runs?** D3b recorded that gap when the roster shipped. Named, not
            // indexed, because `CharacterBook.Order` is hand-written.
            if (arg.StartsWith("characters:"))
            {
                string[] named = arg[11..].Split(',');
                var parsed = new System.Collections.Generic.List<string>();
                foreach (string part in named)
                {
                    if (part.Length > 0)
                        parsed.Add(part);
                }

                if (parsed.Count > 0)
                    characters = parsed.ToArray();
            }

            // What it is wearing. Comma-separated pieces make one loadout;
            // semicolons separate the loadouts to compare.
            //
            // The last dimension the table could not express, and the one that
            // matters most for the questions left open: gear grants rules before
            // the first level-up *and* tilts the deck toward a growth line, so a
            // single piece is both halves of "play this as an Ordnance run".
            if (arg.StartsWith("gear:"))
            {
                string[] named = arg[5..].Split(';');
                var parsed = new System.Collections.Generic.List<string>();
                foreach (string part in named)
                {
                    if (part.Length > 0)
                        parsed.Add(part);
                }

                if (parsed.Count > 0)
                    loadouts = parsed.ToArray();
            }

            // Which growth line the bot plays.
            //
            // Until this existed no number in this file was about a line. Gear
            // and picks tilt what is *offered*; the bot's Phase 8 preference list
            // decided what was *taken*, so "an Ordnance run" was really "a run
            // with a tilted deck and a pick order that had never heard of
            // Ordnance". H4g went looking for the knot rewarding an Ordnance
            // build and could not put one on the field to look at.
            if (arg.StartsWith("lines:"))
            {
                string[] named = arg[6..].Split(',');
                var parsed = new System.Collections.Generic.List<string>();
                foreach (string part in named)
                {
                    if (part.Length > 0)
                        parsed.Add(part);
                }

                if (parsed.Count > 0)
                    lines = parsed.ToArray();
            }

            if (arg == "slots:both")
                slotArms = new[] { "", "solo" };

            if (arg.StartsWith("lingers:"))
            {
                string[] parts = arg[8..].Split(',');
                var parsed = new System.Collections.Generic.List<float>();
                foreach (string part in parts)
                {
                    // `auto` is a tier like any other, carried as a negative so
                    // it can share the array. It is the only one that measures a
                    // decision rather than a duration: the bot stays while the
                    // run is going well and leaves when it stops, which is the
                    // only way a weapon that sells safety can show what it is
                    // worth. See `AutoPlay.StillWorthStaying`.
                    if (part == "auto")
                        parsed.Add(AutoLinger);
                    else if (float.TryParse(part, out float value))
                        parsed.Add(value);
                }

                if (parsed.Count > 0)
                    lingers = parsed.ToArray();
            }
        }

        var rows = new System.Collections.Generic.List<Row>();
        GD.Print($"sweeping {lingers.Length} linger tiers x {seeds.Length} layouts x " +
                 $"{arms.Length} arm(s) x {weapons.Length} weapon(s) x " +
                 $"{characters.Length} survivor(s) x {loadouts.Length} loadout(s) x " +
                 $"{lines.Length} line(s) x {slotArms.Length} slot arm(s) = " +
                 $"{lingers.Length * seeds.Length * arms.Length * weapons.Length * characters.Length * loadouts.Length * lines.Length * slotArms.Length} runs");

        foreach (string slotArm in slotArms)
        foreach (string line in lines)
        foreach (string loadout in loadouts)
        foreach (string character in characters)
        foreach (string weapon in weapons)
        {
            foreach (int arm in arms)
            {
                foreach (float linger in lingers)
                {
                    foreach (ulong seed in seeds)
                    {
                        Row? row = RunOne(linger, seed, arm, weapon, character, loadout, line, slotArm);
                        if (row is { } value)
                            rows.Add(value);
                        else
                            GD.PushError($"  linger {linger:F0} seed {seed} arm {arm} "
                                       + $"weapon {(weapon.Length > 0 ? weapon : "kit")} "
                                       + $"as {(character.Length > 0 ? character : "default")}: no result");
                    }
                }
            }
        }

        Report(rows, lingers);
    }

    /// One child process. `OS.Execute` blocks until it exits, which is what makes
    /// this a sequential sweep rather than twenty Godots fighting over the GPU.
    private static Row? RunOne(float linger, ulong seed, int arm, string weapon, string character,
                               string loadout, string growthLine, string slotArm)
    {
        var output = new Godot.Collections.Array();

        var args = new System.Collections.Generic.List<string>
        {
            "--headless", "--fixed-fps", "60", "--path", ProjectSettings.GlobalizePath("res://"),
            "--script", "test/AutoPlay.cs", "--",
            linger < 0.0f ? "linger:auto" : $"linger:{linger:F0}", $"seed:{seed}",
        };

        if (weapon.Length > 0)
            args.Add($"weapon:{weapon}");

        if (character.Length > 0)
            args.Add($"character:{character}");

        if (loadout.Length > 0)
            args.Add($"gear:{loadout}");

        if (growthLine.Length > 0)
            args.Add($"line:{growthLine}");

        if (slotArm.Length > 0)
            args.Add(slotArm);

        if (arm != NoZone)
        {
            args.Add("--zone");

            // Only a real tier is passed through. `AnyTier` is this file's way of
            // saying "whatever is nearest", which is `AutoPlay`'s default and not
            // something it has a flag for.
            if (arm != AnyTier)
                args.Add($"tier:{arm}");
        }

        // The exit code is ignored on purpose: a death is a failure for the
        // play-test and a data point for this, and treating it as an error here
        // would throw away exactly the rows the table exists to show.
        OS.Execute(OS.GetExecutablePath(), args.ToArray(), output, readStderr: true);

        foreach (Variant line in output)
        {
            foreach (string text in line.AsString().Split('\n'))
            {
                if (text.Contains("SWEEP "))
                    return Parse(text, linger, seed, arm != NoZone);
            }
        }

        return null;
    }

    private static Row Parse(string line, float linger, ulong seed, bool zone)
    {
        string outcome = Field(line, "outcome");
        return new Row(
            zone,
            Mathf.RoundToInt(Number(line, "zoneTier")),
            Mathf.RoundToInt(Number(line, "level")),
            Mathf.RoundToInt(Number(line, "picks")),
            Mathf.RoundToInt(Number(line, "weaponLv")),
            Mathf.RoundToInt(Number(line, "weaponMax")),
            Number(line, "ceilingAt"),
            linger, seed, outcome.Length > 0 ? outcome : "?",
            Number(line, "seconds"),
            Mathf.RoundToInt(Number(line, "banked")),
            Mathf.RoundToInt(Number(line, "lowestHp")),
            Mathf.RoundToInt(Number(line, "peak")),
            Field(line, "weapon"),
            Field(line, "character"),
            Field(line, "gear"),
            Number(line, "knotShare"),
            Field(line, "line"),
            Mathf.RoundToInt(Number(line, "inLine")),
            Mathf.RoundToInt(Number(line, "slots")));
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

            GD.Print($"{(linger < 0.0f ? "auto" : $"{linger:F0}s"),5}   {survived}/{tier.Count,-8}   " +
                     $"{Median(banked),13}   {Median(deaths),12}   {worstPeak,10}   {Median(lowest),16}");
        }

        ReportArms(rows);
        ReportWeapons(rows);
        ReportCharacters(rows);
        ReportLoadouts(rows);
        ReportLines(rows);
        ReportSlots(rows);
        ReportGrowth(rows);

        GD.Print("");
        foreach (Row row in rows)
        {
            GD.Print($"  linger {(row.Linger < 0.0f ? "auto" : $"{row.Linger:F0}s"),4} " +
                     $"seed {row.Seed,-12} {(row.KnotShare > 0.0f ? "knots" : "  ---"),5} " +
                     $"{(row.Gear.Length > 0 ? row.Gear : "kit"),-16} {(row.Zone ? "zone" : "past")} " +
                     $"{row.Outcome,-9} {row.Seconds,6:F1}s  banked {row.Banked,5}  " +
                     $"peak {row.Peak,4}  lowest HP {row.LowestHp,3}");
        }

        // A question nobody asked cannot be answered, and must not be failed.
        //
        // The verdict is about whether the second half of the clock is reachable,
        // and a bot told to leave at 120 s will not reach 180 s however healthy it
        // is. So `lingers:60,120` reported `SWEEP FAILED` every single time —
        // exit code 1 on a table with nothing wrong in it, which is the shape of
        // alarm that teaches whoever reads it next to ignore the verdict line.
        float longest = 0.0f;
        bool auto = false;
        foreach (float linger in lingers)
        {
            longest = Mathf.Max(longest, linger);
            auto |= linger < 0.0f;
        }

        // `auto` has no ceiling of its own short of the clock, so it can reach the
        // target and the question is genuinely being put.
        bool asked = auto || longest >= SurvivalTarget;

        GD.Print("");

        if (!asked)
            GD.Print($"SWEEP OK — the longest linger asked for was {longest:F0}s, so the "
                   + $"{SurvivalTarget:F0}s question was not put");
        else
            GD.Print(reachesTarget
                ? $"SWEEP OK — at least one run reached {SurvivalTarget:F0}s and walked out"
                : $"SWEEP FAILED — nothing reached {SurvivalTarget:F0}s; the second half of the clock is fiction");

        Quit(!asked || reachesTarget ? 0 : 1);
    }

    /// Whether the deck was ever spent.
    ///
    /// A second table rather than more columns on the first, because it answers a
    /// different question. The first says whether a run paid; this says whether
    /// the player got to make the choices the deck exists to offer. A run that
    /// banks well at level 4 out of a deck whose ceilings sum to fifty is a run
    /// that never had a build in it.
    ///
    /// Printed after the loop that fills it, not inside — a table interleaved
    /// with the rows it summarises is a table nobody can read.
    private static void ReportGrowth(System.Collections.Generic.List<Row> rows)
    {
        if (rows.Count == 0)
            return;

        GD.Print("");
        GD.Print("arm      median level   median picks   median weapon lv   reached the ceiling");

        foreach ((string label, bool zone) in new[] { ("past", false), ("zone", true) })
        {
            var levels = new System.Collections.Generic.List<float>();
            var picks = new System.Collections.Generic.List<float>();
            var weapon = new System.Collections.Generic.List<float>();
            int reached = 0, count = 0, max = 0;

            foreach (Row row in rows)
            {
                if (row.Zone != zone)
                    continue;

                count++;
                levels.Add(row.Level);
                picks.Add(row.Picks);
                weapon.Add(row.WeaponLevel);
                max = Mathf.Max(max, row.WeaponMax);

                if (row.CeilingAt >= 0.0f)
                    reached++;
            }

            if (count == 0)
                continue;

            GD.Print($"{label,-8} {Median(levels),13}   {Median(picks),12}   " +
                     $"{Median(weapon),16}   {reached}/{count} (of {max})");
        }
    }

    /// What a danger zone costs and what it pays, as a difference.
    ///
    /// Printed only when both arms ran. A single arm's median is a number about
    /// this bot on these seeds; the gap between two arms on the *same* seeds is a
    /// statement about the design, and it is the only one of the two worth
    /// tuning against.
    private static void ReportArms(System.Collections.Generic.List<Row> rows)
    {
        var groups = new System.Collections.Generic.List<(string Label, System.Collections.Generic.List<Row> Rows)>();

        void Collect(string label, System.Func<Row, bool> belongs)
        {
            var picked = new System.Collections.Generic.List<Row>();
            foreach (Row row in rows)
            {
                if (belongs(row))
                    picked.Add(row);
            }

            if (picked.Count > 0)
                groups.Add((label, picked));
        }

        Collect("past", row => !row.Zone);

        // Grouped by the tier the run actually reached, so a seed with no tier-1
        // zone lands in the tier-0 row rather than diluting the one it was asked
        // for. This is also why the counts per row need not be equal, and why the
        // count is printed.
        Collect("tier 0", row => row.Zone && row.ZoneTier == 0);
        Collect("tier 1", row => row.Zone && row.ZoneTier == 1);
        Collect("zone ?", row => row.Zone && row.ZoneTier < 0);

        PrintGroups("arm", groups);
    }

    /// One row per weapon the bot actually carried.
    ///
    /// Grouped by what `AutoPlay` reported rather than by what the sweep asked
    /// for, so a run that could not equip what it was given lands under the kit
    /// it really used. Exactly the rule `zoneTier` exists to follow, and for
    /// exactly the same reason: a fallback in the wrong column is a table that is
    /// wrong in the way the flag was added to fix.
    private static void ReportWeapons(System.Collections.Generic.List<Row> rows)
    {
        var seen = new System.Collections.Generic.List<string>();
        foreach (Row row in rows)
        {
            if (row.Weapon.Length > 0 && !seen.Contains(row.Weapon))
                seen.Add(row.Weapon);
        }

        // One weapon is the ordinary table, and it does not need a breakdown of
        // itself.
        if (seen.Count < 2)
            return;

        seen.Sort(System.StringComparer.Ordinal);

        var groups = new System.Collections.Generic.List<(string, System.Collections.Generic.List<Row>)>();

        foreach (string name in seen)
        {
            var picked = new System.Collections.Generic.List<Row>();
            foreach (Row row in rows)
            {
                if (row.Weapon == name)
                    picked.Add(row);
            }

            groups.Add((name, picked));
        }

        PrintGroups("weapon", groups);
    }

    /// One row per survivor the run was actually played as.
    ///
    /// `CharacterProbe` asserts the design — nothing is strictly better than the
    /// Drifter, and every difference is at least fifteen per cent — by reading the
    /// table. That answers "is this a ladder" and cannot answer "do these produce
    /// different runs", because a resource comparing favourably with another
    /// resource is not a run. This is the arm D3b asked for when the roster
    /// shipped.
    private static void ReportCharacters(System.Collections.Generic.List<Row> rows)
    {
        var seen = new System.Collections.Generic.List<string>();
        foreach (Row row in rows)
        {
            if (row.Character.Length > 0 && !seen.Contains(row.Character))
                seen.Add(row.Character);
        }

        if (seen.Count < 2)
            return;

        seen.Sort(System.StringComparer.Ordinal);

        var groups = new System.Collections.Generic.List<(string, System.Collections.Generic.List<Row>)>();

        foreach (string name in seen)
        {
            var picked = new System.Collections.Generic.List<Row>();
            foreach (Row row in rows)
            {
                if (row.Character == name)
                    picked.Add(row);
            }

            groups.Add((name, picked));
        }

        PrintGroups("survivor", groups);
    }

    /// One row per loadout the run actually wore.
    private static void ReportLoadouts(System.Collections.Generic.List<Row> rows)
    {
        var seen = new System.Collections.Generic.List<string>();
        foreach (Row row in rows)
        {
            if (row.Gear.Length > 0 && !seen.Contains(row.Gear))
                seen.Add(row.Gear);
        }

        if (seen.Count < 2)
            return;

        seen.Sort(System.StringComparer.Ordinal);

        var groups = new System.Collections.Generic.List<(string, System.Collections.Generic.List<Row>)>();

        foreach (string name in seen)
        {
            var picked = new System.Collections.Generic.List<Row>();
            foreach (Row row in rows)
            {
                if (row.Gear == name)
                    picked.Add(row);
            }

            groups.Add((name, picked));
        }

        PrintGroups("loadout", groups);
    }

    /// One row per growth line the bot played, with how much of the deck went in.
    private static void ReportLines(System.Collections.Generic.List<Row> rows)
    {
        var seen = new System.Collections.Generic.List<string>();
        foreach (Row row in rows)
        {
            if (row.Line.Length > 0 && !seen.Contains(row.Line))
                seen.Add(row.Line);
        }

        if (seen.Count < 2)
            return;

        seen.Sort(System.StringComparer.Ordinal);

        var groups = new System.Collections.Generic.List<(string, System.Collections.Generic.List<Row>)>();

        foreach (string name in seen)
        {
            var picked = new System.Collections.Generic.List<Row>();
            var inLine = new System.Collections.Generic.List<float>();

            foreach (Row row in rows)
            {
                if (row.Line != name)
                    continue;

                picked.Add(row);
                inLine.Add(row.PicksInLine);
            }

            // The label carries the evidence that the line was actually played,
            // so a reader cannot take the row for a build without seeing it.
            groups.Add(($"{name} ({Median(inLine)} picks)", picked));
        }

        PrintGroups("line", groups);
    }

    /// One row per number of weapons firing — the measurement the dual-wield
    /// change exists to produce.
    private static void ReportSlots(System.Collections.Generic.List<Row> rows)
    {
        var groups = new System.Collections.Generic.List<(string, System.Collections.Generic.List<Row>)>();

        foreach (int count in new[] { 1, 2 })
        {
            var picked = new System.Collections.Generic.List<Row>();
            foreach (Row row in rows)
            {
                if (row.Slots == count)
                    picked.Add(row);
            }

            if (picked.Count > 0)
                groups.Add(($"{count} weapon{(count == 1 ? "" : "s")}", picked));
        }

        PrintGroups("firing", groups);
    }

    /// The one table shape this file prints, so an arm and a weapon are read the
    /// same way.
    private static void PrintGroups(
        string heading,
        System.Collections.Generic.List<(string Label, System.Collections.Generic.List<Row> Rows)> groups)
    {
        if (groups.Count < 2)
            return;

        GD.Print("");
        GD.Print($"{heading,-16} survived   median banked   median seconds   median lowest HP   worst peak");

        foreach ((string label, System.Collections.Generic.List<Row> group) in groups)
        {
            int survived = 0, worstPeak = 0;
            var banked = new System.Collections.Generic.List<float>();
            var seconds = new System.Collections.Generic.List<float>();
            var lowest = new System.Collections.Generic.List<float>();

            foreach (Row row in group)
            {
                worstPeak = Mathf.Max(worstPeak, row.Peak);
                lowest.Add(row.LowestHp);
                seconds.Add(row.Seconds);
                if (row.Survived)
                {
                    survived++;
                    banked.Add(row.Banked);
                }
            }

            GD.Print($"{label,-16} {survived}/{group.Count,-8}   {Median(banked),13}   " +
                     $"{Median(seconds),14}   {Median(lowest),16}   {worstPeak,10}");
        }
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
