using Godot;

/// Checks the translation table the way `BuildStrings` cannot.
///
///   godot --headless --script test/StringProbe.cs
///
/// The tool refuses a malformed CSV — duplicate keys, ragged rows, empty cells —
/// because those are things it can see while reading. This asks the questions
/// that only exist once the table is a table and the code is using it: whether a
/// locale's placeholders agree with English, whether every key formats, and
/// whether the width arithmetic the screens depend on is arithmetic.
///
/// **Placeholder parity is the stage worth having.** A translator who drops
/// `{0}` produces a sentence that reads perfectly and has lost the number, and
/// nothing else in this project would notice: the string is present, non-empty,
/// formats without throwing, and renders. The only symptom is a screen that says
/// "未開放 — 需撤離 次" and a player who cannot tell how many.
public partial class StringProbe : SceneTree
{
    public override void _Initialize()
    {
        bool failed = false;

        failed |= !StageTheTableLoads();
        failed |= !StageEveryLocaleAgreesWithEnglish();
        failed |= !StageEveryKeyFormats();
        failed |= !StageWideCharactersCountTwice();

        GD.Print("");
        GD.Print(failed ? "PROBE FAILED" : "PROBE OK");
        Quit(failed ? 1 : 0);
    }

    private static bool StageTheTableLoads()
    {
        bool ok = Strings.Ready();

        if (!ok)
            GD.PushError("  the table did not load — run scripts/tools/BuildStrings.cs");
        else if (Strings.AllKeys.Length == 0)
        {
            GD.PushError("  the table loaded and holds no keys");
            ok = false;
        }

        GD.Print($"the table loads: {(ok ? "ok" : "FAILED")} "
               + $"({Strings.AllKeys.Length} keys x {Strings.AllLocales.Length} locales)");
        return ok;
    }

    /// Every locale of a key uses exactly the placeholders English does.
    ///
    /// A set rather than a sequence, because the *order* is allowed to differ —
    /// that is the entire reason the placeholders are positional. What is not
    /// allowed is a missing one, or an extra one the call site will not supply.
    private static bool StageEveryLocaleAgreesWithEnglish()
    {
        bool ok = true;
        string[] keys = Strings.AllKeys;
        string[] locales = Strings.AllLocales;

        for (int k = 0; k < keys.Length; k++)
        {
            var wanted = Holes(Strings.Raw(k, 0));

            for (int l = 1; l < locales.Length; l++)
            {
                var got = Holes(Strings.Raw(k, l));

                if (!wanted.SetEquals(got))
                {
                    GD.PushError($"  '{keys[k]}' takes {Describe(wanted)} in en "
                               + $"and {Describe(got)} in {locales[l]}");
                    ok = false;
                }
            }
        }

        GD.Print($"every locale takes the same placeholders as english: {(ok ? "ok" : "FAILED")}");
        return ok;
    }

    /// Every key formats in every locale, with as many arguments as it asks for.
    ///
    /// A malformed brace — `{0` , or `{}` — is a `FormatException` on a screen
    /// rather than in a measurement, and it is exactly the sort of thing a
    /// translator's editor does to a string it does not know is a format.
    private static bool StageEveryKeyFormats()
    {
        bool ok = true;
        string[] keys = Strings.AllKeys;
        string[] locales = Strings.AllLocales;
        string was = Strings.Locale;

        for (int l = 0; l < locales.Length; l++)
        {
            Strings.Use(locales[l]);

            for (int k = 0; k < keys.Length; k++)
            {
                int holes = Holes(Strings.Raw(k, l)).Count;
                var args = new object[holes];
                for (int i = 0; i < holes; i++)
                    args[i] = "x";

                string drawn = holes == 0 ? Strings.Get(keys[k]) : Strings.Get(keys[k], args);

                // `Get` answers with the key in guillemets when anything is
                // wrong, which is what makes this readable on a screen — and it
                // is also the one string that must never come back from a key
                // the table holds.
                if (drawn.StartsWith('«'))
                {
                    GD.PushError($"  '{keys[k]}' in {locales[l]} came back as {drawn}");
                    ok = false;
                }
            }
        }

        Strings.Use(was);
        GD.Print($"every key formats in every locale: {(ok ? "ok" : "FAILED")}");
        return ok;
    }

    /// The measurement the whole page layout rests on.
    ///
    /// `UI.md` constraint 2: a monospace CJK glyph is exactly two Latin cells, so
    /// padding that counts cells is exact rather than an approximation that
    /// drifts. `Strings.Pad` is what every column in the game will use, and this
    /// is the assertion that it is counting the right thing — a `Pad` that
    /// counted characters would pass every other stage here and misalign every
    /// screen the moment the locale changed.
    private static bool StageWideCharactersCountTwice()
    {
        bool ok = true;

        (string Text, int Cells)[] cases =
        {
            ("", 0),
            ("abc", 3),
            ("近戰", 4),
            ("近戰／狂戰", 10),
            ("RIN 突擊", 8),
        };

        foreach ((string text, int cells) in cases)
        {
            int got = Strings.Cells(text);
            if (got != cells)
            {
                GD.PushError($"  \"{text}\" measured {got} cells, wanted {cells}");
                ok = false;
            }
        }

        // And the property the columns actually depend on: two strings padded to
        // the same width occupy the same width, whatever they are made of.
        string latin = Strings.Pad("ASSAULT", 18);
        string han = Strings.Pad("近戰／狂戰", 18);

        if (Strings.Cells(latin) != Strings.Cells(han))
        {
            GD.PushError($"  padded to 18, latin is {Strings.Cells(latin)} cells "
                       + $"and han is {Strings.Cells(han)} — every column after them shifts");
            ok = false;
        }

        GD.Print($"a han glyph is two cells and padding agrees: {(ok ? "ok" : "FAILED")}");
        return ok;
    }

    /// The `{0}`-style holes in a format string, as a set of indices.
    ///
    /// Hand-scanned rather than a regex, because `{{` is an escaped brace and a
    /// regex that did not know it would report a placeholder in "{{0}}" — which
    /// is a literal `{0}` on screen and takes no argument at all.
    private static System.Collections.Generic.HashSet<int> Holes(string form)
    {
        var found = new System.Collections.Generic.HashSet<int>();

        for (int i = 0; i < form.Length; i++)
        {
            if (form[i] != '{')
                continue;

            if (i + 1 < form.Length && form[i + 1] == '{')
            {
                i++;
                continue;
            }

            int close = form.IndexOf('}', i);
            if (close < 0)
                continue;

            // The alignment and format parts of `{0,-6:F1}` are not the index.
            string body = form[(i + 1)..close];
            int cut = body.IndexOfAny(new[] { ',', ':' });
            if (cut >= 0)
                body = body[..cut];

            if (int.TryParse(body, out int index))
                found.Add(index);

            i = close;
        }

        return found;
    }

    private static string Describe(System.Collections.Generic.HashSet<int> holes)
    {
        if (holes.Count == 0)
            return "no placeholders";

        var sorted = new System.Collections.Generic.List<int>(holes);
        sorted.Sort();
        return "{" + string.Join("}, {", sorted) + "}";
    }
}
