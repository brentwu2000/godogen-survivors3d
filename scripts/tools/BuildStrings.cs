using Godot;

/// Renders `art-src/ui/strings.csv` into `resources/strings.tres`.
///
///   dotnet build && godot --headless --script scripts/tools/BuildStrings.cs
///
/// The CSV is the thing a translator edits and it never ships: `art-src/` is
/// excluded from the export, so what reaches a player is the resource. Same shape
/// as every other asset here — `BuildAudio` synthesises the sounds, `BuildWeapons`
/// writes the weapon table, and nothing under `resources/` is edited by hand.
///
/// **It refuses more than it accepts, and each refusal is a screen that would
/// otherwise be wrong in a way nobody screenshots.** A duplicate key silently
/// wins or loses depending on dictionary order; a row with the wrong number of
/// columns shifts every locale after it by one; an empty cell is a string the
/// player sees as a hole. All three stop the build.
public partial class BuildStrings : SceneTree
{
    private const string SourcePath = "res://art-src/ui/strings.csv";
    private const string OutputPath = "res://resources/strings.tres";

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        using var file = FileAccess.Open(SourcePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Cannot read {SourcePath}: {FileAccess.GetOpenError()}");
            return false;
        }

        var rows = new System.Collections.Generic.List<string[]>();
        int line = 0;

        while (!file.EofReached())
        {
            // Godot's own CSV reader, so quoting and embedded commas behave the
            // way a translator's spreadsheet writes them rather than the way a
            // hand-rolled `Split(',')` would.
            string[] cells = file.GetCsvLine();
            line++;

            // A trailing newline yields one empty cell, which is not a row.
            if (cells.Length == 0 || (cells.Length == 1 && cells[0].Length == 0))
                continue;

            rows.Add(cells);
        }

        if (rows.Count < 2)
        {
            GD.PushError($"{SourcePath} has a header and no rows");
            return false;
        }

        string[] header = rows[0];
        if (header.Length < 2 || header[0] != "key")
        {
            GD.PushError($"{SourcePath} must start with a 'key' column and at least one locale");
            return false;
        }

        var locales = new string[header.Length - 1];
        System.Array.Copy(header, 1, locales, 0, locales.Length);

        if (System.Array.IndexOf(locales, "en") != 0)
        {
            GD.PushError($"{SourcePath}: 'en' must be the first locale column — it is the "
                       + "one the code is written in and the one a reviewer diffs against");
            return false;
        }

        var keys = new System.Collections.Generic.List<string>();
        var values = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        bool failed = false;

        for (int i = 1; i < rows.Count; i++)
        {
            string[] cells = rows[i];
            string key = cells[0];

            if (key.Length == 0 || key.StartsWith('#'))
                continue;

            if (!seen.Add(key))
            {
                GD.PushError($"{SourcePath} line {i + 1}: '{key}' appears twice");
                failed = true;
                continue;
            }

            if (cells.Length != header.Length)
            {
                GD.PushError($"{SourcePath} line {i + 1}: '{key}' has {cells.Length} columns "
                           + $"against the header's {header.Length} — an unquoted comma shifts "
                           + "every locale after it");
                failed = true;
                continue;
            }

            for (int l = 0; l < locales.Length; l++)
            {
                if (cells[l + 1].Length == 0)
                {
                    GD.PushError($"{SourcePath} line {i + 1}: '{key}' has no {locales[l]} value");
                    failed = true;
                }
            }

            keys.Add(key);
            for (int l = 0; l < locales.Length; l++)
                values.Add(cells[l + 1]);
        }

        if (failed)
            return false;

        if (keys.Count == 0)
        {
            GD.PushError($"{SourcePath} declares no keys");
            return false;
        }

        var table = new StringTableResource
        {
            Keys = keys.ToArray(),
            Locales = locales,
            Values = values.ToArray(),
        };

        Error err = ResourceSaver.Save(table, OutputPath);
        if (err != Error.Ok)
        {
            GD.PushError($"Save failed for {OutputPath}: {err}");
            return false;
        }

        GD.Print($"Saved {OutputPath}: {keys.Count} keys x {locales.Length} locales "
               + $"({string.Join(", ", locales)})");
        return true;
    }
}
