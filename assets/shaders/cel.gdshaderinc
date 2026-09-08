// The lighting model, shared by everything solid in the arena.
//
// **Lambert is a lie this game cannot afford to tell.** A smooth cosine falloff
// describes light on a curved surface, and there are no curved surfaces here:
// bodies are flat-shaded facets and every prop is boxes. The falloff lands as a
// gradient *across a flat plane*, which is the one thing that makes low-poly
// geometry look like untextured low-poly geometry rather than like a drawing.
//
// Banding the diffuse turns each facet into one of a few flat values, and that
// happens to be exactly true: a flat facet really does receive one amount of
// light, and quantising only removes an interpolation that was never describing
// anything.
//
// **This is an include because it was in one shader and had to be in three.**
// `body.gdshader` banded the horde while `PropLibrary` handed every prop a
// `StandardMaterial3D` and the floor ran its own `diffuse_burley` — so cover and
// ground were smoothly shaded under a banded horde, which `ART.md` named as the
// largest remaining inconsistency in the game's look. A second copy of the ramp
// is how two of the three end up drifting a band apart.
//
// Uniforms rather than constants, so a material can still disagree: the ground
// takes more bands than a body does, for the reason written where it does.

// **Two, and three was tried first.** The argument for three was that a mid tone
// is where the form of a limb lives — true of a *smooth* limb, and these are
// six-sided prisms whose facets already quantise the light for free. A third
// band on top of that quantisation lands between two facet values and reads as
// noise rather than as roundness.
uniform int cel_bands : hint_range(2, 6) = 2;

// How wide each riser of the staircase is, in units of a band.
//
// Not zero, and not for taste. A hard step aliases: the terminator is a
// one-pixel line crawling across a facet as the body turns, and at a hundred and
// fifty bodies that is a field of crawling lines. Softening it over three per
// cent of a band costs nothing and is invisible except where it matters.
uniform float cel_softness : hint_range(0.0, 0.5) = 0.06;

// How dark the darkest band is, as a fraction of the brightest.
//
// A cel shadow is a *colour*, not an absence of light. At zero the unlit side of
// everything is whatever the ambient happens to be, which on this map is a blue
// sky and turns every shadow side blue. 0.32 keeps the surface's own hue in its
// shadow, which is what a painted shadow does.
uniform float cel_shadow : hint_range(0.0, 1.0) = 0.32;

// A light along the silhouette, which is the other half of the look.
//
// Fresnel against the view, so it appears exactly where a surface turns away —
// which is the edge. It does two jobs at once: it draws the outline this game
// cannot afford as geometry (an inverted-hull outline is a second draw of every
// body, and there are a hundred and fifty of them), and it separates one body
// from the body behind it in a crowd, which is the thing a flat-shaded horde is
// worst at.
uniform float rim_strength : hint_range(0.0, 2.0) = 0.10;
uniform float rim_width : hint_range(1.0, 8.0) = 6.0;
uniform vec4 rim_colour : source_color = vec4(0.72, 0.80, 0.95, 1.0);

/// One band of the staircase, with a soft riser.
///
/// `floor` gives the tread and `smoothstep` at the top of each fraction gives
/// the riser, so the result is flat across most of a band and moves quickly at
/// the edge. Dividing by the count at the end keeps the whole thing in 0..1
/// however many bands there are.
float cel_ramp(float x) {
	float steps = float(cel_bands);
	float scaled = clamp(x, 0.0, 1.0) * steps;
	float tread = floor(scaled);
	float riser = smoothstep(1.0 - cel_softness, 1.0, scaled - tread);

	return min((tread + riser) / steps, 1.0);
}

/// The banded diffuse term for a surface turned `towards` the light.
///
/// The shadow floor is applied to the *ramp*, not to the result, so the darkest
/// band still carries the light's colour and the surface's albedo. A floor
/// applied afterwards would be a grey wash over the shadow side.
float cel_lit(float towards) {
	return mix(cel_shadow, 1.0, cel_ramp(clamp(towards, 0.0, 1.0)));
}

/// The fresnel term along a silhouette, 0 in the middle and 1 at the edge.
float cel_rim(vec3 normal, vec3 view) {
	return pow(1.0 - clamp(dot(normalize(normal), normalize(view)), 0.0, 1.0), rim_width);
}
