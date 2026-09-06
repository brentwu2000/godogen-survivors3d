using Godot;

/// Asserts that the daylight palette gives away no more than the dusk one did.
///
///   godot --headless --script test/PaletteProbe.cs
///
/// Exit code is the verdict. No scene: everything here is arithmetic over
/// `Palette`, `PropLibrary` and the five biome resources, which is the point —
/// this is the one property of the art direction that a screenshot cannot judge
/// and that no amount of looking at the game will surface.
///
/// **What went wrong, and why nothing caught it.** The palette was lifted from
/// dusk to midday by inverting every value: dark props became bright props, a
/// near-black fog became a near-white haze. It built, every one of the forty-odd
/// probes passed, and the screenshots were the ones that were wanted. It was also
/// a 3.3x cut to how well the fog hid the approaching horde, because fog does not
/// hide things by being dark — it hides them by washing out contrast, in *both*
/// directions. Put the air above the brightest body and every enemy at thirty
/// metres becomes a legible dark shape on a pale field. The dusk rig's real trick
/// was that the horde and the air were the same darkness; the daylight rig's is
/// that the air sits in the middle of the horde's range.
///
/// So the three stages below are the three things the eye cannot check:
///
///   1. Every prop colour is inside the band, so a colour added later in the old
///      dusk range does not quietly read as one prop standing in shadow.
///   2. Every biome hides the ordinary horde at its own spawn ring within 20% of
///      what its own dusk rig did. Per biome, because the fog distances and the
///      ring differ per biome and the places are meant to differ from each other.
///   3. The lantern and the boss stay more visible than any ordinary variant, and
///      the player's hue stays clear of every body in the horde.
public partial class PaletteProbe : SceneTree
{
    /// Rec. 709 luminance of a colour, in the linear space the shader works in.
    ///
    /// `SrgbToLinear` first, and it is not a detail: the sRGB curve is what made
    /// the dusk palette work. Eight bodies spanning 0.13 to 0.46 in authored
    /// values span only 0.017 to 0.168 once decoded, which is a range four times
    /// narrower — the darkness was doing most of the hiding before the fog was
    /// consulted at all. Comparing authored values here would have reported the
    /// dusk rig and the daylight rig as equally contrasty and passed the bug.
    private static float Luminance(Color colour)
    {
        Color linear = colour.SrgbToLinear();
        return 0.2126f * linear.R + 0.7152f * linear.G + 0.0722f * linear.B;
    }

    /// How much of an object's own colour survives the fog at `metres`.
    ///
    /// Godot's depth fog lerps toward the fog colour by
    /// `((d - begin) / (end - begin)) ^ curve`, so what is left of any contrast
    /// the object had is one minus that.
    private static float Kept(float metres, float begin, float end, float curve)
    {
        float t = Mathf.Clamp((metres - begin) / Mathf.Max(end - begin, 0.001f), 0.0f, 1.0f);
        return 1.0f - Mathf.Pow(t, curve);
    }

    /// The fog curve, which is not on `BiomeResource` — it is a constant in
    /// `BuildMain` that no biome overrides. Repeated here rather than exported,
    /// because a biome that could bend the curve would be a fourth way to change
    /// the difficulty by accident.
    private const float FogCurve = 1.6f;

    /// Where the far half of the spawn ring is. The horde arrives on a 12–40 m
    /// ring scaled per biome; thirty is the middle of the outer half, which is
    /// the part the fog exists to cover.
    private const float RingMetres = 30.0f;

    /// The worst ordinary variant's contrast under the dusk rig, per biome, in
    /// the order `BiomeBook` lists them.
    ///
    /// Recorded numbers rather than a recomputation from a second copy of the old
    /// palette: the old palette is gone, and a probe carrying a full replica of
    /// what it is testing against tends to get "fixed" alongside the thing it is
    /// guarding. These are measurements of a build that shipped.
    private static readonly float[] DuskBaseline =
        { 0.0492f, 0.1034f, 0.0492f, 0.1189f, 0.0632f };

    /// How far a biome may drift from its dusk baseline, either way.
    ///
    /// Both directions, and the upper bound is the obvious one — drifting up
    /// means the fog gives the horde away earlier than it used to. The lower
    /// bound matters just as much and is the one that would never be questioned:
    /// a place that hides *more* than it did is a difficulty increase, and one
    /// arriving as a side effect of a colour choice is exactly as unintended as
    /// the cut this probe was written for.
    private const float Tolerance = 0.20f;

    /// The variants the fog is meant to hide.
    ///
    /// The lantern and the boss are deliberately absent. The lantern's whole
    /// design is that it is visible before it arrives, and a boss is announced —
    /// holding either to the ordinary budget would be asserting that the game
    /// does not do the thing it is built to do. Stage 3 checks they exceed it.
    private static (string Name, Color Torso)[] Ordinary() => new[]
    {
        ("walker", Palette.WalkerTorso),
        ("runner", Palette.RunnerTorso),
        ("brute", Palette.BruteTorso),
        ("bloater", Palette.BloaterTorso),
        ("bulwark", Palette.BulwarkTorso),
        ("spitter", Palette.SpitterTorso),
    };

    public override void _Initialize()
    {
        bool failed = false;

        failed |= !CheckBand();
        failed |= !CheckFogContrast();
        failed |= !CheckSeparation();

        GD.Print(failed ? "PROBE FAILED" : "palette ok");
        Quit(failed ? 1 : 0);
    }

    /// Stage 1 — every prop colour is in the daylight band.
    private static bool CheckBand()
    {
        bool ok = true;
        float darkest = 1.0f, brightest = 0.0f;

        foreach (Color colour in PropLibrary.Materials())
        {
            // The authored value rather than the luminance, because the band is a
            // statement about what someone typed. A saturated blue is legitimately
            // dark in luminance and is not a mistake; a colour whose every channel
            // is under a quarter is the dusk palette leaking back in.
            float value = Mathf.Max(colour.R, Mathf.Max(colour.G, colour.B));
            darkest = Mathf.Min(darkest, value);
            brightest = Mathf.Max(brightest, value);

            if (value < Palette.PropFloor || value > Palette.PropCeiling)
            {
                GD.PushError($"prop colour {colour} is at {value:F2}, outside "
                             + $"[{Palette.PropFloor:F2}, {Palette.PropCeiling:F2}]");
                ok = false;
            }
        }

        GD.Print($"stage 1  props {darkest:F2}..{brightest:F2} "
                 + $"(band {Palette.PropFloor:F2}..{Palette.PropCeiling:F2})  {(ok ? "ok" : "FAILED")}");
        return ok;
    }

    /// Stage 2 — each biome still hides the horde the way its dusk rig did.
    private static bool CheckFogContrast()
    {
        bool ok = true;
        BiomeResource[] biomes = BiomeBook.All;

        if (biomes.Length != DuskBaseline.Length)
        {
            // Not a warning. A sixth biome has no recorded baseline, so it would
            // be measured against nothing and silently pass — which is the shape
            // of the bug this whole probe exists to catch.
            GD.PushError($"{biomes.Length} biomes but {DuskBaseline.Length} baselines — "
                         + "a new place needs its dusk contrast recorded before it can be checked");
            return false;
        }

        for (int i = 0; i < biomes.Length; i++)
        {
            BiomeResource biome = biomes[i];
            float ring = RingMetres * biome.SpawnRingScale;
            float kept = Kept(ring, biome.FogBegin, biome.FogEnd, FogCurve);
            float air = Luminance(biome.FogColour);

            float worst = 0.0f;
            string which = "";
            foreach ((string name, Color torso) in Ordinary())
            {
                float contrast = Mathf.Abs(Luminance(torso) - air) * kept;
                if (contrast > worst)
                {
                    worst = contrast;
                    which = name;
                }
            }

            float baseline = DuskBaseline[i];
            float ratio = worst / baseline;
            bool inside = ratio >= 1.0f - Tolerance && ratio <= 1.0f + Tolerance;

            GD.Print($"stage 2  {biome.BiomeName,-14} ring {ring,5:F1} m  "
                     + $"worst {worst:F4} ({which})  dusk {baseline:F4}  {ratio:F2}x  "
                     + $"{(inside ? "ok" : "FAILED")}");

            if (!inside)
            {
                GD.PushError($"{biome.BiomeName}: the fog gives the horde away at {ratio:F2}x "
                             + $"the dusk rig — outside {1.0f - Tolerance:F2}..{1.0f + Tolerance:F2}. "
                             + "Move the fog colour's luminance toward the middle of the horde's "
                             + "range to hide more, away from it to hide less. The fog distances "
                             + "are a balance change and are not the knob to reach for here.");
                ok = false;
            }
        }

        return ok;
    }

    /// Stage 3 — the two bodies that are meant to stand out still do, and the
    /// player still cannot be mistaken for any of them.
    private static bool CheckSeparation()
    {
        bool ok = true;
        BiomeResource yard = BiomeBook.Load(0);
        float kept = Kept(RingMetres * yard.SpawnRingScale, yard.FogBegin, yard.FogEnd, FogCurve);
        float air = Luminance(yard.FogColour);

        float ordinary = 0.0f;
        foreach ((string _, Color torso) in Ordinary())
            ordinary = Mathf.Max(ordinary, Mathf.Abs(Luminance(torso) - air) * kept);

        foreach ((string name, Color torso) in new[]
                 { ("lantern", Palette.LanternTorso), ("boss", Palette.BossTorso) })
        {
            float contrast = Mathf.Abs(Luminance(torso) - air) * kept;
            bool stands = contrast > ordinary;

            GD.Print($"stage 3  {name,-8} {contrast:F4} vs ordinary {ordinary:F4}  "
                     + $"{(stands ? "ok" : "FAILED")}");

            if (!stands)
            {
                GD.PushError($"the {name} is no more visible at the ring than an ordinary "
                             + "variant. It is built to be seen before it arrives; a palette "
                             + "that hides it removes a mechanic without touching its code.");
                ok = false;
            }
        }

        // Chroma, not value. The player is told apart from the horde in a crowd,
        // where value is spent on lighting and shape is spent on the crowd itself
        // — so the rule has always been "blue against greens and greys", and it
        // has never once been checked. A saturated palette is exactly where it
        // breaks, because a saturated colour gets chosen for how it looks alone.
        Vector2 player = Palette.Chroma(Palette.PlayerTorso);
        float nearest = float.MaxValue;

        foreach (Color torso in Palette.HordeTorsos())
            nearest = Mathf.Min(nearest, player.DistanceTo(Palette.Chroma(torso)));

        bool separated = nearest >= Palette.PlayerChromaSeparation;
        GD.Print($"stage 3  player chroma nearest body {nearest:F2} away "
                 + $"(needs {Palette.PlayerChromaSeparation:F2})  {(separated ? "ok" : "FAILED")}");

        if (!separated)
        {
            GD.PushError("the player is inside the horde's colour. In a crowd of fifty, hue and "
                         + "saturation are the only channels with any bandwidth left.");
            ok = false;
        }

        return ok;
    }
}
