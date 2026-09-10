using Godot;

/// Drives the whole outer loop once: base screen, launch, die, come back.
///
///   godot --headless --script test/BaseLoopProbe.cs
///
/// Every other probe holds one scene still and measures inside it. This is the
/// only one that crosses between them, which is where a loop can be complete in
/// both halves and still not close — the run that never returns and the base
/// that never launches both look fine from inside.
///
/// The profile on disk is backed up and restored.
public partial class BaseLoopProbe : SceneTree
{
    private const string ProfilePath = "user://profile.json";

    /// Long enough to cover the meta layer's read-the-banner delay.
    private const int ReturnTimeoutTicks = 60 * 8;

    private string? _backup;
    private int _stage;
    private int _tick;
    private bool _failed;

    public override void _Initialize()
    {
        _backup = FileAccess.FileExists(ProfilePath)
            ? FileAccess.GetFileAsString(ProfilePath)
            : null;

        // A profile with something to lose, so the return trip has a result to
        // report rather than a row of zeroes.
        // `HasSeenBase` set, because this probe is testing the returning player's
        // loop. Without it the base screen does what it should for a new player —
        // launches straight into a run — and the probe never gets to press a key
        // on the screen it exists to drive.
        var profile = new Profile { Credits = 500, HasSeenBase = true };
        using (var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write))
            file?.StoreString(profile.ToJson());

        EnterBase();
    }

    private void EnterBase()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Base.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Base.tscn — run scenes/BuildBase.cs first");
            Quit(1);
            return;
        }

        GetRoot().AddChild(scene);

        // ChangeSceneToFile swaps whatever CurrentScene is, and a --script tree
        // has none until it is told. Without this the launch quietly does
        // nothing and the probe times out with no clue why.
        CurrentScene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        _tick++;

        switch (_stage)
        {
            case 0: return Step(StageNoKeysOnScreen, "the base screen resolves every key it draws");
            case 1: return Step(StageLaunch, "the base screen launches a run");
            case 2: return Step(StageDie, "the run ends");
            case 3: return Step(StageDebrief, "the debrief reports it and waits");
            case 4: return Step(StageReturn, "and hands control back to the base");
            default:
                Restore();
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool Step(System.Func<int, bool?> stage, string label)
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

    /// Nothing on the base screen is a key that somebody forgot to resolve.
    ///
    /// **The failure this catches leaves no other trace.** A field that holds a
    /// key and is interpolated rather than resolved draws the key: the screen is
    /// present, non-empty, padded correctly and fully drawable, and it says
    /// "playing as RIN — character.rin.blurb". `StringProbe` cannot see it — the
    /// table is fine. `FontProbe` cannot see it — every character is ASCII.
    /// `Get` is never called, so there is no `«key»` to notice.
    ///
    /// It happened: `CharacterResource.Blurb` became a key when the roster was
    /// extracted, the roster screen was updated and this line was not, and the
    /// base screen drew the key for the whole time in between, in English as well
    /// as in Chinese. This is the assertion that would have said so on the first
    /// sweep after.
    ///
    /// The base screen is the right place for it because it is the page that
    /// draws the most *content* — a survivor, a biome, a shop entry, a contract
    /// — and content is what `Build*.cs` turns into keys one field at a time.
    /// The fittings, in the order the room lays them out.
    private static readonly Fitting[] Pages =
    {
        Fitting.Armoury, Fitting.Locker, Fitting.Records,
        Fitting.Board, Fitting.Map, Fitting.Console, Fitting.Gate,
    };

    /// Ticks each page is given: one to be stood on, one for the shelter to
    /// notice, one for the screen to draw from the new focus.
    private const int PageTicks = 4;

    private bool _pagesClean = true;

    private bool? StageNoKeysOnScreen(int tick)
    {
        var shelter = CurrentScene?.GetNodeOrNull<Shelter>("Shelter");
        var player = CurrentScene?.GetNodeOrNull<Player>("Player");

        if (shelter == null || player == null)
        {
            GD.PushError($"  no shelter={shelter == null} or player={player == null} in the base");
            return false;
        }

        // **Every page, not whichever one happens to be up.** The first version
        // of this stage read the screen where the probe found it — standing at
        // the gate — and passed with the bug it was written for still in the
        // code, because the survivor's blurb is on the *armoury* page and the
        // gate page does not draw it. A leak check that reads one page of seven
        // is a check that reports on the page with the least content in it.
        //
        // Teleported rather than walked. Walking the room is `ShelterProbe`'s
        // stage; what this needs is only that the focus changes and the screen
        // redraws from it.
        // `_tick` is incremented before the stage runs, so the first tick a stage
        // sees is 1 and not 0. Counting pages off it directly skipped the first
        // teleport and then checked the armoury page while standing at the gate.
        int t = tick - 1;
        int page = t / PageTicks;
        if (page >= Pages.Length)
            return _pagesClean;

        int within = t % PageTicks;

        if (within == 0)
        {
            player.GlobalPosition = shelter.Stations[Pages[page]];
            return null;
        }

        if (within < PageTicks - 1)
            return null;

        if (shelter.Focus != Pages[page])
        {
            GD.PushError($"  meant to be standing on {Pages[page]} and the shelter reads {shelter.Focus}");
            _pagesClean = false;
            return null;
        }

        _pagesClean &= NothingLeaks(CurrentScene, $"the {Pages[page]} page");
        return null;
    }

    /// Every `Label` under `root`, checked against the key table.
    ///
    /// Walked rather than named, so a page added to either panel later is
    /// covered without anybody remembering to add it here.
    private static bool NothingLeaks(Node? root, string what)
    {
        if (root == null)
        {
            GD.PushError($"  no {what} to read");
            return false;
        }

        if (!Strings.Ready())
        {
            GD.PushError("  the string table did not load");
            return false;
        }

        bool ok = true;
        int read = 0;

        foreach (Node node in Walk(root))
        {
            if (node is not Label label || label.Text.Length == 0)
                continue;

            read++;
            string leaked = Strings.Leak(label.Text);

            if (leaked.Length > 0)
            {
                GD.PushError($"  {what}: '{label.Name}' draws the key '{leaked}' "
                           + "rather than what it stands for");
                ok = false;
            }
        }

        if (read == 0)
        {
            GD.PushError($"  {what} drew no text at all, so this stage measured nothing");
            ok = false;
        }
        else if (!ok)
            GD.Print($"  {read} label(s) read on {what}");

        return ok;
    }

    private static System.Collections.Generic.IEnumerable<Node> Walk(Node root)
    {
        yield return root;

        foreach (Node child in root.GetChildren())
        {
            foreach (Node deeper in Walk(child))
                yield return deeper;
        }
    }

    private bool? StageLaunch(int tick)
    {
        // Stand at the gate, then press the one verb key.
        //
        // This used to press `menu_launch`, which was a key that launched a run
        // from anywhere in the base. There is no such key now — the shelter's
        // whole design is that the verb is `[E]` and what it applies to is
        // wherever the player is standing, so a probe that launched without
        // walking to the gate would be testing a path the game does not have.
        //
        // The player is teleported rather than driven. Walking there is
        // `ShelterProbe`'s job; this one is about whether the loop closes.
        if (tick == 2)
        {
            var shelter = CurrentScene?.GetNodeOrNull<Shelter>("Shelter");
            var player = CurrentScene?.GetNodeOrNull<Player>("Player");

            if (shelter == null || player == null)
            {
                GD.PushError($"  no shelter={shelter == null} or player={player == null} in the base");
                return false;
            }

            player.GlobalPosition = shelter.Stations[Fitting.Gate];
            return null;
        }

        // A physics tick has to pass for the shelter to notice, and the screen
        // reads the focus rather than being told it.
        if (tick == 4)
        {
            var shelter = CurrentScene?.GetNodeOrNull<Shelter>("Shelter");
            GD.Print($"  standing at {shelter?.Focus}");

            if (shelter?.Focus != Fitting.Gate)
            {
                GD.PushError($"  standing on the gate reads as {shelter?.Focus}");
                return false;
            }

            Input.ActionPress("interact");
            return null;
        }

        if (tick == 5)
        {
            Input.ActionRelease("interact");
            return null;
        }

        if (tick < 32)
            return null;

        var director = CurrentScene?.GetNodeOrNull<RunDirector>("RunDirector");
        GD.Print($"  now in {CurrentScene?.Name}, run director present = {director != null}");
        return director != null;
    }

    private bool? StageDie(int tick)
    {
        var player = CurrentScene?.GetNodeOrNull<Player>("Player");
        if (player == null)
            return false;

        if (tick == 2)
        {
            player.TakeDamage(99999.0f);
            return null;
        }

        if (tick < 10)
            return null;

        var director = CurrentScene?.GetNodeOrNull<RunDirector>("RunDirector");
        GD.Print($"  run state {director?.State}");
        return director?.State == RunState.Died;
    }

    /// The report has to appear, has to say something, and has to still be there
    /// a second later.
    ///
    /// That last part is the assertion that matters. The screen it replaced was a
    /// three and a half second timer, and anything that dismisses itself is
    /// something the player learns to stop reading — so a debrief that vanished
    /// on its own would pass a test for "appeared" while failing at the only job
    /// it has.
    private bool? StageDebrief(int tick)
    {
        var debrief = CurrentScene?.GetNodeOrNull<DebriefScreen>("Debrief");
        if (debrief == null)
        {
            GD.Print("  no Debrief node in the run scene");
            return false;
        }

        if (tick < 90)
        {
            _debriefStayed &= debrief.Visible || tick < 5;
            return null;
        }

        var meta = CurrentScene?.GetNodeOrNull<MetaManager>("MetaManager");
        RunRecord? run = meta?.LastRun;

        GD.Print($"  debrief visible after 1.5s = {debrief.Visible} (never blinked = {_debriefStayed}); " +
                 $"record says {run?.Outcome} at {run?.Seconds:F1}s, banked {run?.Banked}");

        // The same key check the base screen gets, on the other page that draws
        // content. The debrief names weapons, items, enemy variants, contracts,
        // unlocks and collection pieces — six sources, every one of which is a
        // literal today and a key the day `Build*.cs` extracts it.
        bool clean = NothingLeaks(debrief, "the debrief");

        // Then dismiss it the way a player would.
        Input.ActionPress("ui_accept");
        return clean && debrief.Visible && _debriefStayed && run != null && run.Outcome == RunState.Died;
    }

    private bool _debriefStayed = true;

    private bool? StageReturn(int tick)
    {
        if (tick == 2)
        {
            Input.ActionRelease("ui_accept");
            return null;
        }

        // The shelter is what makes a scene the base.
        //
        // This used to be `CurrentScene is Control`, with a comment explaining
        // that the base screen is a Control and the run is a Node3D. Both are
        // Node3D now — the base is a room — so that test quietly became "is this
        // scene not the base", and the stage failed while the loop it checks was
        // working perfectly.
        if (CurrentScene?.GetNodeOrNull<Shelter>("Shelter") != null)
        {
            Profile after = SaveSystem.Load();
            GD.Print($"  back at {CurrentScene.Name} after {tick / 60.0f:F1}s, " +
                     $"profile says {after.RunsLost} lost");
            return after.RunsLost == 1;
        }

        if (tick < ReturnTimeoutTicks)
            return null;

        GD.Print($"  still in {CurrentScene?.Name} after {tick / 60.0f:F1}s");
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
}
