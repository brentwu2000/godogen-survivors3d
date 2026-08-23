using Godot;

/// The guard every capture script in this folder needs and none of them had.
///
/// Five scripts here write a PNG of the root viewport, and all five carried a
/// comment saying "not headless — the null rendering driver has nothing to
/// capture". A comment is not a check, and running one headless does not
/// produce an error and a non-zero exit. It produces this:
///
///   `GetRoot().GetTexture()` has no image behind it, `GetImage()` throws, Godot
///   prints the exception and starts the next frame. The `return true` that is
///   the script's only way of quitting is never reached, so the process spins at
///   100% of a core, forever, printing nothing.
///
/// Which is exactly what happened. Four `ScaleProbe` processes were found alive
/// at once on this machine — started 00:09, 00:35, 02:12 and 11:01 across two
/// days by sweeps that ran them by mistake — holding four cores between them.
/// The oldest had burned 4,937 seconds of CPU. Every one of those sweeps looked
/// like it was still working, because a probe that has hung and a probe that is
/// slow are the same picture from outside.
public static class Display
{
    /// Quits with a diagnosis if there is nothing to draw on.
    ///
    /// Returns whether the caller may continue, so a `_Initialize` can bail on
    /// the same line it checks. `Quit()` schedules the exit rather than taking it
    /// immediately, so the caller must still return.
    public static bool Required(SceneTree tree, string what)
    {
        if (DisplayServer.GetName() != "headless")
            return true;

        GD.PushError($"{what} needs a display — run it without --headless. " +
                     "There is no viewport to capture here, and nothing it could usefully do.");
        tree.Quit(1);
        return false;
    }
}
