using Godot;

/// Tells the run back to the player before handing them to the shop.
///
/// Until this existed, a three-hundred-second run collapsed into a two-line
/// banner and three and a half seconds of waiting. Everything the run produced —
/// what it killed, what it taught, what it cost, whether it beat the last one —
/// went to the console. The player did the work and the log file got the report.
///
/// It sits over the finished arena rather than replacing it. The horde is still
/// walking in behind the panel, which is the correct picture: the run ended
/// because the player left, not because the world did.
///
/// Advanced by a key, never by a timer. This is the one moment in the loop that
/// should not be on a clock, and a screen that dismisses itself is a screen the
/// player learns to ignore.
public partial class DebriefScreen : CanvasLayer
{
    private ColorRect _panel = null!;
    private Label _title = null!;
    private Label _body = null!;
    private Label _footer = null!;

    private static readonly Color Ink = new(0.96f, 0.95f, 0.90f);
    private static readonly Color Good = new(0.42f, 0.78f, 0.36f);
    private static readonly Color Bad = new(0.88f, 0.30f, 0.24f);

    private bool _showing;

    /// A frame of grace before a key can dismiss it.
    ///
    /// The player is holding movement and very likely firing at the instant the
    /// run ends, and `ui_accept` shares the space bar with `fire` — without this
    /// the screen appears and vanishes in the same breath, which reads as it
    /// never having appeared at all.
    private float _armIn;

    public override void _Ready()
    {
        _panel = GetNode<ColorRect>("Panel");
        _title = GetNode<Label>("Title");
        _body = GetNode<Label>("Body");
        _footer = GetNode<Label>("Footer");

        _panel.Color = new Color(0.05f, 0.06f, 0.08f, 0.90f);
        Style(_title, 46, HorizontalAlignment.Center);
        Style(_body, 22, HorizontalAlignment.Left);
        Style(_footer, 20, HorizontalAlignment.Center);

        Visible = false;
    }

    private static void Style(Label label, int size, HorizontalAlignment alignment)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Ink);
        label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 5);
        label.HorizontalAlignment = alignment;
    }

    public void Show(MetaManager meta, RunLog? log)
    {
        RunRecord? run = meta.LastRun;
        if (run == null)
            return;

        _showing = true;
        _armIn = 0.35f;
        Visible = true;

        // The run's readout goes away with the run. Left up, its banner shows
        // through this panel saying the same thing in the same words, and the
        // level-up cards sit under a report about a run that is already over —
        // both of which read as the interface having failed to notice.
        Node? hud = GetParent()?.GetNodeOrNull("Hud");
        if (hud is CanvasLayer layer)
            layer.Visible = false;

        _title.Text = run.Outcome switch
        {
            RunState.Extracted => "EXTRACTED",
            RunState.Died => "KILLED",
            RunState.TimedOut => "OUT OF TIME",
            _ => "RUN OVER",
        };
        _title.AddThemeColorOverride("font_color", run.Survived ? Good : Bad);

        _body.Text = Compose(meta, run, log);
        _footer.Text = "[Enter] back to base";
    }

    /// Polled, not event-driven. `Input.ActionPress` moves the poll state without
    /// entering the event pipeline, so a screen built on `_UnhandledInput` is one
    /// no probe and no play-test script can press a key on — which is exactly how
    /// the first base screen shipped, and exactly what the loop probe caught.
    public override void _Process(double delta)
    {
        if (!_showing)
            return;

        if (_armIn > 0.0f)
        {
            _armIn -= (float)delta;
            return;
        }

        if (!Input.IsActionJustPressed("ui_accept") && !Input.IsActionJustPressed("interact"))
            return;

        _showing = false;
        if (IsInsideTree())
            GetTree().ChangeSceneToFile("res://scenes/Base.tscn");
    }

    private static string Compose(MetaManager meta, RunRecord run, RunLog? log)
    {
        var text = new System.Text.StringBuilder();
        Profile profile = meta.Profile;

        // The payout and the multiplier that earned it, on one line. "You got
        // 513" and "you got 238 and doubled it by staying" are different
        // sentences about the same number, and only the second one is a lesson.
        text.AppendLine(run.Survived
            ? $"banked {run.Banked}      carried {run.BackpackValue + run.SafeBoxValue} x{run.Multiplier:F2} for lasting {run.Seconds:F0}s"
            : $"banked {run.Banked}      the backpack ({run.BackpackValue}) is gone; the safe box ({run.SafeBoxValue}) is not");

        text.AppendLine($"credits {profile.Credits}      streak {profile.Streak}      " +
                        $"runs {profile.RunsSurvived} out / {profile.RunsLost} lost");
        text.AppendLine();

        text.AppendLine($"killed {run.Kills}{KillBreakdown(run, log)}");
        text.AppendLine($"searched {run.CratesLooted} crates for {run.LootValue}      " +
                        $"used {run.ItemsUsed}, threw {run.ItemsThrown}      " +
                        $"lowest health {run.LowestHealth:F0}/{run.MaxHealth:F0}");

        // Practice was always meant to be a line on this screen (it is why it was
        // moved to a once-per-run settlement at all). It had only ever been a
        // console print.
        if (run.ProficiencyTotal > 0)
            text.AppendLine($"practice{Practice(run, profile)}");

        if (run.LostEquipment.Length > 0)
            text.AppendLine($"lost: {Names(run.LostEquipment)}");

        text.AppendLine();
        text.AppendLine(ContractLine(meta, run, log));

        string records = Records(meta.LastRecordsBeaten, run);
        if (records.Length > 0)
            text.AppendLine(records);

        // Last, and repeating the condition that opened it. A player who reads
        // "unlocked: Thorns" learns that Thorns exists; one who reads what they
        // did to get it learns what this game rewards, which is the only part
        // that changes what they do next.
        foreach (Unlock unlock in meta.NewUnlocks)
            text.AppendLine($"unlocked {unlock.Name} — {unlock.Condition.ToLower()}");

        return text.ToString();
    }

    private static string KillBreakdown(RunRecord run, RunLog? log)
    {
        if (log == null || run.KillsByType.Length == 0)
            return "";

        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < run.KillsByType.Length; i++)
        {
            if (run.KillsByType[i] > 0)
                parts.Add($"{log.TypeName(i)} {run.KillsByType[i]}");
        }

        return parts.Count > 0 ? $"      {string.Join("   ", parts)}" : "";
    }

    private static string Practice(RunRecord run, Profile profile)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < run.ProficiencyGained.Length; i++)
        {
            if (run.ProficiencyGained[i] > 0)
                parts.Add($"{(WeaponCategory)i} +{run.ProficiencyGained[i]} (now {profile.Proficiency[i]})");
        }

        return parts.Count > 0 ? $"      {string.Join("   ", parts)}" : "";
    }

    /// Failed jobs report how far the run got rather than only that it failed.
    /// "9 of 12" says which way to lean next time; "failed" says nothing.
    private static string ContractLine(MetaManager meta, RunRecord run, RunLog? log)
    {
        if (meta.ContractTaken is not { } contract)
            return "no contract taken";

        return meta.ContractMet
            ? $"CONTRACT MET — {contract.Describe(log)}   +{contract.Reward}"
            : $"contract failed — {contract.Describe(log)}   ({contract.Progress(run)})";
    }

    private static string Records(Profile.RecordsBeaten beaten, RunRecord run)
    {
        if (!beaten.Any)
            return "";

        var parts = new System.Collections.Generic.List<string>();
        if (beaten.Bank) parts.Add($"banked {run.Banked}");
        if (beaten.Kills) parts.Add($"killed {run.Kills}");
        if (beaten.Seconds) parts.Add($"lasted {run.Seconds:F0}s");
        if (beaten.Multiplier) parts.Add($"multiplier x{run.Multiplier:F2}");
        if (beaten.Streak) parts.Add("longest streak");

        return $"NEW BEST — {string.Join(", ", parts)}";
    }

    private static string Names(string[] paths)
    {
        var names = new string[paths.Length];
        for (int i = 0; i < paths.Length; i++)
            names[i] = paths[i].GetFile().GetBaseName().Replace('_', ' ');

        return string.Join(", ", names);
    }
}
