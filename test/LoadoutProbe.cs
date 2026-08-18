using Godot;

/// Checks that what you leave the base wearing decides what kind of run it is.
///
///   godot --headless --script test/LoadoutProbe.cs
///
/// Exit code is the verdict. The claim under test is not "the expensive set is
/// better" — that claim is trivially true of any shop and is exactly what the
/// old gear table said, which is why the old gear table was a budget screen. The
/// claim is that two sets at the same price are answers to different questions:
/// each one has to be measurably good at something *and measurably bad at
/// something else*, and the deck each one permits has to differ.
///
/// A sidegrade that is secretly an upgrade passes every other test in this suite.
public partial class LoadoutProbe : SceneTree
{
    private Node? _scene;
    private Player? _player;
    private RunGrowth? _growth;
    private MetaManager? _meta;

    private int _stage;
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

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0)
        {
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _growth = _scene?.GetNodeOrNull<RunGrowth>("RunGrowth");
            _meta = _scene?.GetNodeOrNull<MetaManager>("MetaManager");

            if (_player == null || _growth == null || _meta == null)
            {
                GD.PushError("PROBE FAILED - scene is missing a required node");
                Quit(1);
                return true;
            }

            _scene?.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
        }

        switch (_stage)
        {
            case 0: return RunStage(StageNoSlotHasABestPiece, "no slot has a piece that beats its neighbour everywhere");
            case 1: return RunStage(StageGearGrantsRules, "a piece's rule is live before the first level-up");
            case 2: return RunStage(StageGearShapesTheDeck, "two sets permit two different decks");
            case 3: return RunStage(StageStartingKitIsNeutral, "three pieces granting nothing grant nothing");
            case 4: return RunStage(StageTierGateOpens, "a tier is shut, says how many runs, and opens on time");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<bool> stage, string label)
    {
        bool verdict = stage();
        GD.Print($"{label}: {(verdict ? "ok" : "FAILED")}");
        _failed |= !verdict;
        _stage++;
        return false;
    }

    /// The stage this phase exists for.
    ///
    /// For each slot, the two tier-2 pieces are compared across every axis they
    /// can touch. If one of them is at least as good everywhere, the slot has a
    /// correct answer and the choice is decoration — so the assertion is that
    /// each piece wins somewhere and loses somewhere.
    private bool StageNoSlotHasABestPiece()
    {
        (string A, string B)[] pairs =
        {
            ("plate_carrier", "stitched_vest"),
            ("trekking_pack", "bandolier"),
            ("running_shoes", "tread_boots"),
        };

        bool ok = true;

        foreach ((string a, string b) in pairs)
        {
            GearResource? left = Load(a);
            GearResource? right = Load(b);
            if (left == null || right == null)
            {
                ok = false;
                continue;
            }

            float[] mine = Axes(left);
            float[] theirs = Axes(right);

            bool leftWinsSomewhere = false, rightWinsSomewhere = false;
            for (int i = 0; i < mine.Length; i++)
            {
                leftWinsSomewhere |= mine[i] > theirs[i];
                rightWinsSomewhere |= theirs[i] > mine[i];
            }

            if (!leftWinsSomewhere || !rightWinsSomewhere)
            {
                GD.PushError($"  {a} vs {b}: one of them is at least as good everywhere");
                ok = false;
            }
        }

        GD.Print($"  {pairs.Length} slots, each a trade rather than a tier");
        return ok;
    }

    /// Everything a piece can move, in one array so the comparison is exhaustive
    /// rather than a list somebody remembered to keep up to date. A cap of -1
    /// reads as "no opinion", which must not count as a win.
    private static float[] Axes(GearResource g) => new[]
    {
        g.HealthBonus, g.ArmourBonus, g.MoveSpeedBonus, g.CarryBonus, g.SafeBoxBonus,
        g.PierceBonus, g.AreaBonus, g.ThornsBonus, g.RegenBonus, g.KnockbackBonus, g.DodgeBonus,
        g.HealthUpgradeCap, g.ArmourUpgradeCap, g.SpeedUpgradeCap, g.SearchUpgradeCap,
        Mathf.Max(0, g.PierceUpgradeCap), Mathf.Max(0, g.CritUpgradeCap),
        Mathf.Max(0, g.AreaUpgradeCap), Mathf.Max(0, g.ThornsUpgradeCap),
        Mathf.Max(0, g.RegenUpgradeCap), Mathf.Max(0, g.KnockbackUpgradeCap),
        Mathf.Max(0, g.DodgeUpgradeCap), Mathf.Max(0, g.FortuneUpgradeCap),
    };

    /// A granted rule that only exists after the first upgrade is a piece of
    /// equipment that appears to do nothing for the first ninety seconds, which
    /// is most of what a player would judge it on.
    private bool StageGearGrantsRules()
    {
        Wear("stitched_vest", "bandolier", "tread_boots");

        RunModifiers mods = _player!.Mods;
        GD.Print($"  vest + bandolier + tread boots: pierce {mods.Pierce}, thorns {mods.Thorns:F2}, " +
                 $"regen {mods.Regen:F2}, knockback {mods.Knockback:F2}, area x{mods.AreaScale:F2}");

        return mods.Pierce >= 1
               && mods.Thorns > 0.0f
               && mods.Regen > 0.0f
               && mods.Knockback > 0.0f
               && mods.AreaScale > 1.0f;
    }

    /// Two sets, two ceilings. The point is not that one deck is bigger — it is
    /// that the option each set is built around is the option the other set
    /// cannot stack.
    private bool StageGearShapesTheDeck()
    {
        Wear("plate_carrier", "bandolier", "running_shoes");
        int gunPierce = CapOf(GrowthOption.Pierce);
        int gunThorns = CapOf(GrowthOption.Thorns);
        int gunSpeed = CapOf(GrowthOption.MoveSpeed);

        Wear("stitched_vest", "trekking_pack", "tread_boots");
        int standPierce = CapOf(GrowthOption.Pierce);
        int standThorns = CapOf(GrowthOption.Thorns);
        int standSpeed = CapOf(GrowthOption.MoveSpeed);

        GD.Print($"  carrier/bandolier/shoes: pierce {gunPierce} thorns {gunThorns} speed {gunSpeed}");
        GD.Print($"  vest/trekking/tread:     pierce {standPierce} thorns {standThorns} speed {standSpeed}");

        return gunPierce > standPierce && standThorns > gunThorns && gunSpeed > standSpeed;
    }

    /// The neutral-value trap, asserted rather than remembered.
    ///
    /// AreaScale is a multiplier neutral at 1 while every other rule is neutral
    /// at 0, so an accumulator that treats them alike gives a player in the
    /// starting kit a triple-size blast radius. It was written that way first.
    private bool StageStartingKitIsNeutral()
    {
        Wear("worn_jacket", "canvas_pack", "scuffed_boots");

        RunModifiers mods = _player!.Mods;
        GD.Print($"  starting kit: area x{mods.AreaScale:F2}, pierce {mods.Pierce}, " +
                 $"thorns {mods.Thorns:F2}, regen {mods.Regen:F2}, dodge {mods.Dodge:F2}");

        return Mathf.IsEqualApprox(mods.AreaScale, 1.0f)
               && mods.Pierce == 0 && mods.Thorns == 0.0f
               && mods.Regen == 0.0f && mods.Dodge == 0.0f;
    }

    private bool StageTierGateOpens()
    {
        var profile = new Profile();
        const string tier2 = "res://resources/gear/running_shoes.tres";

        string? shut = UnlockBook.ShopLockReason(profile, tier2, 2);
        bool tier1Free = UnlockBook.ShopLockReason(profile, "res://resources/gear/worn_jacket.tres", 1) == null;

        profile.RunsSurvived = UnlockBook.TierOpensAt(2);
        string? open = UnlockBook.ShopLockReason(profile, tier2, 2);

        // Deaths are not progress toward better equipment. A gate counting
        // attempts would pay for exactly the loop the rest of the game spends
        // its time discouraging.
        var loser = new Profile { RunsLost = 50 };
        bool dyingDoesNotHelp = UnlockBook.ShopLockReason(loser, tier2, 2) != null;

        GD.Print($"  at 0 extractions tier 2 says \"{shut}\"; at {UnlockBook.TierOpensAt(2)} it says " +
                 $"\"{open ?? "nothing"}\"; tier 1 free: {tier1Free}; 50 deaths still shut: {dyingDoesNotHelp}");

        return !string.IsNullOrEmpty(shut) && open == null && tier1Free && dyingDoesNotHelp;
    }

    /// The ceiling the offer actually consults, not a number re-derived here. The
    /// gear caps and the defaults live in two arrays and are merged at read time
    /// precisely so scene order cannot change the answer; asking anywhere else
    /// would test a copy of that logic rather than the logic.
    private int CapOf(GrowthOption option) => _growth!.CapFor(option);

    private GearResource? Load(string name)
    {
        var piece = GD.Load<GearResource>($"res://resources/gear/{name}.tres");
        if (piece == null)
            GD.PushError($"  gear {name} did not load — run BuildGear.cs");

        return piece;
    }

    /// Puts a set on and re-applies it from scratch. The player's stats and rules
    /// are additive, so the modifiers are cleared first — otherwise the second
    /// set measured is the first set plus the second.
    private void Wear(string armour, string pack, string boots)
    {
        _meta!.Profile.EquippedGear[0] = $"res://resources/gear/{armour}.tres";
        _meta.Profile.EquippedGear[1] = $"res://resources/gear/{pack}.tres";
        _meta.Profile.EquippedGear[2] = $"res://resources/gear/{boots}.tres";

        foreach (string path in _meta.Profile.EquippedGear)
            _meta.Profile.Grant(path);

        _player!.Mods.Reset();
        _meta.ReapplyGearForTesting();
    }
}
