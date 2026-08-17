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
    }

    private readonly Slot[] _slots = { new(), new() };
    private int _active;

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

    /// Where this run begins. Practice counts for at most half the weapon's
    /// ceiling, so a veteran starts further along but never arrives — there is
    /// always a climb left, which is the only reason in-run growth is worth
    /// offering to a veteran at all.
    ///
    /// Practice above that half is not wasted, it is unspent: a weapon with a
    /// higher ceiling lets more of the same practice count.
    public int StartLevel => Weapon == null
        ? 0
        : Mathf.Min(_proficiency[(int)Weapon.Category], Weapon.MaxLevel / 2) + Weapon.TierStartBonus;

    /// The one number every curve is read at.
    public int Level => Weapon == null ? 0 : Weapon.ClampLevel(StartLevel + RunUpgrades);

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

        int taken = Mathf.Min(rounds, weapon.MaxReserve - slot.Reserve);
        if (taken <= 0)
            return 0;

        slot.Reserve += taken;
        return taken;
    }

    /// True when topping up would do something. Asked before an ammo item is
    /// spent, so a full reserve leaves the rounds worth their sale price.
    public bool WantsAmmo => MagazineSlot is { Weapon: { } weapon } slot && slot.Reserve < weapon.MaxReserve;

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
            _projectileRenderer = new HordeRenderer(
                texture, shader, ProjectileHeight, ProjectileCapacity, 60.0f,
                groundAnchored: false, bobAmplitude: 0.0f);

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
        slot.Reserve = Mathf.Min(weapon.StartingReserve, weapon.MaxReserve);
        slot.Cooldown = 0.0f;
        slot.ReloadRemaining = 0.0f;
        slot.RunUpgrades = 0;
    }

    public int GetProficiency(WeaponCategory category) => _proficiency[(int)category];

    public void SetProficiency(WeaponCategory category, int level) => _proficiency[(int)category] = level;

    public override void _PhysicsProcess(double delta)
    {
        float step = (float)delta;
        StepProjectiles(step);

        Slot slot = _slots[_active];
        if (slot.Weapon is not { } weapon || _horde == null)
            return;

        int level = Level;

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
        if (slot.Cooldown > 0.0f)
            return;

        Vector3 origin = GlobalPosition;
        float range = weapon.GetEffectiveRange(level);

        if (!TryGetAimDirection(origin, range, out Vector2 direction))
            return;

        Fire(origin, direction, level);
        slot.Cooldown = weapon.GetEffectiveAttackDelay(level);

        if (weapon.MagazineSize > 0)
            slot.Ammo--;
    }

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

    private void Fire(Vector3 origin, Vector2 direction, int level)
    {
        WeaponResource weapon = Weapon!;
        float range = weapon.GetEffectiveRange(level);
        float damage = weapon.GetEffectiveDamage(level);

        if (weapon.IsMelee)
        {
            // SwingArcDegrees is the full sweep; the query wants the half-angle.
            int count = _horde!.QueryArc(origin, direction, range, weapon.SwingArcDegrees * 0.5f, _hitList);

            // Backwards: a kill swap-removes the last element into the killed
            // slot, which would silently skip an enemy on a forward walk.
            for (int i = count - 1; i >= 0; i--)
            {
                _horde.Damage(_hitList[i], damage, direction * weapon.Knockback);
                RecordHit(weapon.Category);
            }
            return;
        }

        Vector2 shot = ApplySpread(direction, weapon.GetEffectiveSpreadDegrees(level));

        if (weapon.IsProjectile)
        {
            float speed = weapon.GetEffectiveProjectileSpeed(level);
            Projectiles.TrySpawn(
                new Vector3(origin.X, 0.0f, origin.Z),
                shot * speed,
                damage,
                weapon.Knockback,
                range / speed,
                weapon.Penetration);
            return;
        }

        int hits = _horde!.QueryRay(origin, shot, range, HitscanThickness, _hitList);
        int remaining = weapon.Penetration;

        // A hitscan shot resolves instantly and would otherwise be invisible —
        // the player could not tell a firing weapon from a jammed one. Zero
        // damage marks it cosmetic, and the projectile step skips its collision.
        SpawnTracer(origin, shot, range);

        // Forward here, because QueryRay ordered the list nearest-first and
        // penetration has to consume it in that order. Re-query after each kill
        // would be correct too, and much slower; instead the loop tolerates the
        // swap by re-reading positions rather than trusting stale indices.
        for (int i = 0; i < hits && remaining > 0; i++)
        {
            int index = _hitList[i];
            if (index >= _horde.Pool.Count)
                continue;

            _horde.Damage(index, damage, shot * weapon.Knockback);
            RecordHit(weapon.Category);
            remaining--;
        }
    }

    /// Purely visual streak along a hitscan shot. Damage of zero is the marker
    /// that keeps it out of collision.
    private void SpawnTracer(Vector3 origin, Vector2 direction, float range)
    {
        const float tracerSpeed = 70.0f;
        Projectiles.TrySpawn(
            new Vector3(origin.X, 0.0f, origin.Z),
            direction * tracerSpeed,
            0.0f,
            0.0f,
            range / tracerSpeed,
            0);
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

            _horde.Damage(target, Projectiles.Damage[i], velocity.Normalized() * Projectiles.Knockback[i]);
            RecordHit(WeaponCategory.BowCrossbow);

            if (--Projectiles.Pierce[i] <= 0)
                Projectiles.DespawnAt(i);
        }

        _projectileRenderer?.Sync(Projectiles, ProjectileHeight);
    }

    private void RecordHit(WeaponCategory category) => _hits[(int)category]++;

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
