using Godot;

/// Writes placeholder variant sprites to assets/sprites/enemies/*.png.
///
///   godot --headless --script scripts/tools/BuildEnemySprites.cs
///
/// These are tinted, downscaled copies of the one real zombie sprite. They exist
/// so the variant system can be built and seen before the art does — replace any
/// file here with a generated sprite and nothing in code changes.
///
/// Two things a replacement has to honour, both enforced by Texture2DArray:
///
///   Same size, every layer. A mismatched layer fails the array outright, which
///   is the good failure — the alternative, an atlas, bleeds neighbouring cells
///   into each other through mipmaps only at distance.
///
///   256px tall is the target. A 2m sprite in an 18m orthographic view covers
///   about 120px on a 1080p viewport, so the source only needs headroom for the
///   1.5x brute and for mips — the original 1051px sprite is 8x oversampled, and
///   in an array that waste is multiplied by the layer count.
public partial class BuildEnemySprites : SceneTree
{
    // In art-src rather than assets: the full-resolution original is an input to
    // this tool and never loaded by the running game, which is the line assets/
    // draws (godot.md:9).
    private const string SourcePath = "res://art-src/zombie.png";
    private const string OutputDir = "res://assets/sprites/enemies";
    private const int TargetHeight = 256;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        var source = GD.Load<Texture2D>(SourcePath);
        if (source == null)
        {
            GD.PushError($"Missing {SourcePath}");
            return false;
        }

        Image original = source.GetImage();
        if (original == null)
        {
            GD.PushError($"{SourcePath} has no readable image");
            return false;
        }

        // A VRAM-compressed import hands back a block format that GetPixel cannot
        // read and the array cannot stack. Decompressing here means the tool does
        // not silently depend on an import setting staying where it is.
        if (original.IsCompressed())
            original.Decompress();
        original.Convert(Image.Format.Rgba8);

        int width = Mathf.RoundToInt(TargetHeight * (float)original.GetWidth() / original.GetHeight());
        original.Resize(width, TargetHeight, Image.Interpolation.Lanczos);

        (string Name, Color Tint)[] variants =
        {
            ("walker", new Color(1.00f, 1.00f, 1.00f)),
            ("runner", new Color(1.25f, 1.15f, 0.60f)),
            ("brute", new Color(0.85f, 0.35f, 0.30f)),
            ("bloater", new Color(0.55f, 1.05f, 0.45f)),
            ("spitter", new Color(0.80f, 0.55f, 1.15f)),
        };

        foreach ((string name, Color tint) in variants)
        {
            Image tinted = Tinted(original, tint);
            string path = $"{OutputDir}/{name}.png";

            Error err = tinted.SavePng(path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }
            GD.Print($"Saved {path} ({width}x{TargetHeight})");
        }

        return true;
    }

    /// Multiplies colour and leaves alpha alone. Alpha is what the scissor cuts
    /// on, so scaling it would move the silhouette rather than recolour it.
    private static Image Tinted(Image source, Color tint)
    {
        var result = Image.CreateEmpty(source.GetWidth(), source.GetHeight(), false, Image.Format.Rgba8);

        for (int y = 0; y < source.GetHeight(); y++)
        {
            for (int x = 0; x < source.GetWidth(); x++)
            {
                Color pixel = source.GetPixel(x, y);
                result.SetPixel(x, y, new Color(
                    Mathf.Min(1.0f, pixel.R * tint.R),
                    Mathf.Min(1.0f, pixel.G * tint.G),
                    Mathf.Min(1.0f, pixel.B * tint.B),
                    pixel.A));
            }
        }

        return result;
    }
}
