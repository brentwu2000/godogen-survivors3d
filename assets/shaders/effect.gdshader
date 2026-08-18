// Additive billboard puffs: muzzle flashes, impacts, deaths, blasts.
//
// A separate shader from the horde's rather than a flag on it. That one discards
// on alpha so hundreds of overlapping sprites need no sorting; this one has to
// blend, because a flash that is either fully there or fully gone cannot fade —
// and fading is the entire effect.
//
// Blending is affordable here for the reason it was not there: there are dozens
// of these, not hundreds, they are small and bright, and additive blending is
// order-independent by construction, so the sorting problem that ruled it out
// for the horde does not exist.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform sampler2D puff : source_color, filter_linear;

varying vec4 v_tint;

void vertex() {
    // Per-instance colour and alpha. The alpha is the fade and the rgb is what
    // kind of thing went off — one mesh, one material, every effect in the game.
    v_tint = COLOR;

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
    vec4 texel = texture(puff, UV);
    ALBEDO = texel.rgb * v_tint.rgb;
    ALPHA = texel.a * v_tint.a;
}
