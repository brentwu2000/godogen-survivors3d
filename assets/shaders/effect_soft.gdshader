// The occluding half of the same pool: smoke, dust kicked off the ground, and
// the dark bloom left where a body was.
//
// Identical to `effect.gdshader` in every respect but the blend. These are the
// puffs whose job is to be *darker* than the daylight behind them, which
// additive cannot express at all.
//
// Ordering is genuinely arbitrary here — one MultiMesh is one draw call and
// nothing sorts its instances — so this channel is for low-contrast, slow,
// overlapping things where two plumes in the wrong order is not a visible
// event. Anything bright and fast belongs on the additive channel.
shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

#include "res://assets/shaders/effect_body.gdshaderinc"
