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

    public WeaponResource? Weapon { get; private set; }
    public int Ammo { get; private set; }
    public bool Reloading => _reloadRemaining > 0.0f;

    /// Practice per weapon category, so switching from a rifle to a spear does
    /// not carry the rifle's skill across.
    private readonly int[] _proficiency = new int[4];
    private readonly float[] _experience = new float[4];

    public ProjectilePool Projectiles { get; private set; } = null!;

    private Horde? _horde;
    private Player? _player;
    private HordeRenderer? _projectileRenderer;
    private int[] _hits = null!;
    private float _cooldown;
    private float _reloadRemaining;
    private ulong _rng = 0xD1B54A32D192ED03UL;

    /// Hits needed for the next level, scaling so early levels come quickly and
    /// mastery does not arrive by accident.
    private static float ExperienceForLevel(int level) => 12.0f + level * 8.0f;

    public override void _Ready()
    {
        _hits = new int[512];
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

    public void Equip(WeaponResource weapon)
    {
        Weapon = weapon;
        Ammo = weapon.MagazineSize;
        _cooldown = 0.0f;
        _reloadRemaining = 0.0f;
    }

    public int GetProficiency(WeaponCategory category) => _proficiency[(int)category];

    public void SetProficiency(WeaponCategory category, int level) => _proficiency[(int)category] = level;

    public override void _PhysicsProcess(double delta)
    {
        float step = (float)delta;
        StepProjectiles(step);

        if (Weapon == null || _horde == null)
            return;

        int level = _proficiency[(int)Weapon.Category];

        if (_reloadRemaining > 0.0f)
        {
            _reloadRemaining -= step;
            if (_reloadRemaining <= 0.0f)
                Ammo = Weapon.MagazineSize;
            return;
        }

        _cooldown -= step;
        if (_cooldown > 0.0f)
            return;

        Vector3 origin = GlobalPosition;
        float range = Weapon.GetEffectiveRange(level);

        if (!TryGetAimDirection(origin, range, out Vector2 direction))
            return;

        Fire(origin, direction, level);
        _cooldown = Weapon.GetEffectiveAttackDelay(level);

        if (Weapon.MagazineSize > 0 && --Ammo <= 0)
            _reloadRemaining = Weapon.GetEffectiveReloadTime(level);
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

        if (weapon.IsMelee)
        {
            // SwingArcDegrees is the full sweep; the query wants the half-angle.
            int count = _horde!.QueryArc(origin, direction, range, weapon.SwingArcDegrees * 0.5f, _hits);

            // Backwards: a kill swap-removes the last element into the killed
            // slot, which would silently skip an enemy on a forward walk.
            for (int i = count - 1; i >= 0; i--)
            {
                _horde.Damage(_hits[i], weapon.BaseDamage, direction * weapon.Knockback);
                AddExperience(weapon.Category);
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
                weapon.BaseDamage,
                weapon.Knockback,
                range / speed,
                weapon.Penetration);
            return;
        }

        int hits = _horde!.QueryRay(origin, shot, range, HitscanThickness, _hits);
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
            int index = _hits[i];
            if (index >= _horde.Pool.Count)
                continue;

            _horde.Damage(index, weapon.BaseDamage, shot * weapon.Knockback);
            AddExperience(weapon.Category);
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
            AddExperience(WeaponCategory.BowCrossbow);

            if (--Projectiles.Pierce[i] <= 0)
                Projectiles.DespawnAt(i);
        }

        _projectileRenderer?.Sync(Projectiles, ProjectileHeight);
    }

    private void AddExperience(WeaponCategory category)
    {
        int index = (int)category;
        _experience[index] += 1.0f;

        while (_experience[index] >= ExperienceForLevel(_proficiency[index]))
        {
            _experience[index] -= ExperienceForLevel(_proficiency[index]);
            _proficiency[index]++;
        }
    }

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
