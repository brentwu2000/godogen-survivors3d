using Godot;

/// Run readout: clock, health, backpack, and whatever hold-to-act prompt is
/// currently relevant.
///
/// The builder supplies the node structure; styling and content live here, so
/// tuning the readout does not mean regenerating a scene.
public partial class Hud : CanvasLayer
{
    private Label _status = null!;
    private Label _prompt = null!;
    private Label _banner = null!;

    private RunDirector? _director;
    private Player? _player;
    private Horde? _horde;
    private ExtractionZone? _extraction;
    private MetaManager? _meta;
    private RunGrowth? _growth;
    private WeaponHandler? _weapons;
    private LootContainer[] _containers = System.Array.Empty<LootContainer>();

    public override void _Ready()
    {
        _status = GetNode<Label>("Status");
        _prompt = GetNode<Label>("Prompt");
        _banner = GetNode<Label>("Banner");

        Style(_status, 20, HorizontalAlignment.Left);
        Style(_prompt, 24, HorizontalAlignment.Center);
        Style(_banner, 44, HorizontalAlignment.Center);
        _banner.Visible = false;

        Node? root = GetParent();
        _director = root?.GetNodeOrNull<RunDirector>("RunDirector");
        _player = root?.GetNodeOrNull<Player>("Player");
        _horde = root?.GetNodeOrNull<Horde>("Horde");
        _extraction = root?.GetNodeOrNull<ExtractionZone>("ExtractionZone");
        _meta = root?.GetNodeOrNull<MetaManager>("MetaManager");
        _growth = root?.GetNodeOrNull<RunGrowth>("RunGrowth");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        var found = new System.Collections.Generic.List<LootContainer>();
        Node? crates = root?.GetNodeOrNull("LootContainers");
        if (crates != null)
        {
            foreach (Node child in crates.GetChildren())
            {
                if (child is LootContainer container)
                    found.Add(container);
            }
        }
        _containers = found.ToArray();

        if (_director != null)
            _director.RunEnded += OnRunEnded;
    }

    private static void Style(Label label, int size, HorizontalAlignment alignment)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(1.0f, 0.97f, 0.9f));

        // An outline rather than a panel: the readout has to stay legible over
        // both the pale ground and a wall of dark sprites.
        label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        label.HorizontalAlignment = alignment;
    }

    public override void _Process(double delta)
    {
        _status.Text = BuildStatus();
        _prompt.Text = BuildPrompt();
    }

    private string BuildStatus()
    {
        float remaining = _director?.Remaining ?? 0.0f;
        int minutes = Mathf.FloorToInt(remaining / 60.0f);
        int seconds = Mathf.FloorToInt(remaining % 60.0f);

        float health = _player?.Health ?? 0.0f;
        float maxHealth = _player?.MaxHealth ?? 1.0f;
        Inventory? bag = _player?.Backpack;

        string extraction = _extraction == null ? "—"
            : _extraction.Open ? "OPEN" : "closed";

        Inventory? safe = _player?.SafeBox;
        float multiplier = _director?.ExtractionMultiplier ?? 1.0f;
        int carried = (bag?.TotalValue ?? 0) + (safe?.TotalValue ?? 0);

        // The payout-if-you-leave-now line is the whole decision. Without it the
        // player cannot weigh another minute against what they are already
        // holding, and the escalation is just something that happens to them.
        // Level over ceiling, not level alone. How much climb is left is what
        // makes a better weapon legible as a longer curve rather than a bigger
        // number, and it is the only warning the player gets that the deck is
        // about to stop offering weapon picks.
        string growth = _weapons?.Weapon == null
            ? ""
            : $"   lv {_weapons.Level}/{_weapons.MaxLevel}" +
              (_weapons.AtCeiling ? " MAX" : "") +
              (_player?.Armour > 0.0f ? $"   armour {_player.Armour:F0}" : "");

        return $"{minutes:00}:{seconds:00}   HP {health:F0}/{maxHealth:F0}{growth}\n" +
               $"bag {bag?.UsedBulk ?? 0}/{bag?.Capacity ?? 0}   value {bag?.TotalValue ?? 0}   " +
               $"safe {safe?.UsedBulk ?? 0}/{safe?.Capacity ?? 0}   [F] secure\n" +
               $"extract now: {Mathf.RoundToInt(carried * multiplier)}   (x{multiplier:F2})\n" +
               $"enemies {_horde?.Pool.Count ?? 0}   extraction {extraction}   credits {_meta?.Profile.Credits ?? 0}";
    }

    /// Extraction outranks looting: if both are in progress the player needs to
    /// see the one that ends the run.
    private string BuildPrompt()
    {
        // Once the run is over the hold bars are stale — leaving "EXTRACTING"
        // on screen under an EXTRACTED banner reads as a stuck UI.
        if (_director is { State: not RunState.Running })
            return "";

        // The offer outranks both hold bars. It is the only thing on screen the
        // player has to answer, and it does not pause anything while they think.
        if (_growth is { HasOffer: true })
        {
            var line = new System.Text.StringBuilder("LEVEL UP  ");
            for (int i = 0; i < _growth.Offer.Length; i++)
                line.Append($"[{i + 1}] {_growth.Describe(_growth.Offer[i])}   ");

            if (_growth.PendingPicks > 1)
                line.Append($"(+{_growth.PendingPicks - 1} more)");

            return line.ToString();
        }

        if (_extraction is { PlayerInside: true, Progress: > 0.0f })
            return $"EXTRACTING  {Bar(_extraction.Progress)}";

        foreach (LootContainer container in _containers)
        {
            if (container is { Looted: false, PlayerInRange: true })
                return $"SEARCHING  {Bar(container.Progress)}";
        }

        return "";
    }

    private static string Bar(float progress)
    {
        const int width = 20;
        int filled = Mathf.Clamp(Mathf.RoundToInt(progress * width), 0, width);
        return $"[{new string('#', filled)}{new string('.', width - filled)}]";
    }

    private void OnRunEnded(int state, int banked)
    {
        _banner.Visible = true;
        _banner.Text = (RunState)state switch
        {
            RunState.Extracted => $"EXTRACTED\nbanked {banked}",
            RunState.Died => "KILLED\nbackpack lost",
            RunState.TimedOut => "OUT OF TIME",
            _ => "",
        };
    }
}
