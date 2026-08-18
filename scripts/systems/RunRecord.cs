using Godot;

/// What one run turned out to be, frozen at the moment it ended.
///
/// One record, two readers: the debrief screen tells it back to the player and
/// the contract check asks whether it satisfies a condition. Those had to be the
/// same numbers — a contract that counts kills differently from the screen that
/// reports them is a contract the player will believe is broken, and they will be
/// right about the disagreement even when both halves are individually correct.
///
/// Immutable by construction. It is assembled once, after the run is over, from a
/// RunLog that was watching the whole time.
public sealed class RunRecord
{
    public RunState Outcome { get; init; } = RunState.Running;
    public float Seconds { get; init; }

    /// Credits actually banked, multiplier already applied.
    public int Banked { get; init; }

    /// The multiplier that earned it. Reported next to the payout because "you
    /// got 513" and "you got 238 and doubled it by staying" are different
    /// sentences about the same number, and only the second one teaches anything.
    public float Multiplier { get; init; }

    public int BackpackValue { get; init; }
    public int SafeBoxValue { get; init; }

    /// Indexed by variant, in the horde's table order.
    public int[] KillsByType { get; init; } = System.Array.Empty<int>();

    public int Kills
    {
        get
        {
            int total = 0;
            foreach (int count in KillsByType)
                total += count;
            return total;
        }
    }

    public int CratesLooted { get; init; }
    public int LootValue { get; init; }

    /// The closest the player came to dying. On a survived run this is the whole
    /// story of how close it was, and nothing else in the record carries it.
    public float LowestHealth { get; init; }
    public float MaxHealth { get; init; }

    public int ItemsUsed { get; init; }
    public int ItemsThrown { get; init; }

    /// Practice banked by this run, indexed by WeaponCategory.
    public int[] ProficiencyGained { get; init; } = new int[4];

    /// Resource paths death took. Empty on an extraction, by the rule that only
    /// dying costs the kit.
    public string[] LostEquipment { get; init; } = System.Array.Empty<string>();

    public bool Survived => Outcome == RunState.Extracted;

    public int ProficiencyTotal
    {
        get
        {
            int total = 0;
            foreach (int gained in ProficiencyGained)
                total += gained;
            return total;
        }
    }
}
