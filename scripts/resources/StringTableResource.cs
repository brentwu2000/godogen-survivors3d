using Godot;

/// Every user-visible string in the game, in every language it has.
///
/// **A generated resource rather than Godot's own `.csv` → `.translation`
/// import, and the reason is the one the audio already learned.** `BuildAudio`
/// writes `AudioStreamWav` resources directly instead of shipping `.wav` files
/// because a `.wav` goes through the importer, whose loop flag lives in a
/// generated `.import` file the tool does not own — and the horde ambience is
/// ruined if that flag silently comes back off. A translation import has fewer
/// settings to lose, but it has the same shape: a build artefact whose contents
/// depend on state nothing in this repository writes.
///
/// So the pipeline here is the one every other asset uses. `art-src/ui/strings.csv`
/// is the source a translator edits, `scripts/tools/BuildStrings.cs` renders it,
/// and `resources/strings.tres` is what ships. `art-src/` is excluded from the
/// export, so the CSV never reaches a player and never has to be parsed at
/// runtime.
///
/// Three parallel arrays rather than a dictionary of dictionaries. Godot's
/// export of nested collections is the kind of thing that serialises today and
/// drops a level next version, and `Values` being row-major over
/// `Keys` × `Locales` is a shape this project already uses everywhere it writes
/// a buffer.
public partial class StringTableResource : Resource
{
    /// What the code asks for. Sorted by the tool, so a diff of this file is a
    /// diff of the translation rather than of the CSV's row order.
    [Export] public string[] Keys { get; set; } = System.Array.Empty<string>();

    /// The locale codes, in column order. `en` is index 0 and is the fallback
    /// for nothing — see `Strings`, which refuses rather than falls back.
    [Export] public string[] Locales { get; set; } = System.Array.Empty<string>();

    /// `Keys.Length * Locales.Length` entries, row-major: key `k` in locale `l`
    /// is at `k * Locales.Length + l`.
    [Export] public string[] Values { get; set; } = System.Array.Empty<string>();

    /// Whether the arrays agree with each other.
    ///
    /// Checked at load rather than trusted, because the failure of a row-major
    /// array whose stride is wrong is not an exception — it is every string on
    /// every screen being the wrong string, which reads as a bad translation.
    public bool IsWellFormed =>
        Locales.Length > 0 && Values.Length == Keys.Length * Locales.Length;
}
