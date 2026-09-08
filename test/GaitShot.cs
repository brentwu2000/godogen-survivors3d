using Godot;

/// Photographs the player's gait as a strip of frames while a movement key is
/// held down.
///
///   godot --script test/GaitShot.cs -- hold:move_up
///   godot --script test/GaitShot.cs -- hold:move_right every:2 frames:8
///
/// Not headless — the null rendering driver has nothing to capture.
///
/// **The walk is the one thing in this game no still can answer and no probe
/// can either.** `BodyProbe` proves the limbs are rigged into the right buckets
/// with opposed phases; `BodyShot` puts the bodies in a row mid-stride. Neither
/// says whether the gait *matches the movement*, which is a question about two
/// things changing together over time.
///
/// It exists because `[A]`/`[D]` became strafing. `body.gdshader` swings a limb
/// about a hip pivot and fore-and-aft is the only axis it has, so a body
/// translating sideways swings its legs along an axis it is not travelling on —
/// which is a moonwalk, and the amount it matters is not derivable from the
/// shader. The camera follows the player, so what a player actually sees is the
/// legs cycling while the *ground* slides sideways, and that is what this
/// captures: the game's own camera, the game's own follow, several frames of one
/// stride.
///
/// A full cycle is 1.4 m of travel — see `BodyRenderer.StridesPerMetre` — which
/// at 6 m/s is fourteen frames, so the defaults sample every other frame across
/// a little more than one cycle.
public partial class GaitShot : SceneTree
{
    private const string ScenePath = "res://scenes/Main.tscn";

    /// Long enough for the camera to settle onto the player and for the velocity
    /// to reach the target: the acceleration is exponential toward it, so the
    /// first frames of a held key are a body still speeding up and a gait still
    /// spinning up with it.
    private const int WarmupFrames = 45;

    private string _hold = "move_right";
    private int _every = 2;
    private int _frames = 8;
    private int _frame;
    private int _taken;
    private Player? _player;
    private CameraRig? _rig;

    public override void _Initialize()
    {
        if (!Display.Required(this, "GaitShot"))
            return;

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("hold:", System.StringComparison.Ordinal))
                _hold = argument[5..];

            if (argument.StartsWith("every:", System.StringComparison.Ordinal)
                && int.TryParse(argument[6..], out int every) && every > 0)
                _every = every;

            if (argument.StartsWith("frames:", System.StringComparison.Ordinal)
                && int.TryParse(argument[7..], out int frames) && frames > 0)
                _frames = frames;
        }

        var scene = GD.Load<PackedScene>(ScenePath)?.Instantiate();
        if (scene == null)
        {
            GD.PushError($"Missing {ScenePath}");
            Quit(1);
            return;
        }

        Fresh.Profile(scene);
        GetRoot().AddChild(scene);

        _player = scene.GetNodeOrNull<Player>("Player");
        _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");
    }

    public override bool _Process(double delta)
    {
        _frame++;

        if (_player == null || _rig == null)
        {
            GD.PushError("GaitShot: no player or no camera rig");
            Quit(1);
            return true;
        }

        // Into the open before anything is held. The spawn is not guaranteed to
        // be clear, and a strip of a body pressed against a wall is a strip of
        // the collision response rather than of the gait.
        if (_frame == 1)
        {
            _player.GlobalPosition = Vector3.Zero;
            _player.Velocity = Vector3.Zero;
        }

        if (_frame == 5)
            Input.ActionPress(_hold);

        if (_frame < WarmupFrames || _taken >= _frames)
        {
            if (_taken < _frames)
                return false;

            Input.ActionRelease(_hold);
            return true;
        }

        if ((_frame - WarmupFrames) % _every != 0)
            return false;

        Vector2 travel = new(_player.Velocity.X, _player.Velocity.Z);
        Vector2 facing = _player.Facing;

        // The number the picture is of: how far the direction of travel is from
        // the direction the body is pointing. Zero walking forward, ninety
        // strafing, and the gait swings along the *facing* whatever this says.
        float off = travel.LengthSquared() < 0.0001f
            ? 0.0f
            : Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(travel.Normalized().Dot(facing), -1.0f, 1.0f)));

        // And against the view's right, which is the axis a strafe is supposed to
        // run along. Printing both is what turns "the number is wrong" into
        // "which of the two bases disagrees".
        Vector2 forward = _rig.Forward();
        var right = new Vector2(-forward.Y, forward.X);
        float offRight = travel.LengthSquared() < 0.0001f
            ? 0.0f
            : Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(travel.Normalized().Dot(right), -1.0f, 1.0f)));

        string path = $"res://screenshots/gait_{_taken:00}.png";
        Image image = GetRoot().GetTexture().GetImage();
        Error err = image.SavePng(ProjectSettings.GlobalizePath(path));

        GD.Print($"{path}  speed {travel.Length():F2} m/s  "
               + $"travel {off:F0}° off the facing, {offRight:F0}° off the view's right  "
               + $"(yaw {Mathf.RadToDeg(_rig.Yaw):F0}°)  "
               + (err == Error.Ok ? "ok" : $"SavePng failed: {err}"));

        _taken++;
        return false;
    }
}
