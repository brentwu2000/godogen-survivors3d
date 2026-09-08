# The art direction, and the brief anyone sourcing a model has to work to

Godot｜MultiMesh horde｜cel-shaded stylised

This file exists to be handed to whoever is looking for assets — a person, an
asset-store search, or a model asked to find some. It is not a wish list. **Most
of it is a list of things this renderer cannot do**, because that is what
actually decides which of two good-looking models is usable, and it is not
guessable from a screenshot.

**§10 is for whoever can change the model rather than only choose one**, and it
is where the five things worth asking a character artist for live.

Read §1 before §5. A model that fails §1 cannot be fixed by any amount of work
downstream, and three of the four most tempting properties an asset page
advertises — animations, blend shapes, a high polygon count — are worth nothing
here or actively cost.

---

## 1. What the renderer is, and what that forbids

**The horde is one `MultiMesh` per variant.** One mesh, N instance transforms.
That is how two hundred bodies cost 1.64 ms — see §2, and note that the 6.90 ms
this line used to quote was a headless measurement of nothing. It has been
defended since Phase 2 and it is defended by the draw call count rather than by
the triangle count.

A `MultiMesh` has **no skeleton**. So the walk is a function evaluated per vertex
in `body.gdshader`, from a rig baked into the mesh: a swing amplitude and a pivot
height per vertex, and the limb turns rigidly about that pivot. Everything below
follows from that one fact.

| The asset page says | Here |
| :--- | :--- |
| Rigged | **Useful** — the rig is read once, at bake time, to decide which vertices are a leg and where the hip is |
| 8 animations: idle / walk / attack / hit / death | **Not used.** `BakeBody` runs *one* frame of *one* animation to get out of the T-pose, freezes the geometry, and the walk after that is a sine function |
| Blend shapes for face / body variants | **Impossible.** There is no per-instance mesh state |
| Swap heads, hats, clothes at runtime | **Impossible** for the horde. One mesh per variant; a second combination is a second `MultiMesh` and a second draw call |
| LOD levels | Not used. A `MultiMesh` draws one mesh |
| 4K PBR textures with normal / roughness / metallic maps | Only the albedo is read, and only to make vertex colours or one 512 atlas layer. Normal maps do nothing on flat-shaded facets |
| 20K, 50K, 140K triangles | See §2. This is the number that decides everything |

**A bending knee cannot be expressed.** The shader turns a whole limb about a
fixed pivot. A model whose appeal is its animation quality is a model whose
appeal does not survive intake.

**What varies per instance**, and is already built: scale, a hue rotation for
elites, a brightness jitter, the stride phase and pace, and a hit flash. Per-body
variety comes from those and from the three painted atlas categories — not from
mesh variants.

---

## 2. The triangle budget, measured rather than guessed

Cost is **triangles × instances on screen**. There is no skinning cost, no
animation cost and no per-enemy node, so this is the *only* thing that scales.

**Measured on this machine, 200 walkers, same session, one variable.** Each row
is the same scene with a different body on the shelf — RTX 3070 Ti, 1080p, vsync
off, player moving so the flow field rebuilds:

| Body | Triangles each | On screen | Total | Frame mean | Median | p95 | Draw calls |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Procedural | ~460 | 200 | 92,000 | 1.64 ms | 0.90 ms | 2.03 ms | 70 |
| Polyart zombie | 1,650 | 200 | 330,000 | **1.44 ms** | 1.28 ms | 2.45 ms | 68 |
| Tactical character | 23,822 | 200 | 4,764,400 | 3.41 ms | 3.12 ms | 5.19 ms | 76 |
| RIN v1, as the player | 27,488 | 1 | 27,488 | 1.30 ms | 0.91 ms | 2.03 ms | 70 |
| RIN v2, as the player | 51,353 | 1 | 51,353 | 1.32 ms | 0.95 ms | 1.94 ms | 70 |

**A 3.6x increase in triangles cost nothing measurable, and a 52x increase cost
2.1x the frame time and still held 293 fps.** The horde is not triangle-bound
anywhere near the band this file used to give. 500 mixed enemies on procedural
bodies measure 1.23 ms and 91 draw calls, so the roster is not the cost either.

**The old number in this table was 6.90 ms and it measured no rendering at all.**
`ART.md` said to measure with `godot --headless --script test/HordePerf.cs`, and
under the dummy driver every one of the three rows above reports 6.90 ms and
145 fps to the decimal — 460 triangles and 23,822 alike — with `avg draw calls 0`
printed underneath, which read as a `MultiMesh` triumph. `HordePerf` calls
`Display.Required` now and refuses to run headless. Every tier below was reasoned
from that number.

| Tier | On screen | Budget each | Decimation |
| :--- | ---: | ---: | :--- |
| Horde — walker, runner, spitter | 150 | up to ~4,000 | Rarely, on this GPU |
| Standard — brute | 10–20 | up to ~20,000 | No |
| Boss / elite | 1–2 | 20,000–40,000 | No |
| **Player** | **1** | **up to ~120,000** | **Never** |

**The player row's limit is the repository, not the renderer.** Doubling the
player from 27,488 to 51,353 triangles moved the frame mean by 0.02 ms, which is
noise — one body's triangles do not register against a horde that is spending
92,000 to 4.7 million. What does register is that the bake is committed and the
source is not: RIN v1's `.res` is 1.6 MB and v2's is 2.9 MB, so ~120,000
triangles is where a survivor starts costing seven megabytes of tracked binary
per version. The other reason to stop is the mobile GPU, which is unmeasured
here as it is everywhere.

The horde row is the only one that moved and it is deliberately not the measured
ceiling. 23,822 at 200 instances runs, so ~4,000 is not a limit — it is a margin
against the two things this measurement does not cover: **a mobile GPU, which is
unmeasured everywhere in this project**, and a horde of *several* authored
variants at once rather than one repeated 200 times. Bring a 2,000-triangle body
and there is no question to ask; bring a 6,000-triangle one and the answer is
probably still yes and wants one `HordePerf` run.

**A boss is one to two on screen, and that was always the free tier.** Every
variant has its own `MultiMesh` and its `VisibleInstanceCount` is the number
alive, so a 30K boss costs 60K triangles total — less than the walker horde, in
an architecture most asset advice is not written for.

**The player is the cheapest body in the game and the only one the camera is
pointed at**, which makes it the first place to spend and the last place this
project looked. It is one draw call at whatever the model arrives as, it is
centre-frame for the whole run, and it is the one body a screenshot is *of*.

Anything can still be brought into range: `art-src/models/decimate.py` runs
Blender's collapse decimation and is verified on the two largest models in the
tree — 28,004 -> 1,760, and 132,169 -> 1,978 **with the skeleton and all
seventeen skinned meshes intact**. What has changed is how rarely it is needed.

So a high triangle count is not disqualifying. It is a step, the step costs
silhouette detail, and the step is usually unnecessary.

---

## 3. The art direction

**Cel-shaded stylised, and the reason is technical before it is aesthetic.**

`cel.gdshaderinc` bands the diffuse into flat tones with a hard terminator, plus
a fresnel rim along the silhouette, and every solid surface in the arena includes
it: the horde and the player through `body.gdshader`, cover and scenery through
`prop.gdshader`, the floor through `ground.gdshader`. That was not a style choice
applied to a realistic game; it was the correct lighting model for geometry that
is entirely flat facets. A smooth cosine falloff describes light on a curved
surface, and a box face is not one — it receives exactly one amount of light, and
the gradient across it was describing nothing.

Which means:

**Realistic assets fight the renderer.** Realism is carried by subsurface
scattering, normal-mapped detail and believable muscle deformation. None of the
three exist here. A realistic model arrives correct in proportion and material
and moves like a mannequin — a poor man's uncanny valley.

**Stylised assets assume what this engine already is.** Flat colour, simplified
geometry, large readable silhouettes, posed rather than simulated motion, colour
doing the work that lighting does elsewhere. That is the same language as
"rigid limb swing, vertex colours, no normal map" — which is a description of
authoring style and, since §2 was measured, not of a triangle count.

Concretely, a candidate should have:

- **Flat or lightly ramped colour**, not photo-sourced texture. Grunge fights the
  bands; the bands are the look.
- **A large, clear silhouette.** At 25 pixels a head, the outline is the whole
  read. Fussy fringes, thin straps and long hair are triangles spent below the
  resolution anyone is looking at.
- **Colour blocked by part** — a shirt colour, a trouser colour, a skin colour —
  because `BakeBody` samples the albedo per vertex, and a flat patch samples
  exactly where a gradient samples approximately.

---

## 4. What a model can never be here

Four of the nine variants are shapes whose *silhouette is the gameplay tell*, and
no humanoid can be any of them. These stay procedural whatever else changes:

| Variant | Why |
| :--- | :--- |
| **stalker** | Quadruped. Currently the one imported bake in the game |
| **bulwark** | Wider than it is tall. A wall that walks; the horizontal outline *is* the warning |
| **bloater** | A belly on legs. Roundness alone means "do not stand next to this" |
| **lantern** | Needs an emissive organ in the chest, driven through vertex-colour alpha |

Everything a pack of humanoids can cover is: **walker, runner, spitter, brute,
boss** — five of nine, and the great majority of what is on screen.

---

## 5. The shopping list

### Search these, for the horde

> `stylized low poly zombie rigged`
> `toon zombie character game ready`
> `cel shaded character low poly`
> `low poly character pack rigged CC0`

**"stylized low poly" is the phrase that works.** It is what the people making
the right thing call it.

### Search these, for the player

> `game ready low poly tactical character`
> `anime low poly character rigged`

**A different budget is a different search, and this file said the opposite for
one phase.** `anime` was under "do not search" on the grounds that
VRoid-lineage models are 10,000–50,000 triangles built around their own outline
pass and two-tone toon shader — every word of which is true, and none of which
disqualifies anything. The triangle count is a horde objection and the player is
not in the horde. The outline pass and the toon shader are simply discarded on
intake, along with the animations and the normal maps, and `body.gdshader`'s own
two bands and fresnel rim are the same look arrived at from the other direction.

The survivor the player controls is one of these: 23,822 triangles, a
`mixamorig_` rig, and the first authored humanoid in three rounds to be better
than what it replaced. The one property it needed and a stylised zombie pack
does not have is **kit** — a plate carrier, gloves, a holster, boots — because
that is what `CHARACTERS.md` says a survivor wears and what `MeshBuilder` will
never model.

### Do not search these

**`realistic`**, **`PBR`**, **`photoscan`**, **`8K textures`** — all describe
qualities this renderer discards.

### Reject on sight

- Over 40,000 triangles with no lower-poly version in the pack — decimating that
  far destroys the silhouette. **A horde rule.** For the player the number is
  ~120,000 and the reason is the size of the committed bake; see §2
- Static / unrigged, unless it is scenery. A body needs a rig to be classified
  into legs and arms
- Bone names that are not recognisable words. `BakeBody.Classify` matches
  `Thigh` / `Shin` / `Calf` / `Foot` and `UpperArm` / `Forearm` / `Hand`, so 3ds
  Max biped names (`bip L Thigh007`) and Mixamo's (`mixamorig_LeftUpLeg_074`)
  both work and `Bone.023` does not. **"Mixamo" on an asset page is a
  compatibility guarantee for this bake**, because that skeleton's names are
  fixed and every one of them is a word
- Sold as an animation set — the animations do not survive

### Prefer

- **Packs of several characters** over one character with variant systems. Ten
  meshes at 1,700 triangles beats one mesh at 19,500 with blend shapes this
  engine cannot use. The Polyart pack is the right shape of thing
- Models that already ship a *walk* or *idle* pose that reads well standing
  still, because one frozen frame is all that is kept

---

## 6. Licences, and which ones can be committed

This repository is **public and commits its assets as files**, which is what
makes this a real constraint rather than a formality. `assets/models/SOURCE.md`
is the register; every download goes in it with a URL, a sha256 and a date.

| Licence | Commit the source file? | Commit a bake? | Notes |
| :--- | :---: | :---: | :--- |
| **CC0** | Yes | Yes | The default answer. Kenney is CC0 |
| **CC BY** | Yes | Yes | **Requires attribution reaching the player** — see the gap below |
| **Sketchfab Free Standard** | **No** | Yes, as a derivative work | Use in a product is permitted; redistributing the file is not. The Polyart pack is this |
| **Quaternius QAL** | Judgement call, already made once | Yes | §3(a) forbids redistributing the asset. The props are here under a written-down decision |
| **CC BY-NC / NC-ND** | No | No | Non-commercial forecloses shipping |
| **"Royalty free"** | Unknown | Unknown | Not a licence. It is a pricing model. Find the actual terms |

**The attribution gap is closed, and it closed because a CC-BY asset arrived.**
This paragraph used to say the credit has to reach the end user, that the game
had no credits screen, and that any plan built on CC-BY assets carried that as an
unscheduled dependency. The five survivors are CC BY 4.0 — see
`assets/models/SOURCE.md` — so the dependency was scheduled the same phase they
were: `BaseScreen.RosterScreen` carries the notice, on the screen where those
five illustrations and the bodies under them are on display.

Two things follow for anything sourced next. A CC-BY asset is now **cheap** rather
than blocked: the surface exists and adding a line to it is a line. And the
notice is load-bearing — it is not decoration on that screen, and a rewrite of it
that drops those two lines is a licence breach rather than a formatting change.

---

## 7. Intake, in order

Each step exists because something failed without it.

**One command, and it is the list below in order:**

```powershell
powershell art-src/models/intake.ps1 -Model ~/Downloads/thing.glb -Slot walker `
    -Node rig_CharRoot007 -Pose "Take 001@1.0" -Yaw 180
```

It copies the file into `assets/models/`, hashes it, says whether the register
already knows that hash, imports, reports the tree, counts **the named node's**
triangles rather than the file's, checks them against the slot's tier, bakes,
writes the lineup and a screenshot of the running game, and prints the section 8
checklist with everything a script can know already filled in. It stops at the
first step that fails and names the flag that is missing.

Nothing else is needed to get the body into the game. `resources/bodies/<slot>.res`
*is* the body for that slot — see `BodyBakes` — so the bake landing there is the
whole of "applying" it: no `BakedBodyPath`, no rebuild of the resource tables, no
code change. Deleting the file puts the procedural body back.

The steps, for when one of them has to be run alone:

```bash
# 1. What is actually in the file. A pack's totals describe the file, not a body.
godot --headless --script test/ModelReport.cs -- res://assets/models/thing.glb tree

# 2. Bring it into budget, if it is over. Rarely needed since section 2 was measured.
blender --background --python art-src/models/decimate.py -- in.glb out.glb 4000

# 3. Bake. `slot:` resolves both the destination and the height.
godot --headless --script scripts/tools/BakeBody.cs -- \
  res://assets/models/thing.glb slot:walker node:rig_CharRoot007 \
  "pose:Take 001@1.0" yaw:180

# 4. Look at it next to what it is replacing. This is the decision. `raw` draws
#    the procedural body and `baked:` appends the candidate, so both stand in one
#    frame; without `raw` the candidate stands next to itself, because BodyShot
#    reads the shelf too.
godot --script test/BodyShot.cs -- one:walker raw front baked:res://resources/bodies/walker.res

# 5. Measure. Never with --headless: the dummy driver renders nothing and
#    reports 6.90 ms for every triangle count there is.
godot --script test/HordePerf.cs
```

**`slot:` is why steps 3 and 5 stopped being where intakes go wrong.** The
destination and the height were two arguments the caller had to know, and the
height is not a property of the model — it is `DesignHeightMeters` from the enemy
table or `BodyHeight` from the survivor roster. `BakeBody` reads both from the
slot now.

**A survivor skips steps 2 and 5.** Nothing is decimated for a body that draws
once, and `HordePerf` measures a `MultiMesh` the player is not in. Its step 4 is
the roster lineup against the two survivors it stands beside on the select
screen, which `intake.ps1` picks by itself from the slot name:

```bash
godot --script test/BodyShot.cs -- roster front baked:res://resources/bodies/drifter.res
godot --script test/Screenshot.cs
```

`PaletteProbe`'s third stage is the one assertion that covers this: it measures
the player's chroma against the nearest body colour in the horde and needs 0.35
of separation. The tactical survivor reads 0.53 — a dark figure on light ground —
which is the answer to "will the player still be findable in a crowd" and the
one question about a survivor that is not a matter of taste.

**Step 4 is not optional and is not ceremony.** Two rounds of authored humanoids
have entered this game and both were worse than the procedural bodies they
replaced — seven three.js models and the Drifter, all deleted. The lineup shot
is what caught it, and it caught it late both times because nobody took it.

Flags, and the failure each one is for:

| Flag | Without it |
| :--- | :--- |
| `node:<name>` | A pack merges into one body and the bake refuses for having several skeletons |
| `pose:<anim>[@s]` | A rigged model's bind pose is a T-pose, and a T-pose is not a body |
| `yaw:180` | The model faces +Z, this game's bodies face -Z, and it walks at you backwards |
| `<tint hex>` | An albedo taken from the model may be a washed near-white — the stalker's `6b5f52` is there for that reason |

The characters in a pack are the nodes that *contain* a skeleton, not the empty
marker nodes named after them. `lpMale_zombie_C` is a label; `rig_CharRoot007` is
the body.

---

## 8. A candidate checklist

Fill one in per model before downloading anything else.

| | |
| :--- | :--- |
| Name and author | |
| URL | |
| Licence, verbatim from the page | |
| Commit the source? (§6) | |
| Triangles, per character | |
| Characters in the pack | |
| Rigged? Bone names look like words? | |
| Flat colour or photo texture? | |
| Which tier — horde / standard / boss? | |
| Decimation needed, to what? | |

---

## 9. What is not done yet, and is not an asset problem

- ~~**The props and the ground are not cel-shaded.**~~ Done. `cel.gdshaderinc`
  holds the ramp, the shadow floor and the rim, and all three shaders include it:
  `body.gdshader` where it was written, the new `prop.gdshader` — which replaced
  a `StandardMaterial3D` and changed nothing but the lighting — and
  `ground.gdshader`. The floor takes four bands against a body's two, because it
  is the one surface in this game that is genuinely curved: `GroundMesh` builds
  6,561 smooth-shaded vertices from `Terrain`, and two bands across an arena is
  two continents. It also takes no rim: on a floor the whole horizon is
  silhouette, and a fresnel edge draws a bright band exactly where the fog is
  trying to make things disappear.
- **The painted skin plates are semi-realistic.** `skin_infected` and
  `skin_mutant` are rendered rot and hide; flat colour with drawn detail would
  suit the bands better. Regenerable from `art-src/textures/`.
- ~~**The roster is now two species.**~~ Done. All five survivors are authored
  bodies from one production, which is what the entry asked for — and it took two
  files per survivor rather than a modelling project, because the shelf is a
  directory. RIN, MIKA, AKIRA, SORA and YUNA at 38,815 to 45,816 triangles each.
  The horde is still boxes, which is now the only split left.
- **A horde variant's colour is a gameplay signal, and a downloaded body brings
  its own.** One polyart zombie was baked into the walker slot and held back for
  this and nothing else: it is a better body and it is a pale-skinned man, and
  the walker is read as *infected* at 25 pixels by being green. A monster model
  wants its role's colour in its albedo, or one surface per part so `--tint` can
  put it there. `assets/models/SOURCE.md` has the picture and the decision.
- **Half a horde is worse than none.** Nine variants, and an authored one beside
  eight boxes reads as a bug rather than as an upgrade — more visible at 150
  instances than the survivor roster's version of the same split is at three. The
  order to do this in is walker, runner, spitter (the three that fill the screen),
  and then brute and boss; the four in §4 stay procedural forever.
- ~~**No attribution surface**~~. Done, on the roster screen, because CC BY 4.0
  assets arrived and required it. §6.
- **The fog and sky are realistic in hue.** A stylised palette usually wants
  fewer, more saturated steps.
---

## 10. If you can change the model

Everything above is about *choosing* something already made. This section is for
whoever can open the source file and change it — and it is short, because most
of what a character artist would reach for is on the discard list in §1.

Five characters came through this pipeline in one production and every number
below is measured off them rather than reasoned about.

### 10.1 Give the iris geometry. This is the one real defect.

**All five survivors bake with blown-white eyes.** Everything else on them
arrives exact — the jackets, the piping, the buckles, the laces, the lips, the
blush. The eyes do not, and the cause is not fixable at intake.

`BakeBody` samples the albedo **once per vertex**. That is exact for a flat patch
of colour and it is an average for drawn detail: an anime iris is a painting a few
millimetres across inside a UV island that has no vertices in it, so the vertices
around it all land on sclera white and the iris never existed. Tinting the
existing 96-triangle eyeball a bright green to check changed nothing on screen —
what is visible is the *face's own* sclera geometry inside `RIN_Body`, with the
eyeball behind it.

**The fix is a few triangles.** An iris disc and a pupil as their own small
meshes, in front of the sclera, weighted to the eye or head bone. Eight triangles
each is enough: the shader flat-shades, so a disc reads as a disc. Anything with
geometry survives; anything painted does not.

The same rule catches everything else of that kind before it is authored. A
seam, a zip tooth, a stitch, a decal, an eyebrow, a lip line: if it is smaller
than the distance between two vertices, it will not be in the game. Make it
geometry or leave it out.

### 10.2 Emissive is free, and nothing has ever used it

`body.gdshader` reads **`1.0 - COLOR.a`** per vertex as how much that vertex
burns from inside, at 2.4x. It costs nothing — no material, no light, no second
draw — and it is the one channel a cel-shaded game most wants.

Author alpha **1** everywhere, and alpha **0** on the vertices meant to glow:

| Character | What should burn |
| :--- | :--- |
| SORA | the six flying swords, and the glyphs on them |
| MIKA | the drone's eye, the panel strips, the tablet |
| YUNA | the medical green — the cross, the field emitter, the gun's core |
| AKIRA | the blade's edge when it is meant to be hot |
| RIN | the sight's dot, the red plate edges |

Nothing in the roster does this today, so every one of them is matte. It is
probably the largest visual return available per hour of work in this whole
pipeline, and it does not touch the silhouette.

*(The baker dropped vertex alpha for one phase and nothing caught it, because the
only thing using glow is the lantern and the lantern is a procedural body. It
carries alpha through now.)*

### 10.3 Colour: vertex colours or a texture, not both

The bake multiplies the three glTF sources together, which is what the
specification says they are: `COLOR_0` x `baseColorFactor` x `baseColorTexture`.

So a model that carries a full albedo texture **and** meaningful vertex colours
comes out twice-darkened. Pick one:

- **A texture with white `COLOR_0`** — what the five survivors do, and it works:
  at 38,000 vertices against a 2K atlas the face resolves lips and blush.
- **Flat vertex colours with no texture** — better for anything that will be
  small on screen, and the only thing that works for the horde.

White is the multiplicative identity, so "white `COLOR_0`" and "no `COLOR_0`" are
the same thing. What is not safe is a half-populated colour attribute: Blender's
glTF exporter writes `COLOR_0` on **every** mesh of an object that has a colour
attribute anywhere, filling the rest with white — which is fine now and was not
before the multiply, when it silently beat the texture and baked a character
white from head to foot.

### 10.4 One large saturated area per character

`PaletteProbe` requires the player to sit 0.35 from every horde body on the
colour wheel, value deliberately excluded because a dark biome takes value away.
Measured on the bakes, printed by `BakeProbe` on every sweep:

| | Reads as | Chroma from nearest horde body |
| :--- | :--- | ---: |
| RIN | `78686b` | 0.203 |
| The rule | | 0.35 |

Every survivor is a near-black outfit with saturated *accents*, and a mean is not
impressed by an accent. At 25 pixels a body **is** its average colour.

This is not a request to abandon the designs. It is one region big enough to
survive averaging — a coloured jacket rather than coloured piping, a coloured
skirt rather than a coloured tag. Each character already has the colour picked
for them: RIN red, MIKA blue, AKIRA red, YUNA medical green, SORA purple. They
are on the identity panels of the design sheets and almost none of it is on the
bodies.

### 10.5 A one-frame bake pose

The bake freezes **one frame of one animation** and the walk after that is a sine
function added on top. So the frame wants to be the pose that sine is added *to*:
standing square, legs together, arms hanging straight down, nothing held.

An action named `BakePose` with exactly that, one keyframe, in every character,
removes the last guess from an intake. Without one the frame is found by trying:
`Idle@0.0` holds the arms forward and bakes a survivor standing there reaching;
`Walk@0.25` is the pass of a walk cycle and is what these five use.

### 10.6 Do not author these. They are discarded at intake.

Not "not used yet" — read and thrown away, or never read at all.

| | |
| :--- | :--- |
| Animations | One frame of one is kept. Twenty-one named clips per character cost twenty-one clips of work for one pose |
| Facial morphs / blend shapes | Impossible. There is no per-instance mesh state and the bake is one frozen surface |
| Normal maps | Nothing samples them, and they describe curvature on geometry that is flat facets |
| ORM / roughness / metallic | `ROUGHNESS` is computed from the albedo's own luminance. Nothing reads a map |
| LOD chains | One export is used. Ship the tier that will be used and skip the others |
| Their own outline pass or toon shader | Replaced by `cel.gdshaderinc`. A model built around a screen-space outline arrives without one |
| Transparent hair cards | Alpha is the glow channel here. A hair card at alpha 0.5 is a hair card that glows |
| Held weapons | The game appends the silhouette of whatever is equipped. A stowed rifle on the model is a second rifle |

### 10.7 Do author these

| | |
| :--- | :--- |
| Skinned trim | 141 of RIN's 143 meshes are skinned and that is why she needed no flags. An unskinned mesh is skipped unless it hangs off a `BoneAttachment3D` |
| Recognisable bone names | `UpperLeg.R`, `Forearm.L`, `Hand.L`, `Foot.L`, `Toe.L`. `Classify` matches the substrings `thigh/shin/calf/leg/foot/toe` and `arm/hand/shoulder/clavicle`, and takes the side from a `.l` / `.r` suffix or the words. **An unrecognised bone is torso, and a body whose legs are torso stands still while it walks** |
| Feet at the origin, Y-up, metres | The bake normalises the height to the roster's number, so the model's own scale is free — but the *base* has to be the floor |
| Weapons as separate files | As `RIN_AR01.glb` already is |
| One export per character | LOD1-sized, ~40,000 triangles for a survivor. See §2 for why the number barely matters and 10.8 for where it does |

### 10.8 The horde is the bigger prize, and it is a different brief

Five survivors is five bodies the camera is pointed at, and it is nine variants
and a hundred and fifty instances that fill the screen. Everything in §1 to §4
applies to those and most of §10 does not:

- **800–4,000 triangles**, flat vertex colours, **no texture**. A face is
  triangles spent below the resolution anyone is looking at
- **Colour is the gameplay tell.** A walker is read as infected at 25 pixels by
  being green. A pale-skinned zombie is a survivor from a distance — this is
  measured, and it is why the one authored horde body in this repo is held back
- **Four of the nine can never be humanoid** — stalker, bulwark, bloater,
  lantern. §4 says why
- The lantern's chest organ is the one existing use of the alpha glow in 10.2,
  and it is procedural. An authored lantern would want the same
