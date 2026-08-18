using Godot;

public enum EnemyBehavior
{
    /// Closes to contact and does its damage by touching the player.
    Chase,

    /// Stops at StandoffDistance and shoots. Never touches, so kiting it is
    /// useless — the answer is cover or killing it.
    Ranged,
}

/// One enemy variant, as data. The horde carries a byte index per instance and
/// reads its numbers from here, so a variant costs one row rather than a class.
///
/// Everything a variant can change lives on this resource. If a stat is not
/// here, the horde applies it uniformly — which is the honest signal that it was
/// never designed to vary.
[GlobalClass]
public partial class EnemyTypeResource : Resource
{
    [Export] public string TypeName { get; set; } = "";

    [Export] public float MaxHealth { get; set; } = 10.0f;

    /// Metres per second before the run director's global SpeedScale.
    [Export] public float MoveSpeed { get; set; } = 2.4f;

    /// Damage per second while touching the player. Ranged variants leave this
    /// at zero — their damage arrives as projectiles.
    [Export] public float ContactDamagePerSecond { get; set; } = 6.0f;

    /// Multiplies the sprite's world size. The shader re-applies per-instance
    /// scale, so this costs nothing extra to vary (horde_billboard.gdshader:52).
    ///
    /// It is not the same number as the design height below, and cannot be: the
    /// wide variants do not fill their frame vertically, so part of this scale is
    /// spent cancelling the empty space above their heads rather than making them
    /// big. `BuildEnemySprites.cs` prints the value each sprite needs.
    [Export] public float SpriteScale { get; set; } = 1.0f;

    /// How tall this variant is meant to stand, in metres. The design intent, kept
    /// separate from the scale that achieves it so the two can be checked against
    /// each other — re-fitting the art moves the scale, and without something to
    /// compare it to a brute quietly becoming 2.4 m looks exactly like a brute.
    [Export] public float DesignHeightMeters { get; set; } = 2.0f;

    /// Layer in the horde's Texture2DArray. Layers are stacked in the order the
    /// types are listed, so this is an index into that list.
    [Export] public int SpriteLayer { get; set; }

    /// Multiplies incoming knockback. A brute at 0.2 barely moves, which is what
    /// makes a knockback weapon a choice rather than a strict upgrade.
    [Export] public float KnockbackScale { get; set; } = 1.0f;

    /// Damage dealt in a radius on death — the reason not to fight everything at
    /// arm's length. Zero disables the blast entirely.
    [Export] public float DeathBlastRadius { get; set; }
    [Export] public float DeathBlastDamage { get; set; }

    [Export] public EnemyBehavior Behavior { get; set; } = EnemyBehavior.Chase;

    /// Ranged only: stops closing here and starts shooting.
    [Export] public float StandoffDistance { get; set; } = 8.0f;
    [Export] public float AttackInterval { get; set; } = 2.4f;
    [Export] public float ProjectileSpeed { get; set; } = 11.0f;
    [Export] public float ProjectileDamage { get; set; } = 8.0f;

    /// Relative chance of being chosen once unlocked.
    [Export] public float SpawnWeight { get; set; } = 1.0f;

    /// Run intensity (0 at the start, 1 at the deadline) before this variant
    /// appears at all. Escalation is then a change of composition and not only
    /// of rate — the same 300 seconds asks for a different answer at the end.
    [Export] public float UnlockIntensity { get; set; }

    /// What a kill is worth to in-run growth. Unused until the growth phase, but
    /// it belongs on the row that already answers every other "how much".
    [Export] public float ExperienceValue { get; set; } = 1.0f;

    /// Far enemies update on one tick in this many. Derived from speed rather
    /// than configured: a fast variant stepped at quarter rate teleports in
    /// visible jumps, because the catch-up step scales with the stride.
    /// Powers of two only — the scheduler spreads work with a bit mask.
    public int FarStride => MoveSpeed switch
    {
        < 3.0f => 4,
        < 4.5f => 2,
        _ => 1,
    };
}
