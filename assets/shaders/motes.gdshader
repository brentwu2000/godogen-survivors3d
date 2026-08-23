shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_add, shadows_disabled;

// Dust in the air, moved entirely on the GPU.
//
// Four hundred motes, one buffer upload at startup and nothing per frame. Each
// mote's home and phase live in its per-instance custom data; the position is
// `home + drift * TIME` wrapped back into the slab with `fract`, so the field
// drifts forever without the CPU ever touching it again.
//
// **`INSTANCE_CUSTOM`, not `CUSTOM0`.** This is the trap that cost a phase
// elsewhere in this project and it compiles cleanly either way. `CUSTOM0` is a
// *mesh vertex* attribute — every mote shares one mesh, so all four hundred read
// the same value, every position collapses to the same point, and the entire
// field renders as a single dot. Nothing errors. `INSTANCE_CUSTOM` is the
// per-instance block, which is what a MultiMesh actually fills.

// Metres the slab spans, matching `AirDust.SlabSize`. The wrap is in normalised
// units and this converts back.
uniform vec3 slab = vec3(34.0, 7.0, 34.0);

// How far a mote travels in a second, in normalised slab units. Slow: this is
// air, not weather.
uniform float drift_speed = 0.018;

// How much it settles. Slightly more than it wanders sideways, so the field
// reads as falling rather than as swirling.
uniform float fall_speed = 0.030;

uniform vec4 tint : source_color = vec4(0.82, 0.80, 0.74, 1.0);

// Found by turning it up. At 1.0 with fourteen-centimetre motes this is heavy
// snowfall, which is how the shader was confirmed to be working at all before it
// was turned down to something that reads as air.
uniform float brightness : hint_range(0.0, 1.0) = 0.11;

void vertex() {
    vec3 home = INSTANCE_CUSTOM.xyz;
    float phase = INSTANCE_CUSTOM.w;

    // A lane per mote rather than one direction for the field. Derived from the
    // phase instead of stored, because the custom block has four floats and three
    // are already spent on the home position.
    float angle = phase * 6.2831853;
    vec3 travel = vec3(cos(angle) * drift_speed, -fall_speed, sin(angle) * drift_speed);

    // Wrapped in normalised space, so a mote leaving the bottom reappears at the
    // top and the slab is never empty. `fract` of a negative is still in [0,1) in
    // GLSL, which is the whole reason the wrap can be one instruction.
    vec3 place = fract(home + travel * TIME);

    // Into the slab, centred on the node — which rides with the player, so the
    // field is always around them and never has to be rebuilt.
    VERTEX += (place - vec3(0.5)) * slab;
}

void fragment() {
    ALBEDO = tint.rgb * brightness;
    ALPHA = 1.0;
}
