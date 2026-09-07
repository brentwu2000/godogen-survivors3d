shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

// The painted surface, one layer per part of a body.
//
// **This replaced a triplanar projection, and the difference is placement.** A
// triplanar sample can say what a body is *made of* and cannot say what is
// *where*: it wrapped rot around a head exactly the way it wrapped rot around a
// shin, because it had no idea which was which. Eyes have to be at eye height.
//
// The layer index arrives in the integer part of `UV.x` and the texture
// coordinate in the fraction — see `BodyAtlas`, which owns that packing. An
// array rather than one packed image because a body is about 120 pixels tall at
// this camera, so its head samples a 16-pixel mip, and in a packed atlas the
// filter at that level averages across cell boundaries and fringes the face
// with whatever was painted beside it.
uniform sampler2DArray surface_detail : source_color, filter_linear_mipmap, repeat_enable;

// How many times a layer repeats across the part it is painted on.
//
// One, for everything with a face or a front: a head has to wear its head layer
// exactly once or the face appears twice. Limbs could take more and do not,
// because the same number keeps the material at one scale across a whole body —
// a forearm at two tiles beside a thigh at one reads as two different creatures
// joined at the elbow.
uniform float surface_detail_scale = 1.0;
uniform float surface_detail_strength : hint_range(0.0, 1.0) = 0.88;

// How much of the plate's own *colour* the body takes, on top of its value.
//
// **The plate was being thrown away three channels at a time.** Only its
// luminance was read, so a painting of rotting skin — grey-green flesh, bruised
// purple, dark red where it has split, a scrap of filthy cloth — arrived as one
// grey number and modulated brightness. Everything that made it a material
// rather than a noise field was discarded at the `dot`.
//
// The chroma is taken normalised, so it carries hue and saturation and no
// brightness at all: brightness is `surface_detail_strength`'s job, and doing it
// twice squares the contrast and makes the crevices black. At 0.4 a body keeps
// the palette identity `PaletteProbe` gates — that probe compares authored
// colours and has no renderer, and this multiplier averages to one — while
// picking up the local variation that says what it is made of.
uniform float surface_detail_chroma : hint_range(0.0, 1.0) = 0.45;

// The plate's mean, in the linear space the sampler hands it over in, and how
// far a texel either side of it moves the body. 0.21 is a mid grey once decoded
// — see `art-src/textures/plate.py`, which grades every plate to exactly this.
uniform float surface_detail_mid = 0.21;
uniform float surface_detail_gain = 2.6;

// Two terms of baked ambient occlusion, because the light in this game comes
// from a bright sky and a body under a bright sky with `diffuse_lambert` and no
// occlusion is a paper cut-out. Both are computed from data the shader already
// carries for the triplanar projection, so they cost two multiplies and nothing
// else — no second UV channel, no baked vertex data, and they apply to imported
// bodies as readily as to procedural ones.
//
// **Downward faces.** Ambient here is the sky, and a surface pointing at the
// ground cannot see it. Under the jaw, under the belly, the underside of a
// shoulder, the top of a boot: every one of those was exactly as bright as the
// surface facing the sun, which is the single thing that made these read as
// coloured shapes rather than as volumes. Only the strictly downward half is
// touched, so the average brightness of a body barely moves and the palette work
// this game has done five times is untouched.
uniform float occlusion_down : hint_range(0.0, 1.0) = 0.40;

// **Near the ground.** A contact shadow, in the albedo, because the real one
// cannot exist: the horde casts no shadows (a hundred and fifty shadow casters
// is the budget), so a body's feet meet the floor with nothing between them.
// Model space, so this is metres above the sole whatever the body's height —
// the shins and boots of a 5.5 m boss darken over the same 70 cm as a walker's,
// which is right, because a contact shadow is a fact about the floor.
uniform float occlusion_ground : hint_range(0.0, 1.0) = 0.26;
uniform float occlusion_ground_height = 0.70;

// The walk, and the only place a body is animated.
//
// A MultiMesh has no skeleton. There is one mesh and N transforms, and nothing
// per-instance for a bone to hang from — so the stride is a function evaluated
// here, per vertex, from data baked into the mesh at build time.
//
// What the mesh carries (written by MeshBuilder):
//
//   UV      where this vertex is painted in the body atlas. **This channel used
//           to hold half the rig**, and freeing it is the whole reason these
//           bodies can carry a face.
//   UV2.x   swing, radians at full pace. Zero for anything that does not move.
//   UV2.y   the pivot height and the phase offset, packed as
//           `floor(pivotY * 100) + phase`. Same trick as INSTANCE_CUSTOM.x
//           below, and for the same reason: a centimetre of quantisation on a
//           rotation pivot is invisible, and a body with no texture is not.
//
// The bob is not per-vertex and never was. It is one number per *body*, so it
// arrives as the uniform `body_bob` on the material, which every variant already
// has its own copy of.
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

// How far this body rises on each footfall, in metres at full pace.
//
// A uniform rather than a vertex attribute, because it is a fact about the whole
// body: `BodyMeshLibrary` passes `spec.Bob` at every one of its six rig calls,
// and a per-vertex copy of a per-body constant was occupying the channel a
// painted surface needs. `BodyRenderer` and `SoloBody` set it per variant.
//
// Zero by default, which is the correct reading for a prop drawn with this
// shader and for a material somebody forgot to configure: it stands still rather
// than bouncing by whatever was left in the vertex format.
uniform float body_bob = 0.0;

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
varying vec3 atlas_coord;
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
    float swing = UV2.x;

    // floor and fract, on a value packed at build time. Pivot heights run from
    // the ankle to a five-and-a-half-metre boss's shoulder, so the integer part
    // is at most 550 and the fraction keeps every bit it was given.
    float packed_rig = UV2.y;
    float pivot_y = floor(packed_rig) * 0.01;
    float phase_offset = fract(packed_rig);

    float bob = body_bob;

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

    // UV is the atlas now, and it is carried across rather than read in the
    // fragment stage because the rig above has already moved this vertex: the
    // *position* is animated and the coordinate is not, and reading UV directly
    // in `fragment()` would be correct but leaves the two channels' jobs less
    // obvious than one varying named for what it is.
    atlas_coord = vec3(UV, 0.0);

    // The model-space position and normal are still kept, and no longer for a
    // projection. The two ambient-occlusion terms below read them: "downward"
    // has to mean downward on the body rather than in the camera, and "near the
    // ground" has to mean near the body's own sole.
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
    // Which layer, and where on it. The integer part of U is the part of the
    // body this fragment belongs to; the fraction is the coordinate across it.
    // `atlas_uv` is taken before the scale so a scale of one lands exactly on
    // the layer it was painted for.
    float layer = floor(atlas_coord.x);
    vec2 atlas_uv = vec2(fract(atlas_coord.x), atlas_coord.y) * surface_detail_scale;

    vec3 blended = texture(surface_detail, vec3(atlas_uv, layer)).rgb;
    float painted = dot(blended, vec3(0.2126, 0.7152, 0.0722));

    // Normalised, so this is the plate's hue and saturation with its brightness
    // divided out. Clamped because a near-black texel divides by nearly nothing,
    // and one texel of 8x chroma on a body is a coloured firefly that moves with
    // the camera.
    vec3 chroma = clamp(blended / max(painted, 0.02), vec3(0.0), vec3(2.0));
    chroma = mix(vec3(1.0), chroma, surface_detail_chroma);

    // Centred on the plate's own mean rather than on a constant that happened to
    // suit the first plate.
    //
    // The old form was `0.58 + painted * 0.82`, which is neutral at a painted
    // value of 0.512 — and `painted` is measured *after* the sRGB decode that
    // `source_color` performs, so 0.512 linear is 0.74 in storage. That is a
    // very bright plate, and every plate this game has ever had was a mid grey:
    // all of them therefore arrived at about 0.85 and darkened every body by
    // fifteen per cent, uniformly, which is indistinguishable from the palette
    // being too dark and is where two rounds of palette-lifting went.
    //
    // `art-src/textures/plate.py` grades every plate to `surface_detail_mid` in
    // exactly this space, so the multiplier now averages to one by construction
    // and the gain is a free parameter instead of a compensation.
    float detail_value = mix(1.0,
                             // Wider than it was at both ends, and the dark end is
                             // why. A painted eye socket has to go a long way
                             // below the skin around it or it is a smudge; at a
                             // floor of 0.55 the deepest thing a face could have
                             // was half a stop, and every face on every body read
                             // as a ghost of one.
                             clamp(1.0 + (painted - surface_detail_mid) * surface_detail_gain,
                                   0.30, 1.45),
                             surface_detail_strength);

    // `detail_normal` and `detail_position` are the animated model-space normal
    // and position kept for the triplanar projection above, which is why these
    // two terms are free. Model space is what they want anyway: "downward" has
    // to mean downward on the body rather than in the camera, and "near the
    // ground" has to mean near the body's own sole.
    float under = max(0.0, -normalize(detail_normal).y);
    float contact = clamp(detail_position.y / max(0.01, occlusion_ground_height), 0.0, 1.0);

    float occlusion = (1.0 - occlusion_down * under)
                    * mix(1.0 - occlusion_ground, 1.0, contact);

    ALBEDO = body_tint * detail_value * chroma * occlusion;

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
