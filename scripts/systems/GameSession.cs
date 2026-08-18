/// The one thing that has to survive a scene change.
///
/// A run started from the base screen returns to it when it ends; a run started
/// by a probe or a capture script must not, because those own the tree and would
/// have it swapped out from under them mid-measurement. There is nowhere else to
/// put that bit — the scene being launched does not exist yet when the base
/// screen decides to launch it.
public static class GameSession
{
    public static bool LaunchedFromBase { get; set; }

    /// Which biome the run is in, as an index into `BiomeBook.All`.
    ///
    /// Here for the same reason as the flag above: the level generator has to
    /// know before it runs, and it runs in a scene that does not exist yet when
    /// the base screen decides. Passing it through a node in the new scene would
    /// mean the answer depends on which node is ready first, which is the bug
    /// this project has now written down four times.
    public static int Biome { get; set; }
}
