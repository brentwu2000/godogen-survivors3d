using Godot;

/// Checks the layer credits finally spend into: that an old save survives the
/// version bump, that a purchase is all-or-nothing, and that taking the good kit
/// out is a wager rather than a formality.
///
///   godot --headless --script test/ShopProbe.cs
///
/// Exit code is the verdict. The profile on disk is backed up and restored, so
/// running this cannot cost a player their save — the file this probe is about
/// is the same one they own.
public partial class ShopProbe : SceneTree
{
    private const string ProfilePath = "user://profile.json";
    private const string Plate = "res://resources/gear/plate_carrier.tres";
    private const string Jacket = "res://resources/gear/worn_jacket.tres";
    private const string ServiceRifle = "res://resources/weapons/service_rifle.tres";

    private Node? _scene;
    private Player? _player;
    private MetaManager? _meta;

    private int _stage;
    private int _stageTick;
    private bool _failed;
    private string? _backup;

    public override void _Initialize()
    {
        _backup = FileAccess.FileExists(ProfilePath)
            ? FileAccess.GetFileAsString(ProfilePath)
            : null;
    }

    public override bool _PhysicsProcess(double delta)
    {
        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageMigration, "a v1 save survives the bump");
            case 1: return RunStage(StageRejectsFuture, "a newer save is refused, not half-read");
            case 2: return RunStage(StagePurchase, "buying is all or nothing");
            case 3: return RunStage(StageSell, "the stash sells at face value");
            case 4: return RunStage(StageGearApplies, "bought gear reaches the run");
            case 5: return RunStage(StageDeathTakesIt, "dying costs the kit, not the practice");
            default:
                Restore();
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

    private void Restore()
    {
        if (_backup == null)
        {
            if (FileAccess.FileExists(ProfilePath))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ProfilePath));
            return;
        }

        using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(_backup);
    }

    /// The reason the version moved at all: a v1 file has no record of what was
    /// bought, and the safe reading of that is "the starting kit and nothing
    /// else" — never "nothing", which would take away the shirt on their back.
    private bool? StageMigration(int tick)
    {
        const string v1 = """
        {
          "version": 1,
          "credits": 4321,
          "stash": { "Circuit Board": 3 },
          "proficiency": [1, 2, 3, 4],
          "loadout": "res://resources/weapons/hunting_bow.tres",
          "runs_survived": 7,
          "runs_lost": 2
        }
        """;

        Profile? migrated = Profile.FromJson(v1);
        if (migrated == null)
        {
            GD.Print("  a v1 file was rejected outright");
            return false;
        }

        bool kept = migrated.Credits == 4321
            && migrated.Stash.TryGetValue("Circuit Board", out int boards) && boards == 3
            && migrated.Proficiency[3] == 4
            && migrated.LoadoutWeapon.EndsWith("hunting_bow.tres")
            && migrated.RunsSurvived == 7;

        bool ownsKit = true;
        foreach (string path in Profile.StartingKit)
            ownsKit &= migrated.Owned.Contains(path);

        bool ownsNothingElse = migrated.Owned.Count == Profile.StartingKit.Length;

        // And it round-trips as a v2 file from here on.
        Profile? again = Profile.FromJson(migrated.ToJson());

        GD.Print($"  v1 -> credits {migrated.Credits}, practice {migrated.Proficiency[3]}, " +
                 $"owns {migrated.Owned.Count} items (starting kit = {ownsKit}); " +
                 $"re-reads as v2 = {again != null}");

        return kept && ownsKit && ownsNothingElse && again != null;
    }

    private bool? StageRejectsFuture(int tick)
    {
        Profile? future = Profile.FromJson("""{ "version": 99, "credits": 1 }""");
        Profile? garbage = Profile.FromJson("not json at all");
        Profile? versionless = Profile.FromJson("""{ "credits": 1 }""");

        GD.Print($"  future refused = {future == null}, garbage refused = {garbage == null}, " +
                 $"versionless refused = {versionless == null}");

        return future == null && garbage == null && versionless == null;
    }

    /// A shop that deducts and does not deliver, or delivers and does not
    /// deduct, is worse than one that refuses.
    private bool? StagePurchase(int tick)
    {
        var catalogue = new ShopCatalogue();
        ShopCatalogue.Entry? plate = null;

        foreach (ShopCatalogue.Entry entry in catalogue.All)
        {
            if (entry.Path == Plate)
                plate = entry;
        }

        if (plate == null || plate.Price <= 0)
        {
            GD.Print("  the plate carrier is not on sale; run BuildGear.cs");
            return false;
        }

        var poor = new Profile { Credits = plate.Price - 1 };
        bool refusedGrant = !poor.Owns(Plate);
        int creditsAfterRefusal = poor.Credits;

        var rich = new Profile { Credits = plate.Price + 500 };
        rich.Credits -= plate.Price;
        rich.Grant(Plate);

        // Through disk, because "it worked in memory" is not what a player gets.
        Profile? reloaded = Profile.FromJson(rich.ToJson());

        GD.Print($"  {plate.Name} at {plate.Price}: too poor keeps {creditsAfterRefusal} and owns " +
                 $"nothing new = {refusedGrant}; bought leaves {rich.Credits} and survives a " +
                 $"round trip = {reloaded?.Owns(Plate)}");

        return refusedGrant
            && creditsAfterRefusal == plate.Price - 1
            && rich.Credits == 500
            && reloaded?.Owns(Plate) == true;
    }

    private bool? StageSell(int tick)
    {
        var profile = new Profile { Credits = 100 };
        profile.AddToStash("Circuit Board", 2);   // 120 each
        profile.AddToStash("Scrap Metal", 3);     // 10 each

        int worth = ShopCatalogue.StashValue(profile);
        profile.Credits += worth;
        profile.Stash.Clear();

        GD.Print($"  stash of 2 boards + 3 scrap worth {worth}, credits 100 -> {profile.Credits}, " +
                 $"stash now {profile.Stash.Count} entries");

        return worth == 270 && profile.Credits == 370 && profile.Stash.Count == 0;
    }

    /// Bought gear has to actually reach the player, or the shop is a menu that
    /// changes a number in a file.
    private bool? StageGearApplies(int tick)
    {
        if (tick == 1)
        {
            var profile = new Profile { Credits = 0 };
            profile.Grant(Plate);
            profile.Grant(ServiceRifle);
            profile.EquippedGear[(int)GearSlot.Armour] = Plate;
            profile.LoadoutWeapon = ServiceRifle;
            profile.Proficiency[(int)WeaponCategory.Firearm] = 5;
            Write(profile);

            LoadRun();
            return null;
        }

        if (tick < 4)
            return null;

        var plate = GD.Load<GearResource>(Plate);
        var rifle = GD.Load<WeaponResource>(ServiceRifle);
        var weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        if (plate == null || rifle == null || weapons == null || _player == null)
            return false;

        float expectedHealth = 100.0f + plate.HealthBonus;
        bool healthApplied = Mathf.Abs(_player.MaxHealth - expectedHealth) < 0.01f;
        bool weaponEquipped = weapons.Weapon?.WeaponName == rifle.WeaponName;

        // The whole point of a longer curve: practice that was capped at 4 by
        // the scavenged rifle counts up to 8 here.
        bool deeperStart = weapons.StartLevel == Mathf.Min(5, rifle.MaxLevel / 2) + rifle.TierStartBonus;

        GD.Print($"  wearing {plate.GearName}: max HP {_player.MaxHealth:F0} (expected {expectedHealth:F0}); " +
                 $"carrying {weapons.Weapon?.WeaponName}, start level {weapons.StartLevel}/{weapons.MaxLevel}");

        return healthApplied && weaponEquipped && deeperStart;
    }

    /// The rule the shop exists to create. Dying takes what was bought and
    /// leaves what was learned.
    private bool? StageDeathTakesIt(int tick)
    {
        if (tick == 1)
        {
            _player?.TakeDamage(99999.0f);
            return null;
        }

        if (tick < 6)
            return null;

        Profile after = SaveSystem.Load();

        bool lostPlate = !after.Owns(Plate);
        bool lostRifle = !after.Owns(ServiceRifle);
        bool keptJacket = after.Owns(Jacket);
        bool keptPractice = after.Proficiency[(int)WeaponCategory.Firearm] >= 5;

        GD.Print($"  after dying: plate gone = {lostPlate}, service rifle gone = {lostRifle}, " +
                 $"starting jacket kept = {keptJacket}, firearm practice {after.Proficiency[3]}");

        return lostPlate && lostRifle && keptJacket && keptPractice;
    }

    private static void Write(Profile profile)
    {
        using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(profile.ToJson());
    }

    private void LoadRun()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Deliberately not ephemeral: this stage is about what the meta layer
        // writes to disk. The file is backed up and restored around the probe.
        GetRoot().AddChild(scene);
        _scene = scene;
        _player = scene.GetNodeOrNull<Player>("Player");
        _meta = scene.GetNodeOrNull<MetaManager>("MetaManager");

        scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
    }
}
