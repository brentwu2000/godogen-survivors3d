shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

uniform sampler2D surface_detail : source_color, filter_linear_mipmap, repeat_enable;
uniform float surface_detail_scale = 3.2;
uniform float surface_detail_strength : hint_range(0.0, 1.0) = 0.52;

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

// How far an elite's colour is pushed, per unit of INSTANCE_CUSTOM.y.
//
// The rotation is small on purpose — about a twelfth of a turn at the top mark,
// against the two thirds it used to be. It is enough to separate the three kinds
// from each other and not enough to leave the palette.
const float ELITE_HUE = 0.075;
const float ELITE_SATURATE = 0.55;
const float ELITE_DARKEN = 0.28;

varying vec3 body_tint;
varying vec3 detail_position;
varying vec3 detail_normal;

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

    // Texture coordinates are occupied by the animation rig. Keep the animated
    // model-space position and normal for triplanar projection instead, so a
    // painted texture can wrap every procedural and baked body without changing
    // the gait data or requiring a second draw pass.
    detail_position = VERTEX;
    detail_normal = NORMAL;

    body_glow = 1.0 - COLOR.a;

    // An elite is the same creature gone *wrong*, not the same creature in a
    // different colour.
    //
    // **The mark used to be a hue rotation of up to two thirds of a turn, and it
    // was the loudest thing on screen.** `shift_hue` preserves luminance by
    // design, which is correct arithmetic and the wrong goal: rotating a muted
    // mid-value colour produces lavender and mint, so on a grim palette the most
    // dangerous bodies in the crowd were also the prettiest. An assessment of the
    // game called them "accidentally tinted rather than more dangerous", which is
    // exactly right.
    //
    // What reads as dangerous against this palette is *deeper and dirtier*: a
    // small hue push so the kind is still distinguishable, then saturation up and
    // value down. The silhouette is untouched and the body still belongs to its
    // variant — it has simply hardened.
    float elite = INSTANCE_CUSTOM.y;

    vec3 colour = shift_hue(COLOR.rgb, elite * ELITE_HUE);

    if (elite > 0.0) {
        // Away from its own grey, which is saturation without a colour-space
        // conversion. Anything already near neutral barely moves, which is
        // correct: a grey creature's elite mark should be that it went darker,
        // not that it turned blue.
        float grey = dot(colour, vec3(0.2126, 0.7152, 0.0722));
        colour = mix(vec3(grey), colour, 1.0 + ELITE_SATURATE);

        colour *= 1.0 - ELITE_DARKEN;
        colour = max(colour, vec3(0.0));
    }

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
    vec3 weights = pow(abs(normalize(detail_normal)), vec3(4.0));
    weights /= max(weights.x + weights.y + weights.z, 0.0001);

    vec3 sample_x = texture(surface_detail, detail_position.zy * surface_detail_scale).rgb;
    vec3 sample_y = texture(surface_detail, detail_position.xz * surface_detail_scale).rgb;
    vec3 sample_z = texture(surface_detail, detail_position.xy * surface_detail_scale).rgb;
    float painted = dot(sample_x * weights.x + sample_y * weights.y + sample_z * weights.z,
                        vec3(0.2126, 0.7152, 0.0722));

    // Re-centre the neutral grayscale map around one before multiplying it over
    // the authored vertex palette. This preserves enemy colour readability while
    // adding actual cloth grain, chipped paint, pores and grime to every surface.
    float detail_value = mix(1.0, clamp(0.58 + painted * 0.82, 0.58, 1.28),
                             surface_detail_strength);
    ALBEDO = body_tint * detail_value;

    // Emission, not a light. The mobile renderer has no global illumination and
    // this is a hundred and fifty bodies deep, so the sac brightens itself and
    // nothing around it. What sells it is the *contrast*: the rest of the
    // creature is the darkest thing in the set, so a bright patch on it reads as
    // a source even though it lights nothing.
    EMISSION = body_tint * body_glow * GLOW_STRENGTH;

    // Vertex colours arrive linear — MeshBuilder converts them once, at build
    // time, because a vertex colour is handed to the shader as-is while a texture
    // is decoded from sRGB first. Nothing to undo here.
    ROUGHNESS = clamp(0.72 + (1.0 - painted) * 0.24, 0.68, 0.96);
    METALLIC = 0.0;
}
