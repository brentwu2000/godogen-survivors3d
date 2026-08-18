# Survivors 3D

A 2.5D extraction horde-survivor in Godot 4.7.1 (C# / .NET 9). Orthographic Brawl-Stars framing,
Vampire-Survivors crowd density, Tarkov's loot-fight-extract stakes: what you carry out is banked,
what you die holding is gone.

Status: both loops close, the surface is on, the run reports itself, the arena is a place, and things
happen when you shoot. A run is fight, loot, grow,
extract on an arena generated fresh each time; it ends on a debrief rather than a timer; between runs
a base screen turns what came back into gear that changes the next one, offers three contracts and
keeps your records, and dying in that gear loses it. Five enemy variants with their own art, finite
ammo, items you can use or throw, synthesised audio, a HUD of bars rather than labels, and hit
feedback. Every probe below passes. Build gate re-verified 2026-08-18.

## Running it

```bash
dotnet build                         # 0 warnings, 0 errors
godot --headless --import            # import the assets
godot --headless --quit              # scene loads clean
godot                                # play — opens at the base
```

WASD/arrows move, weapons fire themselves (mouse or space forces a shot), `[E]` interacts, `[R]`
reloads, `[F]` secures the top item into the safe box, `[Q]` uses a carried item, `[G]` throws one,
`[Tab]` swaps weapons, and `[1]`/`[2]`/`[3]` answer a level-up. At the base, `[1]`/`[2]`/`[3]` take a
contract and `[R]` rerolls the board for credits. Actions are read through `IInputSource`, so the
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
| `test/AudioProbe.cs` | yes | Every clip exists and carries signal, one-shots end on zero, the horde loop meets itself, and repeats are gated |
| `test/HudProbe.cs` | yes | Every bar tracks its value at three widths, the cards follow the offer, and the hold bar clears when the run does |
| `test/DebriefProbe.cs` | yes | The record is what happened — kills by variant, crates, items, the worst moment — and the screen reports the record |
| `test/ContractProbe.cs` | yes | Three distinct jobs with at most one clock card, exact thresholds, nothing paid on a corpse, and rerolls cost |
| `test/AutoPlay.cs` | yes | A whole run driven through the real input layer at real speed — the only balance signal |
| `test/BalanceSweep.cs` | yes | Twenty runs across four linger tiers and five layouts; fails if nothing reaches 180 s |
| `test/HordePerf.cs` | no | Frame time, physics time, draw calls under load (`-- 500`) |
| `test/ScaleProbe.cs` | no | Sprite world-height read against a 2 m reference pole |
| `test/BillboardCompare.cs` | no | The side-by-side that settled full-billboard vs Y-locked |
| `test/Screenshot.cs` | no | Still of the main scene (`-- 0 0 mixed flash` checks the hit-flash channel; `fx` drives kills and a detonation just before the shutter) |
| `test/DebriefShot.cs` | no | Still of the end-of-run report, staged from a compressed run |
| `test/Presentation.cs` | no | The proof video (see Capture) |

Probes are exit-code judged, so they can all be chained. The ones marked "no" need a real rendering
driver — the null driver has nothing to capture and no draw calls to count.

Scenes are not hand-written. `scenes/Build*.cs` emit `.tscn` at build time
(`godot --headless --script scenes/BuildMain.cs`), and
`scripts/tools/Build{InputMap,Weapons,Items,Gear,EnemyTypes,EnemySprites,PlayerSprite,Audio}.cs` emit
the input map, the `.tres` data, the fitted sprites and every sound the same way. Nothing under
`assets/` or `resources/` is edited by hand; all of it is the output of a script that can be re-run.

### Capture

```bash
godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 800 \
      --script test/Presentation.cs
ffmpeg -y -framerate 30 -i screenshots/result/frame%08d.png -i screenshots/result/frame.wav \
      -c:v libx264 -pix_fmt yuv420p -crf 20 -c:a aac -b:a 128k -shortest \
      -movflags +faststart screenshots/result/survivors3d.mp4
```

The movie writer emits `frame.wav` beside the frames, so the clip can carry the game's own audio; mux
it in rather than shipping a silent film of a phase that was mostly about sound. That wav is **32-bit
integer PCM** — read as 16-bit it looks like a wall of full-scale clipping, which is entirely an
artefact of the wrong sample width and cost one wrong tuning pass to work out.

`--quit-after N` writes exactly N frames, so that number *is* the edit. The film ends on the debrief
rather than on the banner — the capture sets `GameSession.LaunchedFromBase`, and since the report waits
for a key nobody presses, the last seconds are what the run was worth. Capture 700 and encode 640 (21.3
s): the run finishes around frame 525, which leaves the report on screen long enough to read and short
enough not to be dead air. `screenshots/` is not versioned; re-run the above to regenerate it.

`Presentation.cs` injects compressed values into `_Initialize` — 44 enemies at open, a 40 s run,
extraction open from t=0, spawn 6→8.5/s, and an opening crowd drawn from an intensity just under the
brute's unlock so the brutes *arrive* — and does not touch `RunDirector`'s own defaults. Shot at
shipping numbers the first minute is an empty field: correct by design, and nothing to film.

It walked in straight lines until Phase 14 put real cover on the map, and then spent every take pressed
against a container while the horde ate it — the run died at fifteen seconds having banked the same 98
each time. `AutoPlay` learned this in Phase 10 and got a flow field; the capture script had not, because
at the time the arena was five grey boxes and a straight line was fine. Routing around cover then made
the two-crate route too long to finish inside the clip, so the film visits one crate.

Three of those numbers were found by watching the result rather than by reasoning about it. A 110 s
run only reaches intensity 0.2 inside the clip, so the brute, bloater and spitter never appeared —
the three variants the art was drawn for. The bot never pressed a level-up key, so three cards sat
over the lower third for the whole film, which reads as a stuck interface rather than as a choice
nobody made. And seeding the opening crowd at intensity 0.65 killed the bot 8 frames after the old
cut, meaning the take that looked like it was about to extract was in fact about to end in a death.
All three are the same class of defect as the stale hold bar in Phase 6: invisible to every
exit-code probe, obvious in one frame of video.

The camera's `Position` and `RotationDegrees` are set in `BuildMain.BuildCameraRig`, because the first
movie frame renders before `_Process` and `CameraRig`'s lerp has not run yet.

## The loop

A run is 300 s. The horde spawns at 2/s and ramps to 8/s while its speed scales to 1.6x, **up to 160
alive at once**. The extraction pad opens at 15% of the clock and needs a 5 s hold, cancelled by
stepping out.

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

The right-hand column is what to chase and what to take: personal bests, and
three contracts of which one can be taken with `[1]`/`[2]`/`[3]`. `[R]` puts a new
board up for 60 credits — a free reroll means spinning until the easiest card
appears, and a job nobody had to weigh is a delayed handout.

**Records are not a fourth growth curve.** There are already three — practice,
gear, in-run upgrades — and a fourth would make it impossible to tell which one
is moving, which is the exact problem that turned practice into a once-per-run
settlement. A record changes no number in the next run. It is only a target, and
a target is what was missing. The exception with teeth is the survival streak: a
single death takes it back to zero, which stacks another layer onto "do I take
the good rifle out" without inventing a mechanic to do it.

**Contracts are the only thing in the game that asks you to play differently.**
Everything else — better gear, more practice, a longer curve — asks you to play
the same run better. A board is three distinct kinds, and **at most one of them
pays for leaving early**: "multiply what you are carrying by staying" is the run's
central tension, and a board that is entirely "leave before 90s" replaces that
decision with a schedule. One such card is a trade; three are an instruction.
Every job also requires walking out, because the counts are easiest to hit on
exactly the run that ends face down — it went on longest.

## The debrief

A run ends on a report, not on a timer. It used to end on a two-line banner and
three and a half seconds of waiting, which was long enough to not finish reading
it; everything else the run produced — kills by variant, practice earned, what
death took, whether it beat the last one — went to the console. The player did the
work and the log file got the report.

It is composed from one `RunRecord`, frozen the moment the run ends, and so is the
contract check and so are the records. Three consumers, one set of numbers: a
contract that counted kills its own way would disagree with the screen reporting
them, and the player would be right to trust neither. `test/DebriefProbe.cs`
asserts that agreement by reading the label text, because that is the version a
player can check too.

It waits for a key. Anything that dismisses itself is something the player learns
to stop reading.

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

**Hit flash rides the MultiMesh colour block, not a fifth `INSTANCE_CUSTOM` float.** All four were
already spoken for — flip, bob phase, in-plane spin, array layer — and bit-packing a fifth value into
one of them is a decoding bug waiting for whoever next changes the flip flag. The colour block costs
four floats per instance (a 16-float stride becomes 20) and is the channel the engine provides for
exactly this. One catch, which bit: a MultiMesh with `use_colors` **off** still hands the vertex stage
an opaque white `COLOR`, so the same shader drew every projectile at full flash, permanently white. A
`flash_enabled` uniform turns the channel off explicitly. The bug appeared only on the renderer that
opted out, while the horde it was written for was correct throughout.

**The scene had no `WorldEnvironment` at all, and that was most of "it looks plain".** Without one Godot
does no tone mapping and lights everything from a flat default ambient: every surface is lit by exactly
one number, highlights clip to white instead of rolling off, and no colour anywhere comes from anything
but the artist. The arena can be textured, the cover can be modelled and the sprites can be good, and it
still reads as a viewport. A warm sun against a cool ambient, filmic tone mapping with the white point
above 1, and a little contrast is the whole fix. The renderer is `mobile`, so SSAO and SSIL were never
options — this is the half that is free.

**Effect sizes were found by overshooting in both directions and measuring.** Additive blending
saturates, so the first pass — six-metre puffs near full alpha — was not a bright explosion but a flat
orange disc over a quarter of the screen, which reads as a rendering fault. The correction went too far:
counting bright pixels across the captured run found about sixty per frame out of two million, an effect
system that technically runs. The only meaningful ruler is the character — 2.2 m of player is about
130 px, so a metre is roughly sixty pixels and anything under half a metre is a speck.

**Damage over time must not raise the hit flash.** The flash confirms that a discrete shot landed, and
burning ground applies damage sixty times a second — so it re-lit every enemy standing in it every tick,
and a crowd caught in a molotov rendered as a row of solid white cut-outs until the fire went out.
`Horde.Damage` takes a `flash` flag now; the hazard path passes false, because what tells the player they
are burning is the fire drawn over them. The flash itself is also capped at 0.72 rather than 1.0: mixing
all the way to white erases the drawing, and a hit enemy that is a blank silhouette reads as a missing
texture — worst exactly when a blast lights a dozen at once.

**Cover is procedural, and that is a constraint rather than a preference.** Fifty to seventy pieces of
cover have to stay inside a draw-call budget that has been near twenty since Phase 2, which means
MultiMesh — and MultiMesh loses an *imported* mesh on pack/save. Boxes are the combination that is
allowed. Grouped by kind, the cost stops depending on how many the seed placed. The measured price was
19 → 36 draw calls, against a written target of 30 that turned out to be based on the wrong model:
every MultiMesh costs one call in the main pass *and one per shadow split*, so seven kinds is not +7.
Landmarks were switched to not cast (their shadows fall across play space they are not in, which reads
as a rendering fault) and the number is what it is. 500 enemies still run at 1.72 ms median with zero
GC.

**Vertex colours are linear; texture pixels are sRGB.** Writing 0.46 in both places gives two visibly
different greys — the props came out at roughly the square root of their intended value and read as
polystyrene next to an asphalt floor that was correct. The first attempt at a fix was to darken the
palette by eye, which was the wrong direction; sampling a rendered pixel showed 0.30 arriving as 0.57,
which is the sRGB curve and nothing else. `MeshBuilder` converts once, on the way in.

**Face winding is fixed, never worked around.** Wound the wrong way the boxes still draw — as their own
interiors, lit from behind, which looks like every prop being made of black plastic rather than like a
culling bug. Turning on `CullMode.Disabled` would have hidden it and removed the shadows with it, so
the "safety net" is the thing that breaks the lighting.

**The layout has to be readable from the floor.** The generator picks one of four tile kinds per grid
cell and, until Phase 14, all of them looked identical from the ground — a decision the player could
not perceive, which is the same as not making one. The ground shader tints by a 5×5 texture written
per run, filtered, so zones blend rather than showing painted borders. One draw call; a decal quad per
cell would have been twenty-five more.

**One audio bus, fourteen voices, one ambience layer.** The rule that shaped the renderer shapes the
mix: the cost of a crowd must not scale with the crowd. Kills are gated to one death sound per 70 ms
and hits to one per 50 ms, because a wide melee arc lands five in a frame and the late horde dies in
double figures a second — ungated that is one loud smear that says nothing about how many, and gated
it still reads as "lots" while staying a sound. The horde itself is a single looping layer mixed by
how many enemies are within 26 m, not N copies of one voice; a crowd does not sound like N of
anything, it sounds like a low mass that swells.

**Sound is synthesised, not sourced.** Same reason the scenes are generated: a recipe in code can be
re-tuned and re-run, and it carries no licence to track. `BuildAudio.cs` writes `AudioStreamWav`
resources as `.tres` rather than `.wav` — a `.wav` goes through the importer, whose loop flag lives
in a generated `.import` file the tool does not own, and the ambience is ruined if that silently
comes back disabled. Rebuilds are byte-identical, so a diff means someone changed a recipe.

**The damage accumulator has exactly one owner.** Contact damage arrives as a per-tick slice of a
rate, so "was I hit" is not a question the player character can answer — only "how much, lately".
`Player.ConsumeDamageTaken()` clears on read, which makes a second consumer a bug that presents as
feedback that sometimes works. The HUD and the camera both watch `Health` instead; the accumulator
belongs to `SoundDirector`.

**Design height and sprite scale are separate fields.** The horde's frame is 176×256, sized for the
narrow variants, so the brute and bloater fit by width and do not fill it vertically — their
`SpriteScale` has to cancel the empty space above their heads as well as set their size. That makes
the scale a number nobody can read as "how big is a brute", so `EnemyTypeResource.DesignHeightMeters`
records the intent and `EnemyTypeProbe` measures quad × scale × the sprite's actual fill against it.
Re-fitting the art moves the scale, and without something to compare it to a 3 m brute quietly
becoming 2.4 m looks exactly like a brute.

**A stale enemy index is normal, not a caller error.** One hit can remove several enemies — a bloater's
death blast takes whatever is standing near it — so every index captured before it can be past the end
by the time it is used, including the rest of a melee swing's own hit list walked backwards exactly as
the contract says. Unguarded, the damage lands on a dead slot whose leftover health may already be at
or below zero, the pool despawns an entry that was never live, and `Count` drops without anything
leaving. A few of those drive it negative, and then the next spawn writes to index -1 — a crash several
seconds and one system away from the blast that caused it. `Horde.Damage` and `EnemyPool.DespawnAt`
both refuse out-of-range indices now. The hitscan path had always guarded; the melee path never had.

**The field is capped at 160 concurrent enemies.** Nothing had ever enforced a ceiling — the director
added spawns and the field grew until somebody died — and a twenty-run sweep found the wall between one
and two minutes: every layout survived a sixty-second linger at near-full health with a peak around a
hundred, and nothing at all survived a hundred and eighty, with peaks of three and four hundred. A
three-hundred-second deadline nobody has seen the second half of is the same as no deadline. A ceiling
rather than a slower rate, for the reason Phase 8 cut the end rate from twelve to eight: what the player
reads is density, and density saturates. And 160 is not a number picked to make the test pass — the
mobile budget has been 150-200 since before any code existed, so the design number and the performance
number are now the same number.

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
| `assets/sprites/player.png` | `art-src/survivor.png` via `BuildPlayerSprite.cs` | 339×512 | 2.2 m tall |
| `assets/sprites/enemies/walker.png` | `art-src/walker.png` via `BuildEnemySprites.cs` | 176×256 | 2.0 m tall |
| `assets/sprites/enemies/runner.png` | `art-src/runner.png` | 176×256 | 1.8 m tall |
| `assets/sprites/enemies/brute.png` | `art-src/brute.png` | 176×256 | 3.0 m tall |
| `assets/sprites/enemies/bloater.png` | `art-src/bloater.png` | 176×256 | 2.4 m tall |
| `assets/sprites/enemies/spitter.png` | `art-src/spitter.png` | 176×256 | 2.0 m tall |
| `assets/sprites/bolt.png` | generated | 160×40 | projectile quad |
| `assets/sprites/blob_shadow.png` | generated | 128×128 | ground decal |
| `assets/shaders/horde_billboard.gdshader` | hand-written | — | horde + projectiles |
| `assets/shaders/vignette.gdshader` | hand-written | — | full-screen damage tint |
| `assets/shaders/ground.gdshader` | hand-written | — | tiled floor, tinted per grid cell |
| `assets/shaders/effect.gdshader` | hand-written | — | additive billboard puffs |
| `assets/shaders/ground_marker.gdshader` | hand-written | — | burning ground and the extraction ring |
| `assets/textures/ground.png` | synthesised by `BuildGroundTexture.cs` | 512×512, seamless | 4.5 m tile |
| `assets/audio/*.tres` | synthesised by `BuildAudio.cs` | 22.05 kHz mono | 13 one-shots + 1 loop |

Cover is not an asset at all. `PropLibrary` builds seven props out of boxes at startup — containers,
barriers, rubble heaps, walls, dumpsters, and two landmarks — and `PropRenderer` draws each kind as
one MultiMesh. Nothing is imported, which is what makes that legal: MultiMesh silently loses an
imported mesh on pack/save, and cover that was boxes to begin with already carries its own collider
instead of needing a primitive measured off an AABB.

**Every layer of the horde array must be exactly 176×256.** That is the array format's rule, not a
preference, and it is a build-time failure rather than a visual one. `BuildEnemySprites.cs` enforces
it: drop a new matted painting into `art-src/` and re-run, and it crops to the visible pixels, fits
the frame, and sits the result on the bottom edge so the feet land on the ground.

The frame is sized for the narrow variants, so the wide ones (brute, bloater) fit by width and do not
fill it vertically. Their `SpriteScale` in `BuildEnemyTypes.cs` has to make up the shortfall, and
nothing keeps the two in step automatically — the tool prints the scale each sprite needs, and
`EnemyTypeProbe` measures the drawn height against `DesignHeightMeters` so a re-fit cannot quietly
turn a 3 m brute into a 2.4 m one.

Sound is generated, not sourced. `BuildAudio.cs` synthesises every clip from a recipe and saves
`AudioStreamWav` resources directly, so a rebuild is byte-identical and a diff means someone changed
a recipe. They are `.tres` rather than `.wav` because a `.wav` goes through the importer, whose loop
setting lives in a generated `.import` file the tool does not own — and the horde ambience is ruined
if that flag silently comes back disabled.

`art-src/` holds what the pipeline consumed and produced on the way — `*_ref.png` (the generated
originals, background intact) and `*_qa.png` (matted, pre-crop, for checking the cut). Nothing there
is loaded at runtime, which is why it is outside `assets/`: that directory holds only files the
running game loads.

Never prompt for a transparent background — generators draw a checkerboard. Prompt a flat colour
that contrasts with the subject but sits near the scene's palette so residual fringing blends, then
matte it. Only one facing is generated; the other is a horizontal flip at runtime.

## What's left

Everything the player looks at has now had a pass. What is left is **whether the numbers behind it still
work**.

The linger curve moved under Phase 14: on one pinned seed, leaving at 0 s banks 381, at 60 s banks 698,
and at 120 s the bot dies with 296 enemies on the field. Some of that is the layout changing because the
prop code consumes the RNG differently, and some of it is real — cover makes the horde pile up, which
Phase 10 first noticed and which the capture script ran into head-on when the same settings that filmed
a clean extraction started killing the bot at fifteen seconds. **A three-hundred-second deadline is only
a deadline if someone can reach it.**

That is Phase 15: measure the curve across several seeds before touching anything, then reach for a
field cap or stronger separation before touching the spawn rate — the pile-up looks like a pathing
problem wearing a difficulty problem's clothes. The base screen, still a monospace list next to a HUD
made of bars, comes along with it. See the roadmap.

Engineering gaps, separately:

- **Mobile is unmeasured.** 150-200 concurrent enemies is an estimate, not a measurement. If a real
  device falls short, cut the distance-tiering thresholds before cutting enemy count.
- **The 300 s clock is unvalidated by a human.** See Balance.
- **`physics_ticks_per_second` is not pinned in `project.godot`** — 60 is the default and Godot strips
  it. Behaviour is correct today, but moving to 30 Hz means re-checking every damping constant.
- **No export presets.** The game runs from the editor and from headless capture only.
- **The audio bus has no limiter.** The mix keeps its headroom by the master volume alone, set
  against a captured run; a louder moment than any capture happened to catch would clip rather than
  compress.
- Third-party CC0 sources (Kenney, Quaternius) are safe to use directly; aggregators like Poly Pizza
  license per item and would need checking per file. Nothing from either is currently in the repo.
