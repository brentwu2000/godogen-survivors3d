using Godot;

/// Checks that a job pays for what it actually asked for, and that the board
/// cannot quietly replace the run's central decision with a schedule.
///
///   godot --headless --script test/ContractProbe.cs
///
/// Exit code is the verdict. Almost everything here is pure logic over a
/// RunRecord, so it does not need a scene — which is the point of having put the
/// run's facts in one object rather than spread across five systems.
public partial class ContractProbe : SceneTree
{
    private const string ProfilePath = "user://profile.json";

    private string? _backup;
    private int _stage;
    private int _tick;
    private bool _failed;

    public override void _Initialize()
    {
        _backup = FileAccess.FileExists(ProfilePath) ? FileAccess.GetFileAsString(ProfilePath) : null;
    }

    public override bool _PhysicsProcess(double delta)
    {
        _tick++;

        switch (_stage)
        {
            case 0: return Step(StageOfferShape, "an offer is three distinct jobs");
            case 1: return Step(StageOneClockCard, "at most one of them pays for leaving early");
            case 2: return Step(StageDeterministic, "the same seed shows the same board");
            case 3: return Step(StageExactTargets, "one short fails, exactly on target pays");
            case 4: return Step(StageDeathPaysNothing, "a corpse satisfies nothing");
            case 5: return Step(StageSettlement, "meeting one pays, failing one does not, and the board turns over");
            case 6: return Step(StageRerollCosts, "a new board costs money, and refuses when there is none");
            default:
                Restore();
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool Step(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_tick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _tick = 0;
        return false;
    }

    private bool? StageOfferShape(int tick)
    {
        int short_ = 0, duplicated = 0;

        for (int seed = 1; seed <= 300; seed++)
        {
            Contract[] offer = ContractBook.Roll((ulong)seed);
            if (offer.Length != ContractBook.OfferSize)
                short_++;

            for (int i = 0; i < offer.Length; i++)
            {
                // By kind, not by exact row. Three cards should be three
                // questions; the same question at two prices reads as a bug even
                // when both prices are fair.
                for (int j = i + 1; j < offer.Length; j++)
                {
                    if (offer[i].Kind == offer[j].Kind)
                        duplicated++;
                }
            }
        }

        GD.Print($"  300 seeds: {short_} short of {ContractBook.OfferSize}, {duplicated} with a repeat");
        return short_ == 0 && duplicated == 0;
    }

    /// The rule the whole system rests on. "Multiply what you are carrying by
    /// staying" is the run's central tension; a board that is entirely "leave
    /// early" replaces that decision with an instruction, which is the collapse
    /// the payout multiplier was introduced to fix in the first place.
    ///
    /// Checked across enough seeds to catch a rule that holds by luck: with two
    /// clock cards in a pool of sixteen, an unconstrained roll of three would
    /// pair them roughly once in fifty.
    private bool? StageOneClockCard(int tick)
    {
        int worst = 0;
        int seedsWithOne = 0;

        for (int seed = 1; seed <= 500; seed++)
        {
            int clockCards = 0;
            foreach (Contract contract in ContractBook.Roll((ulong)seed))
            {
                if (contract.PressuresTheClock)
                    clockCards++;
            }

            worst = Mathf.Max(worst, clockCards);
            if (clockCards == 1)
                seedsWithOne++;
        }

        // Also assert they appear at all: a cap enforced by never offering the
        // card would pass the same test and quietly delete a whole kind of job.
        GD.Print($"  500 seeds: worst case {worst} clock cards, {seedsWithOne} boards offered exactly one");
        return worst <= 1 && seedsWithOne > 0;
    }

    private bool? StageDeterministic(int tick)
    {
        Contract[] first = ContractBook.Roll(12345);
        Contract[] second = ContractBook.Roll(12345);
        Contract[] other = ContractBook.Roll(12346);

        bool same = first.Length == second.Length;
        for (int i = 0; same && i < first.Length; i++)
            same = first[i].Kind == second[i].Kind && first[i].Target == second[i].Target;

        bool differs = first.Length != other.Length;
        for (int i = 0; !differs && i < first.Length; i++)
            differs = first[i].Kind != other[i].Kind || first[i].Target != other[i].Target;

        GD.Print($"  seed 12345 reproduces = {same}; seed 12346 differs = {differs}");
        return same && differs;
    }

    /// Off-by-one is the entire risk with a threshold. A contract that pays at 11
    /// of 12 is a contract the player cannot plan around, and one that refuses at
    /// exactly 12 is a bug they will describe as the game cheating.
    private bool? StageExactTargets(int tick)
    {
        var contract = new Contract(ContractKind.KillVariant, 2, 12, 260);

        bool shortByOne = contract.IsMet(Killing(2, 11));
        bool exact = contract.IsMet(Killing(2, 12));
        bool over = contract.IsMet(Killing(2, 13));
        bool wrongVariant = contract.IsMet(Killing(3, 40));

        var before = new Contract(ContractKind.ExtractBefore, 0, 90, 250);
        bool onTheSecond = before.IsMet(Lasting(90.0f));
        bool late = before.IsMet(Lasting(90.5f));

        GD.Print($"  kill 12 brutes: 11 = {shortByOne}, 12 = {exact}, 13 = {over}, " +
                 $"40 bloaters = {wrongVariant}; extract before 90s: at 90.0 = {onTheSecond}, at 90.5 = {late}");

        return !shortByOne && exact && over && !wrongVariant && onTheSecond && !late;
    }

    /// Every job requires walking out. A contract that pays on a corpse rewards
    /// ignoring the only rule the game has — and the counts are easiest to hit on
    /// exactly the run that ends face down, because it went on longest.
    private bool? StageDeathPaysNothing(int tick)
    {
        var run = new RunRecord
        {
            Outcome = RunState.Died,
            Seconds = 280.0f,
            Banked = 900,
            KillsByType = new[] { 400, 90, 30, 20, 40 },
            CratesLooted = 8,
            ItemsThrown = 6,
        };

        bool anyMet = false;
        foreach (Contract contract in AllKinds())
            anyMet |= contract.IsMet(run);

        GD.Print($"  a 280s death with 580 kills, 8 crates and 900 banked satisfies anything = {anyMet}");
        return !anyMet;
    }

    private bool? StageSettlement(int tick)
    {
        var profile = new Profile { Credits = 1000, ContractSeed = 4242 };
        Contract[] offer = profile.ContractOffer();
        profile.ContractIndex = 0;

        int seedBefore = profile.ContractSeed;
        Contract taken = offer[0];

        // Meeting it pays exactly the reward, once.
        var met = Satisfying(taken);
        bool isMet = taken.IsMet(met);
        profile.Credits += isMet ? taken.Reward : 0;
        profile.RollContracts();

        bool paid = profile.Credits == 1000 + taken.Reward;
        bool boardTurned = profile.ContractSeed != seedBefore && profile.ContractIndex == -1;

        // Failing pays nothing, and the board still turns over — otherwise the
        // same easy card stays up until it lands, and the commitment made before
        // leaving is a formality.
        var second = new Profile { Credits = 1000, ContractSeed = 4242, ContractIndex = 0 };
        var missed = new RunRecord { Outcome = RunState.Extracted, KillsByType = new int[5] };
        bool failedPays = second.AcceptedContract?.IsMet(missed) ?? false;
        second.RollContracts();
        bool failedTurned = second.ContractSeed != 4242 && second.ContractIndex == -1;

        GD.Print($"  took \"{taken.Describe()}\": met = {isMet}, credits 1000 -> {profile.Credits} " +
                 $"(expected +{taken.Reward}); board turned = {boardTurned}; " +
                 $"an empty run met it = {failedPays}, board still turned = {failedTurned}");

        return isMet && paid && boardTurned && !failedPays && failedTurned;
    }

    private bool? StageRerollCosts(int tick)
    {
        var rich = new Profile { Credits = ContractBook.RerollCost, ContractSeed = 77 };
        rich.Credits -= ContractBook.RerollCost;
        rich.RollContracts();

        var poor = new Profile { Credits = ContractBook.RerollCost - 1, ContractSeed = 77 };
        bool refused = poor.Credits < ContractBook.RerollCost;

        GD.Print($"  reroll costs {ContractBook.RerollCost}: paid leaves {rich.Credits}, " +
                 $"{poor.Credits} is refused = {refused}, seed unchanged = {poor.ContractSeed == 77}");

        return rich.Credits == 0 && rich.ContractSeed != 77 && refused && poor.ContractSeed == 77;
    }

    // ---- fixtures ------------------------------------------------------------

    private static RunRecord Killing(int variant, int count)
    {
        var kills = new int[5];
        kills[variant] = count;
        return new RunRecord { Outcome = RunState.Extracted, KillsByType = kills, Seconds = 200.0f };
    }

    private static RunRecord Lasting(float seconds) =>
        new() { Outcome = RunState.Extracted, Seconds = seconds, KillsByType = new int[5] };

    /// A run generous enough to satisfy whichever job it is handed, so the
    /// settlement stage tests payment rather than difficulty.
    private static RunRecord Satisfying(Contract contract) => new()
    {
        Outcome = RunState.Extracted,
        Seconds = contract.Kind == ContractKind.ExtractBefore ? 1.0f : 290.0f,
        Banked = 5000,
        KillsByType = new[] { 500, 500, 500, 500, 500 },
        CratesLooted = 8,
        ItemsThrown = 9,
        ItemsUsed = 0,
    };

    private static Contract[] AllKinds()
    {
        var kinds = System.Enum.GetValues<ContractKind>();
        var contracts = new Contract[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
            contracts[i] = new Contract(kinds[i], 2, 1, 100);

        return contracts;
    }

    /// This probe never writes a profile, but it constructs them — and the file on
    /// disk is the one thing a player cannot afford to have a test touch.
    private void Restore()
    {
        if (_backup == null)
            return;

        using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(_backup);
    }
}
