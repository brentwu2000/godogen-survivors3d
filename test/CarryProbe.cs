using Godot;

/// Checks that a full backpack is a decision rather than a silent tax.
///
///   godot --headless --script test/CarryProbe.cs
///
/// The old behaviour looked correct from every angle except the player's: a
/// crate searched with a full bag was emptied, the overflow was destroyed, and
/// nothing anywhere said so. Carrying capacity was a number that quietly deleted
/// loot, so there was nothing to weigh and no reason to drop anything — which is
/// the opposite of what a carry limit is for.
public partial class CarryProbe : SceneTree
{
    private Player? _player;
    private LootContainer? _crate;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    private int _firstHaul;
    private int _waitingBulk;
    private int _waitingValue;
    private int _emptiedSignals;
    private int _finishedSignals;

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
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _player = scene.GetNodeOrNull<Player>("Player");

            foreach (Node child in scene.GetNodeOrNull("LootContainers")?.GetChildren()
                                   ?? new Godot.Collections.Array<Node>())
            {
                if (child is LootContainer crate)
                {
                    _crate = crate;
                    break;
                }
            }

            if (_player == null || _crate == null)
            {
                GD.PushError($"PROBE FAILED — player={_player != null} crate={_crate != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<Horde>("Horde")?.SetPhysicsProcess(false);
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);

            _crate.Emptied += (value, finished) =>
            {
                _emptiedSignals++;
                if (finished)
                    _finishedSignals++;
            };
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageWorstIsPerBulk, "the worst thing to carry is not the cheapest thing");
            case 1: return RunStage(StageFullBagLeavesItBehind, "a full bag leaves the rest in the crate");
            case 2: return RunStage(StageDroppingMakesRoom, "dropping makes room, and the crate still has it");
            case 3: return RunStage(StageNoReroll, "coming back does not re-roll the crate");
            case 4: return RunStage(StageCountedOnce, "the crate is counted once and valued every visit");
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

    /// Dropping and securing ask different questions of the same bag.
    ///
    /// Built from two invented items rather than two real ones, because every
    /// item in the game currently has a bulk of 1 — which makes "value per bulk"
    /// and "value" the same number and the test vacuous. The first version used
    /// `rifle_rounds` and `circuit_board`, passed, and proved nothing.
    ///
    /// The case that distinguishes them: a crate is worth more in total *and*
    /// less per unit of room. Securing should want it; dropping should want it
    /// gone. Anything that reads the raw value picks the wrong one.
    private bool? StageWorstIsPerBulk(int tick)
    {
        var crate = new ItemResource { ItemName = "Heavy Crate", Value = 100, Bulk = 20 };
        var trinket = new ItemResource { ItemName = "Small Trinket", Value = 40, Bulk = 2 };

        var bag = new Inventory(200);
        bag.TryAdd(crate, 1);
        bag.TryAdd(trinket, 1);

        int worst = bag.LeastValuableIndex();
        int best = bag.MostValuableIndex();

        GD.Print($"  {crate.ItemName} {crate.Value}/{crate.Bulk} = {crate.Value / (float)crate.Bulk:F1} per bulk, " +
                 $"{trinket.ItemName} {trinket.Value}/{trinket.Bulk} = {trinket.Value / (float)trinket.Bulk:F1}: " +
                 $"drop {bag.ItemAt(worst).ItemName}, secure {bag.ItemAt(best).ItemName}");

        // The heavy crate is the most valuable thing in the bag and the worst
        // thing to be carrying. Both at once, which is the whole distinction.
        bool dropsTheHeavyOne = bag.ItemAt(worst).ItemName == crate.ItemName;
        bool securesTheHeavyOne = bag.ItemAt(best).ItemName == crate.ItemName;

        if (!dropsTheHeavyOne)
            GD.PushError("  dropped the trinket — that is the cheapest thing, not the worst thing to carry");
        if (!securesTheHeavyOne)
            GD.PushError("  secured the trinket — securing goes by value, not by value per bulk");

        return dropsTheHeavyOne && securesTheHeavyOne;
    }

    private bool? StageFullBagLeavesItBehind(int tick)
    {
        if (tick == 1)
        {
            // A bag with almost no room, standing on a crate.
            _player!.Backpack.Clear();
            var brick = GD.Load<ItemResource>("res://resources/items/scrap_metal.tres");
            if (brick != null)
                _player.Backpack.TryAdd(brick, 999);

            // A crate holding far more than the bag can take, so the stages
            // after this one still have something left to work with. At the
            // default roll count the contents were barely more than a full
            // backpack, and two units of room emptied the whole thing the moment
            // the item table grew — which the next stage's guard caught.
            _crate!.RollCount = 14;
            _crate.SearchSeconds = 0.05f;
            _player.GlobalPosition = _crate.GlobalPosition;
            return null;
        }

        if (tick < 20)
            return null;

        _firstHaul = _player!.Backpack.TotalValue;
        _waitingBulk = _crate!.RemainingBulk;
        _waitingValue = _crate.RemainingValue;

        GD.Print($"  bag {_player.Backpack.UsedBulk}/{_player.Backpack.Capacity} full; " +
                 $"the crate still holds {_waitingBulk} bulk worth {_waitingValue}, " +
                 $"looted={_crate.Looted}");

        bool kept = _waitingBulk > 0;
        bool notFinished = !_crate.Looted;

        if (!kept)
            GD.PushError("  the crate is empty after a search with a full bag — the overflow was destroyed");
        if (!notFinished)
            GD.PushError("  the crate reports itself looted while it still holds something");

        return kept && notFinished;
    }

    private bool? StageDroppingMakesRoom(int tick)
    {
        if (tick == 1)
        {
            // Through the player's own verb, not by reaching into the inventory:
            // a drop that only works when called directly is a drop the player
            // cannot perform.
            //
            // Two units, not forty. Emptying the bag would empty the crate, and
            // the next stage would have nothing left to check for a re-roll —
            // which is exactly how the first version of this passed while testing
            // nothing.
            _player!.TryDropWorst();
            _player.TryDropWorst();
            return null;
        }

        if (tick < 20)
            return null;

        int nowWaiting = _crate!.RemainingBulk;

        GD.Print($"  after dropping: bag {_player!.Backpack.UsedBulk}/{_player.Backpack.Capacity}, " +
                 $"crate {_waitingBulk} -> {nowWaiting} bulk");

        bool moved = nowWaiting < _waitingBulk;
        bool stillHolding = nowWaiting > 0;

        if (!moved)
            GD.PushError($"  the crate still holds {nowWaiting} bulk — making room took nothing out of it");

        if (!stillHolding)
            GD.PushError("  two units of room emptied the whole crate — the next stage has nothing to test");

        return moved && stillHolding;
    }

    private bool? StageNoReroll(int tick)
    {
        // Whatever is left has to be the same items it was, not a fresh roll.
        // A crate that re-rolled would let a player with a full bag farm one
        // container until it produced the item they wanted.
        if (tick == 1)
        {
            // Read *before* the bag is cleared, or the transfer happens on the
            // same tick and the reading is already zero.
            _leftInCrate = _crate!.RemainingValue;
            _player!.Backpack.Clear();
            return null;
        }

        if (tick < 20)
            return null;

        int before = _leftInCrate;

        // Emptied now, so what came out is what was left.
        int intoBag = _player!.Backpack.TotalValue;

        GD.Print($"  {before} was left in the crate; {intoBag} arrived in an empty bag " +
                 $"and {_crate.RemainingValue} stayed behind");

        // Conservation, not emptying. What came out plus what is still in there
        // has to equal what was there — which holds whether or not the bag had
        // room for all of it, and is a stronger statement about re-rolling than
        // "the crate is now empty" ever was. A re-roll changes the total.
        int stillThere = _crate.RemainingValue;
        int accounted = intoBag + stillThere;

        bool anything = before > 0;
        bool conserved = anything && Mathf.Abs(accounted - before) <= Mathf.Max(1, before / 50);

        if (!anything)
            GD.PushError("  the crate was already empty — this stage tested nothing");
        else if (!conserved)
        {
            GD.PushError($"  {intoBag} came out and {stillThere} remains, of a crate that held " +
                         $"{before} — was it re-rolled?");
        }

        return conserved;
    }

    private int _leftInCrate;

    private bool? StageCountedOnce(int tick)
    {
        // Drain it. The crate holds more than a backpack, so finishing it takes
        // several trips — which is the behaviour under test, and means the
        // "finished" signal cannot be observed without actually doing it.
        if (!_crate!.Looted && tick < 400)
        {
            _player!.Backpack.Clear();
            return null;
        }

        GD.Print($"  emptied over several trips: {_emptiedSignals} payouts, " +
                 $"finished {_finishedSignals} time(s), crate looted={_crate.Looted}");

        // Several payouts, one completion. The run log values every visit and
        // counts the crate once, and with a single-argument signal it could do
        // only one of the two.
        if (!_crate.Looted)
            GD.PushError("  the crate never finished — the drain loop ran out of ticks");

        bool paidSeveralTimes = _emptiedSignals >= 2;
        bool finishedOnce = _finishedSignals == 1;

        if (!paidSeveralTimes)
            GD.PushError($"  only {_emptiedSignals} payout — the full-bag path never ran");
        if (!finishedOnce)
            GD.PushError($"  reported finished {_finishedSignals} times, expected exactly one");

        return paidSeveralTimes && finishedOnce && _crate.Looted;
    }
}
