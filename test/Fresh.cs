using Godot;

/// Detaches a probe's scene from the developer's save file.
///
/// **A probe that reads `user://profile.json` is a probe that measures whoever
/// ran it.** `MetaManager` loads the real profile unless told otherwise, and the
/// profile carries equipped gear, owned weapons, unlocks, credits and a stash —
/// so a player with a stitched vest and trekking pack is a *different player*
/// from the one the assertions were written against.
///
/// This is not hypothetical and it is not new. `EnemyTypeProbe` already carries a
/// comment about the day a single point of armour from a starting loadout turned
/// a 14:6 contact ratio into 13:5 and was read as a broken damage table, with
/// `GrowthProbe` tripped by the same point on the same day. It happened again,
/// worse, the first time the balance sweep was run properly: thirty-six full runs
/// banked credits, bought gear and unlocked weapons into the real save, and the
/// next sweep reported contact damage twenty per cent low on a game nobody had
/// touched.
///
/// The fix is one line per probe, before the scene enters the tree — `_Ready` is
/// where `MetaManager` loads, so afterwards is too late. Auditing that turned up
/// `MetaProbe` setting `Ephemeral` *after* `AddChild`, which had never done
/// anything at all.
///
/// **Five probes deliberately do not call this**: `ShopProbe`, `MetaProbe`,
/// `FirstRunProbe`, `BaseLoopProbe` and `ShelterProbe` write a profile to disk
/// and then load a run to prove it arrives, which is the whole subject. Handing
/// them an empty profile makes them test nothing, and `ShopProbe`'s "bought gear
/// reaches the run" failed within a minute of the helper being applied
/// everywhere. They are the reason this is a call rather than a default.
public static class Fresh
{
    /// Gives `scene` a profile with nothing in it.
    ///
    /// Silent when the scene has no `MetaManager`: several probes build their own
    /// subtree rather than instantiating `Main.tscn`, and requiring them all to
    /// know whether they have one would be a second thing to keep in step.
    public static void Profile(Node scene)
    {
        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;
    }
}
