using Godot;

/// Checks that every in-run upgrade does the thing its card claims.
///
///   godot --headless --script test/ModifierProbe.cs
///
/// Exit code is the verdict. The pool went from five options to seventeen, and
/// twelve of the new ones are rules rather than numbers — read by the weapon, the
/// horde or the loot container at the point of use rather than added to a stat.
/// A rule that is granted and never read is the quietest possible failure: the
/// card appears, the player takes it, and nothing whatsoever happens.
///
/// So this asserts effects, not plumbing: fire something and measure, rather than
/// check that a field went up.
public partial class ModifierProbe : SceneTree
{
    private Node? _scene;
    private Horde? _horde;
    private Player? _player;
    private RunGrowth? _growth;
    private WeaponHandler? _weapons;

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
        if (_stage == 0 && _stageTick == 0)
        {
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _growth = _scene?.GetNodeOrNull<RunGrowth>("RunGrowth");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

            if (_horde == null || _player == null || _growth == null || _weapons == null)
            {
                GD.PushError("PROBE FAILED — scene is missing a required node");
                Quit(1);
                return true;
            }

            _scene?.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _weapons.SetPhysicsProcess(false);
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageEveryOptionApplies, "every option changes something");
            case 1: return RunStage(StagePierce, "pierce reaches further down a line");
            case 2: return RunStage(StageArea, "area widens a swing");
            case 3: return RunStage(StageOnKill, "ignite and detonate fire on a kill");
            case 4: return RunStage(StageThornsAndLifesteal, "thorns bites back, lifesteal pays back");
            case 5: return RunStage(StageDeckEmpties, "a capped option leaves the deck");
            case 6: return RunStage(StageRarity, "rare cards are rarer than common ones");
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

    /// The blanket check: granting any option has to leave the run different
    /// from how it was. It cannot say the change was the *right* one — the
    /// stages below do that for the ones with a measurable effect — but it does
    /// catch the case that matters most, which is a card wired to nothing.
    private bool? StageEveryOptionApplies(int tick)
    {
        bool ok = true;
        var missed = new System.Collections.Generic.List<string>();

        foreach (GrowthOption option in System.Enum.GetValues<GrowthOption>())
        {
            string before = Fingerprint();
            Grant(option);
            string after = Fingerprint();

            if (before == after)
            {
                missed.Add(option.ToString());
                ok = false;
            }
        }

        GD.Print($"  {System.Enum.GetValues<GrowthOption>().Length} options; " +
                 $"{(missed.Count == 0 ? "all changed the run" : "no effect: " + string.Join(", ", missed))}");

        Reset();
        return ok;
    }

    private bool? StagePierce(int tick)
    {
        if (tick == 1)
        {
            Reset();
            Line(count: 4);
            return null;
        }

        if (tick == 2)
        {
            _weapons!.ForceFire(new Vector2(1.0f, 0.0f));
            _withoutPierce = 4 - _horde!.Pool.Count;

            Reset();
            Line(count: 4);
            _player!.Mods.Pierce = 3;
            return null;
        }

        if (tick < 4)
            return null;

        _weapons!.ForceFire(new Vector2(1.0f, 0.0f));
        int withPierce = 4 - _horde!.Pool.Count;

        GD.Print($"  a line of 4: {_withoutPierce} hit without pierce, {withPierce} with +3");
        Reset();
        return withPierce > _withoutPierce;
    }

    private int _withoutPierce;

    private bool? StageArea(int tick)
    {
        // A melee arc, and a target just outside its normal reach.
        var axe = GD.Load<WeaponResource>("res://resources/weapons/fire_axe.tres");
        if (axe == null)
            return false;

        if (tick == 1)
        {
            Reset();
            _weapons!.Equip(0, axe);
            return null;
        }

        if (tick == 2)
        {
            float reach = axe.GetEffectiveRange(_weapons!.Level);
            _horde!.Pool.Clear();
            _horde.Spawn(_player!.GlobalPosition + new Vector3(reach * 1.12f, 0.0f, 0.0f), 0);
            _weapons.ForceFire(new Vector2(1.0f, 0.0f));
            _outsideReach = _horde.Pool.Count;   // 1 means the swing missed

            _horde.Pool.Clear();
            _horde.Spawn(_player.GlobalPosition + new Vector3(reach * 1.12f, 0.0f, 0.0f), 0);
            _player.Mods.AreaScale = 1.6f;
            return null;
        }

        if (tick < 4)
            return null;

        _weapons!.ForceFire(new Vector2(1.0f, 0.0f));
        int withArea = _horde!.Pool.Count;

        GD.Print($"  a target 12% past reach: survivors {_outsideReach} normally, {withArea} with +60% area");
        Reset();
        return _outsideReach == 1 && withArea == 0;
    }

    private int _outsideReach;

    private bool? StageOnKill(int tick)
    {
        if (tick == 1)
        {
            Reset();
            _horde!.Hazards.Clear();

            // Certainty rather than a sample: a probe that rolls a 10% chance is
            // a probe that fails once every ten runs for no reason.
            _player!.Mods.IgniteChance = 1.0f;
            _horde.Spawn(_player.GlobalPosition + new Vector3(20.0f, 0.0f, 0.0f), 0);
            _horde.Damage(_horde.Pool.Count - 1, 9999.0f, Vector2.Zero);
            _fires = _horde.Hazards.Count;

            Reset();
            _player.Mods.DetonateChance = 1.0f;

            // A victim and a bystander inside the blast.
            _horde.Spawn(_player.GlobalPosition + new Vector3(24.0f, 0.0f, 0.0f), 0);
            _horde.Spawn(_player.GlobalPosition + new Vector3(25.0f, 0.0f, 0.0f), 0);
            return null;
        }

        if (tick < 3)
            return null;

        int before = _horde!.Pool.Count;
        _horde.Damage(before - 1, 9999.0f, Vector2.Zero);
        int after = _horde.Pool.Count;

        GD.Print($"  ignite left {_fires} fire; detonate took {before - after} of {before} " +
                 "(the kill plus whoever was standing next to it)");

        Reset();
        _horde.Hazards.Clear();
        return _fires == 1 && before - after == 2;
    }

    private int _fires;

    private bool? StageThornsAndLifesteal(int tick)
    {
        if (tick == 1)
        {
            Reset();
            _horde!.Pool.Clear();
            _player!.Heal(9999.0f);

            // Standing on the player, so contact is certain.
            _horde.Spawn(_player.GlobalPosition, 0);
            _player.Mods.Thorns = 400.0f;
            return null;
        }

        if (tick < 20)
            return null;

        int leftAfterThorns = _horde!.Pool.Count;

        // Lifesteal, measured from a wound rather than from full health, or the
        // heal would land on a full bar and read as nothing.
        Reset();
        _horde.Pool.Clear();
        _player!.Heal(9999.0f);
        _player.TakeDamage(40.0f);
        float wounded = _player.Health;

        _player.Mods.Lifesteal = 7.0f;
        _horde.Spawn(_player.GlobalPosition + new Vector3(20.0f, 0.0f, 0.0f), 0);
        _horde.Damage(_horde.Pool.Count - 1, 9999.0f, Vector2.Zero);

        float healed = _player.Health - wounded;
        GD.Print($"  thorns killed the toucher = {leftAfterThorns == 0}; " +
                 $"lifesteal returned {healed:F1} HP on a kill");

        Reset();
        return leftAfterThorns == 0 && healed > 6.0f;
    }

    /// The ceiling is the part the player can see: an option that stops being
    /// offered is a plan they can make. One that keeps appearing after its cap
    /// is a card that does nothing when taken.
    private bool? StageDeckEmpties(int tick)
    {
        const GrowthOption subject = GrowthOption.Pierce;

        bool availableAtStart = _growth!.IsAvailable(subject);
        int taken = 0;

        for (int i = 0; i < 20 && _growth.IsAvailable(subject); i++)
        {
            Grant(subject);
            taken++;
        }

        bool goneAfterCap = !_growth.IsAvailable(subject);

        GD.Print($"  {subject} available at start = {availableAtStart}, " +
                 $"taken {taken} times, still offered = {!goneAfterCap}");

        Reset();
        return availableAtStart && goneAfterCap && taken is > 0 and < 20;
    }

    /// Rarity has to be visible in the draw, or it is a label. Counted over
    /// enough offers that the difference cannot be luck.
    private bool? StageRarity(int tick)
    {
        Reset();

        int rare = 0, common = 0;
        for (int i = 0; i < 400; i++)
        {
            foreach (GrowthOption option in _growth!.PeekOffer())
            {
                if (RunGrowth.RarityOf(option) == GrowthRarity.Rare)
                    rare++;
                else
                    common++;
            }
        }

        // Six of seventeen options are rare at roughly a third of the weight, so
        // the expected share is around fifteen percent. The assertion is loose on
        // purpose: it is checking that the weighting exists, not its exact value.
        float share = rare / (float)Mathf.Max(1, rare + common);
        GD.Print($"  400 offers: {rare} rare, {common} common ({share:P0} rare)");

        return share is > 0.02f and < 0.35f;
    }

    // ---- helpers -------------------------------------------------------------

    private void Grant(GrowthOption option) => _growth!.GrantForTesting(option);

    private void Reset()
    {
        _player!.Mods.Reset();
        _horde!.Pool.Clear();
    }

    /// A line of enemies straight along +X, close enough together that one shot
    /// crosses all of them.
    private void Line(int count)
    {
        _horde!.Pool.Clear();
        for (int i = 0; i < count; i++)
            _horde.Spawn(_player!.GlobalPosition + new Vector3(2.0f + i * 1.2f, 0.0f, 0.0f), 0);
    }

    /// Everything an upgrade could have moved, as one string. Cheap, and it does
    /// not need to know which field any given card touches.
    /// Everything a card could move, as one string.
    ///
    /// The modifier fields come from reflection, not from a list. This method
    /// used to name all eighteen by hand — and four new ones were added, wired up
    /// correctly, and reported as changing nothing, because the *fingerprint* had
    /// not been updated. The stage exists to catch a card wired to nothing and it
    /// failed by being wired to nothing itself.
    ///
    /// That is the same hole `RunModifiers.Reset` had on the same day. Any place
    /// that enumerates the fields of a growing type by hand will eventually be
    /// out of date, and a test written that way goes stale in exactly the
    /// direction that hides the bug.
    private string Fingerprint()
    {
        var text = new System.Text.StringBuilder();

        // The four the player owns rather than the modifier block.
        text.Append($"{_player!.MaxHealth:F2}|{_player.Armour:F2}|");
        text.Append($"{_player.MoveSpeed:F3}|{_player.SearchSpeed:F3}|{_weapons!.RunUpgrades}|");

        RunModifiers m = _player.Mods;
        foreach (System.Reflection.FieldInfo field in typeof(RunModifiers).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            text.Append(field.GetValue(m)).Append('|');
        }

        return text.ToString();
    }
}
