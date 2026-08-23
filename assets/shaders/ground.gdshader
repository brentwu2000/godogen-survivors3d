// The arena floor: one tiled surface, tinted by which tile of the layout it is.
//
// A plain textured plane would have solved the "it is a white void" problem and
// left the other one: the generator picks a different kind of block for every
// cell of a 5x5 grid, and all three kinds looked identical from the ground. A
// layout the player cannot read is a layout that was not chosen.
//
// One draw call either way. The alternative — a decal quad per cell — would have
// been twenty-five more, on the pass that has been held near twenty since
// Phase 2.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_disabled;

uniform sampler2D detail : source_color, filter_linear_mipmap, repeat_enable;

// One texel per grid cell, filtered. The blur is the feature: hard borders
// between zones would read as painted lines on the floor, and the layout is
// meant to be sensed rather than outlined.
uniform sampler2D zones : source_color, filter_linear, repeat_disable;

uniform float detail_scale = 0.22;
uniform float arena_extent = 55.0;

// Slabs, in metres. Four is about the size of a poured bay, and — more usefully —
// it is a length the player can pace out. The arena read as large and featureless
// partly because nothing on the floor had a known size, so there was nothing to
// measure a distance against.
uniform float slab_metres = 4.0;

// How dark the joint between slabs is, and how wide.
uniform float seam_darkness : hint_range(0.0, 1.0) = 0.34;
uniform float seam_width : hint_range(0.002, 0.2) = 0.028;

// How much each slab varies in brightness. Small: this is a floor poured in one
// go by one crew, not a patchwork. The point is that the eye finds the edges,
// not that any one slab is noticeable.
uniform float slab_variation : hint_range(0.0, 0.4) = 0.09;

// A cheap 2D hash. Deterministic in world space, so a slab is the same shade
// every frame and from every angle — a per-frame or per-screen-position hash
// would make the floor crawl as the camera moves, which is far more distracting
// than no variation at all.
float slab_hash(vec2 cell) {
    return fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
}

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 uv = world_pos.xz * detail_scale;

    // The same texture again, far larger and offset, multiplied over itself.
    // A single tiling frequency is visible as a grid the moment the camera moves
    // across it; two incommensurate ones read as ground.
    vec3 base = texture(detail, uv).rgb;
    vec3 macro = texture(detail, uv * 0.13 + vec2(0.41, 0.17)).rgb;

    vec2 zone_uv = clamp(world_pos.xz / (arena_extent * 2.0) + vec2(0.5), vec2(0.0), vec2(1.0));
    vec3 tint = texture(zones, zone_uv).rgb;

    // Which slab, and where inside it.
    vec2 slab = world_pos.xz / slab_metres;
    vec2 within = abs(fract(slab) - vec2(0.5));

    // Distance to the nearest joint, as a fraction of a slab. `fwidth` keeps the
    // line one pixel wide however far away it is: a fixed width in world units
    // aliases into a shimmering dotted grid at the far end of the arena, which is
    // exactly where the fog is trying to make things quietly disappear.
    float edge = 0.5 - max(within.x, within.y);
    float blur = max(fwidth(edge), 0.0001);
    float seam = 1.0 - smoothstep(0.0, seam_width + blur, edge);

    float shade = 1.0 - seam * seam_darkness;
    shade *= 1.0 + (slab_hash(floor(slab)) - 0.5) * slab_variation;

    ALBEDO = base * mix(vec3(1.0), macro * 1.55, 0.4) * tint * shade;
    ROUGHNESS = 1.0;
}
