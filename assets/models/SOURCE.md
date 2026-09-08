# Where the imported models came from

The record the project owes itself for anything it did not draw. Same shape as
`art-src/fonts/SOURCE.md`, and it exists because the survivor roster is blocked
on exactly this: `art-src/models/base/rigged_anime_girl_cc0.blend` is ten
megabytes of third-party geometry whose only claim to a licence is its own
filename — no URL, no hash, nothing anyone can check. Nothing there is a breach;
what is missing is the ability to demonstrate that.

A URL and a sha256 is the whole requirement. Both below are checkable in one
command.

## Kenney — Mini Characters 1.0

| | |
| :--- | :--- |
| Pack | Mini Characters (1.0), created 17-07-2024 |
| Author | Kenney — <https://kenney.nl> |
| Page | <https://kenney.nl/assets/mini-characters> |
| Archive | <https://kenney.nl/media/pages/assets/mini-characters/bfc7e272b4-1774770718/kenney_mini-characters.zip> |
| sha256 (archive) | `9e1d48e6d7b8479ebbe84df71eb5bd8e1b3f0da546dea641890dccc8a02d0999` |
| Licence | CC0 1.0 — <http://creativecommons.org/publicdomain/zero/1.0/> |
| Attribution | Not required. Kenney asks to be credited as support; this file is that credit. |

Files taken from `Models/GLB format/` in that archive:

| In repo | From | sha256 |
| :--- | :--- | :--- |
| `kenney_survivor_a.glb` | `character-male-a.glb` | `77572792bfe2773b715b8cd8e18644b52b3e1f155fe10450254b50f9c364382a` |
| `Textures/colormap.png` | `Models/GLB format/Textures/colormap.png` | shipped alongside; the `.glb` references it by relative path |

## Kenney — Blocky Characters 2.0

| | |
| :--- | :--- |
| Page | <https://kenney.nl/assets/blocky-characters> |
| Archive | <https://kenney.nl/media/pages/assets/blocky-characters/8369c0cf30-1749547469/kenney_blocky-characters_20.zip> |
| sha256 (archive) | `5e123859aa0c1598342b600c6db197024a1d63eb9ec531398b310725f589887e` |
| Licence | CC0 1.0 — same terms as above |

| In repo | From | sha256 |
| :--- | :--- | :--- |
| `kenney_blocky_a.glb` | `Models/GLB format/character-a.glb` | `8ee5dae167ec589863f6bba222467eb90ace8be357a4c5abfcab289290181616` |
| `Textures/texture-a.png` | `Models/GLB format/Textures/texture-a.png` | `257e944c582ce7cda206fbd8ceb717be648f9721756baf377a36478c11c0059a` |

**Unlike the Mini set this one is not skinned**, and its texture is a painted skin rather than a
palette. Both facts matter to the baker and are the reason both models are kept: between them they
exercise the skinned-plus-palette path and the rigid-node-plus-`--tint` path, which is every way a
body can currently enter this game. Its bake is:

```bash
godot --headless --script scripts/tools/BakeBody.cs -- \
  res://assets/models/kenney_blocky_a.glb res://resources/bodies/kenney_blocky_a.res \
  2.2 0.55 0.30 0.035 "3a5fdb,3a5fdb,2fa05a,e8b98c,e8b98c,d9a06a"
```

Six tints for six surfaces, in node order: both legs, torso, both arms, head. Sampling
`texture-a.png` instead gives one colour per box corner and a gradient across every face — correct
behaviour for a texture with real detail in it, and not what this game draws.

**`Textures/colormap.png` is not optional and is not a texture in the usual
sense.** The `.glb` references it externally rather than embedding it, so
importing the model without it succeeds, logs one `Can't open file from path`
line among the import spam, and produces a material with no albedo texture — from
which `BakeBody` reads a flat white and bakes a white character. That is
indistinguishable from a model whose author chose white. If a Kenney model ever
bakes white, this file is the first thing to check.

It is a 512×512 palette: every triangle is mapped onto a flat patch of colour
rather than onto drawn detail, which is the convention across Kenney's whole 3D
library. `BakeBody` samples it per vertex, and for a flat patch that is exact
rather than approximate — see `PaletteImage` there.

## Sketchfab — Polyart Zombies with Animations Free Pack

| | |
| :--- | :--- |
| Pack | Polyart Zombies with Animations Free Pack |
| Author | Denys Almaral — <https://sketchfab.com/denysalmaral> |
| Page | <https://sketchfab.com/3d-models/polyart-zombies-with-animations-free-pack-d9bcfdd88f5348549bc947226af7c314> |
| Downloaded | 2026-09-08, GLB, texture size 256 |
| sha256 | `17b40fbdc717423184872f6bbfe3bdc92ca2390f6d32947e7ee433a67e278e73` |
| Licence | **Sketchfab Free Standard** — not CC0, not CC BY |
| Contents | ten zombies, 18,840 triangles in total (1,588–1,990 each plus hair), one 7.4 s animation `Take 001`, 405 bones across ten skeletons |

**The `.glb` is deliberately not committed.** The Standard licence permits using
the model in a product — commercially, worldwide, in any derivative work — and
forbids redistributing the file itself as a standalone asset. A raw `.glb` in a
public repository is exactly that. It is in `.gitignore`; the URL and the hash
above are what make it one download away rather than lost.

Whether a *bake* may be committed is the judgement call, and it is a different
one from the file: `resources/bodies/polyart_*.res` is 1,650 triangles of
re-posed, re-scaled, re-coloured vertex data with no textures and no rig, built
by `BakeBody` — a derivative work, which is the category the licence names. That
is the reading this project is going on, and it is written down here rather than
assumed so that it can be revisited.

### How one of the ten is baked

The pack is one file with ten characters and a floor tile in it, so the bake has
to say which:

```bash
# Into the walker slot, which is what a real intake does: slot: resolves the
# destination and the design height, and the whole run is one command --
#   powershell art-src/models/intake.ps1 -Model assets/models/polyart_zombies.glb \
#       -Slot walker -Node rig_CharRoot007 -Pose "Take 001@3.5" -Yaw 180
godot --headless --script scripts/tools/BakeBody.cs -- \
  res://assets/models/polyart_zombies.glb slot:walker \
  node:rig_CharRoot007 "pose:Take 001@3.5" yaw:180
```

`ModelReport.cs -- <model> tree` lists the nodes. The characters are the
`rig_CharRootNNN` nodes, **not** the `lpMale_zombie_C` markers beside them —
those are empty labels, and baking one gives "has no mesh to bake". The marker
immediately above each rig is what names it.

`pose:` is needed because the bind pose is a T-pose. `yaw:180` is needed because
these face +Z and this game's bodies face -Z.

**`Take 001@1.0` was the first frame tried and it is the wrong one**: at one
second the character stands with its hands on its hips, and a bake freezes one
frame forever. At 3.5 s it is mid-stagger with its arms hanging, which is what
the shader's swing is added to. A pose time is worth trying three of.

### The bake is committed as a candidate, and is not in the game

`resources/bodies/polyart_male_c.res` is `lpMale_zombie_C` at 1,650 triangles,
posed from `Take 001@3.5`. It is not a slot name, so `BodyBakes` does not pick
it up — **renaming it to `walker.res` puts it in the game and deleting it takes
it out again, and that is the whole of the decision.**

It was baked into the walker slot, measured and looked at, and it is held back
for a reason that is not about the model. As a *body* it is plainly better than
the procedural walker: a lurching posture, a bloodstained vest, a face. As a
*horde variant* it is a pale-skinned man, and the walker's green is a gameplay
signal — the variants are told apart at 25 pixels by colour before anything
else, and 150 of these read as a crowd of survivors. It also leaves the horde
half authored and half boxes, which is the same inconsistency the survivor
roster now has and is more visible at 150 instances than at three.

The perf question it was meant to answer came back conclusively and is in
`ART.md §2`: 200 of these measure 1.44 ms against the procedural body's 1.64,
and 200 bodies at 23,822 triangles measure 3.41. Triangles are not what the
horde costs.

## PROJECT LAST DAWN — the five survivors

**The licence is known now, and it has a name.** Three phases of this register
carried "not recorded" in the author and licence rows for the body the player
controls. The LAST DAWN production ran the provenance down: the original is
*Game Ready Low Poly Tactical Character* by **DanlyVostok**, and the evidence is
in the file rather than in a claim — `asset.extras` on the companion GLB carries
the title, author, licence and listing URL, and `asset.generator` identifies
Sketchfab.

| | |
| :--- | :--- |
| Original | Game Ready Low Poly Tactical Character |
| Author | **DanlyVostok** — <https://sketchfab.com/1799danly> |
| Listing | <https://sketchfab.com/3d-models/game-ready-low-poly-tactical-character-68213c7d97a04b1b963599180210f5bd> |
| Licence | **CC BY 4.0** — <https://creativecommons.org/licenses/by/4.0/> |
| Archive | `game-ready-low-poly-tactical-character.zip`, nested `source/ForSketchfab.zip/CharacterIdle.fbx` |
| Derived by | PROJECT LAST DAWN, in `godogen-master/artifacts/last_dawn_characters/` |
| Attribution record | that artifact's `THIRD_PARTY_ASSETS.md`, and `reports/source_embedded_metadata.json` for the extracted metadata and hashes |

The listing itself returned HTTP 403 on 2026-09-08, so the row above rests on the
embedded metadata rather than on a fetched snapshot. The licence deed was
reachable.

**Which means the attribution is now a shipping requirement.** `ART.md §6` has
said since it was written that CC BY needs the credit to reach the *player* and
that this game had no surface for one — "an unscheduled dependency, and it should
be scheduled". It is scheduled: the roster screen carries the notice, because
that is the screen where these five illustrations and the bodies under them are
on display. `BaseScreen.RosterScreen` is the two lines, and they are not
optional.

### The five

| Slot | Model | Triangles | Design height | Drawn at |
| :--- | :--- | ---: | ---: | ---: |
| `rin` | `chr_rin.glb` | 45,888 | 172 cm | 2.20 m |
| `mika` | `chr_mika.glb` | 38,909 | 158 cm | 2.02 m |
| `akira` | `chr_akira.glb` | 44,827 | 175 cm | 2.24 m |
| `sora` | `chr_sora.glb` | 42,668 | 168 cm | 2.15 m |
| `yuna` | `chr_yuna.glb` | 45,391 | 163 cm | 2.08 m |

**These are the `art_revision` exports, built to `ART.md` §10 rather than chosen
off a shelf**, and they are what the game loads. Each is one file with one
animation — a single-frame `BakePose` — instead of an LOD chain and twenty named
clips, which is what §10.6 asks for and roughly halves the download for the same
body. The `ld_*` LOD exports of the previous revision are still in the artifact
directory and are what the numbers below were first measured on.

What the revision changed, and all of it was asked for in §10:

- **The iris, the pupil and a highlight are separate meshes bound to `Head`.**
  This is the one that mattered. Per-vertex sampling cannot reproduce a painting
  a few millimetres across, so for three phases every survivor baked with blown
  white sclerae; there is geometry there now and they bake with eyes.
- **The identity colour is on a jacket rather than on piping.** Red on RIN and
  AKIRA, blue on MIKA, green on YUNA, violet on SORA. See §10.4 for why the
  number attached to that request was the wrong number, and what replaced it.
- **Vertex alpha 0 on the glowing parts**, materials left OPAQUE — RIN's backpack
  stripes, MIKA's sleeve modules, YUNA's cross bars. It renders. AKIRA and SORA
  have none, because their weapons are in `equipment/` rather than on the body.
- **White vertex RGB where there is a texture, vertex colour where there is not**,
  so the bake's multiply never doubles a colour.
- The root node is `CHR_<name>` and the bake still wants `yaw:180`.

**About 40,000 triangles each, and the ceiling is the repository rather than the
renderer.** `ART.md §2` measured the player tier and found one body's triangles
unmeasurable against a horde's, so any of these would run at any size — it is the
committed *bake* that costs, because the source is not committed. Five bakes are
11 MB.

The sources are gitignored despite CC BY permitting them, and for size alone.
One `intake.ps1` command rebuilds a bake from one:

```bash
powershell art-src/models/intake.ps1 -Model assets/models/chr_mika.glb     -Slot mika -Pose "BakePose@0.0" -Yaw 180
```

**`BakePose@0.0` is the pose, and it is the first time the frame was neither
guessed nor hunted.** The bake freezes one frame and the walk after that is a
sine added on top, so the frame wants to be what the sine is added *to*:
standing square, legs together, arms hanging, nothing held. `ART.md §10.5` asked
for exactly one keyframe of exactly that under exactly that name, and these have
it. The revision before them shipped eighteen to twenty-one named clips and the
frame was found by trying `Idle@0.0` (arms forward — the bake stood there
reaching) and then `Walk@0.25` (the pass of a walk cycle, which is right).

`yaw:180` because these face +Z like everything else that has come through here.

**141 of RIN's 143 meshes are skinned**, and the same is true of the other four
in proportion. That is the property that makes a model cheap to intake: every
strap, buckle, lace and hair lock comes through without a flag, because
`BakeBody` only skips an unskinned mesh that is not attached to a bone.

### The bone names changed and the classifier did not have to

These rigs are `Root / Hips / Spine / Chest / Neck / Head` with
`UpperArm.L`, `Forearm.L`, `Hand.L`, `UpperLeg.R`, `LowerLeg.R`, `Foot.R`,
`Toe.R` — not the `mixamorig_*` names every earlier import used.
`BakeBody.Classify` matches on the substrings `arm` / `hand` / `leg` / `foot` /
`toe` and takes the side from a `.l` or `.r` suffix, so all of it lands
correctly with nothing added. Worth stating because it is the one place an
intake fails silently: an unrecognised bone name is torso, and a body whose legs
are torso stands still while it walks.

### The portraits

`assets/ui/portraits/*.png`, cut from the roster design sheet by
`art-src/ui/cut_portraits.py`. The five individual design sheets each carry a
hero illustration and cropping those gave five cards that did not match — the
sheets differ in aspect, the name plate sits somewhere different on each, and
RIN's is not in the left column at all. The roster sheet's top band already is
five equal panels with the same framing, so slicing it is the whole job. The
panel offsets in that script are measured off the sheet rather than divided out
of it, because the panels are composed art and are not evenly spaced — assuming
they were put MIKA's drone down the left edge of AKIRA's card three times.

Design sheets and character designs are the user's own, supplied to this
project; the CC BY obligation above is on the *body* beneath them.

### The irises, which were the reason for §10 in the first place

Fixed, by geometry, in the models. Three revisions of a survivor baked with blown
white sclerae because an anime iris is a painting a few millimetres across inside
a UV island with no vertices in it, and per-vertex sampling averages that away.
An iris, a pupil and a highlight as their own meshes is a few dozen triangles and
it is exact.

The engine-side alternative — a survivor-only textured path with the model's own
UVs — is not scheduled, and the reason is that this cost nothing at runtime while
a second material path would cost a code path forever.

### The earlier passes are on disk and are not loaded

`rin_v2.glb`, `rin_v1_reference.glb` and `tactical_character.glb` were each the
player's body for one phase, in that order backwards. None is committed and
nothing points at any of them. They are the same lineage as these five — the
same DanlyVostok original — and are kept as a record of what improved.

## Quaternius — Zombie Apocalypse Kit

Cover and scenery. Baked into `resources/props/*.res` by
`BakeBody.cs ... prop`; `PropLibrary.Baked` maps each to a `PropKind`.

| | |
| :--- | :--- |
| Pack | Zombie Apocalypse Kit (60 models), dated 2024-03-14 |
| Author | Quaternius — <https://quaternius.com> |
| Page | <https://quaternius.com/packs/zombieapocalypsekit.html> |
| Download | the page's button opens a Google Drive folder: <https://drive.google.com/drive/folders/1mWP6sCHun7OUMHQeDNZLrXTteXlzWg_t> |
| Licence | **Quaternius Asset License (QAL) v1.0** — <https://quaternius.com/license.html> |
| Attribution | Not required. This file is the project's own record, not a licence obligation. |

| In repo | From | sha256 | Used as |
| :--- | :--- | :--- | :--- |
| `props/Container_Red.gltf` | `Environment/glTF/` | `181c71d3b35f00043ace29301cd664bdedabc13e9b761efbcfa0b96556571cde` | `Container` |
| `props/Wheels_Stack.gltf` | `Environment/glTF/` | `3297d1566d40a6d2d0d8cb765ec83050633a535b13a2181c067999f6e989b466` | `Rubble` |
| `props/TrafficBarrier_1.gltf` | `Environment/glTF/` | `3594493dc95e87cf9fac68d6b56f9e269f44d01cdb31bded10b34fa300beb2de` | `Barrier` |
| `props/Barrel.gltf` | `Environment/glTF/` | `c05a1b4e43c4779e3d0311d02cb5eb735d6ede9c629d41b25e46f73f0b36b096` | `Dumpster` |
| `props/WaterTower.gltf` | `Environment/glTF/` | `8ace4fa799796c942433659d0c31046d4854e2ce6da6cbc7ba20b358fc386a12` | `WaterTower` |
| `props/TownSign.gltf` | `Environment/glTF/` | `2cc983ee4a4ab0aa9f71605acaaf8371badb5a3d971aa92c6541924054b1b03b` | `Billboard` |
| `props/PlasticBarrier.gltf` | `Environment/glTF/` | `78407ae2ee697c2be2c59cd1e71a23f0acda2c6b23521d57a6837e71a0163ec3` | `TrafficBarrier` |
| `props/Vehicle_Pickup.gltf` | `Vehicles/glTF/` | `b0f522dc1fbc8daafb4df1a7e86d9d7df087c04b06a012f05d48a49b0a99655a` | `CarWreck` |

**No archive hash, and that is a gap worth naming.** The Kenney entries above cite
the sha256 of the published `.zip`, so anyone can re-download and verify the
chain end to end. Quaternius delivers through a Google Drive folder that
re-packs on the fly, so the archive has no stable hash to record. The per-file
hashes are of the files as committed; they prove those files have not changed
since, and they do not prove what was downloaded matched what Quaternius
published. If that matters later, the fix is to obtain the pack from a source
that publishes a stable archive.

**The `.gltf` files carry their textures inside them** as base64 buffers, so
there is no `Textures/` sibling to lose — unlike the Kenney models above, where
forgetting `colormap.png` bakes a white character.

### Why these are committed even though the licence is not CC0

The section below is the reasoning; this is the decision.

QAL §2 permits incorporating the assets into a product and distributing that
product commercially. §3(a) forbids redistributing the assets themselves "as a
standalone asset, asset pack, stock file, template, or similar product …
regardless of how much the Assets have been modified". A public source tree that
commits raw `.gltf` files sits closer to §3(a) than a shipped build does, which
is why the decision was taken explicitly rather than by default.

Two things weigh the other way and are worth writing down. The pack pages —
including this one — still carry a CC0 badge linking to the CC0 deed, while the
site-wide licence page says QAL v1.0 as of 2026-08-28; the two contradict each
other and the more permissive one is the page the asset is actually offered on.
And this repository is a game, not an asset pack: the files are here to be
compiled into a build, alongside the code that consumes them.

If Quaternius ever objects, the remedy is small and known: the models feed
`resources/props/*.res` through one table in `PropLibrary`, and removing the
`.gltf` sources leaves the game running on the procedural boxes it shipped with
for its first twenty phases. Nothing depends on them structurally.

## A note on Quaternius, who used to be the other safe source

`README.md` listed Kenney and Quaternius together as CC0 and safe to use
directly. **That is no longer true of Quaternius.** As of 2026-08-28 their assets
ship under the Quaternius Asset License (QAL) v1.0 rather than CC0
(<https://quaternius.com/license.html>), and some pack pages still link to the
CC0 deed while the site-wide licence page does not.

QAL is generous about *use* — commercial projects, no fee, no attribution — and
its §3(a) forbids redistributing the assets themselves "as a standalone asset,
asset pack, stock file, template, or similar product … regardless of how much the
Assets have been modified."

That restriction is aimed at asset resellers and not at games, but it lands
awkwardly on **this** repository specifically, because this repository is public
and commits its assets as files. A `.glb` sitting in `assets/models/` is
something a third party can take directly, which is closer to the thing §3(a)
names than to the "completed Product that merely incorporates the Assets" §2
permits. A shipped build is unambiguously fine; a public source tree carrying the
raw file is a judgement call nobody needs to make.

Kenney is CC0 — a public-domain dedication, with no downstream restriction at all
— so for anything that gets committed here, Kenney is the source to reach for.
Quaternius remains a good option for assets that are only ever compiled into a
build.
