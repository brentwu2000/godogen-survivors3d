using Godot;

/// Rolls the three jobs on offer at the base.
///
/// The offer is a pure function of a seed, so the profile stores one number and
/// an index rather than three serialised objects — and the same seed shows the
/// same three cards after a restart, which is what stops a reroll from being
/// free by way of the main menu.
public static class ContractBook
{
    public const int OfferSize = 3;

    /// What a reroll costs. Free rerolls mean the player spins until the easiest
    /// card appears, and a contract nobody had to weigh is a delayed handout
    /// rather than a decision.
    public const int RerollCost = 60;

    /// The templates, with their pay. Rewards are set against a typical run
    /// banking somewhere around 300-500: enough that taking one matters, never
    /// so much that the run itself becomes the side quest.
    private static readonly Contract[] Templates =
    {
        new(ContractKind.BankValue, 0, 400, 150),
        new(ContractKind.BankValue, 0, 700, 280),
        new(ContractKind.KillTotal, 0, 120, 140),
        new(ContractKind.KillTotal, 0, 260, 260),
        new(ContractKind.LootCrates, 0, 3, 130),
        new(ContractKind.LootCrates, 0, 5, 240),
        new(ContractKind.SurviveSeconds, 0, 150, 180),
        new(ContractKind.SurviveSeconds, 0, 240, 320),
        new(ContractKind.ThrowItems, 0, 2, 160),
        new(ContractKind.NoConsumables, 0, 0, 220),

        // Variant hunts. The brute and the bloater are worth more because they
        // arrive late, which means holding out to meet the count.
        new(ContractKind.KillVariant, 1, 40, 170),   // runner
        new(ContractKind.KillVariant, 2, 12, 260),   // brute
        new(ContractKind.KillVariant, 3, 10, 240),   // bloater
        new(ContractKind.KillVariant, 4, 20, 200),   // spitter

        // The only kind that pays for leaving early. Deliberately well paid: it
        // has to be worth giving up the multiplier, or it is not a trade.
        new(ContractKind.ExtractBefore, 0, 90, 250),
        new(ContractKind.ExtractBefore, 0, 150, 170),
    };

    /// Three distinct jobs, with at most one of them fighting the clock.
    ///
    /// That cap is the rule this whole system lives or dies by. "Multiply what
    /// you are carrying by staying" is the run's central tension, and an offer
    /// that is entirely "leave early" quietly replaces it with a schedule. One
    /// such card is a choice; three are an instruction.
    public static Contract[] Roll(ulong seed)
    {
        ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        var chosen = new System.Collections.Generic.List<Contract>(OfferSize);
        var takenTemplates = new System.Collections.Generic.HashSet<int>();
        var takenKinds = new System.Collections.Generic.HashSet<ContractKind>();
        bool clockTaken = false;

        // Bounded rather than "until we have three": a pool that cannot satisfy
        // the constraint would otherwise spin forever, and an offer of two is a
        // better failure than a hang.
        for (int attempt = 0; attempt < 200 && chosen.Count < OfferSize; attempt++)
        {
            int index = (int)(Next(ref state) * Templates.Length);
            index = Mathf.Clamp(index, 0, Templates.Length - 1);

            if (!takenTemplates.Add(index))
                continue;

            Contract contract = Templates[index];

            // One of each kind. Two rows of "extract with N banked" at different
            // thresholds is a legitimate difficulty trade on paper and reads as a
            // bug on screen — three cards should be three questions, not one
            // question asked at three prices.
            if (!takenKinds.Add(contract.Kind))
                continue;

            if (contract.PressuresTheClock)
            {
                if (clockTaken)
                    continue;
                clockTaken = true;
            }

            chosen.Add(contract);
        }

        return chosen.ToArray();
    }

    private static float Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return (state >> 40) / 16777216.0f;
    }
}
