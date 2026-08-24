shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

// The walk, and the only place a body is animated.
//
// A MultiMesh has no skeleton. There is one mesh and N transforms, and nothing
// per-instance for a bone to hang from — so the stride is a function evaluated
// here, per vertex, from data baked into the mesh at build time.
//
// What the mesh carries (written by MeshBuilder.SetRig):
//
//   UV.x    swing, radians at full pace. Zero for anything that does not move.
//   UV.y    the height this limb pivots about.
//   UV2.x   phase offset in turns. 0.5 makes the left leg the opposite of the
//           right one.
//   UV2.y   vertical bob in metres at full pace.
//
// What the instance carries (written by BodyRenderer):
//
//   INSTANCE_CUSTOM.x   pace and phase, packed. Integer part is pace in
//                       quarter-metres per second; fraction is the stride phase.
//   INSTANCE_CUSTOM.y   hue shift in turns, for elite marks.
//   INSTANCE_CUSTOM.z   hit flash, 0 to 1.
//   INSTANCE_CUSTOM.w   per-instance brightness jitter, -1 to 1.
//
// INSTANCE_CUSTOM, not CUSTOM0. CUSTOM0 is a *mesh vertex* attribute and is the
// same for every instance; reading it here would give every body in the horde
// an identical stride and no amount of staring at the C# would explain why.

// Metres per second per integer unit of the packed pace.
const float PACE_QUANTUM = 0.25;

// The speed a stride is scaled against. A walker moves at 2.4 m/s and swings its
// legs the amount the mesh asks for; a runner at 4.6 exceeds it and swings
// further, which is most of what makes the two read as different animals from a
// distance.
const float REFERENCE_SPEED = 2.4;

varying vec3 body_tint;

// How much of this vertex is lit from inside, 0 to 1.
//
// Carried in the **alpha of the vertex colour**, which was the only channel
// left and was being written and ignored. INSTANCE_CUSTOM is full — pace and
// phase packed into one float, hue shift, hit flash, brightness jitter — and it
// is per-instance anyway, which is wrong for this: a glow belongs to a *part* of
// a body, not to a whole one. A lantern mutant is a dark thing with a bright
// sac, and the sac is what the player is meant to see coming.
//
// Inverted so that opaque means unlit. Everything already in the game writes
// alpha 1 and glows not at all, so this arrives costing nothing and changing
// nothing.
varying float body_glow;

// How brightly a fully glowing vertex burns. High enough to survive the fog it
// is meant to be seen through, and not so high that it lights the crowd around
// it — this is emission on a mobile renderer, so it is a colour on the surface
// rather than a light in the world, and a value that reads as "lit" up close
// reads as "white blob" at twenty metres.
const float GLOW_STRENGTH = 2.4;

// Rotate about the grey axis, then put the luminance back.
//
// The obvious way to mark an elite is to multiply its colour by a tint, and it
// goes wrong precisely where it matters: a red tint on a green body gives black.
// Saturated vertex colours are exactly what this game's palette is made of, so
// multiplying turns the most distinctive variants into silhouettes.
//
// Rodrigues rotation about (1,1,1) keeps r+g+b fixed, which is close to
// luminance but not equal to it; the rescale afterwards makes it exact under
// Rec. 709. The result is the same body, the same brightness, a different colour
// — which is what an elite mark has to be to stay readable in a crowd.
vec3 shift_hue(vec3 colour, float turns) {
    if (turns == 0.0) {
        return colour;
    }

    const vec3 grey = vec3(0.5773502691896258);
    const vec3 luma = vec3(0.2126, 0.7152, 0.0722);

    float angle = turns * TAU;
    float c = cos(angle);
    float s = sin(angle);

    vec3 rotated = colour * c + cross(grey, colour) * s + grey * dot(grey, colour) * (1.0 - c);
    rotated = max(rotated, vec3(0.0));

    float before = dot(colour, luma);
    float after = dot(rotated, luma);
    return rotated * (before / max(after, 0.0001));
}

void vertex() {
    float swing = UV.x;
    float pivot_y = UV.y;
    float phase_offset = UV2.x;
    float bob = UV2.y;

    float packed = INSTANCE_CUSTOM.x;

    // floor and fract, which is the whole reason for the packing: four custom
    // floats, and pace and phase are the fifth and sixth things that needed one.
    // Pace is quantised to a quarter of a metre per second — far finer than the
    // eye can separate in a swing amplitude — which leaves eighteen bits of
    // mantissa for the phase, or about a quarter of a millionth of a stride.
    float pace = floor(packed) * PACE_QUANTUM;
    float phase = fract(packed);

    // Standing still is legs at rest, not legs frozen mid-stride. That falls out
    // of scaling by pace rather than gating on it, and it is the reason the phase
    // is carried per instance instead of being derived from world position: a
    // body pushed backwards by a shotgun would otherwise walk backwards, and a
    // crowd standing on the same spot would stand in lockstep.
    float amount = clamp(pace / REFERENCE_SPEED, 0.0, 1.6);
    float angle = sin((phase + phase_offset) * TAU) * swing * amount;

    float c = cos(angle);
    float s = sin(angle);

    // About the X axis through the pivot height. Fore-and-aft is the only way a
    // leg swings, so there is no second axis to store, and the vertex's own X is
    // already where it needs to be.
    vec3 local = VERTEX;
    local.y -= pivot_y;
    VERTEX = vec3(local.x, local.y * c - local.z * s + pivot_y, local.y * s + local.z * c);

    // The normal turns with the limb. Without this a swinging arm keeps the
    // lighting it had at rest, which reads as the limb being lit from inside —
    // and it is the kind of thing that looks like a shadow bug for a week.
    NORMAL = vec3(NORMAL.x, NORMAL.y * c - NORMAL.z * s, NORMAL.y * s + NORMAL.z * c);

    // Twice a stride: the body rises on each footfall, and there are two feet.
    VERTEX.y += bob * amount * sin(phase * 2.0 * TAU);

    body_glow = 1.0 - COLOR.a;

    vec3 colour = shift_hue(COLOR.rgb, INSTANCE_CUSTOM.y);

    // A crowd of identical bodies reads as a texture rather than as a crowd.
    // Brightness only — jittering hue as well would fight the elite mark, which
    // is the one colour difference that has to mean something.
    colour *= 1.0 + INSTANCE_CUSTOM.w * 0.12;

    // Toward white on a hit, never past it. Added rather than mixed so a dark
    // body flashes as brightly as a pale one; a mix toward white leaves a nearly
    // black body nearly black at full flash, which is where the feedback is
    // needed most.
    body_tint = colour + vec3(INSTANCE_CUSTOM.z * 0.8);
}

void fragment() {
    ALBEDO = body_tint;

    // Emission, not a light. The mobile renderer has no global illumination and
    // this is a hundred and fifty bodies deep, so the sac brightens itself and
    // nothing around it. What sells it is the *contrast*: the rest of the
    // creature is the darkest thing in the set, so a bright patch on it reads as
    // a source even though it lights nothing.
    EMISSION = body_tint * body_glow * GLOW_STRENGTH;

    // Vertex colours arrive linear — MeshBuilder converts them once, at build
    // time, because a vertex colour is handed to the shader as-is while a texture
    // is decoded from sRGB first. Nothing to undo here.
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
