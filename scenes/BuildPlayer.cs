using Godot;

/// Builds scenes/Player.tscn.
///
///   godot --headless --script scenes/BuildPlayer.cs
public partial class BuildPlayer : SceneTree
{
    private const string SpritePath = "res://assets/sprites/player.png";

    /// On-screen height of the sprite quad, in world units. At the main camera's
    /// ortho size of 18 on a 1080p viewport this lands the character around
    /// 130px tall — above the floor where generated detail turns to mush
    /// (SKILL.md:35).
    private const float SpriteHeightMeters = 2.2f;

    // Physical body, deliberately smaller than the drawing: chibi art reads best
    // when the collider hugs the feet rather than the silhouette.
    private const float BodyRadius = 0.35f;
    private const float BodyHeight = 1.6f;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        var texture = GD.Load<Texture2D>(SpritePath);
        if (texture == null)
        {
            GD.PushError($"Missing {SpritePath} — run 'godot --headless --import' first.");
            return false;
        }

        var root = new CharacterBody3D { Name = "Player" };

        root.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0.0f, BodyHeight * 0.5f, 0.0f),
        });

        int pixelHeight = texture.GetHeight();
        root.AddChild(new Sprite3D
        {
            Name = "Sprite",
            Texture = texture,
            PixelSize = SpriteHeightMeters / pixelHeight,

            // Full billboard: the quad always presents flat to the camera, so the
            // 50°-elevated drawing is never additionally foreshortened by the
            // camera's own tilt.
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,

            // Alpha scissor, not alpha blend. Hundreds of overlapping sprites
            // cannot be depth-sorted correctly; discarding instead of blending
            // keeps depth writes intact and sidesteps sorting entirely. The
            // matte is effectively binary already — only 0.26% of the texture
            // sits in the 64..191 band this threshold has to split.
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            AlphaScissorThreshold = 0.5f,

            // Flat cartoon art carries its own shading; lighting a camera-facing
            // quad just washes it out.
            Shaded = false,

            // A camera-facing quad casts a quad-shaped shadow that swings around
            // as the camera moves — it reads as a smear on the ground, not as the
            // character's shadow. Ground contact comes from a blob decal instead.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // Pivot at the feet so the sprite stands on the ground plane rather
            // than straddling it.
            Offset = new Vector2(0.0f, pixelHeight * 0.5f),
        });

        root.AddChild(BuildBlobShadow(BodyRadius * 2.4f));

        var weapons = new Node3D { Name = "WeaponHandler", Position = new Vector3(0.0f, 1.0f, 0.0f) };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(weapons, "res://scripts/nodes/WeaponHandler.cs"));

        Node scripted = SceneBuildUtil.AttachScriptToRoot(root, "res://scripts/nodes/Player.cs");
        bool ok = SceneBuildUtil.PackAndSave(scripted, "res://scenes/Player.tscn");
        scripted.Free();
        return ok;
    }

    /// Flat disc on the ground. This is the only thing tying a camera-facing
    /// sprite to a position on the floor — without it the character reads as
    /// hovering, and at this camera angle the error is a metre or more.
    private static MeshInstance3D BuildBlobShadow(float diameter)
    {
        var material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>("res://assets/sprites/blob_shadow.png"),
            AlbedoColor = new Color(0.0f, 0.0f, 0.0f, 0.45f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,

            // Coplanar blobs would z-fight and sort badly once there are hundreds
            // of them, so they never write depth — they only tint what is already
            // drawn on the floor.
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        return new MeshInstance3D
        {
            Name = "BlobShadow",
            Mesh = new QuadMesh { Size = new Vector2(diameter, diameter) },
            MaterialOverride = material,
            RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
            Position = new Vector3(0.0f, 0.02f, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
