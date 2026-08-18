using Godot;

public enum ContractKind
{
    /// Bank at least this much on one extraction.
    BankValue,

    /// Kill this many of one variant. Target variant is the contract's Subject.
    KillVariant,

    KillTotal,

    /// Empty this many crates and get out with it.
    LootCrates,

    /// Still alive and extracting this late. Pulls the same direction as the
    /// payout multiplier, so it costs the player nothing to also want it.
    SurviveSeconds,

    /// Out before the clock reaches this. The only kind that pulls against the
    /// multiplier, which is why at most one may appear in an offer.
    ExtractBefore,

    /// Extract without spending anything on staying alive.
    NoConsumables,

    /// Use the backpack as a weapon this many times.
    ThrowItems,
}

/// One job, taken at the base and judged from the run's record.
///
/// This is the only thing in the game that asks the player to play *differently*.
/// Everything else — better gear, more practice, a longer growth curve — asks
/// them to play the same run better. That difference is what separates the
/// twentieth run from the fifth.
public readonly struct Contract
{
    public readonly ContractKind Kind;

    /// What the target counts. A variant index for KillVariant, otherwise unused.
    public readonly int Subject;

    public readonly int Target;
    public readonly int Reward;

    public Contract(ContractKind kind, int subject, int target, int reward)
    {
        Kind = kind;
        Subject = subject;
        Target = target;
        Reward = reward;
    }

    /// Whether a contract fights the reason to stay. Escalation already pays for
    /// lingering, and a job that pays for leaving early is a real trade — but
    /// three of them in one offer would make leaving early simply correct, which
    /// is exactly the collapse the payout multiplier was introduced to fix.
    public bool PressuresTheClock => Kind == ContractKind.ExtractBefore;

    /// Every job requires walking out. A contract that pays on a corpse is a
    /// contract that rewards ignoring the only rule the game has.
    public bool IsMet(RunRecord run) => run.Survived && Kind switch
    {
        ContractKind.BankValue => run.Banked >= Target,
        ContractKind.KillVariant => Subject >= 0 && Subject < run.KillsByType.Length
                                    && run.KillsByType[Subject] >= Target,
        ContractKind.KillTotal => run.Kills >= Target,
        ContractKind.LootCrates => run.CratesLooted >= Target,
        ContractKind.SurviveSeconds => run.Seconds >= Target,
        ContractKind.ExtractBefore => run.Seconds <= Target,
        ContractKind.NoConsumables => run.ItemsUsed == 0,
        ContractKind.ThrowItems => run.ItemsThrown >= Target,
        _ => false,
    };

    public string Describe(RunLog? log = null) => Kind switch
    {
        ContractKind.BankValue => $"extract with {Target} banked",
        ContractKind.KillVariant => $"kill {Target} {VariantName(log)}",
        ContractKind.KillTotal => $"kill {Target} and get out",
        ContractKind.LootCrates => $"empty {Target} crates and get out",
        ContractKind.SurviveSeconds => $"extract after {Target}s",
        ContractKind.ExtractBefore => $"extract before {Target}s",
        ContractKind.NoConsumables => "extract without using an item",
        ContractKind.ThrowItems => $"throw {Target} items and get out",
        _ => Kind.ToString(),
    };

    /// How far along the run got, for the debrief. A contract that says only
    /// "failed" teaches nothing; one that says "9 of 12" says which way to lean
    /// next time.
    public string Progress(RunRecord run) => Kind switch
    {
        ContractKind.BankValue => $"{run.Banked}/{Target}",
        ContractKind.KillVariant => Subject >= 0 && Subject < run.KillsByType.Length
            ? $"{run.KillsByType[Subject]}/{Target}"
            : $"0/{Target}",
        ContractKind.KillTotal => $"{run.Kills}/{Target}",
        ContractKind.LootCrates => $"{run.CratesLooted}/{Target}",
        ContractKind.SurviveSeconds => $"{run.Seconds:F0}s/{Target}s",
        ContractKind.ExtractBefore => $"{run.Seconds:F0}s vs {Target}s",
        ContractKind.NoConsumables => run.ItemsUsed == 0 ? "none used" : $"{run.ItemsUsed} used",
        ContractKind.ThrowItems => $"{run.ItemsThrown}/{Target}",
        _ => "",
    };

    private string VariantName(RunLog? log) => log?.TypeName(Subject) ?? Subject.ToString();
}
