using Godot;

/// Crops and downscales the matted survivor painting into assets/sprites/player.png.
///
///   godot --headless --script scripts/tools/BuildPlayerSprite.cs
///
/// The crop is not cosmetic. `BuildPlayer.cs` derives the sprite's world scale
/// from the texture's pixel height and puts the pivot at half that height, so any
/// transparent margin left in the file makes the character both too small and
/// standing above the ground — and both errors look like a value someone tuned
/// badly rather than like a border nobody removed.
///
/// One sprite, not an array, so there is no shared frame to fit into: the file is
/// exactly the character and nothing else.
public partial class BuildPlayerSprite : SceneTree
{
    private const string SourcePath = "res://art-src/survivor.png";
    private const string OutputPath = "res://assets/sprites/player.png";

    /// The character stands about 130 px tall in the game's orthographic view at
    /// 1080p. This keeps roughly four times that, which survives a 4K window and
    /// leaves mip levels something to work with — and unlike the horde, whose
    /// oversampling was multiplied by five layers, this is one texture.
    private const int TargetHeight = 512;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Image? art = SpriteFit.LoadMatted(SourcePath);
        if (art == null)
            return false;

        Rect2I content = SpriteFit.VisibleRect(art);
        if (content.Size.X <= 0 || content.Size.Y <= 0)
        {
            GD.PushError($"{SourcePath} is fully transparent — the matte removed the subject");
            return false;
        }

        Image cropped = art.GetRegion(content);
        int width = Mathf.Max(1, Mathf.RoundToInt(TargetHeight * content.Size.X / (float)content.Size.Y));
        cropped.Resize(width, TargetHeight, Image.Interpolation.Lanczos);

        Error err = cropped.SavePng(OutputPath);
        if (err != Error.Ok)
        {
            GD.PushError($"Save failed for {OutputPath}: {err}");
            return false;
        }

        GD.Print($"Saved {OutputPath}: cropped {content.Size.X}x{content.Size.Y} → {width}x{TargetHeight}");
        GD.Print("Re-run scenes/BuildPlayer.cs — the sprite's world scale is derived from this height.");
        return true;
    }
}
