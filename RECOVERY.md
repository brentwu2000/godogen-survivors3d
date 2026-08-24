# Recovery plan

**Status: the repository was destroyed on 2026-08-23 and re-cloned from GitHub at Phase 29.**
Everything after Phase 29 is gone. This file is the plan to rebuild it, and it is written down
precisely because the knowledge of *what* was lost lives in a chat session that will not survive.

Read this before starting work. Update it as phases land.

### Rebuilt so far

| Done | What | Commit |
| :--- | :--- | :--- |
| ✅ B1 | Export that reads the build back | `2245607` |
| ✅ B13 | Limiter on the master bus | `d8fd10f` |
| ✅ A1 A2 B8 | Turnable third-person camera, turn-and-advance, `BotDrive` | `deac551` |
| ✅ — | Two inherited probe failures: both were one point of gear armour | `9f4b997` |
| ✅ A3a | `MeshBuilder` gains `Tube`, `Ball` and the rig channels | `f2aad1b` |
| ✅ A3b | `BodyMeshLibrary`, `BodyRenderer`, `SoloBody`, `body.gdshader` | `4cbddf0` |
| ✅ A8 | Sky, depth fog, and arms outside the torso | `3cc1a80` |
| ✅ A5 | Danger zones: `ZonePlan`, `DangerZone`, perimeter spawning | `4c62ec9` |
| ✅ A5b | Zone readout, and `zone_marker.gdshader` so the edge is visible | `debadab` |
| ✅ A5c | `AutoPlay --zone`, and three balance corrections it found | `8871b4c` |
| ✅ A5d | `BalanceSweep zones:both`, and the zone tuned against the table | `ac006d1` |
| ✅ A6 | `Shelter` — the base is a walkable room; the input map is repaired | `188f64b` |
| ✅ A9 A10 | Slab seams in the ground shader, and 843 pieces of `ScatterField` | `a885481` |
| ✅ A7 | `Minimap` — explored, not revealed | `22a5004` |
| ✅ B5 | Spread, Charge and Blast; three weapons that resolve differently | `82ac068` |
| ✅ B6 | A full backpack is a decision; `[R] drop`, and crates keep the overflow | `4293c5a` |
| ✅ B3 | `RunKit` — Orbit, Shockwave, Chain and Chill; cards that fight on their own | `4d93557` |
| ✅ B4 | A fourth equipment slot, for the kit rather than the body | `789ad9f` |
| ✅ B7 | Curiosities, and selling one is not a mistake | `c7b8e0e` |
| ✅ B2 | Enemies arrive instead of appearing; the dark closes in | `7c700b1` |
| ✅ B11 | The deck was five times the size of the run | `3ded786` |
| ✅ — | The danger zones re-tuned against a player who can now spend the deck | `c22c223` |
| ✅ B12 | There is air in it | `bc42d67` |
| ✅ B9 | `Terrain` + `GroundMesh` — the floor has relief and the simulation never noticed | `3641daf` |
| ✅ B10 | Three glTF landmarks, authored offline in three.js | `3c65831` |
| ✅ B14 | The proof video, and the double-draw it found | `dec30dd` |
| ✅ A4 | `ShadowRenderer`, and the first thing that ever ran the sprite fallback | `03c3b57` |

**The rebuild is complete.** Every phase in both halves has landed.

## Half D — characters and monsters that carry a threat

**Direction, decided by the owner on 2026-08-25:** post-apocalyptic base with supernatural
elements. Survivors stay human and grim; the horde goes mutant — extra limbs, glow, mass, wrongness
— and bosses may be exaggerated. Halo-and-tassel motifs are allowed *on a mutated body*, not on a
person.

The complaint being answered is "a few coloured blocks". The blocks are now jointed and faced (C4,
C5), which is a floor rather than an answer: what is missing is that nothing in the horde is
*frightening*, and there is exactly one player character.

### The one architectural fact everything else follows from

Two hundred enemies are drawn by one `MultiMesh`, and **a `MultiMesh` loses an imported mesh on
pack/save** (godot.md:46) — it comes back with the right instance count and no mesh at all, drawing
nothing and reporting nothing. A skinned mesh cannot go in one under any circumstances.

So there are two classes of body and they are not interchangeable:

| | drawn as | budget | animated by |
| :--- | :--- | :--- | :--- |
| the horde | one `MultiMesh` per variant | ~600 tris each | the vertex rig in `body.gdshader` |
| player, elites, bosses | their own node | a few thousand tris | a real `Skeleton3D` and clips |

`test/ModelReport.cs` answers which class a given `.glb` falls into. Run it on anything before
planning around it.

### D1 — The baker  ·  *the unlock, do this first*

An authored model cannot enter the horde today because nothing produces the vertex rig the shader
reads. `MeshBuilder` writes it by hand: **UV = (swing, pivotY)**, **UV2 = (phase, bob)**, albedo in
the vertex colour. A `.glb` carries `JOINTS_0` and `WEIGHTS_0` instead, which is the same information
in a form the shader cannot read.

The baker converts one into the other. Per vertex: take the heaviest-weighted joint, map its bone
name to a rig channel, and write the four numbers.

    Thigh/Shin/Foot   → leg,   pivot = hip Y,      phase 0 or 0.5 by side
    UpperArm/Fore/Hand→ arm,   pivot = shoulder Y, phase 0.5 or 0 by side
    everything else   → torso, swing 0, bob only

**Bake to data, not to a mesh.** The output is a `Resource` holding arrays, and the runtime builds
the `ArrayMesh` from it exactly as `BodyMeshLibrary` does today. A saved `Mesh` would walk straight
back into the pack/save trap; a saved *array of floats* is the same kind of thing `EnemyTypeResource`
already is and has no such problem.

**Budget the triangles at generation time, not afterwards.** 8,600 triangles is fine for one boss and
is 1.7 million for a horde of two hundred, against the 72,000 the current bodies cost. Ask the
generator for 400–600 triangles for anything that will be a horde variant. Decimating afterwards is
possible and is a second tool nobody needs yet.

Probe: `BakeProbe` — a baked body must be closed and outward-facing (the same net-cross-product test
`BodyProbe` uses), must stand at its declared height, must have a non-zero swing on at least one leg
vertex and zero on the torso, and the two sides must be half a turn apart. A bake whose rig is all
zeroes produces a body that renders perfectly and never moves, which is the failure this file has
seen three times in other forms.

### ✅ D2a — The stalker, and the colour that never arrived

The first authored horde variant: a quadruped, 424 triangles, 1.30 m tall and 2.18 m long, baked
from a `.glb` into `resources/bodies/stalker.res` and drawn from inside the horde's `MultiMesh`. It
spawns, it walks, and it is the first thing in the crowd that is not an upright biped.

Three things the baker got wrong on the way, all of which produce a body that renders perfectly:

**It read surface 0 and dropped the rest.** 96 of 424 triangles, gone, on a model with two
materials. Now every surface is merged with its own albedo.

**A quadruped hip is not a broken rig.** The baker refused the stalker because its hip sat above
two thirds of its height — which is true of every four-legged animal ever born, and would have
refused all of them. Replaced with "something that swings reaches the floor", which holds for both
postures; the posture itself is now reported rather than ruled on.

**The colour was never applied, and nothing said so.** `body.gdshader` writes COLOR into ALBEDO and
ALBEDO is linear; a hex code is sRGB. `MeshBuilder` converts once at build time and the baker did
not, so a stalker written as dark brown arrived at roughly twice the brightness and rendered a
washed near-white. It passed every soundness check there is — watertight, correctly scaled,
correctly classified, rigged on both phases — and was simply the wrong colour.

`BakeProbe` now holds the two paths against each other: the same hex through `MeshBuilder` and
through `BakeBody.Tint` must agree to within one 8-bit channel step, and a mid grey must come out
below 0.30 so that two paths which *both* skipped the conversion cannot agree their way to a pass.
Asserting that the baker calls `SrgbToLinear` would have been a tautology; asserting that a baked
body and a procedural body given the same colour end up the same colour is not.

Also fixed: a `#` prefix on the colour argument is a PowerShell comment, so `-- ... #6b5f52` arrived
as no argument at all. An unreadable colour is now refused rather than ignored.

### D2b — The rest of the horde  ·  *needs D1*

Six variants exist and read as one silhouette family: upright, bilateral, human-sized. Threat comes
from breaking that, not from adding more of it.

Candidates, each of which has to be legible as a *silhouette in fog at twenty metres*:

- something with four legs and no upright posture, so the crowd is not all one height
- something much wider than it is tall, that blocks rather than chases
- something that glows, so the dark stops being uniformly empty
- something that arrives in a knot rather than as individuals

The existing table already carries what a variant costs: `EnemyTypeResource` has speed, health,
contact damage, behaviour and design height. A new variant is a `.tres`, a baked body, and a row in
`Horde.TypeNames` — the systems do not need to change.

### D3 — More than one survivor  ·  *independent of D1*

One player character, one silhouette, one set of numbers. Multiple characters with different
abilities is a progression axis the game does not have, and it is the cheapest of the three to build
because `Player`, `WeaponHandler` and `RunModifiers` already separate what a character *is* from what
it *carries*.

A character is a `.tres`: body build, base health, base speed, and one ability that is not a weapon.
The shop already sells weapons and gear; characters unlock the same way.

Probe: whatever is added, the assertion is that two characters produce measurably different runs —
`BalanceSweep` with a character arm, the same way it grew a zone arm and then a tier arm.

## Half E — places, not one field with three tints

**Asked for by the owner on 2026-08-24:** the arena has to look end-of-the-world too, and there
should be several kinds of place — a city, a laboratory, and others.

### What is actually there now

Three biomes exist and are already real *as gameplay*: `rail_yard`, `old_town` and `the_flats` each
carry their own tile weights, cluster count and size, corridor gap, crate count, depth bias and spawn
ring, and they were tuned against each other so that neither is simply the harder one. That part does
not need rebuilding.

**What they do not have is a look.** `BiomeResource` carries exactly two appearance fields —
`GroundTint` and `PropTint` — and every biome draws the same five pieces of cover from
`PropLibrary`: container, barrier, rubble, wall, dumpster. A laboratory built out of tinted shipping
containers is a rail yard with a colour grade on it, and the player will read it as one.

So the work is not "add biomes". It is **give a biome the vocabulary to be somewhere**, then use it.

### ✅ E1 — A biome owns its cover

`PropKind` is a flat enum and `LevelGenerator` draws from all of it. A biome needs to name its own
set, with weights, or every new prop leaks into every existing place — add a server rack for the lab
and it appears in the rail yard the same afternoon.

Built as a **role table** rather than a weight list, and that choice is the whole of why the
existing biomes are unaffected. `PropRole` names what a piece of cover is *for* — Wall, Bulk, Heap,
Low, Odd, plus Tall and Sign for scenery — and the generator picks a role with exactly the rolls and
thresholds it was already tuned with. The biome then names the furniture. A laboratory therefore has
the same cover density and the same sight lines as the rail yard it replaced and differs only in
what the player is looking at; a per-biome weight table would have changed the fight and the scenery
at once and made neither measurable.

Three things had to change behind it:

**`PropRenderer` allocates per biome.** It built one `MultiMesh` per enum value, which is free at
five kinds and stops being free at twenty-one. It now takes the biome's list, keeps the arrays
sparse so `Add` is still an array index, and **warns loudly** on a kind it was not built for — the
silent version is a piece of cover the flow field routes around and nothing draws.

**`Commit` stopped trusting child order.** It found each instance with `Node.GetChild(index)`, which
was only ever correct because every kind got a child in enum order. The moment a biome can skip one,
that indexes the wrong node and the wrong prop turns invisible.

**The renderer is rebuilt when the furniture changes.** `_props ??= CreateProps()` is correct while
every biome shares a set and becomes an empty arena the moment they do not — and the base screen
switches biome without reloading the scene, so it is the ordinary path rather than an edge case.

**Probe:** `BiomeProbe` regenerates into every biome in one scene and asserts that each places props
only from its own set, that it places *some* (a renderer built for the wrong set drops every `Add`
and leaves a bare arena, which photographs as an open biome rather than a broken one), and that the
tables are role-correct. It states its premise first and fails if every biome names the same
furniture, because then the whole stage would pass on a system that does not work.

### ✅ E2a — Ash District

The city, and it is a third *question* rather than a third skin. Old Town has no line of fire and
The Flats is nothing but line of fire; both are answers a build can be assembled around before the
run starts. A street is a line of fire **that has a direction** — fifty metres one way and eight the
other — so the same build is right or wrong depending on which way it is facing, and the decision
moves off the loadout screen and into the run. Corridor-heavy with a wide gap, which sounds like a
contradiction and is the point: long walls, easy ways through, nothing ever sealed and everything
channelled.

Seven props, all boxes with vertex colour, one shared material, same as the existing five: a site
hoarding, a gutted bus, a three-car pile-up, water-filled traffic barriers with a downed signal
across them, a shuttered kiosk, a leaning tower block and a broken overpass.

**A prop has to fill its footprint.** The collider is the layout's block, not the mesh's bounds, so
anything that leaves a corner empty hands the player an invisible wall. That is why the pile-up is
three cars rather than one flattened one, and why the kiosk is a stall with its stock stacked beside
it rather than a narrow booth.

**Strong horizontal banding reads as a cake.** Props are scaled in X and Z and left alone in Y, so
the first kiosk — base, body, counter, canopy, sign — arrived three metres wide as five coloured
trays with nothing tall enough to dominate. A prop authored for this arena needs one dominant
vertical mass and everything else low.

`test/PropShot.cs` is the tool that caught both, and it exists for the reason `BodyShot` does: every
judgement made about an asset viewed on its own has been wrong. It photographs a set at the size the
arena draws it, and keeps cover and scenery in separate shots — framed together, the camera backs
off far enough for a twenty-two metre tower block and every piece of cover becomes forty pixels of
grey.

Two capture bugs fell out of using it:

**`Screenshot -- biome:N` never worked.** It was parsed after `AddChild`, so it set the biome for a
level that had already generated in `_Ready`, and every `biome:` capture ever taken was the rail
yard with a different filename. Nothing about a screenshot of a real arena says it is the wrong
arena.

**`aerial` had to exist.** The game's camera is four metres up and eight back, which is where you
judge a fight and not where you judge a map — cover is generated in clusters across a hundred and
ten metres and the spawn is deliberately clear, so two arenas with ten times the cover between them
photograph identically from the ground. The aerial also has to switch the fog off: the arena fades
to near-black by forty metres on purpose, so the first attempt came back as a photograph of nothing
at all, with no error anywhere.

### ✅ E2b — Cold Storage

The first **interior**, and the fourth question. Every other place is a field with things standing
in it and the layout varies the things; a building varies the *space*. Cover is nearly all long
partitions, so the arena is a set of rooms with doorways between them and the fight is about which
room you are in rather than what you are standing behind. Twelve crates at the lowest depth bias in
the game, because rooms are where things are kept and the danger here is being cornered rather than
being caught in the open.

Seven props: clean-room partitions, server racks, a fallen suspended ceiling, laboratory benches, a
specimen tank, an exhaust stack and a gantry crane. The palette is the other half of the work —
everything else in the game is weathered outdoor material, and painted panel against stainless
against dark glass is most of what makes this read as inside.

**The specimen tank should glow and does not.** Emission is a property of the material and every
prop shares one, so a glowing tank costs the set a second draw call in both the main and the shadow
pass. Written down rather than done, because in this arena's lighting a glow would be invisible
anyway — it wants the dark that E3 provides and a light source of its own.

### ✅ E3 — A biome owns its light

`BiomeResource` gains sun angle, colour and energy; ambient colour and energy; fog colour and range.
`LevelGenerator.Relight()` applies them, because the scene is built once and the biome is chosen
every run — a lighting rig baked into `Main.tscn` belongs to whichever biome happened to be first.

**Every default is the number `BuildMain` already hard-coded, to the digit.** That is the only way
this could be added safely: three biomes and forty-odd probes were tuned against one rig, and a
resource that "improved" the defaults would have re-lit the whole game as a side effect of adding a
fourth place.

The interior is three numbers. The sun points almost straight down, because nothing says *ceiling*
like shadows that fall under things rather than away from them — a raking sun paints long shadows,
which is a statement about a horizon, and a horizon is the thing a room does not have. It is cold
and weak: strip lighting, not daylight. And the fog closes at twenty-four metres, which is the wall.
The arena is still a hundred and ten metres across and every metre is still walkable; the player can
only ever see the room they are in, so the map is discovered rather than surveyed.

`BiomeProbe` asserts both halves separately, and the split is the point: that generating somewhere
applies its light (a `Relight` that never ran leaves a perfectly lit arena that is the wrong arena's
lighting — invisible in a screenshot and invisible to every other probe), *and* that Cold Storage is
measurably enclosed (a resource carrying fog fields set to the outdoor defaults passes the first
assertion completely and is still a field with partitions in it).

### ✅ E4 — The ground stops being one plane

`ground.gdshader` already drew slabs with a seam between them and every biome used the same four
metres. It is one of the strongest scale cues in the frame — the arena reads as large partly because
the floor has a known size to pace out — and handing it to the biome costs no textures, no draw
calls and no geometry. Old Town is 2.4 m setts, The Flats 9 m bays with a seam you have to look for,
Ash District 3.2 m patched asphalt with wide dark joints, Cold Storage a 1.2 m tile grid.

The tile grid is doing as much work as the fog is. It is the clearest possible statement that this
is a floor somebody laid rather than ground somebody stands on, and against The Flats' nine-metre
bays it makes the same arena feel a different size without moving a wall.

### ✅ E5 — Two things the new places exposed

Neither was caused by Half E. Both were live and invisible.

**Nothing may spawn inside the camera.** The spawn ring starts at twelve metres, the camera stands
eleven and a half behind the player, and `SpawnRingScale` multiplied the first without knowing about
the second — so **Old Town has been spawning enemies 2.3 m inside the lens since the day it
shipped**. What it looks like is a two-metre body across the corner of the screen, over the HUD, with
nothing to say what it is or where it came from; what it looked like in a screenshot is nothing,
because every capture taken since was of the rail yard, where the scale is 1.0. Found by looking at
the first Cold Storage screenshot and not believing the minimap.

Fixed with a floor rather than by raising each biome's number, so the ring stays a design decision
and the framing stays a constraint. The camera standoff is **measured off the rig**, not typed: a
copy of 11.7 m here would be a second place to change the framing, silently wrong the first time
somebody moved the camera and never wrong in a way anyone would think to look for.

`BiomeProbe` asks the real `Horde` — `ApplyBiome` once per biome, then measures the ring — because a
clamp that was written but never reached passes a test of the formula. It also fails if no biome
pulls the ring in at all, since then the whole stage passes on a clamp nothing exercises.

**A grain silo was standing in a laboratory.** The three glTF landmarks are a separate system from
`PropKind` and were placed one-of-each in every biome. Nothing in the code was wrong; it had been
right for as long as every place was outdoors. A biome names its own now: the city takes a pylon and
a wrecked coach, the lab takes none and uses its gantry cranes and vent stacks instead.

The probe counts **across both systems** and fails a place with fewer than three things tall enough
to steer by. That matters because opting out is now possible: the landmarks are the only answer to
"which corner am I in" that does not involve reading the compass, and an arena without one is a flat
plane of repeating cover where crossing fifty metres feels like standing still.

### E6 — Still open here

### E2 — Two new places  ·  *needs E1*

- **The specimen tank does not glow.** Needs a second material, and needs E3's dark to be worth
  having. It is the only supernatural note the environment has been given room for.
- **No biome has authored glTF landmarks of its own.** The city and the lab use procedural `PropKind`
  scenery for their skyline, which works and is not the same as a modelled beacon.
- **The three existing biomes now spawn from 14.7 m rather than 12 m.** That is the camera floor
  biting on all of them, including the rail yard, which had 0.3 m of clearance. It makes the opening
  seconds fractionally easier everywhere and it has not been re-measured against the balance table.

**The laboratory.** Cover is server racks, benches, specimen tanks, and containment doors torn off
their frames. It is the first *interior*: the sky is not the light source, the room is. That is what
makes it worth building rather than being a fourth field — and it is what the glowing horde variant
(D2) was always going to need somewhere to be seen.

### E3 — A biome owns its light  ·  *needs E2*

The lab does not work as data alone. `BiomeResource` gains fog colour and density, sun colour, angle
and energy, and ambient level; `LevelGenerator` applies them instead of the one hard-coded set in
`GameRoot`. An interior is then a biome with a low ceiling of fog, a cold weak sun and props that
carry their own emission.

**Do not fold this into E1.** Lighting changes every screenshot in the project at once, and mixing
it with the prop-set change would make any regression impossible to attribute.

### E4 — The ground stops being one plane  ·  *independent*

`GroundMesh` is a tinted height field with slab seams. A road is not a slab grid and a lab floor is
not asphalt. Cheapest honest version: the biome names a ground *pattern* — seam spacing, a second
tint, and how strongly the two mix — which is a vertex-colour variation inside the existing mesh and
costs no draw calls and no textures.

### Order

E1, then E2, then E3, then E4. E4 is last because it is the one that improves the three existing
biomes as well, and doing it first would change the baseline every other stage is judged against.

## Half F — the things in your hands

**The owner's goal, stated 2026-08-24:** scene, characters, monsters, items and skills all
refined. Half E is the scene. This is items and skills, and it is where the least work has been done
of anything in the game — not because it was deprioritised, but because none of it ever showed up in
a probe. A probe asks whether a thing *works*.

### ✅ F1 — The loot crate stops being an untextured cube

`LevelGenerator` built a `BoxMesh` and gave it **no material at all**, and `RunDirector` did the same
for the supply cache. That was the white cube in every screenshot ever taken of this game — including
the ones used to judge the ground shader, the fog, the bodies and all five biomes. It lasted because
no probe asks what a thing looks like, and because a cube reads as "placeholder for something", which
is a category the eye skips over.

`LootLibrary` builds two shapes, six meshes total, on one shared material. A **crate** was already
here and is what the layout scatters: planks, corner irons, two steel bands, feet. A **cache** is
packed and dropped mid-run: moulded shell, ribs, chute harness still attached, spilled canopy. They
have to be distinguishable at fifty metres, because one is scenery you might get to and the other is
a thing you are meant to run toward.

Two things fell out of doing it properly rather than just adding a colour:

**The rarity bias is on the box now.** It already rose with distance from the spawn — a far crate
really was worth more — and the player had to take that entirely on trust, because the far crate and
the near one were the same white cube. Three tiers, stencilled on two opposite faces so it can be
priced without walking around it, and the top tier is the only warm colour on a loot container
anywhere.

**An emptied crate stands open.** Nothing in `LootContainer` had ever touched its mesh, so a looted
crate and a full one were identical from any distance. The minimap knew; the minimap is a
nine-centimetre square in the corner of the screen. A lid on a hinge puts the same information where
the player is already looking, and it is the difference between an arena you are working through and
an arena you are wandering around.

`Shelter.cs:276` is a signboard with a material on it and is left alone.

### ✅ F1b — The sweep could be hung by adding a file

Found while running the above, and it had already cost two sweeps.

`PropShot` is a capture script, and the sweep runs every `.cs` in `test/` that is not on a
hand-written skip list. A capture script run headless does not fail — it spins at 100% of a core
forever, printing nothing (`test/Display.cs` documents this) — and **a probe that has hung looks
exactly like a probe that is slow**. Both sweeps stalled at `MusicProbe`, the entry alphabetically
before it, and I read it twice as "the long run probes are slow" before checking whether anything was
still running.

Two fixes, because either alone leaves the other as a single point of failure:

- `PropShot` and `Presentation` now call `Display.Required` and refuse rather than spin. Presentation
  had never had it either: `--write-movie` headless plays the whole forty-second scripted run, writes
  nothing, and exits reporting success.
- **The skip list is derived rather than written.** Anything calling `Display.Required` has declared
  what it needs, in the file where whoever writes the next capture script cannot forget it. One place
  to get right instead of two.

`TouchProbe` stays on the hand list. It has been there since before a reason was written down, it
claims in its own header to run headless, and taking a name off a skip list is exactly how a sweep
starts hanging — worth revisiting deliberately rather than in passing.

### ✅ F2 — The player is holding something

`BodyMeshLibrary` had no weapon geometry at all. Not a placeholder, not a stub — no reference to a
weapon anywhere in the file, and the survivor had been fighting bare-handed on screen since the body
existed. C6 made the nine weapons sound and feel different, which answered the complaint they were
raised against, and did nothing for the eye.

**Three silhouettes, not nine.** A held object is a dozen or so pixels at the distance this body is
seen, and the only questions it can answer are "long or short" and "does it have a blade". Modelling
a bolt launcher distinctly from a marksman rifle is work spent below the resolution anyone is looking
at. `Longarm` is held diagonally across the chest, butt high by the shoulder; `Bow` is a recurve
stave held upright and clear of the leg; `Blade` is short, at the hand, angled out from the thigh.
The mapping lives in `BodyMeshLibrary` rather than on `WeaponResource`, because it is a fact about
how a body is drawn and a rendering concern in the balance table is one more thing to think about
while tuning damage.

**Rigged, not parented — there is nothing to parent to.** A `MultiMesh` has no skeleton, so "in the
hand" means "turns about the same pivot, on the same phase, by the same amount". The carrying arm
also drops to a quarter of its swing, which is anatomy rather than taste: at full swing the weapon
scythes across the torso every stride and reads as animated rather than held.

**Changing weapon rebuilds the body**, because the weapon is geometry inside the body mesh. That is
cheap at the rate it happens — a keypress a handful of times a run, fifteen hundred triangles, an
order of magnitude less than one frame of the horde — and it keys off the *category*, so a rifle
traded for another rifle costs nothing.

Three things this got wrong first, all found by looking:

**Hung off the wrist, every weapon sits at hip height with the thigh in front of it.** The rifle read
as something dropped by the player's foot and the bow was a thin line behind a leg. Placed from the
shoulder instead: a carried weapon is held *up*, and the height is what says carried rather than
trailed.

**`front` is the worst view to judge it in.** The rifle points forward, and straight-on that
foreshortens to nothing — the first lineup looked like a failure and was a camera angle. The game's
own three-quarter view is the only one worth checking.

**The player vanished.** The first body is added with `CallDeferred`, so on the frames before that
lands its node has no parent — and taking the parent as null and carrying on adds the replacement to
nothing. No error, no warning, an empty patch of floor where the survivor should be. The guard also
has to sit *before* `_held` is written: recording the change and then bailing means the comparison
never fires again and the body stays empty-handed permanently, which is the bug the whole function
exists to fix.

### ✅ F3 — Four cards, four things you can see

The premise going in was wrong in an interesting way. The note said all four were "expressed through
`EffectPool` puffs, differing in tint and size". Orbit had real geometry; **the other three had
nothing at all**, and one of those was a bug rather than a gap.

**The shockwave had been invisible since the day it shipped.** `RunKit.Pulsed` was declared, invoked,
and carried a comment explaining that the effect director draws it "because the effect director owns
every particle in the game" — and nothing ever subscribed. The card damaged, knocked back, and
produced no light at all. Everything about it was correct except that the last line was never
written, which is precisely why it read as a card that does nothing.

It draws a ring of puffs on the wave front now, drifting outward. A ring rather than a burst at the
centre, because the radius is the whole of what the card does and a puff at the player's feet teaches
them nothing about how far it reaches. The count rises with the radius, so stacking the card does not
visibly thin the effect out as it gets stronger.

**The chain arc was never drawn.** `Hit` fires at the destination, so a chain produced an impact on a
second enemy with nothing connecting it to the first — a creature at the back of a crowd flinching for
no visible reason. The thing the player bought is the line between the two, and it was the one part
not on screen.

**Chill had never been drawn at all.** `Horde` slows anything inside `ChillRadius` on a gradient and
nothing showed where that was, so the card's whole effect was enemies moving at a speed the player
could not account for, in an area whose edge they could not see. Of the four it is the one whose
value depends most on knowing its extent: it is bought to make ground defensible, and ground you
cannot identify is not ground you can choose to stand on. Flat shards rather than a disc — a solid
circle on the floor reads as a decal or a selection marker, which the eye looks past.

The card effects are **cold** where every other effect in the director is a warm firearm colour,
because they are things the player bought and the player is blue. In a crowded frame "was that my
card or my gun" has to be answerable without reading a number.

Two mistakes on the way, both found by looking:

- The frost was authored at unit radius and the node scaled by 7.5, which scales the shards too.
  Every plate came out over two metres across and the effect read as sheets of blue paper dropped
  round the player. Position scales with the radius; size does not, and the only way to have both is
  to lay it out at full size.
- The subscription asked the *player* for `RunKit`, and `RunKit` is a sibling. Null, silently, and
  the shockwave would have stayed exactly as invisible as it was.

**`KitProbe` gains a stage that asks whether each card puts something on screen.** Every stage before
it asks what a card *did*, and all seven passed for the entire life of a shockwave nobody could see.
That is the lesson worth keeping from this one: a suite that only ever asks about effects will not
notice that the game has stopped drawing them.

### F4 — Nothing on the ground says what it is

Items go from a crate straight into the bag. There is no dropped-item representation at all, so the
"收集無感" complaint was answered in C7 by making the *collection screen* visible rather than by
making collecting visible. Worth revisiting once F1 exists, because a crate that opens and leaves
something behind is a different feeling from a crate that opens and increments a counter.

### Order

F1 is done. F2 next, then F3. F4 last and only if it still feels missing once a crate is a crate.

---

### ✅ E7 — A ceiling, and four bugs a second reader found

The owner asked for Codex to be used more, including for external verification. Two passes were
run against the uncommitted Half E work, both read-only, both while a sweep was occupying Godot.
Both paid for themselves, and the way they paid is worth recording because it was not the way
expected.

##### The art pass  ·  `codex exec -s read-only -i <four screenshots>`

Its first finding was the one that mattered and the one that had been missed: **Cold Storage read as
an outdoor arena at night rather than as an interior.** The sun points straight down, the light is
cold and weak, the fog closes at twenty-four metres and the floor is a 1.2 m tile grid — every one of
those is right, and none of them is what makes a room. What gave it away was the top of the frame: an
unobstructed black sky, air dust drifting against it like stars, and a far boundary spanning the view
like a horizon instead of converging into corners.

**A ceiling was the obvious answer and it is not the answer.** `CeilingMesh` builds one — a deck,
beams crossed both ways, hanging strip fixtures, two service runs, one draw call, no shadow pass
(a shadow-casting lid under a vertical sun correctly and uselessly blacks out the level). Then the
screenshot came back identical.

The camera is why. The eye sits 5.7 m up, tilted 26° down, with a 60° field — so it never looks more
than about four degrees above horizontal, and a roof at eight metres only enters the frame past forty
metres, which is well beyond where the fog has gone black. **The roof is there, it does occlude the
sky, and it is indistinguishable from the sky it occludes.** The only proof it existed was that the
aerial capture came back as one flat colour, because that camera *does* look at it.

What the game camera can see is whatever stands between the player and the fog. So the fix was to
make the walls walls: `Partition` went from 3 m to 7.6 m, floor to ceiling. A three-metre screen in a
room with an eight-metre ceiling is a cubicle in a field; a full-height wall breaks the horizon line
and closes the arena down to the room you are standing in. It costs nothing mechanically — shots and
sight lines resolve in two dimensions, so prop height here is entirely cosmetic. The fog went from 24
to 28 m at the same time, because at 24 the nearest wall that broke the horizon was already black:
the arena closed down without ever showing what was closing it, which is the same picture as an empty
field at night, only darker.

The roof stays. It is one draw call, it is correct, and it is what makes `CeilingHeight` mean
something if the fog is ever pulled back. But it is not what made the room, and the comment in
`CeilingMesh` says so.

**One thing this exposed that is not fixed:** the spawn clearance is 8 m and the fog closes at 28, so
the *opening* view of a run is empty ground in every biome, whatever the place is made of. The first
Cold Storage screenshot after the walls went in still looked like a field, because the seed put no
cover within thirty metres of the spawn. It only read as an interior once the player was moved
somewhere the arena actually is. That is worth taking seriously: the first ten seconds of every run
are a screenshot of the ground shader.

Its other substantial points, recorded and not yet acted on: the ground plane extends well past the
play space and reads from above as an oversized base mesh; props read as isolated samples rather than
as compositions with debris trailing from them; and nothing darkens the ground under a prop cluster,
so several look slightly airborne.

Two of its findings were wrong, which is worth writing down too. It called the cyan-and-orange shapes
near the player "editor gizmos or debug vectors" — they are arrows in flight. And the "pale
untextured cubes" it found in the aerials were the *old* loot crates, in shots taken before F1: it
confirmed a bug already closed rather than finding a new one.

##### The correctness pass  ·  `codex exec -s read-only` over `git diff`

Four real bugs, two of which would have shipped:

**A non-indexed surface merged with an indexed one vanishes.** The merge writes one global index
array, so the moment any surface is indexed every surface must be — and a non-indexed one contributed
its vertices and nothing pointing at them. Correctly placed, correctly coloured, referenced by no
triangle. glTF exporters mix the two freely.

**The ceiling repeated the prop renderer's `QueueFree` collision**, twenty lines below the comment
explaining why the prop renderer detaches before freeing. The old node is still a child when the
replacement is added, Godot renames the newcomer to `Ceiling2`, and the next generation frees the
doomed original and leaves the survivor in the tree forever. `BiomeProbe` generates once per biome in
a single stage, which is exactly the sequence that triggers it.

**An out-of-range `PropKind` passed validation.** `RoleOf` maps anything unrecognised to `Heap`, so a
hand-edited `.tres` with `999` in the Heap slot satisfied the role check and reached `PropRenderer`,
which indexes its arrays at 999.

**`_baseRingMin <= 0` is a sentinel the export can legally hold.** A probe setting `SpawnRingMin = 0`
would leave the flag true forever, so the second `ApplyBiome` captures the already-clamped 14.7 as
the new base. Now a bool.

It also confirmed, correctly, that the crate-rotation roll had already been moved off `_rng` — that
one was caught here first, by remembering what the terrain offset cost.

##### What this says about how to use it

Codex cannot run Godot or restore NuGet, so it cannot verify anything it says. What it can do is
**read**, and both passes worked because they were given a bounded artefact and a blunt instruction
not to be agreeable. The art pass got four screenshots and "do not compliment, if something looks
fine say nothing"; the review got a diff and an explicit list of what not to comment on. It ran
concurrently with a sweep, which is otherwise dead time — Godot is single-occupancy here and the
review needs none of it.

### Asset pipeline

Codex builds models from an image or a description; `codex exec -s workspace-write` is how it is
driven, and it cannot run Godot or restore NuGet in its sandbox, so **it writes and this session
verifies**. That split has held for two rounds of character work, and now for two rounds of review.

Known limit, found by the review: **the baker reads one mesh node.** A model exported as separate
nodes — body, coat, horns — bakes whichever the tree walk reaches first and silently omits the rest,
producing a sound bake that is missing a coat. It now refuses and names the nodes instead. Merging
them is the surface merge with two more lookups per node, and nothing needs it yet.

Everything imported goes through `ModelReport` first and `BodyShot -- model:res://...` second. The
first says whether it can be used; the second says whether it belongs, and every judgement made about
an asset viewed on its own has been wrong so far.

---

### After the rebuild

| Done | What | Commit |
| :--- | :--- | :--- |
| ✅ C1 | The camera gets out from behind cover | `8bcbe7e` |
| ✅ C2 | A muted renderer is not synced, and owns its own visibility | `710c26e` |
| ✅ C3 | The balance table can tell the two zone tiers apart | *this* |

#### ✅ C3 — The balance table can tell the two zone tiers apart

The zone arm of the balance table had been bimodal since it existed — two seeds paying heavily and
three barely noticing — and the spread was read as variance in what a zone costs. **It was variance in
which zone was taken.** `AutoPlay` took whichever danger zone was nearest, which is usually the shallow
one, so the two tiers were averaged into a single column that looked like noise.

`AutoPlay -- tier:N` picks a tier and says so when the seed has none of it; the `SWEEP` line carries
`zoneTier=` — **the tier attempted, never the tier requested**, or a fallback run lands in the wrong
column and the table is wrong in exactly the way the flag was added to fix. `BalanceSweep -- zones:tiers`
runs three arms and groups by what the run actually reached. `DefaultSeeds` is twelve now, with the
original five first so `seeds:5` still reproduces every earlier table.

Three things had to be fixed before the table meant anything:

**A stuck run left no row at all.** `AutoPlay` printed `AUTOPLAY FAILED` and quit without a `SWEEP`
line, so the sweep logged "no result" and dropped it — and the arm then read "4/4 survived" for a set in
which one of five runs never got home. A failure rate is exactly what a balance table is for, and it
was the one number it could not show. Stuck runs report `outcome=Stuck` now.

**The flow field's zero meant two different things.** Obstacles are inflated by a body radius before
they are marked, so the blocked band reaches about a metre past anything you can actually touch — and
standing in that band is the ordinary result of walking up to a wall. `Sample` returns zero there;
`AutoPlay` read zero as "no route" and substituted the straight line to its target, which is the *worst*
advice available, because the target is on the other side of the thing being stood against. Sixty
seconds leaning on the south face of an eight-metre wall, seven and a half metres from the extraction
pad. `FlowField.EscapeFrom` answers "which way is out"; `Sample` is unchanged.

**The escape must not go into `_flow`.** The first version wrote it straight into the route field, on
the reasoning that a zero there means nothing useful anyway. It does: `Horde` reads zero as "no route"
and runs a fallback it has been tuned around. `LandmarkProbe` caught it in one run — a walker that had
been going round a pylon in 1800 ticks stopped dead thirty-three metres out and stayed there. It is a
separate channel, and `FlowFieldProbe` now asserts `Sample` still returns zero inside an obstacle.

##### What the table says

Twelve layouts, `lingers:0`:

| arm | survived | median banked | median lowest HP | expected value |
| :--- | :--- | :--- | :--- | :--- |
| past | 12/12 | 638 | 98 | 638 |
| tier 0 | 13/13 | 1052 | 91 | 1052 |
| tier 1 | 10/11 | 1328 | 59 | 1207 |

Tier 1 is priced: nine per cent of runs do not come home and the median run ends on 59 health. **Tier 0
was not a gamble at all** — seven points of health, nobody ever died, sixty-five per cent more money.
The correct play was to take it every single run, which collapses the choice the zones exist to create.

Its base intensities went up by about forty per cent of the gap to tier 1, with the per-tier steps
shrunk by the same amount so every tier-1 number is unchanged. The result: 12/13 rather than 13/13, and
92 health rather than 91.

**Intensity is a weak lever here, and that is the finding.** A forty per cent raise bought one death.
The bot arrives at the zone over-levelled and grinds through whatever is in it; more enemies is mostly
more kills, and the banked median went *up*. Cutting the reward instead would lower the payout without
making it a decision. Stopping here on purpose — the next few turns of that screw would be tuning the
game to one bot's strategy rather than to a player.

#### ✅ C2 — A muted renderer is not synced, and owns its own visibility

Two hundred instances were written into a buffer and uploaded twice a frame, every frame, for
renderers whose nodes were hidden. The argument for keeping them running was that a fallback which has
not run since startup is not a fallback — right until A4, when `ShadowProbe` started building the scene
with `SolidBodies` off and running the whole billboard path. A probe in the sweep is a stronger
guarantee than code that executes every frame with nothing looking at the result.

**Measured, and it did not move.** `HordePerf` reads 6.90 ms mean at 200 enemies before and after. The
saving is a per-frame GPU upload, which a headless run cannot see, and the CPU half is below the noise
at this scale. Kept because it removes work that provably cannot reach the screen, not because the
number changed.

**Skipping the sync reintroduced the double-draw immediately.** `HordeRenderer` set `Node.Visible`
inside `Upload`, so a renderer that was never synced kept the visibility it was constructed with —
`true` — and every enemy in the game was drawn twice again, from a different cause, three commits after
the first one was fixed. `BodyProbe` stage 7 caught it in the sweep, which is the entire reason that
stage exists. `Muted` is a property with a setter now, and hiding the node is its job.

#### ✅ C1 — The camera gets out from behind cover

The rig sits 11.7 m behind the player and 5.7 m up; the arena is full of containers 2.5 m tall. Walking
past one put it across the sight line and the player simply disappeared until they had walked far
enough past. It was in every screenshot ever taken and nobody had named it until a frame of the proof
video made it the subject.

`CameraRig` casts one ray per physics tick from a pivot at chest height to where the camera would sit,
and slides the camera **along that same line** toward the pivot until the shot is clear — down to a
third of its reach and no further, with 0.45 m of clearance so the 0.15 m near plane does not end up
inside the wall. In fast (22/s), out slow (5/s): coming in has to beat the geometry, going back out is
cosmetic and doing it quickly reads as the camera being shoved.

Along the line, not to a new place. The tilt, the height and the set-back together *are* the shot;
moving the camera off that line changes the composition rather than the distance. The camera's own
rotation is left alone for the same reason — re-aiming it at the player as it came in would swing the
horizon every time somebody walked past a container.

**The query goes in `_PhysicsProcess`, the movement in `_Process`.** The space state is only safe to
read on the physics tick; reading it from a frame callback is a "Can't change this state while flushing
queries" error, intermittently, depending on where in the frame it lands. Storing the answer and
smoothing it separately also keeps the camera running at the frame rate rather than at 60 Hz.

Two things the ray must not treat as cover: **areas** (the pads, the zones and the crates' pickup radii
are all `Area3D`, and a camera that swung in whenever the player stood near the way out would be
unusable) and **the player** (the ray starts inside its own capsule, which without an exclusion is a hit
at zero distance every frame and a camera permanently inside the character's head).

Probe: `CameraProbe`, six stages, each building its own wall rather than hunting the map for one. The
first stage is the one that matters most — every other stage says "the number went down", and without
one that says the number is 1.0 when nothing is in the way, a rig that pulled in permanently would pass
all of them.

Its last stage samples twelve spots on a 26 m ring and requires three quarters of them to leave the
camera fully out, because off the flat spawn the pivot can sit *below* the flat box the arena collides
with — a ray drawn upward out of the floor is a ray starting inside it, which Godot ignores by default
and would otherwise snap the camera to its minimum in every dip on the map. That stage failed at first
with sixteen of twenty spots "blocked by nothing at all": a fifth of a second per spot, against a
release rate of 5 per second, was reading the previous spot still letting out.

### The three bugs that repeat

Every one of these has now bitten more than once in a single session, all silent, none an error:

1. **`add_child()` from `_Ready` is refused.** Godot prints "Parent node is busy setting up children"
   and carries on; the subtree is built correctly into a node that is not in the tree. Cost the
   player body, then the danger zones. Declare containers in the scene builder, or `CallDeferred`.
2. **An exception out of `_PhysicsProcess` does not stop a `SceneTree` script.** The frame is
   abandoned and the next one starts, so a null dereference is not a crash — it is a process pinned
   at 100% of a core, printing a stack trace sixty times a second, forever. Cost four zombie
   `ScaleProbe` processes and one hung `ZoneProbe`. Never `!` in a probe; fail the stage.
3. **`ProjectSettings.Save()` writes and never removes.** A setting this repo stops defining stays in
   `project.godot`, bound and pollable. Clear it with `default`.

### The rule that keeps producing bugs

**Any hand-written list of a growing thing's members goes stale in the direction that hides the bug.**
The list omits the new item, the check skips it, and the result is a pass. Three instances in one
phase:

| Where | What it silently missed |
| :--- | :--- |
| `RunModifiers.Reset()` | Orbit blades survived into the *next run* |
| `ModifierProbe.Fingerprint()` | Four correctly-wired cards reported as changing nothing |
| `TraitProbe` | Three new weapons never loaded at all |

All three were green. Read the type by reflection, or the directory by listing. And note the second
one: a hand-written list *in the test* has exactly the same hole as the one in the code, so the test
written to catch it fails the same way on the same day.

### Probes that pass by testing nothing

A separate failure mode from the three above, and by now the commoner one. Every case was green.

- `TraitProbe` checked six hardcoded weapon names; three weapons were added and it never loaded them.
  **Read the directory.**
- `CarryProbe`'s value-per-bulk stage used two items that both have a bulk of 1, which makes the two
  orderings identical. **Build the case that distinguishes them, even if it has to be invented.**
- `CarryProbe`'s drop stage emptied the bag, which emptied the crate, so the next stage had nothing
  left to check. **Order stages so the later ones still have something to measure**, and fail loudly
  when they do not.
- `MinimapProbe` checked unvisited zones *after* walking across the map, by which time none were
  unvisited. Moving it earlier immediately failed and exposed a 30 m sight radius that gave the whole
  map away.
- `ZoneProbe` counted an opening burst while the player's rifle was killing it. **Turn off what you
  are not measuring.**

### Where the balance stands

Five seeds, both arms, `lingers:0`:

| arm | survived | median banked | median seconds | median lowest HP | worst peak |
| :--- | :--- | ---: | ---: | ---: | ---: |
| past | 5/5 | 485 | 37s | 98 | 66 |
| zone | 4/5 | 1314 | 60s | 94 | 127 |

Stable across B6 and B3. B6 raised the zone median from 1120 to 1289 because on one seed the bag
filled and the loot that used to be destroyed now reaches the player; B3 moved it to 1314 and dropped
the zone arm's worst peak from 136 to 127, which is the kit killing some of the crowd.

The one death is seed `1374015655`, whose nearest zone is the tier-1 Deep Holdout. It has died since
A5d raised the pressure 30%; it is not a regression from anything after that. **Check the table in
`ac006d1` before blaming a new change for it** — that mistake has already been made once.

```bash
godot --headless --script test/BalanceSweep.cs -- seeds:5 lingers:0 zones:both
```

**Open:** the cost is bimodal, not graded. Four of five zone runs finish near full health and the
fifth dies. The bot takes the *nearest* zone, which is usually tier 0, so the sample is mostly the
easy tier while the one deep Hold is lethal. Whether that shape is right or an artefact of picking by
distance is the next thing the table can answer.

**Render the game and look at it after any visual phase.** Two defects in A3b were invisible to a
green 27-probe sweep and obvious in one screenshot: arms buried inside the torso, and a hard black
band where the sky should be.

```
godot --script test/Screenshot.cs      # no --headless; it needs a display
```

**Never put a shell metacharacter in a `python -c "..."` string.** Writing this section cost two
tries: backticks inside a double-quoted bash string are command substitution, so the line naming the
command above was replaced by the output of running it, and a `'\"'\"'` escape leaked in as literal
text. Both landed in a commit. Write a `.py` file to the scratchpad and run that — the same rule
already in force for PowerShell, for a different reason.

A1, A2 and B8 had to land together. A2 is what breaks every automated driver and B8 is the repair,
so shipping either alone leaves the repository with no working balance signal — which is the state
that made the last measurement worthless.

---

## What happened

While repairing a `godot.md` I had corrupted (PowerShell `Get-Content | Set-Content` eats non-ASCII —
see below), I ran:

```
./publish.sh --engine godot --agent claude --out C:/Projcet/godogen-survivors3d --force
```

`publish.sh` line 93 is `rm -rf "${TARGET:?}"`. `--force` deletes the **entire target directory**,
including `.git`. It emptied the repo and then printed `rm: cannot remove ...: Device or resource
busy` — that error is about removing the now-empty directory, *after* the contents were gone. I read
it as "nothing happened".

The GitHub remote was 54 commits behind, because every phase had been committed and **none had been
pushed**. Nothing on the machine survived: MSYS `rm` does not use the Recycle Bin, and shadow copies
need admin.

### Rules that follow from it

1. **`git push` after every single commit.** Not at the end of a session. A commit is "I finished";
   a push is "this still exists".
2. **Never run a flag named `--force` / `--clean` / `-rf` without reading what it deletes first.**
   Publishing a generated file is not worth a tool that can remove a directory.
3. **Never edit a text file through PowerShell** (`Get-Content`/`Set-Content`/`Out-File`). It replaces
   every em dash with `??` and mangles the rest. Write a Python script to the scratchpad and run that.
4. **`git status` must be clean and pushed before any destructive operation.**

---

## Where the code is now

Phase 29, 26 commits, `61be5db`. Verified: `dotnet build` clean, `--headless --import` clean,
`--headless --quit` exit 0.

Present: 6 weapons, 9 gear, 9 items, 35 test scripts, `scenes/{Base,Main,Player}.tscn`,
a 52-degree orthographic camera at 24 m, billboard sprite horde, no fog.

**`godot.md` and `CLAUDE.md` are gitignored and were restored by hand** (`publish.sh`'s rsync is
broken on this machine — it fails with `mkdir "/tmp/tmp.XXXX/skills/asset-gen" failed`). To restore
them again:

```bash
cd C:/Projcet/godogen-master
cp engines/godot.md   C:/Projcet/godogen-survivors3d/godot.md
T=$(mktemp -d) && cp prompts/runtime.md "$T/CLAUDE.md"
python3 scripts/render_dir.py "$T" ENGINE_NAME=Godot ENGINE_GUIDE_FILE=godot.md ASSET_SKILL_COMMAND=/asset-gen
cp "$T/CLAUDE.md" C:/Projcet/godogen-survivors3d/CLAUDE.md && rm -rf "$T"
```

---

## What was lost, in two halves

### Half A — 43 commits I never saw being made (Phases 30–~40)

These were built in an earlier session. I only ever read the finished code and its README, so what
follows is **reconstruction from reading, not a record of the work**. Expect to redesign rather than
retype. Ordered by dependency.

| # | Thing | What I know about it |
| --: | :--- | :--- |
| A1 | **Third-person camera** | 26° tilt, perspective, 52° FOV, 13 m behind, near 0.15 far 260. Replaced a 52°/24 m orthographic. Turnable: `[A]`/`[D]` turn the view, `[W]`/`[S]` advance along it; right-drag and `[Z]`/`[X]` also turn. `CameraRig.Yaw`, `CameraRig.Turn(radians)`. |
| A2 | **Turn-and-advance controls** | `Player.Steer(stick, step)` with `TurnToSteer = true`: `stick.X` feeds `_rig.Turn(-x * TurnRateDegrees * step)`, `stick.Y` advances along `(-sin yaw, -cos yaw)`. This is the single most consequential change — it breaks every automated driver that decomposes a world direction into four keys. |
| A3 | **Solid low-poly bodies** | `MeshBuilder` (Box/Tube/Ball, vertex colours), `BodyMeshLibrary`, `BodyRenderer` (one MultiMesh per variant, `FloatsPerInstance = 16`), `body.gdshader`. MultiMesh has **no skeleton**, so the walk is a vertex-stage function of rig data baked into the UV channel; pace and phase are packed into one float (integer part / fraction). Phase must **not** be derived from world position. Elite marks preserve luminance and shift hue (multiplying goes wrong on saturated vertex colours). Replaced billboards; `Horde.SolidBodies` and `Player.SolidBody` toggle back to the sprite path, which stays working. |
| ✅ A4 | **`ShadowRenderer`** | Blob shadows for the billboard path only. `DiameterPerMetre = 0.90`, `GroundClearance = 0.02`, culled beyond 26 m with a fade over the last quarter. **`Muted`, not `Visible`** — see B14. |
| A5 | **Danger zones** | `DangerZone.cs`, `ZonePlan.cs`. Rooms with kinds (Hold / Purge / Breach), `HalfExtent (13, 10)`, tier 0–1, `HoldSeconds` 35–60, spawn on the room's own walls, pay a cache with ammunition in it. `Encounter.cs`: packs placed on the map, `WakeRadius = 22`, spent on waking. This replaced a time-based spawn rate — **threat is a place, not a clock**. |
| A6 | **Walkable shelter** | `Shelter.cs` builds the room in code; `HalfWidth 11.5`, `HalfDepth 8`. Fittings: Armoury (south wall, `SlotsAlongX`), Locker, Records, Board, Map, Gate. Standing somewhere *is* the input; one verb key (`[E]`), a second verb key for fittings that have one. |
| A7 | **Minimap**, `Fog` of exploration | `Minimap.cs`, 64-cell fog image, `Extent`, drawn as a `ColorRect` in the HUD corner. |
| A8 | **Depth fog + sky** | `BGMode.Sky` with `ProceduralSkyMaterial`, `FogModeEnum.Depth`, near-black `(0.05,0.05,0.07)`, **`FogDensity = 1.0` — not zero; depth mode still scales by it and a zero density is fog that is configured and contributes nothing.** `FogSkyAffect = 1`, `FogSunScatter = 0`. |
| A9 | **Ground shader** | `ground.gdshader`: two incommensurate tilings of one detail texture, a zone-tint lookup, world-space slab seams with a per-slab hash. |
| A10 | **Scatter props** | ~1000 ankle-high non-colliding decorations through `PropRenderer`; per-instance tint must be `SrgbToLinear()`d or tinted props come out the square root of their colour. |
| A11 | **Contracts, dailies, unlocks, records** | `Contract`, `ContractBook`, `DailyRun`, `UnlockBook`, `Profile` records. |

### Half B — 11 phases I built and can describe exactly

These are mine and the decisions are recorded below. Rebuild in this order; most depend on Half A.

---

## Rebuild queue

Commit **and push** after each. Run the probes named in each entry.

### B1 — Export that reads the build back  ·  *no dependencies*

`export.ps1` at the repo root. Wipes `.godot/mono/temp/{bin,obj}/ExportRelease` **before** exporting,
then reads the finished build back: MZ header **and a non-zero body** on the `.exe`, `GDPC` on the
`.pck`, `Survivors3D.dll` present, and `Survivors3D.runtimeconfig.json` *parsed* rather than measured.

Why: an interrupted export leaves a file at full length containing zeroes, and MSBuild's
`GenerateRuntimeConfigurationFiles` is incremental — an output newer than its inputs is never
regenerated, so the hole is copied into every later build. A 372-byte runtimeconfig of zeroes shipped;
hostfxr refused it, no C# loaded, and the game opened a window that did nothing. Nothing reported a
failure at any point.

`$ErrorActionPreference` must be `Continue` around the `godot` call: Godot writes a harmless
"Unable to open Android 'build-tools' directory" to stderr and Windows PowerShell turns native stderr
into a terminating error under `Stop`.

Also needed: **`Survivors3D.sln`** — Godot's .NET export wants a solution and prints an error per C#
file without one, then *finishes with exit code 0* producing a game-less build.

Also in this phase: `ShopCatalogue.FindItem` must cache. It scanned `res://resources/items` and
`GD.Load`ed every `.tres` on each call, and its only caller is `StashValue`, which `Shelter._Process`
asks for every frame. In an exported build the repeated load raced the script bridge and printed a
"Handle is not initialized" backtrace every frame.

### B2 — Enemies arrive instead of appearing; the dark closes in  ·  *needs A3, A5, A8*

- `EnemyPool.Emerge[]`, 0→1 over `Horde.EmergeSeconds = 0.34`, advanced in the tick loop for **every**
  enemy (not just the near ones on the movement stride). All three renderers multiply instance scale
  by `1 - (1-e)²`, floored at 0.04 — a zero-determinant basis is not a body of no height. Cosmetic
  only: flow field, collision and damage treat a half-risen enemy as fully present.
- Fog: `FogDepthBegin 20`, **`FogDepthEnd 42 → 35`**, **`FogDepthCurve 1.1 → 1.6`**. The ground falls
  away from a 26° camera, so 42 put full black ~35 m in front of the player and nothing spawns that
  far out. 35 puts it ~24 m out, just past the farthest a room can put an arrival.
- `DangerZone` spawn bearing flips when the point is within 22 m **and** within 60° of
  `Player.Facing` — but only when the opposite wall is not itself too close.
- Probe: `EmergeProbe` — asks the pool, not a screenshot. Godot runs up to eight physics ticks on the
  first frame after a scene loads, so the ramp is over before any picture exists. Spawn the subject
  **far from the player**, because the player shoots.

### B3 — A third kind of growth card  ·  *needs A3*

Four options that are not the weapon, in `RunKit.cs` (a `Node3D` **beside** the player, not under it —
parenting inherits the body's yaw and the ring would turn with the character):

| Card | Numbers |
| :--- | :--- |
| `Orbit` | `OrbitRadius 1.5`, `OrbitBite 1.0`, `OrbitDamage 7`, `OrbitInterval 0.3 s`, `OrbitSpin 3.6 rad/s`, up to 8 blades. **1.5 not 2.3**: enemies stop at the horde's 0.7 m contact radius, so a ring at 2.3 with a 0.7 bite covers ground a walker crosses once and never occupies. |
| `Shockwave` | `PulseInterval 5 s − 0.45/stack`, floor 0.8; `PulseRadius 5 + 0.5/stack`; `PulseDamage 20 + 6/stack`; `PulseKnockback 1.5`; 7 sparks drawn at the **edge** so an empty pulse is still visible. |
| `Chain` | +18%/stack, `ChainRange 4.5`, `ChainFraction 0.45`. Hooked in `WeaponHandler.RecordHit(category, where, kind, damage)`. **One jump, never two** — the arc damages through `Horde.Damage` and announces itself directly rather than re-entering `RecordHit`. Excludes the original by **distance** (`NearestOutside(from, range, 0.6)`), not by index: a kill swap-removes and the index means something else. |
| `Chill` | `Chill = 1 − (1−Chill)·0.83` per stack (multiplicative, never reaches zero — an enemy stopped dead is a free kill). Applied in the horde movement loop against `Horde.ChillRadius = 7.5`, full at the player and none at the edge. |

`GrowthRarity.Kit` with `KitWeight = 0.9`; `WeaponWeight 4.0 → 4.8` to stop four new entries taking
the weapon from ~23% of draws to ~19%. Caps: Orbit 5, Shockwave 4, Chain 4, Chill 3.

New `HitKind`s: `Orbit`, `Shockwave`, `Chain`, each with its own row in `EffectDirector.BurstFor`
(the burst table must stay one distinct row per kind — `HitFeedbackProbe` checks).

New `Horde` API: `Within(centre, radius, int[] result)`, `Mark(at, kind)`,
`Damage(index, amount, knockback, HitKind mark)`, `NearestOutside(point, radius, minDistance)`.

Probe: `KitProbe`, with the weapon **silenced by `SetPhysicsProcess(false)`** — `Equip(null)` throws.

### B4 — A fourth equipment slot: trinkets  ·  *needs B3*

`GearSlot.Trinket`; `Profile.EquippedGear` 3 → 4 (**no version bump** — the reader stops at whichever
of file and array is shorter, so a three-entry save loads with an empty trinket). `GearResource` gains
`OrbitBonus`, `ShockwaveBonus`, `ChainBonus`, `ChillBonus` and their `*UpgradeCap`s.
`Player.ApplyGearKit(orbit, shockwave, chain, chill)` — its own call, not four more arguments on
`ApplyGearRules`; chill compounds rather than sums.

| Trinket | Tier/Price | Grants |
| :--- | :--- | :--- |
| Whetstone | 2 / 550 | +1 blade, blades to 7 |
| Cracked Capacitor | 2 / 550 | +1 pulse, pulse to 6, **−25 health** |
| Copper Coil | 2 / 650 | +18% chain, chain to 6 |
| Frost Cell | 3 / 900 | +17% chill, chill to 5 |
| Lucky Bone | 2 / 700 | +1 safe box, fortune to 6 |
| Tourniquet | 2 / 600 | +0.6 regen, regen to 6 |

**Filenames come from the gear name lowercased** — never use an apostrophe ("Lucky Bone", not
"Rabbit's Foot"; "Crayon Drawing", not "Child's Drawing").

The armoury rack divides one wall by the catalogue size, so its `Extent.X` goes 8 → 10. `ShelterProbe`'s
walk tolerance must then be **two fifths of a rack step**, not a fixed 0.9 m, or it stops a peg short.

Probe: `TrinketProbe`. Fit the trinket **after** `AddChild` — `MetaManager._Ready` assigns a fresh
`Profile` when `Ephemeral`, discarding anything set before.

### B5 — Three weapons that resolve differently  ·  *no hard dependency*

New `WeaponTrait`s that change how the attack resolves:

| Weapon | Tier/Price | Trait |
| :--- | :--- | :--- |
| Pump Shotgun | 2 / 1300 | `Spread`, 8 pellets at 34%, 20° cone, `SpreadFloorFraction 0.8` |
| Marksman Rifle | 3 / 2200 | `Charge`, ×3.5 after 3 s idle, penetration 3 |
| Bolt Launcher | 3 / 2000 | `Blast`, 4 m on impact, penetration 1 |

- `Spread` fires *n separate shots*, each rolling its own line — refactor the hitscan block into
  `Hitscan(weapon, origin, direction, level, range, damage, knockback, crit)` and loop it.
- `SpreadFloorFraction` is new on `WeaponResource` (default 0.2). The old global fifth would put eight
  pellets inside four degrees on a practised shotgun.
- `Charge` needs `Slot.SinceFired`, ticked for **both** slots (a holstered sniper is still waiting) and
  cleared at every real attack site including the burst queue. HUD shows `CHARGED`.
- `Blast` needs `ProjectilePool.Blast[]`; the bolt **stops where it connects** whatever penetration says.
- Armoury second verb: **carry as sidearm** (`BaseScreen.ChooseAsSidearm`, `ActSecond` takes a slot).

Probe: extend `TraitProbe` — read the weapon **directory**, not a hardcoded list of six names.

### B6 — A full backpack is a decision  ·  *no hard dependency*

- `LootContainer` keeps what would not fit in its own `Inventory _remains`; re-searching does not
  re-roll; `Progress` resets on a partial haul (otherwise it re-runs every tick against a full bag).
- `Emptied(int value, bool finished)` — two arguments, so `RunLog` values every visit and counts the
  crate once.
- `[R] drop` throws away the worst thing by **value per bulk** (securing uses value alone).
  `Inventory.LeastValuableIndex()`, `Player.TryDropWorst()`, `IInputSource.DropPressed`,
  `TouchAction.Drop` (append to the enum — it indexes the button array).
- Retire `reload`; nothing ever read it.
- HUD shows what is waiting and what it would cost.

Note: a tier-1 zone cache is 5–9 rolls plus twelve boxes of rounds — **more than a starting backpack
holds when empty**. That is the design working; `SupplyProbe`'s late-crate stage needs
`ApplyGear(carry: 60)`.

Probe: `CarryProbe`.

### B7 — Curiosities  ·  *needs B6*

Six pieces in two sets. `ItemResource.CollectionName`; `CollectionBook`; `Profile.Collected` and
`Profile.ClaimedSets`.

| Set | Pieces | Bounty |
| :--- | :--- | ---: |
| Someone's Life | Wedding Ring 150, Crayon Drawing 130, Dog Tags 170 | 400 |
| The Grid | Fuse Coupling 280, Turbine Blade 320, Control Rod 300 | 900 |

All Rare, 2 bulk (the size of a medkit, on purpose), weight 0.20–0.30.

**Selling them is not a mistake** — the record is written when the piece lands in the stash, at the
door rather than at the locker. `LootContainer.Curiosities = false` on every dropped cache: a payout
owes ammunition, and the first zone cache after these shipped handed over four curiosities and three
supplies. Records wall lists both sets with pieces ticked.

Probe: `CollectionProbe`.

### B8 — The bot could not walk  ·  *needs A2; do this EARLY, everything measurable depends on it*

`test/BotDrive.cs`: converts a world direction into turn-and-advance key presses. **Three** drivers
need it — `AutoPlay`, `ShelterProbe`, `Presentation`. Without it a driver holds the turn key forever
and never advances; it moves enough that a stuck detector never fires and arrives nowhere
(`AUTOPLAY FAILED — could not reach Crate5 in 60s (still 57.6m away, peeled off geometry 0x)`).

Deadband `aligned < 0.995`; advance while still turning (`aligned > 0`); at exactly 180° the cross
product is zero and neither key gets pressed — pick a side.

Two input bugs found alongside:
- **`menu_daily` was bound to D, which is also `move_right`** — turning at the map table spent the
  day's one attempt, permanently. Replace the per-fitting keys with one `interact_second` on **C**.
- The armoury's second verb had **no keyboard binding at all**.
- `ShelterProbe` must assert on the **bindings** (no non-movement action shares a physical key with a
  movement one), not by walking: `Input.ActionPress` moves an action, and the collision is two actions
  on one key.
- Retire `fire`, `menu_reroll`, `menu_daily`.

`physics_ticks_per_second = 60` pinned via **`ProjectSettings.SetInitialValue(...)` then `SetSetting`** —
`Save()` omits any setting equal to its default, so a hand-edited line vanishes the next time the input
tool runs.

### ✅ B9 — The floor stopped being a table  ·  *done*

`Terrain.cs` — an analytic height function, **not** a mesh (raycasts do not reliably hit
`ConcavePolygonShape3D`). Two octaves of value noise, coarse wavelength **18 m** (tuned against the
fog: the dark closes 24 m out, so a 40 m wave is half a wave in view and reads as a tilted camera),
fine 6.3 m at 0.32 weight, flat within 7 m of the origin fading to full by 16 m.

`Amplitude` shipped at **1.75**, not the planned 1.05. 1.05 rendered as a table: at this camera height,
over an 18 m wave, the slab seams still read as straight lines. At 1.75 the seams curve and the horizon
bows, and a four-metre block still sits on it without a gap under one corner — which is what bounds it
from above.

`GroundMesh.cs` builds the floor from `Terrain` (200 m at 2.5 m = 6561 vertices; a `.tscn` is a text
file), extending `StaticBody3D` because it is attached to the Ground body itself.

**The winding rule in the plan was wrong.** Godot's front face is the one whose *engine* normal points
at the camera, and the engine normal is the negative of the right-hand rule — `Plane(v0, v1, v2)` is
`(v0 - v2) × (v0 - v1)`. A floor is therefore front-facing from above when the right-hand normal of its
winding points **down**, which is counter-clockwise seen from above. Authored the plan's way first: the
screenshot was a black void with the scatter floating in it, and it looked exactly like a mesh that
never built. The shading normals have to be negated to match, or the floor is lit from underneath and
comes out black anyway — the same symptom from a different cause.

**`GroundMesh` cannot build in `_Ready`.** `Ground` is ready long before `LevelGenerator` runs, so the
floor it builds belongs to whatever offset the previous run left. Nothing about this is visible: the
ground still looks like ground and the props still look planted, because both are plausible surfaces.
`GroundMesh.Rebuild()` is public and the generator calls it once the offset is fixed. `TerrainProbe`
reported 12,744 of 12,800 triangles off the height field — the only place the disagreement was legible.

**Do not draw the terrain offset from `_rng`.** It is two `NextFloat()` calls and it shifts every draw
the generator makes afterwards, so the same seed lays out a completely different map. Hash it off
`Seed` instead. Cost: two probes started failing with enemies that would not walk, and the terrain
looked like the last thing that could be responsible — the enemies were fine, their cover had moved.

**The simulation stays 2D.** Only things that draw consult `Terrain`, plus props placed once. The floor
collider stays a flat box; the player is planted after `MoveAndSlide`. Planted: blocks, crates, pads,
zones, scatter, `PropRenderer`, `BodyRenderer`, `HordeRenderer` (bodies and projectiles), the camera
rig, hazard decals, and effect puffs. Puffs are planted inside `EffectPool.Spawn` rather than at eight
call sites, so `EffectDirector.OnFired` has to flatten the player's position first or the ground height
is added twice.

`ShadowRenderer` plants its blobs the same way; it did not exist when this was written.

Probe: `TerrainProbe`, eight stages. The two the plan named: the enemy pool's Y stays **zero** over
ground that is provably not flat, and `NearestWithin` finds a target 12 m away across 0.5 m of drop at
range 13 and misses it at 11. Both halves of every stage assert presence as well as absence — "the pool
is flat" passes vacuously over a dead height field, so it also counts how many of those enemies are
standing on relief.

`test/sweep.ps1` runs all 38 headless probes. It existed as a habit rather than as a file, which is how
it kept being run from memory against a stale list.

### ✅ B10 — Three landmarks, and the only three.js  ·  *done*

`art-src/models/build.mjs` authors a lattice pylon (564 tris, 12.7 m), a ribbed silo (264 tris,
10.6 m) and a crushed coach (272 tris, 8.6 m long, 2.8 m tall) in three.js and writes `.glb` to
`assets/models/`. `npm install && npm run build`. `GLTFExporter` wants `FileReader` — a nine-line shim
over `Blob` covers it. Colour in materials, not `COLOR_0`.

three.js is an **offline modelling tool** for shapes `MeshBuilder` cannot make, not a renderer and not
a replacement. Nothing at runtime knows it exists; `art-src/models/.gdignore` keeps Godot from even
scanning it, and `node_modules/` is ignored by git.

**Flat shading does not export.** `flatShading: true` is a three.js render-time flag with no glTF
equivalent, so the exporter drops it silently and the model arrives in Godot smoothly shaded — next to
a hundred faceted props. Bake it: `toNonIndexed()` then `computeVertexNormals()`, so every triangle
owns its vertices.

**`godot --headless --import` after every rebuild.** The import cache is keyed on the file and will
not notice a rewritten `.glb`. A recolour that "did not apply" is this, and the next thing edited is
the wrong file.

`LandmarkLibrary.cs`: **never a MultiMesh** (it loses imported meshes on pack/save), **never a trimesh
collider**, and the footprint is measured off the instantiated AABB — a number written down beside a
mesh stops being true the first time the mesh is edited. `Centre()` exists because the coach is not
symmetric about its own origin once it has been crushed.

A landmark is a `Block` for every purpose except drawing: it is a field on the struct, not a parallel
list, so `PushOutOfBlocks`, `EnsureReachable`, the collider and the flow-field bake all see it without
anyone remembering to add it to a fourth place.

**`BuildLandmarks` rolls from a side stream hashed off `Seed`, not from `_rng`** — same reason as the
terrain offset in B9, learned the same way.

Probe: `LandmarkProbe`, seven stages. The field stage asks a **behavioural** question, because the
obvious one fails on a correct field: `FlowField.Sample` deliberately returns a neighbour's flow for a
blocked cell so a body inside an obstacle can walk out, which makes a blocked cell read exactly like an
open one. Instead it puts one walker on the far side of the widest landmark and watches. It closes 97%
of a 25 m gap in 1800 ticks and never enters the footprint — and the reason the budget is thirty
seconds is the finding: the walker first moves *twelve metres away* from the player to clear the
pylon. At 900 ticks it had closed 37% and looked stuck. It was going round.

### B11 — The deck was five times the size of the run  ·  *needs B8 (to measure)*

`RunGrowth.BaseLevelCost 12 → 6`, `LevelCostStep 5 → 1.2`.

At 12 + 5n a run earned ~147 experience and reached level 6 — six cards against a deck of 22 whose
ceilings sum to ~50. The weapon at ~a fifth of draws is one level in six, and eight would take 36.
Nothing was wrong with the weights; the run was too short to spend them.

Add growth to the `SWEEP` line (`level`, `picks`, `weaponLv`, `weaponMax`, `ceilingAt`) and a second
table in `BalanceSweep.Report` — printed **after** the loop that fills it.

Measured before → after (biome 0, 4 zone counts × 5 layouts):

| Rooms | out | banked | level | → | out | banked | level |
| ---: | :-: | ---: | ---: | :-: | :-: | ---: | ---: |
| 0 | 4/5 | 477 | 4 | | 5/5 | 290 | 9 |
| 1 | 4/5 | 801 | 6 | | 5/5 | **840** | 12 |
| 2 | 2/5 | 347 | 8 | | 2/5 | 552 | 17 |
| 3 | 0/5 | 200 | — | | 1/5 | 730 | 17 |

`HudProbe` then fails and is right to: it whittles health with 1-point hits and armour subtracts a
flat amount, so once the deck starts handing over armour every call is absorbed. Use
`TakeDamage(_player.Armour + 1)`.

### B12 — There is air in it  ·  *needs B9*

`AirDust.cs` + `motes.gdshader`: 400 two-centimetre motes in a 34×7×34 m slab riding with the player.
One buffer upload at startup, nothing per frame — each mote's lane and phase live in its custom-data
block and the shader wraps `home + drift*TIME` with a `fract`.

**`INSTANCE_CUSTOM`, not `CUSTOM0`.** The latter is a *mesh vertex* attribute; every quad shares one
mesh without it, so every mote reads zero and the field collapses to one dot. It compiles.

`brightness 0.16`, `Size 0.05`. Found by turning it *up*: at 1.0 with 14 cm quads it is heavy
snowfall, which is how the shader was confirmed to work at all.

### B13 — A limiter on the master bus  ·  *no dependencies*

`AudioBus.Install()` from `SoundDirector._Ready`, guarded (AudioServer is global; every scene builds
its own director). `AudioEffectHardLimiter`, `CeilingDb −0.3`, `Release 0.12`, no pre-gain. Not a
`default_bus_layout.tres` — every other piece of configuration here is generated with its reasoning
next to it.

`AudioProbe` stage computes the mix rather than metering it (the headless driver processes no audio,
so a peak meter reads silence forever and would pass against a bus with no limiter):

```
worst case: 4 music layers sum to 0.41, 14 voices at 0.90 sum to 5.63, total 6.04
```

Six times over unity. Four simultaneous impacts already pass it.

### ✅ B14 — The proof video, and the double-draw it found  ·  *done*

```bash
godot --write-movie screenshots/result/run.avi --fixed-fps 30 --quit-after 1200 \
      --script test/Presentation.cs
ffmpeg -i screenshots/result/run.avi -c:v libx264 -preset slow -crf 25 \
       -pix_fmt yuv420p -movflags +faststart screenshots/result/run.mp4
```

**Write `.avi`, not a PNG sequence.** Godot's PNG writer spends 430 ms per frame encoding; the same
1200 frames as AVI take 55 seconds instead of nine minutes.

`Presentation.cs` drives through `BotDrive`. One of each elite mark at 8 s, and the boss when the bot
reaches the pad, placed relative to the **camera's** heading (`CameraRig.Forward`), not
`Player.Facing`.

Three things about that placement, each of which cost a take:

- The camera is 13 m behind the body, and the fog closes about 24 m from the *lens*. Sixteen metres
  in front of the player is 29 m from the camera, which is black. Nine puts the elites inside it.
- The boss is 5.5 m tall. At ten metres it filled the frame from below and reached the pad, taking
  the player from 90 hp to 32 during the hold. It keeps sixteen.
- **Cue the boss off the stage, not off the clock.** The run ends when the extraction completes, and
  where that lands depends on how long the route took — which changed when the map got cover, and
  again when it got terrain. A boss cued at a fixed 32 s against an extraction that finished at 31.4 s
  spawned into a run that had already ended.

**`FilmedEnemyCap = 90` is what decides whether the take ends in an extraction or a death.** The
shipping cap is 160 and a compressed run reaches it: the bot arrived at the pad with 67 hp and 156
enemies on the field and lost all 67 in the 4.3 s before the five-second hold finished. Lowering the
opening crowd does not help (the director refills it in ten seconds) and lengthening the run moved the
death by 2.6 s. What matters is how many bodies are touching the player while it stands still.

Take timeline, for comparison when it next drifts: crate at 10.8 s, looted at 13.1 s, pad at 26.8 s,
extracted at 31.4 s with 514 banked and 77 killed.

`--quit-after` counts **rendered** frames; `_tick` counts **physics** ticks. Physics is 60 Hz and the
capture renders at 30, so the 1200-frame clip is 2400 ticks.

#### What the video found

**Every enemy in the game was being drawn twice** — a low-poly body and a pixel-art billboard standing
in the same place — for as long as both renderers have existed. `Horde._Ready` set
`_renderer.Node.Visible = false` once; `HordeRenderer.Upload` assigned `Node.Visible = count > 0` on
every sync and turned it straight back on. It is `Muted` now, a flag `Upload` respects.

It survived every probe, because none of them asked what was on the screen, and it survived every
screenshot, because at the distance those are framed at the two silhouettes overlap into one slightly
odd shape. One frame of the video with a runner close to the lens made it obvious.

`BodyProbe` stage 7 catches it: 12 spawned enemies, 100 ticks, and the billboard node must still be
hidden. Verified by reintroducing the bug.

### ✅ A4 — `ShadowRenderer`, and the first thing that ever ran the sprite fallback  ·  *done*

Blob shadows under the billboard enemies: a soft dark ellipse per body, sized off
`DesignHeightMeters * 0.90`, two centimetres off the terrain, culled at 26 m with a fade over the last
quarter of that. Muted on the solid-body path, where the bodies cast real shadows — the same `Muted`
flag B14 added to `HordeRenderer`, for the same reason, and it is built *outside* the `if (SolidBodies)`
branch because inside it the renderer would only exist in the one configuration that mutes it.

The shadows are the small half of this. The large half is that `ShadowProbe` is the **only thing that
has ever run the sprite path**: `SolidBodies` is on everywhere else, so the billboard renderer, its
texture array, its shader and now these shadows were code that shipped and never executed — the state
a fallback is in when it is finally needed and turns out not to work. `test/Screenshot.cs` takes a
`sprites` argument now for the same reason.

**A four-stage probe passed while the blobs drew nothing at all.** Count, transforms, heights,
orientation, node visibility, node parentage: all correct, all readable, all read. The screen was
empty. Turning the opacity to 1.0 and the diameter to 3x changed nothing, because the fault was not in
any number — a MultiMesh writes its per-instance colour into `COLOR` in the **vertex** stage, and
`COLOR` in the fragment stage is the interpolated vertex-colour *attribute*, which a `QuadMesh` does
not have. Reading it there is an alpha of zero. **Forward it through a `varying`.**

What identified it was swapping the `ShaderMaterial` for a flat red `StandardMaterial3D` and watching
the screen fill with red — one render that separated "the plumbing is wrong" from "the shader is
wrong", after three that only re-confirmed the plumbing was right.

Two smaller ones on the way:

- **`SurfaceSetMaterial` does nothing on a `PrimitiveMesh`.** It is what every other renderer here
  calls, because every other renderer builds an `ArrayMesh`. A `QuadMesh` carries its material in
  `Material`; this uses `MaterialOverride` on the node, which is unambiguous. It compiles either way
  and it does not warn.
- The stage that catches the varying is a **text check on the shader source, with comments stripped
  first**. The prose above says `COLOR` in `fragment()` several times, and this would otherwise be the
  third probe in this repository to pass by reading a comment.

---

## Suggested order

`B1 → B8 → A1 A2 A3` (the camera and bodies gate almost everything) `→ A5 A6 A8 → B2 → B3 → B4 →
B5 → B6 → B7 → B9 → B10 → B11 → B12 → B13 → B14`.

B1 and B13 have no dependencies and can go first as quick wins. **B8 must precede any balance
measurement.**

## Verification

```bash
dotnet build && godot --headless --import && godot --headless --quit
```

Then the probes named in each entry, then the full sweep. `AutoPlay` and `BalanceSweep` are the only
balance signal and are worthless until B8.

`TouchProbe` needs a real display and never runs headless. So do the five capture scripts —
`ScaleProbe`, `Screenshot`, `BaseShot`, `DebriefShot`, `BillboardCompare`. They used to say so in a
comment and then **hang forever** when run headless anyway, spinning a core and printing nothing; a
sweep that started one looked like it was still working. Four were found alive at once, from runs two
days apart. They refuse now — see `test/Display.cs`.
