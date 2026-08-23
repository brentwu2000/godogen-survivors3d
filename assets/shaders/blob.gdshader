// A soft dark ellipse on the ground: the billboard path's only contact shadow.
//
// A sprite standing on a floor has nothing to say where it is standing. Without
// this, an enemy walking toward the camera reads as an enemy floating toward the
// camera, and two enemies at different depths look like one in front of the
// other. That is the whole job — it is not a shadow, it is a foot.
//
// Only the fallback path uses it. The solid bodies cast real shadows into the
// shadow map, and drawing this under them as well would double every enemy's
// ground contact, which is a mistake this codebase has already made once with
// the billboards themselves.
shader_type spatial;

// Unshaded, and no depth write. It is a darkening of the floor, not a surface:
// lighting it would make the shadow brighter in the light, and writing depth
// would make two overlapping shadows fight over which is nearer when they are
// both two centimetres off the same ground.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

// How hard the edge is. Low values give a smudge, high values a dinner plate.
// 2.4 is soft enough not to read as a decal and tight enough to say where the
// feet are.
uniform float falloff : hint_range(0.5, 6.0) = 2.4;

// **The per-instance colour has to be forwarded by hand.**
//
// A MultiMesh writes its instance colour into `COLOR` in the *vertex* stage.
// `COLOR` in the fragment stage is the interpolated vertex-colour *attribute*,
// and a `QuadMesh` has no such attribute — so reading `COLOR` in `fragment()`
// gives whatever an absent attribute gives, which here was an alpha of zero.
//
// The result was a renderer that placed, sized, oriented and uploaded every blob
// correctly and drew nothing at all. It survived a four-stage probe, because
// everything a probe can read lives in the buffer, and it survived being turned
// up to full opacity and three times the size. What identified it was swapping
// the shader for a flat red `StandardMaterial3D` and watching the screen fill
// with red.
varying vec4 instance_colour;

void vertex() {
    instance_colour = COLOR;
}

void fragment() {
    // The quad's own UV, so the middle of the blob is the middle of the mesh
    // whatever the instance scale is.
    float r = length(UV - vec2(0.5)) * 2.0;

    // Smooth to zero at the rim. `1.0 - r` alone leaves a visible circular cut
    // where the alpha reaches zero, because it reaches it linearly.
    float mask = clamp(1.0 - r, 0.0, 1.0);
    mask = pow(mask, falloff);

    ALBEDO = vec3(0.0);
    ALPHA = mask * instance_colour.a;
}
