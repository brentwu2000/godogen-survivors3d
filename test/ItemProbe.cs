using Godot;

/// Checks that carried items are worth something before they are sold, and that
/// running out of ammo changes the answer rather than ending it.
///
///   godot --headless --script test/ItemProbe.cs
///
/// Exit code is the verdict. The run director is stopped so nothing arrives that
/// a stage did not put there.
public partial class ItemProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;
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

        // A fixed layout, set before the scene enters the tree because the
        // generator runs in _Ready. Without it every run of this script would
        // face a different map, and a number that changes for reasons the test
        // did not choose is not a measurement.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

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

            if (_horde == null || _player == null || _weapons == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null} " +
                             $"weapons={_weapons != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageHealCosts, "using a heal costs its sale value");
            case 1: return RunStage(StageWasteNothing, "nothing is spent when it would not help");
            case 2: return RunStage(StageAmmo, "looted rounds refill the reserve, up to a cap");
            case 3: return RunStage(StageAdrenaline, "adrenaline is speed for a price, and expires");
            case 4: return RunStage(StageDryThenSwap, "a dry rifle stops; the sidearm does not");
            case 5: return RunStage(StageSlotsAreSeparate, "each slot keeps its own ammo and levels");
            case 6: return RunStage(StageThrowSeparate, "throwing is its own verb, not the use key");
            case 7: return RunStage(StageExplosive, "a thrown charge clears a radius where it lands");
            case 8: return RunStage(StageIncendiary, "fire keeps burning, then goes out");
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

    /// The use key must not reach for a grenade and the throw key must not reach
    /// for a medkit. One shared "spend something" verb is how a player heals by
    /// blowing themselves a hole in the crowd they were running from.
    private bool? StageThrowSeparate(int tick)
    {
        ItemResource? bomb = Load("pipe_bomb");
        ItemResource? medkit = Load("medkit");
        if (bomb == null || medkit == null)
            return false;

        _horde!.Pool.Clear();
        _player!.Heal(9999.0f);
        _player.Backpack.Clear();
        _player.Backpack.TryAdd(bomb, 1);

        // Hurt, carrying only a bomb: the use key has nothing it may spend.
        _player.TakeDamage(40.0f);
        int usedWithOnlyABomb = _player.TryUseBest();

        _player.Backpack.Clear();
        _player.Backpack.TryAdd(medkit, 1);
        int thrownWithOnlyAMedkit = _player.TryThrow();

        GD.Print($"  use key with only a bomb spent {usedWithOnlyABomb}; " +
                 $"throw key with only a medkit spent {thrownWithOnlyAMedkit}; " +
                 $"bomb usable = {bomb.IsUsable}, supply = {bomb.IsSupply}");

        return usedWithOnlyABomb == 0
            && thrownWithOnlyAMedkit == 0
            && bomb.IsThrowable
            && !bomb.IsSupply;
    }

    private bool? StageExplosive(int tick)
    {
        ItemResource? bomb = Load("pipe_bomb");
        if (bomb == null)
            return false;

        _horde!.Pool.Clear();
        _player!.Heal(9999.0f);
        _player.Backpack.Clear();
        _player.Backpack.TryAdd(bomb, 1);

        // Facing is the last non-zero heading, and the probe never moves the
        // player, so it is whatever the default is — read it rather than assume.
        Vector2 aim = _player.Facing;
        Vector3 landing = _player.GlobalPosition +
                          new Vector3(aim.X, 0.0f, aim.Y) * _player.ThrowRange;

        // Three inside the blast, one well outside it.
        for (int i = 0; i < 3; i++)
            _horde.Spawn(landing + new Vector3(i * 0.8f, 0.0f, 0.0f));

        Vector3 bystanderAt = landing + new Vector3(bomb.EffectRadius + 5.0f, 0.0f, 0.0f);
        _horde.Spawn(bystanderAt);

        int before = _horde.Pool.Count;
        int spent = _player.TryThrow();
        int after = _horde.Pool.Count;

        bool bystanderLived = after == 1 &&
            Mathf.Abs(_horde.Pool.Position[0].X - bystanderAt.X) < 0.5f;

        GD.Print($"  {bomb.ItemName} ({bomb.EffectAmount:F0} in {bomb.EffectRadius:F1}m) for {spent}: " +
                 $"{before} -> {after} enemies, the one outside survived = {bystanderLived}");

        return spent == bomb.Value && before == 4 && after == 1 && bystanderLived;
    }

    private bool? StageIncendiary(int tick)
    {
        ItemResource? molotov = Load("molotov");
        if (molotov == null)
            return false;

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _horde.Hazards.Clear();
            _player!.Heal(9999.0f);
            _player.Backpack.Clear();
            _player.Backpack.TryAdd(molotov, 1);

            Vector2 aim = _player.Facing;
            _fireAt = _player.GlobalPosition + new Vector3(aim.X, 0.0f, aim.Y) * _player.ThrowRange;

            // A brute, because it has to survive long enough to be measured
            // burning rather than dying to the first tick.
            _horde.Spawn(_fireAt, 2);
            _spentOnFire = _player.TryThrow();
            _healthAtLight = _horde.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
            return null;
        }

        // Half a second in: burning, still alive, patch still there.
        if (tick == 30)
        {
            _healthWhileBurning = _horde!.Pool.Count > 0 ? _horde.Pool.Health[0] : 0.0f;
            _patchesWhileBurning = _horde.Hazards.Count;
            return null;
        }

        // Past its duration: the patch is gone.
        if (tick < 60 + (int)(molotov.EffectDuration * 60.0f))
            return null;

        int patchesAfter = _horde!.Hazards.Count;

        GD.Print($"  {molotov.ItemName} for {_spentOnFire}: brute {_healthAtLight:F0} -> " +
                 $"{_healthWhileBurning:F0} HP in 0.5s ({_patchesWhileBurning} patch alight); " +
                 $"after {molotov.EffectDuration:F0}s there are {patchesAfter}");

        return _spentOnFire == molotov.Value
            && _patchesWhileBurning == 1
            && _healthWhileBurning < _healthAtLight
            && patchesAfter == 0;
    }

    private Vector3 _fireAt;
    private int _spentOnFire;
    private float _healthAtLight;
    private float _healthWhileBurning;
    private int _patchesWhileBurning;

    private static ItemResource? Load(string name) =>
        GD.Load<ItemResource>($"res://resources/items/{name}.tres");

    /// The whole point of the system: the backpack holds health and money in the
    /// same slots, so spending one is spending the other.
    private bool? StageHealCosts(int tick)
    {
        ItemResource? medkit = Load("medkit");
        if (medkit == null)
            return false;

        _player!.Backpack.Clear();
        _player.Backpack.TryAdd(medkit, 1);

        int valueBefore = _player.Backpack.TotalValue;
        _player.TakeDamage(60.0f);
        float healthBefore = _player.Health;

        int spent = _player.TryUseBest();
        float healed = _player.Health - healthBefore;
        int valueAfter = _player.Backpack.TotalValue;

        GD.Print($"  medkit: healed {healed:F0}, bag value {valueBefore} -> {valueAfter}, " +
                 $"cost reported {spent}");

        return Mathf.Abs(healed - medkit.EffectAmount) < 0.01f
            && spent == medkit.Value
            && valueBefore - valueAfter == medkit.Value;
    }

    /// Cheapest first, and only when it helps. At full health a backpack full of
    /// medkits is money, not medicine.
    private bool? StageWasteNothing(int tick)
    {
        ItemResource? medkit = Load("medkit");
        ItemResource? food = Load("canned_food");
        ItemResource? serum = Load("antiviral_serum");
        if (medkit == null || food == null || serum == null)
            return false;

        _player!.Heal(9999.0f);
        _player.Backpack.Clear();
        _player.Backpack.TryAdd(medkit, 1);
        _player.Backpack.TryAdd(food, 1);
        _player.Backpack.TryAdd(serum, 1);

        int atFullHealth = _player.TryUseBest();

        // Hurt by less than the cheap heal: the cheap one should still go first.
        _player.TakeDamage(10.0f);
        int whenHurt = _player.TryUseBest();

        bool serumUntouched = false;
        for (int i = 0; i < _player.Backpack.EntryCount; i++)
            serumUntouched |= _player.Backpack.ItemAt(i) == serum;

        GD.Print($"  at full health spent {atFullHealth}; hurt spent {whenHurt} " +
                 $"(food {food.Value}, medkit {medkit.Value}); serum still carried = {serumUntouched}");

        return atFullHealth == 0 && whenHurt == food.Value && serumUntouched;
    }

    private bool? StageAmmo(int tick)
    {
        ItemResource? rounds = Load("rifle_rounds");
        WeaponResource? rifle = _weapons!.Weapon;
        if (rounds == null || rifle == null || rifle.MagazineSize <= 0)
        {
            GD.Print("  expected a magazine-fed weapon in the active slot");
            return false;
        }

        _player!.Heal(9999.0f);
        _player.Backpack.Clear();
        _player.Backpack.TryAdd(rounds, 6);

        int before = _weapons.Reserve;
        int spent = _player.TryUseBest();
        int after = _weapons.Reserve;

        // Fill the rest of the way through the same entry point the item uses,
        // then confirm a full reserve refuses the stack and leaves it worth its
        // sale price instead.
        _weapons.AddReserve(rifle.MaxReserve);
        bool atCap = !_weapons.WantsAmmo;
        int refused = _player.TryUseBest();

        GD.Print($"  reserve {before} -> {after} for {spent} of value; " +
                 $"cap {rifle.MaxReserve} reached = {atCap}, and refuses more = {refused == 0}");

        return spent == rounds.Value
            && after - before == Mathf.RoundToInt(rounds.EffectAmount)
            && atCap
            && refused == 0;
    }

    private bool? StageAdrenaline(int tick)
    {
        ItemResource? shot = Load("adrenaline_shot");
        if (shot == null)
            return false;

        if (tick == 1)
        {
            _player!.Heal(9999.0f);
            _player.Backpack.Clear();
            _player.Backpack.TryAdd(shot, 1);

            _spentOnAdrenaline = _player.TryUseBest();
            _adrenalineAtUse = _player.AdrenalineRemaining;
            return null;
        }

        // A second and a bit later it should have ticked down but not expired.
        if (tick < 90)
            return null;

        bool stillRunning = _player!.AdrenalineActive;
        float remaining = _player.AdrenalineRemaining;

        GD.Print($"  spent {_spentOnAdrenaline} for {_adrenalineAtUse:F0}s, " +
                 $"{remaining:F1}s left after 1.5s, boost +{_player.AdrenalineBoost * 100.0f:F0}%");

        return _spentOnAdrenaline == shot.Value
            && Mathf.Abs(_adrenalineAtUse - shot.EffectAmount) < 0.01f
            && stillRunning
            && remaining < _adrenalineAtUse;
    }

    private int _spentOnAdrenaline;
    private float _adrenalineAtUse;

    /// Running out is a change of tactics, not a dead end. The rifle goes quiet
    /// and the knife keeps swinging.
    private bool? StageDryThenSwap(int tick)
    {
        if (tick == 1)
        {
            _player!.Heal(9999.0f);
            _player.Backpack.Clear();
            _horde!.Pool.Clear();

            // A weapon that arrives nearly empty, equipped through the same path
            // the loadout uses. One round in the magazine and nothing behind it,
            // so it fires once and is out — no test-only setter required to get
            // a gun into the state this stage is about.
            _weapons!.Equip(0, new WeaponResource
            {
                WeaponName = "One-Shot Rifle",
                Category = WeaponCategory.Firearm,
                BaseDamage = 999.0f,
                BaseAttackSpeed = 6.0f,
                BaseRange = 18.0f,
                MagazineSize = 1,
                StartingReserve = 0,
                Penetration = 1,
            });

            for (int i = 0; i < 3; i++)
                _horde.Spawn(_player.GlobalPosition + new Vector3(3.0f + i * 0.4f, 0.0f, 0.0f));

            _dryStartCount = _horde.Pool.Count;
            return null;
        }

        // Two seconds of standing next to targets: the one round goes, then
        // nothing.
        if (tick == 120)
        {
            _survivedDry = _horde!.Pool.Count;
            _wasDry = _weapons!.IsDry;
            _weapons.SwapWeapon();
            _swappedTo = _weapons.Weapon?.WeaponName ?? "(none)";
            return null;
        }

        if (tick < 300)
            return null;

        int afterSwap = _horde!.Pool.Count;

        GD.Print($"  one round then dry: {_dryStartCount} -> {_survivedDry} enemies " +
                 $"(dry = {_wasDry}); swapped to {_swappedTo}, then {_survivedDry} -> {afterSwap}");

        // Exactly one dies to the single round, the rest survive the dry spell,
        // and the sidearm finishes them.
        return _wasDry && _survivedDry == _dryStartCount - 1 && afterSwap < _survivedDry;
    }

    private int _dryStartCount;
    private int _survivedDry;
    private bool _wasDry;
    private string _swappedTo = "";

    /// A sidearm that forgets its magazine every time it is put away is not a
    /// sidearm, and a swap must not hand the fresh weapon the other's levels.
    private bool? StageSlotsAreSeparate(int tick)
    {
        // Currently on the sidearm from the previous stage.
        int sidearmSlot = _weapons!.ActiveSlot;
        int sidearmLevel = _weapons.Level;

        _weapons.SwapWeapon();
        int rifleSlot = _weapons.ActiveSlot;
        int rifleAmmo = _weapons.Ammo;
        int rifleReserve = _weapons.Reserve;

        // Give the rifle a level, swap away and back, and check it survived.
        _weapons.AddRunUpgrade();
        int rifleLevel = _weapons.Level;

        _weapons.SwapWeapon();
        int sidearmLevelAgain = _weapons.Level;
        _weapons.SwapWeapon();

        GD.Print($"  slots {sidearmSlot}/{rifleSlot}; rifle kept {rifleAmmo}/{rifleReserve} rounds " +
                 $"and level {rifleLevel} across a round trip ({_weapons.Level}); " +
                 $"sidearm level unchanged at {sidearmLevelAgain} (was {sidearmLevel})");

        return sidearmSlot != rifleSlot
            && _weapons.Ammo == rifleAmmo
            && _weapons.Reserve == rifleReserve
            && _weapons.Level == rifleLevel
            && sidearmLevelAgain == sidearmLevel;
    }
}
