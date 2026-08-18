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

    ALBEDO = base * mix(vec3(1.0), macro * 1.55, 0.4) * tint;
    ROUGHNESS = 1.0;
}
