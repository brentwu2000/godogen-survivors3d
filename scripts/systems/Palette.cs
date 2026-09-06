using Godot;

/// Every authored colour in the game, in one file.
///
/// The art direction was previously spread across three places that could not see
/// each other: nineteen material constants private to `PropLibrary`, twenty-four
/// body colours inline in `BodyMeshLibrary`'s variant switch, and a light rig
/// hard-coded in `BuildMain` and overridden five times in `BuildBiomes`. Each was
/// internally consistent and the three were only consistent by hand — which was
/// survivable while they all agreed on one thing, and stopped being survivable the
/// moment that agreement changed.
///
/// **The band.** Everything here is lit by a midday sun rather than by a dusk one.
/// Props sit between about 0.26 and 0.92, with real saturation; the previous set
/// sat between 0.10 and 0.55 and was nearly grey, because it was pitched against a
/// near-black fog and an asphalt floor at 0.34. Under the same numbers a daylight
/// scene reads as a photograph of a dark scene with the exposure pushed: the mid
/// tones arrive, the colour does not, and everything looks like wet slate.
///
/// **Hue carries identity, value carries depth.** A daylight palette has the
/// opposite problem to a dusk one. At dusk everything is dark and the only way to
/// tell a bus from a wall is its shape; in daylight everything is bright and the
/// eye goes to hue first — so paint is properly saturated and the greys are
/// genuinely grey, rather than every object being a slightly different brown.
///
/// **The one rule that is not taste.** `Fog` is the colour distance goes, and the
/// fog *distance* is a difficulty setting: the horde spawns on a 12–40 m ring and
/// the fog is total at 35, so the far half of that ring arrives out of something
/// rather than appearing in plain sight. That still holds with a bright fog — but
/// only because the horde moved with it. A dark creature against a bright haze is
/// *more* legible at thirty metres than a dark creature against a dark haze, so
/// leaving the bodies where they were and brightening only the air would have been
/// a difficulty cut disguised as an art change. `test/PaletteProbe.cs` asserts the
/// contrast at the ring rather than trusting this paragraph.
public static class Palette
{
    // --- The air ------------------------------------------------------------

    /// What distance turns into, and what the sky meets at the horizon.
    ///
    /// **This value was measured, not chosen, and it is the one number in the file
    /// that a redesign may not simply prefer differently.**
    ///
    /// Fog does not hide things by being dark. It hides them by washing out
    /// contrast, and it does that in both directions — so what a fog hides is
    /// whatever is nearest to it in luminance, and what it reveals is whatever is
    /// furthest. A near-black fog hid the dusk horde well because the whole horde
    /// was dark; the first draft of this palette kept the argument and inverted
    /// only the brightness, putting the fog at 0.83 luminance well *above* the
    /// brightest body, and the horde came out 3.3x more legible at the spawn ring
    /// than it had ever been. Every probe passed. It looked, if anything, better.
    ///
    /// The fix is that the fog sits in the **middle** of the horde's luminance
    /// range rather than above it, so the palest body and the darkest are both
    /// about equally washed out. Every biome lands within about 10% of the
    /// contrast its dusk rig produced at its own spawn ring — see
    /// `test/PaletteProbe.cs`, which measures all five rather than trusting this.
    ///
    /// **It is a sky blue rather than a grey, and that is the second constraint
    /// on it.** Aerial perspective is not a metaphor here: an object far enough
    /// away is drawn in exactly the fog colour and nothing else, so whatever this
    /// says is the colour of the horizon. Four attempts at getting a blue sky
    /// failed while this was grey, and each failure looked like a different bug —
    /// a grey sky, then a blue sky with white slabs floating in it once the two
    /// were decoupled. Those slabs were the skyline props at sixty metres, fully
    /// fogged, correctly drawn, and no longer the same colour as what was behind
    /// them. The sky and the air have to agree, so the way to a blue sky is a blue
    /// air.
    public static readonly Color Fog = new(0.55f, 0.68f, 0.86f);

    /// The top of the sky. Real blue rather than the dark slate it was — the top
    /// third of the frame is sky under this camera, and it is the single largest
    /// area of colour in the game.
    ///
    /// Deeper and more saturated than it looks like it should be, because nothing
    /// on screen is shown at the value it is authored at: Filmic tonemapping at
    /// 1.05 exposure with a 2.2 white point, then 1.08 contrast and 1.18
    /// saturation, lift a mid sky blue to a pale wash. The first attempt used a
    /// pleasant 0.30/0.55/0.88 and arrived as grey.
    public static readonly Color SkyTop = new(0.14f, 0.40f, 0.82f);

    /// Motes hanging in the air, and they are no longer white.
    ///
    /// White was right when the air behind them was black: dust is seen because it
    /// catches the light. Against a bright haze the same specks read as falling
    /// snow, which is a weather report the game did not intend to file. Real motes
    /// against a bright sky are seen as *silhouettes*, so these are darker than
    /// the air and warm, and they read as dust again.
    public static readonly Color Dust = new(0.44f, 0.42f, 0.38f);

    /// The horizon, which is the fog and must stay the fog.
    ///
    /// The camera tilts 26° down and never looks more than about ten degrees above
    /// horizontal, so the only sky this game ever draws is the band right above
    /// the horizon — and anything far enough away is drawn in the fog colour. If
    /// those two differ, every distant object is a cut-out. `LevelGenerator`
    /// assigns the biome's own air here for the same reason, so a sandy district
    /// gets a sandy horizon and its skyline still disappears into it.
    public static readonly Color SkyHorizon = Fog;

    /// Warm, and much closer to white than the old dusk sun. A strongly orange
    /// sun is a statement about the hour, and the hour is now the middle of the
    /// day.
    public static readonly Color Sun = new(1.0f, 0.97f, 0.90f);

    /// **Unchanged from the dusk rig, deliberately.**
    ///
    /// The first draft raised this to 1.5 alongside every albedo, on the reasoning
    /// that midday is brighter than dusk. Both halves of that are true and doing
    /// both is double-counting: the pale materials — concrete, chalk, panel —
    /// came out clipped to white, and a prop at the far edge of the arena read as
    /// a sheet of paper standing on grass rather than as a distant building. The
    /// scene is brighter than it was because the *things in it* are brighter.
    ///
    /// Holding the energies still has a second payoff that is worth more than the
    /// exposure. `test/PaletteProbe.cs` compares authored colours, not rendered
    /// pixels — it has no renderer — and that comparison is only faithful while
    /// every body and every fog is lit by the same lamp at the same strength. Move
    /// this and the probe keeps passing while quietly measuring the wrong scene.
    public const float SunEnergy = 1.25f;

    /// Sky blue, because the sun is warm and the sky is what fills the shadows.
    /// The hue moved a long way — this is now the colour of an actual sky rather
    /// than of a dusk one — and the energy did not, for the reason above.
    public static readonly Color Ambient = new(0.55f, 0.68f, 0.86f);

    public const float AmbientEnergy = 0.55f;

    // --- Materials ----------------------------------------------------------
    //
    // The prop set. Names are the material rather than the object, so a kiosk and
    // a lab bench can be the same painted panel without either owning the colour.

    public static readonly Color Steel = new(0.58f, 0.63f, 0.68f);
    public static readonly Color Concrete = new(0.76f, 0.74f, 0.68f);
    public static readonly Color ConcreteDark = new(0.60f, 0.58f, 0.53f);
    public static readonly Color Rust = new(0.72f, 0.36f, 0.20f);
    public static readonly Color PaintRed = new(0.80f, 0.26f, 0.24f);
    public static readonly Color PaintBlue = new(0.24f, 0.50f, 0.72f);
    public static readonly Color PaintGreen = new(0.32f, 0.62f, 0.34f);

    /// The darkest thing in the set, and it has to stay that way — tar is what
    /// every builder reaches for when it wants a shadow line or a tyre. It is no
    /// longer a hole in the frame, though: at 0.12 it read as missing geometry
    /// against a bright floor.
    public static readonly Color Tar = new(0.26f, 0.27f, 0.30f);

    public static readonly Color Board = new(0.82f, 0.70f, 0.48f);

    /// Glass reflects the sky now instead of being a void. This is the single
    /// colour that changed most, and it is why a bus reads as a bus: every window
    /// in the old set was `(0.10, 0.13, 0.16)`, so the vehicles were solid bodies
    /// with black rectangles cut in them.
    public static readonly Color Glass = new(0.40f, 0.62f, 0.70f);

    public static readonly Color PaintYellow = new(0.92f, 0.74f, 0.22f);
    public static readonly Color PaintOrange = new(0.90f, 0.50f, 0.14f);
    public static readonly Color Chalk = new(0.86f, 0.85f, 0.80f);
    public static readonly Color Brick = new(0.70f, 0.38f, 0.30f);

    public static readonly Color Panel = new(0.80f, 0.83f, 0.80f);
    public static readonly Color PanelTrim = new(0.46f, 0.56f, 0.60f);
    public static readonly Color Fluid = new(0.30f, 0.78f, 0.72f);
    public static readonly Color Cable = new(0.28f, 0.26f, 0.30f);
    public static readonly Color Amber = new(0.92f, 0.62f, 0.16f);

    /// The brightest and the darkest a prop may be.
    ///
    /// Asserted rather than merely documented: the failure this guards is a colour
    /// added later in the old band, which does not look wrong on its own — it looks
    /// like one prop is in shadow — and there are twenty-six kinds, so nobody
    /// notices which.
    public const float PropFloor = 0.24f;
    public const float PropCeiling = 0.94f;

    // --- The horde ----------------------------------------------------------
    //
    // Bright, because the fog is. See the class comment: this is the half of the
    // change that keeps the difficulty where it was.
    //
    // Each variant is torso, limb, head. Hue is the variant's identity and it is
    // unchanged from the dusk set — a runner was reddish and still is — because
    // the player learned those, and relearning "which colour is the fast one" is a
    // cost with nothing on the other side of it.

    public static readonly Color WalkerTorso = new(0.52f, 0.66f, 0.40f);
    public static readonly Color WalkerLimb = new(0.60f, 0.68f, 0.46f);
    public static readonly Color WalkerHead = new(0.66f, 0.80f, 0.46f);

    public static readonly Color RunnerTorso = new(0.78f, 0.42f, 0.36f);
    public static readonly Color RunnerLimb = new(0.82f, 0.50f, 0.42f);
    public static readonly Color RunnerHead = new(0.84f, 0.62f, 0.48f);

    public static readonly Color BruteTorso = new(0.50f, 0.52f, 0.56f);
    public static readonly Color BruteLimb = new(0.58f, 0.58f, 0.60f);
    public static readonly Color BruteHead = new(0.70f, 0.76f, 0.58f);

    public static readonly Color BloaterTorso = new(0.66f, 0.74f, 0.34f);
    public static readonly Color BloaterLimb = new(0.60f, 0.70f, 0.38f);
    public static readonly Color BloaterHead = new(0.76f, 0.84f, 0.44f);

    /// Lifted further than its neighbours, and for arithmetic rather than taste:
    /// the bulwark was the darkest ordinary variant and therefore the one setting
    /// the fog's position, and every step it came up was a step of slack for
    /// everything else. It suits it — the thing is a wall that walks, and a pale
    /// wall reads as more of one.
    public static readonly Color BulwarkTorso = new(0.56f, 0.54f, 0.50f);
    public static readonly Color BulwarkLimb = new(0.62f, 0.60f, 0.55f);
    public static readonly Color BulwarkHead = new(0.72f, 0.74f, 0.58f);

    /// Still the darkest body in the set, and that is load-bearing rather than
    /// stylistic: the lantern's sac has to be the brightest thing on screen and
    /// the creature carrying it has to be nearly nothing, or what walks out of the
    /// fog is a lit man rather than a light. Lifted only as far as "a dark thing
    /// in daylight" — at the old 0.13 it was a silhouette cut out of the floor.
    public static readonly Color LanternTorso = new(0.26f, 0.28f, 0.32f);
    public static readonly Color LanternLimb = new(0.30f, 0.31f, 0.34f);
    public static readonly Color LanternHead = new(0.34f, 0.38f, 0.40f);

    public static readonly Color SpitterTorso = new(0.30f, 0.66f, 0.58f);
    public static readonly Color SpitterLimb = new(0.36f, 0.72f, 0.62f);
    public static readonly Color SpitterHead = new(0.52f, 0.84f, 0.66f);

    /// Darker than the horde it arrives with, which is the rule it had before and
    /// is harder to hold now that the horde is bright. Violet rather than
    /// near-black: at the value a boss needs, neutral grey is the brute's colour.
    public static readonly Color BossTorso = new(0.36f, 0.30f, 0.42f);
    public static readonly Color BossLimb = new(0.42f, 0.34f, 0.44f);
    public static readonly Color BossHead = new(0.56f, 0.44f, 0.48f);

    // --- The player ---------------------------------------------------------

    /// Blue, and it stays blue for the reason it was blue: in a crowd the only
    /// channel with any bandwidth left is hue, and nothing in the horde is blue.
    /// The world got greener, which if anything widens the gap.
    public static readonly Color PlayerTorso = new(0.24f, 0.46f, 0.86f);
    public static readonly Color PlayerLimb = new(0.32f, 0.42f, 0.62f);
    public static readonly Color PlayerHead = new(0.92f, 0.76f, 0.60f);

    /// How far the player must sit from every horde body on the colour wheel,
    /// measured as chroma rather than as hue.
    ///
    /// A rule the game has always had in prose and never in code, and the first
    /// run of `test/PaletteProbe.cs` is why it is written this way rather than as
    /// the obvious "sixty degrees of hue". It failed immediately: the player at
    /// 219° against the **brute** at 218°, one degree apart. Both are correct
    /// readings and the conclusion was nonsense — the brute is a blue-*grey* at
    /// 0.11 saturation, and a grey thing cannot be mistaken for a saturated blue
    /// one no matter where its hue technically lands. Hue is undefined at zero
    /// saturation and merely meaningless just above it.
    ///
    /// So the measure is the distance between the two colours' chroma vectors —
    /// saturation as the length, hue as the angle. Two greys are close to each
    /// other and far from everything saturated, which is what the eye does. The
    /// horde's nearest approach is the lantern at 0.53.
    public const float PlayerChromaSeparation = 0.35f;

    /// A colour's position on the wheel: hue as the angle, saturation as the
    /// radius. Value is deliberately absent — the player has to be legible in a
    /// crowd in shadow and in sun, so brightness cannot be part of what tells
    /// them apart.
    public static Vector2 Chroma(Color colour)
    {
        float angle = Mathf.DegToRad(colour.H * 360.0f);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * colour.S;
    }

    /// The horde variants' torso colours, for anything that needs to reason about
    /// the set rather than about one of them. Order matches nothing in particular;
    /// callers that need a name should ask `BodyMeshLibrary`.
    public static Color[] HordeTorsos() => new[]
    {
        WalkerTorso, RunnerTorso, BruteTorso, BloaterTorso,
        BulwarkTorso, LanternTorso, SpitterTorso, BossTorso,
    };
}
