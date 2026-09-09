using Godot;

/// Photographs the effect vocabulary as a row, against the ground it is drawn on.
///
///   godot --script test/EffectShot.cs
///
/// Not headless — there is nothing to look at under the null driver, and this
/// exists precisely because the questions it answers are ones no exit-code probe
/// can ask. "Is the muzzle flash a star or a ball", "is the smoke darker than the
/// field", "does a stain land flat on a slope" are all properties of pixels.
///
/// Every effect in this game is emitted by something that also moves, dies or
/// fires, so the only picture of one used to be a frame of a firefight with
/// thirty of them overlapping — which is a fine test of whether the system runs
/// and a useless test of whether any single effect reads. This spawns one of
/// each, spaced four metres apart, and holds them still.
public partial class EffectShot : SceneTree
{
    private const string ScenePath = "res://scenes/Main.tscn";
    private const string OutputPath = "res://screenshots/effects.png";

    private Node? _scene;
    private EffectDirector? _effects;
    private int _frame;

    /// `-- bare` photographs the same frame with nothing staged.
    ///
    /// The reference half of the picture. Fog, ground tint and scatter debris all
    /// put brown and grey patches on this floor, and twice a stain that was
    /// drawing perfectly well was read as absent because it landed next to one —
    /// once as "the marks do not render" and once as "the marks render too light".
    /// A difference against the empty frame answers both without an opinion.
    private bool _bare;

    private string Output => _bare ? "res://screenshots/effects_bare.png" : OutputPath;

    public override void _Initialize()
    {
        if (!Display.Required(this, "EffectShot"))
            return;

        foreach (string argument in OS.GetCmdlineUserArgs())
            _bare |= argument == "bare";

        var scene = GD.Load<PackedScene>(ScenePath)?.Instantiate();
        if (scene == null)
        {
            GD.PushError($"Missing {ScenePath}");
            Quit(1);
            return;
        }

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        // The layout is pinned, and it has to be: the reference frame is only a
        // reference if it is the same field. `LevelGenerator` generates from
        // `_Ready`, so this is set before the scene enters the tree — a seed
        // assigned afterwards is a seed for a map that has already been built.
        var generator = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (generator != null)
            generator.Seed = 4021;

        GetRoot().AddChild(scene);
        _scene = scene;
        _effects = scene.GetNodeOrNull<EffectDirector>("Effects");
    }

    public override bool _Process(double delta)
    {
        _frame++;

        // The camera rig lerps onto the player, so an early frame photographs the
        // origin. Same warm-up as `Screenshot`.
        if (_frame < 26)
            return false;

        if (_effects == null)
        {
            GD.PushError("EffectShot: no EffectDirector");
            Quit(1);
            return true;
        }

        // Staged once, with lifetimes long enough to outlast the wait. Re-spawning
        // every frame would hold each puff at age zero, and a mark's opening
        // growth would never play — so the photograph would be of a state the
        // game never actually draws.
        if (_frame == 26 && !_bare)
            Stage(_effects);

        if (_frame < 44)
            return false;

        var image = GetRoot().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath(Output));
        GD.Print($"Wrote {ProjectSettings.GlobalizePath(Output)}");
        Quit(0);
        return true;
    }

    /// A row in front of the camera, in the order a shot happens: the flash at the
    /// muzzle, the spark at the target, the smoke behind both, the gore, and the
    /// two things left on the floor.
    private void Stage(EffectDirector effects)
    {
        EffectPool pool = effects.Effects;
        MarkField marks = effects.Marks;

        var player = _scene?.GetNodeOrNull<Node3D>("Player");
        Vector3 origin = player?.Position ?? Vector3.Zero;

        // Across the camera rather than away from it, so nothing occludes
        // anything and every sample is at the same distance and the same size on
        // screen. The camera looks down −Z, so +X is the row.
        // Seven samples across twelve metres, level with the player rather than
        // out in front of them.
        //
        // Both numbers were found by getting them wrong. The first version stepped
        // 3.6 m and put the last two off the right-hand edge — a picture of five
        // effects and a claim about seven. The second put the row seven metres
        // *ahead*, which is twenty from the camera, and this biome's fog is heavy
        // by then: the two ground stains came back as pale warm smudges and read
        // as not drawing at all. They were drawing correctly and being fogged
        // correctly, which is the worst way for a picture to be wrong. Kills
        // happen inside about ten metres, so that is where the samples belong.
        Vector3 Spot(int i) => new(origin.X - 6.0f + i * 2.0f, 0.0f, origin.Z + 1.5f);

        pool.Clear();

        pool.Spawn(Spot(0) + new Vector3(0.0f, 1.0f, 0.0f), 1.6f, 1.6f,
                   new Color(1.0f, 0.84f, 0.46f, 0.8f), 6.0f, Vector2.Zero, EffectShape.Flash, spin: 0.4f);

        pool.Spawn(Spot(1) + new Vector3(0.0f, 1.0f, 0.0f), 1.2f, 1.2f,
                   new Color(1.0f, 0.92f, 0.72f, 0.6f), 6.0f, Vector2.Zero);

        pool.Spawn(Spot(2) + new Vector3(0.0f, 1.0f, 0.0f), 1.8f, 1.8f,
                   new Color(0.20f, 0.19f, 0.18f, 0.5f), 6.0f, Vector2.Zero,
                   EffectShape.Smoke, soft: true);

        pool.Spawn(Spot(3) + new Vector3(0.0f, 1.0f, 0.0f), 1.5f, 1.5f,
                   new Color(0.62f, 0.80f, 0.34f, 0.6f), 6.0f, Vector2.Zero);

        pool.Spawn(Spot(4) + new Vector3(0.0f, 1.0f, 0.0f), 2.1f, 2.1f,
                   new Color(1.0f, 0.98f, 0.90f, 0.95f), 6.0f, Vector2.Zero,
                   EffectShape.Flash, spin: 1.1f);

        marks.Clear();
        marks.Spawn(Spot(5), 2.4f, new Color(0.020f, 0.028f, 0.010f, 0.72f), 8.0f, MarkShape.Splat, 0.7f);
        marks.Spawn(Spot(6), 3.0f, new Color(0.006f, 0.006f, 0.006f, 0.88f), 8.0f, MarkShape.Scorch, 0.0f);

        // A second row, four metres further out: what a kill leaves standing —
        // or rather not standing. Staged at the same instant as the puffs, so by
        // the shutter they are through the fall and lying flat, which is the state
        // they spend most of their five seconds in and the only one worth a
        // photograph. One of each of the first three variants, so a walker, a
        // runner and whatever is third can be compared lying down the way
        // `BodyShot` compares them upright.
        var horde = _scene?.GetNodeOrNull<Horde>("Horde");
        if (horde == null)
            return;

        horde.Corpses.Clear();
        for (int i = 0; i < 3 && i < horde.Types.Length; i++)
        {
            Vector3 at = Spot(i * 2) + new Vector3(0.0f, 0.0f, -4.5f);
            horde.Corpses.Spawn(i, 0, at, 0.0f, Mathf.Pi * 0.5f * i, 1.0f, 0.5f);
        }
    }
}
