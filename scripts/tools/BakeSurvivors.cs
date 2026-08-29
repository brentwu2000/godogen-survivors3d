using Godot;

/// Bakes every authored survivor from one table.
///
///   godot --headless --script scripts/tools/BakeSurvivors.cs
///
/// Keeping gait, height and palette beside the roster prevents seven manual
/// BakeBody command lines becoming seven subtly different asset pipelines.
public partial class BakeSurvivors : SceneTree
{
    private readonly record struct Spec(
        string Name, float Height, float LegSwing, float ArmSwing, float Bob,
        string[] Palette);

    private static readonly Spec[] Roster =
    {
        new("drifter", 2.20f, 0.60f, 0.33f, 0.040f,
            new[] { "385785", "424d61", "b89a7a", "2e3a4a" }),
    };

    public override void _Initialize()
    {
        bool ok = true;
        foreach (Spec spec in Roster)
        {
            var palette = new Color[spec.Palette.Length];
            for (int i = 0; i < palette.Length; i++)
                palette[i] = BakeBody.Tint(spec.Palette[i]);

            ok &= BakeBody.Bake(
                $"res://assets/models/survivors/{spec.Name}.glb",
                $"res://resources/bodies/{spec.Name}.res",
                spec.Height, spec.LegSwing, spec.ArmSwing, spec.Bob, palette);
        }

        Quit(ok ? 0 : 1);
    }
}
