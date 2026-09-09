// The body of both puff renderers. Two shaders include it because `render_mode`
// is the one thing a shader cannot decide per instance, and the blend mode is
// exactly what separates a spark from smoke.
//
// Additive is right for anything that emits: a muzzle flash, a spark, fire. It
// is wrong for everything that *occludes*, and that was invisible while the game
// was played at dusk — additive grey over a near-black field reads as smoke
// because the field has nowhere to go but up. Over a daylight ground at 0.5 the
// same grey lightens what is behind it, so every plume of smoke in the game was
// drawn as haze. The soft channel blends instead, so smoke can be darker than
// what it hangs in front of.

uniform sampler2DArray shapes : source_color, filter_linear;

varying vec4 v_tint;
varying flat float v_layer;

void vertex() {
    // Per-instance colour and alpha. The alpha is the fade and the rgb is what
    // kind of thing went off — one mesh, one material, every effect in the game.
    v_tint = COLOR;

    // INSTANCE_CUSTOM.x: which shape this puff draws as — a soft ball, a hard
    //                    flash with spikes, or a lump of smoke.
    // INSTANCE_CUSTOM.y: in-plane rotation, radians. A four-pointed flash drawn
    //                    at the same angle every shot reads as a decal.
    v_layer = INSTANCE_CUSTOM.x;

    float spin = INSTANCE_CUSTOM.y;
    if (spin != 0.0) {
        float s = sin(spin);
        float c = cos(spin);
        VERTEX.xy = vec2(VERTEX.x * c - VERTEX.y * s, VERTEX.x * s + VERTEX.y * c);
    }

    // Same billboard rebuild as the horde: keep the instance's world position,
    // take the camera's basis, and re-apply the scale the MultiMesh baked into
    // column 0 — overwriting MODELVIEW_MATRIX otherwise throws that scale away
    // and every puff renders at one unit.
    vec3 world_origin = MODEL_MATRIX[3].xyz;
    float scale = length(MODEL_MATRIX[0].xyz);

    mat4 billboard = mat4(
        INV_VIEW_MATRIX[0] * scale,
        INV_VIEW_MATRIX[1] * scale,
        INV_VIEW_MATRIX[2] * scale,
        vec4(world_origin, 1.0));

    MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}

void fragment() {
    vec4 texel = texture(shapes, vec3(UV, v_layer));
    ALBEDO = texel.rgb * v_tint.rgb;
    ALPHA = texel.a * v_tint.a;
}
