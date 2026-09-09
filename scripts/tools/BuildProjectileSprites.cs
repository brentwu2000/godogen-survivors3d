using Godot;

/// Emits the six projectile silhouettes into assets/sprites/bolts/.
///
///   dotnet build && godot --headless --script scripts/tools/BuildProjectileSprites.cs
///
/// Drawn in code rather than painted, for the same reason the audio is
/// synthesised and the puff textures are: these are six outlines a few dozen
/// pixels across, and an outline is easier to re-tune as arithmetic than to
/// redraw. Nothing here is art — it is a shape that has to be recognisable at
/// about twenty pixels on screen while moving.
///
/// **Hard edges on purpose.** `horde_billboard.gdshader` discards below an alpha
/// of 0.5 rather than blending, because hundreds of overlapping camera-facing
/// quads cannot be depth-sorted. A shape antialiased into the background loses its
/// fringe to the scissor and comes out ragged at exactly the sizes these are seen
/// at, so every pixel here is in or out.
///
/// Colour is a *value*, not a hue: the renderer multiplies by the per-shot tint,
/// so white is "take the full tint" and half-grey is "take half of it". That is
/// what puts a bright tip on a dull shaft without the shaft and the tip having to
/// be two different weapons.
public partial class BuildProjectileSprites : SceneTree
{
    private const string OutputDirectory = "res://assets/sprites/bolts";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error made = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDirectory));
        if (made != Error.Ok && made != Error.AlreadyExists)
        {
            GD.PushError($"Cannot create {OutputDirectory}: {made}");
            return false;
        }

        for (int layer = 0; layer < Bolts.Paths.Length; layer++)
        {
            Image image = Draw((BoltShape)layer);
            Error err = image.SavePng(Bolts.Paths[layer]);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {Bolts.Paths[layer]}: {err}");
                return false;
            }

            GD.Print($"Saved {Bolts.Paths[layer]}");
        }

        GD.Print($"{Bolts.Paths.Length} bolts at {Bolts.Width}x{Bolts.Height}, pointing +X.");
        return true;
    }

    /// Every shape is authored pointing along +X, because that is the convention
    /// the renderer's in-plane spin assumes: it rotates the quad by the shot's
    /// screen-space heading, so local +X ends up along travel.
    private static Image Draw(BoltShape shape)
    {
        var image = Image.CreateEmpty(Bolts.Width, Bolts.Height, false, Image.Format.Rgba8);

        for (int y = 0; y < Bolts.Height; y++)
        {
            for (int x = 0; x < Bolts.Width; x++)
            {
                // u runs 0 at the tail to 1 at the point; v is 0 on the axis and 1
                // at either edge, so every shape below is written as "how thick am
                // I here" rather than as two mirrored halves.
                float u = (x + 0.5f) / Bolts.Width;
                float v = Mathf.Abs((y + 0.5f) / Bolts.Height - 0.5f) * 2.0f;

                float value = Value(shape, u, v);
                image.SetPixel(x, y, value <= 0.0f
                    ? new Color(0.0f, 0.0f, 0.0f, 0.0f)
                    : new Color(value, value, value, 1.0f));
            }
        }

        return image;
    }

    /// Zero for "not part of the shape", otherwise how much of the shot's tint
    /// this pixel takes.
    private static float Value(BoltShape shape, float u, float v) => shape switch
    {
        BoltShape.Slug => Slug(u, v),
        BoltShape.Arrow => Arrow(u, v),
        BoltShape.Lance => Lance(u, v),
        BoltShape.Pellet => Pellet(u, v),
        BoltShape.Charge => Charge(u, v),
        BoltShape.Spit => Spit(u, v),
        _ => 0.0f,
    };

    /// A capsule with a hot nose and a short tail, which is what a rifle round
    /// looks like when it is allowed to be twenty pixels long.
    private static float Slug(float u, float v)
    {
        if (u > 0.42f && u < 0.96f && v < Taper(u, 0.42f, 0.96f, 0.34f))
            return u > 0.84f ? 1.0f : 0.72f;

        // The tail. Thin and dim: it is the thing that says which way this is
        // going, and a bright one competes with the nose.
        if (u > 0.12f && u <= 0.42f && v < 0.10f)
            return 0.34f;

        return 0.0f;
    }

    /// Shaft, head and fletching, in three values so the parts read separately.
    private static float Arrow(float u, float v)
    {
        // Head: a triangle that closes to a point.
        if (u > 0.74f && v < (0.98f - u) / 0.24f * 0.42f)
            return 1.0f;

        // Shaft.
        if (u > 0.20f && u <= 0.74f && v < 0.11f)
            return 0.66f;

        // Fletching: two tabs swept back off the tail.
        if (u > 0.06f && u < 0.26f)
        {
            float sweep = (0.26f - u) / 0.20f;
            if (v < 0.10f + 0.42f * sweep && v > 0.06f)
                return 0.45f;
        }

        return 0.0f;
    }

    /// One long thin bright bar, nearly the whole frame. The marksman rifle is
    /// sold on reach and this is the only channel reach is visible in.
    private static float Lance(float u, float v)
    {
        if (u > 0.04f && u < 0.99f && v < Taper(u, 0.04f, 0.99f, 0.13f))
            return u > 0.90f ? 1.0f : 0.86f;

        return 0.0f;
    }

    /// A speck near the nose of the frame, and nothing else.
    private static float Pellet(float u, float v)
    {
        float du = (u - 0.82f) / 0.09f;
        float dv = v / 0.30f;
        return du * du + dv * dv < 1.0f ? 0.9f : 0.0f;
    }

    /// A fat finned body. The one shot in the game worth watching travel, so it
    /// is the one shape with an interior.
    private static float Charge(float u, float v)
    {
        if (u > 0.36f && u < 0.94f && v < Taper(u, 0.36f, 0.94f, 0.80f))
        {
            // A band across the middle, so the body is not a solid lozenge — the
            // detail is what stops it reading as a scaled-up slug.
            bool band = u > 0.58f && u < 0.68f;
            return band ? 0.45f : (u > 0.86f ? 1.0f : 0.78f);
        }

        // Fins, swept back off the tail and taller than the body.
        if (u > 0.20f && u < 0.44f && v > 0.42f && v < 0.42f + (0.44f - u) * 2.6f)
            return 0.55f;

        return 0.0f;
    }

    /// The horde's, and the only shape the player is on the receiving end of.
    ///
    /// Deliberately the ugliest of the six. Everything the player fires is
    /// machined — capsules, lances, a finned charge — and the thing coming back
    /// at them is a wobbling glob with a dribble behind it, which is the whole of
    /// what separates "my shot" from "their shot" in a frame with both in it.
    private static float Spit(float u, float v)
    {
        float wobble = 1.0f
            + 0.22f * Mathf.Sin(u * 34.0f)
            + 0.13f * Mathf.Sin(u * 61.0f + 1.9f);

        if (u > 0.52f && u < 0.97f && v < Taper(u, 0.52f, 0.97f, 0.72f) * wobble)
            return u > 0.80f ? 0.95f : 0.70f;

        // The dribble: three shrinking beads trailing the glob.
        for (int n = 0; n < 3; n++)
        {
            float at = 0.44f - n * 0.11f;
            float du = (u - at) / (0.035f - n * 0.008f);
            float dv = v / (0.30f - n * 0.07f);
            if (du * du + dv * dv < 1.0f)
                return 0.55f - n * 0.10f;
        }

        return 0.0f;
    }

    /// Half-thickness of a lozenge running from `from` to `to`, peaking at
    /// `thickness` in the middle. Shared because five of the six shapes are a
    /// lozenge with something added to it, and five copies of the same ellipse
    /// would drift.
    private static float Taper(float u, float from, float to, float thickness)
    {
        float t = (u - from) / (to - from);
        return Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (2.0f * t - 1.0f) * (2.0f * t - 1.0f))) * thickness;
    }
}
