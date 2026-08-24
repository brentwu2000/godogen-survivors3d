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
        Color Sac = default);

    /// The variants, by the same names `Horde.TypeNames` uses.
    ///
    /// Heights come from the enemy table rather than being repeated here — the
    /// table is what the rest of the game balances against, and a second copy of
    /// a height is a second thing to forget to change.
    public static Build ForVariant(string typeName, float height) => typeName switch
    {
        // Gaunt and slightly stooped. The baseline everything else reads against,
        // so it is deliberately the least distinctive silhouette in the set.
        "walker" => new Build(height, 0.42f, 0.055f, 0.20f, 8.0f, 0.55f, 0.30f, 0.035f,
            new Color(0.36f, 0.40f, 0.34f), new Color(0.44f, 0.42f, 0.38f),
            new Color(0.62f, 0.58f, 0.50f), false),

        // Thin, leaning hard into the run, arms back. Recognisable from the
        // silhouette alone before the speed is apparent, which is the whole point
        // — by the time the speed is apparent it is next to you.
        "runner" => new Build(height, 0.36f, 0.045f, 0.16f, 26.0f, 0.95f, 0.48f, 0.055f,
            new Color(0.46f, 0.30f, 0.28f), new Color(0.52f, 0.36f, 0.32f),
            new Color(0.66f, 0.50f, 0.42f), false),

        // Shoulders wider than a doorway, short stride. Bulk reads as slowness at
        // any distance, which is honest: it is the slowest thing in the game.
        "brute" => new Build(height, 0.78f, 0.115f, 0.38f, -4.0f, 0.32f, 0.24f, 0.045f,
            new Color(0.30f, 0.29f, 0.31f), new Color(0.38f, 0.35f, 0.34f),
            new Color(0.50f, 0.44f, 0.40f), false),

        // A belly on legs. Nothing else in the set is round, so roundness alone
        // is enough to mean "do not stand next to this".
        "bloater" => new Build(height, 0.46f, 0.085f, 0.30f, 4.0f, 0.30f, 0.30f, 0.075f,
            new Color(0.44f, 0.46f, 0.30f), new Color(0.40f, 0.42f, 0.32f),
            new Color(0.56f, 0.56f, 0.40f), true),

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
        "bulwark" => new Build(height, 1.62f, 0.16f, 0.44f, -8.0f, 0.20f, 0.16f, 0.030f,
            new Color(0.26f, 0.25f, 0.24f), new Color(0.33f, 0.30f, 0.27f),
            new Color(0.42f, 0.38f, 0.33f), true),

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
        "lantern" => new Build(height, 0.40f, 0.052f, 0.19f, 16.0f, 0.50f, 0.34f, 0.045f,
            new Color(0.13f, 0.14f, 0.16f), new Color(0.16f, 0.16f, 0.18f),
            new Color(0.20f, 0.21f, 0.22f), false,
            Carry.None, true, new Color(0.55f, 0.92f, 0.72f, 0.0f)),

        // Long-armed and narrow, because it fights at eight metres and the reach
        // is the tell.
        "spitter" => new Build(height, 0.38f, 0.050f, 0.18f, 12.0f, 0.45f, 0.38f, 0.030f,
            new Color(0.28f, 0.42f, 0.38f), new Color(0.34f, 0.48f, 0.42f),
            new Color(0.48f, 0.62f, 0.52f), false),

        // Everything larger, and darker than anything around it. A boss that
        // shared the horde's value range would disappear into it at exactly the
        // moment the horde is thickest.
        "boss" => new Build(height, 1.10f, 0.150f, 0.50f, 0.0f, 0.40f, 0.34f, 0.060f,
            new Color(0.20f, 0.18f, 0.22f), new Color(0.26f, 0.22f, 0.24f),
            new Color(0.40f, 0.30f, 0.30f), false),

        _ => new Build(height, 0.42f, 0.055f, 0.20f, 8.0f, 0.55f, 0.30f, 0.035f,
            new Color(0.36f, 0.40f, 0.34f), new Color(0.44f, 0.42f, 0.38f),
            new Color(0.62f, 0.58f, 0.50f), false),
    };

    /// Upright, squarer, and in colours nothing in the horde uses.
    ///
    /// The player is the one body that must never be mistaken for one of them for
    /// even a frame, and in a crowd the only channel with any bandwidth left is
    /// hue. Blue against a horde of greens, greys and reds.
    public static Build ForPlayer(float height) => ForPlayer(height, Carry.None);

    public static Build ForPlayer(float height, Carry held) =>
        ForPlayer(height, held,
                  new Color(0.22f, 0.34f, 0.52f), new Color(0.26f, 0.30f, 0.38f),
                  new Color(0.72f, 0.60f, 0.48f));

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
        new(height, 0.48f, 0.065f, 0.24f, 4.0f, 0.60f, 0.33f, 0.040f,
            torso, limb, head, false, held);

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
    private const float ShoulderFraction = 0.80f;

    // The centre and radius still sum to one design height. Taking half a per
    // cent from the radius and giving it to the centre exposes the neck without
    // changing StandingHeight; the tube ends half a per cent of design height
    // inside the head so the newly visible joint cannot open when viewed uphill.
    private const float NeckFraction = 0.875f;
    private const float HeadFraction = 0.935f;
    private const float HeadRadiusFraction = 0.065f;

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
            mesh.Box(new Vector3(x, 0.04f, -spec.LimbRadius * 1.25f),
                     new Vector3(spec.LimbRadius * 2.15f, 0.08f, spec.LimbRadius * 3.9f),
                     Darken(spec.Limb, 0.62f));
        }

        // --- torso -----------------------------------------------------------
        // Leaning is baked into the geometry rather than applied by the shader:
        // it never changes, and a constant does not belong in a per-vertex
        // function evaluated for every body on screen every frame.
        mesh.SetRig(0.0f, 0.0f, 0.0f, spec.Bob);

        if (spec.Belly)
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
            float waist = spec.ShoulderWidth * 0.30f;
            float chest = spec.ShoulderWidth * 0.45f;

            mesh.Barrel(Lean(new Vector3(0.0f, hipY + chestHeight * 0.24f, 0.0f), lean, hipY),
                        Lean(new Vector3(0.0f, hipY + chestHeight * 0.96f, 0.0f), lean, hipY),
                        new Vector2(waist, spec.TorsoDepth * 0.42f),
                        new Vector2(chest, spec.TorsoDepth * 0.54f),
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
                    new Vector2(spec.ShoulderWidth * 0.34f, spec.TorsoDepth * 0.46f),
                    new Vector2(spec.ShoulderWidth * 0.29f, spec.TorsoDepth * 0.40f),
                    trousers);
        // The shoulder line, across rather than up. A barrel lying on its side:
        // the axis runs from one shoulder to the other, so the taper is the
        // *deltoid* falling away at each end rather than a slab with two square
        // corners. It is the last hard-edged box on the upper body and the one
        // the eye lands on, because it is where the arms are supposed to join.
        //
        // The brute's warning is still its width — nothing here narrows it, the
        // corners are simply no longer square.
        float halfSpan = spec.ShoulderWidth * 0.5f;
        Vector3 shoulderLine = Lean(new Vector3(0.0f, shoulderY - chestHeight * 0.06f, 0.0f), lean, hipY);

        // Two, from the middle outward, each thinning as it goes.
        //
        // One barrel end to end has the same radius the whole way and reads as a
        // girder laid across the back — which on the bulwark, whose shoulders are
        // 1.6 m wide, looked like something it was carrying. A deltoid falls
        // away, and the only way to say that with a tapered primitive is to run
        // it from the centre out in both directions.
        Color deltoid = Darken(spec.Torso, 0.86f);
        var inner = new Vector2(chestHeight * 0.13f, spec.TorsoDepth * 0.52f);
        var outer = new Vector2(chestHeight * 0.075f, spec.TorsoDepth * 0.30f);

        foreach (int side in new[] { -1, 1 })
        {
            mesh.Barrel(shoulderLine,
                        shoulderLine + new Vector3(side * halfSpan, -chestHeight * 0.04f, 0.0f),
                        inner, outer, deltoid, 6);
        }

        // --- head ------------------------------------------------------------
        // Wider where it meets the shoulders than where it meets the skull. A
        // constant-width neck is a bolt, and it is the join the eye goes to first
        // because the head is the only part of a body anyone looks at.
        mesh.Tube(Lean(new Vector3(0.0f, shoulderY, 0.0f), lean, hipY),
                  Lean(new Vector3(0.0f, neckY, 0.0f), lean, hipY),
                  spec.LimbRadius * 1.35f, spec.LimbRadius * 0.95f, spec.Limb);

        Vector3 head = Lean(new Vector3(0.0f, headY, 0.0f), lean, hipY);
        mesh.Ball(head, headRadius, spec.Head, 6, 4);

        // These project beyond the sphere rather than being decoration painted
        // onto it. The jaw survives as a profile from the side; the brow stays
        // inside the crown's narrow low-poly silhouette so darkness, rather than
        // a mushroom cap, survives when the face is three pixels tall.
        mesh.Box(head + new Vector3(0.0f, -headRadius * 0.48f, -headRadius * 0.50f),
                 new Vector3(headRadius * 1.18f, headRadius * 0.62f, headRadius * 0.72f),
                 Darken(spec.Head, 0.78f));
        mesh.Box(head + new Vector3(0.0f, headRadius * 0.12f, -headRadius * 0.82f),
                 new Vector3(headRadius * 1.16f, headRadius * 0.24f, headRadius * 0.22f), shadow);

        // --- arms ------------------------------------------------------------
        // Counter-phased against the leg on the same side, which is what stops a
        // walk reading as a march. The pivot is the shoulder, after the lean has
        // moved it — an arm swinging about where the shoulder would have been
        // upright detaches from a leaning body at the top of every stride.
        for (int side = 0; side < 2; side++)
        {
            // Sixty-five per cent of the root radius crosses the shoulder edge.
            // That overlap survives the faceted tube's narrowest presentation
            // during a swing, while the remainder still carries the arm in the
            // silhouette at horde distance.
            float x = (side == 0 ? -1.0f : 1.0f) * (half + armRadius * 0.35f);
            Vector3 shoulder = Lean(new Vector3(x, shoulderY, 0.0f), lean, hipY);
            // Width alone also selects the runner, whose swept-back compact arms
            // are part of its arrowhead outline. The modest lean cutoff leaves
            // the narrow, stooped spitter as the only body reaching below its hip.
            float armLength = chestHeight *
                (spec.ShoulderWidth < 0.40f && spec.LeanDegrees < 20.0f ? 1.34f : 1.16f);
            // Twelve per cent of a radius shows which way the elbow faces in
            // profile without moving the hand away from the hip. Animation
            // supplies the gesture; the resting mesh only supplies the anatomy.
            Vector3 elbow = shoulder + new Vector3(0.0f, -armLength * 0.52f, -armRadius * 0.12f);
            Vector3 wrist = shoulder + new Vector3(0.0f, -armLength, -armRadius * 0.03f);
            float phase = side * 0.5f + 0.5f;

            // The carrying arm barely swings, and that is anatomy rather than
            // taste: a person holding a rifle across their body does not let
            // that arm travel. Left at full swing the weapon scythes back and
            // forth across the torso every stride, which reads as the weapon
            // being animated rather than held.
            bool carrying = side == 1 && spec.Held != Carry.None;
            float swing = carrying ? spec.ArmSwing * 0.25f : spec.ArmSwing;

            mesh.SetRig(swing, shoulder.Y, phase, spec.Bob);
            mesh.Tube(shoulder, elbow, armRadius * 1.15f, armRadius * 0.86f, spec.Limb);

            // One shoulder rotation keeps the elbow sealed and lets a hanging
            // arm read as one line. A second absolute pivot cannot behave like a
            // child bone and was turning the small resting bend into a doll kink.
            mesh.Tube(elbow, wrist, armRadius * 0.9f, armRadius * 0.66f, spec.Head);
            mesh.Box(wrist + new Vector3(0.0f, -armRadius * 0.75f, -armRadius * 0.10f),
                     new Vector3(armRadius * 1.65f, armRadius * 1.75f, armRadius * 1.25f),
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
