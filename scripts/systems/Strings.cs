using Godot;

/// The words, and how wide they are.
///
/// Two jobs that are one job here. Looking a string up is the ordinary half; the
/// other is that this game's screens are space-aligned text pages, and a Han
/// glyph occupies two monospace cells where a Latin one occupies one. Every
/// column in the game misaligns the moment the text is Chinese unless the padding
/// counts cells rather than characters — so `Pad` lives beside `Get` and the
/// screens use both or neither.
///
/// `test/FontProbe.cs` measured that ratio rather than assuming it: a monospace
/// CJK face came back at **exactly 2.00** Latin cells, which is what makes this
/// arithmetic instead of an approximation that drifts a pixel per column.
public static class Strings
{
    private const string TablePath = "res://resources/strings.tres";

    /// The locale everything is drawn in. Nothing outside `Use` writes it.
    public static string Locale { get; private set; } = "en";

    /// True once a command line has named a locale, after which nothing else may
    /// change it. A capture asked for a language and then got the profile's is a
    /// screenshot of the wrong thing.
    private static bool _forced;

    private static StringTableResource? _table;
    private static System.Collections.Generic.Dictionary<string, int>? _index;
    private static int _column;

    /// Loads the table if it is not loaded, and returns whether there is one.
    ///
    /// Lazy rather than an autoload. Every screen that draws text calls `Get`
    /// before it draws, so the first call does the work — and a probe that builds
    /// its own subtree without the scene never pays for a table it does not use.
    public static bool Ready()
    {
        if (_table != null)
            return true;

        var table = GD.Load<StringTableResource>(TablePath);
        if (table == null)
        {
            GD.PushError($"Strings: {TablePath} did not load — run scripts/tools/BuildStrings.cs");
            return false;
        }

        if (!table.IsWellFormed)
        {
            GD.PushError($"Strings: {TablePath} has {table.Keys.Length} keys, "
                       + $"{table.Locales.Length} locales and {table.Values.Length} values, "
                       + "which do not multiply — the table was written by something that "
                       + "disagrees with this file about its own shape");
            return false;
        }

        _table = table;
        _index = new System.Collections.Generic.Dictionary<string, int>(table.Keys.Length);
        for (int i = 0; i < table.Keys.Length; i++)
            _index[table.Keys[i]] = i;

        // `-- locale:zh_TW` on any script or capture, ahead of anything the
        // profile says.
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("locale:", System.StringComparison.Ordinal))
            {
                Locale = argument["locale:".Length..];
                _forced = true;
            }
        }

        SelectColumn(Locale);
        return true;
    }

    /// Switches language. Unknown locales are refused rather than approximated:
    /// `zh` is not `zh_TW` and guessing which Chinese somebody meant is how a
    /// player ends up reading the wrong one.
    public static bool Use(string locale)
    {
        if (_forced)
            return true;

        Locale = locale;

        if (!Ready())
            return false;

        return SelectColumn(locale);
    }

    /// The locale a save should be drawn in, given what it remembers.
    ///
    /// **Headless is English whatever the save says, and that is the rule that
    /// keeps the sweep honest.** Four probes read rendered strings — `HudProbe`,
    /// `DebriefProbe`, `ShopProbe` and `BaseLoopProbe` — and a probe that passes
    /// in one locale and fails in another is testing the translation rather than
    /// the game. Without this, the sweep would be red on a Chinese machine and
    /// green on a reviewer's, which is the worst of both. It is the same argument
    /// as `EffectDirector`'s hitstop: nobody is watching a headless run.
    ///
    /// A capture or a probe that wants the other language passes `locale:`, which
    /// wins over everything including this.
    public static string Resolve(string saved)
    {
        if (_forced)
            return Locale;

        if (DisplayServer.GetName() == "headless")
            return "en";

        // An empty string is a save that has never chosen — a file written before
        // the setting existed, or a fresh profile. That is the bootstrap case,
        // and it is answered from the system rather than by asking.
        return saved.Length > 0 ? saved : FromSystem();
    }

    /// Whatever comes after `locale` in the table's own column order, wrapping.
    ///
    /// The Console cycles rather than lists, because there are two and the list
    /// would be a menu for its own sake — the same call `CycleBiome` makes about
    /// five places, and the same note about where cycling stops being right.
    public static string Next(string locale)
    {
        if (!Ready() || _table == null || _table.Locales.Length == 0)
            return locale;

        int at = System.Array.IndexOf(_table.Locales, locale);
        return _table.Locales[(at + 1 + _table.Locales.Length) % _table.Locales.Length];
    }

    /// The language's own name, in its own language, which is the only way a
    /// player who cannot read the current one can find theirs.
    public static string NameOf(string locale) => Get($"locale.{locale}.name");

    private static bool SelectColumn(string locale)
    {
        if (_table == null)
            return false;

        int found = System.Array.IndexOf(_table.Locales, locale);
        if (found < 0)
        {
            GD.PushWarning($"Strings: no column for '{locale}', staying on "
                         + $"'{_table.Locales[_column]}'");
            return false;
        }

        _column = found;
        return true;
    }

    /// What the game's language should be on a save that has never chosen one.
    ///
    /// **The bootstrap problem, which is the easy one to miss: a player who
    /// cannot read English cannot find the room where the language lives.** So
    /// the language is never asked for — it is taken from the OS, and the setting
    /// exists to correct that rather than to establish it. The first screen a
    /// Chinese player sees is in Chinese.
    ///
    /// `zh_TW`, `zh_HK` and `zh_Hant` all mean the writing system this game has,
    /// and Godot reports the locale in more shapes than those three.
    public static string FromSystem()
    {
        string locale = OS.GetLocale();

        if (locale.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase)
            && (locale.Contains("TW") || locale.Contains("HK") || locale.Contains("MO")
                || locale.Contains("Hant")))
        {
            return "zh_TW";
        }

        return "en";
    }

    /// The string for a key, in the current locale.
    ///
    /// **A missing key is loud and is still legible.** It returns the key wrapped
    /// in guillemets rather than an empty string, because a screen with a hole in
    /// it is a screen somebody has to diff against a previous screenshot to
    /// notice, while `«ui.roster.header»` on the top line names the thing that is
    /// wrong. The error goes to the log as well, and `StringProbe` is what turns
    /// it into a build failure.
    public static string Get(string key)
    {
        if (!Ready() || _index == null || _table == null)
            return $"«{key}»";

        if (!_index.TryGetValue(key, out int row))
        {
            GD.PushError($"Strings: no key '{key}'");
            return $"«{key}»";
        }

        string value = _table.Values[row * _table.Locales.Length + _column];

        // An empty cell is a key somebody has not translated yet, and it is a
        // different failure from a key that does not exist. It is not filled in
        // from English: a screen that is half one language and half another looks
        // like a design decision, and this one is not.
        if (value.Length == 0)
        {
            GD.PushError($"Strings: '{key}' has no {_table.Locales[_column]} value");
            return $"«{key}»";
        }

        return value;
    }

    /// The string for a key, with its placeholders filled.
    ///
    /// Positional, because Chinese does not put the number where English does —
    /// which is the whole reason the literals could not simply be replaced in
    /// place. `{0}` in one column may be `{1}` in another and the call site does
    /// not change.
    public static string Get(string key, params object[] args)
    {
        string form = Get(key);

        try
        {
            return string.Format(form, args);
        }
        catch (System.FormatException)
        {
            // A translator writing `{0` or `{2}` where the code passes one
            // argument would otherwise throw on a screen rather than in a
            // measurement. `StringProbe` formats every key in every locale for
            // this reason; this is what happens if one gets past it.
            GD.PushError($"Strings: '{key}' in {Locale} does not accept {args.Length} argument(s)");
            return $"«{key}»";
        }
    }

    /// How many monospace cells a string occupies.
    ///
    /// The East Asian Wide and Fullwidth ranges count as two. This is not the
    /// full Unicode East_Asian_Width property — that is a table of several
    /// hundred ranges — but it is every range this game can produce, and a range
    /// it cannot produce cannot misalign a column.
    public static int Cells(string text)
    {
        int cells = 0;

        foreach (char c in text)
            cells += IsWide(c) ? 2 : 1;

        return cells;
    }

    /// Left-aligned in a field `cells` wide, measured in cells rather than in
    /// characters.
    ///
    /// The drop-in for `$"{name,-6}"`, which counts characters and is therefore
    /// wrong for every string this project is about to start drawing. Over-long
    /// text is not truncated: a name cut to fit is a name the player cannot read,
    /// and a column that shifts is a column somebody notices and fixes.
    public static string Pad(string text, int cells)
    {
        int width = Cells(text);
        return width >= cells ? text : text + new string(' ', cells - width);
    }

    /// Right-aligned, for the numbers.
    public static string PadLeft(string text, int cells)
    {
        int width = Cells(text);
        return width >= cells ? text : new string(' ', cells - width) + text;
    }

    private static bool IsWide(char c) =>
        // CJK Radicals through the end of the Unified Ideographs, which covers
        // every hanzi, every kana and the CJK punctuation the translation uses.
        (c >= 'ᄀ' && c <= 'ᅟ')     // Hangul Jamo
        || (c >= '⺀' && c <= '〾')  // CJK radicals, kangxi, punctuation
        || (c >= 'ぁ' && c <= '㏿')  // kana, compatibility, squared forms
        || (c >= '㐀' && c <= '䶿')  // extension A
        || (c >= '一' && c <= '鿿')  // unified ideographs
        || (c >= 'ꀀ' && c <= '꓏')  // yi
        || (c >= '가' && c <= '힣')  // hangul syllables
        || (c >= '豈' && c <= '﫿')  // compatibility ideographs
        || (c >= '︰' && c <= '﹯')  // compatibility forms
        || (c >= '＀' && c <= '｠')  // fullwidth forms
        || (c >= '￠' && c <= '￦');

    /// Forgets everything, so a probe can load a table it just wrote.
    public static void Forget()
    {
        _table = null;
        _index = null;
        _column = 0;
        _forced = false;
        Locale = "en";
    }

    /// The keys the table holds, for a probe. The game never asks.
    public static string[] AllKeys => _table?.Keys ?? System.Array.Empty<string>();

    public static string[] AllLocales => _table?.Locales ?? System.Array.Empty<string>();

    /// The raw cell, for a probe checking a locale that is not the current one.
    public static string Raw(int key, int locale) =>
        _table == null ? "" : _table.Values[key * _table.Locales.Length + locale];
}
