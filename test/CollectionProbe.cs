using Godot;

/// Checks the curiosity sets.
///
///   godot --headless --script test/CollectionProbe.cs
///
/// The load-bearing claim is that **selling a curiosity does not lose it**. The
/// record is written when the piece reaches the stash, at the door rather than at
/// the locker, because selling the stash for credits is the ordinary thing to do
/// with it. A collection that quietly forfeited a set for doing the obvious thing
/// would be a trap dressed as content, and the player would find out about it two
/// hours in with no way to undo it.
public partial class CollectionProbe : SceneTree
{
    private bool _failed;

    public override void _Initialize()
    {
        Stage("every piece named by a set exists as an item", PiecesExist);
        Stage("a set pays once, and only when it is complete", PaysOnceWhenComplete);
        Stage("selling the stash does not lose the set", SellingKeepsIt);
        Stage("a dropped cache pays supplies, not collectibles", CachesCarryNone);
        Stage("the collection survives a save and a load", SurvivesRoundTrip);

        GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
        Quit(_failed ? 1 : 0);
    }

    private void Stage(string label, System.Func<bool> stage)
    {
        bool ok;
        try
        {
            ok = stage();
        }
        catch (System.Exception e)
        {
            GD.PushError($"  {label} threw: {e.Message}");
            ok = false;
        }

        GD.Print($"{label}: {(ok ? "ok" : "FAILED")}");
        _failed |= !ok;
    }

    /// A set naming a piece that does not exist is a set nobody can finish.
    private bool PiecesExist()
    {
        bool ok = true;
        int pieces = 0;

        foreach (CollectionBook.Set set in CollectionBook.All)
        {
            foreach (string piece in set.Pieces)
            {
                pieces++;
                string path = $"res://resources/items/{piece.ToLower().Replace(' ', '_')}.tres";
                var item = GD.Load<ItemResource>(path);

                if (item == null)
                {
                    GD.PushError($"  {piece} has no item at {path} — run BuildItems.cs");
                    ok = false;
                    continue;
                }

                // Two bulk, the size of a medkit. A set piece that cost nothing to
                // carry would never be a decision, and the whole carry phase
                // exists to make bulk one.
                if (item.Bulk < 2)
                {
                    GD.PushError($"  {piece} is {item.Bulk} bulk — carrying it has to cost something");
                    ok = false;
                }

                // And the name has to match, or `SetOf` will never find it.
                if (item.ItemName != piece)
                {
                    GD.PushError($"  {path} is named '{item.ItemName}', the set says '{piece}'");
                    ok = false;
                }
            }
        }

        GD.Print($"  {CollectionBook.All.Length} sets, {pieces} pieces, all present at 2+ bulk");
        return ok;
    }

    private bool PaysOnceWhenComplete()
    {
        var profile = new Profile { Credits = 0 };
        CollectionBook.Set set = CollectionBook.All[0];

        // Two of three: nothing yet.
        profile.Record(set.Pieces[0]);
        profile.Record(set.Pieces[1]);
        int partial = CollectionBook.Claim(profile);

        profile.Record(set.Pieces[2]);
        int completed = CollectionBook.Claim(profile);

        // And again, which must pay nothing.
        int again = CollectionBook.Claim(profile);

        GD.Print($"  {set.Name}: 2 of 3 paid {partial}, the third paid {completed}, " +
                 $"asking again paid {again}");

        bool silentUntilDone = partial == 0;
        bool paid = completed == set.Bounty;
        bool once = again == 0;

        if (!silentUntilDone)
            GD.PushError($"  an incomplete set paid {partial}");
        if (!paid)
            GD.PushError($"  a complete set paid {completed}, the book says {set.Bounty}");
        if (!once)
            GD.PushError($"  the set paid {again} a second time");

        return silentUntilDone && paid && once;
    }

    /// The one that matters.
    private bool SellingKeepsIt()
    {
        var profile = new Profile { Credits = 0 };
        CollectionBook.Set set = CollectionBook.All[1];

        foreach (string piece in set.Pieces)
        {
            profile.AddToStash(piece, 1);
            profile.Record(piece);
        }

        int bounty = CollectionBook.Claim(profile);

        // Now sell everything, the way the locker does.
        profile.Stash.Clear();

        bool stillComplete = CollectionBook.Complete(profile, 1);
        int foundAfter = CollectionBook.Found(profile, 1);

        GD.Print($"  {set.Name} completed for {bounty}, then the whole stash sold: " +
                 $"{foundAfter}/{set.Pieces.Length} still recorded, complete = {stillComplete}");

        if (!stillComplete)
        {
            GD.PushError("  selling the stash lost the set — the record is being kept at the locker " +
                         "rather than at the door");
        }

        return stillComplete && bounty == set.Bounty;
    }

    /// A cache is a payout. It owes ammunition, not two-bulk keepsakes.
    private bool CachesCarryNone()
    {
        // Different positions, because the roll is seeded from where the crate
        // stands — two at the origin would produce identical contents and the
        // comparison would be between a table and itself.
        var cache = new LootContainer
        {
            Curiosities = false, RollCount = 40, RarityBias = 3.2f,
            Position = new Vector3(11.0f, 0.0f, -7.0f),
        };

        var ordinary = new LootContainer
        {
            RollCount = 40, RarityBias = 3.2f,
            Position = new Vector3(-4.0f, 0.0f, 19.0f),
        };

        // Rolled into a bag with room for everything, so nothing is refused for
        // bulk and the contents are the contents.
        var fromCache = new Inventory(4000);
        var fromCrate = new Inventory(4000);

        cache.RollIntoForTesting(fromCache);
        ordinary.RollIntoForTesting(fromCrate);

        int inCache = CountCuriosities(fromCache);
        int inCrate = CountCuriosities(fromCrate);

        GD.Print($"  40 rolls at bias 3.2: a dropped cache produced {inCache} curiosities, " +
                 $"a placed crate {inCrate}");

        bool cacheClean = inCache == 0;

        // And the ordinary crate has to still produce them, or the flag has
        // switched them off everywhere and the sets are unfinishable.
        bool crateHasThem = inCrate > 0;

        if (!cacheClean)
            GD.PushError($"  a payout handed over {inCache} collectibles — that is a reward that " +
                         "fills the backpack the player needs for the next five minutes");

        if (!crateHasThem)
            GD.PushError("  a placed crate produced none either — the sets cannot be finished");

        return cacheClean && crateHasThem;
    }

    private static int CountCuriosities(Inventory bag)
    {
        int count = 0;
        for (int i = 0; i < bag.EntryCount; i++)
        {
            if (CollectionBook.SetOf(bag.ItemAt(i).ItemName) >= 0)
                count += bag.CountAt(i);
        }

        return count;
    }

    private bool SurvivesRoundTrip()
    {
        var before = new Profile();
        before.Record(CollectionBook.All[0].Pieces[0]);
        before.Record(CollectionBook.All[0].Pieces[1]);
        before.ClaimedSets.Add("Someone's Life");

        // `ToJson` already returns text. Wrapping it in `Json.Stringify` produces
        // a JSON *string containing* JSON, which parses to a string rather than a
        // dictionary and comes back as null.
        Profile? after = Profile.FromJson(before.ToJson());
        if (after == null)
        {
            GD.PushError("  the profile did not survive being written and read");
            return false;
        }

        GD.Print($"  {after.Collected.Count} pieces and {after.ClaimedSets.Count} claimed set(s) " +
                 "came back from JSON");

        bool pieces = after.Collected.Count == before.Collected.Count;
        bool claimed = after.ClaimedSets.Contains("Someone's Life");

        if (!pieces)
            GD.PushError($"  {after.Collected.Count} pieces came back of {before.Collected.Count}");
        if (!claimed)
            GD.PushError("  the claimed set did not survive — the bounty would be paid twice");

        return pieces && claimed;
    }
}
