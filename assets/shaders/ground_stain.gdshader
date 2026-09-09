// What a fight leaves on the floor: blood under a body, scorch under a blast.
//
// The complaint this answers is that a cleared field looked exactly like a field
// nothing had happened in. Every piece of feedback the game had was gone inside
// a third of a second, so the player could stand on ground they had killed forty
// things over and read nothing from it.
//
// Not billboarded. `MarkField` writes a real basis, tilted to the ground's own
// normal, because a mark is the one effect in this game that is genuinely flat —
// and a camera-facing quad lying on the floor is the shape that gives 2.5D away.
shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform sampler2DArray marks : source_color, filter_linear;

varying vec4 v_tint;
varying flat float v_layer;

void vertex() {
    v_tint = COLOR;
    v_layer = INSTANCE_CUSTOM.x;
}

void fragment() {
    vec4 texel = texture(marks, vec3(UV, v_layer));
    ALBEDO = texel.rgb * v_tint.rgb;
    ALPHA = texel.a * v_tint.a;
}
