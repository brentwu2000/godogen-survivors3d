using Godot;

/// Can this game draw Traditional Chinese, and with what.
///
///   godot --headless --script test/FontProbe.cs
///
/// `UI.md` step 1, and it is step 1 for a reason: it is the only step in that
/// plan that can fail for reasons outside the plan — a licence, a missing glyph,
/// a monospace face that does not exist. Extracting three hundred strings and
/// *then* discovering the font is the expensive order.
///
/// This asks the engine instead of assuming. It reports what the default font
/// covers, what the machine's own fonts cover, and whether a `SystemFont` — which
/// ships nothing and therefore has no licence question at all — is enough to put
/// the language on screen.
public partial class FontProbe : SceneTree
{
    /// Characters the UI will actually need, rather than a lorem-ipsum sample.
    ///
    /// Drawn from the words the plan already knows it must say: the fittings, the
    /// two verbs, the settings row, and the digits and punctuation the pages pad
    /// with. A font that covers a random Han sample and misses 繁 is a font that
    /// fails on the one screen this work exists for.
    private const string Wanted = "繁體中文語言設定角色裝備武器護甲背包靴子飾品"
                                + "軍械庫置物櫃紀錄合約地圖大門宿舍主控台"
                                + "購買裝上賣出出發啟程選擇離開返回"
                                + "生命值彈藥信用點存活秒數波次";

    /// Families worth asking for, best first.
    ///
    /// Monospace first because `UI.md`'s cheap answer to double-width alignment
    /// needs one — the pages are padded with spaces and a proportional face makes
    /// every column a guess. The proportional names are the fallback that proves
    /// the *language* works even if the *layout* has to change.
    private static readonly string[][] Candidates =
    {
        new[] { "Noto Sans Mono CJK TC", "Sarasa Mono TC", "Source Han Mono TC" },
        new[] { "MingLiU", "PMingLiU", "MS Gothic" },
        new[] { "Microsoft JhengHei", "Microsoft YaHei", "Noto Sans CJK TC" },
    };

    private static readonly string[] Labels = { "monospace CJK", "legacy monospace", "proportional CJK" };

    /// Where a shipped UI font would be declared, once there is one.
    private const string ThemeFontSetting = "gui/theme/custom_font";

    public override void _Initialize()
    {
        GD.Print($"asking for {Distinct(Wanted).Length} distinct characters the UI needs");
        GD.Print("");

        var configuredPath = ProjectSettings.GetSetting(ThemeFontSetting).AsString();
        bool shipped = configuredPath.Length > 0 && ResourceLoader.Exists(configuredPath);

        if (shipped)
        {
            // The gate, and nothing else.
            //
            // **The survey below stops telling the truth the moment a font is
            // shipped.** A project theme font becomes the last-resort fallback
            // for every `SystemFont`, so asking for a family this machine does
            // not have comes back fully covered — the first run after wiring the
            // font in reported "Noto Sans Mono CJK TC: all" on a machine where it
            // is not installed. Printing that would be worse than printing
            // nothing: it is a survey that agrees with whatever was just done.
            bool covers = Report($"shipped UI font ({configuredPath})", GD.Load<Font>(configuredPath));

            GD.Print("");
            GD.Print(covers
                ? "PROBE OK — the shipped font covers everything the UI asks for"
                : "PROBE FAILED — the shipped font cannot draw text this UI produces");

            Quit(covers ? 0 : 1);
            return;
        }

        // No font shipped, so this is the survey that decides which one to ship.
        // It only runs in that state, which is also the only state where its
        // answers mean anything.
        Font? fallback = ThemeDB.Singleton?.FallbackFont;
        bool fallbackCovers = Report("project default", fallback);

        for (int i = 0; i < Candidates.Length; i++)
        {
            // `AllowSystemFallback` off on purpose. With it on the engine quietly
            // substitutes whatever it can find, every row comes back covered, and
            // the probe reports that a font this machine does not have works
            // fine — which is exactly the answer that would survive until an
            // export ran on a machine that also does not have it.
            var font = new SystemFont
            {
                FontNames = Candidates[i],
                AllowSystemFallback = false,
            };

            Report($"{Labels[i]} ({string.Join(", ", Candidates[i])})", font);
        }

        // And the honest last resort: whatever the OS wants to give us. This is
        // what a shipped `SystemFont` would really do, and if it covered the set
        // then step 1 would cost nothing at all — no file, no licence, no
        // subsetting tool.
        //
        // It does not. Measured on a Windows machine that *has* three CJK
        // families installed, a generic request plus `AllowSystemFallback` still
        // reports no coverage, because the fallback resolves during text shaping
        // and not when a font is asked what it holds. So "ship nothing and let
        // the OS sort it out" cannot be checked ahead of time, which for this
        // project is the same as it not working: a glyph that is a box on someone
        // else's machine and cannot be detected on ours is the exact failure the
        // build gate exists to prevent.
        var anything = new SystemFont { FontNames = new[] { "sans-serif" }, AllowSystemFallback = true };
        Report("system fallback (ships nothing)", anything);

        // The verdict is about the *project*, not about this machine.
        //
        // A probe that passed because Windows happens to bundle MingLiU would go
        // green here and red on the build server, and would say nothing at all
        // about the game. With nothing shipped, the thing worth holding is the
        // premise the whole plan rests on: the engine's own default cannot draw
        // this language. The day that stops being true, `UI.md`'s font section
        // should be deleted rather than followed — and this is what would say so.
        GD.Print("");
        GD.Print(fallbackCovers
            ? "PROBE FAILED — the default font now covers Han; UI.md's font section is obsolete"
            : "PROBE OK — no UI font shipped yet, and the default cannot draw this language");

        Quit(fallbackCovers ? 1 : 0);
    }

    /// How much of the wanted set a font can actually draw.
    private static bool Report(string what, Font? font)
    {
        if (font == null)
        {
            GD.Print($"  {what,-52} no font");
            return false;
        }

        char[] distinct = Distinct(Wanted);
        var missing = new System.Text.StringBuilder();
        int have = 0;

        foreach (char c in distinct)
        {
            if (font.HasChar(c))
                have++;
            else if (missing.Length < 24)
                missing.Append(c);
        }

        bool full = have == distinct.Length;
        string detail = full ? "all" : $"{have}/{distinct.Length}, missing e.g. {missing}";
        GD.Print($"  {what,-52} {detail}");

        // Monospace is a separate question from coverage, and the one the padding
        // depends on: if a Han glyph is not exactly twice a Latin one, the columns
        // drift however carefully they are counted.
        if (full)
        {
            Vector2 latin = font.GetStringSize("MM", HorizontalAlignment.Left, -1, 18);
            Vector2 han = font.GetStringSize("繁", HorizontalAlignment.Left, -1, 18);
            float ratio = latin.X > 0.0f ? han.X / (latin.X * 0.5f) : 0.0f;
            GD.Print($"  {"",-52} one Han = {ratio:F2} Latin cells");
        }

        return full;
    }

    private static char[] Distinct(string text)
    {
        var seen = new System.Collections.Generic.SortedSet<char>();
        foreach (char c in text)
            seen.Add(c);

        var result = new char[seen.Count];
        seen.CopyTo(result);
        return result;
    }
}
