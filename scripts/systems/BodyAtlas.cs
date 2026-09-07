using Godot;

/// Where each part of a body is painted, as UV rectangles the mesh maps into.
///
/// **This is the file that lets a body have a face.** Until it existed the two
/// UV channels both held rig data — swing, pivot, phase and bob — so the only
/// way to get a painted surface onto a body was to project one triplanar in
/// model space. A triplanar projection can say what a body is *made of* and
/// cannot say what is *where*: it wraps rot around a head the same way it wraps
/// it around a shin, because it has no idea which is which. Eyes need to be at
/// eye height. See `MeshBuilder`, which now packs the whole rig into UV2.
///
/// **A texture array, not one packed image, and the reason is mipmaps.** An
/// atlas that puts a head next to a torso in one 1024 image is fine at full
/// resolution and wrong at every level below it: a body is about 120 pixels tall
/// at this camera, so its head samples a 16- or 32-pixel mip, and at that size
/// the filter is averaging across cell boundaries. The face acquires a fringe of
/// whatever was painted beside it. A layer per part has no boundaries to bleed
/// across, tiles correctly within itself, and costs one `sampler2DArray`.
///
/// The layer index rides in **UV.x's integer part**, `layer + u`, with the
/// texture coordinate in the fraction — the same packing `MeshBuilder` uses for
/// the pivot and `body.gdshader` uses for pace and phase. There is nowhere else
/// to put it: a third channel would be a third vertex attribute on a mesh drawn
/// a hundred and fifty times.
public static class BodyAtlas
{
    /// The layers, in the order the array stacks them. The names are also the
    /// filenames — `BuildBodyAtlas` writes `body/<category>_<name>.png`.
    public const int FlatLayer = 0;
    public const int HeadLayer = 1;
    public const int TorsoLayer = 2;
    public const int LimbLayer = 3;
    public const int ClothLayer = 4;
    public const int KitLayer = 5;

    public static readonly string[] LayerNames =
        { "flat", "head", "torso", "limb", "cloth", "kit" };

    /// Every layer is square and this size. One number, because a `Texture2DArray`
    /// refuses to stack layers that disagree — and refuses at load time, with a
    /// warning, leaving the bodies untextured rather than failing the build.
    public const int LayerSize = 512;

    /// A hair under one, so `u = 1` stays inside its own layer.
    ///
    /// A tube's seam wraps: the last facet's far edge is `u = 1`, which is the
    /// same place on the texture as `u = 0`. Mapped at full width that lands on
    /// `layer + 1`, whose integer part is the *next* layer — so every limb in the
    /// game would have one facet wearing the head. One texel short of the seam
    /// is invisible and cannot do that.
    private const float Span = 1.0f - 1.0f / LayerSize;

    private static Rect2 Whole(int layer) => new(layer, 0.0f, Span, 1.0f);

    /// The middle of the flat layer, as a point rather than a rectangle.
    ///
    /// `MeshBuilder`'s default, so a prop — or a part nobody has dressed, or a
    /// baked body that has no UVs of its own — samples one solid neutral texel
    /// and comes out its own vertex colour. A zero-sized rect maps every `u` and
    /// `v` to the same place, which is exactly what "no texture" means here.
    public static readonly Rect2 Flat = new(FlatLayer + 0.5f, 0.5f, 0.0f, 0.0f);

    /// Equirectangular. `MeshBuilder.Ball` runs `u` round the azimuth from +X
    /// toward +Z and `v` from the crown down, and a body faces -Z — so the face
    /// is three quarters of the way across this layer. `BuildBodyAtlas` paints it
    /// there and nowhere else.
    public static readonly Rect2 Head = Whole(HeadLayer);

    /// `u` round the chest, `v` from the waist at the bottom to the collar at the
    /// top — `MeshBuilder.Barrel` puts `from` at v = 1, and a torso is built from
    /// the waist upward.
    public static readonly Rect2 Torso = Whole(TorsoLayer);

    /// Bare limbs: forearms, shins, the neck. `v = 1` is the end nearer the body.
    public static readonly Rect2 Limb = Whole(LimbLayer);

    /// Covered limbs and the pelvis — sleeves, thighs, trousers.
    public static readonly Rect2 Cloth = Whole(ClothLayer);

    /// Hard parts: boots, hands, a survivor's cap, a held weapon.
    public static readonly Rect2 Kit = Whole(KitLayer);

    /// The layer files for one body category, in stacking order.
    ///
    /// Three categories, matching `BodyRenderer.DetailTextureFor`: the fast
    /// infected read as torn skin and cloth, the heavy roster as hide and
    /// calcified plate, and a survivor as a person wearing things.
    public static string[] Paths(string category)
    {
        var paths = new string[LayerNames.Length];
        for (int i = 0; i < paths.Length; i++)
            paths[i] = $"res://assets/textures/body/{category}_{LayerNames[i]}.png";

        return paths;
    }
}
