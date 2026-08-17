using Godot;

/// Writes the enemy variant table to resources/enemies/*.tres.
///
///   godot --headless --script scripts/tools/BuildEnemyTypes.cs
///
/// Order matters: SpriteLayer is the index into the horde's Texture2DArray, and
/// the array is stacked in this order. Re-run to reset the table to its designed
/// baseline.
public partial class BuildEnemyTypes : SceneTree
{
    private const string OutputDir = "res://resources/enemies";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        Error dirError = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
        if (dirError != Error.Ok && dirError != Error.AlreadyExists)
        {
            GD.PushError($"Could not create {OutputDir}: {dirError}");
            return false;
        }

        EnemyTypeResource[] types =
        {
            // The baseline the whole game was tuned against through Phase 6.
            new()
            {
                TypeName = "walker",
                SpriteLayer = 0,
                MaxHealth = 10.0f,
                MoveSpeed = 2.4f,
                ContactDamagePerSecond = 6.0f,
                SpriteScale = 1.0f,
                SpawnWeight = 1.0f,
                UnlockIntensity = 0.0f,
                ExperienceValue = 1.0f,
            },

            // Fragile and faster than the player's comfortable kiting speed, so
            // standing still stops being free. Dies to one hit of anything.
            new()
            {
                TypeName = "runner",
                SpriteLayer = 1,
                MaxHealth = 4.0f,
                MoveSpeed = 4.6f,
                ContactDamagePerSecond = 4.0f,
                SpriteScale = 0.9f,
                SpawnWeight = 0.8f,
                UnlockIntensity = 0.2f,
                ExperienceValue = 1.0f,
            },

            // The reason knockback and penetration are stats rather than flavour:
            // it soaks a magazine and shrugs off the axe's shove.
            new()
            {
                TypeName = "brute",
                SpriteLayer = 2,
                MaxHealth = 60.0f,
                MoveSpeed = 1.4f,
                ContactDamagePerSecond = 14.0f,
                SpriteScale = 1.5f,
                KnockbackScale = 0.2f,
                SpawnWeight = 0.35f,
                UnlockIntensity = 0.45f,
                ExperienceValue = 4.0f,
            },

            // Kills at arm's length after it dies, so clearing a pile face-first
            // costs something. The blast damages the horde too, but only one
            // level deep — see Horde.Damage.
            new()
            {
                TypeName = "bloater",
                SpriteLayer = 3,
                MaxHealth = 25.0f,
                MoveSpeed = 1.8f,
                ContactDamagePerSecond = 6.0f,
                SpriteScale = 1.2f,
                DeathBlastRadius = 3.0f,
                DeathBlastDamage = 25.0f,
                SpawnWeight = 0.4f,
                UnlockIntensity = 0.6f,
                ExperienceValue = 3.0f,
            },

            // The only variant that does not want to touch the player. Standing
            // off means cover and closing distance become the answer, which is
            // the one thing a pure melee horde can never ask for.
            new()
            {
                TypeName = "spitter",
                SpriteLayer = 4,
                MaxHealth = 8.0f,
                MoveSpeed = 2.0f,
                ContactDamagePerSecond = 0.0f,
                SpriteScale = 1.0f,
                Behavior = EnemyBehavior.Ranged,
                StandoffDistance = 8.0f,
                AttackInterval = 2.4f,
                ProjectileSpeed = 11.0f,
                ProjectileDamage = 8.0f,
                SpawnWeight = 0.5f,
                UnlockIntensity = 0.3f,
                ExperienceValue = 2.0f,
            },
        };

        foreach (EnemyTypeResource type in types)
        {
            string path = $"{OutputDir}/{type.TypeName}.tres";
            Error err = ResourceSaver.Save(type, path);
            if (err != Error.Ok)
            {
                GD.PushError($"Save failed for {path}: {err}");
                return false;
            }
            GD.Print($"Saved {path}");
        }

        return true;
    }
}
