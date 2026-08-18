using Godot;

/// One rule bent on an otherwise ordinary enemy.
///
/// A run whose only escalation is "more of them, faster" runs out of things to
/// say about ninety seconds in. An elite is the cheapest possible answer: no new
/// sprite, no new behaviour tree, no second code path — the same enemy with one
/// number changed and a colour that says which.
public enum EliteKind : byte
{
    None,

    /// Takes a fraction of incoming damage. The answer to a build that has
    /// solved crowds and never has to aim.
    Armoured,

    /// Moves much faster. Punishes standing still, which is what a player does
    /// once their weapon is strong enough to clear the ring around them.
    Swift,

    /// Bursts hard on death. Makes killing the thing in your face a decision
    /// rather than the obvious move.
    Volatile,
}

/// What each kind changes, and what it looks like.
///
/// A table rather than a switch in three files. The tint is the contract with
/// the player: they have to be able to tell at a glance which rule is bent, and
/// there is exactly one place that decides.
public static class Elites
{
    /// Multiplies incoming damage.
    public static float DamageScale(byte kind) => (EliteKind)kind switch
    {
        EliteKind.Armoured => 0.35f,
        _ => 1.0f,
    };

    public static float SpeedScale(byte kind) => (EliteKind)kind switch
    {
        EliteKind.Swift => 1.9f,
        _ => 1.0f,
    };

    /// Health multiplier. Everything that is worth marking is worth surviving
    /// long enough to be recognised.
    public static float HealthScale(byte kind) => (EliteKind)kind switch
    {
        EliteKind.None => 1.0f,
        EliteKind.Swift => 2.0f,
        _ => 3.0f,
    };

    /// Extra size, so an elite reads before its colour does. Silhouette first —
    /// a player fighting fifty things does not compare colours.
    public static float ScaleBonus(byte kind) => kind == (byte)EliteKind.None ? 1.0f : 1.25f;

    public static float ExperienceScale(byte kind) => kind == (byte)EliteKind.None ? 1.0f : 4.0f;

    /// Radius and damage of the burst a Volatile leaves. Zero for the rest.
    public static (float Radius, float Damage) DeathBlast(byte kind) => (EliteKind)kind switch
    {
        EliteKind.Volatile => (4.5f, 40.0f),
        _ => (0.0f, 0.0f),
    };

    /// Carried on the instance colour block and read by the horde shader. Green
    /// and blue only: red is the hit flash, and the two have to be able to
    /// happen at the same time.
    public static Color Tint(byte kind) => (EliteKind)kind switch
    {
        EliteKind.Armoured => new Color(0.0f, 0.35f, 0.85f),
        EliteKind.Swift => new Color(0.0f, 0.95f, 0.35f),
        EliteKind.Volatile => new Color(0.0f, 0.75f, 0.10f),
        _ => new Color(0.0f, 0.0f, 0.0f),
    };

    public static string Name(byte kind) => ((EliteKind)kind).ToString().ToLower();
}
