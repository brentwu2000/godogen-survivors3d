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
| ✅ B9 | `Terrain` + `GroundMesh` — the floor has relief and the simulation never noticed | `3641daf` |
| ✅ B10 | Three glTF landmarks, authored offline in three.js | *this* |

**Next: B14** (the proof video), then A4 last.

**Half A is done except A4.** Blob shadows were the billboard path's only ground contact and solid
bodies cast real ones, so A4 is a fallback-path fix rather than a visual one — the lowest-value item
left in either half.

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
| A4 | **`ShadowRenderer`** | Blob shadows for the billboard path only (`Visible = false` when `SolidBodies`). `DiameterPerMetre = 0.90`, `GroundClearance = 0.02`, culled beyond 26 m. |
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

`ShadowRenderer` is not in the list because it does not exist yet — it is A4.

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

### B14 — The proof video  ·  *needs B8, and everything you want filmed*

`Presentation.cs` drives through `BotDrive`. One of each elite mark at 8 s and the boss at 32 s,
placed relative to the **camera's** heading (`_rig.Yaw`), not `Player.Facing` — the camera is 13 m
behind the body with its own yaw, so "in front of the player" is not "on screen". The boss at 13 s had
27 seconds to cross 22 m at 1.15 m/s, did, and killed the take.

`--quit-after 1200` (40 s). `_tick` counts **physics** ticks (60 Hz) while `--fixed-fps 30` fixes the
render rate, so a 1200-frame clip is 2400 ticks.

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
