using Godot;

/// Which way to walk when the flow field will not answer.
///
/// A `FlowField` inflates obstacles by 0.55 m before marking them, and the
/// player's collision radius is 0.35 — so the blocked band reaches about a metre
/// past anything a body can actually touch, and `Sample` returns zero throughout
/// it. That band is a margin, not a wall, and the two ways of reading it are the
/// two bugs this class exists to hold apart:
///
/// - A bot **leaning on a wall** is in the band with nowhere to go, and the
///   straight line to its target points into the wall. It has to be told which
///   way is out. That is `EscapeFrom`, and it was the fix for a bot that spent
///   sixty seconds against the south face of an eight-metre wall.
/// - A bot **clipping the band while turning through a gap** is in exactly the
///   same state and needs the opposite answer. Turn-and-advance arcs, so a driver
///   the field has pointed at a gap will drift a metre sideways on the way in;
///   escaping then sends it back the way it came, the field points at the gap
///   again, and the two correct answers make a loop. Seed `0xD6E8FEB1` ran that
///   cycle for a whole leg on a six-second period — nineteen metres from its
///   crate, four metres of travel every ten seconds, every step defensible.
///
/// Nothing in a single frame distinguishes them, which is why this holds state.
/// The last heading the field gave wins for `BandPushTicks` — long enough to
/// cross the margin, short enough that a bot against something real gives up
/// inside a second — and only then does the escape run. The collider decides
/// whether the gap was real; that is what a collider is for.
public sealed class RouteMemory
{
    /// Three quarters of a second: four and a half metres at walking pace,
    /// against a margin about a metre wide.
    public const int BandPushTicks = 45;

    private Vector2 _lastFlow;
    private int _bandTicks;

    /// `flow` is `FlowField.Sample`, `escape` is `FlowField.EscapeFrom`, and
    /// `straight` is the direct line to the target — the answer when the target
    /// is genuinely unreachable rather than merely inconvenient.
    public Vector2 Choose(Vector2 flow, Vector2 escape, Vector2 straight)
    {
        if (flow != Vector2.Zero)
        {
            _lastFlow = flow;
            _bandTicks = 0;
            return flow;
        }

        _bandTicks++;
        if (_bandTicks <= BandPushTicks && _lastFlow != Vector2.Zero)
            return _lastFlow;

        if (escape != Vector2.Zero)
        {
            // Committed to leaving, and the field cannot un-commit it on the next
            // frame. Without this the cell it escapes into hands back the heading
            // that walked it into the band, and one tick of that puts it straight
            // back — which is the loop with an extra step in it rather than a fix.
            _lastFlow = Vector2.Zero;
            return escape;
        }

        return straight;
    }
}
