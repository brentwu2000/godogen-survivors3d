using Godot;

/// The thing above you, which is what makes a room a room.
///
/// Cold Storage shipped its first version with an overhead sun, cold weak light,
/// fog closing at twenty-four metres and a 1.2 m floor tile grid. Every one of
/// those is right and together they produced **an outdoor arena at night**. The
/// giveaway was the top of the frame: an unobstructed black sky with air dust
/// drifting against it like stars, and a far boundary spanning the view like a
/// horizon instead of converging into corners.
///
/// No amount of floor dressing fixes that, and this is the cheapest thing that
/// does. One mesh, one material, one draw call, no shadow pass — a slab, the
/// beams under it, and the fixtures that explain where the light is coming from.
///
/// **It casts no shadow.** It is a solid lid over the entire arena directly under
/// a sun pointing straight down, so a shadow-casting roof puts the whole level in
/// darkness — correctly, and uselessly.
public static class CeilingMesh
{
    /// How far apart the beams run, in metres.
    ///
    /// Wide enough that the player passes several between one side of the arena
    /// and the other, and never so many that the roof becomes a texture. Eleven
    /// is also not a factor of the 1.2 m floor tile, which keeps the two grids
    /// from lining up into a single moiré that reads as one object.
    private const float BeamSpacing = 11.0f;

    /// Fixtures per beam, spaced along it. A fixture is the only pale thing up
    /// there, so it is the only thing the eye reads as a source.
    private const float FixtureSpacing = 14.0f;

    public static MeshInstance3D Build(float extent, float height, Color colour)
    {
        var mesh = new MeshBuilder();

        // The deck. Slightly wider than the arena so its edge is never in shot —
        // a roof that stops short of the fog is a roof with a visible rim, which
        // reads as a floating slab rather than as a ceiling.
        float span = extent * 2.4f;
        mesh.Box(new Vector3(0.0f, height + 0.3f, 0.0f), new Vector3(span, 0.6f, span), colour);

        Color beam = colour.Lerp(new Color(0.08f, 0.09f, 0.10f), 0.55f);
        Color rib = colour.Lerp(new Color(0.30f, 0.33f, 0.34f), 0.4f);

        // Beams both ways. One direction alone is a set of stripes; crossing them
        // is what makes the roof read as structure with a span rather than as a
        // painted pattern — and the bays between them are the thing that gives
        // the arena a sense of how big it is from underneath.
        for (float at = -span * 0.5f; at <= span * 0.5f; at += BeamSpacing)
        {
            mesh.Box(new Vector3(at, height - 0.24f, 0.0f), new Vector3(0.5f, 0.5f, span), beam);
            mesh.Box(new Vector3(0.0f, height - 0.62f, at), new Vector3(span, 0.44f, 0.7f), beam);
        }

        // Fixtures, on a coarser grid than the beams so they do not sit on every
        // crossing. Pale, and the only pale thing on the roof.
        Color housing = new(0.42f, 0.46f, 0.48f);
        Color tube = new(0.72f, 0.78f, 0.80f);

        for (float x = -span * 0.5f + 7.0f; x <= span * 0.5f; x += FixtureSpacing)
        {
            for (float z = -span * 0.5f + 5.0f; z <= span * 0.5f; z += FixtureSpacing)
            {
                mesh.Box(new Vector3(x, height - 0.95f, z), new Vector3(0.34f, 0.16f, 2.6f), housing);
                mesh.Box(new Vector3(x, height - 1.04f, z), new Vector3(0.24f, 0.06f, 2.4f), tube);

                // The drops it hangs on. Without them the fixture floats, which
                // at this distance is the difference between a light and a smear.
                foreach (int sz in new[] { -1, 1 })
                    mesh.Box(new Vector3(x, height - 0.6f, z + sz * 1.0f), new Vector3(0.05f, 0.6f, 0.05f), housing);
            }
        }

        // Service runs, offset from the beam grid so the roof is not symmetric.
        // Two of them, crossing, which is enough to say "this building has plant
        // in it" and cheap enough not to think about.
        mesh.Box(new Vector3(-span * 0.18f, height - 1.1f, 0.0f), new Vector3(1.5f, 1.1f, span), rib);
        mesh.Box(new Vector3(0.0f, height - 1.5f, span * 0.22f), new Vector3(span, 0.8f, 1.1f), rib);

        return new MeshInstance3D
        {
            Name = "Ceiling",
            Mesh = Dressed(mesh.Build()),

            // See the class note. A lid over the arena under a vertical sun.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static ArrayMesh Dressed(ArrayMesh mesh)
    {
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            Metallic = 0.0f,

            // Front faces only, and the boxes are wound outward, so the deck is
            // solid from below and invisible from above. That matters for the
            // aerial capture, which sits over the arena looking down and would
            // otherwise photograph the top of the roof.
            CullMode = BaseMaterial3D.CullModeEnum.Back,
        });

        return mesh;
    }
}
