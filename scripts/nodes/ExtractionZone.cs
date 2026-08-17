using Godot;

/// Standing here for a few uninterrupted seconds ends the run and banks the
/// backpack. Leaving cancels — the countdown restarting is what makes a horde
/// arriving mid-extraction matter.
public partial class ExtractionZone : Node3D
{
    [Export] public float Radius { get; set; } = 3.0f;
    [Export] public float HoldSeconds { get; set; } = 5.0f;

    /// Extraction is only live once the run director opens it.
    [Export] public bool Open { get; set; } = true;

    [Signal] public delegate void ExtractedEventHandler();
    [Signal] public delegate void ProgressChangedEventHandler(float progress);

    public float Progress { get; private set; }
    public bool PlayerInside { get; private set; }

    private Player? _player;
    private bool _finished;

    public override void _Ready()
    {
        _player = GetTree().Root.FindChild("Player", recursive: true, owned: false) as Player;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished || _player == null || !_player.IsAlive)
            return;

        bool insideNow = Open && _player.GlobalPosition.DistanceTo(GlobalPosition) <= Radius;

        if (insideNow != PlayerInside)
        {
            PlayerInside = insideNow;
            if (!insideNow)
            {
                Progress = 0.0f;
                EmitSignal(SignalName.ProgressChanged, Progress);
            }
        }

        if (!insideNow)
            return;

        Progress += (float)delta / Mathf.Max(0.01f, HoldSeconds);
        EmitSignal(SignalName.ProgressChanged, Mathf.Min(Progress, 1.0f));

        if (Progress < 1.0f)
            return;

        Progress = 1.0f;
        _finished = true;
        EmitSignal(SignalName.Extracted);
    }
}
