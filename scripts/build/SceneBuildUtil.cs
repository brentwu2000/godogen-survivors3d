using System;
using Godot;

/// Shared save path for the build-time scene builders under scenes/.
///
/// Every guard here exists because the matching failure is silent: it compiles,
/// it prints nothing, and only the saved .tscn is wrong.
public static class SceneBuildUtil
{
    /// Runs a builder body and guarantees the tree quits.
    ///
    /// An exception escaping _Initialize never reaches Quit(), and headless Godot
    /// has no window to close — so the build hangs forever instead of failing.
    /// Every builder should route through here.
    public static void Run(SceneTree tree, Func<bool> build)
    {
        int code = 1;
        try
        {
            code = build() ? 0 : 1;
        }
        catch (Exception e)
        {
            GD.PushError($"Builder threw: {e}");
        }
        tree.Quit(code);
    }

    /// A node with no Owner does not serialize. Instanced sub-scenes are the
    /// exception that matters: set Owner on the instance root, but never recurse
    /// past it, or a GLB's meshes get inlined as text and the .tscn passes 100MB.
    public static void SetOwnerRecursive(Node node, Node root)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = root;
            if (string.IsNullOrEmpty(child.SceneFilePath))
                SetOwnerRecursive(child, root);
        }
    }

    public static int CountNodes(Node node)
    {
        int count = 1;
        foreach (Node child in node.GetChildren())
            count += CountNodes(child);
        return count;
    }

    /// SetScript() disposes the C# wrapper, so a root that needs a script has to
    /// be built under a temp parent and re-fetched. Returns the live root.
    public static Node AttachScriptToRoot(Node root, string scriptPath)
    {
        var temp = new Node();
        temp.AddChild(root);
        root.SetScript(GD.Load<Script>(scriptPath));

        Node refetched = temp.GetChild(0);
        temp.RemoveChild(refetched);
        temp.Free();
        return refetched;
    }

    /// Packs and saves, refusing to write if the pack dropped nodes.
    public static bool PackAndSave(Node root, string path)
    {
        SetOwnerRecursive(root, root);
        int expected = CountNodes(root);

        var packed = new PackedScene();
        if (packed.Pack(root) != Error.Ok)
        {
            GD.PushError($"Pack failed: {path}");
            return false;
        }

        Node probe = packed.Instantiate();
        int got = CountNodes(probe);
        probe.Free();
        if (got < expected)
        {
            GD.PushError($"Nodes dropped packing {path}: expected {expected}, got {got}");
            return false;
        }

        Error err = ResourceSaver.Save(packed, path);
        if (err != Error.Ok)
        {
            GD.PushError($"Save failed: {path} ({err})");
            return false;
        }

        GD.Print($"Saved {path} ({expected} nodes)");
        return true;
    }
}
