using Godot;

/// Checks that firing one weapon does not look and sound like firing another.
///
///   godot --headless --script test/WeaponFeelProbe.cs
///
/// The owner's complaint was "武器種類無感" — the weapon types feel the same. The
/// numbers were never the problem: nine weapons across four categories and seven
/// traits, a shotgun throwing eight pellets across twenty degrees, a marksman
/// rifle putting one round through three bodies at thirty metres. All of it
/// worked and none of it reached the screen.
///
/// The cause was one line. `WeaponHandler.Fired` carried the weapon's *category*,
/// four values for nine weapons, so `EffectDirector` and `SoundDirector` could
/// only ever tell three of them apart — every firearm produced the same white
/// puff at the same size for the same duration, and played the same clip at the
/// same pitch and the same volume.
///
/// So this probe asks the only question that matters: fire each weapon and see
/// whether what came out is distinguishable. It compares *emissions*, not
/// screenshots, because a puff lives for a tenth of a second and nothing
/// headless can look at it.
public partial class WeaponFeelProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private EffectDirector? _effects;
    private CameraRig? _rig;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    /// One firing signature.
    ///
    /// `ShotTint` and `ShotScale` are what crossed the screen, kept apart.
    ///
    /// They were one packed number first, `R*100 + G*10 + B + Scale*1000`, and it
    /// was a bad fingerprint in both directions: the bow's brown arrow at 984.6
    /// and the scavenged rifle's pale round at 986.1 came out 1.5 apart, which is
    /// inside any tolerance worth setting, for two shots that look nothing alike.
    /// A hash that can put unlike things together can also put like things apart,
    /// and neither failure announces itself.
    ///
    /// `ShotScale` of zero means nothing crossed the screen at all — every melee
    /// weapon, and itself a signature.
    private readonly record struct Print(string Name, int Puffs, float Size, float Kick,
                                         Color ShotTint, float ShotScale, int Shots)
    {
        /// Emissions per round, which is what "a shotgun fans" is a claim about.
        ///
        /// The raw total is per *window*, and a window is forty ticks of firing at
        /// whatever rate the weapon manages against a target that may or may not
        /// still be alive — so it reads an automatic's rate of fire and a
        /// shotgun's fan as the same kind of number. It also made the reading
        /// depend on accuracy: opening the Service Rifle's cone during H4b made it
        /// miss more, its target survived the whole window instead of dying to the
        /// first shot, and its emission total went from 6 to 9 without one thing
        /// about its muzzle effect changing.
        public float PerShot => Shots > 0 ? Puffs / (float)Shots : 0.0f;
    }

    private readonly System.Collections.Generic.List<Print> _prints = new();
    private string[] _paths = System.Array.Empty<string>();

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stageTick == 0 && _stage == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Player>("Player");
            _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");
            _effects = scene.GetNodeOrNull<EffectDirector>("Effects");
            _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");

            if (_horde == null || _player == null || _weapons == null || _effects == null || _rig == null)
            {
                GD.PushError("PROBE FAILED — the scene is missing something this needs");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            // Counted at the source rather than inferred from ammo: three of the
            // nine weapons have no magazine at all, so an ammo delta is zero for
            // a knife that swung forty times.
            _weapons.Fired += (_, _, _) => _shotsFired++;

            // Read the directory rather than listing names here. A weapon added
            // to the game and not to this array is exactly the weapon nobody has
            // checked, and hardcoded lists in this repository have hidden three
            // new weapons from a probe before.
            _paths = WeaponPaths();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageEveryWeaponFires, "every weapon in the directory emits something");
            case 1: return RunStage(StageNoTwoAlike, "no two weapons emit the same signature");
            case 2: return RunStage(StageTheCharactersHold, "the shotgun fans, the bow is quiet, melee has no muzzle");
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

    /// Ticks spent on each weapon before its signature is read.
    ///
    /// Long enough for the slowest weapon in the set to get one shot away — the
    /// bolt launcher fires 1.1 times a second — and short enough that the fastest
    /// does not empty a magazine into the reading.
    private const int TicksPerWeapon = 40;

    private bool? StageEveryWeaponFires(int tick)
    {
        int index = (tick - 1) / TicksPerWeapon;
        int within = (tick - 1) % TicksPerWeapon;

        if (index >= _paths.Length)
            return _prints.Count == _paths.Length;

        if (within == 0)
        {
            var weapon = GD.Load<WeaponResource>(_paths[index]);
            if (weapon == null)
            {
                GD.PushError($"  {_paths[index]} did not load");
                return false;
            }

            _weapons!.Equip(weapon);
            _weapons.HoldFire = false;

            // How far the target stands, by what is being fired at it.
            //
            // A knife reaches 1.6 m, so a melee weapon needs the enemy almost
            // touching or it never swings and its signature is silence. But a
            // projectile crossing 1.2 m exists for about one frame, and the bolt
            // launcher reported a shot appearance of 0.00 — grouped with the
            // melee weapons, which emit nothing in flight because they have
            // nothing to emit. Six metres is far enough to sample and near enough
            // that every ranged weapon in the set still reaches.
            float away = weapon.Category is WeaponCategory.MeleeShort or WeaponCategory.MeleeLong
                ? 1.2f
                : 6.0f;

            _horde!.Pool.Clear();
            _horde.Spawn(_player!.GlobalPosition
                         + new Vector3(CameraRig.Forward(_rig!.Yaw).X, 0.0f,
                                       CameraRig.Forward(_rig.Yaw).Y) * away, 0);

            _effects!.Effects.ForgetTotals();
            _shotsFired = 0;
            _peakShake = 0.0f;
            _shotTint = default;
            _shotScale = 0.0f;
            return null;
        }

        // The peak, sampled every tick, not the level at the end.
        //
        // `CameraRig` decays shake at six per second, so over a forty-tick window
        // the reading at the end is mostly a measure of how long ago the shot was
        // — every weapon in the set reported a kick of exactly 0.000, including
        // the shotgun, which delivers the largest one in the game.

        if (within != TicksPerWeapon - 1)
            return null;

        EffectPool pool = _effects!.Effects;
        var weaponName = GD.Load<WeaponResource>(_paths[index])?.WeaponName ?? _paths[index];

        float kick = _peakShake;

        _prints.Add(new Print(weaponName, pool.TotalSpawned, pool.TotalStartSize, kick,
                              _shotTint, _shotScale, _shotsFired));

        string shot = _shotScale <= 0.0f
            ? "nothing in flight"
            : $"{_shotScale:F2}x ({_shotTint.R:F2},{_shotTint.G:F2},{_shotTint.B:F2})";

        GD.Print($"  {weaponName,-16} {pool.TotalSpawned,3} puffs over {_shotsFired,2} shots "
               + $"({(_shotsFired > 0 ? pool.TotalSpawned / (float)_shotsFired : 0.0f),4:F1}/shot), "
               + $"{pool.TotalStartSize,6:F2} size, kick {kick:F3}, shot {shot}");

        if (pool.TotalSpawned == 0)
        {
            GD.PushError($"  {weaponName} emitted nothing at all");
            return false;
        }

        return null;
    }

    private int _shotsFired;
    private float _peakShake;
    private Color _shotTint;
    private float _shotScale;

    /// The peak is sampled at frame rate, not on the physics tick.
    ///
    /// Recoil fades at nine per second, so an automatic's small per-shot kick is
    /// gone within a single 60 Hz tick and a probe sampling there reads exactly
    /// zero for it — which is not what the player sees, because the frame that
    /// drew the shot drew the displacement. Both rifles reported 0.000 that way
    /// while visibly buzzing.
    public override bool _Process(double delta)
    {
        if (_rig != null)
            _peakShake = Mathf.Max(_peakShake, _rig.RecoilLevel);

        // Sampled here too: a tracer lives for a fraction of a second and the
        // pool is empty again by the end of the window.
        ProjectilePool? shots = _weapons?.Projectiles;
        if (shots is { Count: > 0 } && _shotScale == 0.0f)
        {
            _shotTint = shots.Tint[0];
            _shotScale = shots.Scale[0];
        }

        return false;
    }

    /// The whole point, stated as an inequality.
    ///
    /// Two weapons whose signatures round to the same numbers are two weapons the
    /// player cannot tell apart without reading the corner of the screen. The
    /// tolerance is deliberately loose — this is not asking for them to differ by
    /// a lot, only to differ at all.
    private bool? StageNoTwoAlike(int tick)
    {
        bool ok = true;

        for (int i = 0; i < _prints.Count; i++)
        {
            for (int j = i + 1; j < _prints.Count; j++)
            {
                Print a = _prints[i];
                Print b = _prints[j];

                bool samePuffs = a.Puffs == b.Puffs;
                bool sameSize = Mathf.Abs(a.Size - b.Size) < 0.02f;
                bool sameKick = Mathf.Abs(a.Kick - b.Kick) < 0.002f;
                bool sameShot = Mathf.Abs(a.ShotScale - b.ShotScale) < 0.02f
                             && Apart(a.ShotTint, b.ShotTint) < 0.05f;

                if (samePuffs && sameSize && sameKick && sameShot)
                {
                    GD.PushError($"  {a.Name} and {b.Name} emit the same thing: "
                               + $"{a.Puffs} puffs, {a.Size:F2} size, {a.Kick:F3} kick, "
                               + $"a {a.ShotScale:F2}x shot of the same colour");
                    ok = false;
                }
            }
        }

        GD.Print($"  {_prints.Count} weapons, {(ok ? "all distinguishable" : "some identical")}");
        return ok;
    }

    /// Named characters, so "different" cannot be satisfied by noise.
    ///
    /// The stage above would pass on nine weapons whose effects differ by a
    /// hundredth in a random direction. These three assertions are the actual
    /// design: a shotgun's width, a bow's silence, and the fact that a swing is
    /// not a gunshot.
    private bool? StageTheCharactersHold(int tick)
    {
        Print shotgun = Find("Pump Shotgun");
        Print bow = Find("Hunting Bow");
        Print rifle = Find("Service Rifle");
        Print knife = Find("Combat Knife");

        bool ok = true;

        // A fan of pellets is more emissions than a single report, and that is
        // what makes a shotgun read as a shotgun before the damage lands.
        //
        // Per shot. Compared as window totals this asked whether a weapon firing
        // 1.4 times a second emits more than one firing seven times a second, and
        // the answer had nothing to do with either muzzle.
        if (shotgun.PerShot <= rifle.PerShot)
        {
            GD.PushError($"  the shotgun emits {shotgun.PerShot:F1} per shot against the rifle's "
                       + $"{rifle.PerShot:F1} — no fan");
            ok = false;
        }

        // No powder. A bow that kicks like a firearm is a firearm that happens to
        // be slow, which is what it used to be.
        if (bow.Kick >= shotgun.Kick)
        {
            GD.PushError($"  the bow kicks {bow.Kick:F3} against the shotgun's {shotgun.Kick:F3}");
            ok = false;
        }

        // A bow's arrow does not look like a rifle round. Checked as an
        // inequality on the packed look rather than on the colour itself, because
        // what matters is that they differ at all — which of them is browner is a
        // decision, not a rule.
        if (Apart(bow.ShotTint, rifle.ShotTint) < 0.15f)
        {
            GD.PushError($"  the bow's arrow and the rifle's round are the same colour in flight "
                       + $"({bow.ShotTint} against {rifle.ShotTint})");
            ok = false;
        }

        // A swing is a smear, not a flash. Checked by size rather than by count:
        // the melee effect is one large puff and the rifle's is one small one, so
        // counting alone cannot tell them apart.
        if (knife.Size <= rifle.Size)
        {
            GD.PushError($"  the knife's smear is {knife.Size:F2} against the rifle's flash {rifle.Size:F2}");
            ok = false;
        }

        GD.Print($"  shotgun {shotgun.PerShot:F1} puffs/shot vs rifle {rifle.PerShot:F1}; "
               + $"bow kick {bow.Kick:F3} vs shotgun {shotgun.Kick:F3}; "
               + $"knife smear {knife.Size:F2} vs rifle flash {rifle.Size:F2}");

        return ok;
    }

    /// How far apart two colours are, summed over the channels.
    ///
    /// Not a perceptual distance and not trying to be. The question is whether
    /// two shots were given different colours on purpose, and a sum of absolute
    /// differences answers it without pretending to know how they look.
    private static float Apart(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B);

    private Print Find(string name)
    {
        foreach (Print print in _prints)
        {
            if (print.Name == name)
                return print;
        }

        GD.PushError($"  no signature recorded for {name}");
        _failed = true;
        return default;
    }

    /// Every weapon resource on disk.
    ///
    /// `.tres.remap` is what an exported build leaves behind in place of the
    /// source, and trimming it is not optional: a probe that misses the suffix
    /// finds nothing in an export and reports that the game has no weapons.
    private static string[] WeaponPaths()
    {
        var found = new System.Collections.Generic.List<string>();

        using DirAccess dir = DirAccess.Open("res://resources/weapons");
        if (dir == null)
            return found.ToArray();

        foreach (string name in dir.GetFiles())
        {
            string file = name.EndsWith(".remap", System.StringComparison.Ordinal)
                ? name[..^6]
                : name;

            if (file.EndsWith(".tres", System.StringComparison.Ordinal) && !found.Contains(file))
                found.Add(file);
        }

        found.Sort(System.StringComparer.Ordinal);

        var paths = new string[found.Count];
        for (int i = 0; i < found.Count; i++)
            paths[i] = $"res://resources/weapons/{found[i]}";

        return paths;
    }
}
