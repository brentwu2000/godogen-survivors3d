using Godot;

/// Reads and writes the profile under user://.
///
/// Writes go to a temp file and are renamed over the real one. A crash midway
/// through a direct write leaves a truncated profile — the one file the player
/// cannot afford to lose.
public static class SaveSystem
{
    public const string ProfilePath = "user://profile.json";
    private const string TempPath = "user://profile.json.tmp";

    public static Profile Load()
    {
        if (!FileAccess.FileExists(ProfilePath))
            return new Profile();

        using FileAccess file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"SaveSystem: cannot read {ProfilePath} ({FileAccess.GetOpenError()}) — starting fresh");
            return new Profile();
        }

        Profile? profile = Profile.FromJson(file.GetAsText());
        if (profile != null)
            return profile;

        GD.PushWarning($"SaveSystem: {ProfilePath} is not a readable profile — starting fresh");
        return new Profile();
    }

    public static bool Save(Profile profile)
    {
        using (FileAccess file = FileAccess.Open(TempPath, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                GD.PushError($"SaveSystem: cannot write {TempPath} ({FileAccess.GetOpenError()})");
                return false;
            }

            file.StoreString(profile.ToJson());
        }

        using DirAccess dir = DirAccess.Open("user://");
        if (dir == null)
        {
            GD.PushError("SaveSystem: cannot open user:// to commit the profile");
            return false;
        }

        // Rename is the commit point: until it succeeds the old profile is still
        // whole on disk.
        Error err = dir.Rename(TempPath, ProfilePath);
        if (err == Error.Ok)
            return true;

        GD.PushError($"SaveSystem: could not commit profile ({err})");
        return false;
    }

    public static void Delete()
    {
        using DirAccess dir = DirAccess.Open("user://");
        if (dir != null && dir.FileExists(ProfilePath))
            dir.Remove(ProfilePath);
    }
}
