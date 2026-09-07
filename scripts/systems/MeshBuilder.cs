using Godot;

/// Accumulates boxes, tubes and balls into one `ArrayMesh`, with rig data.
///
/// Every prop in the arena is built from these. Boxes rather than an imported
/// model, for two reasons that are both about failure modes rather than taste:
///
///   MultiMesh silently loses an imported GLB on pack/save (godot.md:46), and
///   MultiMesh is the only way fifty pieces of cover stay inside the draw-call
///   budget this project has defended since Phase 2. A procedural mesh is the
///   combination that is actually allowed.
///
///   A GLB also drags in collision: the primitive shape has to be measured off
///   its AABB, because `CreateTrimeshShape()` on an imported mesh drops the frame
///   rate below one (godot.md:39). Cover that was boxes to begin with already has
///   its collider.
///
/// Normals are written per face as the boxes are emitted, never by calling
/// `GenerateNormals()` afterwards. A procedural mesh with no normals silently
/// fails to receive shadows (godot.md:45) — it renders perfectly and is simply
/// never darkened, which reads as a lighting setting rather than as missing data.
public sealed class MeshBuilder
{
    private readonly System.Collections.Generic.List<Vector3> _vertices = new();
    private readonly System.Collections.Generic.List<Vector3> _normals = new();
    private readonly System.Collections.Generic.List<Color> _colours = new();

    /// Rig data, in **UV2 alone**, as vertices are emitted.
    ///
    /// A MultiMesh has no skeleton — there is one mesh and N transforms, and
    /// nothing per-instance a bone could hang off. So the walk is a function
    /// evaluated in the vertex stage, and everything it needs about *this vertex*
    /// has to be baked into the mesh at build time. There is nowhere else to put
    /// it: the per-instance custom data is four floats shared by every vertex of
    /// the instance, which is the right home for "how fast is this one walking"
    /// and the wrong home for "which limb is this".
    ///
    /// **It used to occupy both channels, and that is why these bodies had no
    /// textures.** Only three of the four numbers are per-vertex, and the fourth
    /// was sitting in the channel a painted surface needs.
    ///
    ///   UV2.x  swing, in radians at full pace. Zero for anything that does not
    ///          move, which is most of a body.
    ///   UV2.y  the pivot height and the phase, packed. Rotation is about the X
    ///          axis through that height, so a leg swings from the hip rather
    ///          than about the floor — and no other component of the pivot is
    ///          needed, because a limb that swings fore-and-aft pivots on a line
    ///          parallel to X and each vertex already carries its own X. Phase is
    ///          the offset in turns; 0.5 is what makes the left leg the opposite
    ///          of the right one.
    ///
    /// The pack is `floor(pivotY * 100) + phase`, decoded with `floor` and
    /// `fract` — the same trick `body.gdshader` already plays on pace and phase
    /// in `INSTANCE_CUSTOM.x`, and for the same reason. A centimetre of
    /// quantisation on a rotation pivot is invisible; a body with no texture is
    /// not.
    ///
    /// The bob left entirely. It is `spec.Bob` at every one of the six call
    /// sites — one number per *body*, not per vertex — so it belongs on the
    /// material, and `body.gdshader` reads it as the uniform `body_bob`.
    private Vector2 _rigUv2;

    private readonly System.Collections.Generic.List<Vector2> _uvs = new();
    private readonly System.Collections.Generic.List<Vector2> _uv2s = new();

    /// Where in the body atlas everything emitted from here on is painted.
    ///
    /// A rectangle in 0..1 texture space. Each primitive maps its own natural
    /// parameterisation into it — a tube's angle around and distance along, a
    /// ball's azimuth and polar — so a caller places a *part* rather than a
    /// vertex, which is the only granularity a procedural body has.
    ///
    /// Zero-sized by default, which sends every vertex to (0, 0). `BodyAtlas`
    /// reserves that corner as a flat patch for exactly this reason: a prop, or a
    /// part nobody has dressed, comes out its own colour rather than wearing
    /// whatever happens to be at the origin.
    public Rect2 UvRect { get; set; }

    private Vector2 Map(float u, float v) =>
        new(UvRect.Position.X + u * UvRect.Size.X,
            UvRect.Position.Y + v * UvRect.Size.Y);

    /// Sets the rig data for everything emitted from here on.
    ///
    /// Current-state rather than a parameter on every call, because a limb is
    /// several primitives — an arm is a tube and a hand — and repeating four
    /// numbers at each one is how two of them end up disagreeing.
    ///
    /// Not reset automatically. A builder that quietly returned to rest after
    /// each shape would make the common case, a whole limb, the one needing
    /// ceremony.
    /// `bobMetres` is accepted and ignored, and stays in the signature because it
    /// is the number every call site has in hand: dropping it would make six of
    /// them read as though the bob had been forgotten. `BodyRenderer` and
    /// `SoloBody` push it to the material instead.
    public void SetRig(float swingRadians, float pivotY, float phaseTurns, float bobMetres)
    {
        // Wrapped into [0, 1) before packing. One call site passes a phase of
        // exactly 1.0 — the carried weapon, a whole turn round from zero and
        // identical to it — and an unwrapped 1.0 would land in the integer part
        // and move that limb's pivot a centimetre up the body.
        float phase = phaseTurns - Mathf.Floor(phaseTurns);

        _rigUv2 = new Vector2(swingRadians, Mathf.Floor(pivotY * 100.0f) + phase);
    }

    /// Back to a part that does not move.
    public void ClearRig() => SetRig(0.0f, 0.0f, 0.0f, 0.0f);

    /// The six faces of a unit cube, as (normal, and the four corners in
    /// counter-clockwise order seen from outside).
    ///
    /// Winding is the thing to get right rather than to work around: turning on
    /// `CullMode.Disabled` to hide a backwards face also removes the shadow, so
    /// the "safety net" is what breaks the lighting.
    private static readonly (Vector3 Normal, Vector3 A, Vector3 B, Vector3 C, Vector3 D)[] Faces =
    {
        (Vector3.Up,      new(-1, 1, -1), new(-1, 1, 1), new(1, 1, 1), new(1, 1, -1)),
        (Vector3.Down,    new(-1, -1, 1), new(-1, -1, -1), new(1, -1, -1), new(1, -1, 1)),
        (Vector3.Forward, new(-1, -1, -1), new(-1, 1, -1), new(1, 1, -1), new(1, -1, -1)),
        (Vector3.Back,    new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1), new(-1, -1, 1)),
        (Vector3.Left,    new(-1, -1, 1), new(-1, 1, 1), new(-1, 1, -1), new(-1, -1, -1)),
        (Vector3.Right,   new(1, -1, -1), new(1, 1, -1), new(1, 1, 1), new(1, -1, 1)),
    };

    /// A box, centred on `centre`, `size` across, optionally spun about Y.
    public void Box(Vector3 centre, Vector3 size, Color colour, float yawDegrees = 0.0f)
    {
        // Converted here, once, because vertex colours and texture pixels do not
        // mean the same thing. A PNG imported as `source_color` is decoded from
        // sRGB to linear before it is shaded; a vertex colour is handed to the
        // shader as-is and treated as linear already. Writing 0.46 in both places
        // therefore produces two visibly different greys — the props came out
        // roughly the square root of their intended value, which read as every
        // piece of cover being made of polystyrene next to an asphalt floor that
        // was correct.
        colour = colour.SrgbToLinear();

        Vector3 half = size * 0.5f;
        float yaw = Mathf.DegToRad(yawDegrees);
        float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);

        foreach ((Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d) in Faces)
            Quad(Place(a), Place(b), Place(c), Place(d), Spin(normal, cos, sin), colour);

        Vector3 Place(Vector3 corner) => centre + Spin(corner * half, cos, sin);
    }

    /// A quad given counter-clockwise as seen from outside.
    ///
    /// The reversal to Godot's clockwise-is-front convention happens here and
    /// nowhere else, so every shape below can be reasoned about the way the
    /// geometry is drawn on paper. Wound the other way the shapes still render —
    /// as their own interiors, lit from behind — which looks like a material
    /// problem rather than a winding one, and the usual fix for it
    /// (`CullMode.Disabled`) also removes the shadow.
    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color colour) =>
        Quad(a, b, c, d, normal, colour,
             Map(0.0f, 0.0f), Map(0.0f, 1.0f), Map(1.0f, 1.0f), Map(1.0f, 0.0f));

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color colour,
                      Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
    {
        Triangle(c, b, a, normal, colour, uvC, uvB, uvA);
        Triangle(d, c, a, normal, colour, uvD, uvC, uvA);
    }

    /// A triangle given counter-clockwise as seen from outside.
    private void TriangleOutward(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color colour) =>
        Triangle(c, b, a, normal, colour);

    private void TriangleOutward(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color colour,
                                 Vector2 uvA, Vector2 uvB, Vector2 uvC) =>
        Triangle(c, b, a, normal, colour, uvC, uvB, uvA);

    /// A limb: a prism from `from` to `to`, `sides` around, capped at both ends.
    ///
    /// Faceted, and with few sides. Same decision as the boxes — a six-sided arm
    /// at the distance this camera sits reads as an arm, and every extra side is
    /// vertices multiplied by however many of the horde are on screen.
    ///
    /// Per-face normals, never `GenerateNormals()`: a procedural mesh with no
    /// normals silently fails to receive shadows (godot.md:45), and a smoothed
    /// normal on a six-sided tube would light it as though it were round, which
    /// is a lie the silhouette contradicts from every angle.
    public void Tube(Vector3 from, Vector3 to, float radius, Color colour, int sides = 6) =>
        Tube(from, to, radius, radius, colour, sides);

    /// A tube that is not the same width at both ends.
    ///
    /// **This is the cheapest thing in the project that makes a body stop looking
    /// like plumbing, and it costs exactly zero extra triangles.** Every limb was
    /// a constant-radius cylinder: a thigh the same width at the knee as at the
    /// hip, a forearm the same width at the wrist as at the elbow. Nothing about
    /// a person is, and a stack of equal cylinders reads as pipework however good
    /// the proportions between them are.
    ///
    /// The two rings were already being generated separately — the only change is
    /// that they no longer have to share a number.
    public void Tube(Vector3 from, Vector3 to, float fromRadius, float toRadius, Color colour,
                     int sides = 6)
    {
        colour = colour.SrgbToLinear();

        Vector3 axis = to - from;
        float length = axis.Length();
        if (length < 0.0001f || (fromRadius <= 0.0f && toRadius <= 0.0f) || sides < 3)
            return;

        axis /= length;

        // Any perpendicular will do — the tube has no texture and no seam, so
        // where the facets land around it does not matter. Crossed against
        // whichever world axis is least parallel, because crossing with a
        // parallel one gives a zero vector and a limb that silently vanishes.
        Vector3 reference = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up;
        Vector3 u = axis.Cross(reference).Normalized();
        Vector3 v = axis.Cross(u);

        var lower = new Vector3[sides];
        var upper = new Vector3[sides];
        var outward = new Vector3[sides];

        for (int i = 0; i < sides; i++)
        {
            float angle = Mathf.Tau * i / sides;
            Vector3 direction = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
            outward[i] = direction;
            lower[i] = from + direction * fromRadius;
            upper[i] = to + direction * toRadius;
        }

        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;

            // Counter-clockwise seen from outside: along the surface in the
            // direction of increasing angle at the near end, then back at the far
            // end. The face normal is the average of the two ring directions
            // rather than either, so a flat facet is lit as the flat facet it is
            // instead of as one of its edges.
            //
            // U runs round the limb and V along it, `from` at the bottom of the
            // rect. `i + 1` rather than `j`, so the last facet's far edge is at
            // u = 1 instead of wrapping to u = 0 and mirroring the whole strip
            // back across itself in one triangle.
            Quad(lower[i], lower[j], upper[j], upper[i], (outward[i] + outward[j]).Normalized(),
                 colour,
                 Map((float)i / sides, 1.0f), Map((float)(i + 1) / sides, 1.0f),
                 Map((float)(i + 1) / sides, 0.0f), Map((float)i / sides, 0.0f));
        }

        // Caps, wound opposite to each other. Both are fans from a ring centre;
        // "counter-clockwise from outside" reverses when the outside is the other
        // end of the tube, which is the one thing about capping that is easy to
        // get backwards and invisible until something looks hollow.
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            TriangleOutward(to, upper[i], upper[j], axis, colour);
            TriangleOutward(from, lower[j], lower[i], -axis, colour);
        }
    }

    /// A tapered tube with an oval cross-section.
    ///
    /// **A torso is not a box and it is not a cylinder.** It was a box, and a box
    /// is a crate: four hard vertical edges catching the light in four flat
    /// bands, which is the single thing that made these bodies read as furniture
    /// with legs. A round tube is no better — a person is much wider than they
    /// are deep, and a cylindrical chest reads as a barrel someone is wearing.
    ///
    /// Two radii per ring, across and front-to-back, so a chest can be broad and
    /// shallow and a waist can pinch in one axis without pinching in the other.
    /// Eight sides costs twenty triangles more than the box it replaces and is
    /// the best-spent twenty in the file.
    public void Barrel(Vector3 from, Vector3 to, Vector2 fromRadii, Vector2 toRadii,
                       Color colour, int sides = 8)
    {
        colour = colour.SrgbToLinear();

        Vector3 axis = to - from;
        float length = axis.Length();
        if (length < 0.0001f || sides < 3)
            return;

        axis /= length;

        Vector3 reference = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up;
        Vector3 u = axis.Cross(reference).Normalized();
        Vector3 v = axis.Cross(u);

        var lower = new Vector3[sides];
        var upper = new Vector3[sides];
        var outward = new Vector3[sides];

        for (int i = 0; i < sides; i++)
        {
            float angle = Mathf.Tau * i / sides;
            float c = Mathf.Cos(angle), sn = Mathf.Sin(angle);

            lower[i] = from + u * (c * fromRadii.X) + v * (sn * fromRadii.Y);
            upper[i] = to + u * (c * toRadii.X) + v * (sn * toRadii.Y);

            // The normal of an oval is not the direction to the point on it: a
            // wide flat chest lit as though it were round comes out shaded like a
            // pipe. Scaling the two components by the *other* radius is the
            // ellipse's own gradient, and it is the difference between a torso
            // that turns away from the light and one that rolls.
            outward[i] = (u * (c * fromRadii.Y) + v * (sn * fromRadii.X)).Normalized();
        }

        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Quad(lower[i], lower[j], upper[j], upper[i], (outward[i] + outward[j]).Normalized(),
                 colour,
                 Map((float)i / sides, 1.0f), Map((float)(i + 1) / sides, 1.0f),
                 Map((float)(i + 1) / sides, 0.0f), Map((float)i / sides, 0.0f));
        }

        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            TriangleOutward(to, upper[i], upper[j], axis, colour);
            TriangleOutward(from, lower[j], lower[i], -axis, colour);
        }
    }

    /// A head: a low-poly sphere of latitude bands, flat shaded.
    ///
    /// Few enough facets to still read as carved, which is the look the rest of
    /// the geometry has.
    public void Ball(Vector3 centre, float radius, Color colour, int segments = 8, int rings = 5)
    {
        colour = colour.SrgbToLinear();

        if (radius <= 0.0f || segments < 3 || rings < 2)
            return;

        Vector3 At(int ring, int segment)
        {
            float polar = Mathf.Pi * ring / rings;
            float azimuth = Mathf.Tau * (segment % segments) / segments;
            return centre + new Vector3(
                Mathf.Sin(polar) * Mathf.Cos(azimuth),
                Mathf.Cos(polar),
                Mathf.Sin(polar) * Mathf.Sin(azimuth)) * radius;
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                Vector3 a = At(ring, segment);
                Vector3 b = At(ring, segment + 1);
                Vector3 c = At(ring + 1, segment + 1);
                Vector3 d = At(ring + 1, segment);

                // The poles collapse a quad to a triangle. Emitting it as a quad
                // anyway would add a degenerate triangle with an undefined normal,
                // which shades as a black speck exactly at the crown of every head.
                // Equirectangular: u round the azimuth, v from the crown down.
                // The azimuth starts at +X and turns toward +Z, so the front of a
                // body — which faces -Z — is three quarters of the way round,
                // at u = 0.75. `BodyAtlas` paints the face there.
                float ua = (float)segment / segments;
                float ub = (float)(segment + 1) / segments;
                float va = (float)ring / rings;
                float vb = (float)(ring + 1) / rings;

                if (ring == 0)
                    TriangleOutward(a, c, d, FaceNormal(a, c, d, centre), colour,
                                    Map(ua, va), Map(ub, vb), Map(ua, vb));
                else if (ring == rings - 1)
                    TriangleOutward(a, b, c, FaceNormal(a, b, c, centre), colour,
                                    Map(ua, va), Map(ub, va), Map(ub, vb));
                else
                    Quad(a, b, c, d, FaceNormal(a, b, c, centre), colour,
                         Map(ua, va), Map(ub, va), Map(ub, vb), Map(ua, vb));
            }
        }
    }

    /// The outward normal of a face, disambiguated by which side the inside is on.
    ///
    /// A cross product alone gives the right line and a coin-flip for the
    /// direction. On a sphere the answer is never ambiguous, because there is a
    /// known interior to point away from.
    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
    {
        Vector3 normal = (b - a).Cross(c - a);
        if (normal.LengthSquared() < 0.0000001f)
            return (a - inside).Normalized();

        normal = normal.Normalized();
        return normal.Dot(a - inside) < 0.0f ? -normal : normal;
    }

    private static Vector3 Spin(Vector3 v, float cos, float sin) =>
        new(v.X * cos - v.Z * sin, v.Y, v.X * sin + v.Z * cos);

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color colour) =>
        Triangle(a, b, c, normal, colour, Map(0.5f, 0.5f), Map(0.5f, 0.5f), Map(0.5f, 0.5f));

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color colour,
                          Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
        _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
        _colours.Add(colour); _colours.Add(colour); _colours.Add(colour);

        _uvs.Add(uvA); _uvs.Add(uvB); _uvs.Add(uvC);

        // Whole triangles share one rig, because a triangle straddling two limbs
        // would tear open as they swung apart. The body library keeps limbs as
        // separate primitives for exactly this reason. The *texture* coordinate
        // is per-vertex, which is the whole difference between the two channels.
        _uv2s.Add(_rigUv2); _uv2s.Add(_rigUv2); _uv2s.Add(_rigUv2);
    }

    public int TriangleCount => _vertices.Count / 3;

    public ArrayMesh Build()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray();

        // Always written, even when every value is zero. A surface built without
        // them carries no UV format bit, and a shader reading UV then gets
        // whatever the vertex format leaves there — so a prop drawn with the body
        // shader would animate by garbage instead of standing still.
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = _uv2s.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// The height of the tallest vertex, for anything that needs to know how far
    /// a prop sticks up without keeping a second copy of its dimensions.
    public float Height
    {
        get
        {
            float top = 0.0f;
            foreach (Vector3 vertex in _vertices)
                top = Mathf.Max(top, vertex.Y);

            return top;
        }
    }
}
