// Flat discs on the floor that have to read as something: burning ground, and an
// extraction pad.
//
// Both were a solid colour at fixed alpha, which is the most recognisably
// unfinished thing a 3D scene can contain — a seven-metre orange ellipse sitting
// on the road does not read as fire, it reads as a placeholder someone forgot.
// One shader serves both because the difference between them is a colour and a
// speed, not a technique.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 inner_colour : source_color = vec4(1.0, 0.75, 0.25, 1.0);
uniform vec4 outer_colour : source_color = vec4(0.9, 0.18, 0.03, 1.0);

uniform float strength : hint_range(0.0, 2.0) = 0.75;

// How fast the edge crawls and the interior flickers. Fire is fast and unsteady;
// a pad is slow and even, and that difference is most of what separates "this is
// dangerous" from "this is the way out".
uniform float churn = 2.3;
uniform float flicker : hint_range(0.0, 1.0) = 0.3;

// Ring rather than disc. The pad wants an outline the player can stand inside
// and still see the ground under their feet.
uniform float hollow : hint_range(0.0, 1.0) = 0.0;

// Per-instance offset, so eight patches of fire are not one animation played
// eight times in perfect unison.
uniform float seed = 0.0;

void fragment() {
    vec2 p = UV - vec2(0.5);
    float r = length(p) * 2.0;
    if (r > 1.0) {
        discard;
    }

    float angle = atan(p.y, p.x);

    // A ragged, moving edge. A perfect circle is the tell that something was
    // drawn by a mesh rather than by whatever it is meant to be.
    float wobble = sin(angle * 7.0 + TIME * churn + seed) * 0.055
                 + sin(angle * 13.0 - TIME * churn * 0.7 + seed * 1.7) * 0.035;

    float edge = smoothstep(1.0, 0.55, r + wobble);
    if (hollow > 0.0) {
        edge *= smoothstep(hollow - 0.35, hollow, r + wobble * 0.5);
    }

    float cells = sin(p.x * 18.0 + TIME * churn * 1.4 + seed)
                * sin(p.y * 15.0 - TIME * churn * 1.1 + seed);

    float heat = clamp(edge * (1.0 - flicker + flicker * (0.5 + 0.5 * cells)), 0.0, 1.0);

    ALBEDO = mix(outer_colour.rgb, inner_colour.rgb, heat * heat);
    ALPHA = heat * strength;
}
