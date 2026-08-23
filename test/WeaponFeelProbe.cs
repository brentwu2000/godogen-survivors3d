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
    private readonly record struct Print(string Name, int Puffs, float Size, float Kick);

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

            // One target, close enough for the shortest reach in the set. A knife
            // reaches 1.6 m, so anything further away means the melee weapons
            // never fire and their signature is silence.
            _horde!.Pool.Clear();
            _horde.Spawn(_player!.GlobalPosition
                         + new Vector3(CameraRig.Forward(_rig!.Yaw).X, 0.0f,
                                       CameraRig.Forward(_rig.Yaw).Y) * 1.2f, 0);

            _effects!.Effects.ForgetTotals();
            _peakShake = 0.0f;
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

        _prints.Add(new Print(weaponName, pool.TotalSpawned, pool.TotalStartSize, kick));
        GD.Print($"  {weaponName,-16} {pool.TotalSpawned,3} puffs, "
               + $"{pool.TotalStartSize,6:F2} total size, kick {kick:F3}");

        if (pool.TotalSpawned == 0)
        {
            GD.PushError($"  {weaponName} emitted nothing at all");
            return false;
        }

        return null;
    }

    private float _peakShake;

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

                if (samePuffs && sameSize && sameKick)
                {
                    GD.PushError($"  {a.Name} and {b.Name} emit the same thing: "
                               + $"{a.Puffs} puffs, {a.Size:F2} size, {a.Kick:F3} kick");
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
        if (shotgun.Puffs <= rifle.Puffs)
        {
            GD.PushError($"  the shotgun emits {shotgun.Puffs} against the rifle's {rifle.Puffs} — no fan");
            ok = false;
        }

        // No powder. A bow that kicks like a firearm is a firearm that happens to
        // be slow, which is what it used to be.
        if (bow.Kick >= shotgun.Kick)
        {
            GD.PushError($"  the bow kicks {bow.Kick:F3} against the shotgun's {shotgun.Kick:F3}");
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

        GD.Print($"  shotgun {shotgun.Puffs} puffs vs rifle {rifle.Puffs}; "
               + $"bow kick {bow.Kick:F3} vs shotgun {shotgun.Kick:F3}; "
               + $"knife smear {knife.Size:F2} vs rifle flash {rifle.Size:F2}");

        return ok;
    }

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
