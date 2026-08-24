using Godot;

/// Fits each matted variant painting into the one frame size the horde's
/// Texture2DArray allows, and writes assets/sprites/enemies/*.png.
///
///   godot --headless --script scripts/tools/BuildEnemySprites.cs
///
/// Three constraints decide everything here, and they conflict:
///
///   Every layer must be the same pixels. Texture2DArray refuses a mismatch —
///   which is the good failure. The alternative, an atlas, would accept anything
///   and then bleed neighbouring cells together through mipmaps, but only once
///   instances get far enough away to drop a level.
///
///   The creatures are not the same shape. A brute is nearly as wide as it is
///   tall and a runner is half that; there is no single frame that is tight
///   around both. So the frame is sized for the narrow majority and the wide
///   ones are fitted by width, which leaves empty space above their heads.
///
///   The quad is anchored at the feet. HordeRenderer lifts the mesh so the
///   instance position is ground level, so a creature centred in its frame would
///   hover. Everything is anchored to the bottom of the frame instead, and the
///   empty space goes above the head where it costs nothing but discarded
///   fragments.
///
/// Because a fitted-by-width creature does not fill its frame, its drawn height
/// is a fraction of the quad — so its SpriteScale has to make up the difference.
/// This tool prints the number each variant needs; `BuildEnemyTypes.cs` holds it,
/// and `EnemyTypeProbe` asserts the two still agree. Nothing keeps them in step
/// automatically, and a silent drift here is a brute that is quietly the wrong
/// size, so the assertion is the point.
public partial class BuildEnemySprites : SceneTree
{
    /// Wide enough for the standing variants with a little slack for outstretched
    /// arms, and no wider. Every extra column is discarded fragments on every
    /// instance of every variant — which is the whole horde, on the platform with
    /// the tightest budget.
    private const int FrameWidth = 176;
    private const int FrameHeight = 256;

    /// A 2 m sprite covers about 120 px in an 18 m orthographic view at 1080p, so
    /// 256 leaves headroom for the biggest variant and for mips. The paintings are
    /// 1254 px square; keeping that would be an eightfold oversample, multiplied
    /// by the layer count.
    private const string SourceDir = "res://art-src";
    private const string OutputDir = "res://assets/sprites/enemies";

    /// Layer order, the matted painting each layer comes from, and how tall that
    /// creature is meant to stand. The design height is duplicated in
    /// `BuildEnemyTypes.cs` on purpose — it is what the printed SpriteScale is
    /// computed from here, and what `EnemyTypeProbe` measures against there.
    private static readonly (string Variant, string Source, float DesignHeight)[] Layers =
    {
        ("walker", "walker", 2.0f),
        ("runner", "runner", 1.8f),
        ("brute", "brute", 3.0f),
        ("bloater", "bloater", 2.4f),
        ("spitter", "spitter", 2.0f),

        // The first version of this row reused the brute's painting at nearly
        // twice the height, on the theory that a familiar shape arriving at an
        // unfamiliar size is what makes a boss. On screen it read as a brute
        // standing closer to the camera. It has its own painting now — bone
        // plating, one arm mutated past the other — because the size difference
        // is doing enough work already and the silhouette was doing none.
        ("boss", "boss", 5.5f),

        // The three authored after the solid-body path became the primary one.
        //
        // **Their absence was a real defect and `EnemyTypeProbe` caught it.** The
        // billboard array is a fallback for hardware that cannot afford a hundred
        // and fifty meshes, and a variant with no layer in it draws a magenta
        // placeholder there — so the horde shipped correct on one path and
        // visibly broken on the other, with a warning in every log that had
        // started to look like part of the scenery.
        //
        // Order matters and is not cosmetic: this array is the stack order of the
        // Texture2DArray, and `SpriteLayer` in `BuildEnemyTypes.cs` is the index
        // into it. Stalker 6, bulwark 7, lantern 8 — append only.
        ("stalker", "stalker", 1.3f),
        ("bulwark", "bulwark", 1.5f),
        ("lantern", "lantern", 1.9f),
    };

    /// The quad height a scale of 1.0 draws, matching Horde.SpriteHeight.
    private const float QuadHeightMeters = 2.0f;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        foreach ((string variant, string source, float designHeight) in Layers)
        {
            Image? art = SpriteFit.LoadMatted($"{SourceDir}/{source}.png");
            if (art == null)
                return false;

            Rect2I content = SpriteFit.VisibleRect(art);
            if (content.Size.X <= 0 || content.Size.Y <= 0)
            {
                GD.PushError($"{source}.png is fully transparent — the matte removed the subject");
                return false;
            }

            Image framed = Fit(art, content, out float fillFraction);
            string path = $"{OutputDir}/{variant}.png";

            Error err = framed.SavePng(path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }

            float scale = designHeight / (QuadHeightMeters * fillFraction);
            GD.Print($"Saved {path}: content {content.Size.X}x{content.Size.Y} fills " +
                     $"{fillFraction * 100.0f:F1}% of the frame height → " +
                     $"SpriteScale {scale:F3} for {designHeight:F1} m");
        }

        return true;
    }

    /// Contain-fit into the frame, centred across and sat on the bottom edge.
    /// Reports what fraction of the frame's height the art ended up occupying,
    /// because that fraction is what the variant's SpriteScale has to cancel.
    private static Image Fit(Image art, Rect2I content, out float fillFraction)
    {
        // Crop first: scaling the whole painting would spend resolution on the
        // transparent margin the matte left behind, and the margin is not the
        // same size in every one of them.
        Image cropped = art.GetRegion(content);

        float scale = Mathf.Min(FrameWidth / (float)content.Size.X, FrameHeight / (float)content.Size.Y);
        int width = Mathf.Max(1, Mathf.FloorToInt(content.Size.X * scale));
        int height = Mathf.Max(1, Mathf.FloorToInt(content.Size.Y * scale));
        cropped.Resize(width, height, Image.Interpolation.Lanczos);

        var framed = Image.CreateEmpty(FrameWidth, FrameHeight, false, Image.Format.Rgba8);

        // Explicitly cleared: an uninitialised frame would leave whatever the
        // allocator had in the columns the art does not cover, and the scissor
        // only discards on alpha.
        framed.Fill(new Color(0.0f, 0.0f, 0.0f, 0.0f));
        framed.BlitRect(cropped, new Rect2I(Vector2I.Zero, cropped.GetSize()),
                        new Vector2I((FrameWidth - width) / 2, FrameHeight - height));

        fillFraction = height / (float)FrameHeight;
        return framed;
    }

}
