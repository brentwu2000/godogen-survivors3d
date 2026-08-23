using Godot;

/// Turns a world direction into the keys a player would press to go there.
///
/// This exists because `Player.Steer` made `[A]`/`[D]` turn the view instead of
/// strafing, and every automated driver in this repository was built on the
/// assumption that a direction decomposes into four independent keys. Under
/// turn-and-advance it does not, and the failure is quiet in the worst way: a
/// driver aiming at something to its right presses `move_right`, the view swings
/// past the target, the driver keeps pressing `move_right` because the target is
/// still not ahead, and it spins on the spot forever. It moves — the shake and
/// the follow keep the transform changing — so a stuck detector never fires. It
/// simply never arrives:
///
///     AUTOPLAY FAILED — could not reach Crate5 in 60s
///     (still 57.6m away, peeled off geometry 0x)
///
/// Three drivers need this: `AutoPlay`, `Presentation`, and the shelter probe.
/// One copy, because the sign conventions below are exactly the kind of thing
/// that gets fixed in one file and left wrong in the other two.
public static class BotDrive
{
    /// How closely aligned counts as aligned, as a dot product.
    ///
    /// 0.995 is about 5.7°. Not tighter: the turn is applied once per physics
    /// tick at 150°/s, so a single tick moves the heading 2.5° and a deadband
    /// narrower than that would overshoot, correct, overshoot, and read as a bot
    /// with a tremor. Not looser either — 10° of error over a 40 m walk is 7 m
    /// off, which is a miss.
    private const float Aligned = 0.995f;

    private static readonly string[] Actions =
        { "move_up", "move_down", "move_left", "move_right" };

    /// Presses the keys that steer toward `desired` and advance along it.
    ///
    /// `yaw` is the rig's, and `desired` is in world XZ — the same space
    /// `CameraRig.Forward` returns, so a caller with a flow-field direction can
    /// pass it straight in.
    public static void Steer(Vector2 desired, float yaw)
    {
        if (desired.LengthSquared() < 0.000001f)
        {
            Release();
            return;
        }

        Vector2 target = desired.Normalized();
        Vector2 forward = CameraRig.Forward(yaw);

        float aligned = forward.Dot(target);

        // The z-component of forward × target, which is positive when the target
        // is clockwise from the heading seen from above — the direction
        // `move_right` turns. Deriving the side from a cross product rather than
        // from comparing angles avoids the wrap at ±π, which is where an
        // angle-difference bot turns the long way round for no visible reason.
        float side = forward.X * target.Y - forward.Y * target.X;

        // Dead astern. The cross product is zero at exactly 180° and the bot
        // would press neither key and advance backwards away from the target
        // forever — the one input where "no correction needed" and "maximum
        // correction needed" produce the same number. Pick a side; either is
        // half a turn.
        if (side == 0.0f && aligned < 0.0f)
            side = 1.0f;

        bool turning = aligned < Aligned;
        Set("move_right", turning && side > 0.0f);
        Set("move_left", turning && side < 0.0f);

        // Advance while still turning, as long as the target is not behind.
        // Waiting for the turn to finish before moving produces a bot that stops
        // dead at every corner, which is both slower and — because it is standing
        // still in a horde while it turns — a different game from the one being
        // measured.
        Set("move_up", aligned > 0.0f);
        Set("move_down", false);
    }

    /// Lets go of everything this class presses.
    ///
    /// Separate from the drivers' own cleanup because a driver that also presses
    /// pick keys should not have to know which of its actions came from here.
    public static void Release()
    {
        foreach (string action in Actions)
        {
            if (Input.IsActionPressed(action))
                Input.ActionRelease(action);
        }
    }

    /// Idempotent. `Input.ActionPress` on an already-pressed action re-fires the
    /// just-pressed edge, which would make a driver holding a direction look like
    /// a player tapping it sixty times a second to anything reading
    /// `IsActionJustPressed`.
    private static void Set(string action, bool held)
    {
        if (held)
        {
            if (!Input.IsActionPressed(action))
                Input.ActionPress(action);
        }
        else if (Input.IsActionPressed(action))
        {
            Input.ActionRelease(action);
        }
    }
}
