using Godot;

/// Drives the main scene with synthetic input and reports whether the player
/// actually moved and the camera actually followed.
///
///   godot --headless --script test/MovementProbe.cs
///
/// Headless is fine — this measures transforms, not pixels. Exit code is the
/// verdict, so it can gate a build.
///
/// Deliberately does NOT use SceneBuildUtil.Run: that helper quits as soon as
/// its callback returns, which is right for a builder and fatal for a probe that
/// needs to live across frames.
public partial class MovementProbe : SceneTree
{
    private const int SettleFrames = 10;
    private const int DriveFrames = 90;

    private Node3D? _player;
    private Node3D? _rig;
    private Vector3 _playerStart;
    private int _frame;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // Only the add happens here. Nodes are not inside the tree yet during
        // _Initialize, so any global transform read now returns identity.
        GetRoot().AddChild(scene);
    }

    public override bool _Process(double delta)
    {
        _frame++;

        if (_frame == 1)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _player = scene.GetNodeOrNull<Node3D>("Player");
            _rig = scene.GetNodeOrNull<Node3D>("CameraRig");
            if (_player == null || _rig == null)
            {
                GD.PushError($"PROBE FAILED — player={_player != null} rig={_rig != null}");
                Quit(1);
                return true;
            }
            _playerStart = _player.GlobalPosition;
            return false;
        }

        if (_frame == SettleFrames)
        {
            Input.ActionPress("move_right");
            Input.ActionPress("move_down");
        }

        if (_frame < SettleFrames + DriveFrames)
            return false;

        Input.ActionRelease("move_right");
        Input.ActionRelease("move_down");

        Vector3 moved = _player!.GlobalPosition - _playerStart;
        Vector3 lag = _rig!.GlobalPosition - _player.GlobalPosition;

        GD.Print($"player moved {moved.Snapped(Vector3.One * 0.001f)} — distance {moved.Length():F2}m");
        GD.Print($"camera rig lag {lag.Length():F3}m");

        bool right = moved.X > 1.0f;
        bool forward = moved.Z > 1.0f;
        bool followed = lag.Length() < 1.0f;
        bool ok = right && forward && followed;

        GD.Print(ok ? "PROBE OK" : $"PROBE FAILED — right={right} forward={forward} camera={followed}");
        Quit(ok ? 0 : 1);
        return true;
    }
}
