using Godot;

/// Builds scenes/Main.tscn. Run after BuildPlayer — leaf scenes first, parents
/// after, or the instance here resolves to nothing.
///
///   godot --headless --script scenes/BuildMain.cs
///
/// Builders construct the hierarchy, set properties, attach scripts, pack, and
/// quit. No runtime logic belongs here — no _Ready/_Process, signals, or state.
public partial class BuildMain : SceneTree
{
    // Orthographic framing: the camera sits on a 24m arc and tilts down 52°,
    // which puts the horizon out of frame and keeps ground distances readable.
    /// How far the camera looks down, in degrees. Negative is downward.
    ///
    /// Was −52°, which is most of the way to overhead and reads as a map. At −26°
    /// the horizon is in frame, walls occlude each other, and something twenty
    /// metres away is visibly twenty metres away. The cost is real and was paid
    /// knowingly: less of the ground around the player is visible, so what is
    /// behind you is genuinely hidden — which is the reason the camera turns.
    private const float CameraTiltDegrees = -26.0f;

    /// Metres back along the view axis. At the tilt above this puts the eye
    /// 5.7 m up and 11.7 m behind, which frames the player in the lower third
    /// with the direction of travel filling the rest.
    private const float CameraDistance = 13.0f;

    /// Vertical field of view. Wide enough that the periphery still carries
    /// information, narrow enough not to bend straight walls at the edges.
    private const float CameraFov = 52.0f;

    /// Near plane close enough that a wall the player is against does not clip
    /// away, far plane past the fog — see the depth fog in the environment, which
    /// is what actually ends the view.
    private const float CameraNear = 0.15f;
    private const float CameraFar = 260.0f;

    /// Vertical extent of the view in world units. 18 keeps a 2.2m character
    /// around 130px on a 1080p viewport while still showing roughly 32x18m of
    /// ground — enough room for a horde to close in from off-screen.
    private const float OrthoSize = 18.0f;

    private const float GroundSize = 120.0f;

    /// The design resolution from project.godot. The stretch mode is
    /// canvas_items, so the readout is laid out once at this size and scaled to
    /// whatever the window is.
    private const float ScreenWidth = 1920.0f;
    private const float ScreenHeight = 1080.0f;

    /// Matches RunGrowth.OfferSize. The cards are built once and shown or hidden,
    /// because a scene that grows nodes mid-run allocates during the exact moment
    /// the offer exists to interrupt.
    private const int OfferCards = 3;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        var root = new Node3D { Name = "Main" };

        root.AddChild(BuildEnvironment());

        // Warm sun against the cool ambient the environment supplies. A single
        // white light and a grey ambient is the default look, and the default
        // look is what "it renders correctly and feels like a prototype" is made
        // of — the lit and unlit sides of a container differ in brightness and in
        // nothing else, so nothing on screen has a time of day.
        root.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55.0f, -35.0f, 0.0f),
            LightColor = new Color(1.0f, 0.94f, 0.83f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,

            // Long, soft shadows across a flat plane read as late afternoon and
            // give every prop a second silhouette on the ground — which is most
            // of what tells the player a box has volume under this camera.
            ShadowBlur = 1.4f,
            DirectionalShadowMaxDistance = 90.0f,
        });

        root.AddChild(BuildGround(GroundSize));

        var player = GD.Load<PackedScene>("res://scenes/Player.tscn")?.Instantiate();
        if (player == null)
        {
            GD.PushError("Missing res://scenes/Player.tscn — run BuildPlayer.cs first.");
            return false;
        }
        player.Name = "Player";
        root.AddChild(player);

        root.AddChild(BuildCameraRig());

        // Empty containers, filled at runtime. The layout is a per-run decision
        // and cannot live in a packed scene; what the scene owes it is somewhere
        // to put the results, in the tree, before anything goes looking.
        // The generator fills these; they have to exist in the saved scene.
        //
        // `LevelGenerator.Container` creates one if it is missing, and that path
        // does not work from `_Ready`: Godot refuses `add_child()` while a parent
        // is still setting up its children, prints "Parent node is busy setting up
        // children", and carries on. The generator then builds a whole subtree
        // into a node that is not in the tree — everything constructs, nothing
        // errors, and the contents simply do not exist. Danger zones spent their
        // first run that way.
        root.AddChild(new Node3D { Name = "Obstacles" });
        root.AddChild(new Node3D { Name = "LootContainers" });
        root.AddChild(new Node3D { Name = "ExtractionZones" });
        root.AddChild(new Node3D { Name = "DangerZones" });

        // Before the horde, whose flow field bakes obstacles once in _Ready.
        // Generating after that bake gives enemies a map they walk straight
        // through while the screen shows walls.
        var level = new Node3D { Name = "Level" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(level, "res://scripts/nodes/LevelGenerator.cs"));

        // Added last so Player, Obstacles and the zones are already in the tree
        // when the horde and the director look for them in _Ready.
        root.AddChild(BuildHorde());
        root.AddChild(BuildRunDirector());

        // Beside the player, never under it.
        //
        // A child inherits the body's yaw, and the player's body turns to face
        // the view — so a parented ring would spin with the character and the
        // blades would appear to hang motionless relative to whatever the player
        // was looking at. The ring belongs to the world; it follows the player's
        // position and ignores their rotation.
        var kit = new Node3D { Name = "RunKit" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(kit, "res://scripts/nodes/RunKit.cs"));

        // After the horde, whose kill event it subscribes to, and before the meta
        // manager, which hands it the caps the equipped gear allows.
        var growth = new Node { Name = "RunGrowth" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(growth, "res://scripts/nodes/RunGrowth.cs"));

        // Before the meta manager, which asks it to freeze the run: the log has
        // to have been subscribed for the whole run, not attached at the end.
        var log = new Node { Name = "RunLog" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(log, "res://scripts/nodes/RunLog.cs"));

        // After the director so its RunEnded signal exists to connect to, and
        // after the player so the loadout has something to equip.
        var meta = new Node { Name = "MetaManager" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(meta, "res://scripts/nodes/MetaManager.cs"));

        root.AddChild(BuildDebrief());

        root.AddChild(BuildHud());

        // Last, because they subscribe to everything above them — including the
        // loot containers the level generator put in the tree during its _Ready.
        var sound = new Node { Name = "Sound" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(sound, "res://scripts/nodes/SoundDirector.cs"));

        // Its own node with its own players, beside the sound director rather
        // than inside it. The SFX pool is a fixed ring that the oldest voice
        // recycles out of, so a music layer borrowed from it would be cut off by
        // a busy second of explosions.
        var music = new Node { Name = "Music" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(music, "res://scripts/nodes/MusicDirector.cs"));

        var effects = new Node3D { Name = "Effects" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(effects, "res://scripts/nodes/EffectDirector.cs"));

        // Above the readout, so a thumb on the stick is not competing with a
        // label for the same pixels. Present in every build and hidden unless
        // the device has a touchscreen — one scene, not two, which is the thing
        // the input abstraction was written for and never used.
        var touch = new CanvasLayer { Name = "TouchHud", Layer = 3 };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(touch, "res://scripts/nodes/TouchHud.cs"));

        Node scripted = SceneBuildUtil.AttachScriptToRoot(root, "res://scripts/nodes/GameRoot.cs");
        bool ok = SceneBuildUtil.PackAndSave(scripted, "res://scenes/Main.tscn");
        scripted.Free();
        return ok;
    }

    /// The scene had no environment at all, which is why it looked untouched.
    ///
    /// Without one Godot renders with no tone mapping and a flat default ambient:
    /// every surface is lit by exactly one number, highlights clip rather than
    /// roll off, and there is no colour anywhere the artist did not paint. The
    /// arena can be textured, the props can be modelled and the sprites can be
    /// good, and the result still reads as a viewport rather than as a place.
    ///
    /// The renderer is `mobile`, so screen-space effects are not on the table —
    /// no SSAO, no SSIL. Everything here is the part that is free: how the light
    /// is coloured, and how the final image is mapped.
    private static WorldEnvironment BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            // A sky, because there is a horizon now.
            //
            // This was a flat colour, and the comment justifying it said "the
            // camera is orthographic and tilted down 52°, so the horizon is never
            // in frame". That was true and stopped being true the moment the
            // camera became perspective at 26° — and the cost was not subtle. The
            // top third of every frame was pure black, with the ground running
            // into it along a hard line. It read as the level having an edge.
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = BuildSky(),

            // Ambient stays a colour rather than being taken from the sky. The
            // value below was tuned by eye against this geometry, and handing the
            // job to a sky that was chosen for how it looks on the horizon would
            // re-light the entire game as a side effect of a background change.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,

            // Cool, because the sun is warm. Shadowed faces picking up sky colour
            // instead of simply being darker is the single cheapest thing that
            // makes geometry look lit rather than shaded.
            AmbientLightColor = new Color(0.42f, 0.50f, 0.62f),
            AmbientLightEnergy = 0.55f,

            // Filmic rather than linear: linear clips anything bright to flat
            // white, which is what made the concrete tops read as paper. The
            // white point above 1 is what leaves headroom for them to stay grey.
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            TonemapWhite = 2.2f,

            AdjustmentEnabled = true,
            AdjustmentContrast = 1.08f,
            AdjustmentSaturation = 1.12f,
            AdjustmentBrightness = 1.0f,

            // Distance goes dark, and this is the whole reason the game has a
            // horizon worth looking at.
            //
            // Without it a perspective camera shows every enemy in the arena at
            // once, at full contrast, forty metres out — so nothing ever
            // *arrives*. They wink into existence at the spawn ring, in plain
            // sight, and the horde stops being a thing that closes in and becomes
            // a thing that is already there.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,

            // Near-black rather than atmospheric grey. The game is set at dusk
            // and the point is losing sight of things, not haze.
            FogLightColor = new Color(0.05f, 0.05f, 0.07f),
            FogLightEnergy = 1.0f,

            // **One, not zero.** Depth mode computes its falloff from the range
            // below and then still multiplies by this — a density of zero is fog
            // that is fully configured, reports no error, and contributes exactly
            // nothing. Every number here would look right in the inspector.
            FogDensity = 1.0f,

            // The sky takes the fog colour too. Without this the ground fades to
            // near-black and meets a sky that did not, and the horizon becomes a
            // hard bright line — worse than the black void it replaced, because
            // it looks deliberate.
            FogSkyAffect = 1.0f,

            // No sun scattering. It would put a bright bloom in the fog on
            // whichever side the sun is, which is a lovely effect in a landscape
            // and, in a game about not seeing what is coming, a light source
            // pointing at the thing you are trying to hide.
            FogSunScatter = 0.0f,

            FogDepthBegin = FogBeginMetres,
            FogDepthEnd = FogEndMetres,
            FogDepthCurve = FogCurve,
        };

        return new WorldEnvironment { Name = "Environment", Environment = environment };
    }

    /// Where the fog starts, in metres. Comfortably past the crowd immediately
    /// around the player — fog that begins where the fighting is would grey out
    /// the enemy actually biting you.
    private const float FogBeginMetres = 10.0f;

    /// Where the fog is total.
    ///
    /// Thirty-five, and the number matters. The horde spawns on a ring 12 to 40 m
    /// out, so at 35 the far half of that ring is inside the fog and enemies walk
    /// out of the dark instead of appearing in it. Set it to 42 — past the whole
    /// ring — and the fog is technically working and does nothing at all: it
    /// reaches full only where nothing is ever spawned.
    private const float FogEndMetres = 35.0f;

    /// Above 1, so the fog holds off and then closes quickly. A linear ramp
    /// starts dimming things at ten metres, which reads as the game being murky
    /// rather than as distance being dangerous.
    private const float FogCurve = 1.6f;

    /// A dusk sky that agrees with the fog at the horizon.
    ///
    /// Every colour here is dark. A sky bright enough to be pretty would put the
    /// brightest thing in the frame directly behind whatever is walking toward
    /// the player, and silhouettes are the only channel left at that distance.
    private static Sky BuildSky() => new()
    {
        SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.07f, 0.09f, 0.14f),

            // Meets the fog colour. `FogSkyAffect` above pushes the sky toward
            // the fog near the horizon, and starting close to it means the two
            // blend rather than crossfade through a third colour.
            SkyHorizonColor = new Color(0.11f, 0.11f, 0.13f),
            SkyCurve = 0.15f,
            SkyEnergyMultiplier = 1.0f,

            // The ground half of the sky is only ever seen past the edge of the
            // terrain, so it is the fog colour and nothing else.
            GroundBottomColor = new Color(0.05f, 0.05f, 0.07f),
            GroundHorizonColor = new Color(0.09f, 0.09f, 0.11f),
            GroundCurve = 0.02f,

            // No sun disc. There is a directional light for shape, but a visible
            // sun would date the scene to an hour the rest of the palette denies.
            SunAngleMax = 0.0f,
            SunCurve = 0.0f,
        },
    };

    private static Node3D BuildCameraRig()
    {
        var rig = new Node3D { Name = "CameraRig" };

        // Local to the rig, so the rig's yaw carries it. The camera itself is
        // never rotated about Y and never moved — every bit of "where am I
        // looking" lives in one place, on the parent.
        float tilt = Mathf.DegToRad(-CameraTiltDegrees);
        rig.AddChild(new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = CameraFov,
            Near = CameraNear,
            Far = CameraFar,
            Position = new Vector3(0.0f, CameraDistance * Mathf.Sin(tilt), CameraDistance * Mathf.Cos(tilt)),
            RotationDegrees = new Vector3(CameraTiltDegrees, 0.0f, 0.0f),
        });

        // Script last, once the hierarchy is complete — and only through the
        // helper, which owns the re-fetch that SetScript's disposal forces.
        var live = (CameraRig)SceneBuildUtil.AttachScriptToRoot(rig, "res://scripts/nodes/CameraRig.cs");
        live.TargetPath = new NodePath("../Player");
        return live;
    }

    private static Node3D BuildHorde()
    {
        var horde = new Node3D { Name = "Horde" };
        var live = (Horde)SceneBuildUtil.AttachScriptToRoot(horde, "res://scripts/nodes/Horde.cs");

        // A standing presence at spawn, with everything after it coming from the
        // run director's escalation curve rather than from a fixed pile.
        live.InitialSpawn = 24;
        return live;
    }

    private static Node3D BuildRunDirector()
    {
        var director = new Node3D { Name = "RunDirector" };
        return (RunDirector)SceneBuildUtil.AttachScriptToRoot(director, "res://scripts/nodes/RunDirector.cs");
    }

    /// The readout, as structure only. Every colour, string and width lives in
    /// Hud.cs — a builder that also styled would mean regenerating a scene to
    /// move a bar two pixels.
    ///
    /// Laid out for where the eye already is. Health and the backpack are the two
    /// things the player is trading against each other, so they share the top
    /// left corner; the clock and the payout-if-you-leave-now share the top
    /// centre, because that pair is the decision the whole run is about; the way
    /// out is top right, on its own, because it answers a different question.
    private static CanvasLayer BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };

        // Behind everything else, and never in the way of a click.
        hud.AddChild(new ColorRect
        {
            Name = "Vignette",
            Position = Vector2.Zero,
            Size = new Vector2(ScreenWidth, ScreenHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // Bottom-left, away from the health bar and out of the way of the
        // level-up cards, which own the middle of the screen when they appear.
        var minimap = new TextureRect
        {
            Name = "Minimap",
            Position = new Vector2(24.0f, ScreenHeight - 252.0f),
            Size = new Vector2(228.0f, 228.0f),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.88f),
        };

        hud.AddChild(SceneBuildUtil.AttachScriptToRoot(minimap, "res://scripts/nodes/Minimap.cs"));

        AddBar(hud, "Health", new Vector2(24.0f, 22.0f), new Vector2(360.0f, 26.0f));
        AddBar(hud, "Bag", new Vector2(24.0f, 58.0f), new Vector2(360.0f, 18.0f));
        AddBar(hud, "Level", new Vector2(24.0f, 122.0f), new Vector2(360.0f, 10.0f));

        hud.AddChild(new Label
        {
            Name = "Arms",
            Position = new Vector2(24.0f, 84.0f),
            Size = new Vector2(560.0f, 36.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Keys",
            Position = new Vector2(24.0f, 140.0f),
            Size = new Vector2(680.0f, 30.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Clock",
            Position = new Vector2(ScreenWidth * 0.5f - 300.0f, 16.0f),
            Size = new Vector2(600.0f, 52.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Payout",
            Position = new Vector2(ScreenWidth * 0.5f - 300.0f, 66.0f),
            Size = new Vector2(600.0f, 34.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Exit",
            Position = new Vector2(ScreenWidth - 500.0f - 24.0f, 22.0f),
            Size = new Vector2(500.0f, 90.0f),
        });

        // Bottom centre, above the offer: a hold bar is the one thing on screen
        // that is counting while the player stands still, so it sits where they
        // are already looking — at their own character.
        AddBar(hud, "Hold", new Vector2(ScreenWidth * 0.5f - 240.0f, ScreenHeight - 108.0f),
               new Vector2(480.0f, 22.0f));

        for (int i = 0; i < OfferCards; i++)
        {
            const float cardWidth = 300.0f;
            const float gap = 18.0f;
            float total = OfferCards * cardWidth + (OfferCards - 1) * gap;
            float x = ScreenWidth * 0.5f - total * 0.5f + i * (cardWidth + gap);

            hud.AddChild(new ColorRect
            {
                Name = $"Card{i}",
                Position = new Vector2(x, ScreenHeight - 226.0f),
                Size = new Vector2(cardWidth, 96.0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

            hud.AddChild(new Label
            {
                Name = $"Card{i}Text",
                Position = new Vector2(x, ScreenHeight - 210.0f),
                Size = new Vector2(cardWidth, 70.0f),
            });
        }

        hud.AddChild(new Label
        {
            Name = "Banner",

            // Upper third, not centre: the player sprite sits dead centre, and a
            // banner over it hides the moment it is announcing.
            Position = new Vector2(0.0f, 170.0f),
            Size = new Vector2(ScreenWidth, 200.0f),
        });

        // Its own label rather than borrowing the banner's. They overlap in time
        // only at the very end of a run, but "boss" and "you died" wanting the
        // same line is exactly the collision that shows up once, in front of a
        // player, and never in a probe.
        hud.AddChild(new Label
        {
            Name = "Alert",
            Position = new Vector2(0.0f, 96.0f),
            Size = new Vector2(ScreenWidth, 60.0f),
        });

        // The boss bar sits under the clock, not over the character. Full width
        // is deliberate: it is the only bar in the game that is about something
        // other than the player, and it should not be mistakable for one of theirs.
        hud.AddChild(new ColorRect
        {
            Name = "BossBarBack",
            Position = new Vector2(ScreenWidth * 0.5f - 420.0f, 150.0f),
            Size = new Vector2(840.0f, 14.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hud.AddChild(new ColorRect
        {
            Name = "BossBarFill",
            Position = new Vector2(ScreenWidth * 0.5f - 420.0f, 150.0f),
            Size = new Vector2(840.0f, 14.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        return (Hud)SceneBuildUtil.AttachScriptToRoot(hud, "res://scripts/nodes/Hud.cs");
    }

    /// The end-of-run report. A layer over the arena rather than a scene of its
    /// own: the horde is still walking in behind it, which is the honest picture
    /// — the run ended because the player left, not because the world did.
    private static CanvasLayer BuildDebrief()
    {
        var layer = new CanvasLayer { Name = "Debrief", Layer = 2 };

        layer.AddChild(new ColorRect
        {
            Name = "Panel",
            Position = new Vector2(ScreenWidth * 0.5f - 640.0f, 150.0f),
            Size = new Vector2(1280.0f, 730.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        layer.AddChild(new Label
        {
            Name = "Title",
            Position = new Vector2(ScreenWidth * 0.5f - 640.0f, 186.0f),
            Size = new Vector2(1280.0f, 60.0f),
        });

        layer.AddChild(new Label
        {
            Name = "Body",
            Position = new Vector2(ScreenWidth * 0.5f - 580.0f, 286.0f),
            Size = new Vector2(1160.0f, 480.0f),
        });

        layer.AddChild(new Label
        {
            Name = "Footer",
            Position = new Vector2(ScreenWidth * 0.5f - 640.0f, 818.0f),
            Size = new Vector2(1280.0f, 40.0f),
        });

        return (DebriefScreen)SceneBuildUtil.AttachScriptToRoot(layer, "res://scripts/nodes/DebriefScreen.cs");
    }

    /// A bar is a track, a fill and a caption over the top. Three nodes rather
    /// than a ProgressBar because the fill has to change colour with its own
    /// meaning — health goes red, the backpack goes amber as it closes on full —
    /// and theming a ProgressBar per state is more code than moving a rectangle.
    private static void AddBar(CanvasLayer hud, string name, Vector2 position, Vector2 size)
    {
        hud.AddChild(new ColorRect
        {
            Name = $"{name}Back",
            Position = position,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hud.AddChild(new ColorRect
        {
            Name = $"{name}Fill",
            Position = position + new Vector2(2.0f, 2.0f),
            Size = size - new Vector2(4.0f, 4.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hud.AddChild(new Label
        {
            Name = $"{name}Text",
            Position = position + new Vector2(8.0f, -1.0f),
            Size = size,
        });
    }

    private static StaticBody3D BuildGround(float size)
    {
        var body = new StaticBody3D { Name = "Ground" };

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ground.gdshader") };
        material.SetShaderParameter("detail", GD.Load<Texture2D>("res://assets/textures/ground.png"));

        // MaterialOverride rather than a material on the mesh: this node is
        // owned by the scene, so the override serialises — the case godot.md:46
        // warns about is an override on a node *inside* an imported GLB, whose
        // owner the packer skips.
        body.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            MaterialOverride = material,
        });

        // A plane collider has no thickness for fast movers to miss, so the floor
        // is a thin box sunk to put its top face at y = 0.
        const float thickness = 1.0f;
        body.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(size, thickness, size) },
            Position = new Vector3(0.0f, -thickness * 0.5f, 0.0f),
        });

        return body;
    }
}
