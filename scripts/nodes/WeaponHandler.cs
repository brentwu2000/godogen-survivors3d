using Godot;

/// Drives the equipped weapon: target selection, firing, ammo and proficiency.
///
/// Firing is automatic — the survivors-like contract is that the player steers
/// and the weapon handles itself. An explicit aim from the input source overrides
/// target selection; with no aim, the nearest enemy in range is chosen.
public partial class WeaponHandler : Node3D
{
    /// Cone thickness for hitscan, in metres. A ray with no width misses almost
    /// everything, because enemy positions are points.
    [Export] public float HitscanThickness { get; set; } = 0.45f;

    /// Equipped on _Ready. Empty leaves the player unarmed until something calls
    /// Equip, which is what the loadout screen will do.
    [Export] public string StartingWeaponPath { get; set; } = "res://resources/weapons/scavenged_rifle.tres";

    [Export] public int ProjectileCapacity { get; set; } = 256;
    [Export] public float ProjectileHeight { get; set; } = 0.25f;

    /// Metres above the ground that shots leave from and projectiles fly at.
    [Export] public float MuzzleHeight { get; set; } = 1.0f;

    /// Stops the weapon starting an attack, without stopping anything else.
    ///
    /// Turning the whole node off is the obvious way to isolate a measurement and
    /// it is wrong: projectiles are stepped here, and so is the burst queue, so a
    /// disabled handler measures a trait that can never happen. This suppresses
    /// the decision to fire and leaves the rest of the tick running.
    [Export] public bool HoldFire { get; set; }

    /// One weapon's entire live state. Two of these rather than one, because a
    /// swap has to keep the other weapon's magazine, cooldown and levels — a
    /// sidearm that resets every time it is put away is not a sidearm.
    private sealed class Slot
    {
        public WeaponResource? Weapon;
        public int Ammo;
        public int Reserve;
        public int RunUpgrades;
        public float Cooldown;
        public float ReloadRemaining;

        /// Shots still owed by a burst, and the wait before the next one. Queued
        /// rather than fired all at once: a burst that lands in a single frame is
        /// one loud shot with a bigger number, and the whole point of the trait
        /// is the rhythm.
        public int BurstLeft;
        public float BurstDelay;
        public Vector2 BurstDirection;

        /// Seconds since this weapon last actually attacked.
        ///
        /// Per slot and ticked for *both*, because a holstered marksman rifle is
        /// still waiting. Charging only while the weapon is drawn would make the
        /// trait "hold this weapon and do nothing", which is not a decision — the
        /// interesting version is carrying it as a sidearm, fighting with the
        /// other one, and swapping to a charged shot.
        public float SinceFired;

        /// Whether this slot's next shot is a charged one.
        ///
        /// On the slot rather than on the handler because both slots fire and
        /// only one of them is active. The handler's `IsCharged` answered for the
        /// active slot and was *also* what the shot itself consulted, so a
        /// charged Marksman Rifle waiting in the second slot handed its 3.5x to
        /// whatever the first slot fired next — and the readout agreed, because
        /// it asked the same wrong question.
        public bool Charged =>
            Weapon is { Trait: WeaponTrait.Charge, TraitCount: > 0 }
            && SinceFired >= Weapon.TraitCount;
    }

    private readonly Slot[] _slots = { new(), new() };
    private int _active;

    /// Raised once per shot or swing, and once per enemy the shot reached. Plain
    /// C# events rather than Godot signals for the same reason Horde.EnemyKilled
    /// is one: both ends are C#, and these fire several times a second.
    ///
    /// Two events rather than one, because a swing that hits nothing still has to
    /// be audible while five hits from one swing must not be five times as loud.
    /// Both carry where they happened. Sound does not need it — the mix is not
    /// positional — but a muzzle flash with no muzzle and a spark with no impact
    /// point are the two effects most worth having, so the position travels with
    /// the event rather than being re-derived by whoever wants to draw it.
    /// A shot went out. Carries the *weapon*, not its category.
    ///
    /// The category was four values for nine weapons, so a pump shotgun and a
    /// marksman rifle announced themselves identically and were drawn
    /// identically — the same white puff, the same size, the same duration. The
    /// weapons resolve completely differently and none of that reached the
    /// screen, which is the whole of "the weapon types feel the same".
    ///
    /// The resource is already in hand at every call site. Passing it costs
    /// nothing and lets the effect be built from damage, spread, rate and trait
    /// rather than from a four-way switch.
    public event System.Action<WeaponResource, Vector3, Vector2>? Fired;
    /// Something was hit, and by how much.
    ///
    /// The damage is what makes a spark worth scaling: a knife tick and a
    /// marksman round both used to produce the same flare, so the one piece of
    /// feedback that could have said "that landed properly" said nothing.
    public event System.Action<Vector3, WeaponCategory, float>? Hit;

    /// Where a chain arc went from and to. Drawn by `EffectDirector`, which owns
    /// every particle in the game.
    public event System.Action<Vector3, Vector3>? Chained;

    public WeaponResource? Weapon => _slots[_active].Weapon;
    public int Ammo => _slots[_active].Ammo;
    public int Reserve => _slots[_active].Reserve;
    public bool Reloading => _slots[_active].ReloadRemaining > 0.0f;
    public int ActiveSlot => _active;

    /// Out of magazine and out of reserve. Not a failure state — it is the
    /// moment the other weapon becomes the answer.
    public bool IsDry => Weapon is { MagazineSize: > 0 } && Ammo <= 0 && Reserve <= 0;

    /// Levels bought with this run's kills. Reset by starting a run, never
    /// carried out of one: what the player keeps is the loot and the practice.
    public int RunUpgrades => _slots[_active].RunUpgrades;

    /// The same readings for a named slot.
    ///
    /// The properties above all mean "the one in hand", which was the only one
    /// that could fire and is now one of two that do. The HUD has to show both or
    /// it is reporting half of what the player is doing — a magazine emptying on
    /// a weapon the readout never mentions is the kind of thing a player concludes
    /// is a bug in the damage.
    public int SlotCount => _slots.Length;
    public WeaponResource? WeaponIn(int slot) => _slots[slot].Weapon;
    public int AmmoIn(int slot) => _slots[slot].Ammo;
    public int ReserveIn(int slot) => _slots[slot].Reserve;
    public bool ReloadingIn(int slot) => _slots[slot].ReloadRemaining > 0.0f;

    public bool IsDryIn(int slot) =>
        _slots[slot].Weapon is { MagazineSize: > 0 } && _slots[slot].Ammo <= 0 && _slots[slot].Reserve <= 0;

    /// Whether this slot is one of the ones firing. See `LiveSlots`.
    public bool FiringIn(int slot) => slot == _active || slot < LiveSlots;

    /// Where this run begins. Practice counts for at most half the weapon's
    /// ceiling, so a veteran starts further along but never arrives — there is
    /// always a climb left, which is the only reason in-run growth is worth
    /// offering to a veteran at all.
    ///
    /// Practice above that half is not wasted, it is unspent: a weapon with a
    /// higher ceiling lets more of the same practice count.
    public int StartLevel => StartLevelOf(_slots[_active]);

    /// The one number every curve is read at.
    public int Level => LevelOf(_slots[_active]);

    /// Both of the above, for a named slot rather than the one in hand.
    ///
    /// **Every curve in this file used to be read at "the active weapon's
    /// level", because only the active weapon fired.** Both fire now, so a
    /// level asked for without saying whose is a question with two answers —
    /// and the wrong one is a sidearm swinging on a rifle's numbers.
    private int StartLevelOf(Slot slot) => slot.Weapon == null
        ? 0
        : Mathf.Min(_proficiency[(int)slot.Weapon.Category], slot.Weapon.MaxLevel / 2)
          + slot.Weapon.TierStartBonus;

    private int LevelOf(Slot slot) => slot.Weapon == null
        ? 0
        : slot.Weapon.ClampLevel(StartLevelOf(slot) + slot.RunUpgrades);

    public int MaxLevel => Weapon?.MaxLevel ?? 0;
    public bool AtCeiling => Weapon != null && Level >= Weapon.MaxLevel;

    public void AddRunUpgrade() => _slots[_active].RunUpgrades++;

    /// Puts looted rounds into whichever slot takes a magazine, active or not.
    /// Returns how many fit; zero means the item did nothing and should not have
    /// been spent, so the caller checks before using rather than after.
    ///
    /// Not "the active weapon": rounds go in the pouch, not in the gun that
    /// happens to be in hand. Filling only the active slot means that swapping
    /// to the knife when the rifle runs out turns every round in the backpack
    /// into dead weight, at exactly the moment they matter most.
    public int AddReserve(int rounds)
    {
        Slot? slot = MagazineSlot;
        if (slot?.Weapon is not { } weapon)
            return 0;

        // Everything offered, unless the weapon declares a cap. See
        // `WeaponResource.MaxReserve` — none of the shipped weapons does.
        int taken = weapon.CapsReserve
            ? Mathf.Min(rounds, weapon.MaxReserve - slot.Reserve)
            : rounds;
        if (taken <= 0)
            return 0;

        slot.Reserve += taken;
        return taken;
    }

    /// True when topping up would do something. Asked before an ammo item is
    /// spent, so a full reserve leaves the rounds worth their sale price.
    public bool WantsAmmo => MagazineSlot is { Weapon: { } weapon } slot
                             && (!weapon.CapsReserve || slot.Reserve < weapon.MaxReserve);

    /// The slot that consumes ammo, preferring the one in hand when both do.
    private Slot? MagazineSlot
    {
        get
        {
            if (_slots[_active].Weapon is { MagazineSize: > 0 })
                return _slots[_active];

            Slot other = _slots[1 - _active];
            return other.Weapon is { MagazineSize: > 0 } ? other : null;
        }
    }

    /// Whether the weapon not in hand could fire right now. What makes swapping
    /// an answer rather than a gesture.
    public bool OtherSlotReady
    {
        get
        {
            Slot other = _slots[1 - _active];
            return other.Weapon is { } weapon
                && (weapon.MagazineSize == 0 || other.Ammo > 0 || other.Reserve > 0);
        }
    }

    public void SwapWeapon()
    {
        if (_slots[1 - _active].Weapon == null)
            return;

        _active = 1 - _active;
    }

    /// Practice per weapon category, so switching from a rifle to a spear does
    /// not carry the rifle's skill across.
    private readonly int[] _proficiency = new int[4];

    /// Hits landed this run, per category. Banked into practice once at the end
    /// rather than levelled as they land: two growth curves moving at the same
    /// time are two curves the player cannot tell apart, and that nobody can
    /// balance separately.
    private readonly int[] _hits = new int[4];

    public ProjectilePool Projectiles { get; private set; } = null!;

    /// The run's upgrades. Empty until a player exists, so a weapon handler in a
    /// probe's bare scene behaves like an unmodified one rather than crashing.
    private RunModifiers Mods => _player?.Mods ?? _fallbackMods;
    private readonly RunModifiers _fallbackMods = new();

    private Horde? _horde;
    private Player? _player;
    private HordeRenderer? _projectileRenderer;
    private int[] _hitList = null!;
    private ulong _rng = 0xD1B54A32D192ED03UL;

    /// Hits per point of practice, and the most a single run can teach.
    ///
    /// The cap is what keeps practice a slow axis. Without it one long run with
    /// a wide melee arc banks more levels than a dozen careful ones — every
    /// enemy caught by a swing used to count, so the widest weapon learned
    /// fastest and had the most to gain from learning.
    private const int HitsPerProficiency = 250;
    private const int MaxProficiencyPerRun = 3;

    /// Practice earned this run, for the meta layer to bank when it ends.
    public int ProficiencyGain(WeaponCategory category) =>
        Mathf.Min(MaxProficiencyPerRun, _hits[(int)category] / HitsPerProficiency);

    public int HitsThisRun(WeaponCategory category) => _hits[(int)category];

    public override void _Ready()
    {
        _hitList = new int[512];
        Projectiles = new ProjectilePool(ProjectileCapacity);

        _horde = GetParent().GetNodeOrNull<Horde>("Horde") ?? GetParent().GetParent()?.GetNodeOrNull<Horde>("Horde");
        _player = GetParent() as Player ?? GetParent().GetNodeOrNull<Player>("Player");

        // One layer, because the shader samples an array either way — a single
        // sprite is the degenerate case, not a separate code path.
        Texture2DArray? texture = HordeRenderer.LoadArray(new[] { "res://assets/sprites/bolt.png" });
        var shader = GD.Load<Shader>("res://assets/shaders/horde_billboard.gdshader");
        if (texture != null && shader != null)
        {
            // `useColours` costs four floats an instance and buys the only
            // channel that can tell one weapon's shot from another's. Without it
            // every projectile in the game is the same white streak, which undoes
            // between the muzzle and the target everything the flash, the report
            // and the recoil just established.
            _projectileRenderer = new HordeRenderer(
                texture, shader, ProjectileHeight, ProjectileCapacity, 60.0f,
                groundAnchored: false, bobAmplitude: 0.0f, useColours: true);

            // TopLevel detaches it from the player's transform without moving it
            // in the tree: projectile positions are already world-space, and a
            // normal child would have the player drag every arrow along with them.
            _projectileRenderer.Node.TopLevel = true;
            AddChild(_projectileRenderer.Node);
        }

        if (!string.IsNullOrEmpty(StartingWeaponPath))
        {
            var weapon = GD.Load<WeaponResource>(StartingWeaponPath);
            if (weapon != null)
                Equip(weapon);
            else
                GD.PushWarning($"WeaponHandler: could not load {StartingWeaponPath}");
        }
    }

    public void Equip(WeaponResource weapon) => Equip(0, weapon);

    /// Fills a slot from scratch. Run upgrades belong to the weapon that earned
    /// them, so a new weapon in a slot starts at the bottom of its own curve
    /// rather than inheriting the last one's.
    public void Equip(int slotIndex, WeaponResource weapon)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length)
            return;

        Slot slot = _slots[slotIndex];
        slot.Weapon = weapon;
        slot.Ammo = weapon.MagazineSize;
        slot.Reserve = weapon.FitReserve(weapon.StartingReserve);
        slot.Cooldown = 0.0f;
        slot.ReloadRemaining = 0.0f;
        slot.RunUpgrades = 0;

        // The burst belongs to the weapon that started it. Left standing, the
        // shots it still owes come out of whatever is put in the slot next — a
        // rifle's two queued rounds fired as axe swings, on the rifle's timing,
        // while the early return skipped the normal firing path entirely.
        slot.BurstLeft = 0;
        slot.BurstDelay = 0.0f;
    }

    /// Whether the active weapon's next shot is a charged one.
    ///
    /// Read by the readout as well as by the shot, so what the player is told and
    /// what happens cannot disagree — the entire value of the trait is that the
    /// player knows when it is ready.
    public bool IsCharged => ChargedIn(_active);

    /// Whether *that* slot's next shot is a charged one.
    ///
    /// Per slot because both of them fire. `IsCharged` read the active slot and
    /// was also the test the shot itself used, so a holstered Marksman Rifle
    /// charging in the second slot — the exact case the trait's own comment sells
    /// it on — fired its multiplier onto whatever was in the first.
    public bool ChargedIn(int slot) =>
        slot >= 0 && slot < _slots.Length && _slots[slot].Charged;

    public int GetProficiency(WeaponCategory category) => _proficiency[(int)category];

    public void SetProficiency(WeaponCategory category, int level) => _proficiency[(int)category] = level;

    public override void _PhysicsProcess(double delta)
    {
        float step = (float)delta;
        StepProjectiles(step);

        // Every slot, before anything that can return early. There are four ways
        // out of this method below — a burst in progress, a reload, a dry
        // magazine, a cooldown — and a charge ticked further down would stall on
        // all of them.
        foreach (Slot each in _slots)
            each.SinceFired += step;

        if (_horde == null)
            return;

        // **Both of them.**
        //
        // Only `_slots[_active]` used to fire and the other sat idle except for
        // its charge timer, so half of every loadout in the game did nothing.
        // That is also why melee measured badly: what a melee weapon buys is
        // that it never runs out, and against a rifle that never runs out either
        // the trade was worth nothing. A weapon that keeps working while the
        // other reloads, empties, or cannot reach what is already touching the
        // player is the version of that trade with something in it.
        //
        // In slot order rather than active-first, so a run is repeatable: two
        // weapons that both want the same target resolve the same way every
        // time, and a swap mid-fight does not silently reorder who shoots first.
        for (int i = 0; i < _slots.Length; i++)
        {
            // `LiveSlots` is the control variable for the measurement this change
            // exists to produce, and it is a real setting rather than a test-only
            // back door: at 1 the game is exactly what it was before both slots
            // fired, which is the only honest "before" a "how much is a pair
            // worth" table can be read against. Same code, same layouts, one
            // variable.
            if (i != _active && i >= LiveSlots)
                continue;

            TickSlot(_slots[i], step);
        }
    }

    /// How many slots fire. Two is the game; one is the game before this change.
    [Export] public int LiveSlots { get; set; } = 2;

    /// One slot's whole firing path.
    ///
    /// Lifted out of `_PhysicsProcess` unchanged except for reading `slot`
    /// rather than `_slots[_active]`. Its five exits — no weapon, a burst in
    /// progress, a reload, a dry magazine, a cooldown — were all written as
    /// `return` against the single active weapon, so leaving them in place would
    /// have made the first slot's reload end the second slot's turn.
    private void TickSlot(Slot slot, float step)
    {
        if (slot.Weapon is not { } weapon)
            return;

        int level = LevelOf(slot);

        // A burst finishes even if the target has died or moved: the shots were
        // already fired as far as the player is concerned, and a burst that
        // silently stops halfway reads as the weapon jamming.
        if (slot.BurstLeft > 0)
        {
            slot.BurstDelay -= step;
            if (slot.BurstDelay <= 0.0f)
            {
                slot.BurstLeft--;
                slot.BurstDelay = weapon.TraitAmount;
                slot.SinceFired = 0.0f;
                Fire(slot, GlobalPosition, slot.BurstDirection, level, allowBurst: false);

                if (weapon.MagazineSize > 0)
                    slot.Ammo = Mathf.Max(0, slot.Ammo - 1);
            }

            return;
        }

        if (slot.ReloadRemaining > 0.0f)
        {
            slot.ReloadRemaining -= step;
            if (slot.ReloadRemaining <= 0.0f)
            {
                // A magazine is drawn from the reserve, not conjured. A partial
                // one is loaded when that is all there is — the last few rounds
                // still fire, they just do not last.
                int loaded = Mathf.Min(weapon.MagazineSize, slot.Reserve);
                slot.Reserve -= loaded;
                slot.Ammo = loaded;
            }
            return;
        }

        // Dry: no magazine and nothing to fill it with. The weapon simply stops,
        // which is the cue to swap rather than a message to read.
        if (weapon.MagazineSize > 0 && slot.Ammo <= 0)
        {
            if (slot.Reserve > 0)
                slot.ReloadRemaining = weapon.GetEffectiveReloadTime(level);
            return;
        }

        slot.Cooldown -= step;
        if (slot.Cooldown > 0.0f || HoldFire)
            return;

        Vector3 origin = GlobalPosition;
        float range = weapon.GetEffectiveRange(level);

        if (!TryGetAimDirection(origin, range, out Vector2 direction))
            return;

        Fire(slot, origin, direction, level);
        slot.SinceFired = 0.0f;
        slot.Cooldown = weapon.GetEffectiveAttackDelay(level) * Mods.AttackDelayScale;

        if (weapon.MagazineSize > 0)
            slot.Ammo--;
    }

    /// Fires once, now, in a given direction, ignoring cooldown and ammo.
    ///
    /// For probes. Waiting for the weapon to fire on its own means waiting for a
    /// cooldown and for auto-targeting to agree with the test about which enemy
    /// matters, and neither is the thing being measured.
    public void ForceFire(Vector2 direction)
    {
        if (Weapon == null || _horde == null)
            return;

        Fire(_slots[_active], GlobalPosition, direction.Normalized(), Level);

        // Spent here too. This is a real attack site — a probe firing twice in a
        // row must not get two charged shots — and it is the third of three,
        // which is exactly why the reset lives at the call sites rather than
        // inside Fire: Fire is also reached by the burst queue, where the shot
        // has already been paid for.
        _slots[_active].SinceFired = 0.0f;
    }

    /// Puts the firing RNG back to a known point.
    ///
    /// For a probe that has to fire the same weapon twice and attribute the
    /// difference to one modifier. Crit, spread, ricochet and the chain arc all
    /// draw from this one stream, so two otherwise identical trials differ by
    /// whatever the previous trial happened to consume — and at the two per cent
    /// threshold a comparison stage uses, that noise is the same size as the
    /// signal.
    ///
    /// Reseeding rather than zeroing: this generator is an xorshift, and a state
    /// of zero is its fixed point. It would return zero forever, which reads as a
    /// crit on every shot and a perfectly centred cone.
    public void ReseedForTesting(ulong seed) => _rng = seed != 0UL ? seed : 1UL;

    private bool TryGetAimDirection(Vector3 origin, float range, out Vector2 direction)
    {
        direction = Vector2.Zero;

        // Melee swings even at nothing; a whiffed swing is still feedback. Ranged
        // weapons hold fire rather than burning ammo on empty air.
        int target = _horde!.NearestWithin(origin, range);
        if (target < 0)
            return Weapon!.IsMelee && TryGetPlayerFacing(out direction);

        Vector3 delta3 = _horde.Pool.Position[target] - origin;
        var delta = new Vector2(delta3.X, delta3.Z);
        if (delta.LengthSquared() < 0.0001f)
            return TryGetPlayerFacing(out direction);

        direction = delta.Normalized();
        return true;
    }

    private bool TryGetPlayerFacing(out Vector2 direction)
    {
        direction = _player?.Facing ?? Vector2.Zero;
        if (direction != Vector2.Zero)
            return true;

        direction = Vector2.Down;
        return true;
    }

    private void Fire(Slot slot, Vector3 origin, Vector2 direction, int level, bool allowBurst = true)
    {
        // **This slot's weapon, not the one in hand.**
        //
        // It read `Weapon` — which is `_slots[_active].Weapon` — while taking the
        // slot as an argument and using it for everything else. So from the day
        // both slots started firing, the second slot has been firing the *first
        // slot's weapon*: its damage, its trait, its category, its penetration and
        // its knockback, on the sidearm's own cooldown and out of the sidearm's
        // own magazine.
        //
        // Nothing caught it for four phases because nothing could. `TickSlot`
        // reads `slot.Weapon` correctly for the schedule, the ammo, the reload,
        // the level and the aim range, so the sidearm ran dry on its own reserve
        // and reloaded on its own timer and every readout agreed with itself.
        // Every probe equips into slot 0 and calls `ForceFire`, which fires the
        // active slot — so the one line that was wrong is the one line no test
        // ever executed. `StageSidearmFiresItself` is the test that does.
        //
        // It is also why the pair measured +10% instead of something larger, and
        // why three of the four Sidearms measured today were indistinguishable
        // from each other: they were all a Scavenged Rifle.
        if (slot.Weapon is not { } weapon)
            return;

        float range = weapon.GetEffectiveRange(level);

        // Rolled once per attack, not once per target. A swing that crits should
        // crit — a wide arc rolling separately for each of five enemies turns a
        // twelve percent chance into "one of them took extra", every time, which
        // is a different and much duller card.
        float damage = weapon.GetEffectiveDamage(level);
        if (Mods.CritChance > 0.0f && NextFloat() < Mods.CritChance)
            damage *= Mods.CritMultiplier;

        // Charge: the only trait in the game that pays for *not* attacking.
        // Applied before the shot and spent by it — the `SinceFired` reset lives
        // at the call sites, so a burst shot and an ordinary shot both count as
        // having fired.
        //
        // Asked of *this* slot. The same mistake as the weapon above: a charged
        // Marksman Rifle waiting in the second slot handed its 3.5x to whatever
        // the first slot fired next.
        if (slot.Charged)
            damage *= weapon.TraitAmount;

        float knockback = weapon.Knockback + Mods.Knockback;

        Fired?.Invoke(weapon, origin, direction);

        if (allowBurst && weapon.Trait == WeaponTrait.Burst && weapon.TraitCount > 0)
        {
            slot.BurstLeft = weapon.TraitCount;
            slot.BurstDelay = weapon.TraitAmount;
            slot.BurstDirection = direction;
        }

        if (weapon.IsMelee)
        {
            // SwingArcDegrees is the full sweep; the query wants the half-angle.
            int count = _horde!.QueryArc(origin, direction, range * Mods.AreaScale,
                                         weapon.SwingArcDegrees * 0.5f, _hitList);

            if (weapon.Trait == WeaponTrait.Cleave)
                SpreadBleed(_hitList, count);

            // Backwards: a kill swap-removes the last element into the killed
            // slot, which would silently skip an enemy on a forward walk.
            for (int i = count - 1; i >= 0; i--)
            {
                int index = _hitList[i];
                if (index >= _horde.Pool.Count)
                    continue;

                Vector3 where = _horde.Pool.Position[index];
                bool killed = _horde.Damage(index, damage, direction * knockback);
                if (!killed)
                    killed = ResolveReaction(weapon, index, damage);
                if (!killed)
                    ApplyOnHit(weapon, index);
                RecordHit(weapon.Category, where, damage);
            }

            // Cleave: the same reach, behind as well, for a fraction of the
            // damage. Being surrounded stops being purely a problem — which is
            // the one thing a melee weapon can offer that a rifle cannot.
            if (weapon.Trait == WeaponTrait.Cleave && weapon.TraitAmount > 0.0f)
            {
                int behind = _horde.QueryArc(origin, -direction, range * Mods.AreaScale,
                                             weapon.SwingArcDegrees * 0.5f, _hitList);

                SpreadBleed(_hitList, behind);

                for (int i = behind - 1; i >= 0; i--)
                {
                    int index = _hitList[i];
                    if (index >= _horde.Pool.Count)
                        continue;

                    Vector3 where = _horde.Pool.Position[index];
                    float cleaveDamage = damage * weapon.TraitAmount;
                    bool killed = _horde.Damage(index, cleaveDamage, -direction * knockback);
                    if (!killed)
                        ResolveReaction(weapon, index, cleaveDamage);
                    RecordHit(weapon.Category, where);
                }
            }

            return;
        }

        float cone = weapon.GetEffectiveSpreadDegrees(level);

        if (weapon.IsProjectile)
        {
            float speed = weapon.GetEffectiveProjectileSpeed(level);
            Projectiles.TrySpawn(
                new Vector3(origin.X, 0.0f, origin.Z),
                ApplySpread(direction, cone) * speed,
                damage,
                knockback,
                range / speed,

                // A blast bolt stops where it connects whatever penetration says.
                // Punching through and detonating at the end of its flight would
                // put the explosion behind the crowd, which is wrong and also
                // impossible to aim.
                weapon.Trait == WeaponTrait.Blast ? 1 : weapon.Penetration + Mods.Pierce,
                weapon.Trait == WeaponTrait.Ricochet ? weapon.TraitCount : 0,
                weapon.Trait == WeaponTrait.Blast ? weapon.TraitAmount : 0.0f,
                Look(weapon).Tint,
                Look(weapon).Scale);
            return;
        }

        // One pull, several shots.
        //
        // Each rolls its own line, which is what makes a shotgun a distance
        // rather than a damage number: at range most of the cone misses, at
        // contact all of it lands, and no amount of tuning one shot's damage
        // produces that shape.
        int pellets = weapon.Trait == WeaponTrait.Spread ? Mathf.Max(1, weapon.TraitCount) : 1;
        float perPellet = weapon.Trait == WeaponTrait.Spread ? weapon.TraitAmount : 1.0f;

        for (int pellet = 0; pellet < pellets; pellet++)
            Hitscan(weapon, origin, ApplySpread(direction, cone), range, damage * perPellet, knockback);
    }

    /// One instantaneous line, resolved against the horde.
    ///
    /// Lifted out of `Fire` so `Spread` can loop it. Sharing the body matters
    /// more than the eight lines it saves: penetration, the tracer and the
    /// swap-tolerant walk are all easy to get subtly wrong, and a second copy
    /// written for pellets would drift from the one the rifle uses.
    private void Hitscan(WeaponResource weapon, Vector3 origin, Vector2 shot,
                         float range, float damage, float knockback)
    {
        int hits = _horde!.QueryRay(origin, shot, range, HitscanThickness, _hitList);
        int remaining = weapon.Penetration + Mods.Pierce;

        // A hitscan shot resolves instantly and would otherwise be invisible —
        // the player could not tell a firing weapon from a jammed one. Zero
        // damage marks it cosmetic, and the projectile step skips its collision.
        SpawnTracer(origin, shot, range, weapon);

        // Forward here, because QueryRay ordered the list nearest-first and
        // penetration has to consume it in that order. Re-query after each kill
        // would be correct too, and much slower; instead the loop tolerates the
        // swap by re-reading positions rather than trusting stale indices.
        for (int i = 0; i < hits && remaining > 0; i++)
        {
            int index = _hitList[i];
            if (index >= _horde.Pool.Count)
                continue;

            Vector3 where = _horde.Pool.Position[index];
            bool killed = _horde.Damage(index, damage, shot * knockback);
            if (!killed)
                killed = ResolveReaction(weapon, index, damage);
            if (!killed)
                ApplyOnHit(weapon, index);
            RecordHit(weapon.Category, where, damage);
            remaining--;
        }
    }

    /// Purely visual streak along a hitscan shot. Damage of zero is the marker
    /// that keeps it out of collision.
    private void SpawnTracer(Vector3 origin, Vector2 direction, float range, WeaponResource weapon)
    {
        const float tracerSpeed = 70.0f;
        (Color tint, float scale) = Look(weapon);

        Projectiles.TrySpawn(
            new Vector3(origin.X, 0.0f, origin.Z),
            direction * tracerSpeed,
            0.0f,
            0.0f,
            range / tracerSpeed,
            0,
            tint: tint,
            scale: scale);
    }

    /// What this weapon's shot looks like crossing the arena.
    ///
    /// Read off the weapon's own numbers and its trait, so a weapon added to
    /// `resources/weapons/` gets a shot that looks like itself without being
    /// listed here — the same rule the muzzle flash and the report follow.
    ///
    /// Colour carries *what it is* and size carries *how much of it there is*.
    /// A shotgun throws eight small grey pellets and a marksman rifle sends one
    /// long pale round, and at the distance most shooting happens the difference
    /// in count and size is legible before the colour is.
    private static (Color Tint, float Scale) Look(WeaponResource weapon)
    {
        float bite = Mathf.Clamp(weapon.BaseDamage / 34.0f, 0.3f, 1.0f);

        return weapon.Trait switch
        {
            // Wood and fletching. The only shot in the game that should look
            // hand-made.
            WeaponTrait.Ricochet => (new Color(0.78f, 0.62f, 0.36f), 0.9f),

            // A charge with something in it, and big enough to watch travel.
            WeaponTrait.Blast => (new Color(1.0f, 0.58f, 0.24f), 1.5f),

            // Pellets. Small and dull: a shotgun's shot is the *spread*, and
            // eight bright streaks would read as a beam weapon.
            WeaponTrait.Spread => (new Color(0.72f, 0.70f, 0.64f), 0.55f),

            // One long pale round. Length is the tell at thirty metres.
            WeaponTrait.Charge => (new Color(0.94f, 0.96f, 1.0f), 1.35f),

            // Cold, and the same cold the chilled body turns — the tracer is what
            // connects the shot the player fired to the enemy that went blue, and
            // two unrelated colours would leave them looking like two effects.
            WeaponTrait.Chill => (new Color(0.55f, 0.84f, 1.0f), 0.8f),

            // Thin and amber. Small on purpose: a mark does no damage, and a
            // round that looked heavier than the rifle's would be selling the
            // wrong thing about the weapon.
            WeaponTrait.Mark => (new Color(1.0f, 0.74f, 0.30f), 0.5f),

            _ => (new Color(1.0f, 0.90f, 0.62f), 0.7f + 0.5f * bite),
        };
    }

    private void StepProjectiles(float delta)
    {
        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            Projectiles.Life[i] -= delta;
            if (Projectiles.Life[i] <= 0.0f)
            {
                Projectiles.DespawnAt(i);
                continue;
            }

            Vector2 velocity = Projectiles.Velocity[i];
            Vector3 position = Projectiles.Position[i];
            position = new Vector3(position.X + velocity.X * delta, 0.0f, position.Z + velocity.Y * delta);
            Projectiles.Position[i] = position;

            // Cosmetic tracers fly through everything; the shot they represent
            // was already resolved the instant it was fired.
            if (_horde == null || Projectiles.Damage[i] <= 0.0f)
                continue;

            int target = _horde.NearestWithin(position, HitscanThickness);
            if (target < 0)
                continue;

            // The next target is chosen *before* the hit lands. A kill
            // swap-removes, which moves the last enemy into the victim's slot —
            // so asking afterwards to exclude "the one just hit" excludes
            // whoever took its index, and with two enemies on the field that is
            // reliably the only candidate. Same family as the stale index a
            // death blast leaves behind.
            int next = Projectiles.Bounces[i] > 0
                ? _horde.NearestExcept(position, BounceRange, target)
                : -1;
            Vector3 nextPosition = next >= 0 ? _horde.Pool.Position[next] : Vector3.Zero;

            bool killed = _horde.Damage(target, Projectiles.Damage[i],
                                        velocity.Normalized() * Projectiles.Knockback[i]);

            // A blast projectile is the Bolt Launcher's heavy impact. The pool
            // does not retain its WeaponResource, but Blast > 0 is deliberately
            // exclusive to that trait, so it carries exactly the fact the
            // reaction needs without growing every projectile for one enum.
            if (!killed && Projectiles.Blast[i] > 0.0f)
                Shatter(target, Projectiles.Damage[i]);
            if (!killed)
                Conduct(target, WeaponCategory.BowCrossbow, Projectiles.Damage[i]);
            RecordHit(WeaponCategory.BowCrossbow, position, Projectiles.Damage[i]);

            // Detonate where it connected, and stop.
            //
            // After the direct hit, so the thing it struck takes both — a bolt
            // that only splashed would be strictly worse against a single target
            // than the rifle it costs the same as. Before the pierce and bounce
            // checks, because a blast bolt does neither: it stops here whatever
            // else it was carrying.
            if (Projectiles.Blast[i] > 0.0f)
            {
                // `Horde.Blast` raises Exploded itself, so the camera shake and
                // the sound arrive without this having to know about either.
                _horde.Blast(position, Projectiles.Blast[i], Projectiles.Damage[i]);
                Projectiles.DespawnAt(i);
                continue;
            }

            // Ricochet turns to face somebody new rather than carrying straight
            // on, which is what makes it different from penetration: it curves
            // through a group instead of needing them lined up.
            if (Projectiles.Bounces[i] > 0)
            {
                if (next >= 0)
                {
                    Vector3 toNext = nextPosition - position;
                    var heading = new Vector2(toNext.X, toNext.Z);
                    if (heading.LengthSquared() > 0.0001f)
                    {
                        Projectiles.Bounces[i]--;
                        Projectiles.Velocity[i] = heading.Normalized() * velocity.Length();
                        Projectiles.Life[i] = Mathf.Max(Projectiles.Life[i], BounceRange / velocity.Length());
                        continue;
                    }
                }
            }

            if (--Projectiles.Pierce[i] <= 0)
                Projectiles.DespawnAt(i);
        }

        _projectileRenderer?.Sync(Projectiles, ProjectileHeight);
    }

    /// How far a ricochet will look for its next target. Short: a bounce is a
    /// crowd tool, and one that can cross the arena is a homing missile.
    private const float BounceRange = 7.0f;

    /// Whatever this weapon leaves behind on something it hit and did not kill.
    ///
    /// One place, called from both the swing and the shot, because the three
    /// statuses it can leave are the three weapons whose entire signature *is*
    /// what happens after the hit — a trait wired into one of the two paths would
    /// work on a rifle and silently do nothing on a blade, and the symptom is a
    /// weapon that reads as merely underpowered.
    ///
    /// **Only on a survivor.** The pool swap-removes, so a killing hit leaves the
    /// index pointing at whoever was last — and marking or chilling a body chosen
    /// by the order of an array is worse than not doing it at all, because it is
    /// invisible and it is sometimes right. Bleed had this the whole time and got
    /// away with it: 4 damage a second lands on *someone* either way. A mark
    /// lands on the wrong target and the player watches the wrong enemy not die.
    private void ApplyOnHit(WeaponResource weapon, int index)
    {
        if (weapon.TraitAmount <= 0.0f)
            return;

        switch (weapon.Trait)
        {
            case WeaponTrait.Bleed:
                _horde!.ApplyBleed(index, weapon.TraitAmount, weapon.TraitCount);
                break;

            case WeaponTrait.Chill:
                _horde!.ApplyChill(index, weapon.TraitAmount, weapon.TraitCount);
                break;

            case WeaponTrait.Mark:
                _horde!.ApplyMark(index, weapon.TraitAmount, weapon.TraitCount);
                break;
        }
    }

    /// Resolves reactions consumed by the weapon that just landed.
    /// Returns true when the reaction killed and invalidated the pool index.
    private bool ResolveReaction(WeaponResource weapon, int index, float impactDamage)
    {
        Conduct(index, weapon.Category, impactDamage);

        if (weapon.Trait is not (WeaponTrait.Cleave or WeaponTrait.Spread
                                 or WeaponTrait.Blast or WeaponTrait.Shatter))
            return false;

        return Shatter(index, impactDamage);
    }

    /// Chill + a heavy impact: spend the slow for a damage burst proportional
    /// to both the impact and the amount of chill that was banked.
    private bool Shatter(int index, float impactDamage)
    {
        if (_horde == null || impactDamage <= 0.0f)
            return false;

        float chill = _horde.ConsumeChill(index);
        if (chill <= 0.0f)
            return false;

        // At the Hand Emitter's 45% this adds 67.5% of the triggering hit. A
        // stronger non-weapon chill pays more, while Horde.MaxChill keeps the
        // burst bounded below one extra full impact.
        const float ShatterScale = 1.5f;
        return _horde.Damage(index, impactDamage * chill * ShatterScale, Vector2.Zero);
    }

    private void Conduct(int index, WeaponCategory category, float impactDamage)
    {
        if (_horde == null || index < 0 || index >= _horde.Pool.Count
            || _horde.Pool.Chill[index] <= 0.0f || !_horde.ConsumeShock(index))
            return;

        Vector3 from = _horde.Pool.Position[index];
        int first = _horde.NearestExcept(from, ConductRange, index);
        int second = _horde.NearestExceptTwo(from, ConductRange, index, first);
        _conductTargets[0] = first;
        _conductTargets[1] = second;

        foreach (int target in _conductTargets)
        {
            if (target < 0 || target >= _horde.Pool.Count)
                continue;

            Vector3 to = _horde.Pool.Position[target];
            _horde.Damage(target, impactDamage * ConductFraction, Vector2.Zero);
            _hits[(int)category]++;
            Hit?.Invoke(to, category, impactDamage * ConductFraction);
            Chained?.Invoke(from, to);
        }
    }

    private const float ConductRange = 5.0f;
    private const float ConductFraction = 0.40f;
    private readonly int[] _conductTargets = new int[2];

    /// Bleed + cleave: take one wound off the sweep and put it on every other
    /// body the same arc touched. The source is excluded so the setup is truly
    /// spent rather than immediately refreshed under a different name.
    private void SpreadBleed(int[] hits, int count)
    {
        if (_horde == null)
            return;

        int source = -1;
        for (int i = 0; i < count; i++)
        {
            int index = hits[i];
            if (index >= 0 && index < _horde.Pool.Count && _horde.Pool.BleedRemaining[index] > 0.0f)
            {
                source = index;
                break;
            }
        }

        if (source < 0)
            return;

        (float damage, float seconds) = _horde.ConsumeBleed(source);
        if (damage <= 0.0f || seconds <= 0.0f)
            return;

        for (int i = 0; i < count; i++)
        {
            int index = hits[i];
            if (index != source)
                _horde.ApplyBleed(index, damage, seconds);
        }
    }

    private void RecordHit(WeaponCategory category, Vector3 where) =>
        RecordHit(category, where, 0.0f);

    /// A hit landed, and possibly a second one nearby.
    ///
    /// `damage` is what the original hit did, so the arc can be a fraction of it
    /// rather than a flat number that is enormous early and irrelevant late.
    /// Passed as zero from the paths that have no meaningful figure — a swing
    /// that already resolved, a tracer — and the arc simply does not fire.
    private void RecordHit(WeaponCategory category, Vector3 where, float damage)
    {
        _hits[(int)category]++;
        Hit?.Invoke(where, category, damage);

        if (damage <= 0.0f || Mods.ChainChance <= 0.0f || _horde == null)
            return;

        if (NextFloat() >= Mods.ChainChance)
            return;

        // Excluded by *distance*, not by index. A kill swap-removes the last
        // enemy into the dead one's slot, so passing "the index just hit" to an
        // exclusion excludes whoever took its place — and with two enemies on
        // the field that is reliably the only candidate, so the arc silently
        // never fires. Same family of bug as the ricochet's stale index.
        int next = _horde.NearestOutside(where, ChainRange, ChainMinimumGap);
        if (next < 0)
            return;

        // One jump, never two. The arc damages through `Horde.Damage` and
        // announces itself here rather than calling back into `RecordHit`, which
        // would let a chain chain — a chance-based effect that can re-trigger
        // itself has a tail nobody chose and a frame cost nobody measured.
        Vector3 to = _horde.Pool.Position[next];
        bool killed = _horde.Damage(next, damage * ChainFraction, Vector2.Zero);
        if (!killed)
            _horde.ApplyShock(next, ShockSeconds);
        _hits[(int)category]++;

        // A chain jump reads as a smaller event than the shot that started it,
        // because it is: the chain does a fraction of the damage.
        Hit?.Invoke(to, category, damage * ChainFraction);

        // And the jump itself, which nothing drew.
        //
        // `Hit` fires at the destination, so a chain has always produced an
        // impact on a second enemy with nothing connecting it to the first — a
        // creature at the back of a crowd flinching for no visible reason. The
        // card is a *chain*; the thing the player bought is the line between the
        // two, and it was the one part not on screen.
        Chained?.Invoke(where, to);
    }

    /// How far an arc will reach. Short, like the ricochet: this is a crowd
    /// effect, and one that can cross the arena is a second weapon.
    private const float ChainRange = 4.5f;

    /// How much of the original hit arrives at the second target.
    private const float ChainFraction = 0.45f;

    /// How far away the second target has to be. Enough to be a different enemy
    /// rather than the same one measured from a slightly different point.
    private const float ChainMinimumGap = 0.6f;
    private const float ShockSeconds = 3.0f;

    /// Rotates the shot by a uniform angle inside the cone. Deterministic and
    /// allocation-free, so a capture run reproduces exactly.
    private Vector2 ApplySpread(Vector2 direction, float halfAngleDegrees)
    {
        if (halfAngleDegrees <= 0.0f)
            return direction;

        float offset = (NextFloat() * 2.0f - 1.0f) * Mathf.DegToRad(halfAngleDegrees);
        return direction.Rotated(offset);
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
