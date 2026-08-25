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
    private Label _alert = null!;
    private ColorRect _bossBack = null!;
    private ColorRect _bossFill = null!;
    private ColorRect _vignette = null!;

    private float _alertLeft;
    private float _bossBarWidth;
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
    private DangerZone[] _zones = System.Array.Empty<DangerZone>();

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
        _alert = GetNode<Label>("Alert");
        _bossBack = GetNode<ColorRect>("BossBarBack");
        _bossFill = GetNode<ColorRect>("BossBarFill");
        _vignette = GetNode<ColorRect>("Vignette");

        Style(_arms, 22, HorizontalAlignment.Left);
        Style(_keys, 17, HorizontalAlignment.Left, Dim);
        Style(_clock, 40, HorizontalAlignment.Center);
        Style(_payout, 24, HorizontalAlignment.Center);
        Style(_exit, 22, HorizontalAlignment.Right);
        Style(_banner, 44, HorizontalAlignment.Center);
        _banner.Visible = false;

        Style(_alert, 30, HorizontalAlignment.Center, Critical);
        _alert.Visible = false;

        _bossBack.Color = Track;
        _bossFill.Color = Critical;
        _bossBarWidth = _bossFill.Size.X;
        _bossBack.Visible = false;
        _bossFill.Visible = false;

        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i] = GetNode<ColorRect>($"Card{i}");
            _cardText[i] = GetNode<Label>($"Card{i}Text");
            _cards[i].Color = CardFace;
            Style(_cardText[i], 22, HorizontalAlignment.Center);
            _cardText[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;

            // Tappable. The offer is the one thing in a run answered by a key
            // that never reached the input abstraction — RunGrowth polls
            // pick_1/2/3 directly — so on a touch device it could not be
            // answered at all, and the cards would sit there forever.
            //
            // The card the offer is already drawing is the button. A fifth
            // on-screen control for something that appears twice a minute is a
            // control nobody would find.
            int index = i;
            _cards[i].MouseFilter = Control.MouseFilterEnum.Stop;
            _cards[i].GuiInput += @event => OnCardInput(@event, index);
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

        RefreshContainers();
        FindZones();

        if (_director != null)
        {
            _director.RunEnded += OnRunEnded;
            _director.BossArrived += OnBossArrived;
            _director.SupplyDropped += OnSupplyDropped;
        }
    }

    private void OnBossArrived() => Announce("SOMETHING BIG IS COMING", 4.0f);

    private void OnSupplyDropped(Vector3 at) => Announce("SUPPLY DROP", 4.0f);

    /// Re-taken whenever a crate arrives, because the compass points at the ones
    /// it knows about — and the two that appear mid-run are the two most worth
    /// pointing at.
    private void RefreshContainers()
    {
        var found = new System.Collections.Generic.List<LootContainer>();
        Node? crates = GetParent()?.GetNodeOrNull("LootContainers");
        if (crates != null)
        {
            foreach (Node child in crates.GetChildren())
            {
                if (child is LootContainer container)
                    found.Add(container);
            }

            if (!_watchingCrates)
            {
                _watchingCrates = true;
                crates.ChildEnteredTree += _ => RefreshContainers();
            }
        }

        _containers = found.ToArray();
    }

    private bool _watchingCrates;

    /// The danger zones, and their announcements.
    ///
    /// Collected once. Unlike the crates, zones are never added mid-run — the
    /// level places all of them before the first frame — so there is nothing to
    /// watch for and no reason to pay for a signal that would never fire.
    private void FindZones()
    {
        var found = new System.Collections.Generic.List<DangerZone>();

        foreach (Node child in GetParent()?.GetNodeOrNull("DangerZones")?.GetChildren()
                               ?? new Godot.Collections.Array<Node>())
        {
            if (child is not DangerZone zone)
                continue;

            found.Add(zone);

            // A zone waking is the single most important thing that can happen
            // without the player having pressed anything, and it happens *because*
            // they walked somewhere. Without a line saying so, the first sign is
            // eight enemies arriving and no explanation for them.
            zone.ZoneStarted += title => Announce($"{title.ToUpper()} — HOLD OR LEAVE", 2.6f);
            zone.ZoneCleared += (title, rounds) =>
                Announce($"{title.ToUpper()} CLEARED — +{rounds} ROUNDS", 2.6f);
        }

        _zones = found.ToArray();
    }

    /// A line that says itself and then gets out of the way. Anything permanent
    /// in the upper third competes with the thing it is warning about.
    /// What the banner currently says, or empty when it is not showing. Only a
    /// probe asks — an announcement is the one kind of feedback with no other
    /// trace, so without this "did the player get told" is unanswerable outside a
    /// screenshot.
    public string AlertText => _alert.Visible ? _alert.Text : string.Empty;

    public void Announce(string text, float seconds)
    {
        _alert.Text = text;
        _alert.Visible = true;
        _alertLeft = seconds;
    }

    private bool _touchActive;
    private bool _layoutSettled;

    /// Asked on the first frame rather than in _Ready, because _Ready is too
    /// early: nodes are readied in tree order and TouchHud is added after this
    /// one, so its answer is still "no touchscreen" when this node asks. The
    /// symptom was the offer staying under the thumb — the left-hand card sits
    /// inside the move stick, which is on a higher canvas layer and swallowed
    /// every tap on it, so the player could read three options and take two.
    ///
    /// Lifting the row above the stick is the fix that does not involve
    /// disabling movement while an offer is up, which would be a pause by
    /// another name and is exactly what the offer was designed not to be.
    private void AdoptTouchLayout()
    {
        if (_layoutSettled)
            return;

        _layoutSettled = true;
        _touchActive = GetParent()?.GetNodeOrNull<TouchHud>("TouchHud")?.Active ?? false;
        if (!_touchActive)
            return;

        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].Position = new Vector2(_cards[i].Position.X, 196.0f);
            _cardText[i].Position = new Vector2(_cardText[i].Position.X, 212.0f);
        }
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
        AdoptTouchLayout();

        UpdateHealth(step);
        UpdateCargo();
        UpdateArms();
        UpdateClock();
        UpdateExit();
        UpdateHold();
        UpdateOffer();
        UpdateAlert(step);
        UpdateBossBar();
        UpdateVignette(step);
    }

    private void UpdateAlert(float step)
    {
        if (_alertLeft <= 0.0f)
            return;

        _alertLeft -= step;
        _alert.Visible = _alertLeft > 0.0f;

        // Blink rather than fade. A fading warning reads as something already
        // over; a pulsing one reads as still true, which it is until the thing
        // is dead.
        _alert.Modulate = new Color(1.0f, 1.0f, 1.0f,
                                    0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_alertLeft * 6.0f)));
    }

    /// Hidden until it exists, hidden again once it is dead. The bar is not a
    /// readout of a number the player asked for — it is the answer to "is this
    /// working", and a bar that stays at zero after the fight says the opposite.
    private void UpdateBossBar()
    {
        int index = _director is { BossAlive: true } && _horde != null
            ? _horde.FirstOfType(_director.BossType)
            : -1;

        if (index < 0 || _horde == null)
        {
            _bossBack.Visible = false;
            _bossFill.Visible = false;
            return;
        }

        float max = Mathf.Max(1.0f, _horde.Types[_director!.BossType].MaxHealth);
        float fraction = Mathf.Clamp(_horde.Pool.Health[index] / max, 0.0f, 1.0f);

        _bossBack.Visible = true;
        _bossFill.Visible = true;
        _bossFill.Size = new Vector2(_bossBarWidth * fraction, _bossFill.Size.Y);
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

        // Both of them, because both of them fire.
        //
        // A line per slot, the held one marked. Which weapon is *held* still
        // matters — it is the one the body draws and the one a level-up card
        // raises — but it stopped being the only one doing anything, and a
        // readout that names one while two are shooting is a readout the player
        // will disbelieve on the first magazine that empties silently.
        var lines = new System.Collections.Generic.List<string>();

        for (int i = 0; i < _weapons.SlotCount; i++)
        {
            if (_weapons.WeaponIn(i) is not { } each)
                continue;

            string ammo = each.MagazineSize > 0
                ? $"   {_weapons.AmmoIn(i)}/{_weapons.ReserveIn(i)}"
                  + (_weapons.IsDryIn(i) ? "  DRY" : _weapons.ReloadingIn(i) ? "  reloading" : "")
                : "";

            // The marker says which is in hand, not which is working — a dot
            // against an idle slot would be exactly the wrong thing to learn.
            string held = i == _weapons.ActiveSlot ? "> " : "  ";
            string idle = _weapons.FiringIn(i) ? "" : "   (idle)";

            lines.Add($"{held}{each.WeaponName}{ammo}{idle}");
        }

        // A newline escape, never AppendLine. That writes Environment.NewLine,
        // which on Windows is carriage-return plus newline, and Godot Label
        // treats the carriage return as a line break of its own — every menu in
        // this game was double-spaced for sixteen phases because of it.
        _arms.Text = string.Join("\n", lines);

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

        // The charge is worthless unsaid. It is the only trait whose value
        // depends on the player *knowing* it is ready — a shot that quietly does
        // three and a half times the damage is a weapon with inconsistent numbers
        // rather than one that rewards restraint.
        if (_weapons.IsCharged)
            _arms.Text += "      CHARGED";

        if (_player?.AdrenalineActive == true)
            _arms.Text += $"      ADRENALINE {_player.AdrenalineRemaining:F0}s";

        // Silent on a touch build. Naming keys that do not exist is worse than
        // naming nothing — the buttons on the right are the answer there, and
        // they say what they do on their faces.
        _keys.Text = _touchActive
            ? ""
            : "[Tab] swap   [Q] use   [F] secure" +
              (_player?.ThrowableCount > 0 ? $"   [G] throw x{_player.ThrowableCount}" : "") +
              (_player?.Backpack.EntryCount > 0 ? "   [R] drop worst" : "");
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

        // Then the zone the player is standing in. Below extraction because
        // extraction ends the run, above searching because a crate can wait and
        // a zone is spawning enemies at the player while they read it.
        //
        // Only while inside. A zone keeps running when the player steps out —
        // deliberately, so it cannot be farmed from the edge — but a progress bar
        // for somewhere they are no longer standing is a bar that describes
        // nothing they can act on.
        foreach (DangerZone zone in _zones)
        {
            if (zone is { State: DangerZone.ZoneState.Running, PlayerInside: true })
            {
                ShowHold(true, zone.Progress, zone.Title.ToUpper());
                return;
            }
        }

        foreach (LootContainer container in _containers)
        {
            if (container is not { Looted: false, PlayerInRange: true })
                continue;

            // What is still in it, once there is anything. A crate keeps what
            // would not fit, and "your bag is full" is only a decision next to
            // "and this is what is sitting here" — without the second half the
            // player has no idea whether it is worth dropping something for.
            ShowHold(true, container.Progress, container.RemainingBulk > 0
                ? $"{container.RemainingBulk} LEFT, WORTH {container.RemainingValue}"
                : "SEARCHING");

            return;
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

    /// A tap or a click on a card takes it. Both, because the same build runs on
    /// a desktop where the mouse is how anyone would try it first.
    private void OnCardInput(InputEvent @event, int index)
    {
        bool pressed = @event is InputEventScreenTouch { Pressed: true }
                    or InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };

        if (pressed && _growth is { HasOffer: true })
            _growth.Choose(index);
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
