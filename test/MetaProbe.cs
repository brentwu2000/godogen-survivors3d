using Godot;

/// Checks the layer above a run: profile serialisation, the safe box, and the
/// rule that dying keeps only what was secured.
///
///   godot --headless --script test/MetaProbe.cs
///
/// The on-disk profile is backed up and restored around the save/load stage, and
/// the scene's MetaManager is switched to ephemeral before anything can bank, so
/// running this never costs a real profile.
public partial class MetaProbe : SceneTree
{
    private Player _player = null!;
    private MetaManager _meta = null!;
    private Horde _horde = null!;
    private LootContainer _crate = null!;

    private int _stage;
    private int _tick;
    private bool _failed;

    private int _creditsBefore;
    private int _securedValue;
    private int _backpackValueAfterSecuring;
    private int _endedBanked = -1;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _tick == 0 && !Bind())
        {
            Quit(1);
            return true;
        }

        _tick++;

        switch (_stage)
        {
            case 0: return Advance(StageJsonRoundTrip, "profile json round trip");
            case 1: return Advance(StageDiskRoundTrip, "profile survives a disk round trip");
            case 2: return Advance(StageRejectsGarbage, "unreadable profile falls back to fresh");
            case 3: return Advance(StageSecure, "safe box takes the best item");
            case 4: return Advance(StageDeathBanking, "death banks the safe box only");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool Bind()
    {
        Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
        Player? player = scene.GetNodeOrNull<Player>("Player");
        MetaManager? meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        Horde? horde = scene.GetNodeOrNull<Horde>("Horde");
        LootContainer? crate = scene.GetNodeOrNull<LootContainer>("LootContainers/Crate0");

        if (player == null || meta == null || horde == null || crate == null)
        {
            GD.PushError($"PROBE FAILED — player={player != null} meta={meta != null} " +
                         $"horde={horde != null} crate={crate != null}");
            return false;
        }

        _player = player;
        _meta = meta;
        _horde = horde;
        _crate = crate;

        // Switch off persistence before anything can bank. _Ready already read
        // the profile, which is harmless; Persist checks the flag at call time.
        _meta.Ephemeral = true;
        _creditsBefore = _meta.Profile.Credits;

        _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);
        scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

        return true;
    }

    private bool Advance(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_tick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _tick = 0;
        return false;
    }

    private bool? StageJsonRoundTrip(int tick)
    {
        var original = new Profile { Credits = 1234, LoadoutWeapon = "res://resources/weapons/fire_axe.tres" };
        original.AddToStash("Medkit", 3);
        original.AddToStash("Scrap Metal", 7);
        original.Proficiency[(int)WeaponCategory.MeleeLong] = 5;
        original.RunsSurvived = 2;
        original.RunsLost = 9;

        Profile? restored = Profile.FromJson(original.ToJson());
        if (restored == null)
        {
            GD.Print("  round trip returned null");
            return false;
        }

        bool ok = restored.Credits == 1234
                  && restored.LoadoutWeapon == original.LoadoutWeapon
                  && restored.Stash.TryGetValue("Medkit", out int medkits) && medkits == 3
                  && restored.Stash.TryGetValue("Scrap Metal", out int scrap) && scrap == 7
                  && restored.Proficiency[(int)WeaponCategory.MeleeLong] == 5
                  && restored.RunsSurvived == 2
                  && restored.RunsLost == 9;

        GD.Print($"  credits {restored.Credits}, stash {restored.Stash.Count} kinds, " +
                 $"melee-long proficiency {restored.Proficiency[(int)WeaponCategory.MeleeLong]}");
        return ok;
    }

    private bool? StageDiskRoundTrip(int tick)
    {
        string? backup = ReadProfileText();
        try
        {
            var written = new Profile { Credits = 777 };
            written.AddToStash("Antiviral Serum", 2);

            if (!SaveSystem.Save(written))
            {
                GD.Print("  save reported failure");
                return false;
            }

            Profile loaded = SaveSystem.Load();
            loaded.Stash.TryGetValue("Antiviral Serum", out int serum);

            GD.Print($"  wrote credits 777, read back {loaded.Credits}, serum {serum}");
            return loaded.Credits == 777 && serum == 2;
        }
        finally
        {
            RestoreProfileText(backup);
        }
    }

    /// A profile the build cannot read must not be partially applied.
    private bool? StageRejectsGarbage(int tick)
    {
        if (Profile.FromJson("{ not json at all") != null)
        {
            GD.Print("  malformed text was accepted");
            return false;
        }

        if (Profile.FromJson("{\"version\": 999, \"credits\": 5}") != null)
        {
            GD.Print("  a future version was accepted");
            return false;
        }

        string? backup = ReadProfileText();
        try
        {
            WriteProfileText("{ garbage");
            Profile fresh = SaveSystem.Load();
            GD.Print($"  unreadable file -> credits {fresh.Credits}, stash {fresh.Stash.Count}");
            return fresh.Credits == 0 && fresh.Stash.Count == 0;
        }
        finally
        {
            RestoreProfileText(backup);
        }
    }

    private bool? StageSecure(int tick)
    {
        if (tick == 1)
        {
            _horde.Pool.Clear();
            _player.GlobalPosition = _crate.GlobalPosition;
            return null;
        }

        if (tick < 240 && !_crate.Looted)
            return null;

        if (_player.Backpack.TotalValue <= 0)
        {
            GD.Print("  crate produced nothing to secure");
            return false;
        }

        int bagBefore = _player.Backpack.TotalValue;
        int bulkBefore = _player.Backpack.UsedBulk;

        _securedValue = _player.TrySecureBest();
        _backpackValueAfterSecuring = _player.Backpack.TotalValue;

        bool moved = _securedValue > 0
                     && _player.SafeBox.TotalValue == _securedValue
                     && _backpackValueAfterSecuring == bagBefore - _securedValue
                     && _player.Backpack.UsedBulk < bulkBefore;

        GD.Print($"  bag {bagBefore} -> {_backpackValueAfterSecuring}, safe box {_player.SafeBox.TotalValue} " +
                 $"({_player.SafeBox.UsedBulk}/{_player.SafeBox.Capacity} bulk)");
        return moved;
    }

    private bool? StageDeathBanking(int tick)
    {
        if (tick == 1)
        {
            _meta.ProfileBanked += (gained, total) => _endedBanked = gained;
            _player.TakeDamage(_player.MaxHealth * 2.0f);
            return null;
        }

        if (tick < 10 && _endedBanked < 0)
            return null;

        int gained = _meta.Profile.Credits - _creditsBefore;
        bool stashHasSecured = _meta.Profile.Stash.Count > 0;
        bool backpackLost = _backpackValueAfterSecuring > 0;

        GD.Print($"  died holding {_backpackValueAfterSecuring} in the bag and {_securedValue} secured " +
                 $"-> banked {gained}, runs lost {_meta.Profile.RunsLost}");

        return gained == _securedValue
               && backpackLost
               && stashHasSecured
               && _meta.Profile.RunsLost > 0;
    }

    private static string? ReadProfileText()
    {
        if (!FileAccess.FileExists(SaveSystem.ProfilePath))
            return null;

        using FileAccess file = FileAccess.Open(SaveSystem.ProfilePath, FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    private static void WriteProfileText(string text)
    {
        using FileAccess file = FileAccess.Open(SaveSystem.ProfilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(text);
    }

    private static void RestoreProfileText(string? backup)
    {
        if (backup == null)
            SaveSystem.Delete();
        else
            WriteProfileText(backup);
    }
}
