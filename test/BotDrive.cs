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
    /// Below this, a component of the desired direction is not worth a key.
    ///
    /// 0.08 is about 5°. Its only job is to stop a bot walking almost due north
    /// from also tapping `move_right` sixty times a second — the movement would
    /// be within a degree of correct and the input log would read as a tremor.
    private const float Deadband = 0.08f;

    private static readonly string[] Actions =
        { "move_up", "move_down", "move_left", "move_right" };

    /// Presses the keys that move toward `desired`.
    ///
    /// `yaw` is the rig's, and `desired` is in world XZ — the same space
    /// `CameraRig.Forward` returns, so a caller with a flow-field direction can
    /// pass it straight in.
    ///
    /// **This file used to be forty lines of turning geometry and it is now a
    /// projection onto two axes, because the game's controls changed under it.**
    /// The note above is kept because it is the reason this class exists at all:
    /// `[A]`/`[D]` turned the view, so a direction did *not* decompose into four
    /// independent keys, and a driver that assumed it did spun on the spot
    /// forever while every diagnostic agreed the route was correct.
    ///
    /// `Player.Steer` strafes now. A direction decomposes into four independent
    /// keys again — which is what every driver in this repository assumed in the
    /// first place — and the turning circle that made two of `BalanceSweep`'s
    /// twelve seeds orbit a crate 2.3 m away does not exist, because nothing
    /// turns to move.
    ///
    /// `distance` and `turnRadius` are still accepted and are no longer read. A
    /// turning radius is v/ω and ω is zero now. They stay in the signature rather
    /// than being taken out of three callers for no behavioural gain, and they
    /// stay documented as dead so that nobody tunes them.
    public static void Steer(Vector2 desired, float yaw,
                             float distance = float.PositiveInfinity,
                             float turnRadius = 0.0f)
    {
        if (desired.LengthSquared() < 0.000001f)
        {
            Release();
            return;
        }

        Vector2 target = desired.Normalized();
        Vector2 forward = CameraRig.Forward(yaw);

        // `Right` is `Forward` turned a quarter clockwise seen from above, which
        // is the same basis `Player.Steer` builds. Both have to agree or the bot
        // presses the key for the opposite side, and the failure looks like a
        // pathing bug rather than a sign error.
        var right = new Vector2(-forward.Y, forward.X);

        float along = forward.Dot(target);
        float across = right.Dot(target);

        // `move_up` is −Y, so forward is the up key. Two keys at once is an
        // ordinary diagonal and `Player.Steer` normalises the pair, so a bot can
        // hold any of the eight directions a player can and no more.
        Set("move_up", along > Deadband);
        Set("move_down", along < -Deadband);
        Set("move_right", across > Deadband);
        Set("move_left", across < -Deadband);
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
