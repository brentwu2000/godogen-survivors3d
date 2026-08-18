using Godot;

/// Paints the arena's ground texture into assets/textures/ground.png.
///
///   godot --headless --script scripts/tools/BuildGroundTexture.cs
///
/// Generated rather than sourced, like the audio and for the same reasons: a
/// recipe can be re-tuned and re-run, and it carries no licence to track.
///
/// **Seamless by construction, not by blurring the edges.** Every octave uses a
/// lattice whose period divides the image, so the right edge is literally the
/// same lattice row as the left. A tiling artefact on a plane this size is not
/// subtle — it is a grid of identical smudges across the whole arena, and it is
/// the kind of thing that reads as a rendering bug rather than as a texture.
public partial class BuildGroundTexture : SceneTree
{
    private const string OutputPath = "res://assets/textures/ground.png";

    /// Covers 4 m of ground, so 512 px is about 128 px per metre at the source
    /// and roughly a third of that once the camera has it. Bigger buys detail
    /// nothing at this framing can see.
    private const int Size = 512;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(
            ProjectSettings.GlobalizePath("res://assets/textures"));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create assets/textures: {dirError}");
            return false;
        }

        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size;
                float v = y / (float)Size;

                // Broad patches of worn and unworn surface, then grit on top of
                // them — three octaves rather than two. The first pass used two plus a
                // field of wrapped-sine cracks, and the cracks were the problem:
                // a periodic function looks like a periodic function, so the
                // whole arena wore the same curl of pattern every four metres.
                // Noise all the way down has no motif to recognise.
                float patch = Noise(u, v, 3, 0x9E3779B9u) * 0.5f
                            + Noise(u, v, 7, 0x85EBCA77u) * 0.32f
                            + Noise(u, v, 19, 0x27220A95u) * 0.18f;
                float grit = Noise(u, v, 71, 0xC2B2AE3Du);

                // Per-pixel speckle, which is what makes it asphalt rather than a
                // gradient. Unfiltered hash: adjacent pixels must not correlate.
                float speckle = Hash(unchecked((uint)x * 374761393u + (uint)y * 668265263u + 0x27D4EB2Fu));

                float value = 0.34f + patch * 0.22f + (grit - 0.5f) * 0.11f + (speckle - 0.5f) * 0.06f;

                // Faintly warm where it is bright and cool where it is dark; a
                // perfectly neutral ground looks like a debug material.
                var colour = new Color(
                    Mathf.Clamp(value * 1.04f, 0.0f, 1.0f),
                    Mathf.Clamp(value * 1.00f, 0.0f, 1.0f),
                    Mathf.Clamp(value * 0.95f + 0.012f, 0.0f, 1.0f));

                image.SetPixel(x, y, colour);
            }
        }

        Error err = image.SavePng(OutputPath);
        if (err != Error.Ok)
        {
            GD.PushError($"Save failed for {OutputPath}: {err}");
            return false;
        }

        GD.Print($"Saved {OutputPath} ({Size}x{Size}, seamless)");
        return true;
    }

    /// Value noise on a lattice of `cells` across the whole image, interpolated
    /// smoothly. The lattice index wraps at `cells`, which is what makes the
    /// result tile.
    private static float Noise(float u, float v, int cells, uint seed)
    {
        float x = u * cells;
        float y = v * cells;

        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;

        // Smoothstep on the fraction, so the lattice does not show as a grid of
        // creases where the linear interpolation changes direction.
        fx = fx * fx * (3.0f - 2.0f * fx);
        fy = fy * fy * (3.0f - 2.0f * fy);

        float c00 = Lattice(x0, y0, cells, seed);
        float c10 = Lattice(x0 + 1, y0, cells, seed);
        float c01 = Lattice(x0, y0 + 1, cells, seed);
        float c11 = Lattice(x0 + 1, y0 + 1, cells, seed);

        return Mathf.Lerp(Mathf.Lerp(c00, c10, fx), Mathf.Lerp(c01, c11, fx), fy);
    }

    private static float Lattice(int x, int y, int cells, uint seed) => Hash(unchecked(
        (uint)Mathf.PosMod(x, cells) * 374761393u
        + (uint)Mathf.PosMod(y, cells) * 668265263u
        + seed));

    private static float Hash(uint value)
    {
        value ^= value >> 15;
        value *= 0x2545F491u;
        value ^= value >> 13;
        value *= 0x9E3779B1u;
        value ^= value >> 16;
        return (value & 0xFFFFFF) / 16777216.0f;
    }
}
