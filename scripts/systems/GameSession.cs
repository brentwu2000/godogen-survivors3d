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
}
