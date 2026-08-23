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
| ✅ A3b | `BodyMeshLibrary`, `BodyRenderer`, `SoloBody`, `body.gdshader` | this one |

**Next: A4** (blob shadows for the sprite path), then A5 A6 A8, then B2 onward.

A4 matters less than it did — shadows were the billboard path'"'"'s only ground contact, and solid
bodies cast real ones. It is now a fallback-path fix rather than a visual one.

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

### B9 — The floor stopped being a table  ·  *needs A3, A9, B8*

`Terrain.cs` — an analytic height function, **not** a mesh (raycasts do not reliably hit
`ConcavePolygonShape3D`). Two octaves of value noise, `Amplitude 1.05`, coarse wavelength **18 m**
(tuned against the fog: the dark closes 24 m out, so a 40 m wave is half a wave in view and reads as a
tilted camera), fine 6.3 m at 0.32 weight, flat within 7 m of the origin fading to full by 16 m,
offset from `GameSession.RunSeed`.

`GroundMesh.cs` builds the floor in `_Ready` (200 m at 2.5 m = 6561 vertices; a `.tscn` is a text
file). **Wind the triangles clockwise seen from above** — the other order is culled from every angle
the camera can reach, and with black fog behind it that looks exactly like a mesh that failed to build.

**The simulation stays 2D.** Only things that draw consult `Terrain`, plus props placed once. The floor
collider stays a flat box; the player is planted after `MoveAndSlide`. Plant: blocks, crates, pads,
rooms, packs, `PropRenderer` (Y translation, was a hard zero), `BodyRenderer`, `ShadowRenderer`,
`HordeRenderer` (bodies and projectiles), hazard decals, effect puffs.

Probe: `TerrainProbe` — the enemy pool's Y must stay **zero** over ground that is provably not flat,
and `NearestWithin` must find a target 12 m away across 0.5 m of drop at range 13 and miss it at 11.

### B10 — Three landmarks, and the only three.js  ·  *needs B9*

`art-src/models/build.mjs` authors a lattice pylon (357 tris, 13.1 m), a ribbed silo (334 tris,
10.6 m) and a crushed coach (133 tris, 2.7 m) in three.js and writes glTF to `assets/models/`.
`npm i three`. `GLTFExporter` wants `FileReader` — a nine-line shim over `Blob` covers it. Colour in
materials, not `COLOR_0`. Flat shading.

three.js is an **offline modelling tool** for shapes `MeshBuilder` cannot make (lattices, cones), not
a renderer and not a replacement. Nothing at runtime knows it exists.

`LandmarkLibrary.cs`: **never a MultiMesh** (it loses imported meshes on pack/save), **never a trimesh
collider** (takes the frame under a second, never errors), and measure the footprint off the
instantiated AABB. A landmark is a `Block` for every purpose except drawing, so the reachability sweep
counts it.

Probe: `LandmarkProbe`. Its field stage must ask a **behavioural** question — `FlowField.Sample`
deliberately returns a neighbour's flow for a blocked cell so a body inside an obstacle can walk out.

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
