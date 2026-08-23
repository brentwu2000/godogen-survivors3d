using Godot;

/// What a danger zone asks of the player.
public enum ZoneKind
{
    /// Stand in it and survive. The clock only runs while the player is inside,
    /// so leaving pauses rather than fails — a fight that punishes repositioning
    /// is a fight with one correct answer.
    Hold,

    /// Kill a quota inside it. Rewards aggression where Hold rewards nerve, and
    /// the two want different weapons, which is the point of having both.
    Purge,

    /// A sealed cache. Opening it is the trigger; the enemies arrive after, all
    /// at once, and the player has to leave with what they took.
    Breach,
}

/// Where a danger zone is and what it costs.
///
/// A plain value decided once per seed, separate from the node that runs it.
/// The level generator owns the map and is the only thing that can place a
/// thirteen-metre rectangle without landing it on a wall; `DangerZone` owns the
/// state machine and needs to know nothing about how it was sited.
///
/// **This is what replaces a spawn rate.** The run director used to interpolate
/// enemies-per-second from elapsed time, which makes threat a property of the
/// clock: the same pressure arrives wherever the player is and whatever they do,
/// and the only decision left is whether to keep moving. Threat is a *place*
/// now. Somewhere on the map is a rectangle that will pay for a hard two minutes,
/// and walking around it is a real option with a real cost.
public readonly record struct ZonePlan(
    Vector2 Centre,
    Vector2 HalfExtent,
    ZoneKind Kind,

    /// 0 or 1. Deeper zones are worth more and cost more; nothing else scales.
    int Tier,

    /// Seconds inside, for Hold. 35 to 60 — long enough that the first wave is
    /// not the whole encounter, short enough to be attempted with a half-full
    /// magazine.
    float HoldSeconds,

    /// Kills inside, for Purge.
    int PurgeKills,

    /// Rolls on the loot table when it pays out.
    int Rolls,

    /// Rounds handed to the player's reserve on completion.
    ///
    /// Ammunition rather than only loot, and it is not a garnish. A zone is the
    /// most expensive thing on the map to attempt and the reason to refuse one is
    /// almost always that the magazine will not carry it — so a reward that does
    /// not restock leaves the second zone strictly harder than the first, and the
    /// third impossible, however well the player did.
    int Rounds)
{
    /// Three zones, sited on a wide ring and spread apart.
    ///
    /// On a ring rather than anywhere, because a zone next to the player's
    /// spawn is one they walk into before they have a weapon worth the name, and
    /// a zone at the very edge competes with the extraction pads for the same
    /// ground.
    ///
    /// Deterministic from the level seed, like everything else the generator
    /// decides. Two runs of the same seed have to be the same run or the balance
    /// sweep is measuring noise.
    public static ZonePlan[] Plan(int count, float extent, System.Func<float> nextFloat)
    {
        var plans = new ZonePlan[Mathf.Max(0, count)];
        if (plans.Length == 0)
            return plans;

        float baseAngle = nextFloat() * Mathf.Tau;

        for (int i = 0; i < plans.Length; i++)
        {
            // Evenly spaced with a jitter under half the gap, so they never
            // collide and never form a visible triangle.
            float angle = baseAngle + Mathf.Tau * (i + (nextFloat() - 0.5f) * 0.5f) / plans.Length;

            // Between a third and half of the way out. Inside the pads, outside
            // the opening minutes.
            float radius = extent * (0.34f + nextFloat() * 0.16f);

            var kind = (ZoneKind)(i % 3);

            // Tier by depth, so the map's own geography says which is worth more
            // before the player has been told anything.
            int tier = radius > extent * 0.42f ? 1 : 0;

            plans[i] = new ZonePlan(
                Centre: new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius),
                HalfExtent: new Vector2(13.0f, 10.0f),
                Kind: kind,
                Tier: tier,
                HoldSeconds: Mathf.Lerp(35.0f, 60.0f, tier),
                PurgeKills: tier == 0 ? 18 : 30,
                Rolls: tier == 0 ? 3 : 5,
                Rounds: tier == 0 ? 60 : 110);
        }

        return plans;
    }

    /// How hard it pushes while running, in enemies per second.
    ///
    /// A Breach is a single burst rather than a rate, so its steady pressure is
    /// low; a Hold has to keep producing for a minute without the player ever
    /// being able to stand still and reload.
    public float SpawnRate => Kind switch
    {
        ZoneKind.Hold => 2.4f + Tier * 1.4f,
        ZoneKind.Purge => 3.0f + Tier * 1.6f,
        ZoneKind.Breach => 1.0f + Tier * 0.6f,
        _ => 2.0f,
    };

    /// Enemies delivered at once when it starts. The whole encounter for a
    /// Breach; an opening statement for the others.
    public int OpeningBurst => Kind switch
    {
        ZoneKind.Breach => 14 + Tier * 10,
        _ => 5 + Tier * 3,
    };

    public string Title => Kind switch
    {
        ZoneKind.Hold => Tier == 0 ? "Holdout" : "Deep Holdout",
        ZoneKind.Purge => Tier == 0 ? "Nest" : "Deep Nest",
        ZoneKind.Breach => Tier == 0 ? "Sealed Cache" : "Deep Cache",
        _ => "Zone",
    };
}
