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
godot --headless --script scripts/tools/BakeBody.cs --   res://assets/models/polyart_zombies.glb res://resources/bodies/polyart_male_c.res   2.0 0.55 0.30 0.035 node:rig_CharRoot007 "pose:Take 001@1.0" yaw:180
```

`ModelReport.cs -- <model> tree` lists the nodes. The characters are the
`rig_CharRootNNN` nodes, **not** the `lpMale_zombie_C` markers beside them —
those are empty labels, and baking one gives "has no mesh to bake". The marker
immediately above each rig is what names it.

`pose:` is needed because the bind pose is a T-pose. `yaw:180` is needed because
these face +Z and this game's bodies face -Z.

## Unknown source — `tactical_character.glb`, the survivor the player controls

| | |
| :--- | :--- |
| File as downloaded | `game_ready_low_poly_tactical_character.glb` |
| Author | **not recorded** |
| Page | **not recorded** |
| Downloaded | 2026-09-08, from the user's own `Downloads` |
| sha256 | `7aa7a73b3000fc410c831a0e8f35c5dc2d99c61da528a4de864b282dbe9dafe3` |
| Licence | **not recorded** |
| Contents | one character, 23,822 triangles across 16 surfaces, one skeleton of 89 `mixamorig_*` bones, one 8.3 s `IdleAnimation`, two 1024 textures |

**Two of the three fields that matter are blank, and that is the whole entry.**
The geometry is excellent and bakes cleanly — it is the first authored humanoid
in three rounds to beat the procedural body it replaced — and none of that is a
provenance record. A file name is not a licence, which is the lesson
`rigged_anime_girl_cc0.blend` taught this project at ten megabytes.

So the same call is made here, on purpose: **the `.glb` is in `.gitignore` and
the bake is committed.** Whatever the licence turns out to be, every row of the
table in `ART.md §6` permits a derivative — a `.res` of vertex data with no
texture, no rig and no animation — and the two rows that forbid *anything*
(CC BY-NC, NC-ND) would sink the file either way. The one thing no licence
permits is redistributing the source as a standalone asset, and that is exactly
what a `.glb` in a public repo is.

`ART.md §8` is the checklist this entry fails. Fill in the page URL and the
licence verbatim from it, then either commit the `.glb` (CC0 / CC BY) or leave
this note as the reason it stays out (Sketchfab Standard, or a QAL-shaped
restriction).

The bake, and it wants `yaw:180` like every +Z-facing import:

```bash
godot --headless --script scripts/tools/BakeBody.cs -- \
  res://assets/models/tactical_character.glb res://resources/bodies/tactical_survivor.res \
  2.2 0.55 0.30 0.035 "pose:IdleAnimation@2.5" yaw:180
```

2.2 m is the Drifter's `BodyHeight` and not a property of the model. The
`mixamorig_` prefix is what makes this one work at all: `BakeBody.Classify`
matches on `thigh` / `arm` / `hand`, and Mixamo's names carry them, so 3,862 leg
vertices and 5,729 arm vertices land in the right buckets and the shader swings
them about a hip at 1.12 m and a shoulder at 1.74 m.

## Verifying

```bash
curl -sLO https://kenney.nl/media/pages/assets/mini-characters/bfc7e272b4-1774770718/kenney_mini-characters.zip
sha256sum kenney_mini-characters.zip     # must match the table above
unzip -p kenney_mini-characters.zip "Models/GLB format/character-male-a.glb" | sha256sum
```

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
