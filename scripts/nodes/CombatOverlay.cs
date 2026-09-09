using Godot;

/// The two things a fight has to say in screen space rather than in world space:
/// what a hit was worth, and where the one that hurt came from.
///
/// A `Control` with a `_Draw` rather than nodes. A damage number is a Label that
/// lives for half a second, and a crowd produces a dozen a second — that is a
/// hundred nodes a minute created, laid out and freed, on the frames the game is
/// busiest. Everything here is two fixed pools and one draw call's worth of
/// immediate mode, on the same rule as `EffectDirector`: the cost of a crowd must
/// not scale with the crowd.
///
/// Added by `Hud` at runtime rather than by the scene builder. It is part of the
/// readout, it needs exactly what the readout already looked up, and a node in
/// `Main.tscn` is a node somebody has to remember to regenerate.
public partial class CombatOverlay : Control
{
    /// **Damage numbers are against this project's own HUD rule and they are here
    /// anyway.** The rule is that a number the player has to read is a number they
    /// will not read while a brute is on them, which is why everything else in the
    /// readout is a bar. It is a good rule and it is about the *readout* — a
    /// figure in a fixed corner that has to be found, focused on and compared with
    /// what it was a second ago.
    ///
    /// A number over the thing you just shot is a different object. It is not
    /// consulted, it is glanced at, and it answers the one question a build cannot
    /// otherwise answer: whether the card just taken did anything. Fifteen weapons
    /// and eighteen growth options multiply into a damage figure that appears
    /// nowhere — the player picks "+12% crit" and finds out whether it mattered by
    /// how the run ends forty seconds later.
    ///
    /// The restraint is the rate limit rather than the feature. Crits are never
    /// suppressed, because a crit is the thing the number exists to show.
    [Export] public bool ShowDamage { get; set; } = true;

    /// Shortest gap between two ordinary numbers, in seconds. A magazine into a
    /// crowd lands seven hits a second across three bodies and every one of them
    /// wants to say something; ten a second is already at the edge of a thing a
    /// person can read, and the ones that get dropped are indistinguishable from
    /// the ones that do not.
    [Export] public float NumberInterval { get; set; } = 0.1f;

    [Export] public int NumberCapacity { get; set; } = 28;

    /// Seconds a threat arc takes to fade once nothing is renewing it.
    [Export] public float ThreatFade { get; set; } = 1.6f;

    private static readonly Color Plain = new(1.0f, 0.95f, 0.86f);
    private static readonly Color Crit = new(1.0f, 0.86f, 0.36f);
    private static readonly Color Threat = new(0.90f, 0.24f, 0.18f);

    private Player? _player;
    private WeaponHandler? _weapons;
    private Font? _font;

    private Vector3[] _at = System.Array.Empty<Vector3>();
    private float[] _life = System.Array.Empty<float>();
    private float[] _maxLife = System.Array.Empty<float>();
    private int[] _amount = System.Array.Empty<int>();
    private bool[] _crit = System.Array.Empty<bool>();
    private float[] _drift = System.Array.Empty<float>();
    private int _count;

    private float _clock;
    private float _lastNumber = float.NegativeInfinity;
    private ulong _rng = 0xD1B54A32D192ED03UL;

    /// The compass, in eight sectors around the player.
    ///
    /// Sectors rather than one bearing, because being surrounded is the situation
    /// this exists for and a single arrow can only point at one of five things
    /// eating you. Each holds a weight that damage adds to and time takes away, so
    /// what is drawn is *where the pressure is*, which is the decision the player
    /// is actually making — which way to walk.
    private const int Sectors = 8;
    private readonly float[] _threat = new float[Sectors];

    public void Bind(Player? player, WeaponHandler? weapons)
    {
        _player = player;
        _weapons = weapons;

        if (_weapons != null)
            _weapons.Hit += OnHit;

        if (_player != null)
            _player.Hurt += OnHurt;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // The overlay covers the whole screen and the level-up cards are
        // clickable. Anything but Ignore here eats the one input the HUD has.
        MouseFilter = MouseFilterEnum.Ignore;

        _at = new Vector3[NumberCapacity];
        _life = new float[NumberCapacity];
        _maxLife = new float[NumberCapacity];
        _amount = new int[NumberCapacity];
        _crit = new bool[NumberCapacity];
        _drift = new float[NumberCapacity];

        _font = GetThemeDefaultFont();
    }

    public override void _ExitTree()
    {
        if (_weapons != null)
            _weapons.Hit -= OnHit;

        if (_player != null)
            _player.Hurt -= OnHurt;
    }

    public override void _Process(double delta)
    {
        float step = (float)delta;
        _clock += step;

        for (int i = _count - 1; i >= 0; i--)
        {
            _life[i] -= step;
            if (_life[i] <= 0.0f)
                Remove(i);
        }

        bool anyThreat = false;
        for (int i = 0; i < Sectors; i++)
        {
            if (_threat[i] <= 0.0f)
                continue;

            _threat[i] = Mathf.Max(0.0f, _threat[i] - step / ThreatFade);
            anyThreat |= _threat[i] > 0.0f;
        }

        if (_count > 0 || anyThreat)
            QueueRedraw();
    }

    private void OnHit(Vector3 where, WeaponCategory category, float damage, bool crit)
    {
        if (!ShowDamage || damage <= 0.5f)
            return;

        // Crits jump the queue for the same reason they jump the impact-puff
        // queue: it is the rarest thing this channel has to say, and a card the
        // player paid for is worth more than the fourth ordinary tick of the same
        // magazine.
        if (!crit && _clock - _lastNumber < NumberInterval)
            return;

        _lastNumber = _clock;

        int slot;
        if (_count < NumberCapacity)
        {
            slot = _count++;
        }
        else
        {
            slot = 0;
            for (int i = 1; i < _count; i++)
            {
                if (_life[i] < _life[slot])
                    slot = i;
            }
        }

        // Scattered across the body rather than centred on it. Two hits on one
        // enemy in the same half second draw two numbers in exactly the same
        // place, which reads as one number flickering.
        _at[slot] = new Vector3(
            where.X + (NextFloat() - 0.5f) * 0.7f,
            Terrain.Height(where.X, where.Z) + 1.35f + NextFloat() * 0.3f,
            where.Z + (NextFloat() - 0.5f) * 0.7f);

        _life[slot] = crit ? 0.85f : 0.6f;
        _maxLife[slot] = _life[slot];
        _amount[slot] = Mathf.Max(1, Mathf.RoundToInt(damage));
        _crit[slot] = crit;
        _drift[slot] = (NextFloat() - 0.5f) * 0.5f;

        QueueRedraw();
    }

    /// Something hurt the player, from a bearing.
    ///
    /// Accumulated rather than latched. Contact damage arrives sixty times a
    /// second as a slice of a rate, so "the last thing that hurt me" is a
    /// meaningless reading — what the player needs is which way the pressure has
    /// been coming from over the last second, which is a sum with a decay on it.
    private void OnHurt(Vector2 bearing, float amount)
    {
        if (amount <= 0.0f || bearing.LengthSquared() < 0.0001f || _player == null)
            return;

        float angle = Mathf.Atan2(bearing.X, bearing.Y);
        int sector = Mathf.PosMod(Mathf.RoundToInt(angle / Mathf.Tau * Sectors), Sectors);

        // As a fraction of maximum health, so a 140-health Warden and an 80-health
        // Courier both get an arc that means "this is a lot".
        float share = amount / Mathf.Max(1.0f, _player.MaxHealth);
        _threat[sector] = Mathf.Min(1.0f, _threat[sector] + share * 5.0f);

        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawThreat();
        DrawNumbers();
    }

    /// Arcs at the edge of the screen, in the direction the damage came from.
    ///
    /// **The bearing is rotated into the camera's frame, which is the whole
    /// reason this is not a world-space marker.** The mouse turns the view, so a
    /// world direction means nothing to a player looking at a screen — what they
    /// need is "behind me, and to the left", and the only frame in which that is
    /// expressible is the one the camera is in.
    private void DrawThreat()
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
            return;

        // The viewport's rectangle, not this control's own.
        //
        // `Size` was (0, 0) and the arcs were being drawn at a radius of nothing
        // — invisible, with no error, while the damage numbers in the same
        // `_Draw` came out correctly because they project from the camera and
        // never ask this node how big it is. A Control parented to a CanvasLayer
        // has no parent Control to anchor against, so its rect is whatever the
        // layout pass last decided, which here is nothing at all.
        Vector2 screen = GetViewportRect().Size;
        Vector2 middle = screen * 0.5f;
        float radius = Mathf.Min(screen.X, screen.Y) * 0.34f;
        Basis frame = camera.GlobalTransform.Basis;

        for (int i = 0; i < Sectors; i++)
        {
            float weight = _threat[i];
            if (weight <= 0.02f)
                continue;

            float world = Mathf.Tau * i / Sectors;
            var bearing = new Vector3(Mathf.Sin(world), 0.0f, Mathf.Cos(world));

            // Into camera space: X is the view's right and −Z is the view's
            // forward, so this is the angle off "straight ahead" the player sees.
            Vector3 local = frame.Inverse() * bearing;
            float onScreen = Mathf.Atan2(local.X, -local.Z);

            // Screen angles run clockwise from +X, and straight ahead is up.
            float centre = onScreen - Mathf.Pi * 0.5f;
            float half = Mathf.Pi / Sectors * 0.85f;

            DrawArc(middle, radius, centre - half, centre + half, 16,
                    new Color(Threat.R, Threat.G, Threat.B, 0.18f + 0.62f * weight),
                    8.0f + 14.0f * weight, antialiased: true);
        }
    }

    private void DrawNumbers()
    {
        if (_font == null)
            return;

        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
            return;

        for (int i = 0; i < _count; i++)
        {
            float age = 1.0f - _life[i] / Mathf.Max(0.0001f, _maxLife[i]);

            // Rises and eases out, so the number is furthest from the body when
            // it is faintest — a number that fades in place reads as a texture on
            // the enemy rather than as an event.
            float lift = (1.0f - (1.0f - age) * (1.0f - age)) * 1.1f;
            Vector3 world = _at[i] + new Vector3(_drift[i] * age, lift, 0.0f);

            if (camera.IsPositionBehind(world))
                continue;

            Vector2 screen = camera.UnprojectPosition(world);

            // Held, then out. Same curve as the muzzle flash and for the same
            // reason: a linear fade spends half its life half-there.
            float alpha = age < 0.55f ? 1.0f : 1.0f - (age - 0.55f) / 0.45f;

            // Crits open larger and settle, which is the only size cue in a
            // channel that is otherwise all the same height.
            int size = _crit[i]
                ? Mathf.RoundToInt(Mathf.Lerp(34.0f, 26.0f, Mathf.Min(1.0f, age * 4.0f)))
                : 20;

            Color tint = _crit[i] ? Crit : Plain;
            string text = _amount[i].ToString();
            Vector2 width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size);

            // A dark pass one pixel down and across. The arena is bright, the
            // horde is pale green and the numbers are pale — without this they
            // vanish over exactly the bodies they are describing.
            DrawString(_font, screen - width * 0.5f + new Vector2(1.5f, 1.5f), text,
                       HorizontalAlignment.Left, -1, size, new Color(0.0f, 0.0f, 0.0f, alpha * 0.55f));

            DrawString(_font, screen - width * 0.5f, text,
                       HorizontalAlignment.Left, -1, size,
                       new Color(tint.R, tint.G, tint.B, alpha));
        }
    }

    private void Remove(int index)
    {
        int last = --_count;
        if (index == last)
            return;

        _at[index] = _at[last];
        _life[index] = _life[last];
        _maxLife[index] = _maxLife[last];
        _amount[index] = _amount[last];
        _crit[index] = _crit[last];
        _drift[index] = _drift[last];
    }

    /// What is on screen, for a probe. The game never reaches in here.
    public int NumberCount => _count;

    public float ThreatAt(int sector) =>
        sector >= 0 && sector < Sectors ? _threat[sector] : 0.0f;

    public int SectorCount => Sectors;

    /// Which sector a world bearing lands in, so a probe can ask about the
    /// direction it fired from rather than about an index it had to derive the
    /// same way this does.
    public static int SectorOf(Vector2 bearing) =>
        Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(bearing.X, bearing.Y) / Mathf.Tau * Sectors), Sectors);

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
