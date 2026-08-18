// Screen-edge tint for the two things the player must not have to read a number
// to notice: taking a hit, and being nearly dead.
//
// A flat translucent rectangle would do neither. Damage that dims the whole
// screen also dims the horde the player is trying to see, and a constant tint at
// low health becomes invisible within seconds because the eye adapts to anything
// uniform. Weight at the edges leaves the middle — where the player, the crowd
// and the crate all are — untouched, and edges are where peripheral vision is
// most sensitive to change.
shader_type canvas_item;

uniform vec4 tint : source_color = vec4(0.65, 0.05, 0.05, 1.0);

// 0 is off. Driven from code, so one rectangle serves both the hit flash (spikes
// and decays) and the low-health state (slow pulse).
uniform float strength : hint_range(0.0, 1.0) = 0.0;

// Where the darkening starts, as a fraction of the way from centre to corner.
uniform float inner : hint_range(0.0, 1.0) = 0.62;

void fragment() {
    // Scaled so the corner reads as 1.0 rather than 0.707, otherwise the falloff
    // never completes and the edges stay half lit at full strength.
    float d = distance(UV, vec2(0.5)) * 1.41421356;
    COLOR = vec4(tint.rgb, smoothstep(inner, 1.0, d) * strength);
}
