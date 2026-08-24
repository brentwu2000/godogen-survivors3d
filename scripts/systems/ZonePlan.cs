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

    /// Seconds inside, for Hold. 35 to 50 — long enough that the first wave is
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
    /// Which zones this map asks for.
    ///
    /// **It was `i % 3`, so every map ever generated had exactly one Hold, one
    /// Purge and one Breach.** The positions moved, the tiers moved, and the
    /// *structure* of a run never did — once the player had learned the three,
    /// every map was the same checklist in rearranged geography. That is the
    /// single clearest reason a second run felt like the first.
    ///
    /// The three kinds want different things: Hold rewards nerve and a weapon
    /// that holds ground, Purge rewards aggression and a weapon that clears, and
    /// Breach rewards being able to leave in a hurry. A map that always contains
    /// all three never makes any of those the wrong build. A map with two Holds
    /// and no Breach does.
    ///
    /// Rules, in order of how much they matter:
    ///
    ///   - **Never all the same.** Three Holds is one long fight repeated, and a
    ///     run whose every zone refuses the same loadout is a run the player
    ///     cannot answer at all.
    ///   - **A repeat is allowed and is the point.** Two of one kind and one of
    ///     another is what makes a map lean, and leaning is what makes the choice
    ///     of what to bring matter.
    ///   - **Order is shuffled**, so the nearest zone is not always the same
    ///     kind. The first zone the player meets sets what they expect of the
    ///     rest.
    private static ZoneKind[] Composition(int count, System.Func<float> nextFloat)
    {
        var kinds = new ZoneKind[count];
        if (count == 0)
            return kinds;

        const int Kinds = 3;

        // One kind the map leans on, and a second to keep it from being a
        // monoculture. Drawn as an offset from the first rather than
        // independently, so "the other one" can never land back on the same kind
        // and need re-rolling.
        int lead = (int)(nextFloat() * Kinds) % Kinds;
        int other = (lead + 1 + (int)(nextFloat() * (Kinds - 1))) % Kinds;

        for (int i = 0; i < count; i++)
        {
            // Two thirds lead, one third other. At three zones that is usually
            // 2-1 and sometimes 1-2, which are different maps rather than
            // different orderings of the same one.
            kinds[i] = (ZoneKind)(nextFloat() < 0.62f ? lead : other);
        }

        // The guard, and it has to be a guard rather than a tendency: a run of
        // three identical draws is one map in nine, which is often enough that
        // somebody would meet it on their second evening.
        bool same = true;
        for (int i = 1; i < count && same; i++)
            same = kinds[i] == kinds[0];

        // Replaced with *whichever it is not*, and that is the whole of the fix.
        //
        // The first version wrote `other` unconditionally, which is correct when
        // the three all came up `lead` and does nothing at all when they all came
        // up `other` — it overwrites a value with itself. At a 0.38 draw that is
        // about five per cent of maps, and `StageCompositionVaries` caught two in
        // forty on its first run. A guard that only covers one of the two ways
        // its condition can be reached is not a guard.
        if (same && count > 1)
        {
            kinds[count - 1] = kinds[0] == (ZoneKind)lead
                ? (ZoneKind)other
                : (ZoneKind)lead;
        }

        // Shuffled, so the lead kind is not reliably the one nearest the spawn.
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(nextFloat() * (i + 1));
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }

        return kinds;
    }

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

        ZoneKind[] kinds = Composition(plans.Length, nextFloat);

        float baseAngle = nextFloat() * Mathf.Tau;

        for (int i = 0; i < plans.Length; i++)
        {
            // Evenly spaced with a jitter under half the gap, so they never
            // collide and never form a visible triangle.
            float angle = baseAngle + Mathf.Tau * (i + (nextFloat() - 0.5f) * 0.5f) / plans.Length;

            // Between a third and half of the way out. Inside the pads, outside
            // the opening minutes.
            float radius = extent * (0.34f + nextFloat() * 0.16f);

            ZoneKind kind = kinds[i % kinds.Length];

            // Tier by depth, so the map's own geography says which is worth more
            // before the player has been told anything.
            int tier = radius > extent * 0.42f ? 1 : 0;

            plans[i] = new ZonePlan(
                Centre: new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius),
                HalfExtent: new Vector2(13.0f, 10.0f),
                Kind: kind,
                Tier: tier,
                HoldSeconds: Mathf.Lerp(35.0f, 50.0f, tier),
                PurgeKills: tier == 0 ? 18 : 30,
                Rolls: tier == 0 ? 3 : 5,
                Rounds: tier == 0 ? 60 : 110);
        }

        return plans;
    }

    /// How hard it pushes while running, in enemies per second.
    ///
    /// Twice measured, twice moved, and both numbers are in the history for a
    /// reason — this is the one dial the whole feature turns on.
    ///
    /// It started at 2.4/3.0/1.0 with a +1.4/+1.6/+0.6 tier bonus. A tier-1 Hold
    /// was then 60 seconds at 3.8 a second, which is 228 enemies against a
    /// starting rifle: the bot died at 80 s with 123 on the field, banking 140
    /// against the 406 it takes home by walking past. Not a hard decision, a
    /// wrong one.
    ///
    /// Cut to 1.5/1.9/0.7 with the Hold shortened to 50 seconds, and five seeds
    /// through both arms said the opposite: 5 of 5 survived either way, a zone
    /// paid 2.4 times as much, and it cost a median of **three health points**.
    /// Free money is not a decision either.
    ///
    /// These are thirty percent above that — 150 enemies through a tier-1 Hold
    /// rather than 115 or 228. The cost is meant to be the risk of standing still
    /// while a crowd arrives, which means it has to actually cost something.
    ///
    /// A Breach is a single burst rather than a rate, so its steady pressure is
    /// low; a Hold has to keep producing for the better part of a minute without
    /// the player ever standing still to reload.
    public float SpawnRate => Kind switch
    {
        // Tier 0 raised, tier 1 left exactly where it was.
        //
        // Measured over twelve layouts with `BalanceSweep -- zones:tiers`, which
        // is the first table that could tell the tiers apart at all — before it,
        // the bot took whichever zone was nearest and the two tiers were averaged
        // into one column that looked like noise. Split, they read:
        //
        //   past     12/12 survived, banked 638, lowest HP 98
        //   tier 0   13/13 survived, banked 1052, lowest HP 91
        //   tier 1   10/11 survived, banked 1328, lowest HP 59
        //
        // Tier 1 is priced. Tier 0 was not a gamble at all: seven points of health
        // and nobody ever died, for sixty-five per cent more money. A shallow zone
        // that always pays is not "a dangerous place you chose to walk into", it is
        // a chore with a reward attached, and the correct play is to take it every
        // single run — which collapses the choice the zones exist to create.
        //
        // The bases move about forty per cent of the way toward tier 1 and the
        // per-tier steps shrink by the same amount, so every tier-1 number here is
        // unchanged to two decimal places. Only the cheap one gets dearer.
        ZoneKind.Hold => 3.45f + Tier * 0.90f,
        ZoneKind.Purge => 4.31f + Tier * 1.14f,
        ZoneKind.Breach => 1.58f + Tier * 0.42f,
        _ => 2.6f,
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
