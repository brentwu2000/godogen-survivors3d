// Cover, scenery and the shelter's furniture: vertex-coloured boxes, cel shaded.
//
// **This replaced a `StandardMaterial3D` and the only thing that changed is the
// lighting model.** `PropLibrary` builds every prop out of boxes and writes the
// palette into the vertex colours, so the material never had anything to do but
// read `COLOR` — which `VertexColorUseAsAlbedo` did, through Godot's own Burley
// diffuse. The horde has been banded since the phase before this one, so the
// arena was a smoothly-shaded floor and smoothly-shaded cover under a banded
// crowd, and `ART.md §9` named that as the largest remaining inconsistency in
// the look.
//
// A prop is the case banding is *most* correct for. A body is flat facets
// approximating a limb; a container is an actual box, and a box face receives
// exactly one amount of light. The gradient across it was describing nothing at
// all.
shader_type spatial;

// No `diffuse_*`: a `light()` function replaces the built-in diffuse entirely
// and the render mode would be read and ignored.
//
// Boxes are closed and correctly wound, so back faces are never needed — and
// leaving culling on is what keeps the shadow pass honest. Props *do* cast
// shadows, unlike the horde, which is why they read as standing on the ground.
render_mode cull_back, specular_disabled;

#include "res://assets/shaders/cel.gdshaderinc"

// How much of the rim the props take, as a fraction of what a body takes.
//
// Less, and the reason is what the rim is *for* on a body: separating one figure
// from the figure behind it in a crowd of a hundred and fifty. Cover is sparse
// and already separated by its own shadow, so the same strength reads as a
// glowing outline around every crate. Enough to catch the top edge of a barrier
// against the fog, which is the one place cover disappears.
uniform float rim_share : hint_range(0.0, 1.0) = 0.45;

// Two terms of baked ambient occlusion, the same pair `body.gdshader` carries
// and for the same reason: ambient here is a bright sky, and a surface pointing
// at the ground cannot see it. Without the downward term the underside of a
// dumpster is exactly as bright as its lid.
//
// There is no ground-contact term. A prop casts a real shadow, so it already has
// one; the horde's is in the albedo precisely because a hundred and fifty shadow
// casters is not the budget.
uniform float occlusion_down : hint_range(0.0, 1.0) = 0.26;

varying vec3 prop_normal;

void vertex() {
	prop_normal = NORMAL;
}

void light() {
	// `NORMAL` and `LIGHT` are both view space here and both already
	// normalised, which is worth stating because the same name means model
	// space in the vertex stage above.
	float lit = cel_lit(dot(NORMAL, LIGHT));

	// `ALBEDO` and a division by pi, both of which `body.gdshader` got wrong
	// once each on the way here. Godot 4 does not multiply `DIFFUSE_LIGHT` by
	// albedo after a custom `light()` the way it does for its own lambert, so
	// leaving albedo out adds *white* light over an albedo-tinted ambient; and
	// omitting the division by pi makes the direct term three times the ambient
	// it is meant to sit beside, which flattens the banding into one bright
	// tread.
	DIFFUSE_LIGHT += lit * ATTENUATION * LIGHT_COLOR * ALBEDO / PI;
}

void fragment() {
	// Vertex colours arrive linear. `MeshBuilder` converts them once, at build
	// time, because a vertex colour is handed to the shader as-is while a
	// texture is decoded from sRGB first — so there is nothing to undo here, and
	// converting again is how a palette comes out twice as bright as drawn.
	float under = max(0.0, -normalize(prop_normal).y);

	ALBEDO = COLOR.rgb * (1.0 - occlusion_down * under);
	EMISSION = rim_colour.rgb * cel_rim(NORMAL, VIEW) * rim_strength * rim_share;

	ROUGHNESS = 0.92;
	METALLIC = 0.0;
}
