using Godot;

/// The run, readable at a glance: what is left of the player, what they are
/// carrying, what it is worth if they leave now, and where "now" would be.
///
/// The builder supplies the node structure; every colour, string and width lives
/// here, so tuning the readout does not mean regenerating a scene.
///
/// The organising idea is that a number the player has to read is a number they
/// will not read while a brute is on them. Anything that changes fast is a bar
/// whose length and colour carry the meaning; text is reserved for what the
/// player consults between fights.
public partial class Hud : CanvasLayer
{
    /// Fraction of maximum health below which the readout stops being neutral.
    [Export] public float HealthWarnAt { get; set; } = 0.5f;
    [Export] public float HealthCriticalAt { get; set; } = 0.25f;

    private static readonly Color Ink = new(1.0f, 0.97f, 0.9f);
    private static readonly Color Dim = new(0.72f, 0.71f, 0.66f);
    private static readonly Color Track = new(0.05f, 0.05f, 0.06f, 0.72f);
    private static readonly Color Healthy = new(0.42f, 0.78f, 0.36f);
    private static readonly Color Wounded = new(0.90f, 0.72f, 0.20f);
    private static readonly Color Critical = new(0.88f, 0.22f, 0.18f);
    private static readonly Color Cargo = new(0.45f, 0.66f, 0.86f);
    private static readonly Color CargoFull = new(0.90f, 0.60f, 0.24f);
    private static readonly Color Growth = new(0.72f, 0.52f, 0.90f);
    private static readonly Color Hold = new(0.36f, 0.80f, 0.74f);
    private static readonly Color CardFace = new(0.08f, 0.09f, 0.12f, 0.86f);

    private sealed class Bar
    {
        public ColorRect Back = null!;
        public ColorRect Fill = null!;
        public Label Text = null!;
        public float FullWidth;
    }

    private Bar _health = null!;
    private Bar _bag = null!;
    private Bar _level = null!;
    private Bar _hold = null!;

    private Label _arms = null!;
    private Label _keys = null!;
    private Label _clock = null!;
    private Label _payout = null!;
    private Label _exit = null!;
    private Label _banner = null!;
    private ColorRect _vignette = null!;
    private ShaderMaterial? _vignetteMaterial;

    private readonly ColorRect[] _cards = new ColorRect[3];
    private readonly Label[] _cardText = new Label[3];

    private RunDirector? _director;
    private Player? _player;
    private Horde? _horde;
    private MetaManager? _meta;
    private RunGrowth? _growth;
    private WeaponHandler? _weapons;
    private LootContainer[] _containers = System.Array.Empty<LootContainer>();

    /// Health as the readout shows it, which lags the real value. The gap between
    /// the two is the hit — a bar that snaps gives the player nothing to see, and
    /// contact damage arrives in slices too small to notice one at a time.
    private float _shownHealth = -1.0f;
    private float _flash;
    private float _clockSeconds;

    public override void _Ready()
    {
        _health = FindBar("Health");
        _bag = FindBar("Bag");
        _level = FindBar("Level");
        _hold = FindBar("Hold");

        _arms = GetNode<Label>("Arms");
        _keys = GetNode<Label>("Keys");
        _clock = GetNode<Label>("Clock");
        _payout = GetNode<Label>("Payout");
        _exit = GetNode<Label>("Exit");
        _banner = GetNode<Label>("Banner");
        _vignette = GetNode<ColorRect>("Vignette");

        Style(_arms, 22, HorizontalAlignment.Left);
        Style(_keys, 17, HorizontalAlignment.Left, Dim);
        Style(_clock, 40, HorizontalAlignment.Center);
        Style(_payout, 24, HorizontalAlignment.Center);
        Style(_exit, 22, HorizontalAlignment.Right);
        Style(_banner, 44, HorizontalAlignment.Center);
        _banner.Visible = false;

        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i] = GetNode<ColorRect>($"Card{i}");
            _cardText[i] = GetNode<Label>($"Card{i}Text");
            _cards[i].Color = CardFace;
            Style(_cardText[i], 22, HorizontalAlignment.Center);
            _cardText[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        var shader = GD.Load<Shader>("res://assets/shaders/vignette.gdshader");
        if (shader != null)
        {
            _vignetteMaterial = new ShaderMaterial { Shader = shader };
            _vignette.Material = _vignetteMaterial;
        }
        else
        {
            GD.PushWarning("Hud: vignette shader missing — damage feedback will be flat");
        }

        Node? root = GetParent();
        _director = root?.GetNodeOrNull<RunDirector>("RunDirector");
        _player = root?.GetNodeOrNull<Player>("Player");
        _horde = root?.GetNodeOrNull<Horde>("Horde");
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

    private Bar FindBar(string name)
    {
        var bar = new Bar
        {
            Back = GetNode<ColorRect>($"{name}Back"),
            Fill = GetNode<ColorRect>($"{name}Fill"),
            Text = GetNode<Label>($"{name}Text"),
        };

        bar.Back.Color = Track;
        bar.FullWidth = bar.Fill.Size.X;
        Style(bar.Text, Mathf.Max(14, Mathf.RoundToInt(bar.Back.Size.Y * 0.72f)), HorizontalAlignment.Left);
        return bar;
    }

    private static void Style(Label label, int size, HorizontalAlignment alignment) =>
        Style(label, size, alignment, Ink);

    private static void Style(Label label, int size, HorizontalAlignment alignment, Color colour)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);

        // An outline rather than a panel: the readout has to stay legible over
        // both the pale ground and a wall of dark sprites.
        label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        label.HorizontalAlignment = alignment;
    }

    public override void _Process(double delta)
    {
        float step = (float)delta;

        UpdateHealth(step);
        UpdateCargo();
        UpdateArms();
        UpdateClock();
        UpdateExit();
        UpdateHold();
        UpdateOffer();
        UpdateVignette(step);
    }

    private void UpdateHealth(float step)
    {
        float health = _player?.Health ?? 0.0f;
        float maxHealth = Mathf.Max(1.0f, _player?.MaxHealth ?? 1.0f);
        float fraction = Mathf.Clamp(health / maxHealth, 0.0f, 1.0f);

        if (_shownHealth < 0.0f)
            _shownHealth = health;

        // The chase is one-way: damage shows instantly and the ghost catches up,
        // so what lingers on screen is how much was just lost. Healing has no
        // ghost to leave behind, so it simply moves.
        if (health > _shownHealth)
            _shownHealth = health;
        else
            _shownHealth = Mathf.Lerp(_shownHealth, health, 1.0f - Mathf.Exp(-4.0f * step));

        // Only a real bite, and only up to a point. Contact damage never stops
        // while a crowd is on the player, so a flash that tracked the gap exactly
        // would pin at full and tint the arena — which hides the horde the player
        // is trying to escape, and stops being information the moment it is
        // always on.
        float lost = _shownHealth - health;
        if (lost > maxHealth * 0.04f)
            _flash = Mathf.Max(_flash, Mathf.Min(0.75f, lost / (maxHealth * 0.30f)));

        Fill(_health, fraction, fraction <= HealthCriticalAt ? Critical
                              : fraction <= HealthWarnAt ? Wounded
                              : Healthy);

        // The lost slice, drawn as the gap between the ghost and the fill. Same
        // rectangle, tinted — no extra node, and it disappears on its own.
        _health.Back.Color = Track.Lerp(Critical, Mathf.Clamp((_shownHealth - health) / maxHealth * 3.0f, 0.0f, 0.7f));

        float armour = _player?.Armour ?? 0.0f;
        _health.Text.Text = armour > 0.0f
            ? $"{health:F0} / {maxHealth:F0}      armour {armour:F0}"
            : $"{health:F0} / {maxHealth:F0}";
    }

    private void UpdateCargo()
    {
        Inventory? bag = _player?.Backpack;
        Inventory? safe = _player?.SafeBox;

        float fraction = bag is { Capacity: > 0 } ? bag.UsedBulk / (float)bag.Capacity : 0.0f;

        // Amber before full, not at it. "Nearly out of room" is the moment the
        // player has to start choosing what to leave behind, and finding out at
        // the crate is finding out too late.
        Fill(_bag, fraction, fraction >= 0.8f ? CargoFull : Cargo);
        _bag.Text.Text = $"bag {bag?.UsedBulk ?? 0}/{bag?.Capacity ?? 0}   " +
                         $"value {bag?.TotalValue ?? 0}   " +
                         $"safe {safe?.UsedBulk ?? 0}/{safe?.Capacity ?? 0} ({safe?.TotalValue ?? 0})";
    }

    private void UpdateArms()
    {
        if (_weapons?.Weapon is not { } weapon)
        {
            _arms.Text = "unarmed";
            _level.Fill.Visible = false;
            _level.Text.Text = "";
            _keys.Text = "";
            return;
        }

        string ammo = weapon.MagazineSize > 0
            ? $"   {_weapons.Ammo}/{_weapons.Reserve}" + (_weapons.IsDry ? "  DRY" : _weapons.Reloading ? "  reloading" : "")
            : "";

        _arms.Text = $"{weapon.WeaponName}{ammo}";

        // Level against its ceiling, as a length rather than as a fraction. How
        // much climb is left is what makes a better weapon legible as a longer
        // curve rather than a bigger number, and it is the only warning the
        // player gets that the deck is about to stop offering weapon picks.
        float fraction = _weapons.MaxLevel > 0 ? _weapons.Level / (float)_weapons.MaxLevel : 0.0f;
        _level.Fill.Visible = true;
        Fill(_level, fraction, _weapons.AtCeiling ? CargoFull : Growth);
        _level.Text.Text = "";

        _arms.Text += _weapons.AtCeiling
            ? $"      lv {_weapons.Level}/{_weapons.MaxLevel} MAX"
            : $"      lv {_weapons.Level}/{_weapons.MaxLevel}";

        if (_player?.AdrenalineActive == true)
            _arms.Text += $"      ADRENALINE {_player.AdrenalineRemaining:F0}s";

        _keys.Text = "[Tab] swap   [Q] use   [F] secure" +
                     (_player?.ThrowableCount > 0 ? $"   [G] throw x{_player.ThrowableCount}" : "");
    }

    private void UpdateClock()
    {
        float remaining = _director?.Remaining ?? 0.0f;
        _clockSeconds = remaining;

        int minutes = Mathf.FloorToInt(remaining / 60.0f);
        int seconds = Mathf.FloorToInt(remaining % 60.0f);
        _clock.Text = $"{minutes:00}:{seconds:00}";

        // The last thirty seconds are the only part of the clock that changes a
        // decision, so that is the only part that changes colour.
        _clock.AddThemeColorOverride("font_color", remaining <= 30.0f ? Critical : Ink);

        Inventory? bag = _player?.Backpack;
        Inventory? safe = _player?.SafeBox;
        float multiplier = _director?.ExtractionMultiplier ?? 1.0f;
        int carried = (bag?.TotalValue ?? 0) + (safe?.TotalValue ?? 0);

        // The payout-if-you-leave-now line is the whole decision. Without it the
        // player cannot weigh another minute against what they are already
        // holding, and the escalation is just something that happens to them.
        _payout.Text = $"leave now: {Mathf.RoundToInt(carried * multiplier)}   (x{multiplier:F2})";
        _payout.AddThemeColorOverride("font_color", carried > 0 ? Wounded : Dim);
    }

    private void UpdateExit()
    {
        ExtractionZone? nearest = NearestOpenPad();
        string line;

        if (nearest != null && _player != null)
        {
            Vector3 delta = nearest.GlobalPosition - _player.GlobalPosition;
            line = $"EXIT  {Compass(new Vector2(delta.X, delta.Z))}  {delta.Length():F0} m";
        }
        else
        {
            float opensAt = (_director?.RunSeconds ?? 0.0f) * (_director?.ExtractionOpensAt ?? 0.0f);
            float until = Mathf.Max(0.0f, opensAt - (_director?.Elapsed ?? 0.0f));
            line = $"exit opens in {until:F0}s";
        }

        _exit.Text = $"{line}\nenemies {_horde?.Pool.Count ?? 0}\ncredits {_meta?.Profile.Credits ?? 0}";
        _exit.AddThemeColorOverride("font_color", nearest != null ? Hold : Dim);
    }

    private void UpdateHold()
    {
        // Once the run is over the hold bars are stale — leaving "EXTRACTING" on
        // screen under an EXTRACTED banner reads as a stuck UI.
        if (_director is { State: not RunState.Running })
        {
            ShowHold(false, 0.0f, "");
            return;
        }

        foreach (ExtractionZone pad in _director?.Pads ?? System.Array.Empty<ExtractionZone>())
        {
            // Extraction outranks looting: if both are in progress the player
            // needs to see the one that ends the run.
            if (pad is { Open: true, PlayerInside: true, Progress: > 0.0f })
            {
                ShowHold(true, pad.Progress, "EXTRACTING");
                return;
            }
        }

        foreach (LootContainer container in _containers)
        {
            if (container is { Looted: false, PlayerInRange: true })
            {
                ShowHold(true, container.Progress, "SEARCHING");
                return;
            }
        }

        ShowHold(false, 0.0f, "");
    }

    private void ShowHold(bool visible, float progress, string label)
    {
        _hold.Back.Visible = visible;
        _hold.Fill.Visible = visible;
        _hold.Text.Visible = visible;

        if (!visible)
            return;

        Fill(_hold, progress, Hold);
        _hold.Text.Text = $"{label}   {Mathf.RoundToInt(progress * 100.0f)}%";
    }

    /// The offer as cards rather than as a line of text. It is the only thing on
    /// screen the player has to answer, and it does not pause anything while they
    /// think — so it has to be findable in one glance, not parsed.
    private void UpdateOffer()
    {
        bool showing = _growth is { HasOffer: true } && _director is { State: RunState.Running };

        for (int i = 0; i < _cards.Length; i++)
        {
            bool used = showing && i < _growth!.Offer.Length;
            _cards[i].Visible = used;
            _cardText[i].Visible = used;

            if (used)
                _cardText[i].Text = $"[{i + 1}]\n{_growth!.Describe(_growth.Offer[i])}";
        }

        if (showing && _growth!.PendingPicks > 1)
            _cardText[_growth.Offer.Length - 1].Text += $"\n(+{_growth.PendingPicks - 1} more)";
    }

    /// Red at the edges when hit, and a slow pulse of it while nearly dead. Two
    /// meanings on one rectangle because they never need different colours — both
    /// are saying the same thing with different urgency.
    private void UpdateVignette(float step)
    {
        if (_vignetteMaterial == null)
            return;

        _flash = Mathf.Max(0.0f, _flash - step * 3.5f);

        float health = _player?.Health ?? 0.0f;
        float maxHealth = Mathf.Max(1.0f, _player?.MaxHealth ?? 1.0f);
        float fraction = Mathf.Clamp(health / maxHealth, 0.0f, 1.0f);

        float lowHealth = 0.0f;
        if (fraction < HealthCriticalAt && _player is { IsAlive: true })
        {
            float depth = 1.0f - fraction / HealthCriticalAt;
            _clockSeconds += step;
            lowHealth = depth * 0.35f * (0.6f + 0.4f * Mathf.Sin(_clockSeconds * 4.0f));
        }

        _vignetteMaterial.SetShaderParameter("strength", Mathf.Clamp(Mathf.Max(_flash * 0.55f, lowHealth), 0.0f, 1.0f));
    }

    private static void Fill(Bar bar, float fraction, Color colour)
    {
        bar.Fill.Size = new Vector2(bar.FullWidth * Mathf.Clamp(fraction, 0.0f, 1.0f), bar.Fill.Size.Y);
        bar.Fill.Color = colour;
    }

    /// Nearest open pad, for the arrow. Once the exits are somewhere different
    /// every run, a player who cannot find one is not being challenged.
    private ExtractionZone? NearestOpenPad()
    {
        if (_director == null || _player == null)
            return null;

        ExtractionZone? best = null;
        float bestDistance = float.MaxValue;

        foreach (ExtractionZone pad in _director.Pads)
        {
            if (!pad.Open)
                continue;

            float distance = pad.GlobalPosition.DistanceTo(_player.GlobalPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pad;
            }
        }

        return best;
    }

    /// Eight-way arrow in screen space. World +Z is down-screen under this
    /// camera, so north on the readout is away from the viewer.
    private static string Compass(Vector2 direction)
    {
        if (direction.LengthSquared() < 0.001f)
            return "•";

        string[] arrows = { "→", "↘", "↓", "↙", "←", "↖", "↑", "↗" };
        float angle = Mathf.Atan2(direction.Y, direction.X);
        int index = Mathf.PosMod(Mathf.RoundToInt(angle / (Mathf.Tau / 8.0f)), 8);
        return arrows[index];
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

        _banner.AddThemeColorOverride("font_color", (RunState)state == RunState.Extracted ? Healthy : Critical);
    }
}
