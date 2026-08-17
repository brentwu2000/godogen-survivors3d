# Survivors 3D

A 2.5D extraction horde-survivor in Godot 4.7.1 (C# / .NET 9). Orthographic Brawl-Stars framing,
Vampire-Survivors crowd density, Tarkov's loot-fight-extract stakes: what you carry out is banked,
what you die holding is gone.

Status: the loop is complete and playable end to end — camera and movement, the horde and its
performance floor, weapons and proficiency, loot and extraction, the meta layer and saves, and the
proof video. Every probe below passes. Build gate re-verified 2026-08-17.

## Running it

```bash
dotnet build                         # 0 warnings, 0 errors
godot --headless --import            # import the assets
godot --headless --quit              # scene loads clean
godot                                # play
```

WASD/arrows move, weapons fire themselves (mouse or space forces a shot), `[E]` interacts, `[R]`
reloads, `[F]` secures the top item into the safe box. Actions are read through `IInputSource`, so the
touch implementation drives the same code with two virtual sticks.

The build gate is those first three commands. Every stage closes against it plus a probe below.

| Script | Headless | Asserts |
| :--- | :---: | :--- |
| `test/MovementProbe.cs` | yes | Synthetic input moves the player; the camera follows within its lag budget |
| `test/FlowFieldProbe.cs` | yes | An enemy behind the long wall routes around it instead of into it |
| `test/WeaponProbe.cs` | yes | Per-category mechanic: penetration, arc, travel time, and every proficiency curve |
| `test/RunLoopProbe.cs` | yes | Six stages: extraction closed at t=0 → loot → leave-resets → contact damage → enrage → bank |
| `test/MetaProbe.cs` | yes | Profile round-trip, malformed/future files rejected, safe box keeps only what was secured |
| `test/AutoPlay.cs` | yes | A whole run driven through the real input layer at real speed — the only balance signal |
| `test/HordePerf.cs` | no | Frame time, physics time, draw calls under load (`-- 500`) |
| `test/ScaleProbe.cs` | no | Sprite world-height read against a 2 m reference pole |
| `test/BillboardCompare.cs` | no | The side-by-side that settled full-billboard vs Y-locked |
| `test/Screenshot.cs` | no | Still of the main scene |
| `test/Presentation.cs` | no | The proof video (see Capture) |

Probes are exit-code judged, so they can all be chained. The ones marked "no" need a real rendering
driver — the null driver has nothing to capture and no draw calls to count.

Scenes are not hand-written. `scenes/Build*.cs` emit `.tscn` at build time
(`godot --headless --script scenes/BuildMain.cs`), and `scripts/tools/Build{InputMap,Weapons,Items}.cs`
emit the input map and the `.tres` data the same way.

### Capture

```bash
godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 700 \
      --script test/Presentation.cs
ffmpeg -y -framerate 30 -pattern_type glob -i 'screenshots/result/frame*.png' \
      -c:v libx264 -pix_fmt yuv420p -movflags +faststart screenshots/result/survivors3d.mp4
```

`--quit-after N` writes exactly N frames, so that number *is* the length: 700 @ 30 fps = 23.3 s.
`screenshots/` is not versioned; re-run the above to regenerate it.

`Presentation.cs` injects compressed values into `_Initialize` — 70 enemies at open, a 110 s run,
extraction open from t=0, spawn 6→16/s — and does not touch `RunDirector`'s own defaults. Shot at
shipping numbers the first minute is an empty field: correct by design, and nothing to film. The two
crates sit on opposite corners deliberately, so the walk back to the pad is part of the shot.

The camera's `Position` and `RotationDegrees` are set in `BuildMain.BuildCameraRig`, because the first
movie frame renders before `_Process` and `CameraRig`'s lerp has not run yet.

## The loop

A run is 300 s. The horde spawns at 2/s and ramps to 12/s while its speed scales to 1.6x. The
extraction pad opens at 15% of the clock and needs a 5 s hold, cancelled by stepping out.

The backpack holds **20 bulk, not 20 slots** — dumping bulky scrap to fit a small vial is the trade,
and a full bag still takes what fits rather than refusing the crate. Banking pays
`value × ExtractionMultiplier`, which climbs 1.0 → 3.0 across the run. The safe box holds 4 bulk,
takes one item at a time while the horde keeps coming, and pays **face value only** — it is the hedge
against dying, never a way to farm the multiplier. Die and the backpack is lost; the safe box and all
weapon proficiency survive.

## Decisions, and the numbers that settled them

**Full billboard, not Y-locked.** Under the same camera, `FixedY` is crushed to ~62% height by the
52° pitch — characters read short and wide and the sprite's vertical resolution is wasted.

**Alpha scissor, not alpha blend.** Hundreds of overlapping camera-facing quads cannot be
depth-sorted, and blending turns that into what looks like a shader bug. Matted sprite alpha is
strongly bimodal: only **0.26%** of pixels land in the 64-191 band a threshold has to cut, and a 0.5
cut differs from the matte foreground by 12 pixels.

**Sprites cast no shadows.** A quad that always faces the camera projects a rectangle that swings as
the camera turns. Contact comes from a flat ground decal with depth writes off, so coplanar decals
don't z-fight.

**MultiMesh over procedural `QuadMesh` only, never GLB.** The mesh-loss-on-pack trap is the imported
model, not the MultiMesh. Billboarding is rebuilt in `vertex()` from `INV_VIEW_MATRIX`, and the
per-instance scale is re-applied — overwriting `MODELVIEW_MATRIX` otherwise throws the MultiMesh's
own scale away and every instance renders at 1 unit. Animation state rides `INSTANCE_CUSTOM`.

**Enemies are not physics bodies.** The game asks one question about an enemy — who is near me — and
a uniform `SpatialGrid` answers it with an O(n) counting sort per tick, so separation is a single
pass instead of a constraint solve. Within 15 m enemies separate and update every tick; beyond it
they follow the field on a 4-tick stride spread by index, so no tick carries the whole far set.

**Flow field, not per-agent navigation.** A BFS toward the player every 8 ticks; everyone samples one
field. The distance pass is 4-directional (8 cuts corners through obstacles) and the gradient is read
8-directionally for smooth headings. Obstacle footprints are dilated by the enemy radius, or the
field steers bodies into gaps they don't fit through.

**Hits query the horde, not `Area3D`.** With no physics bodies there is nothing for an area to
detect. `Horde.QueryArc` / `QueryRay` / `NearestWithin` linear-scan the pool — weapons fire a few
times a second, not per enemy per tick, so a flat scan over a few hundred beats walking the grid.
`SwingArcDegrees` is the **full** angle; as a half-angle the axe's 100 became a 200° sweep that hit
enemies behind the player.

**Hitscan needs a tracer.** Resolved instantly, nothing appears on screen and the player can't tell
firing from jamming. A zero-damage projectile is fired purely to be seen, skipped at the collision
stage by its zero damage.

**Damping is exponential**, never a fixed per-tick multiplier — the latter silently changes feel if
`physics_ticks_per_second` ever moves off 60.

**Saves are JSON, written to a temp file and renamed.** A corrupted or hand-edited file fails with a
parse error that can be reported, instead of deserialising into an object with one quietly wrong
field. A version mismatch rejects the whole file; nothing is partially applied. Until the rename
succeeds, the one file a player can't afford to lose is intact on disk.

**Scene builders have three silent failure modes**, all handled by `SceneBuildUtil.Run`: every
builder is wrapped so an exception still reaches `Quit()` (headless has no window to close, so a
throwing builder hangs instead of failing); node counts are compared before pack and after
re-instantiating, because dropped nodes look exactly like success; and `SetScript()` releases the C#
wrapper, so the root is parked under a temp node and retrieved via `GetChild(0)`.

**Some bugs only exist on camera.** After a run ended, the HUD kept drawing its hold bar, so
`EXTRACTING [####....]` sat under the `EXTRACTED` banner and read as frozen UI. `Hud.BuildPrompt` now
returns empty when `State != Running`. Every probe is exit-code judged; not one of them could have
seen it.

**Input actions are generated**, not hand-written into `project.godot` — `Object(InputEventKey,...)`
literals are version-sensitive and a malformed one drops the whole action with no error.

## Performance

RTX 3070 Ti, 1080p, vsync off, player moving so the field actually rebuilds:

| Enemies | Mean | Median | p95 | Draw calls | GC |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 200 | 1.12 ms | 0.59 ms | 1.54 ms | 9 | 0 |
| 500 | 1.08 ms | 0.56 ms | 1.64 ms | 9 | 0 |

200 and 500 are indistinguishable — cost is dominated by fixed overhead, not enemy count, which is
the whole point of the architecture. Draw calls are 9 either way (ground, 5 obstacles, player sprite,
blob shadow, and the entire horde as one). Firing weapons adds one draw call and nothing measurable.
Zero collections across all three generations during sampling.

**These are desktop numbers, not the mobile budget.** The architecture reserves the knobs — lower
`ActiveRadius`, longer `FieldRebuildInterval`, wider far-stride — none of which change structure.

## Balance

`test/AutoPlay.cs` found that the first version gave the player **no reason to stay**: loitering 180 s
banked exactly what leaving immediately banked (266 either way), because all value sat in crates and
the route's crates were emptied in 11.4 s. The enrage curve could never be reached by optimal play.
Three changes followed: the time-scaled extraction multiplier, the run clock cut 600 → 300 s (the bot
died at 246 s, so 600 was fiction), and a HUD line showing what extracting *right now* pays.

| Loiter | Extract at | Banked | Low HP | Peak enemies |
| ---: | ---: | ---: | ---: | ---: |
| 0 s | 18.8 s | 299 | 100 | 26 |
| 60 s | 70.0 s | 390 | 100 | 41 |
| 120 s | 129.8 s | 496 | 94 | 69 |
| 180 s | died at 187 s | 36 | — | 205 |

**A bot's numbers, not a person's.** It circles at a fixed radius, never using obstacles or backing
off. A human should last longer, so the 300 s clock is still unvalidated at human skill.

## Assets

Sprites were generated with the Codex CLI's built-in image tool (no per-image cost, no API key),
matted with rembg, then cropped. 3D models were never needed — everything the player sees is either a
billboard sprite or procedural geometry, so no GLB is imported and no paid 3D generation was used.

| File | Source | Pixels | In-game size |
| :--- | :--- | :--- | :--- |
| `assets/sprites/player.png` | generated → matted → cropped | 606×1308 | 2.2 m tall |
| `assets/sprites/zombie.png` | generated → matted → cropped | 575×1051 | 2.0 m tall |
| `assets/sprites/bolt.png` | generated | 160×40 | projectile quad |
| `assets/sprites/blob_shadow.png` | generated | 128×128 | ground decal |
| `assets/shaders/horde_billboard.gdshader` | hand-written | — | horde + projectiles |

`art-src/` holds what the pipeline consumed and produced on the way — `*_ref.png` (the generated
originals, background intact) and `*_qa.png` (matted, pre-crop, for checking the cut). Nothing there
is loaded at runtime, which is why it is outside `assets/`: that directory holds only files the
running game loads.

Never prompt for a transparent background — generators draw a checkerboard. Prompt a flat colour
that contrasts with the subject but sits near the scene's palette so residual fringing blends, then
matte it. Only one facing is generated; the other is a horizontal flip at runtime.

## What's left

- **Mobile is unmeasured.** 150-200 concurrent enemies is an estimate, not a measurement. If a real
  device falls short, cut the distance-tiering thresholds before cutting enemy count.
- **The 300 s clock is unvalidated by a human.** See Balance.
- **`physics_ticks_per_second` is not pinned in `project.godot`** — 60 is the default and Godot strips
  it. Behaviour is correct today, but moving to 30 Hz means re-checking every damping constant.
- **No audio and no export presets.** Neither is wired up; the game runs from the editor and from
  headless capture only.
- Third-party CC0 sources (Kenney, Quaternius) are safe to use directly; aggregators like Poly Pizza
  license per item and would need checking per file. Nothing from either is currently in the repo.
