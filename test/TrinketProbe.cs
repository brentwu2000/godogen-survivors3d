using Godot;

/// Checks the fourth equipment slot.
///
///   godot --headless --script test/TrinketProbe.cs
///
/// Two things carry this phase and neither is visible from the shop screen: a
/// three-entry save has to keep loading, and a trinket's kit has to land on the
/// same curve the growth cards use. Both fail silently — an old profile would
/// load with its boots in the wrong slot, and a mismatched chill curve would only
/// show up as the horde stopping dead in a run nobody could reproduce.
public partial class TrinketProbe : SceneTree
{
    private Player? _player;
    private MetaManager? _meta;
    private RunGrowth? _growth;
    private Node? _scene;

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

        _scene = scene;
        GetRoot().AddChild(scene);

        // Fitted *after* AddChild, never before. `MetaManager._Ready` assigns a
        // fresh Profile when Ephemeral, so anything equipped on the way in is
        // discarded — and the symptom is a probe that measures a player wearing
        // the starting kit while believing it dressed them.
        _player = scene.GetNodeOrNull<Player>("Player");
        _meta = meta;
        _growth = scene.GetNodeOrNull<RunGrowth>("RunGrowth");
    }

    public override bool _PhysicsProcess(double delta)
    {
        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageSlotExists, "there is a fourth slot, and it is last");
            case 1: return RunStage(StageOldSavesStillLoad, "a save written before the slot existed still loads");
            case 2: return RunStage(StageTrinketsExist, "six trinkets, none with an apostrophe in its path");
            case 3: return RunStage(StateTrinketGrantsKit, "wearing one starts the run holding kit");
            case 4: return RunStage(StageChillCompounds, "gear chill and card chill land on the same curve");
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

    private bool? StageSlotExists(int tick)
    {
        var slots = System.Enum.GetValues<GearSlot>();
        int trinket = (int)GearSlot.Trinket;

        GD.Print($"  {slots.Length} slots, Trinket is index {trinket}; " +
                 $"EquippedGear holds {new Profile().EquippedGear.Length}");

        // Last, and that is not cosmetic. The array is indexed by this enum and
        // saved by index, so a value inserted in the middle would move every
        // existing player's boots into their backpack slot.
        bool last = trinket == slots.Length - 1;
        bool sized = new Profile().EquippedGear.Length == slots.Length;

        if (!last)
            GD.PushError($"  Trinket is index {trinket} of {slots.Length} — inserting a slot renumbers saves");
        if (!sized)
            GD.PushError("  EquippedGear is not one entry per slot");

        return last && sized;
    }

    /// A three-entry save has to load with an empty trinket and nothing moved.
    private bool? StageOldSavesStillLoad(int tick)
    {
        var before = new Profile();
        before.EquippedGear[0] = "res://resources/gear/plate_carrier.tres";
        before.EquippedGear[1] = "res://resources/gear/trekking_pack.tres";
        before.EquippedGear[2] = "res://resources/gear/tread_boots.tres";

        // A save from before the slot existed: three entries, no fourth.
        string json = Json.Stringify(new Godot.Collections.Dictionary
        {
            { "equipped_gear", new Godot.Collections.Array { before.EquippedGear[0], before.EquippedGear[1], before.EquippedGear[2] } },
        });

        var parsed = Json.ParseString(json).AsGodotDictionary();
        var loaded = new Profile();

        // The reader's own rule: stop at whichever of the file and the array is
        // shorter. Written out here rather than called, because `SaveSystem` reads
        // a whole profile and this is a question about one field.
        Godot.Collections.Array saved = parsed["equipped_gear"].AsGodotArray();
        int count = Mathf.Min(saved.Count, loaded.EquippedGear.Length);
        for (int i = 0; i < count; i++)
            loaded.EquippedGear[i] = saved[i].AsString();

        GD.Print($"  a 3-entry save into a {loaded.EquippedGear.Length}-slot array: " +
                 $"boots=[{Short(loaded.EquippedGear[2])}] trinket=[{Short(loaded.EquippedGear[3])}]");

        bool bootsKept = loaded.EquippedGear[2] == before.EquippedGear[2];
        bool trinketEmpty = string.IsNullOrEmpty(loaded.EquippedGear[3]);

        if (!bootsKept)
            GD.PushError("  the boots did not survive the load — the slots shifted");
        if (!trinketEmpty)
            GD.PushError("  the trinket slot is not empty after loading a save that had none");

        return bootsKept && trinketEmpty;
    }

    private static string Short(string path) =>
        string.IsNullOrEmpty(path) ? "" : path[(path.LastIndexOf('/') + 1)..];

    private bool? StageTrinketsExist(int tick)
    {
        using var directory = DirAccess.Open("res://resources/gear");
        if (directory == null)
        {
            GD.PushError("  cannot open res://resources/gear");
            return false;
        }

        var found = new System.Collections.Generic.List<string>();
        bool ok = true;

        foreach (string file in directory.GetFiles())
        {
            if (!file.EndsWith(".tres") && !file.EndsWith(".tres.remap"))
                continue;

            string path = $"res://resources/gear/{file.Replace(".remap", "")}";
            var piece = GD.Load<GearResource>(path);
            if (piece == null || piece.Slot != GearSlot.Trinket)
                continue;

            found.Add(piece.GearName);

            // An apostrophe in a name becomes an apostrophe in a path, which
            // works until something quotes it — a shell, a build script, a
            // `.tres` reference. Cheaper to forbid than to find later.
            if (!path.Contains('\''))
                continue;

            GD.PushError($"  {piece.GearName} has an apostrophe in its path: {path}");
            ok = false;
        }

        GD.Print($"  {found.Count} trinkets: {string.Join(", ", found)}");

        if (found.Count < 4)
        {
            GD.PushError($"  only {found.Count} trinkets — the slot needs a real choice in it");
            ok = false;
        }

        return ok;
    }

    private bool? StateTrinketGrantsKit(int tick)
    {
        var whetstone = GD.Load<GearResource>("res://resources/gear/whetstone.tres");
        if (whetstone == null || _player == null || _meta == null)
        {
            GD.PushError("  whetstone.tres did not load, or the scene is missing a player");
            return false;
        }

        if (tick == 1)
        {
            _player.Mods.Reset();
            _meta.Profile.Grant(whetstone.ResourcePath);
            _meta.Profile.EquippedGear[(int)GearSlot.Trinket] = whetstone.ResourcePath;
            _meta.ReapplyGearForTesting();
            return null;
        }

        int blades = _player.Mods.OrbitBlades;
        int cap = _growth?.CapFor(GrowthOption.Orbit) ?? 0;

        GD.Print($"  wearing a {whetstone.GearName}: {blades} blade(s) at the start, " +
                 $"orbit caps at {cap}");

        bool granted = blades == whetstone.OrbitBonus;
        bool raised = cap == whetstone.OrbitUpgradeCap;

        if (!granted)
            GD.PushError($"  {blades} blades from a trinket granting {whetstone.OrbitBonus}");
        if (!raised)
            GD.PushError($"  orbit caps at {cap}; the trinket says {whetstone.OrbitUpgradeCap}");

        return granted && raised;
    }

    /// Gear chill and card chill have to be the same curve.
    ///
    /// Both compound, so two sources approach a limit instead of crossing it. If
    /// gear summed and the card compounded, a Frost Cell plus three picks could
    /// pass 1.0 and stop the horde where it stood — and the run that produced it
    /// would be unreproducible, because it needs a specific trinket and a
    /// specific set of draws.
    private bool? StageChillCompounds(int tick)
    {
        var frost = GD.Load<GearResource>("res://resources/gear/frost_cell.tres");
        if (frost == null || _player == null || _growth == null)
        {
            GD.PushError("  frost_cell.tres did not load");
            return false;
        }

        _player.Mods.Reset();
        _player.ApplyGearKit(0, 0, 0.0f, frost.ChillBonus);
        float fromGear = _player.Mods.Chill;

        // Then every card the deck would ever offer, on top.
        for (int i = 0; i < 12; i++)
            _growth.GrantForTesting(GrowthOption.Chill);

        float total = _player.Mods.Chill;

        GD.Print($"  {frost.GearName} alone: {fromGear:P1}; with twelve chill picks on top: {total:P1}");

        bool gearWorks = Mathf.IsEqualApprox(fromGear, frost.ChillBonus);
        bool stillUnderOne = total < 0.999f;

        if (!gearWorks)
            GD.PushError($"  the trinket granted {fromGear:P1} against {frost.ChillBonus:P1}");
        if (!stillUnderOne)
            GD.PushError($"  gear and cards together reach {total:P1} — the horde stops dead");

        return gearWorks && stillUnderOne;
    }
}
