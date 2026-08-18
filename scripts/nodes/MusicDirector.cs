using Godot;

/// The run's shape, in sound.
///
/// `SoundDirector` already turns an ambience layer up as the crowd grows, which
/// is a volume knob rather than music: a run's tension has a shape — opening,
/// the horde forming, the boss, the countdown out — and none of it was audible.
///
/// Four loops of identical length at one tempo, each faded in and out
/// independently. Not a playlist. A cut between two pieces of music is heard as a
/// glitch, and a crossfade between two that do not share a tempo is heard as a
/// worse one; layers written to sit on top of each other can be added or dropped
/// at any moment and it still sounds deliberate.
///
/// Its own players, never the SFX voice pool. The pool is a fixed ring that the
/// oldest voice recycles out of, so a busy second of explosions would take the
/// music with it.
public partial class MusicDirector : Node
{
    /// Seconds to reach a layer's target volume. Slow on the way in and slower on
    /// the way out: a layer that appears the instant a threshold is crossed reads
    /// as a switch, and one that vanishes the instant it is uncrossed makes the
    /// mix flicker every time the crowd hovers around a number.
    [Export] public float FadeIn { get; set; } = 2.5f;
    [Export] public float FadeOut { get; set; } = 4.0f;

    [Export] public float MasterVolumeDb { get; set; } = -14.0f;

    /// Intensity at which the pulse and then the tension layer come in.
    ///
    /// Hysteresis on both, because `Intensity` is a smooth ramp but the crowd
    /// count is not: a single threshold with a value wobbling across it is a
    /// layer that stutters, and the fade is slow enough that the player would
    /// hear it as the music breathing wrong rather than as a bug.
    [Export] public float PulseAt { get; set; } = 0.14f;
    [Export] public float TensionAt { get; set; } = 0.45f;

    /// Nearby enemies that bring the tension layer up regardless of the clock.
    /// A quiet moment at high intensity should sound quiet, and being swarmed at
    /// forty seconds should not.
    [Export] public int TensionCrowd { get; set; } = 28;
    [Export] public float CrowdRadius { get; set; } = 22.0f;

    private enum Layer
    {
        Bed,
        Pulse,
        Tension,
        Boss,
    }

    private static readonly string[] ClipNames = { "music_bed", "music_pulse", "music_tension", "music_boss" };

    /// Per-layer ceiling, so the mix is balanced in the table rather than by
    /// every layer being normalised to the same peak and fighting.
    private static readonly float[] LayerGain = { 1.0f, 0.85f, 0.7f, 0.95f };

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[ClipNames.Length];
    private readonly float[] _level = new float[ClipNames.Length];
    private readonly float[] _target = new float[ClipNames.Length];

    private RunDirector? _director;
    private Horde? _horde;
    private Player? _player;

    private bool _ready;

    public override void _Ready()
    {
        Node? root = GetParent();
        _director = root?.GetNodeOrNull<RunDirector>("RunDirector");
        _horde = root?.GetNodeOrNull<Horde>("Horde");
        _player = root?.GetNodeOrNull<Player>("Player");

        for (int i = 0; i < ClipNames.Length; i++)
        {
            var clip = GD.Load<AudioStream>($"res://assets/audio/{ClipNames[i]}.tres");
            if (clip == null)
            {
                GD.PushWarning($"MusicDirector: missing {ClipNames[i]} — run BuildAudio.cs");
                continue;
            }

            var voice = new AudioStreamPlayer
            {
                Name = ClipNames[i],
                Stream = clip,

                // Silent, not stopped. Every layer plays from the first frame
                // and stays playing for the whole run — that is what keeps them
                // in phase with each other, and starting one late would put it a
                // few seconds out for the rest of the run with no way back.
                VolumeDb = -80.0f,
            };

            AddChild(voice);
            voice.Play();
            _players[i] = voice;
        }

        _ready = true;
    }

    public override void _Process(double delta)
    {
        if (!_ready)
            return;

        Decide();

        float step = (float)delta;
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i] == null)
                continue;

            float rate = _target[i] > _level[i] ? FadeIn : FadeOut;

            // Exponential approach rather than a linear ramp, so a layer arrives
            // quickly and settles rather than sliding in at a constant speed —
            // which is audible as a fader being moved by hand.
            _level[i] = Mathf.Lerp(_level[i], _target[i], 1.0f - Mathf.Exp(-step / Mathf.Max(0.01f, rate)));

            // Below a hundredth, silence. `LinearToDb` of zero is negative
            // infinity, which Godot will happily set and then not recover from.
            _players[i].VolumeDb = _level[i] < 0.01f
                ? -80.0f
                : MasterVolumeDb + Mathf.LinearToDb(_level[i] * LayerGain[i]);
        }
    }

    /// What should be playing, as four numbers between zero and one.
    private void Decide()
    {
        if (_director is not { } director)
            return;

        bool running = director.State == RunState.Running;

        // Everything down when the run is over. The debrief is a screen the
        // player reads, and reading it under the boss layer is the game still
        // shouting after the fight is finished.
        if (!running)
        {
            for (int i = 0; i < _target.Length; i++)
                _target[i] = 0.0f;

            return;
        }

        float intensity = director.Intensity;
        int crowd = NearbyCount();

        _target[(int)Layer.Bed] = 1.0f;

        // Hysteresis: comes in at the threshold, does not leave until well below
        // it. Written as two constants rather than one so the gap is visible.
        _target[(int)Layer.Pulse] = Hold(_target[(int)Layer.Pulse], intensity, PulseAt, PulseAt * 0.6f);

        float tension = Mathf.Max(
            Hold(_target[(int)Layer.Tension], intensity, TensionAt, TensionAt * 0.75f),
            Mathf.Clamp((crowd - TensionCrowd * 0.5f) / TensionCrowd, 0.0f, 1.0f));

        // The extraction countdown is its own kind of tense and does not
        // correspond to either the clock or the crowd — the player is standing
        // still on purpose, which is the one thing this game has spent every
        // other system teaching them not to do.
        if (director.PrimaryPad is { Open: true } && _player != null
            && director.PrimaryPad.GlobalPosition.DistanceTo(_player.GlobalPosition) < 6.0f)
        {
            tension = 1.0f;
        }

        _target[(int)Layer.Tension] = tension;
        _target[(int)Layer.Boss] = director.BossAlive ? 1.0f : 0.0f;
    }

    /// One-way threshold with a lower release point, returning a 0/1 target.
    private static float Hold(float current, float value, float rise, float fall) =>
        current > 0.5f
            ? (value >= fall ? 1.0f : 0.0f)
            : (value >= rise ? 1.0f : 0.0f);

    private int NearbyCount()
    {
        if (_horde == null || _player == null)
            return 0;

        Vector3 at = _player.GlobalPosition;
        float radiusSqr = CrowdRadius * CrowdRadius;
        int count = 0;

        for (int i = 0; i < _horde.Pool.Count; i++)
        {
            if (_horde.Pool.Position[i].DistanceSquaredTo(at) < radiusSqr)
                count++;
        }

        return count;
    }

    /// Re-decides now rather than on the next frame.
    ///
    /// For probes: a stage that moves the clock and then reads the targets is
    /// reading the previous frame's decision, and would pass or fail on whether
    /// a `_Process` happened to have run in between.
    public void ForceDecideForTesting() => Decide();

    /// For probes: what each layer is currently asked to be, and where it is.
    public float TargetOf(int layer) => layer >= 0 && layer < _target.Length ? _target[layer] : 0.0f;

    public float LevelOf(int layer) => layer >= 0 && layer < _level.Length ? _level[layer] : 0.0f;

    public int LayerCount => _players.Length;

    public bool Playing(int layer) =>
        layer >= 0 && layer < _players.Length && _players[layer] is { } voice && voice.Playing;

    public double LengthOf(int layer) =>
        layer >= 0 && layer < _players.Length && _players[layer]?.Stream is { } stream
            ? stream.GetLength()
            : 0.0;
}
