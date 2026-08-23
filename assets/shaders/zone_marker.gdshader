shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_add;

// The boundary of a danger zone, drawn as a boundary.
//
// A separate shader from `ground_marker`, which is radial. That one is right for
// what it does — extraction pads and burning ground are circles — and putting it
// on a 26 by 20 metre plane produces a soft elliptical blob with no visible edge
// anywhere. Which was the first version, and it failed at the only job the marker
// has: the entire design of a zone is that enemies arrive at the perimeter and
// backing out through it is a move, so a player who cannot see where the line is
// has been given a harder fight and no information.
//
// Chebyshev distance in UV space, which is a rectangle by construction and
// matches whatever the plane's dimensions happen to be without being told them.

uniform vec4 edge_colour : source_color = vec4(0.62, 0.68, 0.82, 1.0);
uniform vec4 fill_colour : source_color = vec4(0.20, 0.26, 0.40, 1.0);

// Overall brightness. The one thing that changes between dormant, live and
// spent, along with the two colours.
uniform float strength : hint_range(0.0, 2.0) = 0.35;

// How much of the half-width the glowing band occupies. Narrow: a thick band is
// an area, and the point is a line.
uniform float band : hint_range(0.005, 0.4) = 0.055;

// How strongly the interior is tinted. Low — the player has to be able to read
// the ground they are standing on, and a filled rectangle is a sticker.
uniform float fill : hint_range(0.0, 1.0) = 0.10;

uniform float pulse_speed = 1.1;
uniform float seed = 0.0;

void fragment() {
    // 0 at the centre, 1 exactly on the boundary. Chebyshev rather than
    // Euclidean is the whole difference between a rectangle and an ellipse.
    vec2 from_centre = abs(UV - vec2(0.5)) * 2.0;
    float d = max(from_centre.x, from_centre.y);

    // Inside the band and inside the rectangle. `1.0 - band` rather than a
    // symmetric window, so the line sits just within the edge — a band centred
    // on the boundary would be half outside the plane and clipped to half
    // brightness.
    float edge = smoothstep(1.0 - band, 1.0 - band * 0.35, d) * (1.0 - smoothstep(0.985, 1.0, d));

    // Breathing, so a dormant zone reads as waiting rather than as decal. Slow:
    // anything quick competes with the horde for attention, and the horde should
    // win that.
    float breath = 0.82 + 0.18 * sin(TIME * pulse_speed + seed);

    // Corners a little brighter. They are where the two edges meet and where a
    // player judging whether they are inside actually looks.
    float corner = smoothstep(0.55, 1.0, min(from_centre.x, from_centre.y));

    vec3 colour = fill_colour.rgb * fill + edge_colour.rgb * edge * (1.0 + corner * 0.6);

    // Additive, so the marker lights the ground rather than painting over it.
    // Alpha stays at one — with `blend_add` the darkness is the transparency, and
    // an alpha ramp on top of it would double-count the falloff.
    ALBEDO = colour * strength * breath;
    ALPHA = 1.0;
}
