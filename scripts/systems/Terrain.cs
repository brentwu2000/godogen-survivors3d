using Godot;

/// The height of the ground at a point, as a function rather than as a mesh.
///
/// **Analytic, and that is not an optimisation.** The obvious implementation is a
/// heightmap mesh with a `ConcavePolygonShape3D` collider and a raycast per
/// query, and it does not work: raycasts do not reliably hit a trimesh
/// (godot.md:39), so a crate placed by raycast lands at zero every few dozen
/// tries with nothing to indicate it. A function has no such failure mode — the
/// same point always returns the same height, on any thread, with no physics
/// frame in between.
///
/// **The simulation stays two-dimensional.** Nothing here is consulted by the
/// flow field, by collision, by the horde's movement or by damage. Only things
/// that *draw* ask, plus props placed once when the level generates. The floor
/// collider is still a flat box and the player is planted after `MoveAndSlide`.
///
/// That is the whole design. Making the simulation three-dimensional would mean
/// slopes affecting movement speed, flow fields that route around gradients, and
/// a horde that walks up hills — none of which the game wants, all of which would
/// have to be balanced, and every one of which is a place for the terrain to
/// disagree with the collider.
public static class Terrain
{
    /// Metres from trough to crest, before the flattening near the origin.
    ///
    /// Tuned by rendering, not by reasoning. The first value was 1.05, which is
    /// the largest step the eye can be argued into ignoring — and at a camera this
    /// high, over an eighteen-metre wave, it was invisible: the slab seams still
    /// read as straight lines and the floor was still a table. At 1.75 the seams
    /// curve, the horizon bows, and a crest can hide a crate. It is also still
    /// small enough that a four-metre block sits on it without a visible gap
    /// under one corner, which is the failure that bounds this from above.
    public const float Amplitude = 1.75f;

    /// The long wave, in metres.
    ///
    /// Eighteen, tuned against the fog rather than against a picture of the
    /// terrain. The dark closes about twenty-four metres out, so a forty-metre
    /// wave is half a wave in view — the visible ground is then one long tilt in
    /// one direction, which reads as the camera being crooked rather than as
    /// undulation. At eighteen there is more than a full period on screen and the
    /// eye sees ground.
    public const float CoarseWavelength = 18.0f;

    /// The short wave, and how much of the amplitude it takes.
    public const float FineWavelength = 6.3f;
    public const float FineWeight = 0.32f;

    /// Flat within this radius of the origin, fading to full terrain by
    /// `FlatFadeEnd`.
    ///
    /// The player spawns at the origin and so does the extraction hold. A run
    /// that began on a slope would start with the character's feet planted at a
    /// height the collider does not have, and the first thing the player would
    /// see is themselves standing in the ground.
    public const float FlatRadius = 7.0f;
    public const float FlatFadeEnd = 16.0f;

    /// Shifts the whole field, so two runs of different seeds are not the same
    /// hills. Set once when the level generates.
    public static Vector2 Offset { get; set; }

    /// Height at a world point.
    public static float Height(float x, float z)
    {
        float ox = x + Offset.X;
        float oz = z + Offset.Y;

        float coarse = Noise(ox / CoarseWavelength, oz / CoarseWavelength);
        float fine = Noise(ox / FineWavelength + 31.7f, oz / FineWavelength + 11.3f);

        float height = Mathf.Lerp(coarse, fine, FineWeight) * Amplitude;

        // Flattened near the origin, eased rather than clipped — a hard edge to
        // the flat zone is a visible circular step in the ground.
        float distance = Mathf.Sqrt(x * x + z * z);
        float blend = Mathf.Clamp((distance - FlatRadius) / (FlatFadeEnd - FlatRadius), 0.0f, 1.0f);
        blend = blend * blend * (3.0f - 2.0f * blend);

        return height * blend;
    }

    public static float Height(Vector3 at) => Height(at.X, at.Z);

    /// Puts a point on the ground, keeping its X and Z.
    public static Vector3 Plant(Vector3 at) => new(at.X, Height(at.X, at.Z), at.Z);

    /// Value noise: a hash at each lattice corner, smoothly interpolated.
    ///
    /// Value rather than gradient noise because the difference is invisible at
    /// this amplitude and this is called for every one of 6,561 ground vertices
    /// plus every drawn object every frame. Two octaves is enough for ground that
    /// is meant to be felt rather than looked at.
    private static float Noise(float x, float z)
    {
        float ix = Mathf.Floor(x);
        float iz = Mathf.Floor(z);
        float fx = x - ix;
        float fz = z - iz;

        // Smoothstep on the fractional part. Linear interpolation between
        // lattice corners gives a surface with visible creases along every
        // integer line, which at eighteen metres is a grid of ridges.
        fx = fx * fx * (3.0f - 2.0f * fx);
        fz = fz * fz * (3.0f - 2.0f * fz);

        float a = Hash(ix, iz);
        float b = Hash(ix + 1.0f, iz);
        float c = Hash(ix, iz + 1.0f);
        float d = Hash(ix + 1.0f, iz + 1.0f);

        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz) * 2.0f - 1.0f;
    }

    /// Deterministic in world space, so a point is the same height every frame
    /// and from every angle. A hash that involved time or screen position would
    /// make the ground crawl.
    private static float Hash(float x, float z)
    {
        float value = Mathf.Sin(x * 127.1f + z * 311.7f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }
}
