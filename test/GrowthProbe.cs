using Godot;

/// Checks the growth model: that a run starts where practice and gear put it,
/// climbs on kills, and stops at the ceiling the weapon declares.
///
///   godot --headless --script test/GrowthProbe.cs
///
/// Exit code is the verdict. The run director is stopped so the only enemies on
/// the field are the ones a stage put there.
public partial class GrowthProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private RunGrowth? _growth;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // A fixed layout, set before the scene enters the tree because the
        // generator runs in _Ready. Without it every run of this script would
        // face a different map, and a number that changes for reasons the test
        // did not choose is not a measurement.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Player>("Player");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
            _growth = scene.GetNodeOrNull<RunGrowth>("RunGrowth");

            if (_horde == null || _player == null || _weapons == null || _growth == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null} " +
                             $"weapons={_weapons != null} growth={_growth != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            if (!CheckCurves())
            {
                Quit(1);
                return true;
            }
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageStartLevel, "start level = practice (halved) + gear");
            case 1: return RunStage(StageArmour, "armour is flat, with a floor, and caps out");
            case 2: return RunStage(StageClimbToCeiling, "kills climb to the ceiling and stop");
            case 3: return RunStage(StageDeckEmpties, "a fully capped deck drops the pick");
            case 4: return RunStage(StagePracticeIsSeparate, "practice does not move during a run");
            case 5: return RunStage(StageRunsCommit, "a run commits to a line rather than collecting fragments");
            case 6: return RunStage(StageGearShapesTheDeck, "what you wore decides what you are offered");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// Pure data. Every curve has to stop at MaxLevel, including the new damage
    /// one — a single uncapped curve is the whole ceiling gone.
    private bool CheckCurves()
    {
        var rifle = GD.Load<WeaponResource>("res://resources/weapons/scavenged_rifle.tres");
        var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres");
        if (rifle == null || axe == null)
        {
            GD.PushError("PROBE FAILED — weapon resources missing; run BuildWeapons.cs");
            return false;
        }

        int cap = axe.MaxLevel;

        // Past the ceiling nothing may move. Before it, damage must.
        bool damageGrows = axe.GetEffectiveDamage(cap) > axe.GetEffectiveDamage(0);
        bool damageCaps = Mathf.IsEqualApprox(axe.GetEffectiveDamage(cap + 50), axe.GetEffectiveDamage(cap));
        bool rangeCaps = Mathf.IsEqualApprox(axe.GetEffectiveRange(cap + 50), axe.GetEffectiveRange(cap));
        bool delayCaps = Mathf.IsEqualApprox(axe.GetEffectiveAttackDelay(cap + 50), axe.GetEffectiveAttackDelay(cap));
        bool spreadCaps = Mathf.IsEqualApprox(rifle.GetEffectiveSpreadDegrees(rifle.MaxLevel + 50),
                                              rifle.GetEffectiveSpreadDegrees(rifle.MaxLevel));
        bool speedCaps = Mathf.IsEqualApprox(axe.GetEffectiveProjectileSpeed(cap + 50),
                                             axe.GetEffectiveProjectileSpeed(cap));

        GD.Print($"axe damage {axe.GetEffectiveDamage(0):F1} -> {axe.GetEffectiveDamage(cap):F1} " +
                 $"at ceiling {cap}, unchanged at {cap + 50}: {damageCaps}");

        bool ok = damageGrows && damageCaps && rangeCaps && delayCaps && spreadCaps && speedCaps;
        if (!ok)
        {
            GD.PushError($"PROBE FAILED — curves: grows={damageGrows} damage={damageCaps} " +
                         $"range={rangeCaps} delay={delayCaps} spread={spreadCaps} speed={speedCaps}");
        }
        else
        {
            GD.Print("every curve stops at the ceiling: ok");
        }

        return ok;
    }

    /// Practice counts for at most half the ceiling; gear adds on top. The half
    /// is what guarantees a veteran still has somewhere to climb.
    private bool? StageStartLevel(int tick)
    {
        WeaponResource weapon = _weapons!.Weapon!;
        int half = weapon.MaxLevel / 2;

        _weapons.SetProficiency(weapon.Category, 0);
        int atZero = _weapons.StartLevel;

        _weapons.SetProficiency(weapon.Category, half);
        int atHalf = _weapons.StartLevel;

        // Far past the half: the surplus is unspent, not lost — a weapon with a
        // higher ceiling would let more of the same practice count.
        _weapons.SetProficiency(weapon.Category, half + 20);
        int atSurplus = _weapons.StartLevel;

        bool climbRemains = atSurplus < weapon.MaxLevel;

        GD.Print($"  practice 0 -> start {atZero}, {half} -> {atHalf}, {half + 20} -> {atSurplus} " +
                 $"(ceiling {weapon.MaxLevel}, tier bonus {weapon.TierStartBonus})");

        _weapons.SetProficiency(weapon.Category, 0);

        return atZero == weapon.TierStartBonus
            && atHalf == half + weapon.TierStartBonus
            && atSurplus == atHalf
            && climbRemains;
    }

    /// Flat subtraction, not a percentage, and never below a fifth. Armour is
    /// the answer to a crowd and not to a brute.
    ///
    /// Runs first, and narrows the deck to armour and the weapon so that every
    /// offer contains something this probe wants. A stage that discards an
    /// unwanted card still spends it — the offer is fixed until it is answered,
    /// which is the design, so the test has to live with it too.
    private bool? StageArmour(int tick)
    {
        const int ArmourCap = 3;

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _growth!.SetCaps(health: 0, armour: ArmourCap, speed: 0, search: 0,
                             rules: new System.Collections.Generic.Dictionary<GrowthOption, int>());
            return null;
        }

        // Drive to the cap by *picks*, which is the unit the cap is in.
        //
        // This used to loop on `_player.Armour < ArmourCap`, and the two are only
        // the same number while the player starts a run with no armour. They stop
        // being the same the moment a starting loadout carries a piece — which it
        // now does. One point of gear armour meant two picks reached three points,
        // the loop exited, and the deck was correctly still offering the third
        // pick. The probe called that a capping failure. It was reading a pick
        // ceiling off a health-mitigation number.
        float startingArmour = _player!.Armour;
        int guard = 0;
        while (_growth!.IsAvailable(GrowthOption.Armour) && guard++ < 40)
        {
            EarnOnePick();
            TakeIfOffered(GrowthOption.Armour);
        }

        float armour = _player!.Armour;
        int taken = _growth!.TakenCount(GrowthOption.Armour);
        bool cappedOut = !_growth.IsAvailable(GrowthOption.Armour);

        _player.Heal(9999.0f);
        float before = _player.Health;
        _player.TakeDamage(50.0f);
        float bigHit = before - _player.Health;

        _player.Heal(9999.0f);
        before = _player.Health;
        _player.TakeDamage(armour * 0.5f);
        float smallHit = before - _player.Health;

        GD.Print($"  {taken}/{ArmourCap} picks took armour {startingArmour:F0} -> {armour:F0} " +
                 $"(still offered: {!cappedOut}); " +
                 $"50 damage -> {bigHit:F2}, {armour * 0.5f:F2} damage -> {smallHit:F3} " +
                 $"(a fifth always gets through)");

        _player.Heal(9999.0f);

        // Absolute tolerance, not IsEqualApprox: these are differences of two
        // health values near 100, so the relative epsilon of a 0.3 result is
        // finer than the float subtraction that produced it.
        // Every pick landed, the deck closed, and the points arrived — three
        // separate claims that the old single comparison collapsed into one.
        return taken == ArmourCap
            && Mathf.IsEqualApprox(armour, startingArmour + ArmourCap * _growth.ArmourPerPick)
            && cappedOut
            && Mathf.Abs(bigHit - (50.0f - armour)) < 0.01f
            && Mathf.Abs(smallHit - armour * 0.5f * 0.2f) < 0.01f;
    }

    /// The real path: kills earn picks, picks raise the weapon, and the climb
    /// stops exactly at the ceiling however many more arrive.
    private bool? StageClimbToCeiling(int tick)
    {
        WeaponResource weapon = _weapons!.Weapon!;
        int ceiling = weapon.MaxLevel;

        // Armour is already capped by the previous stage and everything else is
        // capped at zero, so the deck now holds the weapon alone.
        int guard = 0;
        while (_weapons.Level < ceiling && guard++ < 60)
        {
            EarnOnePick();
            TakeIfOffered(GrowthOption.WeaponLevel);
        }

        int atCeiling = _weapons.Level;
        float damageAtCeiling = weapon.GetEffectiveDamage(_weapons.Level);
        bool weaponOffered = _growth!.IsAvailable(GrowthOption.WeaponLevel);

        // Four more picks past the top. Nothing may move.
        for (int i = 0; i < 4; i++)
        {
            EarnOnePick();
            TakeIfOffered(GrowthOption.WeaponLevel);
        }

        GD.Print($"  {weapon.WeaponName} climbed to {atCeiling}/{ceiling} in {guard} picks, " +
                 $"still {_weapons.Level} after 4 more; damage {weapon.BaseDamage:F1} -> " +
                 $"{damageAtCeiling:F1}; weapon still offered = {weaponOffered}");

        return atCeiling == ceiling
            && _weapons.Level == ceiling
            && _weapons.AtCeiling
            && !weaponOffered;
    }

    /// With every option at its cap the deck has nothing to deal, and the pick
    /// is dropped rather than left pending against an offer that can never come.
    private bool? StageDeckEmpties(int tick)
    {
        // Exhaust the whole deck, not the first five. The pool grew from five
        // options to eighteen in Phase 18, and capping only the originals left
        // thirteen rules still on offer — so this stage started reporting a
        // working drop as broken, which is a probe that hardcoded the size of
        // the thing it was testing.
        foreach (GrowthOption option in System.Enum.GetValues<GrowthOption>())
        {
            for (int i = 0; i < 40 && _growth!.IsAvailable(option); i++)
                _growth.GrantForTesting(option);
        }

        // Take whatever is already on the table. An offer built by an earlier
        // stage is still standing, and BuildOffer only runs when there is none —
        // so the stale cards made the drop look like it never happened.
        while (_growth!.HasOffer)
            _growth.Choose(0);

        bool anyAvailable = false;
        foreach (GrowthOption option in System.Enum.GetValues<GrowthOption>())
            anyAvailable |= _growth.IsAvailable(option);

        EarnOnePick();
        _growth!._PhysicsProcess(0.0);
        bool dropped = !_growth.HasOffer && _growth.PendingPicks == 0;

        GD.Print($"  anything left to offer = {anyAvailable}; pick dropped = {dropped}");

        return !anyAvailable && dropped;
    }

    /// Practice is banked once by the meta layer, not levelled as it lands. Two
    /// curves moving at once are two curves nobody can tell apart or balance.
    private bool? StagePracticeIsSeparate(int tick)
    {
        WeaponCategory category = _weapons!.Weapon!.Category;

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _weapons.SetProficiency(category, 2);
            _player!.GlobalPosition = new Vector3(-30.0f, 0.0f, 30.0f);
            return null;
        }

        // Feed the weapon a steady supply so it lands real hits through the
        // real firing path.
        if (_horde!.Pool.Count < 20)
        {
            for (int i = 0; i < 20; i++)
                _horde.Spawn(_player!.GlobalPosition + new Vector3(3.0f + i * 0.3f, 0.0f, 0.0f));
        }

        if (tick < 180)
            return null;

        int hits = _weapons.HitsThisRun(category);
        int gain = _weapons.ProficiencyGain(category);
        int proficiencyNow = _weapons.GetProficiency(category);

        GD.Print($"  {hits} hits this run -> practice would bank +{gain}, " +
                 $"live proficiency still {proficiencyNow}");

        // Hits landed, the level did not move, and a single run can never bank
        // more than the per-run cap however long it goes.
        return hits > 0 && proficiencyNow == 2 && gain <= 3;
    }

    /// Kills walkers until one more pick is pending. The real path — the growth
    /// node only ever hears about kills.
    private void EarnOnePick()
    {
        int target = _growth!.PendingPicks + 1;
        int guard = 0;

        while (_growth.PendingPicks < target && guard++ < 200)
        {
            var far = new Vector3(-45.0f, 0.0f, 45.0f);
            int spawned = 0;
            while (spawned < 64 && _horde!.Spawn(far))
                spawned++;

            for (int i = _horde!.Pool.Count - 1; i >= 0; i--)
                _horde.Damage(i, 9999.0f, Vector2.Zero);
        }
    }

    private bool TakeIfOffered(GrowthOption wanted)
    {
        // Touch the offer the way the game does: one physics step builds it.
        _growth!._PhysicsProcess(0.0);

        for (int i = 0; i < _growth.Offer.Length; i++)
        {
            if (_growth.Offer[i] == wanted)
                return _growth.Choose(i);
        }

        // Not on this deal — discard it so the next pick draws a fresh hand.
        if (_growth.HasOffer)
            _growth.Choose(0);

        return false;
    }

    /// A run ends up somewhere rather than everywhere, and still has a choice
    /// while it gets there.
    ///
    /// **The deck had twenty-two options and no shape.** An ordinary run lasts 83
    /// to 142 seconds and buys a handful of picks, so drawing uniformly hands the
    /// player fragments of five builds and lets them finish none — which is most
    /// of why two runs felt the same. `RunGrowth.LineOf` groups the cards and a
    /// pick tilts the deck toward its own line.
    ///
    /// Two things have to hold and they pull against each other, so both are
    /// measured:
    ///
    ///   **The deck tilts.** Compared against the same simulation with the
    ///   affinity switched off, which is the only honest control — an absolute
    ///   threshold would be a number chosen to make the test pass.
    ///
    ///   **The offer still contains a choice.** This is the one that matters to
    ///   the player and the first version of this stage did not measure it. It
    ///   measured how concentrated a run ended up, which a *greedy* simulated
    ///   player produces on its own: the control came out at 71% with the
    ///   affinity off entirely. Concentration was never evidence of anything.
    ///   Whether three cards on the table are all from one line is.
    private bool? StageRunsCommit(int tick)
    {
        const int Runs = 30;
        const int Picks = 7;

        (float tilted, float choiceOn) = Play(Runs, Picks, _growth!.LineAffinity);
        (float flat, _) = Play(Runs, Picks, 0.0f);

        GD.Print($"  {Picks} picks over {Runs} runs: strongest line holds {tilted * 100.0f:F0}% "
               + $"against {flat * 100.0f:F0}% with no affinity; "
               + $"{choiceOn * 100.0f:F0}% of offers held more than one line");

        bool ok = true;

        if (tilted < flat * 1.1f)
        {
            GD.PushError($"  affinity moved concentration from {flat * 100.0f:F0}% to "
                       + $"{tilted * 100.0f:F0}% — the deck is not tilting");
            ok = false;
        }

        // Three cards from one line is a card the player is told to take rather
        // than one they choose. Some of those are fine and inevitable — the deck
        // is only twenty-two entries and five lines — but most offers have to be
        // a decision or the growth layer is decoration again, which is exactly
        // what it was before the lines existed.
        if (choiceOn < 0.7f)
        {
            GD.PushError($"  only {choiceOn * 100.0f:F0}% of offers held more than one line — "
                       + "the deck has collapsed and the player is being told what to take");
            ok = false;
        }

        return ok;
    }

    /// Plays `runs` runs of `picks`, always continuing whatever the run has most
    /// of, and reports how concentrated the result was and how often the offer
    /// held a real choice.
    ///
    /// Through `RunGrowth`'s own offer and take path rather than a
    /// reimplementation of the draw — a test that models the thing it is testing
    /// passes when the model is right and the code is wrong.
    private (float Concentration, float Choice) Play(int runs, int picks, float affinity)
    {
        float saved = _growth!.LineAffinity;
        _growth.LineAffinity = affinity;

        float concentration = 0.0f;
        int offers = 0;
        int withChoice = 0;

        for (int r = 0; r < runs; r++)
        {
            _growth.ResetForTesting();

            for (int i = 0; i < picks; i++)
            {
                GrowthOption[] offer = _growth.PeekOffer();
                if (offer.Length == 0)
                    break;

                var lines = new System.Collections.Generic.HashSet<GrowthLine>();
                foreach (GrowthOption option in offer)
                    lines.Add(RunGrowth.LineOf(option));

                offers++;
                if (lines.Count > 1)
                    withChoice++;

                // The greedy player: take whatever continues what this run
                // already has. A player picking at random would measure the
                // deck's shuffle rather than its tilt.
                int best = 0;
                int mostSoFar = -1;

                for (int n = 0; n < offer.Length; n++)
                {
                    GrowthLine line = RunGrowth.LineOf(offer[n]);
                    int held = line == GrowthLine.None ? 0 : _growth.TakenInLine(line);

                    if (held > mostSoFar)
                    {
                        mostSoFar = held;
                        best = n;
                    }
                }

                _growth.GrantForTesting(offer[best]);
            }

            int strongest = 0;
            int spent = 0;

            foreach (GrowthLine line in System.Enum.GetValues<GrowthLine>())
            {
                if (line == GrowthLine.None)
                    continue;

                int taken = _growth.TakenInLine(line);
                spent += taken;
                strongest = Mathf.Max(strongest, taken);
            }

            if (spent > 0)
                concentration += strongest / (float)spent;
        }

        _growth.LineAffinity = saved;

        return (runs > 0 ? concentration / runs : 0.0f,
                offers > 0 ? withChoice / (float)offers : 0.0f);
    }

    /// A loadout changes what the run offers, not only what it starts with.
    ///
    /// **This is the meta layer's whole problem, stated as a test.** An
    /// assessment asked whether anything the player earns between runs changes
    /// how a run is *played* as opposed to making the same run easier, and for
    /// the armour, backpacks and boots the answer was no — they were numerical
    /// efficiency trades. `GearResource.Favours` names a growth line, and the
    /// deck reads it in the same currency as a pick.
    ///
    /// **Measured as a share of the cards offered, not as "did an offer contain
    /// one".** The first version asked the second question and very nearly failed
    /// a working feature: Ward holds six of twenty-two cards and an offer is
    /// three, so roughly seven offers in ten contain a Ward card *by chance*.
    /// Against a baseline that high there is no room left to move, and a
    /// multiplicative threshold on a number near its own ceiling is a test that
    /// fails for arithmetic reasons rather than for real ones.
    private bool? StageGearShapesTheDeck(int tick)
    {
        const int Offers = 400;

        float bare = ShareOfCards(GrowthLine.Ward, Offers, 0.0f);
        float worn = ShareOfCards(GrowthLine.Ward, Offers, 2.0f);

        GD.Print($"  Ward is {bare * 100.0f:F0}% of cards offered with no gear, "
               + $"{worn * 100.0f:F0}% wearing two Ward pieces");

        bool ok = true;

        // Two pieces are worth two picks, and the stage above showed the deck
        // responding to picks. If this does not move, the gear field is
        // decoration.
        if (worn < bare * 1.25f)
        {
            GD.PushError($"  two Ward pieces moved Ward from {bare * 100.0f:F0}% to "
                       + $"{worn * 100.0f:F0}% of cards — the loadout is not shaping the deck");
            ok = false;
        }

        // And still a tilt rather than a lock. A loadout that guaranteed its own
        // line would make the run's own picks meaningless, which is the failure
        // this change is trying to avoid rather than cause.
        if (worn > 0.8f)
        {
            GD.PushError($"  two pieces made Ward {worn * 100.0f:F0}% of every offer — "
                       + "the loadout has locked the deck rather than leaning it");
            ok = false;
        }

        _growth!.ClearFavour();
        return ok;
    }

    /// What share of the cards across `offers` fresh offers belong to `line`,
    /// with `favour` worth of gear leaning that way.
    ///
    /// The deck is reset between offers so every draw sees the same empty run —
    /// otherwise this would measure the pick affinity from the stage above rather
    /// than the gear.
    private float ShareOfCards(GrowthLine line, int offers, float favour)
    {
        int cards = 0;
        int hits = 0;

        for (int i = 0; i < offers; i++)
        {
            _growth!.ResetForTesting();
            _growth.ClearFavour();

            if (favour > 0.0f)
                _growth.FavourLine(line, favour);

            foreach (GrowthOption option in _growth.PeekOffer())
            {
                cards++;
                if (RunGrowth.LineOf(option) == line)
                    hits++;
            }
        }

        return cards > 0 ? hits / (float)cards : 0.0f;
    }
}
