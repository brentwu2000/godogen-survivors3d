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

                // Drawn from an authored model rather than from `MeshBuilder`.
                //
                // The procedural walker was correct in every proportion and
                // still read as stacked primitives, because that is what it was.
                // The baker exists so a modelled body can enter a `MultiMesh`,
                // the stalker proved it, and this is the first of the humanoids
                // to follow — 448 triangles, three surfaces, rigged with the bone
                // names `BakeBody.Classify` matches.
                //
                // Its proportions are the ones this table and
                // `BodyMeshLibrary.ForVariant` already agreed on: hip at 46% of
                // height, shoulder at 80%, hands just below the hip. The model
                // was rebuilt once because the first attempt had arms to the
                // knees, which is what happens when a modelling brief carries a
                // reference picture and no numbers.
                BakedBodyPath = "res://resources/bodies/walker.res",
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

            // The first variant that is not a person.
            //
            // Six variants in and every one of them was upright, bilateral and
            // roughly human-sized, so the horde read as one silhouette at six
            // scales — which is most of "a few coloured blocks". This one runs on
            // four limbs: 1.3 m at the shoulder and 2.2 m nose to tail, longer
            // than it is tall, and the only thing in the crowd whose outline is
            // wider than it is high.
            //
            // Fast and fragile, like the runner, because a low silhouette that
            // also soaked damage would be a wall the player cannot see over. The
            // threat it carries is that it is hard to *pick out*, not that it is
            // hard to kill.
            //
            // Drawn from an authored model rather than from `MeshBuilder`. It is
            // the first variant that is, and the reason `BakeBody` exists: a
            // quadruped assembled from boxes and tubes is a week of fiddling with
            // pivot fractions for a shape somebody can draw in an afternoon.
            new()
            {
                TypeName = "stalker",
                SpriteLayer = 6,
                BakedBodyPath = "res://resources/bodies/stalker.res",
                DesignHeightMeters = 1.3f,
                MaxHealth = 8.0f,
                MoveSpeed = 4.2f,
                ContactDamagePerSecond = 7.0f,

                // What the sprite needs, printed by `BuildEnemySprites`: the
                // painting is a long low quadruped and fills 48.4% of a frame
                // shaped for a standing figure, so it takes 1.342 to come out at
                // 1.3 m on the billboard path.
                //
                // It has no effect on the solid body, which is built at
                // `DesignHeightMeters` and drawn at it — see `BodyRenderer`.
                SpriteScale = 1.342f,

                // Shoved easily. Four legs low to the ground is a shape that
                // should skid, and it is the compensation for how hard it is to
                // see coming.
                KnockbackScale = 1.4f,

                SpawnWeight = 0.7f,

                // After the runner and before the brute. The horde's first
                // lesson is that things get faster; the stalker is the second,
                // that they stop looking like people.
                UnlockIntensity = 0.3f,
                ExperienceValue = 2.0f,
            },

            // A wall that walks.
            //
            // The first thing in the horde whose job is to *stop* the player
            // rather than to reach them. Enormous health, almost no damage, and
            // knockback that barely moves it — shooting it is a decision to spend
            // time, and time is the resource the run is actually about.
            //
            // Slow on purpose and not merely slow: at 1.1 m/s the player can
            // always walk around it, so it never removes an option. What it does
            // is make the option *cost* something, which is the difference
            // between an obstacle and a wall.
            new()
            {
                TypeName = "bulwark",
                SpriteLayer = 7,
                DesignHeightMeters = 1.5f,

                // Four times the brute. Nothing else in the table is close, and
                // that is the point: the player has to learn to leave it alone.
                MaxHealth = 140.0f,
                MoveSpeed = 1.1f,

                // Low. It is not a damage threat and pretending otherwise would
                // make ignoring it wrong, which is the one behaviour it is meant
                // to teach.
                ContactDamagePerSecond = 6.0f,

                // Wider than tall, so the painting fills 46.9% of an upright
                // frame and needs 1.600 to stand 1.5 m on the billboard path.
                // The solid body carries its own proportions.
                SpriteScale = 1.600f,

                // Barely shifts. A shotgun blast that skids a brute moves this a
                // hand's width.
                KnockbackScale = 0.15f,

                SpawnWeight = 0.45f,

                // Late. It is a lesson about spending ammunition, and the player
                // needs to have something worth spending first.
                UnlockIntensity = 0.55f,

                // Worth the time it costs, and no more. Paying out for a kill
                // the player was supposed to walk around would argue with the
                // whole design of it.
                ExperienceValue = 6.0f,
            },

            // Dark, and carrying a light.
            //
            // The arena goes black somewhere between twenty-four and forty-four
            // metres depending on the biome, and until now the dark was
            // uniformly empty — a thing was either in the lit part or was not
            // there. This is the first enemy visible *before* it arrives, which
            // inverts what the fog means: an approaching glow is free
            // information the player has to decide what to do with.
            //
            // Fragile, fast-ish, and it hurts. The bargain is that it announces
            // itself from further away than anything else and is worse than
            // average to let reach you — seeing it coming is the compensation
            // for what happens if you ignore it.
            new()
            {
                TypeName = "lantern",
                SpriteLayer = 8,
                DesignHeightMeters = 1.9f,
                MaxHealth = 14.0f,
                MoveSpeed = 3.0f,
                ContactDamagePerSecond = 16.0f,
                SpriteScale = 0.950f,
                KnockbackScale = 1.2f,
                SpawnWeight = 0.5f,

                // Between the stalker and the bulwark. The horde's lessons in
                // order: things get faster, they stop looking like people, they
                // start seeing you first, and some of them are not worth
                // shooting.
                UnlockIntensity = 0.42f,
                ExperienceValue = 3.0f,
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
