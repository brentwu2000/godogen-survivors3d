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
        float LegSwing,
        float ArmSwing,

        /// Metres the whole body rises on each footfall.
        float Bob,

        Color Torso,
        Color Limb,
        Color Head,

        /// A belly instead of a chest. The bloater is the only thing shaped like
        /// a hazard rather than a person, and the silhouette is the warning.
        bool Belly);

    /// The variants, by the same names `Horde.TypeNames` uses.
    ///
    /// Heights come from the enemy table rather than being repeated here — the
    /// table is what the rest of the game balances against, and a second copy of
    /// a height is a second thing to forget to change.
    public static Build ForVariant(string typeName, float height) => typeName switch
    {
        // Gaunt and slightly stooped. The baseline everything else reads against,
        // so it is deliberately the least distinctive silhouette in the set.
        "walker" => new Build(height, 0.42f, 0.055f, 0.20f, 8.0f, 0.55f, 0.45f, 0.035f,
            new Color(0.36f, 0.40f, 0.34f), new Color(0.44f, 0.42f, 0.38f),
            new Color(0.62f, 0.58f, 0.50f), false),

        // Thin, leaning hard into the run, arms back. Recognisable from the
        // silhouette alone before the speed is apparent, which is the whole point
        // — by the time the speed is apparent it is next to you.
        "runner" => new Build(height, 0.36f, 0.045f, 0.16f, 26.0f, 0.95f, 0.80f, 0.055f,
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

        // Long-armed and narrow, because it fights at eight metres and the reach
        // is the tell.
        "spitter" => new Build(height, 0.38f, 0.050f, 0.18f, 12.0f, 0.45f, 0.60f, 0.030f,
            new Color(0.28f, 0.42f, 0.38f), new Color(0.34f, 0.48f, 0.42f),
            new Color(0.48f, 0.62f, 0.52f), false),

        // Everything larger, and darker than anything around it. A boss that
        // shared the horde's value range would disappear into it at exactly the
        // moment the horde is thickest.
        "boss" => new Build(height, 1.10f, 0.150f, 0.50f, 0.0f, 0.40f, 0.34f, 0.060f,
            new Color(0.20f, 0.18f, 0.22f), new Color(0.26f, 0.22f, 0.24f),
            new Color(0.40f, 0.30f, 0.30f), false),

        _ => new Build(height, 0.42f, 0.055f, 0.20f, 8.0f, 0.55f, 0.45f, 0.035f,
            new Color(0.36f, 0.40f, 0.34f), new Color(0.44f, 0.42f, 0.38f),
            new Color(0.62f, 0.58f, 0.50f), false),
    };

    /// Upright, squarer, and in colours nothing in the horde uses.
    ///
    /// The player is the one body that must never be mistaken for one of them for
    /// even a frame, and in a crowd the only channel with any bandwidth left is
    /// hue. Blue against a horde of greens, greys and reds.
    public static Build ForPlayer(float height) =>
        new(height, 0.48f, 0.065f, 0.24f, 4.0f, 0.60f, 0.50f, 0.040f,
            new Color(0.22f, 0.34f, 0.52f), new Color(0.26f, 0.30f, 0.38f),
            new Color(0.72f, 0.60f, 0.48f), false);

    /// Fractions of height. Named rather than inlined because they are used twice
    /// each and a body assembled from two slightly different ideas of where the
    /// hip is comes apart when it walks.
    private const float HipFraction = 0.46f;
    private const float ShoulderFraction = 0.80f;
    private const float NeckFraction = 0.86f;
    private const float HeadFraction = 0.93f;
    private const float HeadRadiusFraction = 0.070f;

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
            mesh.Tube(knee, hip, spec.LimbRadius * 1.05f, trousers);

            // An eighth of a turn makes the lower leg lag visibly without making
            // the knee pop apart at full stride; a quarter-turn looked jointed in
            // profile but opened a hand-wide gap on the runner's fastest frame.
            mesh.SetRig(spec.LegSwing * 0.72f, kneeY, phase + 0.08f, spec.Bob);
            mesh.Tube(ankle, knee, spec.LimbRadius * 0.88f, spec.Limb);
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
            mesh.Box(Lean(new Vector3(0.0f, hipY + chestHeight * 0.60f, 0.0f), lean, hipY),
                     new Vector3(spec.ShoulderWidth * 0.82f, chestHeight * 0.72f, spec.TorsoDepth),
                     spec.Torso);
        }

        // Separating hips, ribs and shoulder line costs two boxes but removes the
        // wardrobe silhouette: the waist can now pinch while the brute keeps the
        // full width which is its warning at fog distance.
        mesh.Box(Lean(new Vector3(0.0f, hipY + chestHeight * 0.10f, 0.0f), lean, hipY),
                 new Vector3(spec.ShoulderWidth * 0.58f, chestHeight * 0.25f,
                             spec.TorsoDepth * 0.92f), trousers);
        mesh.Box(Lean(new Vector3(0.0f, shoulderY - chestHeight * 0.06f, 0.0f), lean, hipY),
                 new Vector3(spec.ShoulderWidth, chestHeight * 0.20f,
                             spec.TorsoDepth * 1.04f), Darken(spec.Torso, 0.86f));

        // --- head ------------------------------------------------------------
        mesh.Tube(Lean(new Vector3(0.0f, shoulderY, 0.0f), lean, hipY),
                  Lean(new Vector3(0.0f, neckY, 0.0f), lean, hipY),
                  spec.LimbRadius * 1.1f, spec.Limb);

        Vector3 head = Lean(new Vector3(0.0f, headY, 0.0f), lean, hipY);
        mesh.Ball(head, headRadius, spec.Head, 6, 4);

        // These project beyond the sphere rather than being decoration painted
        // onto it. The jaw survives as a profile from the side; the brow makes a
        // dark eye socket from the front when the face is only three pixels tall.
        mesh.Box(head + new Vector3(0.0f, -headRadius * 0.48f, -headRadius * 0.50f),
                 new Vector3(headRadius * 1.18f, headRadius * 0.62f, headRadius * 0.72f),
                 Darken(spec.Head, 0.78f));
        mesh.Box(head + new Vector3(0.0f, headRadius * 0.12f, -headRadius * 0.82f),
                 new Vector3(headRadius * 1.48f, headRadius * 0.24f, headRadius * 0.22f), shadow);

        // --- arms ------------------------------------------------------------
        // Counter-phased against the leg on the same side, which is what stops a
        // walk reading as a march. The pivot is the shoulder, after the lean has
        // moved it — an arm swinging about where the shoulder would have been
        // upright detaches from a leaning body at the top of every stride.
        for (int side = 0; side < 2; side++)
        {
            // Clear of the torso, not flush with it. At exactly `half` the arm
            // centre sits on the chest's own surface and half the tube is buried
            // inside it — which does not look like a bug in a screenshot, it looks
            // like a body with no arms. The silhouette is the only thing carrying
            // information at the distance most of the horde is seen from, and an
            // arm that is not in the silhouette may as well not have been built.
            float x = (side == 0 ? -1.0f : 1.0f) * (half + armRadius);
            Vector3 shoulder = Lean(new Vector3(x, shoulderY, 0.0f), lean, hipY);
            // Width alone also selects the runner, whose swept-back compact arms
            // are part of its arrowhead outline. The modest lean cutoff leaves
            // the narrow, stooped spitter as the only body reaching below its hip.
            float armLength = chestHeight *
                (spec.ShoulderWidth < 0.40f && spec.LeanDegrees < 20.0f ? 1.34f : 1.16f);
            Vector3 elbow = shoulder + new Vector3(0.0f, -armLength * 0.52f, -armRadius * 0.30f);
            Vector3 wrist = shoulder + new Vector3(0.0f, -armLength, armRadius * 0.18f);
            float phase = side * 0.5f + 0.5f;

            mesh.SetRig(spec.ArmSwing, shoulder.Y, phase, spec.Bob);
            mesh.Tube(shoulder, elbow, armRadius, spec.Limb);

            mesh.SetRig(spec.ArmSwing * 0.68f, elbow.Y, phase + 0.08f, spec.Bob);
            mesh.Tube(elbow, wrist, armRadius * 0.82f, spec.Head);
            mesh.Box(wrist + new Vector3(0.0f, -armRadius * 0.75f, -armRadius * 0.10f),
                     new Vector3(armRadius * 1.65f, armRadius * 1.75f, armRadius * 1.25f),
                     spec.Head);
        }

        mesh.ClearRig();
        return mesh.Build();
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
