// Billboard for the MultiMesh horde.
//
// Sprite3D's built-in billboard is a per-node material property; a MultiMesh has
// one material for thousands of instances, so the facing has to be rebuilt in
// the vertex stage from each instance's world position.
//
// Alpha is discarded rather than blended for the same reason the player sprite
// discards: hundreds of overlapping quads cannot be depth-sorted, and discarding
// keeps depth writes intact so they simply occlude each other correctly.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled;

// An array rather than an atlas. Atlas cells bleed into each other under
// filter_linear_mipmap once instances get small enough to drop a mip level —
// a bug that only appears at distance. Array layers are sampled independently,
// so there is no neighbour to bleed from. The cost is that every layer must be
// the same size, which is a build-time failure rather than a visual one.
uniform sampler2DArray albedo : source_color, filter_linear_mipmap;
uniform float alpha_scissor : hint_range(0.0, 1.0) = 0.5;

// Vertical bob, in world units. Purely cosmetic, but a crowd that is perfectly
// still reads as a screenshot rather than a horde.
uniform float bob_amplitude = 0.06;
uniform float bob_speed = 4.0;

// Whether this MultiMesh actually carries a per-instance colour block. It must
// be, because a MultiMesh with use_colors off still hands the vertex stage an
// opaque white COLOR — so a shader that trusted COLOR.r unconditionally would
// draw every projectile at full hit-flash, permanently white. That is exactly
// what happened, and it is invisible in the horde where the same shader is
// correct: the bug only shows on the renderer that opted out.
uniform float flash_enabled : hint_range(0.0, 1.0) = 0.0;

varying flat float v_flip;
varying flat float v_layer;

// How white this instance is being burned out, 0..1. Carried on the MultiMesh
// colour block rather than in INSTANCE_CUSTOM because all four of those floats
// were already spoken for, and bit-packing a fifth value into one of them is a
// decoding bug waiting for whoever changes the flip flag next.
varying flat float v_flash;

// Green and blue of the instance colour: which elite rule this one carries, or
// black for an ordinary enemy. Separate from the flash so a marked enemy being
// shot can show both at once.
varying flat vec2 v_elite;

void vertex() {
    // INSTANCE_CUSTOM.x: 1.0 mirrors the sprite. One drawn facing, mirrored at
    // runtime — generators cannot reliably draw a specific direction anyway.
    // INSTANCE_CUSTOM.y: phase offset so instances bob out of step.
    // INSTANCE_CUSTOM.z: in-plane rotation, radians. Used by projectiles to point
    //                    along travel; zero for characters, which stay upright.
    // INSTANCE_CUSTOM.w: array layer — which variant this instance draws as.
    v_flip = INSTANCE_CUSTOM.x;
    v_layer = INSTANCE_CUSTOM.w;
    v_flash = COLOR.r * flash_enabled;
    v_elite = COLOR.gb * flash_enabled;

    float spin = INSTANCE_CUSTOM.z;
    if (spin != 0.0) {
        float s = sin(spin);
        float c = cos(spin);
        VERTEX.xy = vec2(VERTEX.x * c - VERTEX.y * s, VERTEX.x * s + VERTEX.y * c);
    }

    vec3 world_origin = MODEL_MATRIX[3].xyz;
    world_origin.y += sin(TIME * bob_speed + INSTANCE_CUSTOM.y) * bob_amplitude;

    // Keep the instance's world position, take the camera's basis: the quad
    // always presents flat to the camera, so the sprite is never foreshortened
    // by the camera's own tilt.
    mat4 billboard = mat4(
        INV_VIEW_MATRIX[0],
        INV_VIEW_MATRIX[1],
        INV_VIEW_MATRIX[2],
        vec4(world_origin, 1.0));

    // Column 0's length carries the per-instance scale the MultiMesh baked into
    // MODEL_MATRIX; without re-applying it every zombie would render at 1 unit.
    float scale = length(MODEL_MATRIX[0].xyz);
    billboard[0] *= scale;
    billboard[1] *= scale;
    billboard[2] *= scale;

    MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}

void fragment() {
    vec2 uv = UV;
    uv.x = mix(uv.x, 1.0 - uv.x, v_flip);

    vec4 texel = texture(albedo, vec3(uv, v_layer));
    if (texel.a < alpha_scissor) {
        discard;
    }

    // Toward white rather than multiplied by it: a hit has to read on a sprite
    // that is already dark, and multiplying leaves a black brute black.
    //
    // Never all the way, though. At full strength the mix erases the drawing
    // entirely and a hit enemy is a solid white cut-out — which reads as a
    // missing texture rather than as an impact, and is worst exactly when it
    // matters most, with a dozen of them lit at once by a blast.
    // The elite mark, mostly multiplied and only slightly added.
    //
    // The first version added 0.55 flat and the screenshot settled it: an
    // armoured brute came out as a solid blue silhouette with none of the
    // painting left, which is the hit flash's failure mode wearing a different
    // colour — and this one is permanent rather than a tenth of a second.
    //
    // Scaling the sprite's own colour pushes the hue hard while keeping every
    // fold and shadow the artist put there, because the shading is what is being
    // multiplied. The small additive term is what stops a black brute from
    // staying black: multiply alone has nothing to work on at zero.
    //
    // The push is scaled by how dark the sprite already is, because a flat
    // multiplier is two different effects on two different paintings: the brute
    // is dark and took the blue well, while the bloater is pale and clipped to a
    // glowing white-green blob with no drawing left in it. Weighting by (1 - lum)
    // spends the strength where there is headroom and backs off where there is
    // none, so one number is right for both.
    vec3 mark = vec3(0.0, v_elite.x, v_elite.y);
    float lum = dot(texel.rgb, vec3(0.299, 0.587, 0.114));
    vec3 marked = texel.rgb * (1.0 + mark * 2.2 * (1.0 - lum)) + mark * 0.07;
    ALBEDO = mix(marked, vec3(1.0), v_flash * 0.72);
}
