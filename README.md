# Survivors 3D

A 2.5D extraction horde-survivor in Godot 4.7.1 (C# / .NET 9). Orthographic Brawl-Stars framing,
Vampire-Survivors crowd density, Tarkov's loot-fight-extract stakes: what you carry out is banked,
what you die holding is gone.

Status: both loops close. A run is fight, loot, grow, extract on an arena generated fresh each time;
between runs a base screen turns what came back into gear that changes the next one, and dying in
that gear loses it. Five enemy variants, finite ammo, items you can use or throw. Every probe below
passes. Build gate re-verified 2026-08-17.

## Running it

```bash
dotnet build                         # 0 warnings, 0 errors
godot --headless --import            # import the assets
godot --headless --quit              # scene loads clean
godot                                # play — opens at the base
```

WASD/arrows move, weapons fire themselves (mouse or space forces a shot), `[E]` interacts, `[R]`
reloads, `[F]` secures the top item into the safe box, `[Q]` uses a carried item, `[G]` throws one,
`[Tab]` swaps weapons, and `[1]`/`[2]`/`[3]` answer a level-up. Actions are read through `IInputSource`, so the
touch implementation drives the same code with two virtual sticks.

The build gate is those first three commands. Every stage closes against it plus a probe below.

| Script | Headless | Asserts |
| :--- | :---: | :--- |
| `test/MovementProbe.cs` | yes | Synthetic input moves the player; the camera follows within its lag budget |
| `test/FlowFieldProbe.cs` | yes | An enemy behind the long wall routes around it instead of into it |
| `test/WeaponProbe.cs` | yes | Per-category mechanic: penetration, arc, travel time, and every proficiency curve |
| `test/RunLoopProbe.cs` | yes | Six stages: extraction closed at t=0 → loot → leave-resets → contact damage → enrage → bank |
| `test/EnemyTypeProbe.cs` | yes | Each variant moves, hurts, resists and dies by its own row; blast is one level deep; roster follows intensity |
| `test/GrowthProbe.cs` | yes | Start level, every curve stopping at the ceiling, armour's floor, and the deck emptying as caps fill |
| `test/ItemProbe.cs` | yes | Using something costs its sale value, nothing is wasted, a dry rifle stops and the sidearm does not, and throwing is its own verb |
| `test/LevelProbe.cs` | yes | A seed reproduces its arena, nothing is placed in a wall, the horde routes around what was generated, and 100 seeds produce no sealed exit |
| `test/ShopProbe.cs` | yes | A v1 save migrates, a newer one is refused, buying is all-or-nothing, and dying costs the kit but not the practice |
| `test/BaseLoopProbe.cs` | yes | Base → launch → die → back at the base, driven from the keys |
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
(`godot --headless --script scenes/BuildMain.cs`), and
`scripts/tools/Build{InputMap,Weapons,Items,Gear,EnemyTypes,EnemySprites}.cs` emit the input map, the
`.tres` data and the placeholder variant sprites the same way.

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
shipping numbers the first minute is an empty field: correct by design, and nothing to film.

It also still expects two crates on opposite corners, which a hand-placed arena promised and a
generated one does not. The seed is pinned so the shot is at least repeatable, but this has not been
re-cut since the map stopped being fixed — see the end of this file.

The camera's `Position` and `RotationDegrees` are set in `BuildMain.BuildCameraRig`, because the first
movie frame renders before `_Process` and `CameraRig`'s lerp has not run yet.

## The loop

A run is 300 s. The horde spawns at 2/s and ramps to 8/s while its speed scales to 1.6x. The
extraction pad opens at 15% of the clock and needs a 5 s hold, cancelled by stepping out.

The top of that ramp used to be 12/s. A maxed weapon clears roughly three a second against the late
roster, so the field is already growing without bound at six — every rate above it only changed how
fast the number climbed, and the whole second half of the escalation curve was escalation the player
could not read. Eight keeps the curve visible at four times the opening while leaving the last
stretch somewhere skill still moves the outcome.

**The arena is generated per run from one seed**, printed at startup so an interesting layout can be
walked again. A 5×5 grid of tiles — open ground, block clusters, walled corridors with a gap — around
a cleared spawn, eight crates, and three extraction pads of which two will open. Which two is decided
by the level and revealed by the director at 15% of the clock, so the way out is not known from the
first second and the map is a decision rather than a corridor.

Crates get better the further out they sit: rarity weight is multiplied once per rarity step, scaled
by distance from the spawn. Depth has to pay, or everything past the first ring is risk with no
reason.

Escalation is also a change of composition, not only of rate. Five variants share one table
(`resources/enemies/*.tres`), each gated behind a point on the run clock:

| Variant | HP | Speed | Contact | Scale | From | Exists because |
| :--- | ---: | ---: | ---: | ---: | ---: | :--- |
| walker | 10 | 2.4 | 6/s | 1.0 | 0% | the baseline everything else is read against |
| runner | 4 | 4.6 | 4/s | 0.9 | 20% | standing still stops being free |
| spitter | 8 | 2.0 | — | 1.0 | 30% | holds at 8 m and shoots, so kiting is the wrong answer |
| brute | 60 | 1.4 | 14/s | 1.5 | 45% | takes knockback at 0.2x, which makes knockback a choice |
| bloater | 25 | 1.8 | 6/s | 1.2 | 60% | 25 damage in 3 m on death — clearing a pile face-first costs something |

## Between runs

The game opens at the base, not in a run. It lists what came back, what the
stash is worth, what is on sale, and what practice you have — with "not for
sale" written next to it, because that is the one axis credits cannot reach.

Up and down move, enter buys or equips, `[S]` sells the stash at face value (the
extraction multiplier was earned by walking out with it and is not paid twice),
`[L]` launches. Buying and equipping share a key: a shop where they are separate
is a shop where the player buys something and walks out without it.

**Everything above starting kit is left behind if you die wearing it.** That is
what makes the shop a decision rather than a one-time unlock — buying the better
rifle is easy, taking it out is the wager. The starting rifle, knife, jacket,
pack and boots can never be lost or sold; a player who cannot afford a backpack
still has one, or the loop has no next run.

## Growth

Three axes, and they do not overlap. A run's weapon sits at one number:

```
level = clamp(start + run upgrades, 0, ceiling)
start = min(practice, ceiling / 2) + gear tier
```

| Axis | Earned by | Lives for | Moves |
| :--- | :--- | :--- | :--- |
| Practice | using the category | forever; a death cannot take it | the **starting point** |
| Gear | credits | until you die wearing it | the starting point **and the ceiling** |
| Run upgrades | kills, this run | **reset on extraction** | the climb between them |

Practice counts for at most half the ceiling, which is what guarantees a veteran still has a climb
left — the point at which in-run growth stops being worth offering is the point at which it stops
being a game. Practice above the half is not wasted, it is unspent: a weapon with a longer curve lets
more of the same practice count, which is most of what buying one gets you.

Kills buy levels and levels deal three cards. The weapon card is one option among character stats, and
**it stops being dealt once the weapon is at its ceiling** — the deck visibly runs out, which is how a
ceiling becomes something the player plans around instead of a number in a formula. Nothing pauses
while they choose: the cost of a decision is the seconds it takes while the horde keeps walking, the
same design as the search timer.

Armour subtracts a flat amount from an incoming rate or amount and never scales it, so it is the
answer to a crowd of walkers and never the answer to a brute. A fifth always gets through, because
armour that can reach zero turns the weakest variant into scenery.

The backpack holds **20 bulk, not 20 slots** — dumping bulky scrap to fit a small vial is the trade,
and a full bag still takes what fits rather than refusing the crate.

**Carried items are worth something before they are sold.** `[Q]` spends the cheapest thing that would
currently help — tinned food heals 15, a medkit 45, rifle rounds refill the reserve, an adrenaline
shot buys 8 seconds of +35% speed. Using one costs exactly its extraction value, so the backpack holds
health and money in the same slots and every heal is money not banked. Only if it would help: nothing
is spent at full health or into a full reserve. The two most valuable items are pure cargo and cannot
be used at any price, which is what makes carrying the serum a gamble rather than a stockpile.

**`[G]` throws.** A pipe bomb does 55 in 4.5 m where it lands; a molotov leaves a patch burning at
22/s for 7 seconds — a burst answers a crowd, a fire answers a doorway. It is a separate verb from
`[Q]` on purpose: one shared "spend something" key is how a player heals by blowing a hole in the
crowd they were running from. Throws land a fixed 8 m along the facing rather than at the nearest
cluster, because an item whose landing point cannot be predicted is one nobody spends.

Both hurt enemies only. The thrower chose the spot, and a patch they also have to avoid turns a
tactical item into a way to kill yourself while being pushed backwards — the bloater already owns
"your own kills can hurt you". Blast kills do not chain into bloaters either, for the same reason
theirs do not chain into each other.

**Firearms run out.** The rifle starts with 240 rounds behind its magazine, capped at 360, and reloads
draw from that reserve rather than conjuring one. Melee and the bow have no magazine and so can never
run dry — running out has to be a change of tactics, never a dead end. `[Tab]` swaps between two
slots, each keeping its own magazine, cooldown and levels; looted rounds go into whichever slot takes
a magazine whether or not it is in hand, because otherwise swapping to the knife when the rifle
empties turns every round in the bag into dead weight at exactly the moment they matter. Banking pays
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

**Variants are layers of a `Texture2DArray`, not cells of an atlas.** Under `filter_linear_mipmap` an
atlas bleeds neighbouring cells into each other once instances drop a mip level — a bug that appears
only at distance, which is the hardest kind to catch standing still. Array layers have no neighbour
to bleed from. The cost is that every layer must be identical in size, and that failure happens at
load with a message rather than on screen. The layer index rides the one `INSTANCE_CUSTOM` float that
was still free, so five variants are still one draw call and one 16-float instance stride.

**Enemy sprites are 256 px tall, not 1051.** A 2 m sprite in an 18 m orthographic view covers about
120 px at 1080p, so the original art was 8x oversampled — waste that an array multiplies by its layer
count. Downscaling took 500 enemies from 5.67 ms to 1.09 ms median on the same machine, measured
against the previous commit back to back.

**Enemies are not physics bodies.** The game asks one question about an enemy — who is near me — and
a uniform `SpatialGrid` answers it with an O(n) counting sort per tick, so separation is a single
pass instead of a constraint solve. Within 15 m enemies separate and update every tick; beyond it
they follow the field on a 4-tick stride spread by index, so no tick carries the whole far set.

**Flow field, not per-agent navigation.** A BFS toward the player every 8 ticks; everyone samples one
field. The distance pass is 4-directional (8 cuts corners through obstacles) and the gradient is read
8-directionally for smooth headings. Obstacle footprints are dilated by the enemy radius, or the
field steers bodies into gaps they don't fit through.

**The level generates before the horde, and the reachability check is the horde's own.** The flow
field bakes obstacles once at startup, so a level built after that bake produces walls every enemy
walks straight through while the screen looks perfectly correct. Ordering handles that; what does not
is writing a second reachability test next to the generator. One was written, and it agreed with
itself and disagreed with the game — the field blocks a cell with `floor()` on one edge and `ceil()`
on the other, so a copy that floors both is a shade more optimistic than the thing it stands in for.
The generator now builds a real `FlowField` and asks it.

**A carve that has never been needed is a guess.** When a layout does seal an objective off, every
block on the line from spawn is removed. At shipping density that never happens — 60 seeds, zero
carves — so the probe also sweeps 40 seeds at more than triple the block count, where 37 of them need
it. The first version of that rescue passed its own check and still left six pads sealed: a corridor
cleared to the width a body needs arrives at the field narrowed twice, once by the enemy-radius
inflation and again by rounding the footprint outward to whole cells.

**A death blast resolves one level deep.** A bloater's blast kills other bloaters without those
blasting in turn. A chain whose depth is however many happened to be standing together is both a
frame spike and a balance number nobody chose.

**The far-update stride comes from the variant, not from a constant.** Distant enemies run at a
reduced rate with a proportionally longer catch-up step, which is invisible at 2.4 m/s and very
visible at 4.6 — a runner on a 4-tick stride teleports. Fast variants carry a shorter stride, and it
stays a power of two because the scheduler spreads work with a bit mask.

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

**Practice is banked once at the end of a run, not levelled as it lands.** It used to rise a point at
a time as hits connected, which put two growth curves on screen at once — indistinguishable to the
player and impossible to balance separately. It was also unbounded: every enemy caught by a swing
counted, so the widest melee arc learned fastest and had the most to gain from learning, and a single
long axe run banked more levels than a dozen careful ones. A run now teaches at most three points,
and what they buy is a starting point, capped at half the weapon's ceiling.

**Menus poll input; they do not listen for events.** `Input.ActionPress` moves the
poll state and never enters the event pipeline, so a screen built on
`_UnhandledInput` is one no script can press a key on — which is how the base
screen shipped its first version untestable, and how the loop probe found it.

**Capture and play-test tools never touch the save.** They run a scene, and a scene that ends a run
banks it — so taking a screenshot was spending credits and practice into the real profile. Every tool
that instantiates the game for measurement now marks the meta layer ephemeral.

**The save version moved for the first time, and older files are migrated rather
than refused.** Adding an optional key never needed it — every field is read with
a fallback, so v1 files kept loading when the sidearm slot appeared. Owned
equipment did: a v1 file has no record of what was bought, and the safe reading
of that is "the starting kit and nothing else", never "nothing", which would take
away the shirt on their back. A *newer* file is still refused outright, because
reading one with older rules is how a save gets quietly rewritten with half its
contents gone.

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

| 500 enemies | Mean | Median | p95 | Draw calls | GC |
| :--- | ---: | ---: | ---: | ---: | ---: |
| walkers only | 1.94 ms | 1.09 ms | 2.11 ms | 19 | 0 |
| mixed roster | 2.04 ms | 2.18 ms | 3.32 ms | 19 | 0 |
| previous commit, walkers only | 4.91 ms | 5.67 ms | 8.24 ms | 19 | 0 |

**A mixed horde costs what a uniform one does**, which is the claim the variant system had to earn:
same draw call count, same order of frame time. Five sprites, one array, one call. Zero collections
across all three generations in every sample.

The third row is the previous commit measured back to back on the same machine in the same session,
because a number recorded weeks ago is not a baseline. Two things follow from it. The sprite
downscale is worth roughly 5x at 500 enemies — that is the whole difference between those rows.
And the draw call count is 19 for old and new code alike: the 9 recorded during Phase 2 is not
reproducible in the current environment, so it was environmental, not something this change spent.

**These are desktop numbers, not the mobile budget.** The architecture reserves the knobs — lower
`ActiveRadius`, longer `FieldRebuildInterval`, wider far-stride — none of which change structure.

## Balance

`test/AutoPlay.cs` found that the first version gave the player **no reason to stay**: loitering 180 s
banked exactly what leaving immediately banked (266 either way), because all value sat in crates and
the route's crates were emptied in 11.4 s. The enrage curve could never be reached by optimal play.
Three changes followed: the time-scaled extraction multiplier, the run clock cut 600 → 300 s (the bot
died at 246 s, so 600 was fiction), and a HUD line showing what extracting *right now* pays.

One seed, one route, four lengths of stay:

| Loiter | Extract at | Banked | Low HP | Enemies | Ammo |
| ---: | ---: | ---: | ---: | ---: | :--- |
| 0 s | 35.2 s | 331 | 100 | 49 | never below 150 |
| 60 s | 69.3 s | **827** | 100 | 84 | reserve hit 0, refilled by looting |
| 120 s | 133.4 s | 359 | 33 | 206 | ran dry at 93 s, finished on the knife |
| 180 s | died at 179 s | — | — | 379 | ran dry at 93 s |

**The curve has a peak now instead of a slope.** Staying to 60 s more than doubles the haul, because
the far crates are where the rarity bias puts the serum; staying to 120 s banks less than half of
that, because by then the bag is being spent on staying alive. That is the item system and the depth
bias arguing with each other, which is the argument they were built to have.

**The reserve is calibrated so ammo runs out if and only if you stop looting.** A bot that opens two
crates and then circles is dry at 95 s and ends with a knife against a hundred enemies; the same bot
searching as it goes never empties. Looting is a supply line, not a phase that ends in the first
minute.

**Cover makes the horde accumulate.** 206 enemies alive at 120 s where the old open arena held 55: a
crowd that has to route around fifty blocks arrives slower than it spawns. The field the player is
kiting through is denser than the same run used to be, and the reason is the map, not the rate.

The bot now paths with a flow field of its own rather than steering straight. On a hand-made arena
with five blocks that was enough; on a generated one it walked into the first wall between it and the
crate and reported the route as blocked. A player looks at the screen and goes around, and the
closest thing to that this project already owns is the field.

**These are measured on a fresh profile, and the earlier ones were not.** The table recorded when
variants landed was taken against a save with 37 points of firearm practice on it, which under the
old uncapped system meant a rifle already past every floor it had — a maxed weapon, by accident. The
play-test had been writing to the real profile for weeks of runs, so it was quietly measuring a
veteran and reporting it as a baseline. It is ephemeral now: a play-test does not spend the player's
save, and a balance number measured against whatever practice happens to be on disk is not a balance
number, because practice moves the starting point. The rows above start from nothing.

**A bot's numbers, not a person's.** It circles at a fixed radius, never using obstacles or backing
off, and it is the worst possible case for a spitter — it never breaks line of sight, so every shot
from the one variant built to punish standing in the open lands for free. It does now take survival
upgrades when it drops below 60% health, because a bot that always takes damage measures a player who
never notices they are dying. A human should still last longer, so the 300 s clock remains
unvalidated at human skill.

## Assets

Sprites were generated with the Codex CLI's built-in image tool (no per-image cost, no API key),
matted with rembg, then cropped. 3D models were never needed — everything the player sees is either a
billboard sprite or procedural geometry, so no GLB is imported and no paid 3D generation was used.

| File | Source | Pixels | In-game size |
| :--- | :--- | :--- | :--- |
| `assets/sprites/player.png` | generated → matted → cropped | 606×1308 | 2.2 m tall |
| `assets/sprites/enemies/walker.png` | **placeholder** — downscaled from `art-src/zombie.png` | 140×256 | 2.0 m tall |
| `assets/sprites/enemies/runner.png` | **placeholder** — tinted copy | 140×256 | 1.8 m tall |
| `assets/sprites/enemies/brute.png` | **placeholder** — tinted copy | 140×256 | 3.0 m tall |
| `assets/sprites/enemies/bloater.png` | **placeholder** — tinted copy | 140×256 | 2.4 m tall |
| `assets/sprites/enemies/spitter.png` | **placeholder** — tinted copy | 140×256 | 2.0 m tall |
| `assets/sprites/bolt.png` | generated | 160×40 | projectile quad |
| `assets/sprites/blob_shadow.png` | generated | 128×128 | ground decal |
| `assets/shaders/horde_billboard.gdshader` | hand-written | — | horde + projectiles |

**Four of those are placeholders and want replacing.** `scripts/tools/BuildEnemySprites.cs` writes
them as tinted, downscaled copies of the one real zombie so the variant system could be built and
seen before the art existed. Drop a generated sprite in over any of them and nothing in code changes,
subject to one rule the array enforces: **every layer must be exactly 140×256**. Generate at whatever
size, then unify — the same step the pipeline already has for mixed sources.

`art-src/` holds what the pipeline consumed and produced on the way — `*_ref.png` (the generated
originals, background intact) and `*_qa.png` (matted, pre-crop, for checking the cut). Nothing there
is loaded at runtime, which is why it is outside `assets/`: that directory holds only files the
running game loads.

Never prompt for a transparent background — generators draw a checkerboard. Prompt a flat colour
that contrasts with the subject but sits near the scene's palette so residual fringing blends, then
matte it. Only one facing is generated; the other is a horizontal flip at runtime.

## What's left

The loop closes now: a run pays for gear, the gear changes the next run, and dying takes it back.
What is left is the surface. There is no audio at all, the HUD is three bare labels and a wall of
text, the base screen is a monospace list, the cover is untextured boxes, and four of the five enemy
sprites are tinted placeholders. None of that is a system — it is the part a player would notice
first and the part that has been deferred longest.

Also open: mobile is still unmeasured, the 300 s clock is unvalidated at human skill, and
`physics_ticks_per_second` is not pinned in `project.godot`.

**The proof video is stale.** `Presentation.cs` still frames two crates it expects on opposite
corners, which a generated map does not promise, and it has not been re-shot since the arena stopped
being hand-placed — or since anything in the last four phases landed. It runs on a pinned seed, so it
is at least reproducible.

Engineering gaps, separately:

- **Mobile is unmeasured.** 150-200 concurrent enemies is an estimate, not a measurement. If a real
  device falls short, cut the distance-tiering thresholds before cutting enemy count.
- **The 300 s clock is unvalidated by a human.** See Balance.
- **`physics_ticks_per_second` is not pinned in `project.godot`** — 60 is the default and Godot strips
  it. Behaviour is correct today, but moving to 30 Hz means re-checking every damping constant.
- **No audio and no export presets.** Neither is wired up; the game runs from the editor and from
  headless capture only.
- Third-party CC0 sources (Kenney, Quaternius) are safe to use directly; aggregators like Poly Pizza
  license per item and would need checking per file. Nothing from either is currently in the repo.
