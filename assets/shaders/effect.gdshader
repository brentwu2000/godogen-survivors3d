// Additive billboard puffs: muzzle flashes, impacts, sparks, fire, blasts.
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

#include "res://assets/shaders/effect_body.gdshaderinc"
