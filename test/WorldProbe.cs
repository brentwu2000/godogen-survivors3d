using Godot;

/// Checks the environment the scene actually saved: sky, fog, and the numbers
/// that make fog do something.
///
///   godot --headless --script test/WorldProbe.cs
///
/// Read off `Main.tscn` rather than off `BuildMain`, which is the point. The
/// builder is a script that runs by hand; the scene is what ships. Every number
/// here could be correct in the builder and absent from the file, and the way you
/// would find out is by looking at the game.
///
/// Fog is unusually good at being configured and inert. It has eight properties,
/// no required ones, and no combination of them is an error — a fog with zero
/// density, or one that reaches full only past the far plane, or one whose range
/// ends beyond anything that exists, all load clean and render nothing. The
/// inspector looks identical in every case.
public partial class WorldProbe : SceneTree
{
    private bool _failed;

    public override void _Initialize()
    {
        var packed = GD.Load<PackedScene>("res://scenes/Main.tscn");
        if (packed?.Instantiate() is not Node scene)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var world = scene.GetNodeOrNull<WorldEnvironment>("Environment");
        var horde = scene.GetNodeOrNull<Horde>("Horde");
        Camera3D? camera = scene.GetNodeOrNull<Camera3D>("CameraRig/Camera");

        if (world?.Environment is not Godot.Environment env || horde == null || camera == null)
        {
            GD.PushError($"PROBE FAILED — environment={world?.Environment != null} " +
                         $"horde={horde != null} camera={camera != null}");
            Quit(1);
            return;
        }

        CheckSky(env);
        CheckFog(env, horde, camera);
        CheckCamera(camera);

        GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
        Quit(_failed ? 1 : 0);
    }

    private void CheckSky(Godot.Environment env)
    {
        bool isSky = env.BackgroundMode == Godot.Environment.BGMode.Sky;
        bool hasMaterial = env.Sky?.SkyMaterial is ProceduralSkyMaterial;

        GD.Print($"  background {env.BackgroundMode}, sky material " +
                 $"{env.Sky?.SkyMaterial?.GetType().Name ?? "none"}");

        // A flat colour was right while the camera was orthographic at 52° and
        // the horizon was never in frame. At 26° perspective the top third of
        // every frame is sky, and a flat one meets the ground along a hard line
        // that reads as the level having an edge.
        if (!isSky)
            GD.PushError("  the background is not a sky — the horizon is in frame now");

        if (!hasMaterial)
            GD.PushError("  the sky has no material — it will render as flat grey");

        Report("the background is a sky", isSky && hasMaterial);
    }

    private void CheckFog(Godot.Environment env, Horde horde, Camera3D camera)
    {
        GD.Print($"  fog {(env.FogEnabled ? "on" : "OFF")} mode {env.FogMode} " +
                 $"density {env.FogDensity:F2} over {env.FogDepthBegin:F0}–{env.FogDepthEnd:F0} m " +
                 $"curve {env.FogDepthCurve:F2}, sky affect {env.FogSkyAffect:F2}");
        GD.Print($"  the horde spawns on a ring {horde.SpawnRingMin:F0}–{horde.SpawnRingMax:F0} m out");

        Report("fog is switched on", env.FogEnabled);
        Report("fog is in depth mode", env.FogMode == Godot.Environment.FogModeEnum.Depth);

        // The one that has actually happened. Depth mode computes falloff from
        // the range and then multiplies by density, so zero is fog that is fully
        // configured, reports nothing, and contributes nothing.
        if (env.FogDensity <= 0.0f)
            GD.PushError("  fog density is zero — depth mode still scales by it, so this fog does nothing");

        Report("fog density is not zero", env.FogDensity > 0.0f);

        // Fog that reaches full past everything that exists is fog that works
        // perfectly and hides nothing. The horde spawns on a ring; the far half
        // of it has to be inside the fog, or enemies appear in plain sight at the
        // spawn distance and the whole point is lost.
        bool hidesSpawns = env.FogDepthEnd > horde.SpawnRingMin && env.FogDepthEnd < horde.SpawnRingMax;
        if (!hidesSpawns)
        {
            GD.PushError($"  fog reaches full at {env.FogDepthEnd:F0} m, outside the " +
                         $"{horde.SpawnRingMin:F0}–{horde.SpawnRingMax:F0} m spawn ring — " +
                         "nothing ever emerges from it");
        }

        Report("the fog covers the far half of the spawn ring", hidesSpawns);

        // And it must not start where the fighting is. Enemies reach contact at
        // well under two metres; fog beginning there would grey out the thing
        // currently biting you.
        bool startsClear = env.FogDepthBegin > horde.ContactRadius * 4.0f
                           && env.FogDepthBegin < env.FogDepthEnd;

        if (!startsClear)
            GD.PushError($"  fog begins at {env.FogDepthBegin:F1} m, which is inside the fight");

        Report("the fog begins past the fighting", startsClear);

        // Without sky affect the ground fades to the fog colour and meets a sky
        // that did not, and the horizon becomes a hard bright line — worse than
        // the black void it replaced, because it looks deliberate.
        Report("the fog reaches the sky too", env.FogSkyAffect > 0.99f);
    }

    private void CheckCamera(Camera3D camera)
    {
        GD.Print($"  camera {camera.Projection} fov {camera.Fov:F0}° " +
                 $"near {camera.Near:F2} far {camera.Far:F0}");

        Report("the camera is perspective", camera.Projection == Camera3D.ProjectionType.Perspective);

        // The far plane has to be past the fog, not at it. Clipping is a hard
        // pop; fog is a fade. A far plane inside the fog range would mean things
        // wink out while still partly visible, which reads as a rendering bug and
        // is the one artefact fog exists to prevent.
        var world = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        var env = world?.GetNodeOrNull<WorldEnvironment>("Environment")?.Environment;
        bool clipsBeyondFog = env == null || camera.Far > env.FogDepthEnd * 1.5f;

        if (!clipsBeyondFog)
            GD.PushError($"  the far plane at {camera.Far:F0} m is too close to the fog end — " +
                         "geometry will pop out rather than fade");

        Report("the far plane is well past the fog", clipsBeyondFog);
    }

    private void Report(string label, bool ok)
    {
        GD.Print($"{label}: {(ok ? "ok" : "FAILED")}");
        _failed |= !ok;
    }
}
