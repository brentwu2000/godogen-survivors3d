using Godot;

/// Shared steps between a matted painting and a sprite the game can load.
///
/// Both sprite tools need the same two things and neither is what the obvious
/// engine call does, so they live here rather than being written twice slightly
/// differently.
public static class SpriteFit
{
    /// What the horde shader and the player's Sprite3D both discard below. The
    /// bounding box has to use the same number, or the tools disagree with the
    /// renderer about where the sprite ends.
    public const float Scissor = 0.5f;

    /// Loads a matted PNG as readable RGBA and clears the background residue.
    ///
    /// The matte leaves the background at a few units of alpha rather than at
    /// zero. Invisible on its own — the scissor throws it away — but it is real
    /// weight in every mip level, so a shrinking sprite develops a faint
    /// rectangular haze around it at exactly the distances that matter.
    public static Image? LoadMatted(string path)
    {
        var texture = GD.Load<Texture2D>(path);
        Image? image = texture?.GetImage();
        if (image == null)
        {
            GD.PushError($"Cannot read {path} — matte the reference with rembg first");
            return null;
        }

        // A VRAM-compressed import hands back a block format whose pixels cannot
        // be read or blitted. Decompressing here keeps the tools independent of
        // an import setting nothing else in the project pins down.
        if (image.IsCompressed())
            image.Decompress();
        image.Convert(Image.Format.Rgba8);

        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (pixel.A is > 0.0f and < 0.03f)
                    image.SetPixel(x, y, new Color(pixel.R, pixel.G, pixel.B, 0.0f));
            }
        }

        return image;
    }

    /// Bounding box of the pixels the game will actually draw.
    ///
    /// Not `Image.GetUsedRect()`: that counts any alpha above zero, and a matte
    /// leaves the background just above it — so it reports the whole canvas as
    /// content, every sprite appears to fit its frame perfectly, and every one of
    /// them is silently the wrong size.
    public static Rect2I VisibleRect(Image image)
    {
        int minX = image.GetWidth(), minY = image.GetHeight(), maxX = -1, maxY = -1;

        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A < Scissor)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < minX ? new Rect2I() : new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
