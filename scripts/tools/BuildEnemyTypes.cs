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
                DesignHeightMeters = 2.0f,
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
                DesignHeightMeters = 1.8f,
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
                DesignHeightMeters = 3.0f,
                MaxHealth = 60.0f,
                MoveSpeed = 1.4f,
                ContactDamagePerSecond = 14.0f,
                SpriteScale = 2.098f,
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
                DesignHeightMeters = 2.4f,
                MaxHealth = 25.0f,
                MoveSpeed = 1.8f,
                ContactDamagePerSecond = 6.0f,
                SpriteScale = 1.592f,
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
                DesignHeightMeters = 2.0f,
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

            // The run's climax, and the only enemy the director places by hand.
            //
            // UnlockIntensity above 1 keeps it out of the weighted roll entirely:
            // a boss that could turn up twice, or not at all, is not a climax, it
            // is a rare spawn. Enormous and slow, so the fight is about the space
            // around it rather than about reflexes — and everything else on the
            // field is still there while it comes.
            new()
            {
                TypeName = "boss",
                SpriteLayer = 5,
                DesignHeightMeters = 5.5f,
                MaxHealth = 1600.0f,
                MoveSpeed = 1.15f,
                ContactDamagePerSecond = 26.0f,

                // Slow enough to outwalk, which is the reason it also shoots.
                // Siege rather than Ranged: it opens fire at 22 m and keeps
                // coming, so distance buys time and never buys safety.
                Behavior = EnemyBehavior.Siege,
                StandoffDistance = 22.0f,
                AttackInterval = 1.5f,
                ProjectileSpeed = 13.0f,
                ProjectileDamage = 14.0f,
                SpriteScale = 3.157f,
                KnockbackScale = 0.0f,
                DeathBlastRadius = 6.0f,
                DeathBlastDamage = 30.0f,
                SpawnWeight = 0.0f,
                UnlockIntensity = 2.0f,
                ExperienceValue = 120.0f,
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
