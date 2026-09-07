using Godot;

/// Builds one solid low-poly body per variant, rigged for `body.gdshader`.
///
/// These replace the billboard sprites. The sprites were the right answer under a
/// camera that could not turn: a quad facing you is the cheapest way to draw a
/// crowd, and nothing about it is ever seen from an angle it was not drawn for.
/// A turnable camera removes that guarantee — walk around a billboard horde and
/// every one of them pivots to keep facing you, which is a good deal more
/// unsettling than the zombies.
///
/// Built here in code rather than imported, for the same reasons `MeshBuilder`
/// exists: MultiMesh silently loses an imported GLB on pack/save (godot.md:46),
/// and MultiMesh is the only way a hundred bodies stay inside the draw-call
/// budget. A procedural mesh is the combination that is actually allowed.
///
/// Proportions are fractions of the variant's design height, so a 3 m brute is
/// not a scaled-up walker — it has a brute's shoulders and a brute's reach at its
/// own size. That is what makes the horde readable at a distance where the only
/// information left is the silhouette.
public static class BodyMeshLibrary
{
    /// Everything that distinguishes one body from another.
    ///
    /// What the body is holding, as a silhouette rather than as a weapon.
    ///
    /// **The player has been fighting bare-handed on screen since the body
    /// existed.** There was no weapon geometry anywhere in this file — not a
    /// placeholder, not a stub. C6 made the nine weapons sound and feel
    /// different, which answered the complaint they were raised against, and did
    /// nothing at all for the eye: four categories, one identical outline.
    ///
    /// Three shapes, not nine. A held object at this size is fifteen pixels of
    /// silhouette hanging off an arm, and the questions it can answer are "long
    /// or short" and "does it have a blade". Modelling a bolt launcher
    /// distinctly from a marksman rifle would be work spent below the resolution
    /// anyone is looking at.
    public enum Carry
    {
        /// Nothing. Every horde variant, and the player before a weapon is
        /// resolved.
        None,

        /// Held across the body in both hands, muzzle forward. Firearms.
        Longarm,

        /// A stave with a limb across it, carried at an angle. Bows and
        /// crossbows — a different outline from a rifle at the same length,
        /// which is the whole reason it is its own shape.
        Bow,

        /// Short, in one hand, at the hip. Knives and blades.
        Blade,
    }

    /// A record of numbers rather than a subclass per variant. The variants
    /// differ in proportion and colour and nothing else — a `WalkerBody` type
    /// would be six lines of constructor around one call.
    public readonly record struct Build(
        float Height,

        /// The chest, across. The arms hang outside this, so the body is wider
        /// than the number by two arm radii.
        float ShoulderWidth,
        float LimbRadius,
        float TorsoDepth,

        /// How far the torso leans forward, in degrees. A runner leans into it; a
        /// brute stands up straight because nothing it meets requires urgency.
        float LeanDegrees,

        /// Radians of swing at reference pace. Legs and arms separately: a body
        /// that swings both the same amount marches.
        ///
        /// **The arm figures are smaller than they look like they should be, and
        /// that is the rig's fault rather than the animation's.** `SetRig` turns a
        /// vertex about a *fixed* Y by `swing * sin(phase)`, which cannot express
        /// a child bone: a forearm given its own pivot at the elbow separates from
        /// the upper arm the moment the upper arm swings. So the whole arm turns
        /// as one piece about the shoulder, and past about a third of a radian a
        /// rigid arm at full swing reads as a plank rather than as a stride. The
        /// legs do not have this problem — a straightening knee is what a leg does
        /// at the top of a stride, so the same rigidity reads as correct.
        float LegSwing,
        float ArmSwing,

        /// Metres the whole body rises on each footfall.
        float Bob,

        Color Torso,
        Color Limb,
        Color Head,

        /// A belly instead of a chest. The bloater is the only thing shaped like
        /// a hazard rather than a person, and the silhouette is the warning.
        bool Belly,

        /// What it is carrying. Defaulted, so every existing construction site —
        /// seven variants and the player — is unchanged by this field arriving.
        Carry Held = Carry.None,

        /// A lit organ in the chest, and eyes to match.
        ///
        /// The glow travels in the **alpha of the vertex colour**, which was the
        /// only channel left and was being written and ignored — see
        /// `body.gdshader`. Alpha below one means lit; everything already in the
        /// game writes one and is unaffected.
        bool Lantern = false,

        /// What the organ burns, when `Lantern` is set. Its alpha is the glow,
        /// so this wants an alpha near zero.
        Color Sac = default,

        /// Arm length, as a multiple of the chest.
        ///
        /// **This used to be inferred and the inference was a landmine.** The
        /// arm was long when `ShoulderWidth < 0.40 && LeanDegrees < 20`, which
        /// selected the spitter and nothing else — by arithmetic that held only
        /// while every other narrow body happened to lean more than twenty
        /// degrees. Widening the shoulders by eight centimetres, which is a
        /// decision about the *chest*, silently took the spitter's reach away;
        /// and the spitter fights at eight metres, so its reach is the tell that
        /// says so before the first glob lands.
        ///
        /// A body that wants long arms now says it wants long arms.
        float Reach = 1.16f,

        /// A cap over the crown, and a brim in front of it.
        ///
        /// **This is the survivor rule, built.** `CHARACTERS.md` says a survivor
        /// is *manufactured* and the horde is *grown* — straight edges, bilateral
        /// symmetry, hard kit with a flat face on it — and until the heads grew
        /// there was nothing on a head large enough to put kit on. Now there is,
        /// and a bare skull was the last thing the player and a walker still had
        /// in common: at fifteen metres the strongest read on any body is the
        /// blob at the top of it, and both blobs were the same bare dome in a
        /// skin colour.
        ///
        /// Nothing in the horde sets this, and nothing in the horde may. It is
        /// hue's partner rather than its replacement — hue wins at a distance
        /// where a twelve-triangle cap is four pixels, and the cap wins in the
        /// press of bodies where every colour is half in shadow.
        ///
        /// It stays under `HeadFraction + HeadRadiusFraction`, so it changes no
        /// standing height and `BodyProbe` does not care that it exists.
        bool Cap = false,

        /// How crooked. Zero is a survivor; one is the horde.
        ///
        /// **This is the other half of the rule the cap started, and it is the
        /// half that was missing.** `CHARACTERS.md`: *a survivor is manufactured;
        /// the horde is grown. Straight edges, bilateral symmetry, hard kit with
        /// a flat face on it. The horde is asymmetric mass — leaning, swollen,
        /// spilling.* Every body in this file was perfectly bilaterally
        /// symmetrical, standing to attention with its arms at its sides, which
        /// means the horde had been built to the survivor's rule for eleven
        /// phases. Nine variants of shop mannequin.
        ///
        /// One shoulder drops, the arm under it hangs longer, the head sits
        /// forward of the spine and off the centre line, and the chest rolls a
        /// few degrees toward the low side. None of it is large — a tenth of a
        /// chest here, a sixth of a head there — and all of it is on the same
        /// side, which is what makes it read as one crooked body rather than as
        /// four separate errors.
        ///
        /// Nothing moves vertically. `StandingHeight` is `HeadFraction +
        /// HeadRadiusFraction` at zero lean and `BodyProbe` asserts every variant
        /// stands at the height the balance table names, so the head may go
        /// forward and sideways and may not go down.
        float Asymmetry = 0.0f);

    /// The variants, by the same names `Horde.TypeNames` uses.
    ///
    /// Heights come from the enemy table rather than being repeated here — the
    /// table is what the rest of the game balances against, and a second copy of
    /// a height is a second thing to forget to change.
    public static Build ForVariant(string typeName, float height) => typeName switch
    {
        // Gaunt and slightly stooped. The baseline everything else reads against,
        // so it is deliberately the least distinctive silhouette in the set.
        "walker" => new Build(height, 0.50f, 0.095f, 0.20f, 8.0f, 0.55f, 0.30f, 0.035f,
            Palette.WalkerTorso, Palette.WalkerLimb, Palette.WalkerHead, false,
            Asymmetry: 1.0f),

        // Thin, leaning hard into the run, arms back. Recognisable from the
        // silhouette alone before the speed is apparent, which is the whole point
        // — by the time the speed is apparent it is next to you.
        "runner" => new Build(height, 0.44f, 0.078f, 0.16f, 26.0f, 0.95f, 0.48f, 0.055f,
            Palette.RunnerTorso, Palette.RunnerLimb, Palette.RunnerHead, false,
            Asymmetry: 0.75f),

        // Shoulders wider than a doorway, short stride. Bulk reads as slowness at
        // any distance, which is honest: it is the slowest thing in the game.
        "brute" => new Build(height, 0.78f, 0.180f, 0.38f, -4.0f, 0.32f, 0.24f, 0.045f,
            Palette.BruteTorso, Palette.BruteLimb, Palette.BruteHead, false,
            Asymmetry: 1.25f),

        // A belly on legs. Nothing else in the set is round, so roundness alone
        // is enough to mean "do not stand next to this".
        "bloater" => new Build(height, 0.54f, 0.140f, 0.30f, 4.0f, 0.30f, 0.30f, 0.075f,
            Palette.BloaterTorso, Palette.BloaterLimb, Palette.BloaterHead, true,
            Asymmetry: 1.1f),

        // Wider than it is tall, and that is the entire idea.
        //
        // Every other thing in the horde is an upright biped of roughly human
        // proportion, including the brute — which is a big one, not a different
        // shape. This is the first silhouette in the set that is *horizontal*,
        // and at twenty metres in fog the only thing the player can read is the
        // outline. A wall that walks.
        //
        // It exists to block rather than to chase. The numbers in the table give
        // it the health and the knockback resistance; the shape has to be what
        // says so before the player has been hit once.
        "bulwark" => new Build(height, 1.62f, 0.24f, 0.44f, -8.0f, 0.20f, 0.16f, 0.030f,
            Palette.BulwarkTorso, Palette.BulwarkLimb, Palette.BulwarkHead, true,
            Asymmetry: 0.8f),

        // Dark, and carrying a light.
        //
        // The arena goes black at somewhere between twenty-four and forty-four
        // metres depending on the place, and until now the dark was uniformly
        // empty — a thing either was in the lit part or was not there at all.
        // This is the first enemy that is visible *before* it arrives, which
        // inverts what the fog means: an approaching glow is information the
        // player gets for free and has to decide what to do with.
        //
        // The body is the darkest in the set on purpose. The sac has to be the
        // brightest thing on screen and the creature around it has to be nearly
        // nothing, or what approaches is a lit man rather than a light.
        "lantern" => new Build(height, 0.47f, 0.090f, 0.19f, 16.0f, 0.50f, 0.34f, 0.045f,
            Palette.LanternTorso, Palette.LanternLimb, Palette.LanternHead, false,
            Carry.None, true, new Color(0.55f, 0.92f, 0.72f, 0.0f), Asymmetry: 0.9f),

        // Long-armed and narrow, because it fights at eight metres and the reach
        // is the tell.
        "spitter" => new Build(height, 0.44f, 0.082f, 0.18f, 12.0f, 0.45f, 0.38f, 0.030f,
            Palette.SpitterTorso, Palette.SpitterLimb, Palette.SpitterHead, false,
            Reach: 1.34f, Asymmetry: 1.15f),

        // Everything larger, and darker than anything around it. A boss that
        // shared the horde's value range would disappear into it at exactly the
        // moment the horde is thickest.
        "boss" => new Build(height, 1.26f, 0.240f, 0.50f, 0.0f, 0.40f, 0.34f, 0.060f,
            Palette.BossTorso, Palette.BossLimb, Palette.BossHead, false,
            Asymmetry: 1.0f),

        _ => new Build(height, 0.50f, 0.095f, 0.20f, 8.0f, 0.55f, 0.30f, 0.035f,
            Palette.WalkerTorso, Palette.WalkerLimb, Palette.WalkerHead, false,
            Asymmetry: 1.0f),
    };

    /// Upright, squarer, and in colours nothing in the horde uses.
    ///
    /// The player is the one body that must never be mistaken for one of them for
    /// even a frame, and in a crowd the only channel with any bandwidth left is
    /// hue. Blue against a horde of greens, greys and reds.
    public static Build ForPlayer(float height) => ForPlayer(height, Carry.None);

    public static Build ForPlayer(float height, Carry held) =>
        ForPlayer(height, held,
                  Palette.PlayerTorso, Palette.PlayerLimb, Palette.PlayerHead);

    /// A named survivor, in their own colours.
    ///
    /// Proportions are shared and only the palette moves, which is a decision
    /// rather than laziness. The player is the one body that must never be
    /// mistaken for the horde for even a frame, and what carries that is hue:
    /// blue against a crowd of greens, greys and reds. Three survivors that were
    /// three *silhouettes* would each have to win that fight separately, and two
    /// of them would lose it — there is exactly one shape in this game that
    /// reads as "not one of them", and all three get it.
    public static Build ForPlayer(float height, Carry held, Color torso, Color limb, Color head) =>
        new(height, 0.56f, 0.108f, 0.24f, 4.0f, 0.60f, 0.33f, 0.040f,
            torso, limb, head, false, held, Cap: true);

    /// Which silhouette a weapon category carries.
    ///
    /// Kept here rather than on `WeaponResource` because it is a fact about how
    /// a body is *drawn*, and the weapon table is what the game balances
    /// against. A rendering concern in the balance table is a rendering concern
    /// somebody has to think about while tuning damage.
    public static Carry CarryFor(WeaponCategory category) => category switch
    {
        WeaponCategory.MeleeShort => Carry.Blade,

        // A long melee weapon is a scythe or a pole, and reads much closer to a
        // rifle held across the body than to a knife at the hip.
        WeaponCategory.MeleeLong => Carry.Longarm,

        WeaponCategory.BowCrossbow => Carry.Bow,
        _ => Carry.Longarm,
    };

    /// Fractions of height. Named rather than inlined because they are used twice
    /// each and a body assembled from two slightly different ideas of where the
    /// hip is comes apart when it walks.
    private const float HipFraction = 0.46f;

    /// **The heads were anatomically correct and that was the problem.**
    ///
    /// A human head is about an eighth of a person, and at 0.065 of design
    /// height these were exactly that — 26 cm on a 2 m walker, twelve pixels at
    /// the distance the game is played from, and nothing whatever to look at.
    /// Every low-poly character that reads as a *character* rather than as a
    /// mannequin does the same thing to escape it: the head goes to a fifth of
    /// the body and the limbs thicken to match. It is not a cartoon convention,
    /// it is a resolution one. The head is where the eye goes, it is the part
    /// that says which way a body faces, and at this triangle budget it has to
    /// be large enough to hold a jaw and a brow and still be a shape.
    ///
    /// Height is preserved exactly, because `StandingHeight` is
    /// `HeadFraction + HeadRadiusFraction` at zero lean and `BodyProbe` asserts
    /// every variant stands at the number the balance table names. The two moved
    /// against each other by the same 0.035 and their sum is still one.
    private const float HeadFraction = 0.900f;
    private const float HeadRadiusFraction = 0.100f;

    /// The shoulder came down to make room for the neck.
    ///
    /// A head this size has its underside at `0.900 - 0.100 = 0.80`, which is
    /// precisely where the shoulder line used to be — so the skull sat straight
    /// on the collarbone and the neck tube was drawing inside the head. Dropping
    /// the shoulders to 0.76 buys four per cent of design height of visible neck,
    /// which is 8 cm on a walker and the thing that keeps a big head reading as a
    /// head rather than as a helmet.
    ///
    /// It shortens the chest and therefore the arms, both measured from
    /// `shoulderY - hipY`. That is the same direction the head moved in and it is
    /// wanted: short limbs under a large head is the whole proportion.
    private const float ShoulderFraction = 0.76f;

    // Ends inside the skull. The head's underside is at 0.80 and the tube stops
    // 4.5% of design height above it, so no camera angle can open the joint.
    private const float NeckFraction = 0.845f;

    /// How tall this body actually stands, which is not its design height.
    ///
    /// Leaning forward makes you shorter, and the runner leans 26 degrees — it
    /// draws 1.71 m against a table saying 1.80. That is correct and it is worth
    /// being able to say so exactly: a blanket tolerance wide enough to admit the
    /// lean would also admit a head placed at the wrong fraction, which is the
    /// error this is actually guarding against.
    ///
    /// The head is a ball, so the top is its centre after leaning plus its radius.
    public static float StandingHeight(Build spec)
    {
        float hipY = spec.Height * HipFraction;
        float headY = spec.Height * HeadFraction;
        float headRadius = spec.Height * HeadRadiusFraction;

        return hipY + (headY - hipY) * Mathf.Cos(Mathf.DegToRad(spec.LeanDegrees)) + headRadius;
    }

    public static ArrayMesh Build3D(Build spec)
    {
        var mesh = new MeshBuilder();

        float height = spec.Height;
        float hipY = height * HipFraction;
        float shoulderY = height * ShoulderFraction;
        float neckY = height * NeckFraction;
        float headRadius = height * HeadRadiusFraction;
        float headY = height * HeadFraction;

        float lean = Mathf.DegToRad(spec.LeanDegrees);
        float half = spec.ShoulderWidth * 0.5f;
        float legX = half * 0.43f;
        float kneeY = hipY * 0.52f;
        float armRadius = spec.LimbRadius * 0.9f;
        float chestHeight = shoulderY - hipY;
        Color trousers = Darken(spec.Limb, 0.72f);
        Color shadow = Darken(spec.Head, 0.34f);

        // Everything crooked, resolved once and applied on the body's left.
        //
        // On one side deliberately. Dropping a shoulder here and lengthening the
        // *other* arm reads as two mistakes; doing both on the same side reads as
        // one body carrying its weight wrong, which is what it is meant to be.
        float asym = spec.Asymmetry;
        float shoulderDrop = chestHeight * 0.11f * asym;

        // Forward and to the low side. Forward is the stoop — a head over its own
        // feet is a person standing, and a head over the ground in front of them
        // is a person coming at you — and sideways is what stops the stoop
        // reading as a bow.
        //
        // Damped by the lean, because the two are the same gesture and they were
        // stacking. `Lean` already swings the whole upper body forward about the
        // hip, so on the runner — 26 degrees of it — a full head lead on top put
        // the skull in front of its own sternum at chest height. A body cannot be
        // both hunched and sprinting; the lean is the sprint and this is the
        // hunch, so whichever one the variant has more of takes the difference.
        float hunch = Mathf.Clamp(1.0f - spec.LeanDegrees / 40.0f, 0.25f, 1.0f) * asym;

        var headLead = new Vector3(-headRadius * 0.20f * hunch, 0.0f,
                                   -headRadius * 0.38f * hunch);

        // Bob is on every part, including the legs. Applying it to the torso
        // alone would lift the hips off the thighs on every footfall, and a body
        // that comes apart four centimetres at a time is worse than one that does
        // not bob at all. Four centimetres of foot lift is invisible; a four
        // centimetre gap at the hip is not.

        // --- legs ------------------------------------------------------------
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -legX : legX;
            float phase = side * 0.5f;
            Vector3 ankle = new(x, 0.07f, -spec.LimbRadius * 0.28f);
            Vector3 knee = new(x, kneeY, -spec.LimbRadius * 0.38f);
            Vector3 hip = new(x, hipY, 0.0f);

            mesh.SetRig(spec.LegSwing, hipY, phase, spec.Bob);
            // Tapered, at no cost — see `MeshBuilder.Tube`. A thigh the same width
            // at the knee as at the hip is the single thing that made these read
            // as plumbing rather than as legs, and the fix is one more argument.
            mesh.Tube(knee, hip, spec.LimbRadius * 0.86f, spec.LimbRadius * 1.18f, trousers);

            // Four hundredths of a turn leaves enough disagreement to bend the
            // knee beneath the body, while 92% of the thigh's travel makes both
            // sections nearly collinear at full extension. A larger lag shortened
            // the leg precisely when its forward silhouette needed the reach.
            mesh.SetRig(spec.LegSwing * 0.92f, kneeY, phase + 0.04f, spec.Bob);
            // Narrowest at the ankle. The calf is above the midpoint on a real
            // leg, but a second segment to say so costs twelve triangles per leg
            // for something nobody will see at this distance — the taper alone
            // carries it.
            mesh.Tube(ankle, knee, spec.LimbRadius * 0.62f, spec.LimbRadius * 0.92f, spec.Limb);
            // A foot is a multiple of the ankle it sits on, so it grew with the
            // limbs and grew too far: at 3.9 radii deep the boss was standing on
            // two ninety-centimetre skis. Retuned against the thicker leg so the
            // proportion is the one it was before, not the arithmetic.
            mesh.Box(new Vector3(x, 0.05f, -spec.LimbRadius * 0.85f),
                     new Vector3(spec.LimbRadius * 1.55f, 0.10f, spec.LimbRadius * 2.60f),
                     Darken(spec.Limb, 0.62f));
        }

        // --- torso -----------------------------------------------------------
        // Leaning is baked into the geometry rather than applied by the shader:
        // it never changes, and a constant does not belong in a per-vertex
        // function evaluated for every body on screen every frame.
        mesh.SetRig(0.0f, 0.0f, 0.0f, spec.Bob);

        if (spec.Belly && spec.ShoulderWidth > height * 0.6f)
        {
            // Wider than it is tall, so its body is a barrel laid on its side.
            //
            // **The bulwark and the bloater share `Belly` and do not share a
            // shape**, and treating them the same left the bulwark as a pile of
            // separate boulders. Its shoulders are 1.62 m apart on a 1.5 m body
            // and its ball was sized off the *chest height* — 0.29 m of radius
            // trying to span 1.62 m of shoulder — so the two deltoids, the belly
            // and the head were four objects with air between them. From the
            // front it read as three heads.
            //
            // A horizontal barrel spans them by construction: the axis runs
            // shoulder to shoulder and the radii are the height and the depth, so
            // the thing is exactly as wide as it is meant to be and as deep as
            // its own `TorsoDepth`. This is the only body in the game whose
            // *width* is the silhouette, and it is now the only one built along
            // that axis.
            float span = spec.ShoulderWidth * 0.42f;
            var girth = new Vector2(chestHeight * 0.62f, spec.TorsoDepth * 0.82f);
            Vector3 middle = Lean(new Vector3(0.0f, (hipY + shoulderY) * 0.5f, 0.0f), lean, hipY);

            mesh.Barrel(middle + new Vector3(-span, 0.0f, 0.0f),
                        middle + new Vector3(span, 0.0f, 0.0f), girth, girth, spec.Torso, 8);
        }
        else if (spec.Belly)
        {
            mesh.Ball(Lean(new Vector3(0.0f, (hipY + shoulderY) * 0.5f, 0.0f), lean, hipY),
                      chestHeight * 0.62f, spec.Torso, 6, 4);
        }
        else
        {
            // A ribcage: narrow at the waist, broadest just under the arms,
            // and much wider than it is deep. See `MeshBuilder.Barrel` — the box
            // this replaces was four hard vertical edges catching the light in
            // four flat bands, which is most of why these read as furniture.
            float waist = spec.ShoulderWidth * 0.38f;

            // Half the shoulder span, so the chest is exactly as wide as the
            // shoulder line and the deltoids below add the bulge on top of it.
            // At 0.56 it was *wider* than the span — a torso overhanging its own
            // shoulders, with the arms emerging from under it like a tablecloth,
            // which is most of why the shoulder never read as a joint.
            float chest = spec.ShoulderWidth * 0.50f;

            // The top of the chest goes with the low shoulder. A ribcage that
            // stays level under a dropped shoulder is a body with a broken
            // collarbone rather than a crooked one.
            mesh.Barrel(Lean(new Vector3(0.0f, hipY + chestHeight * 0.24f, 0.0f), lean, hipY),
                        Lean(new Vector3(-spec.ShoulderWidth * 0.05f * asym,
                                         hipY + chestHeight * 0.96f - shoulderDrop * 0.45f, 0.0f),
                             lean, hipY),
                        new Vector2(waist, spec.TorsoDepth * 0.52f),
                        new Vector2(chest, spec.TorsoDepth * 0.66f),
                        spec.Torso);
        }

        // Separating hips, ribs and shoulder line costs two boxes but removes the
        // wardrobe silhouette: the waist can now pinch while the brute keeps the
        // full width which is its warning at fog distance.
        // The pelvis, tapering the other way — wide where the legs leave it and
        // narrower where the ribs sit on it. Two barrels meeting at the waist is
        // what gives a body a middle, and a middle is what a box never had.
        mesh.Barrel(Lean(new Vector3(0.0f, hipY - chestHeight * 0.06f, 0.0f), lean, hipY),
                    Lean(new Vector3(0.0f, hipY + chestHeight * 0.26f, 0.0f), lean, hipY),
                    new Vector2(spec.ShoulderWidth * 0.42f, spec.TorsoDepth * 0.56f),
                    new Vector2(spec.ShoulderWidth * 0.36f, spec.TorsoDepth * 0.50f),
                    trousers);
        // A deltoid is a ball, and two barrels laid end to end were never going
        // to be one.
        //
        // **The shoulder was the worst joint on the body and it was the one the
        // eye lands on.** What was here ran a tapered barrel from the sternum out
        // to each shoulder tip: correct in span, and in silhouette a slab with a
        // step down to the arm hanging past its end. On the brute — a grey torso
        // with pale limbs — it read as two epaulettes with somebody else's arms
        // under them, and no amount of retuning the taper fixed it, because the
        // shape a shoulder actually is is a sphere with the arm leaving its
        // underside.
        //
        // So: one ball per side, centred where the arm's own root is, with the
        // upper arm's top ring inside it. There is no join left to see, because
        // the two overlap rather than meet. Twelve triangles cheaper, too.
        float halfSpan = spec.ShoulderWidth * 0.5f;
        float armX = halfSpan - armRadius * 0.25f;

        // The limb's colour, not the torso's. A shoulder belongs to the arm
        // hanging off it — painting the ball from the torso palette and the arm
        // from the limb palette put a colour break exactly at the joint, which is
        // the one place a body must not have one, and turned a deltoid into a
        // pauldron. On the brute, where the two palettes are a grey and a pale
        // green, it was somebody else's arm again.
        Color deltoid = Darken(spec.Limb, 0.94f);

        // Reaches to `halfSpan + 1.35` arm radii, which is where the old barrel
        // plus the arm hanging off it reached. The brute's warning is its width
        // and nothing here narrows it.
        float deltoidRadius = armRadius * 1.45f;

        foreach (int side in new[] { -1, 1 })
        {
            float drop = side < 0 ? shoulderDrop : 0.0f;
            mesh.Ball(Lean(new Vector3(side * armX, shoulderY - chestHeight * 0.04f - drop, 0.0f),
                           lean, hipY),
                      deltoidRadius, deltoid, 6, 4);
        }

        // --- head ------------------------------------------------------------
        // Wider where it meets the shoulders than where it meets the skull. A
        // constant-width neck is a bolt, and it is the join the eye goes to first
        // because the head is the only part of a body anyone looks at.
        // Leaning with the head rather than standing under it. `headLead` puts
        // the skull forward of the spine on a crooked body, and a neck that
        // stayed vertical under it would leave the head floating off the front of
        // the shoulders — so the top of the neck goes three quarters of the way,
        // which is a neck at an angle.
        mesh.Tube(Lean(new Vector3(0.0f, shoulderY, 0.0f), lean, hipY),
                  Lean(new Vector3(0.0f, neckY, 0.0f), lean, hipY) + headLead * 0.75f,
                  spec.LimbRadius * 1.35f, spec.LimbRadius * 0.95f, spec.Limb);

        Vector3 head = Lean(new Vector3(0.0f, headY, 0.0f), lean, hipY) + headLead;

        // Eight around and five up, against the six-and-four the small head had.
        // Faceting is a style here rather than an artefact, but the facet has to
        // be smaller than the feature sitting on it: at six segments a head this
        // size is a hexagonal prism and the brow spans a whole flat face, so the
        // jaw and the brow stop being a jaw and a brow and become two ledges.
        // Sixteen more triangles on the one part of the body anyone looks at.
        mesh.Ball(head, headRadius, spec.Head, 8, 5);

        // These project beyond the sphere rather than being decoration painted
        // onto it. The jaw survives as a profile from the side; the brow stays
        // inside the crown's narrow low-poly silhouette so darkness, rather than
        // a mushroom cap, survives when the face is three pixels tall.
        mesh.Box(head + new Vector3(0.0f, -headRadius * 0.44f, -headRadius * 0.52f),
                 new Vector3(headRadius * 1.10f, headRadius * 0.52f, headRadius * 0.66f),
                 Darken(spec.Head, 0.88f));
        // Two sockets with a bridge between them, rather than one bar across.
        //
        // The bar was right when the head was 26 cm: at that size it is a line of
        // shadow under a brow and there is no room for anything with structure in
        // it. On a head twice as wide the same box spans the whole face at a
        // constant height, and what a horizontal dark band across a face reads as
        // is a visor — the boss in particular arrived wearing sunglasses. Two
        // patches either side of a lit bridge is the smallest thing that reads as
        // a face instead, and it costs the same twelve triangles.
        foreach (int eye in new[] { -1, 1 })
        {
            mesh.Box(head + new Vector3(eye * headRadius * 0.40f, headRadius * 0.10f,
                                        -headRadius * 0.80f),
                     new Vector3(headRadius * 0.42f, headRadius * 0.26f, headRadius * 0.24f),
                     shadow);
        }

        // A brow over them, and a mouth under.
        //
        // Two dark slots on a smooth dome is a mask, and that is what these were
        // reading as — the eyes were the only feature on the face and nothing
        // above or below them said which way was up. A ridge catching the light
        // directly over a dark socket is the oldest trick there is for making a
        // face out of almost nothing, and it costs twelve triangles.
        mesh.Box(head + new Vector3(0.0f, headRadius * 0.30f, -headRadius * 0.78f),
                 new Vector3(headRadius * 1.12f, headRadius * 0.18f, headRadius * 0.30f),
                 Darken(spec.Head, 0.88f));

        // On the front of the jaw rather than under it. A mouth on the underside
        // is invisible from a camera 26 degrees above the horizontal, which is
        // every camera this game has.
        mesh.Box(head + new Vector3(0.0f, -headRadius * 0.46f, -headRadius * 0.84f),
                 new Vector3(headRadius * 0.60f, headRadius * 0.20f, headRadius * 0.18f),
                 Darken(spec.Head, 0.26f));

        // The cap, on the survivors and on nothing else.
        if (spec.Cap)
        {
            Color kit = Darken(spec.Limb, 0.88f);

            // **Two segments, because one cone cannot cover a sphere.** The
            // first attempt was a single truncated cone from the temple to the
            // crown, and what it produced was a headband: a cone's radius falls
            // linearly and a sphere's falls as a cosine, so above the brow the
            // skull is *wider* than any straight-sided cap over it and poked
            // through everywhere except at the very bottom ring. Following the
            // sphere in two steps, six per cent proud at each ring, is a cap.
            //
            // Radii are the sphere's own at each height, `sqrt(1 - y²)`, times
            // 1.06. The top ring stops one per cent short of the crown, which is
            // what keeps `StandingHeight` — computed as centre plus radius — the
            // truth about how tall this body stands.
            //
            // It starts at 0.30 of a radius, just above the eye sockets at 0.10,
            // so the cap sits on a face rather than replacing one.
            mesh.Barrel(head + new Vector3(0.0f, headRadius * 0.30f, 0.0f),
                        head + new Vector3(0.0f, headRadius * 0.72f, 0.0f),
                        new Vector2(headRadius * 1.011f, headRadius * 1.011f),
                        new Vector2(headRadius * 0.735f, headRadius * 0.735f),
                        kit, 8);

            mesh.Barrel(head + new Vector3(0.0f, headRadius * 0.72f, 0.0f),
                        head + new Vector3(0.0f, headRadius * 0.99f, 0.0f),
                        new Vector2(headRadius * 0.735f, headRadius * 0.735f),
                        new Vector2(headRadius * 0.200f, headRadius * 0.200f),
                        kit, 8);

            // A peak, on the brimline. Bilateral symmetry and a straight edge are
            // two thirds of the survivor rule, and this is the only flat plane on
            // a head made of spheres — it is what says the shape was *made*, and
            // it points the way the body is facing from behind as well as in
            // front.
            mesh.Box(head + new Vector3(0.0f, headRadius * 0.30f, -headRadius * 1.00f),
                     new Vector3(headRadius * 1.24f, headRadius * 0.14f, headRadius * 0.62f),
                     Darken(kit, 0.82f));
        }

        // --- arms ------------------------------------------------------------
        // Counter-phased against the leg on the same side, which is what stops a
        // walk reading as a march. The pivot is the shoulder, after the lean has
        // moved it — an arm swinging about where the shoulder would have been
        // upright detaches from a leaning body at the top of every stride.
        for (int side = 0; side < 2; side++)
        {
            // Rooted *inside* the deltoid ball rather than outboard of the
            // shoulder line. The ball is centred here and is 1.6 arm radii
            // across, so the upper arm's top ring is wholly inside it and there
            // is no join to open however the arm swings — the overlap the old
            // outboard placement was trying to buy with 0.35 of a radius and
            // never quite got.
            float sign = side == 0 ? -1.0f : 1.0f;
            float x = sign * armX;
            float drop = sign < 0.0f ? shoulderDrop : 0.0f;
            Vector3 shoulder = Lean(new Vector3(x, shoulderY - drop, 0.0f), lean, hipY);
            // Longer on the low side. A dropped shoulder with an arm the same
            // length as the other one reads as a shrug; the point of the drop is
            // that the whole side hangs.
            float armLength = chestHeight * spec.Reach * (sign < 0.0f ? 1.0f + 0.09f * asym : 1.0f);
            // Twelve per cent of a radius shows which way the elbow faces in
            // profile without moving the hand away from the hip. Animation
            // supplies the gesture; the resting mesh only supplies the anatomy.
            //
            // Forward and out, on a crooked body. Arms hanging plumb at the sides
            // is a person waiting for a bus; the horde is meant to be reaching,
            // and the rig only swings fore and aft, so the reach has to be in the
            // rest pose. Splayed as well as forward, because a body with both
            // arms in the same vertical plane is a diagram.
            Vector3 elbow = shoulder + new Vector3(sign * armRadius * 0.55f * asym,
                                                   -armLength * 0.52f,
                                                   -armRadius * (0.12f + 1.10f * asym));
            Vector3 wrist = shoulder + new Vector3(sign * armRadius * 0.30f * asym,
                                                   -armLength,
                                                   -armRadius * (0.03f + 1.90f * asym));
            float phase = side * 0.5f + 0.5f;

            // The carrying arm barely swings, and that is anatomy rather than
            // taste: a person holding a rifle across their body does not let
            // that arm travel. Left at full swing the weapon scythes back and
            // forth across the torso every stride, which reads as the weapon
            // being animated rather than held.
            bool carrying = sign > 0.0f && spec.Held != Carry.None;
            float swing = carrying ? spec.ArmSwing * 0.25f : spec.ArmSwing;

            mesh.SetRig(swing, shoulder.Y, phase, spec.Bob);
            // Thicker at the top than it was, so the step from the deltoid ball
            // down to the arm is 0.15 of a radius rather than 0.45. Below about
            // that the two read as one shoulder; above it the ball reads as
            // something resting on the arm.
            mesh.Tube(shoulder, elbow, armRadius * 1.30f, armRadius * 0.86f, spec.Limb);

            // One shoulder rotation keeps the elbow sealed and lets a hanging
            // arm read as one line. A second absolute pivot cannot behave like a
            // child bone and was turning the small resting bend into a doll kink.
            mesh.Tube(elbow, wrist, armRadius * 0.9f, armRadius * 0.66f, spec.Head);
            // Same correction as the foot. A hand is barely wider than the wrist
            // it is on; at 1.65 radii on the new arm it was a mitten.
            mesh.Box(wrist + new Vector3(0.0f, -armRadius * 0.62f, -armRadius * 0.10f),
                     new Vector3(armRadius * 1.32f, armRadius * 1.40f, armRadius * 1.15f),
                     spec.Head);

            // The weapon rides the same rig as the hand holding it. Rigged
            // rather than parented, because there is nothing to parent to: a
            // `MultiMesh` has no skeleton, so "attached to the hand" means
            // "turns about the same pivot, on the same phase, by the same
            // amount" and nothing else.
            if (carrying)
                Weapon(mesh, spec, shoulder, wrist, armRadius);
        }

        // The organ, last, so it sits over the torso rather than inside it.
        if (spec.Lantern)
        {
            // On the torso's rig — which is no rig at all: the chest does not
            // swing, so the sac rides the bob and nothing else. A sac on an arm
            // pivot would swing out of the body every stride.
            mesh.ClearRig();
            mesh.SetRig(0.0f, 0.0f, 0.0f, spec.Bob);

            // Mid-chest, not the gut. At 0.42 of the chest it sat at the waist
            // and read as something the creature was carrying; a light at the
            // sternum reads as something inside it. The difference matters more
            // than it sounds, because this is the one enemy the player meets as
            // a shape in the dark before they meet it as a body.
            float sacY = hipY + chestHeight * 0.66f;
            float sacR = spec.ShoulderWidth * 0.38f;

            mesh.Ball(Lean(new Vector3(0.0f, sacY, -spec.ShoulderWidth * 0.24f), lean, hipY),
                      sacR, spec.Sac, 7, 5);

            // A dimmer collar around it, so the bright core has an edge rather
            // than ending at the torso. Half the glow, which at this size is the
            // difference between a lamp and a hole cut in the body.
            var collar = new Color(spec.Sac.R * 0.6f, spec.Sac.G * 0.6f, spec.Sac.B * 0.6f, 0.5f);
            mesh.Ball(Lean(new Vector3(0.0f, sacY, -spec.ShoulderWidth * 0.18f), lean, hipY),
                      sacR * 1.35f, collar, 7, 4);

            // Eyes. Two points at head height are what make the glow read as a
            // creature looking at you rather than as a lamp being carried.
            float eyeY = shoulderY + (headY - shoulderY) * 0.55f;
            foreach (int side in new[] { -1, 1 })
            {
                mesh.Box(Lean(new Vector3(side * spec.ShoulderWidth * 0.13f, eyeY,
                                          -spec.ShoulderWidth * 0.30f), lean, hipY),
                         new Vector3(0.045f, 0.03f, 0.03f), spec.Sac);
            }
        }

        mesh.ClearRig();
        return mesh.Build();
    }

    /// Only the held object, carrying the same rigid arm channel as Build3D.
    /// This lets an authored survivor keep the procedural weapon silhouettes
    /// without rebuilding or duplicating the authored body.
    public static ArrayMesh BuildCarry3D(Build spec)
    {
        var mesh = new MeshBuilder();
        if (spec.Held == Carry.None)
            return mesh.Build();

        float hipY = spec.Height * HipFraction;
        float shoulderY = spec.Height * ShoulderFraction;
        float lean = Mathf.DegToRad(spec.LeanDegrees);
        float half = spec.ShoulderWidth * 0.5f;
        float armRadius = spec.LimbRadius * 0.9f;
        float chestHeight = shoulderY - hipY;
        float x = half + armRadius * 0.35f;
        Vector3 shoulder = Lean(new Vector3(x, shoulderY, 0.0f), lean, hipY);
        float armLength = chestHeight *
            (spec.ShoulderWidth < 0.40f && spec.LeanDegrees < 20.0f ? 1.34f : 1.16f);
        Vector3 wrist = shoulder + new Vector3(0.0f, -armLength, -armRadius * 0.03f);

        mesh.SetRig(spec.ArmSwing * 0.25f, shoulder.Y, 1.0f, spec.Bob);
        Weapon(mesh, spec, shoulder, wrist, armRadius);
        mesh.ClearRig();
        return mesh.Build();
    }

    /// What the right hand is holding.
    ///
    /// Drawn under the arm's rig, so it swings with the hand. Rigged rather than
    /// parented because there is nothing to parent to: a `MultiMesh` has no
    /// skeleton, so "attached to the hand" means "turns about the same pivot, on
    /// the same phase, by the same amount" and nothing else.
    ///
    /// **Placed from the shoulder, not from the wrist**, and that was the whole
    /// of the first version's problem. Hung off the wrist, every weapon sits at
    /// hip height with the thigh in front of it: the rifle read as something
    /// dropped by the player's foot, and the bow was a thin line almost entirely
    /// behind a leg. A carried weapon is held *up*, across the body, and the
    /// height it is held at is the thing that says it is being carried rather
    /// than trailed.
    ///
    /// Everything here is deliberately chunky. The whole object is a dozen or so
    /// pixels across at the distance this body is usually seen, and detail below
    /// that is geometry nobody will ever resolve.
    private static void Weapon(MeshBuilder mesh, Build spec, Vector3 shoulder, Vector3 wrist,
                               float armRadius)
    {
        Color metal = Darken(spec.Limb, 0.5f);
        Color wood = new(0.30f, 0.20f, 0.13f);
        Color edge = new(0.60f, 0.62f, 0.66f);

        // Inward, because the shoulder is at the outside of the body and a
        // weapon held out beyond it reads as being pushed away rather than
        // carried. `side` is always the right arm here, so inward is -X.
        float inward = -1.0f;

        switch (spec.Held)
        {
            case Carry.Longarm:
            {
                // Across the chest, butt high by the shoulder and muzzle low
                // across the front — a patrol carry. The diagonal is the read:
                // horizontal is a plank and vertical is a staff, and only the
                // diagonal is unmistakably a long gun.
                Vector3 butt = shoulder + new Vector3(inward * 0.02f, -0.10f, 0.14f);
                Vector3 muzzle = shoulder + new Vector3(inward * 0.30f, -0.52f, -0.44f);

                mesh.Tube(butt, muzzle, armRadius * 0.40f, metal, 5);

                // Stock, magazine and foregrip. Three lumps on a line is what
                // separates a firearm from a pipe.
                mesh.Box(butt + new Vector3(inward * 0.01f, 0.01f, 0.03f),
                         new Vector3(armRadius * 1.1f, armRadius * 1.6f, 0.20f), wood);

                Vector3 mid = butt.Lerp(muzzle, 0.45f);
                mesh.Box(mid + new Vector3(0.0f, -armRadius * 1.3f, 0.0f),
                         new Vector3(armRadius * 0.8f, armRadius * 2.2f, armRadius * 1.2f), metal);

                mesh.Box(butt.Lerp(muzzle, 0.75f),
                         new Vector3(armRadius * 1.1f, armRadius * 1.0f, 0.14f), wood);
                break;
            }

            case Carry.Bow:
            {
                // Held upright and clear of the leg. Vertical where the rifle is
                // diagonal, which is the entire reason it is its own shape rather
                // than a longarm in a different colour.
                Vector3 hand = shoulder + new Vector3(inward * 0.06f, -0.34f, -0.16f);
                Vector3 top = hand + new Vector3(0.0f, 0.44f, -0.06f);
                Vector3 bottom = hand + new Vector3(0.0f, -0.44f, -0.04f);

                mesh.Tube(bottom, top, armRadius * 0.36f, wood, 5);

                // The recurve: short pieces kicked forward at both tips. A
                // straight stave is a stick.
                mesh.Box(top + new Vector3(0.0f, -0.02f, -0.08f),
                         new Vector3(armRadius * 0.75f, 0.16f, 0.12f), wood);
                mesh.Box(bottom + new Vector3(0.0f, 0.02f, -0.07f),
                         new Vector3(armRadius * 0.75f, 0.14f, 0.11f), wood);

                // The string, straight between the tips and behind the stave,
                // and the riser the hand is on.
                mesh.Box(hand + new Vector3(0.0f, 0.0f, 0.06f),
                         new Vector3(armRadius * 0.28f, 0.86f, armRadius * 0.28f), edge);
                mesh.Box(hand, new Vector3(armRadius * 1.1f, 0.20f, armRadius * 1.3f), metal);
                break;
            }

            case Carry.Blade:
            {
                // Down at the hand and angled out from the thigh, which is the
                // one thing that keeps it visible at all. Short on purpose: the
                // difference from a longarm has to be obvious at a glance, and
                // the only channel for that is length.
                Vector3 hand = wrist + new Vector3(inward * 0.04f, -armRadius * 0.6f, -0.06f);
                Vector3 tip = hand + new Vector3(inward * 0.06f, -0.10f, -0.34f);

                mesh.Box(hand + new Vector3(0.0f, 0.04f, 0.03f),
                         new Vector3(armRadius * 0.9f, armRadius * 1.9f, armRadius * 0.9f), wood);
                mesh.Box(hand + new Vector3(0.0f, -0.02f, -0.02f),
                         new Vector3(armRadius * 1.9f, armRadius * 0.55f, armRadius * 0.8f), metal);
                mesh.Tube(hand + new Vector3(0.0f, -0.03f, -0.05f), tip, armRadius * 0.34f, edge, 4);
                break;
            }
        }
    }

    /// Tips a point forward about the hip.
    ///
    /// About the hip rather than the feet, because a body pivoted at the floor
    /// leans its head a long way forward of its toes and reads as falling over.
    private static Vector3 Lean(Vector3 point, float radians, float hipY)
    {
        if (radians == 0.0f)
            return point;

        float y = point.Y - hipY;
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector3(point.X, y * c + hipY, -y * s);
    }

    private static Color Darken(Color colour, float amount) =>
        new(colour.R * amount, colour.G * amount, colour.B * amount, colour.A);
}
