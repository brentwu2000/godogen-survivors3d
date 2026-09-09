# Survivors 3D

An extraction horde-survivor in Godot 4.7.1 (C# / .NET 9). A mouse-driven third-person camera,
Vampire-Survivors crowd density, Tarkov's loot-fight-extract stakes: what you carry out is banked,
what you die holding is gone.

Status: both loops close, the surface is on, the run reports itself, the arena is a place, and things
happen when you shoot. A run is fight, loot, grow, extract on an arena generated fresh each time; it
ends on a debrief rather than a timer; between runs a walkable shelter turns what came back into gear
that changes the next one, offers three contracts and keeps your records, and dying in that gear loses
it.

**Nine enemy variants** as solid low-poly bodies rigged in the vertex stage, across **five places**
that ask different questions of a build, from a rail yard to a laboratory interior with its own light.
**Five survivors**, chosen on a roster screen before the loadout. **Fifteen weapons**, none of them a strictly better
version of another. Threat is a *place* — danger zones you choose to enter — rather than a spawn rate,
and a map leans toward one kind of them rather than holding one of each. The growth deck has five
lines and both the shop and the run tilt it. Finite ammo, items you can use or throw, synthesised
audio and music that follows the run's shape, a HUD of bars rather than labels, a minimap that records
where you have been rather than revealing the map, and a fight that leaves bodies and evidence on
the floor.

**The look is cel-shaded stylised**, and `ART.md` is the brief anyone sourcing a model works to. Most
of that file is a list of things this renderer cannot do — a `MultiMesh` has no skeleton, so animations,
blend shapes and per-instance mesh variants are all worth nothing here — because that is what decides
which of two good-looking models is usable and none of it is guessable from a screenshot.

**Five survivors, all authored models** — RIN, MIKA, AKIRA, SORA and YUNA, from one production, chosen
on a roster screen with the illustration each was designed from. The horde is still built by
`MeshBuilder`. Which of the fourteen bodies are which is decided by what is in `resources/bodies/`: a
bake named after its slot *is* that slot's body, so applying a model is a file landing there and
undoing it is deleting the file. `art-src/models/intake.ps1` takes a `.glb` to that file in one
command, and `ART.md` is the brief for choosing one.

The measured cost of doing it is in Performance and it is close to nothing: 200 bodies at 1,650
triangles run marginally faster than 200 at 460, and 200 at 23,822 still hold 293 fps.

The billboard sprite path is still there and still works, behind `Horde.SolidBodies` — it is the
fallback for hardware that cannot afford a hundred and fifty meshes, and `ShadowProbe` builds the
scene with it so it cannot quietly rot.

Sweep clean at 48 probes; the table below lists 34 of them and is the older set. Two of those 48
had been failing on their own bookkeeping rather than on anything they measure — `PaletteProbe`
printed "palette ok" where `sweep.ps1` looks for "PROBE OK", and `RouteMemory` is a helper class
the sweep was trying to run as a probe. Build gate re-verified 2026-09-08.

## Running it

```bash
dotnet build                         # 0 warnings, 0 errors
godot --headless --import            # import the assets
godot --headless --quit              # scene loads clean
godot                                # play — opens at the base
```

**The mouse is the camera and WASD is the movement, which is the ordinary third-person scheme.** The
cursor is captured when a run starts: moving the mouse turns the view and pitches it between 50° and
10° above the horizon, `[W]`/`[S]` walk along the view, `[A]`/`[D]` strafe across it, and `[Esc]`
gives the cursor back — a click inside the window takes it again. `[Z]`/`[X]` still turn the view for
anyone playing with the pointer free, and so does dragging with the right button.

Weapons fire themselves and reload themselves, `[F]` secures the top item into the safe box, `[Q]`
uses a carried item, `[G]` throws one, `[Tab]` swaps weapons, and `[1]`/`[2]`/`[3]` answer a level-up
— or click the card. At the base, `[1]`/`[2]`/`[3]` take a contract, `[R]` rerolls the board, and
`[C]` at the gate opens the roster.

**On a touchscreen the layout is one stick and four buttons.** The left half of the screen is a
floating move stick, whose origin is wherever the thumb lands; the bottom right is an arc of four
buttons — secure, use, throw, swap — that grey out when they would do nothing. Level-ups are answered
by tapping the card the offer is already drawing.

There is no aim stick. It was the original plan and it costs the whole second thumb, which is the
entire touch budget for everything that is not walking, and it buys very little: firing is automatic,
the weapon already picks the nearest target, and the survivors-like contract this is built on is that
the player steers and the weapon handles itself. Actions are read through `IInputSource`, so both
implementations drive exactly the same code. Run any script with `-- touch` to force the controls on
without a touchscreen.

The build gate is those first three commands. Every stage closes against it plus a probe below.

**`dotnet build` before running anything under `scripts/tools/`.** `--script` runs the compiled
assembly, not the `.cs` file on disk, so editing a builder and running it straight away re-emits the
*previous* values — over the same paths, printing the same `Saved res://…` lines, exit code 0. Nothing
says the output is stale. A balance measurement was taken against an untouched weapon this way and was
only caught because it came back bit-identical to the run before it. Read one field back out of the
generated file before trusting a re-generation:

```bash
dotnet build && godot --headless --script scripts/tools/BuildWeapons.cs
grep -E 'StartingReserve|TraitAmount' resources/weapons/sidearm_pistol.tres
```

| Script | Headless | Asserts |
| :--- | :---: | :--- |
| `test/MovementProbe.cs` | yes | `[W]` advances along the view; `[D]` translates along the view's right and does *not* turn it; the view keys turn without translating; `[W]` after a turn follows the new view; a driver closes on something 2 m away and 90° off; and the field's heading survives clipping the margin while a bot leaning on a wall still escapes |
| `test/FlowFieldProbe.cs` | yes | An enemy behind the long wall routes around it instead of into it |
| `test/WeaponProbe.cs` | yes | Per-category mechanic: penetration, arc, travel time, every proficiency curve, and that both shelves are stocked — the Sidearm one with four weapons, three distinct signatures and something that reaches 8 m |
| `test/RunLoopProbe.cs` | yes | Six stages: extraction closed at t=0 → loot → leave-resets → contact damage → enrage → bank |
| `test/KnotProbe.cs` | yes | Some runs knot and some do not, a knot lands as a mass rather than a spread, and a knot run receives no more bodies than a scattered one |
| `test/EnemyTypeProbe.cs` | yes | Each variant moves, hurts, resists and dies by its own row; blast is one level deep; roster follows intensity |
| `test/GrowthProbe.cs` | yes | Start level, every curve stopping at the ceiling, armour's floor, and the deck emptying as caps fill |
| `test/DeckMatrix.cs` | yes | Every weapon fired under every growth option: no weapon can spend less than half the deck its neighbours can. **Not in the sweep** — 276 trials, about a quarter of an hour. Run it when a weapon, an option or a trait changes |
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
| `test/TouchProbe.cs` | no | Synthetic fingers: the stick moves the player, a held button fires once, a dead button is dead, and the level-up card can be tapped |
| `test/ModifierProbe.cs` | yes | Every upgrade changes the run, and pierce, area, ignite, detonate, thorns and lifesteal do what their card says |
| `test/ImpactProbe.cs` | yes | Twelve stages of the feedback that is not a muzzle flash: a kill stains the floor downrange of the shove and on the ground, the floor clears itself and never overflows, both blend channels carry puffs, a crit emits more than the same shot without one, a burning body is on fire while an identical cold one is not, a ranged enemy charges before it fires and not before that, an explosion is a light source and stops being one, a hit puts a number over it that goes away, damage from a bearing lights that bearing and no other and four bodies standing on the player light the side they are on, a body falls away from the shot that killed it and the ground takes it back, the corpse field has a ceiling the living are never dropped for — and the clock is untouched headless and comes back when it is not |
| `test/TraitProbe.cs` | yes | Every weapon carries a signature, and bleed, cleave, ricochet, burst, chill and mark each do what only they do — the last two also that the status is *spent* rather than permanent |
| `test/SupplyProbe.cs` | yes | Caches land on the clock and once each, they are richer than anything the map placed, and a crate that arrives mid-run is counted when it is emptied |
| `test/FirstRunProbe.cs` | yes | A fresh profile has not seen the base, an older save has, and opening the game on a new profile lands in a run without a keypress |
| `test/MusicProbe.cs` | yes | Four layers of one length all playing from the first frame, layers arriving with intensity and crowd and the boss, a threshold that does not chatter, silence when the run ends, and every layer audibly what it claims to be |
| `test/DailyProbe.cs` | yes | One date derives one run every time, consecutive days differ, the second attempt does not count, dying spends it too, a streak is consecutive days, and the score is mostly about the shared card |
| `test/BiomeProbe.cs` | yes | Every biome loads, one has cover everywhere and the other has sight lines, the emptier one pays better for the walk, and both the crowd and a 0.35 m body can cross the dense one |
| `test/LoadoutProbe.cs` | yes | No slot has a piece that beats its neighbour everywhere, a piece's rule is live before the first level-up, two sets permit two different decks, and the starting kit grants exactly nothing |
| `test/UnlockProbe.cs` | yes | A fresh profile is offered less, every condition fires on its own run and nothing else's, opening one moves exactly one card, a locked row is listed and explains itself, and a save from before unlocks keeps what it proved |
| `test/EliteProbe.cs` | yes | A mark is a different fight — armour soaks, swift outruns, volatile bursts — it survives the swap-remove, and the boss arrives once, announced, and pays |
| `test/HordePerf.cs` | no | Frame time, physics time, draw calls under load (`-- 500`) |
| `test/ScaleProbe.cs` | no | Sprite world-height read against a 2 m reference pole |
| `test/GaitShot.cs` | no | A strip of frames while a movement key is held, under the game's own camera, printing travel against the facing and against the view's right (`-- hold:move_right`). It is the print that matters: strafing must read 90° off the facing and 0° off the right |
| `test/BillboardCompare.cs` | no | The side-by-side that settled full-billboard vs Y-locked |
| `test/Screenshot.cs` | no | Still of the main scene (`-- 0 0 mixed flash` checks the hit-flash channel; `fx` drives kills, a detonation, a shot and a hit *taken* just before the shutter — the last of those because the threat compass only exists while something is hurting the player, so it cannot be photographed by driving their side of a fight) |
| `test/EffectShot.cs` | no | The effect vocabulary as a row on the ground — flash, spark, smoke, gore, crit, splat, scorch — held still and spaced out, with three bodies lying behind it. `-- bare` photographs the same seeded frame with nothing staged, and the difference is the measurement: this floor draws brown and grey patches of its own, and twice a stain that was rendering perfectly was read off a single picture as absent |
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

A run is 300 s. The ambient horde ramps while its speed scales to 1.6x, **up to 160 alive at once**;
the dangerous places are the ones the player walks into rather than the clock. The extraction pad
opens at 15% of the clock and needs a 5 s hold, cancelled by stepping out.

**A run has a schedule, not a timetable.** Pads, both supply caches and the boss are drawn per seed,
in bands deliberately narrow around the numbers the sweep tuned — the boss near 0.40 because runs end
between 83 and 142 seconds, the first cache near 0.25 because the bag is full at 60 s and empty at
120 s. None of that tuning is discarded; the player simply cannot set a watch by it. A player four
runs in used to know the whole thing, and a timetable is not a decision.

Two things vary the shape rather than the times:

- **A surge**, in somewhat over half of runs: one announced wave from a single bearing. A wedge rather
  than a ring, because a ring is the ordinary spawn pattern with more of it while a wedge is a
  *direction* — something to turn away from or fire into. A run without one is not an easier run with
  something missing; it is a run in which holding a grenade back for it was wrong.
- **Knots**, in about a third of runs: 14–32% of the ordinary arrivals come as four or five bodies
  inside 2.2 m instead of one more from one more bearing. Same total over the same clock, delivered as
  a mass. It is not announced, because it is the texture of the whole run and the place to read it is
  what is walking toward you.

The top of that ramp used to be 12/s. A maxed weapon clears roughly three a second against the late
roster, so the field is already growing without bound at six — every rate above it only changed how
fast the number climbed, and the whole second half of the escalation curve was escalation the player
could not read. Eight keeps the curve visible at four times the opening while leaving the last
stretch somewhere skill still moves the outcome.

**Three places to fight, and they are different questions rather than different textures.** One arena
rule with a seed on it drew a different map every run and asked the same thing every time — fine until
the loadouts had identities, at which point a build made for standing still and a build made for
shooting through six were permanently being compared on the same ground.

| | Cover | Line of fire | Crates | Depth pays |
| :--- | ---: | ---: | ---: | ---: |
| Rail Yard | 63 blocks | 26 m | 8 | x1.9 |
| Old Town | 166 blocks | 17 m | 11 | x1.4 |
| The Flats | 12 blocks | 36 m | 7 | x3.0 |

Old Town is loot-rich with nothing to shoot down: crates are close together and a pierce build spends
the run hitting a bin, while thorns and knockback have walls to work against. The Flats has fewer
crates, further out, worth much more when you get there, across ground with nowhere to break contact —
speed and range are the answer and standing still is not. A biome is a row of numbers, not a second
asset pipeline: the ground tint multiplies the per-tile colour rather than replacing it, so the
player can still read where the rubble is from the floor.

Terrain is chosen at the base with `[B]`, **before** the shop, because a loadout that could not have
been built for the ground it is going to is a loadout whose identity does not matter.

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
| boss | 1600 | 1.15 | 26/s | 3.16 | 40% | placed by hand, once — the only thing in the run that is an event |

From 25% of the clock, a spawn can arrive **marked**, on a chance that ramps to 14% at the end. A mark
is one rule bent on an otherwise ordinary enemy: no new sprite, no new behaviour, one number changed
and a colour that says which. All three are 1.25x bigger, worth 4x experience, and answer a way of
playing that has stopped needing an answer.

| Mark | Bends | Answers |
| :--- | :--- | :--- |
| armoured | takes 0.35x damage, 3x health | a build that solved crowds and never has to aim |
| swift | moves 1.9x, 2x health | standing still, once the ring around the player clears itself |
| volatile | 40 damage in 4.5 m on death, 3x health | killing the thing in your face by reflex |

The colour lives in the instance colour block's green and blue because **red is the hit flash**, and an
armoured elite being shot is both at once — one channel could only say one of them. Size does the
reading anyway: nobody fighting fifty things compares colours.

The boss is the run's only scripted event: one, at 40% of the clock, from 30 m out. It is announced —
on the HUD, and by the explosion clip dropped two octaves — because a boss noticed only when health
starts dropping is a difficulty spike, while one that is announced is a decision: leave now with what
you have, or stay and take it. Killing it drops a cache biased hard toward the rare tail, so the
answer is worth something that outlives the run.

**It shoots, and that is not what it was designed to do.** The first version was slow, enormous and
melee, on the theory that the fight would be about the space around it. The balance sweep put one on
the field for a full minute and every measured outcome came back unchanged to within a rounding error:
at 1.15 m/s it can be walked away from forever, so it was scenery with a health bar. `EnemyBehavior`
gained a third case — `Siege`, which opens fire at 22 m *and keeps closing*, unlike `Ranged`, which
settles at its standoff. After that change the survivors' worst moment moved from 62 HP to 26 and from
86 to 30 on the two runs that reached it. Distance now buys time and never buys safety.

## Between runs

The game opens at the base, not in a run. It lists what came back, what the
stash is worth, what is on sale, and what practice you have — with "not for
sale" written next to it, because that is the one axis credits cannot reach.

Up and down move, enter buys or equips, `[S]` sells the stash at face value (the
extraction multiplier was earned by walking out with it and is not paid twice),
`[L]` launches. Buying and equipping share a key: a shop where they are separate
is a shop where the player buys something and walks out without it.

**Each slot offers two pieces at one tier, and they are not better and worse.**
Tier 2 used to be tier 1 plus numbers, which meant every slot had a correct
answer and the only question was what you could afford — a budget screen wearing
a shop's clothes. Now the piece that grants a rule pays for it in the stat its
neighbour is best at:

| Slot | | |
| :--- | :--- | :--- |
| armour | **Plate Carrier** soaks: +25 health, +1 armour, **−0.35 speed** | **Stitched Vest** returns: 35% thorns, +6% dodge, armour ceiling 1 |
| backpack | **Trekking Pack** carries loot: +8 bulk, +2 safe box, fortune to 5 | **Bandolier** carries ammunition: +1 pierce, pierce to 5, crit to 6, **fortune 0** |
| boots | **Running Shoes** leave: +0.6 speed, speed ceiling 5 | **Tread Boots** stay: regen, knockback, +20% area, **speed ceiling 1** |

**Tier 3 is one shelf and it is the backpack**, because that slot is literally the question *what are
you carrying instead of loot*, and "charges" and "the things that fight for you" are the two answers
the body had no way to give:

| Slot | | |
| :--- | :--- | :--- |
| backpack | **Demolition Rig** carries charges: +30% area, a shockwave already turning, **−6 bulk** | **Blade Harness** carries a retinue: +2 blades, +15% chain, **−4 bulk and −0.4 speed** |

Both pay in bulk, because bulk is what this slot is best at and a rule costs what the slot is best at.
That is the same rule the tier-2 pair lives by, against a Trekking Pack that grants *+8* of it — so a
tier-3 backpack is a decision to stop carrying the run's supply line, and it means something different
to the Warden's fourteen bulk than to the Courier's twenty-eight.

**Every growth line can now be committed to before a run**, which is what makes a leaning map an
argument rather than a verdict. Ward has five pieces and everything else two or three; the backpack
holds four of the five lines and lets you wear one, which makes it the largest single decision on the
screen.

Gear grants its rules *before the first level-up*, not as a bonus applied later —
a piece that appears to do nothing for the first ninety seconds is a piece the
player judges on those ninety seconds. It also sets the ceiling on the options it
is built around, so what a loadout decides is **the shape of the deck**, not what
gets drawn from it: the run is still different every time.

### Who is going

The decision made *before* the loadout, on `[C]` at the gate. None of the three abilities is damage or
fire rate — those are what the shop already sells, and a survivor selling them again is a difficulty
setting with a name on it. Every one is an existing `RunModifiers` field granted at the start of a
run, so a survivor is a head start on a strategy the deck, the gear and the trinkets all speak to.

| | | Opens after |
| :--- | :--- | ---: |
| **Drifter** | 100 health, 6.0 m/s, 20 bulk. No edges and no gaps — and it is what `Player` shipped with, to the digit, which is the whole reason the other two can exist safely | 0 |
| **Courier** | 80 / 6.6 / **28**, a wider reach on a crate and 15% more for what it carries out. Gets in, takes everything, does not stay | 3 |
| **Warden** | **140** / 5.3 / 14, a blade already turning and cold ground underfoot. Stands somewhere and makes the crowd come to it | 8 |

**They measure as three different runs, not three difficulties.** Twelve layouts, `lingers:auto`,
median: the Courier banks **2092** and leaves at 121 s on 50 health; the Drifter banks 1844 at 127 s;
the Warden banks 1190, stays to **139 s** and walks out on **87**. Survival is 9, 10 and 10. Nothing
dominates — the Courier owns the payout, the Warden owns the clock and the margin, the Drifter wins
nothing outright and loses nothing badly.

**The Warden's margin is money it cannot spend**, and that is recorded rather than tuned. Its binding
constraint is bulk and its advantage is health, so forty per cent more health buys nothing once
fourteen bulk is full — which happens long before it is ever in danger. The Courier's thin health
converts, because what it bought was eight more bulk.

Tiers open on extractions, not attempts. Dying repeatedly is not progress toward
being ready for better equipment, and a gate counting runs would pay for exactly
the loop everything else here discourages.

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

**Unlocks are the only progress that is not money.** Credits already buy every number in this game, so
a second currency-shaped track would be the same axis wearing a hat — the point of an unlock is that it
cannot be bought, only done. Eight of them, each gating one weapon or one growth option:

| Opens | By |
| :--- | :--- |
| Hunting Bow | Extract without firing a gun |
| Service Rifle | Extract three runs in a row |
| Reaper Scythe | Kill the boss and walk out |
| Ignite | Kill 60 in a single run |
| Detonate | Kill 8 with one thrown item |
| Thorns | Survive a run that took you below 15 health |
| Lifesteal | Search 6 crates in one run |
| Fortune | Extract with a multiplier of 2.5 or better |

**The condition text is the tutorial.** "Extract without firing a gun" tells a player that the bow
exists, that a run can be finished with one weapon, and that extracting is something you can plan for —
three things no menu was going to teach them. So locked rows are listed in the shop with the condition
printed where the price would go, and never hidden: content the player cannot see does not make them
want it, and content they can see and cannot have does. The debrief repeats the condition when
something opens, because "unlocked: Thorns" teaches that Thorns exists and nothing else.

**Nothing here is strictly better than what it replaces**, or the table would be a numbers curve with
achievements painted on it, and the first two hours would be the part of the game where the player
does not have the good weapon yet. Locked growth options are absent from the deck rather than shown
and refused — a card that explains itself is right in the shop, where the player is browsing, and
wrong mid-run, where the offer is three seconds long and they are being chased.

**Today's run is the reason to come back tomorrow**, and it works by being the same run for everyone
and playable once. Take either half away and it is an ordinary run with a label on it: without a fixed
derivation nobody is comparing anything, and without the single attempt a player who dislikes their
result simply plays it again, at which point "everyone got the same one" also means "everyone got as
many tries as they wanted".

The date derives all three of the seed, the place and the job — a stable seed with a biome read from
the profile would give every player the same layout somewhere else. It settles **nothing**: no
credits, no stash, no practice, no personal bests, no unlocks, and no equipment lost either. The
symmetry is the point. A daily that paid better than an ordinary run would turn the ordinary run into
the practice mode; a daily that cost gear but paid nothing would be a mode nobody takes their good
rifle into. What it pays is a row on the record and a streak of consecutive days.

Dying spends the attempt. Every other rule here makes death expensive, and a daily that let you keep
the day by dying would be a reroll button wearing a corpse. UTC throughout, because local time gives
unlimited attempts for the price of changing a clock.

**The record book** is the other half: `RunRecord` has always measured crates searched, the best
single throw, bosses killed, the lowest health a run came back from and the fastest way out — and every
one of them was read once, printed on the debrief, and thrown away. They sit next to the personal
bests now, as targets for different kinds of play rather than one leaderboard with four columns.

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

**Every weapon carries a signature as well as a stat line.** Six weapons separated only by damage,
range and magazine size are six difficulty settings for one weapon, and the choice at the shop is
supposed to be "which way do I want to fight". The knife bleeds — which rewards touching many things
once, the exact opposite of what its damage number suggests. The axe and the scythe both cleave, at a
quarter and three quarters, and the gap is the point: the scythe sweeps 160 degrees and carries most
of its damage through, while the axe is a 70-degree chop with the most damage per swing of any melee
weapon and the hardest shove in the game. One answers a crowd and one answers the thing in front of
you. The bow ricochets to a *new* target, which is what makes it different from penetration: it
curves through a group instead of needing them lined up. Both rifles burst, and the trait's cost is
the ammunition — a burst that fires extra shots for free is a damage buff with a sound effect.

**No weapon is a strictly better version of a sibling, and `WeaponProbe` has a stage that says so.**
The gear table has lived by that rule since the loadout rework and the weapon table never had it: the
Service Rifle beat the starting rifle on all thirteen of its axes for 1400 credits, and the Reaper
Scythe beat the Fire Axe on all eight of theirs. The rifle is the one that never stops now — the
largest magazine and reserve in the game and the fastest reload, paid for with a lighter round and a
shorter reach — and the axe went from being a worse scythe to being the opposite of one.

**Two of the fifteen signatures do no damage at all, and they arrived with the Sidearm shelf.** The
Hand Emitter takes 45% of a body's speed for two seconds; the Sidearm Pistol leaves it taking 20% more
from *everything* for three. Neither is worth measuring on its own, which is the point of putting them
in the small hand: both slots fire, so a shelf of four weapons each scored on its own damage is a
shelf with one correct answer on it. A mark is worth 20% of whatever the other hand is holding, so the
pistol is worth least beside a knife and most beside a Fire Axe — the first time a loadout has priced
its two halves against each other rather than adding them up. See `WEAPONS.md`.

Both are per-body statuses on the machinery bleed already used, with their own clocks and a clamp in
`Horde` (60% chill, 50% mark) so a typo in a `.tres` file is a weak weapon rather than a stun-lock.
**`RunModifiers.Chill` is a different thing with the same name** — a gradient of sticky ground around
the player, from a card — and the two multiply rather than replace, so being in the ring *and* shot by
an emitter is worse than either and still never a stop.

**Four reactions, and every one of them crosses the slot line.** A status that only its own weapon
could cash in would be a second damage number wearing a story; what makes these decisions is that the
applier and the consumer are usually in different hands, so a reaction is something a *loadout* does:

| Reaction | Applied by | Consumed by | What happens |
| :--- | :--- | :--- | :--- |
| **Shatter** | chill — Hand Emitter, Frost Cell, the Chill card | any heavy impact | the chill is spent for `impact x chill x 1.5`, so 45% turns a 26-damage axe hit into 43.5 and the next one is 26 again |
| **Spread** | bleed — knife, katana | a cleave sweep | the wound transfers to every *other* body in the arc; the source is excluded, or "spread" would mean "copy forever" |
| **Cook off** | burn — molotov, the Ignite card | any blast | the burn detonates in 2.4 m for 1.5 seconds of that fire, and the same blast cannot cook it twice |
| **Conduct** | shock — Arc Lance, Pulse Rifle | any hit on a body that is *also* chilled | 40% of the hit reaches two neighbours, one level deep, and the shock is spent |

Each also has a non-weapon source — a trinket or a card — so the deck can reach a reaction the shop
did not sell, which is what stops the shop dictating the run. And each is **spent**: a reaction that
left its status behind would be a permanent multiplier, and the player would stop choosing when to
trigger it. That is the failure mode `TraitProbe` asserts against, one stage per reaction, by driving
the real applier and the real consumer rather than calling the status setter.

The **War Hammer** exists to be the native Shatter consumer: 38 damage at 0.55/s through a 45-degree
arc, the slowest and narrowest heavy on the table, with the hardest shove. Beside a Hand Emitter it is
the biggest single number the game can produce; on its own it is a bad axe.

**Two weapons pay for the trigger with something other than ammunition, and that is a fifth
proficiency track rather than a trait.** A category says how a weapon *resolves* and which practice it
feeds, so the Hand Emitter stayed a Firearm — it resolves like a pistol — while overheat and a held
beam are genuinely different firing models and became `Tech`:

| | Fires | Pays with | Signature |
| :--- | :--- | :--- | :--- |
| **Pulse Rifle** | 8/s, no magazine at all | heat: nine shots fill the bar, a full bar locks the weapon, and it resumes below 35% | Shock |
| **Arc Lance** | a held beam, 0.1 s a tick, 12 m | nothing — it never stops | Shock, but only after 0.75 s dwelling on the *same* body |

Venting is the reload, and it is a better one to read: a magazine is a number that has to be looked
at, while a bar that fills as you hold the trigger is the thing you were already watching. The beam's
dwell is what stops it being a rifle with the recoil turned off — it rewards staying on one target,
which is the opposite of everything else that shoots.

Both leave **Shock**, and Shock exists because Conduct needed a source that was not a growth card: a
hit on a body carrying both Shock and Chill spends the Shock and carries 40% of it to two neighbours,
one level deep. Pool indices are stable for exactly one tick, so the beam also asks that the body it
thinks it is holding is still *where a moving body could be* — a death elsewhere swaps a stranger into
the same number, and without the distance check the dwell would silently transfer to them.

Old four-entry profiles load with Tech practice at zero rather than being refused. The arrays that
count practice and hits are sized from the enum now, so the next category is a row in a table instead
of five hard-coded fours nobody can find from the card that broke.

**The largest thing the Sidearm shelf found was that the sidearm was never firing.** `WeaponHandler.Fire`
took a slot as an argument and then read `Weapon` — which is the *active* slot's weapon — so from the
day both slots were turned on, the second slot fired the first slot's weapon: its damage, trait,
category, penetration and knockback, on the sidearm's own cooldown and out of the sidearm's own
magazine. `TickSlot` was correct about everything else, so the sidearm ran dry on its own reserve,
reloaded on its own timer and aimed to its own reach, and every readout agreed with itself. `Charge`
had it too, from the same cause: a rifle charging in the second slot handed its 3.5x to whatever the
first slot fired next.

Nothing could have caught it. `ForceFire` fires the active slot and every probe equips into slot 0, so
the second slot's firing path was the one path no test had ever run. It surfaced in the balance table:
the Sidearm Pistol's mark was changed from 20% to 12% and twelve seeded runs came back *byte-identical*,
which a deterministic simulation cannot do if the number reaches anything.
`TraitProbe.StageSidearmFiresItself` closes it, and it is the only stage in that file that lets the
handler fire on its own instead of driving `ForceFire`.

Three more bugs the traits uncovered, all about an index that stopped meaning what it meant:

- Every status was applied *after* the damage that could kill, and a kill swap-removes — so a killing
  hit left the index pointing at whoever had been last, and the status landed on a body chosen by
  array order. Bleed had this since it was written and got away with it, because 4 damage a second
  lands on somebody either way. A mark does not: the player watches the wrong enemy fail to die
  faster. `ApplyOnHit` is behind the kill check now, and it is one function rather than one per firing
  path, because a status wired into the shot and not the swing works on rifles and silently does
  nothing on blades.

- A ricochet chose its next target *after* the hit landed, and a kill swap-removes — so "anyone but
  the one I just hit" excluded whoever had taken the victim's index, which with two enemies on the
  field is reliably the only candidate. The next target is now picked before the damage.
- `Equip` did not clear the burst queue, so the shots a rifle still owed came out of whatever was put
  in the slot next, on the rifle's timing, while the early return skipped the normal firing path
  entirely. Swapping mid-burst fired an axe as a rifle.

**The upgrade pool is eighteen options, and twelve of them are rules rather than
numbers.** It used to be five, all of them a stat going up, which is the one thing
a survivors-like cannot be short of: with five, every run is the same run in a
different order and the offer stops being a decision by the third level.

Six are rare — crit, ignite, detonate, lifesteal, dodge, fortune — drawn at about
a third of the weight of a common one, so seeing one is the run's good news rather
than its baseline. Each has its own ceiling, low for the ones that compound, and
an option that hits its cap leaves the deck where the player can watch it go.

The rules live in `RunModifiers`, a plain field bag owned by the player and read
by the weapon, the horde and the loot containers at the point of use. The first
five options could add to whichever system owned their number; that stops working
the moment an upgrade is a rule, because a chance to crit is read by the weapon,
a chance to ignite by the horde and a chance to shrug off a hit by the player, and
scattering them means three systems each holding a field nobody can find from the
card that granted it.

Two details worth their own line. **Crit is rolled once per attack, not once per
target** — a wide arc rolling separately for five enemies turns a twelve percent
chance into "one of them took extra, every time", which is a duller card. And
**dodge is rolled per tick, not per hit**, because contact damage is a rate and
there are no hits: a tenth of dodge therefore removes a tenth of the damage over
any window that matters, which is exactly what the card promises.

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

**The game is played in daylight, and the fog is the reason that was hard.** Every authored colour
now lives in `scripts/systems/Palette.cs` — nineteen prop materials that were private to
`PropLibrary`, twenty-four body colours that were inline in `BodyMeshLibrary`'s switch, and the light
rig `BuildMain` hard-codes and five biomes override. They were consistent only by hand, which was
survivable while they all agreed on dusk.

The naive version of this change — invert every value, near-black fog becomes near-white haze —
builds clean, passes all forty-odd probes, and **cuts how well the fog hides the approaching horde by
3.3x**. Fog does not hide things by being dark; it hides them by washing out contrast, and it does
that in *both* directions. Put the air above the brightest body and every enemy at thirty metres is a
legible dark shape on a pale field. The dusk rig's real trick was never the darkness — it was that
the horde and the air were the same darkness. So the daylight air sits in the **middle** of the
horde's luminance range instead of above it, and the bulwark came up furthest because it was the
darkest ordinary variant and therefore the one setting where the fog had to sit.

`test/PaletteProbe.cs` measures this per biome at that biome's own spawn ring, against the dusk
rig's recorded numbers. All five land within 13%:

| Place | Ring | Dusk | Daylight | |
| :--- | ---: | ---: | ---: | ---: |
| Rail Yard | 30.0 m | 0.0492 | 0.0528 | 1.07x |
| Old Town | 23.4 m | 0.1034 | 0.1110 | 1.07x |
| The Flats | 30.0 m | 0.0492 | 0.0528 | 1.07x |
| Ash District | 26.4 m | 0.1189 | 0.1225 | 1.03x |
| Cold Storage | 22.2 m | 0.0632 | 0.0716 | 1.13x |

**The sun's energy did not move, and holding it still is what makes that table mean anything.** The
first draft raised it to 1.5 alongside every albedo, on the reasoning that midday is brighter than
dusk — both halves true, and doing both is double-counting: concrete, chalk and panel clipped to
white, and a prop at the arena's edge read as a sheet of paper standing on grass. The scene is
brighter because the things in it are. The probe compares authored colours rather than rendered
pixels, so it is only faithful while every body and every fog is lit by the same lamp at the same
strength; moving the energy would leave it passing while measuring a scene the game does not draw.

**The way to a blue sky was a blue air, and four attempts went past that.** The camera tilts 26° down
with a 60° field, so it never looks more than about ten degrees above horizontal — the only sky this
game draws is the band right above the horizon, and `LevelGenerator` assigns the biome's fog to
exactly that band. Attempts one to three moved `FogSkyAffect`, then `SkyCurve` (whose direction is
inverted from how it reads — a *smaller* curve makes the top colour dominate), then `SkyTop` itself,
and all three were adjusting a gradient that was being overwritten. Attempt four decoupled the
horizon from the fog and produced a blue sky with **white slabs floating in it**: the skyline ring at
sixty metres, fully fogged, correctly drawn, and no longer the colour of what was behind it. Anything
past `FogEnd` *is* the fog colour, so the sky and the air cannot disagree. The fog is now
`(0.55, 0.68, 0.86)` — a sky blue that still lands at 1.07x on the table above.

**Hue was the wrong measure for "the player must never be mistaken for one of them".** A rule the
game had in prose and never in code. Written the obvious way — sixty degrees of separation — it
failed on its first run: the player at 219° against the **brute** at 218°. Both readings correct, the
conclusion nonsense, because the brute is a blue-*grey* at 0.11 saturation and hue is meaningless
just above zero saturation. The probe compares chroma vectors instead, saturation as the length and
hue as the angle, so two greys are near each other and far from anything saturated. The horde's
nearest approach is the lantern at 0.53 against a floor of 0.35.

**The air dust stopped being white.** White is right when the air behind it is black — dust is seen
because it catches light. Against a bright haze the same specks read as falling snow. Real motes
against a bright sky are silhouettes, so they are now darker than the air and warm.

**A `.tres` omits any property equal to its class default, which made one bug invisible.** Three of
the five biomes say nothing about their air and are re-read against whatever `BiomeResource` declares
today — so a literal default there kept serving the *first* draft of the daylight fog to those three
for a build, while the two that name their own colour looked perfect. The defaults are taken from
`Palette` now. The probe caught it; nothing else would have.

**The baker learned two things, and between them a CC0 library of 4,800 models became usable.** Both
were predicted in `BakeBody`'s own comments and neither was needed until a real third-party character
was put through it.

It refused a model with more than one mesh node, and said why: baking the first one produces a
*sound* bake — watertight, correctly scaled, correctly rigged, and missing a head. Kenney's characters
ship as `body-mesh` plus `head-mesh`, which is an ordinary way to author a character rather than an
export mistake. `Convert` walks the list now, each node in its own transform, because a head parented
under a neck carries that offset in its node transform and applying the body's would stack it on the
floor.

The second is what actually unlocks the library. A baked body is one vertex-coloured surface and
`body.gdshader` samples nothing — that is what puts a hundred and eighty of them in one draw call —
so a textured model arrives as a material whose `AlbedoColor` is white and bakes a white character,
which looks exactly like a model whose author chose white. But a large part of the free low-poly world
does not texture in the usual sense: it maps every triangle onto a flat patch of a small palette
image. Kenney's entire 3D library does, under the name `colormap`. Sampling that per vertex is exact
for a flat patch, and turns those models into precisely the geometry this game already draws. It goes
wrong visibly rather than silently on a model with real painted detail, and `--tint` still overrides
per surface.

The texel conversion is the opposite of the one beside it, and that asymmetry is the glTF spec rather
than a preference: `COLOR_0` and `baseColorFactor` are stored linear and must *not* be converted,
while `baseColorTexture` is required to be sRGB and must be. Getting it backwards produces a body
about twice as bright as drawn — the exact bug the first baked stalker shipped with.

**Cover can come from a model now, and the reason it could not is narrower than it looked.**
`PropRenderer` says the props are boxes because a MultiMesh loses an imported mesh on pack/save. True,
and it is about a mesh resource *owned by another file* — the same escape hatch the horde already uses
applies: the arrays live in a `.res` and the `ArrayMesh` is built at runtime, so it is owned by
nobody, and `PropRenderer` builds per run rather than packing. Eight kinds are authored models; `Wall`
and every structure stay boxes because the generator stretches cover along its footprint and boxes
stretch gracefully where a water tower does not.

**A modelled prop is normalised into a unit footprint, and the height is never the model's.** Cover is
authored with X and Z in -0.5..0.5 and Y in real metres because the layout scales each instance to the
footprint it picked. `Height(kind)` is what the fight was tuned against, so a biome swaps furniture
without swapping what the player can see over — the same rule the enemy table has.

**Two lifetime bugs, both invisible in a single run.** Caching the built `ArrayMesh` statically
survives exactly one arena: the mesh goes to a MultiMesh the arena owns, the arena is freed, and the
next run holds a wrapper around a disposed object — a hundred levels in one process turned into a wall
of `ObjectDisposedException`. Then loading with the default cache mode hands back Godot's *shared*
instance, so arenas share one resource and the first teardown releases it under the others; that one
did not throw at all, it was an intermittent native crash passing one run in two. `CacheMode.Ignore`,
and no cache.

**There is a second way to author a body, and it is the better match for this shader.** A skinned
model says which vertices are a leg through joints and weights; a **rigid-node** model says it through
the node tree — one mesh per limb, named `leg-left` and `arm-right`, animated by moving node
transforms rather than by deforming anything. `body.gdshader` turns whole limbs rigidly about a pivot
and cannot express a bending knee, so a rigid-node model *is* that rig already: the conversion is the
identity rather than the "take the dominant joint" approximation the skinned path has to make. The
baker used to refuse these with "no Skeleton3D — there is nothing to derive a rig from", which is a
poor thing to tell a model that names its limbs more plainly than any rig does.

**Two Kenney sets were tried and they fail in opposite directions, which is what makes the pair worth
keeping as fixtures.**

| | Mini Characters | Blocky Characters |
| :--- | :--- | :--- |
| Rig | skinned, 7 joints | 6 rigid nodes |
| Colour | 512² `colormap` palette | 1024² painted skin |
| Merged | 723 tris, 1259 verts | **72 tris**, 143 verts |
| Hip / shoulder of height | 0.50 / 1.04 m at 1.90 m | 0.81 / 1.55 m at 2.20 m |
| Bakes | colour exactly; **T-pose** arms | pose correctly; **colour smears** |

The Mini set's bind pose is a T-pose, so the arms come out straight sideways — fixable by applying a
frame of the `idle` animation the pack ships before flattening. The Blocky set has no such problem
because its rest pose has no rotations at all, but its texture is a painted skin rather than a
palette, so per-vertex sampling gives one colour per box corner and each face becomes a gradient —
exactly the failure `PaletteImage` documents. **The fix for that one needed no new code**: six
surfaces, one per limb, and `--tint` already forces a flat colour per surface. Seventy-two triangles
against the procedural bodies' five hundred, in the game's own palette.

Which set the survivors should use is a taste decision and is not made here. Both are committed as
bake fixtures because between them they exercise both code paths.

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

**One blend mode was never going to be enough, and moving to daylight is what proved it.** Every puff
in the game was drawn additively, which is right for anything that emits and has no ceiling: a pale
plume over the bright half of the frame climbs to white. At dusk that never showed, because the field
behind it was near-black and had nowhere to climb to. On a daylit ground it is most of "the effects
look like lens dirt" — smoke read as haze, and a kill's spray of four additive puffs went white and
lost the horde's green entirely. `render_mode` is the one thing a shader cannot decide per instance,
so the pool is one pool split across two passes at upload: `effect.gdshader` adds, `effect_soft.gdshader`
blends, and `effect_body.gdshaderinc` is everything else about them, which is everything but that line.
One extra draw call and a branch.

What blending buys is *not* darkness. That was the first reason written down and it is wrong:
instance colours are linear, and the arena's grass measures **0.058 linear** — a pixel of 0.27 — so
anything a person would type as "dark grey" is several times brighter than the floor it is drawn over.
A plume that reads as smoke here is a pale one, which is what powder smoke looks like anyway. What
blending buys is the ceiling: the same plume converges *to its own colour*, lightening a dark floor and
darkening a bright sky, and occluding what is behind it either way.

**The stains had to go an order of magnitude darker than they read on the page, for the same reason.**
A splat authored at (0.20, 0.17, 0.12) — which anybody would call dark brown — blended over the grass
and came out *brighter* and warmer: a worn patch of bare earth, indistinguishable from the tan tiles the
ground shader already draws. Measured against the same seeded frame with nothing staged it lifted the
floor from (68, 86, 58) to (86, 109, 62), which is the opposite of a stain. Under 0.03 the arithmetic
goes the other way: (0.020, 0.028, 0.010) now takes it to (60, 77, 41), and the scorch to (52, 65, 44).
Hue is what keeps the two apart from each other and from the arena — the splat stays inside the horde's
green, the scorch has no hue at all.

**That comparison is why `EffectShot` takes two pictures.** This floor draws brown and grey patches of
its own — biome tint, scatter debris, the ground texture — and twice a stain that was rendering
perfectly well was read off a single screenshot as absent, once as "the marks do not render" and once
as "the marks render too light". `-- bare` photographs the same seeded frame with nothing staged, and
the difference between the two is a number rather than an opinion. It also caught the third version of
the same mistake: the row was staged seven metres *in front* of the player, twenty from the camera, and
this biome's fog had washed both stains out by then. They were drawing correctly and fogging correctly,
which is the worst way for a picture to be wrong.

**A muzzle flash is a shape, not a size.** Sized off the weapon's damage the old ball was a fifteen-
centimetre speck — nine pixels, under the width of the HUD's own font — and growing it enough to see
turned it into a floating orange disc, because a soft radial ball has no shape to grow into. Every puff
in the game was that one texture, so a flash, a blood burst and a plume of smoke differed only in size
and colour, and size and colour are both magnitudes. There are three shapes now, in one
`Texture2DArray` selected per instance: a ball, a four-pointed star with a hot core, and a lumpy
edge-soft blob with no core at all. The star is mostly transparent, so it can be a metre across and
still put only a small bright core on screen — and it lasts four frames rather than five, because
shape buys legibility that duration used to have to.

**A body comes apart along the shot that killed it.** The burst used to scatter uniformly, which reads
as a body deciding to stop existing: the shot could have come from anywhere. `KillDetail` carries the
knockback now, unscaled by the variant's resistance, because what the effect wants is the *bearing* of
the shot and a brute taking knockback at 0.2x is still being shot at from somewhere. It is zero for
everything that kills without a direction — burning ground, bleed, a blast the victim was merely
standing inside — and those scatter, correctly.

**Two cards the player could buy had no picture, and both are now drawn.** Crit multiplies damage, and
damage already scales the impact puff, so the only evidence of the twelve percent was that a target
occasionally died a shot early — indistinguishable from having aimed at a weaker one. It is carried on
the `Hit` event rather than inferred, because the number a listener would have to compare against is
the weapon's effective damage at its level, scaled by armour, elite mark and any charge held, none of
which reaches the screen. The roll is once per attack, so the flash is once per attack: a shotgun's
eight pellets and a scythe's five bodies each say it once. Ignite was worse — it is one of the eight
things in this game that cannot be bought, opened by killing sixty in a run, and what it bought was a
`Pool.Burn` field nothing drew. A burning enemy looked exactly like one that was not, right up until it
fell over. It gets fire over the body rather than a hit flash, for the reason immediately below, and
the scan is a rotating window over the pool because a molotov can leave forty things alight.

**The game stops for a fraction of a second on the biggest hits, and it is safe for a reason that is
arithmetic rather than care.** Hitstop is the only thing in this project that touches
`Engine.TimeScale`, and slowing the clock slows the *whole* clock — the run timer, the spawn rate, the
damage per second and the player's own speed all scale together, so damage per game second is unchanged
and every balance table in this file still measures what it says. What it changes is real time, which
is the entire point: the player gets four extra hundredths of a second of looking at the thing they
just killed, and no advantage they can spend. It fires on three events and not on kills, because an
ordinary kill happens three times a second at the top of the ramp and a game that stops three times a
second stutters rather than lands: a **marked** enemy dying, a blast, and a crit. It never stacks and
never shortens, so two blasts a frame apart are one longer stop and a crit inside a blast's stop cannot
cut it short.

`EffectDirector._Ready` clears the flag headless rather than a second condition guarding every call
site, and that choice is what makes it testable: a probe counting ticks would be counting a different
length of tick, so nothing in the sweep may ever see it — but `ImpactProbe` can switch it back on,
drive a blast and assert the clock both moves and comes back. That last assertion is the one worth
having. A clock left slow is not a visible defect; it is every other probe in the sweep quietly
measuring a different second.

**An explosion lit nothing.** Twenty bodies standing inside a blast were lit by the sun exactly as they
had been a frame earlier, so the one moment in a run with a real light source in it read as a decal
played over the top — and the horde, which is where an explosion's meaning is, was the part that did
not react. Four pooled omni lights, warm, three tenths of a second, energy falling off a square.
Shadows off, and not as an optimisation: a light that casts shadows renders a cube map on the frame it
appears, and the frame it appears is the frame that already has a screen shake and a dozen puffs on it.
The stutter would land on the one event the whole effect system exists to sell.

**Damage numbers are against this project's own HUD rule and they are here anyway.** The rule is that a
number the player has to read is a number they will not read while a brute is on them, which is why
everything else in the readout is a bar. It is a good rule and it is about the *readout* — a figure in a
fixed corner that has to be found, focused on and compared with what it was a second ago. A number over
the thing you just shot is a different object: not consulted, glanced at, and it answers the one
question a build cannot otherwise answer. Fifteen weapons and eighteen growth options multiply into a
damage figure that appears nowhere, so a player takes "+12% crit" and finds out whether it mattered by
how the run ends forty seconds later. The restraint is the rate limit rather than the feature — ten a
second, with crits never suppressed, because a crit is the thing the number exists to show.

It is drawn in immediate mode by a `Control` rather than by nodes. A damage number is a `Label` that
lives for half a second and a crowd makes a dozen a second, which is a hundred nodes a minute created,
laid out and freed on exactly the frames the game is busiest.

**Being hurt had no direction.** The vignette says *that* you are being hurt, which the health bar
already said, and the player's decision is which way to walk. So the same overlay carries a compass:
eight sectors around the player, each holding a weight that damage adds to and time takes away.
Sectors rather than one arrow, because being surrounded is the situation it exists for and a single
arrow can only point at one of five things eating you — and the contact sum is deliberately not
normalised, so a perfect ring cancels itself out, which is the honest answer. `Player.Hurt` is a second
damage signal and had to be: `ConsumeDamageTaken` is read-and-clear with exactly one owner, and a
second consumer would take turns with the first and see about half of what happened. An event that
fires per application is the right shape for a rate; a second accumulator is not.

**A spitter had no wind-up.** It stood at eight metres and a projectile existed. The only warning was
the projectile, which is already the damage — so the variant that exists to punish kiting could not be
answered by moving, because there was nothing to move *before*. A charge that brightens over the last
fifth of a second is the whole fix, and it costs one read of the cooldown the ranged step already
keeps: the cooldown only runs while the thing is inside its standoff, so "nearly zero" already means
"in range and about to fire" without anything having to re-derive either. A fifth of a second is about
a metre and a quarter of movement at a survivor's 6 m/s, and it is a small fraction of the spitter's own
interval rather than a state it is usually in.

**Every projectile was one sprite, and shape is the channel that survives the frame.** Per-shot tint and
scale arrived a phase ago and fixed half of it, but the pump shotgun's pellet and the marksman rifle's
round were 0.55 and 1.35 of the same 160×40 lozenge — one object at two sizes, which is what the player
was being asked to tell apart at twenty metres through fog. There are six silhouettes now, generated by
`BuildProjectileSprites.cs` because they are outlines a few dozen pixels across and an outline is easier
to re-tune as arithmetic than to redraw: a slug with a stub of a tail, a fletched arrow, a long thin
lance, a speck of a pellet, a finned charge, and the horde's ragged glob. Every pixel is in or out, with
no antialiasing, because `horde_billboard.gdshader` discards below an alpha of 0.5 rather than blending
and a soft fringe comes out ragged at exactly the size these are seen at. `WeaponFeelProbe`'s
fingerprint carries the shape now, so "no two weapons emit the same signature" covers it.

**The horde's shot is the ugly one on purpose.** Everything the player fires is machined — capsules, a
lance, a finned charge — and the thing coming back at them wobbles and dribbles. That is the whole of
what separates "my shot" from "their shot" in a frame containing both, and it is why the two renderers
load one shared list: they have to agree on what layer four is, and while they both loaded a single
file that agreement was free and invisible.

**A kill was a body ceasing to exist.** Everything else about it was answered — a spray, a dark cloud, a
stain on the floor, a sound — and the one object the player was actually looking at vanished between two
frames. That is the loudest artificial thing left in a fight: fifty of them a minute, each a hole in the
picture at the exact moment the player's attention is on it.

What is there now is not a ragdoll and deliberately not one. The horde is a MultiMesh — one mesh, N
transforms, no skeleton — so what a body can do after it dies is exactly what a transform can express.
It falls over: pivoting about the sole of the foot, accelerating on a square like something that has
stopped holding itself up, going over *away from the shove that killed it*. A body that always fell
north would be scenery; a body that falls the way it was pushed is the last frame of the thing that just
happened. A death with no direction in it — a bleed, burning ground, a blast the victim was merely
standing inside — drops where it stood, which is the same distinction the gore spray makes.

It ends by sinking rather than fading, because an opaque instance has no alpha to fade: the body is
translated down through the floor over the last four fifths of a second and the ground closes over it.
Forty at a time against a run that kills several hundred, and the ceiling is the design — a corpse is
evidence that a fight happened here, and evidence stops being evidence when it is the floor. They are
written into the horde's own per-variant buffers *after* the living, so the one thing that happens when
a buffer is genuinely full is that a corpse is dropped rather than an enemy. An enemy nobody can see is
a bug; a corpse nobody can see is a corpse that sank early.

**Everything a weapon does was drawn at ankle height, and nothing said so.** `WeaponHandler.MuzzleHeight`
has existed since the weapon was written and was read by nothing. The projectile renderer flew shots at
half of `ProjectileHeight` — which is the bolt *sprite's size*, 0.25 m — so every arrow, bolt and tracer
in the game crossed the field at twelve centimetres, and `EffectDirector` put the muzzle flash at
fifteen, correctly, to avoid adding the ground height twice to an origin that is already planted. Both
were right in isolation and both were on the floor: a whole exchange read as something happening around
the players' shoes. It stayed invisible for as long as the flash was a fifteen-centimetre ball, and
became obvious the moment the flash was a metre-wide star. `Sync` takes a flight height separate from
the quad's size now, and the flash, the tracer and the impact are on one line at a metre.

**A shot that takes time to arrive leaves a wake.** Travel time is the whole of what separates a
projectile weapon from a hitscan one — a bow's shot has to be led, and leading something you can barely
see is guesswork. One small quad crossing thirty metres is a fleck; a fleck with three metres of fading
behind it is a trajectory. Only the shots that are real get one: a hitscan tracer is already a
zero-damage projectile fired purely to be seen, and a wake behind every round of an automatic at seven
a second is a beam weapon rather than a rifle. Zero damage is the existing marker for "cosmetic" — the
collision stage skips on it too — so this needed no new field. The wake takes the projectile's own
tint, because per-shot tint is what stopped fifteen weapons crossing the screen identically and a trail
in one authored colour would undo half of it.

**A swing is drawn as the arc it covers.** Reach and sweep are the whole of a melee weapon's identity
and neither was on screen: a knife at 1.6 m over 90° and a scythe at 3.4 m over 200° drew the same
streak at different lengths. The puffs step along the weapon's own arc at its own radius now, brightest
in the middle of the sweep, each living a hair longer than the one before — which is what makes a
static line read as a blade travelling.

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

**The music is four loops, not a playlist.** A run has a shape — opening, the horde forming, the boss,
the walk out — and the ambience layer swelling with the crowd is a volume knob, not that shape. Bed,
pulse, tension and boss are all 48 seconds at 80 BPM, all playing from the first frame and never
stopped, each faded independently. A cut between two pieces of music is heard as a glitch and a
crossfade between two that do not share a tempo is heard as a worse one; layers written to sit on top
of each other can be added or dropped at any moment and it still sounds deliberate. They stay
*playing* while silent because starting one late would put it seconds out of phase for the rest of the
run with no way back.

Texture, not melody. A synthesised tune is both unpleasant and finite — the player hears it forty times
an hour — while a drone, a pulse and a noise bed are things a run can be underneath for five minutes.
The bed is two low sines a third of a hertz apart, so it beats slowly and never resolves into a tone
the ear gets tired of; the boss layer is a semitone against that root, the one interval nobody hears
as music by accident.

Every threshold has hysteresis. `Intensity` is smooth but the crowd count is not, and a single
threshold with a value hovering on it turns a four-second fade into a layer breathing in and out once
a second — which reads as the mix being broken rather than as a number being borderline. Its own
`AudioStreamPlayer`s, never the SFX pool: that pool is a fixed ring the oldest voice recycles out of,
so a busy second of explosions would take the music with it.

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

**A body mesh is built once per silhouette, and that is a crash fix rather than a frame-time saving.**
Changing what the player holds rebuilds the body — a `MultiMesh` has no skeleton, so a held object is
geometry *inside* the mesh turning on the arm's pivot — and every rebuild allocated a fresh `ArrayMesh`
and several `Godot.Collections.Array`, all of them `RefCounted`. The two loadouts where both weapons
are melee flip between `Longarm` and `Blade` on every swap, and that was enough churn to take the
process down inside `BakedBody.Build`:

```
FATAL: Condition "gchandle.is_released()" is true   mono_object_disposed_baseref
  BakedBody.Build   Player.CreateBody   Player.CarryChanged   Player._PhysicsProcess
```

Five of the twelve sweep layouts on `fire_axe+katana`, and some on `fire_axe+combat_knife`. Never the
same frame twice, because what decides it is when the GC runs — and the stack is `Player._PhysicsProcess`,
so it was a player's crash and not a test's. `Player` keeps one mesh per `Carry` now, which bounds a
whole run at two builds; `SoloBody` leaves a material alone when the mesh already carries the right
one, or the cache would have traded an `ArrayMesh` per swap for a `ShaderMaterial` per swap. Twenty-four
runs across both double-melee pairs came back without it.

Nothing can assert "the finaliser did not race", because that is a question about the GC.
`BodyProbe.StageCarryMeshIsBuiltOnce` asserts the thing that can be checked — that holding a silhouette
a second time returns the same mesh, and that two silhouettes are still two meshes — and it fails
against the previous code, which is the only reason to trust it.

**The touch layer had never been executed.** It was written in Phase 1 and compiled for sixteen phases
with nothing instantiating a `VirtualStick`, `TouchStickInput` never constructed, `SetInputSource`
never called, and six of the actions it exposed hardcoded to false. `FireHeld`, `InteractPressed` and
`ReloadPressed` were on the interface and read by nothing at all — firing and reloading are automatic —
so they are gone: an interface member nobody reads is a promise nobody checks, and every implementation
still had to invent an answer for it.

Two bugs that only exist on touch, both found by the probe rather than by looking:

- The move stick owns the left half of the screen on a higher canvas layer, and it sat on top of the
  left-hand level-up card. The player could read three options and take two. The row lifts above the
  stick on a touch build, rather than disabling movement while an offer is up — that would be a pause
  by another name, and the offer was designed not to pause.
- `Hud` asked `TouchHud` whether touch was active during `_Ready`, which is too early: nodes are
  readied in tree order and `TouchHud` is added later, so the answer was always "no". The layout
  decision moved to the first frame.

`TouchProbe` pushes synthetic fingers through the input singleton rather than calling `_GuiInput`,
because half of what can be wrong with a touch UI is layout — a control the finger never reaches, a
filter that swallows the press — and calling the handler skips exactly those. It needs a real display:
the headless dummy never dispatches GUI input, so every stage passes its rect check and receives
nothing.

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

**Adding one row to the enemy table broke two probes that were entirely right about the game.**
`EnemyTypeProbe` and `DebriefProbe` both had `5` written into them as the variant count, so a correct
sixth row made them report a correct game as broken. `DebriefProbe` now reads `_horde.Types.Length`;
`EnemyTypeProbe` keeps a literal but moved it to 6 with the reason written down, because *that* probe's
job includes noticing a row nobody announced. The general shape is worth keeping in mind: a constant
copied out of the data is a claim about the data that stops being checked the moment it is copied.

**A kill's worth cannot be measured on the progress bar.** The first version of the elite experience
check read `RunGrowth.Experience` before and after a kill, and measured a marked walker at **minus
eight** — true about the bar, which is spent on every level-up, and silent about the question. The fix
was a second counter (`ExperienceEarned`) that only ever goes up. Anything spent is not a measurement
of what was earned.

**The elite scale bonus had to go into the custom AABB too.** The horde's bounds are computed from the
largest `SpriteScale` in the table, and elites multiply that by 1.25 at draw time. Left out, the
symptom would have been the marked enemies — the ones worth watching — vanishing early at the screen
edge while everything around them kept drawing, which reads as a culling glitch rather than as a
number that was never updated.

**`KillDetail` is a second event rather than a wider first one.** Six things subscribe to
`EnemyKilled`, and exactly one of them needs to know that a marked walker is worth four times a plain
one. Widening the shared signature would have written that one subscriber's requirement into five
files that do not care.

**The elite tint took three tries, and only a screenshot could judge any of them.** Added flat at 0.55
it erased the painting — an armoured brute was a solid blue silhouette, which is the hit flash's
failure mode wearing a different colour, except permanent. Multiplied instead, the brute came back but
the bloater — a much paler sprite — clipped to a glowing white-green blob. The answer was to weight the
push by `1 - luminance`, spending strength where the sprite has headroom and backing off where it has
none, so one constant is right for a dark creature and a pale one. Every probe passed at every stage.

**"Extract without firing a gun" was checked against practice, and practice is not what it means.**
Practice is banked at 250 hits per point, so a run with 249 firearm hits and a run that never drew a
gun both record zero — and the bow opened on a seventeen-second extraction. The tell was in the save
file rather than in any probe, because the probe's fixtures all had an all-zero hit array and the
stage that was supposed to catch cross-firing had a written-in exemption saying the bow was allowed to
come along with anything. The record now carries `HitsByCategory`, the condition reads it, every
fixture fires a gun by default, and the exemption is gone.

**The boss cache's loot has never been counted, since Phase 20.** Everything that cares about crates
took its list once in `_Ready`: the log's census, the sound director's subscriptions, the HUD compass,
and the play-test bot. All four are correct for a map that does not change, and a crate that arrives
mid-run raised nobody's `CratesLooted`, satisfied no "empty N crates" contract, and set no record. It
survived six phases because a run in which the player did not open it looks identical. Found while
adding a second thing that arrives mid-run; all four now watch `ChildEnteredTree` instead.

**A new player's first ninety seconds was a shop.** Fifteen rows, three terrains, a contract board and
eight unlock conditions, every one of them an answer to a question they had not been asked. The first
launch now goes straight into a run and the base is what they come back to, with a result in hand.
It is the least-exercised path in the game and the one the most people meet — everyone sees it, nobody
sees it twice, and a developer with a save file on disk cannot see it at all — so `FirstRunProbe`
drives it end to end rather than asserting the flag.

The flag is stored rather than inferred from "are the run counts zero", because zero counts are also
what a probe writes for a clean profile. An absent key means a save written before this existed, and
those players default to *seen* — the other way round would drop every existing player into a run on
their next launch, past the shop they were walking to.

**A probe cannot hear, so it checks the things that fail inaudibly.** Whether the mix sounds good was
settled by listening, which is the only way. `MusicProbe` exists so a change three phases from now
does not silently undo it: that the four loops are the same length (unequal lengths drift apart and
the layers stop agreeing about where the bar is — obvious after a minute, never reported as "the loops
are different lengths"), that they all start together, that layers follow the run's state rather than
a flag a probe set, that a value parked on a threshold produces one answer forty times running, and —
last, because everything above it would pass unchanged against four arrays of zeros — that each layer
is audible and the pulse layer's energy actually rises and falls where the bed's does not.

**Three of that probe's stages failed on their first run and none of them was the game's fault.** Two
read a layer's target immediately after moving the clock, which returns the previous frame's decision;
the third re-ran its own setup every tick, so "before the boss arrived" was captured four ticks after
it had. A probe that drives a system from outside its update loop has to ask for the update, and one
whose setup is not guarded to the first tick is measuring its own last iteration.

**Every menu in this game has been double-spaced since the first one was built.**
`StringBuilder.AppendLine` writes `Environment.NewLine`, which on Windows is `\r\n`, and Godot's Label
treats the carriage return as a line break of its own. So each line drew twice as tall as it should,
and the symptom — "the list runs off the bottom of the screen" — reads as a content problem. It was
treated as one four separate times: the base screen was split into two columns for it, the shop list
was given a scrolling window sized to it, and a per-item description was moved above the list because
of it. One `Replace("\r\n", "\n")` on the way into the Label recovered half the screen; the window
survives, sized to the screen this time, because the catalogue does only grow.

**Skipping a run's settlement is not the same as skipping the run.** The daily's "settles nothing"
rule was first written as an early `return` at the top of `OnRunEnded`, which also skipped freezing
the record, writing the score, and showing the debrief — the mode turned itself off, including the
part that records the result. It is a `settles` flag guarding the specific effects now.

**The dense biome shipped with an exit the play-test bot could not reach.** "Could not reach
extraction in 60 s, still 49 m away" — on a map whose own reachability check had carved nothing,
because it found every route fine. Three numbers were involved and none of them was the geometry: the
player's body is 0.35 m, the navigation grid is 1.5 m cells, and the bot inflated obstacles by 0.9 m
before pathing. A 2.2 m doorway survives a 0.35 m body and does not survive 0.9 m of inflation at that
resolution. The bot's margin came down to 0.55 and the biome's doorways went up to 3.2 m — a gap that
only just exists on the grid is one that some consumer of the grid will decide is not there.

**A bot that never stops turning cannot reach anything inside its own turning circle**, and it reads
as a level bug. Turn-and-advance moves at `v` while turning at `ω`, so the tightest
arc it can trace has radius `v/ω` — 6.0 m/s against 150°/s is **2.29 m**. `BotDrive` advanced whenever
the target was not behind it, so a bot that arrived off-heading settled onto exactly that circle and
stayed there: seed `0x27D4EB2F` orbited `Crate0` at **2.3 m** for the full sixty seconds, on every
loadout, with the flow field pointing straight at it the whole time. Every diagnostic said the route
was correct, because it was. The Warden, at 5.3 m/s, orbited the same crate at 2.2 m — which is
`v/ω` again and is what identified it.

It turns on the spot now, and only inside `2·v/ω`; outside it the old rule stands, because a bot that
stopped dead at every corner would be standing still in a horde and measuring a different game. The
radius comes from the player's own top speed rather than a constant, since speed is a growth option
and four of them nearly double the circle — and from top speed rather than current velocity, or the
gate would collapse the moment it fired and start the orbit again. That seed went from
`Stuck, banked 120 at 70 s` to `Extracted, banked 3172 at 167 s`; it now searches the crate it spent a
minute circling **at 11 s**.

**The other stuck seed was two correct answers making a loop, and it needed the bot to remember which
one it had just given.** Obstacles are inflated by 0.55 m before the field marks them and the body is
0.35, so the blocked band reaches about a metre past anything that can be touched — and `Sample`
returns zero throughout it. Two completely different situations produce that zero. A bot *leaning on a
wall* needs to be told which way is out, which is what `EscapeFrom` was written for. A bot *clipping
the band while turning through a gap* needs the opposite, and it clips constantly, because
turn-and-advance arcs and the cells are 1.5 m.

`0xD6E8FEB1` ran that on a six-second cycle for a whole leg, traced tick by tick:

```
at (33.9,14.4) blocked=False sample=(0,1)          <- the field points through the gap
at (32.9,15.6) blocked=True  escape=(-0.71,-0.71)  <- arced a metre sideways on the way in
at (31.0,14.7) blocked=False sample=(1,0)          <- escaped, back where it started
at (33.8,12.8) blocked=False sample=(0,1)          <- and pointed at the gap again
```

Nineteen metres from `Crate7`, four metres of travel every ten seconds, and every single step
defensible. Nothing in one frame separates the two cases, so `RouteMemory` holds state: the last
heading the field gave wins for 45 ticks — long enough to cross a metre of margin, short enough that a
bot against something real gives up inside a second — and only then does the escape run. The collider
decides whether the gap was real, which is what a collider is for. The escape also *forgets* the
heading it is escaping from, because the cell it lands in hands that heading straight back and one
tick of it is the same loop with an extra step.

That seed went from `Stuck, banked 440 at 71 s` to `Extracted, banked 2695 at 63 s`, and with both
defects gone the sweep's twelve layouts extract twelve times out of twelve. Neither could reach a
player — a person walks through the gap and does not orbit a crate — but both had been quietly voting
in every balance table this project has printed.

**"The crowd gets through" and "the player gets through" are different claims.** Enemies are not
physics bodies; they follow a flow field and collide with nothing, so a stage that watches 24 walkers
close from 34 m says only that a route exists. `BiomeProbe` now runs a separate check with the
player's own radius, to the pads that will actually open — the first version asserted all three and
failed on a correct map, because some pads are decoys the generator never promises a route to.

**The Flats measured as a smaller run rather than a different one.** First numbers gave it a wider
spawn ring, on the theory that open ground with a tight ring is an ambush rather than open ground. The
play-test came back with a third of the payout *and* half the peak crowd of the other two — no trade,
just less of everything, and nobody would pick it. What makes open ground open is that there is
nothing to break contact behind, not that the crowd starts further away; the ring went back to
neutral. Its payout still reads low against the others, and that part is left alone: the compensation
is entirely in the depth bias, the bot routes to the two nearest crates, and tuning against a bot that
does not go deep is the Phase 16 mistake with new numbers.

**A neutral value of 1 among a dozen neutral values of 0.** Gear rules were first
accumulated into a `RunModifiers`, which is the obvious container and the wrong
one: its `AreaScale` is neutral at 1, so three pieces each granting nothing summed
to a triple-size blast radius for a player in the starting kit. The accumulators
are now plain locals that all mean "what the gear adds", and `LoadoutProbe` has a
stage that asserts the starting kit grants exactly nothing.

**Two calls that each wrote only what they knew about.** `SetCaps` and
`SetRuleCaps` both wrote into one array without clearing, so a loadout's ceilings
were a delta on the previous one — a bandolier taken off still granted five
pierce. Invisible in the game, where the real caller runs exactly once per run,
and immediately obvious to a probe wearing two sets in one scene. They are one
call now, and it clears first: a complete statement rather than an update.

**The gear ceilings were correct only because of node order.** `RunGrowth._Ready`
fills the defaults and `MetaManager._Ready` overwrites four of them, so the game
was right only while RunGrowth sat above MetaManager in `Main.tscn` — and the
symptom of moving it would have been every ceiling silently reverting, which
plays *almost* right. Gear caps live in their own array now and are merged at
read time, so scene order cannot decide the answer.

**Fifteen shop rows ran off the bottom of a 1080p screen**, and the first version
of the per-item description was appended after them — shipped into the void. The
description moved above the list and the list became a window that follows the
cursor and states how many rows are hidden. A shop that silently stops listing
its last item is worse than one that admits there is more.

**Four of six weapons locked left a new profile with nothing to buy.** Two of the remaining four are
starting kit, so the shop's weapon section was entirely "owned" or "unbuyable" — a dead screen on day
one and no sink at all for what the first run pays. The fire axe went back on the shelf and its
condition kept its card, granting a growth option instead. The probe found this by printing the size
of the opening deck rather than by failing: a first stage that just asserts the locked set is
non-empty would have been perfectly happy.

**Where the boss walks in was decided by the sweep, not by the design.** 62% of the clock was the
written answer: late enough for a build, early enough not to collide with the timer. The sweep said
runs end between 83 and 142 seconds, so a boss at 186 happened once in twenty runs. A climax the run
does not reach is not late, it is absent. 40%.

**The player is an authored body now, and it is the third attempt that worked.** Seven three.js
humanoids and the Drifter's own predecessor were both deleted for being worse than the `MeshBuilder`
body they replaced. `tactical_character.glb` is better than it by every reading of the lineup:
23,822 triangles wearing a plate carrier, gloves, a holster and boots — which is what `CHARACTERS.md`
says a survivor wears and what `MeshBuilder` will never model — on a `mixamorig_` rig whose names
classify cleanly, so 3,862 leg vertices and 5,729 arm vertices swing about a hip at 1.12 m and a
shoulder at 1.74 m and it walks rather than scatters.

**What made it available was noticing which tier the player is in.** `ART.md` had three: 150 on
screen at 800–2,000 triangles, 10–20 at 2,000–6,000, and 1–2 bosses at 20,000–40,000. The player is
one body, so it belongs in the last one, and that tier is nearly free — 23,822 triangles is a third
of what the walker horde spends and the horde is not what anyone is looking at. Every intake decision
before this had been made against the horde's budget, which is why a model like this had never been
considered. **The same file also said not to search for `anime`**, on the true grounds that
VRoid-lineage models are 10,000–50,000 triangles built around their own outline pass and toon shader.
The triangle count was a horde objection applied to a body that is not in the horde, and the outline
pass and toon shader are discarded on intake like the animations and the normal maps are. This
survivor is one of those models. §5 says so now.

**`BakedBody.Append` joined five arrays and never the index buffer**, which is correct for two
non-indexed meshes and produces nothing at all for anything else. The player holds a procedural
weapon appended to the same surface; an authored mesh is indexed — that is most of why this one is
17,087 vertices rather than 70,890 — so the joined mesh had its vertices and no triangle referencing
them. Godot refused the surface with "vertex amount (17255) must be a multiple of 3" once per frame,
forever, and the player was invisible in a game that otherwise ran perfectly. It survived because the
only two meshes that had ever met there were non-indexed: `BodyMeshLibrary` builds triangle soup, and
every survivor bake before this one was thrown away for looking wrong rather than for failing to
draw. Sequential indices are what "non-indexed" means, so the mixed case is now not a case.

**A mesh hanging off a `BoneAttachment3D` is not a loose accessory, and the difference is the
author's own statement.** `BakeBody` skips unskinned meshes on a rigged model because on Quaternius
packs those are the ten weapons lying in the file — merging them produced a character wielding an axe,
a guitar, a pistol and a rifle simultaneously. But an attachment names the bone the thing belongs to,
and the class of model that uses one is large: eyes, glasses, hats and hair are routinely rigid meshes
pinned to the head rather than skinned into it. Skipping them cost this survivor its irises, so it
baked with two blank white sclerae and looked possessed at any range close enough to see a face — two
96-triangle meshes out of 23,822, which is not a number anyone notices in a report. The attachment's
bone is also the better classifier: a prop pinned to a hand bone now gets the arm swing rather than
none.

**A body is a file on a shelf, and that is the whole of "applying" a model.**
`resources/bodies/<slot>.res` is the body for that slot — `walker` through `boss` from the enemy
table, `drifter` / `courier` / `warden` from the survivor roster — and `BodyBakes` resolves it by
name. Drop a bake in and it is drawn; delete it and the procedural body comes back. `BakedBodyPath`
still wins where it is set and is empty on all twelve now.

It replaced twelve string literals in two build tools, each of which had to be edited, compiled and
re-run before anybody could *look* at a model — and looking at it is the decision. Two rounds of
authored humanoids were judged too late, and part of the reason is that seeing one in the game was a
twenty-minute errand. It is one command now: `art-src/models/intake.ps1` copies the file in, hashes
it, says whether `SOURCE.md` already knows that hash, imports, reports the tree, counts **the named
node's** triangles rather than the file's, checks the slot's tier, bakes with `slot:` — which
resolves the destination and the design height, the two arguments nobody gets right — and writes the
lineup and a screenshot of the running game.

**Four things read a bake and only two of them were reading it from the resource.** `BodyRenderer`
and `Player` were the pair anybody would think to change; `BodyProbe`, `EnemyTypeProbe` and
`BodyShot` each tested `BakedBodyPath` for emptiness too, and each was wrong in a different way once
the field went empty. `BodyProbe` predicted a *procedural* height for a baked body and failed on the
first model dropped in, which is the one moment it most needs to be right. `BodyShot` drew the
procedural body for a variant the game draws from a bake — a lineup of the thing being replaced,
captioned as the replacement, which is worse than no lineup. All five go through `BodyBakes.Resolve`.

**One polyart zombie was baked into the walker slot, measured, looked at, and held back.** As a body
it beats the procedural walker outright: a lurching posture, a bloodstained vest, a face. As a horde
variant it is a pale-skinned man, and the walker's green is a gameplay signal — variants are told
apart at 25 pixels by colour before anything else, and a hundred and fifty of these read as a crowd
of survivors. It also leaves the horde half authored and half boxes. The bake is committed as
`resources/bodies/polyart_male_c.res`, which is not a slot name; renaming it to `walker.res` puts it
in the game and deleting it takes it out, and that is the entire decision either way.

**The floor and the cover are cel shaded now, and the horde had been for a phase without them.**
`body.gdshader` banded the diffuse into two flat tones; `PropLibrary` handed every prop a
`StandardMaterial3D` and `ground.gdshader` ran `diffuse_burley`, so the arena was a smoothly-lit
floor and smoothly-lit crates under a banded crowd. The ramp, the shadow floor and the rim moved
into `cel.gdshaderinc` unchanged and all three shaders include it. A prop is the case banding is
*most* correct for: a body is flat facets approximating a limb, and a container is an actual box.

**The floor takes four bands against a body's two, and that is not a compromise.** The include
argues for two on the grounds that a body's facets quantise the light for free, so a third band
lands between two facet values and reads as noise. `GroundMesh` builds a heightmapped surface of
6,561 smooth-shaded vertices from `Terrain` — the one thing in this game where a mid tone describes
real form, and where two bands across an arena would be two continents. It also takes no rim: on a
floor the whole horizon is silhouette, and fresnel draws a bright band across the far edge exactly
where the fog is meant to be hiding things.

**RIN v2, and the interesting part is that 141 of her 143 meshes are skinned.** v1 had 38 of 57, and
the difference matters to this pipeline more than any of the modelling does: `BakeBody` skips an
unskinned mesh on a rigged model unless it hangs off a `BoneAttachment3D`, because on a Quaternius
pack those are the ten weapons lying loose in the file. A model whose trim is skinned needs nothing
explained to it — eleven ponytail locks, crimson underlocks, boot laces and hooks, belt loops and
rivets, thigh quick-releases and the jacket's zip tape all came through on the first bake.

The rifle also left the character, which removes a small absurdity: the game appends the silhouette
of whatever weapon is equipped to the player's own mesh, so v1 carried a stowed rifle *and* a drawn
one. And the two crimson underlocks are a legibility gain rather than a detail — from behind, which
is how the player is seen for an entire run, they read as two red stripes down her back at any range
she is visible at, which is the one saturated thing on a body the chroma rule calls grey.

**Doubling the player's triangles cost 0.02 ms and 1.3 MB, and only one of those is a real number.**
27,488 to 51,353 moved the frame mean from 1.30 ms to 1.32, which is noise — one body does not
register against a horde spending between 92,000 and 4.7 million triangles. The bake went from 1.6 MB
to 2.9 MB, and the bake is the half that is committed, because the source cannot be. That is the
ceiling on a survivor and it is why `ART.md §2` now puts the player tier at ~120,000 triangles
instead of at 40,000 — the old number was a horde rule wearing the player's name.

**Five survivors, and the roster went from a keypress to a screen.** RIN, MIKA, AKIRA, SORA and YUNA
arrive as one production with models, eighteen to twenty-one named animations each, and a design sheet
apiece. The three existing survivors kept their numbers to the digit and took the first three names —
`CharacterBook.Order` is a fixed list and the profile stores an *index*, so renaming entry zero from
Drifter to RIN leaves every saved profile pointing at the same hundred-health survivor. SORA and YUNA
are the Scout and the Revenant `CHARACTERS.md` had already designed and costed, under new names.

**The note that argued against a select screen was right and is now out of date.** It said choosing a
survivor is not a separate act from equipping one — "the Warden's fourteen bulk changes what is worth
buying and the Courier's twenty-eight changes it the other way" — which is why the roster opens *at
the gate*, over the shop screen, and closes back onto it. What changed is that cycling was right for
three lines of text and is a carousel for five characters with illustrations. The same note had
already admitted this about the biomes two paragraphs later.

**The ability list was hand-copied into four places and adding two survivors would have made it
six.** `CharacterResource`'s fields, `Player.ApplyCharacter` and three stages of `CharacterProbe` each
enumerated the same four abilities, so the probe stages that check "the default carries no ability"
and "this survivor gains something" would have gone on passing while ignoring the two new ones. There
is one `GrantTo` now, plus a `HasAbility` that measures by granting to a fresh `RunModifiers` and
asking whether anything moved — rather than testing each field again, which is the same list a third
time and the same way for it to go stale.

**`BodyShot -- roster` was drawing five procedural bodies while the game drew five bakes.** The horde
path had been fixed to read the shelf a phase earlier and the roster path had not, so the picture that
answers "does this roster read as five people" was of five things that are not in the game. It reads
the shelf now, and `raw` is how to ask the other question.

**The licence is known, so the attribution surface stopped being optional.** Three phases of
`SOURCE.md` carried "not recorded" for the author and licence of the body the player controls; the
LAST DAWN production ran it down to *Game Ready Low Poly Tactical Character* by DanlyVostok, **CC BY
4.0**, with the evidence in the file's own `asset.extras` rather than in a claim. `ART.md §6` had said
since it was written that CC BY needs the credit to reach the *player*, that this game had no surface
for one, and that this was an unscheduled dependency. It is on the roster screen, which is where those
five illustrations and the bodies under them are on display — not decoration there, and a rewrite that
drops those two lines is a licence breach rather than a formatting change.

**LOD1 rather than LOD0, and the constraint is the repository rather than the renderer.** Each
character ships three exports; LOD0 is 81,359 triangles and Performance below says one body's
triangles do not register. It is the committed *bake* that costs, because the source is not committed:
five LOD1 bakes are 11 MB and five LOD0 bakes would be nearer twenty.

**`[A]`/`[D]` strafe now, and it deleted a geometry problem rather than tuning one.** They turned the
view for eleven phases, which is a coherent scheme and the wrong one for a camera the mouse drives:
two controls that both change the heading fight each other, and the player ends up steering by the
difference between them. Under mouse-look the keys have to mean translation and only the mouse means
direction.

What it removed is bigger than what it added. Turn-and-advance moves at v while turning at ω, so the
tightest arc it can trace has radius v/ω — 6.0 m/s against 150°/s is 2.29 m — and **anything inside
that circle cannot be walked to at all, only orbited**. `BotDrive` exists entirely because of it and
opens by saying so; two of `BalanceSweep`'s twelve seeds spent sixty seconds circling a crate 2.3 m
away with the flow field pointing straight at it, and it was read as a level bug for three phases
because every diagnostic agreed the route was correct. Four independent keys have no turning circle.
`BotDrive.Steer` is now a projection onto two axes instead of forty lines of turning geometry, and its
`turnRadius` parameter is documented as dead rather than tuned.

**`MovementProbe` had to be rewritten and its new third assertion is the one that matters.** It
checks that `[D]` *translates* along the view's right — and that it does not turn the view, which is
what says the old scheme is gone rather than merely switched off. Under turn-and-advance that leg
yawed the rig 112° and moved the player almost nowhere. The orbit leg stays and is now easy, which is
the point of keeping it: it is what would notice if anything ever put turn-and-advance back.

**The camera pitches, between 50° and 10° above the horizon, and the rest angle is read off the scene
rather than restated.** `BuildMain` builds the camera at −26° and the rig takes that as its resting
pose, so a run nobody touches the mouse in is framed exactly as every screenshot in this file was.
The limits are what stops the mouse breaking the framing: past 50° it is the top-down orthographic
view this game deliberately left, and above 10° the horizon rises far enough that the fog stops
hiding the arena's edge.

Rebuilding the camera offset from a distance and an angle was the first attempt and `CameraProbe`
caught it: the scene's camera sits at `(0, d sin t, d cos t)` from the rig's origin and not from the
pivot, so reconstruction moved the resting shot a few centimetres and the "pulling in changes the
distance and nothing else" check read 0.996 against its threshold. Pitching rotates the real offset
about the pivot, which is exact at rest by construction.

**The five characters came back rebuilt against `ART.md` §10, and four of the five asks landed.**

**The eyes are fixed and it took geometry.** Per-vertex sampling cannot reproduce a painting a few
millimetres across inside a UV island, which is why every survivor had blown-white sclerae for three
phases; the iris, pupil and highlight are separate meshes bound to `Head` now, and the sampler has
something to sample. This is the defect that had been carried longest and it closed without a line of
engine code.

**`BakePose` removes the last guess from an intake.** One frame, arms down, empty-handed, in all five
files — so the pose argument is a name rather than a search. Every earlier survivor's frame was found
by trying three.

**Emissive renders, and three of five characters use it.** Vertex alpha 0 on RIN's backpack stripes,
MIKA's sleeve modules and YUNA's cross bars; AKIRA and SORA have none, because their greatsword and
flying swords are separate equipment files rather than parts of the body. It is the cheapest visual
return in the pipeline and it is still mostly unclaimed.

**The colour ask was answered and my target for it was wrong.** I asked for one region of identity
colour big enough to move the mean past `PaletteProbe`'s 0.35, and got exactly that — red jackets,
a blue coat, a green medical shell, violet — and **four of the five means moved closer to the
horde**, because a saturated red averaged with a face and black cloth is a desaturated warm grey. A
body's mean cannot reach 0.35 while a face is in frame, and `CHARACTERS.md` had already said so
before the ask went out.

So `BakeProbe` prints a second number: **what share of the body sits past 0.35 from everything in the
horde**. RIN 12.6%, MIKA 12.0%, AKIRA 26.0%, SORA 25.2%, YUNA 9.8% — and the stalker, a horde body
used as a control, 0.0%. That control is why the number is trustworthy, and it is the standing
request now. There is no threshold on it: five samples between 9.8% and 26.0% is not enough to set
one.

**Strafing left the survivor facing the wrong way and firing there, and forty-seven probes passed
it.** `UpdateFacing` branched on `TurnToSteer`: with it on the body faced the view, and with it off —
the new default — it faced `_input.Move`, which is *screen space*. `move_right` is `(1, 0)` whatever
the camera is doing, so the survivor pointed north-east because a key said so while the view pointed
somewhere else, and the weapon fired along the key rather than along the aim, because `Facing` is what
`Aim` reads.

**The second half of the same bug was an aim source that had been dead for eleven phases.**
`KeyboardMouseInput.Aim` projects the cursor onto the player's ground plane, and `CameraRig` had
already written down why that stopped working — "the world sweeps beneath a stationary cursor as the
view comes round, so the player spins while the hand holding the mouse is still". The camera stopped
reading it. The input source went on computing it. The only thing keeping it out of the game was
`UpdateFacing` returning early, so turning that early return off did not introduce the defect, it
exposed it — and with the cursor now captured, the projection is of a pointer parked wherever it was
grabbed. Both ends are gone: the facing is the view, and `Aim` is `Zero` until a real aim device
arrives on it.

**Neither would have been found by a probe, and `test/GaitShot.cs` is what found them.** It holds a
movement key and photographs the player's gait as a strip of frames under the game's own camera,
printing the angle between the direction of travel, the body's facing and the view's right. That
print is the whole tool: strafing has to read *90° off the facing and 0° off the view's right*, and it
read 168°/0° — which says in one line that the movement is right and the facing is wrong. `MovementProbe`
measures travel against the view and never had an opinion about the facing.

**And the moonwalk, which is what the tool was actually built to judge, is much milder than predicted.**
`body.gdshader` swings a limb about a hip pivot and fore-and-aft is the only axis it has, so a body
strafing at 90° swings its legs along an axis it is not travelling on. Photographed against the
forward walk frame for frame, the two gaits look nearly the same: from behind at a 26° downward tilt
the swing foreshortens into a few pixels of vertical scissor, the legs are black over black boots
against a dark ground, and the motion cue the eye is actually taking is the sliding ground — which is
correct either way. So no shader change: a side-swing channel needs a vertex channel and the channels
are full, and facing the movement instead of the view would break aim to fix something almost
invisible. Recorded as measured and accepted.

## Performance

RTX 3070 Ti, 1080p, vsync off, player moving so the field actually rebuilds.

**The triangle count of a body is not what the horde costs, and this is the
measurement that says so.** Same scene, same session, 200 walkers, one variable —
which body is on the shelf:

| 200 walkers | Triangles each | Total | Mean | Median | p95 | Draw calls |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| procedural | ~460 | 92,000 | 1.64 ms | 0.90 ms | 2.03 ms | 70 |
| authored, Polyart | 1,650 | 330,000 | 1.44 ms | 1.28 ms | 2.45 ms | 68 |
| authored, tactical | 23,822 | 4,764,400 | 3.41 ms | 3.12 ms | 5.19 ms | 76 |
| 500 mixed, procedural | ~460–570 | ~250,000 | 1.23 ms | 1.01 ms | 2.13 ms | 91 |
| 500 mixed, idle, 10 s warm-up | ~460–570 | ~250,000 | 1.13 ms | 1.00 ms | 2.08 ms | 89 |
| 500 mixed, **fighting**, 10 s warm-up | ~460–570 | ~250,000 | 1.51 ms | 1.25 ms | 2.77 ms | 131 |

And the player, which is one body and therefore the cheap half of every one of those rows:

| 200 walkers, procedural + this player | Triangles | Mean | Median | p95 | Draw calls |
| :--- | ---: | ---: | ---: | ---: | ---: |
| RIN v1 | 27,488 | 1.30 ms | 0.91 ms | 2.03 ms | 70 |
| RIN v2 | 51,353 | 1.32 ms | 0.95 ms | 1.94 ms | 70 |

Doubling the player's triangles moved the mean 0.02 ms. What it did move is the committed bake:
1.6 MB to 2.9 MB, which is the real ceiling on a survivor and the reason `ART.md §2` puts the player
tier at ~120,000 rather than at whatever the GPU would take.

3.6x the triangles cost nothing measurable. **52x the triangles cost 2.1x the
frame time and still held 293 fps**, which is a body forty times over the budget
`ART.md` used to publish, at 200 instances. The tier table there has been
rewritten around these numbers.

**A saturated combat frame costs 0.38 ms of mean and 44 draw calls, and holds 662 fps.** The fight row
is `HordePerf -- 500 mixed fight warmup:600`: it fires the weapon every frame, detonates twice a
second, sets an eighth of the field alight, and re-spawns whatever it killed so the body count under
measurement does not drain away. By the time it samples, every pool is at or near its ceiling — **224
puffs of 224**, 59 marks of 96, 38 corpses of 40 — which is what makes it a ceiling rather than a
typical frame. A run never explodes twice a second.

The draw calls are where the cost actually is: 89 idle against 131 fighting. Two extra puff passes and
the ground-mark pass account for three of those; the rest is the blast lights, which are cheap per
light and are not free. Corpses cost nothing in calls at all — they are extra instances in buffers the
horde already uploads, which is the whole reason they are written into the horde's own MultiMeshes
rather than their own.

**Both rows are taken at ten seconds of warm-up, and that number is not decoration.** At the old one
second, six runs alternating idle and fight came back at either ~1.35 ms or ~3.1 ms *with no relation
to which mode was running* — a 2.3x spread between two runs of the same command, which is far larger
than anything either mode costs. Two runs of `fight` differed by more than `fight` differs from `idle`.
That is a GPU that has not finished clocking up, and a number taken during it is a number about power
management. `warmup:N` exists now; the default is still 60 frames so every row above keeps meaning what
it meant, and every row worth quoting from here is taken at 600.

**This is also the row that says the pools were the right shape.** The puff pool saturates and the
frame time does not care, because a saturated pool is exactly what "fixed ceiling" buys: the cost of
the effect system is bounded by a constant chosen before it was measured, and a worse fight cannot
spend more than this.

**The number this table used to give was 6.90 ms and it measured no rendering at
all.** `HordePerf` was documented and run as `godot --headless --script
test/HordePerf.cs`, and the dummy display driver draws nothing: all four rows
above report 6.90 ms and 145 fps to the decimal under it, with `avg draw calls 0`
printed underneath, which reads as a MultiMesh triumph rather than as an empty
frame. The file's own doc comment said "not headless" for eleven phases, which is
the same "a comment is not a check" that `test/Display.cs` exists for — and it
now calls `Display.Required` like the five capture scripts do.

The older sprite-path measurements, kept because the billboard fallback is still
shipped and still has to hold up:

| 500 enemies, sprites | Mean | Median | p95 | Draw calls | GC |
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

**Re-taken on 2026-09-09, under the control scheme the game actually has.** Every table below the
divider was measured by a driver that could not strafe and could not reach two of the twelve layouts,
and the README said so three times without anyone doing it. What follows the divider is the old set,
kept because the *comparisons* in it are still the right comparisons; what follows immediately is what
the same instrument says today.

### The four tiers, twelve layouts, starting kit

`godot --headless --script test/BalanceSweep.cs`, 48 runs, Rail Yard.

| Loiter | Walked out | Banked, per attempt | Banked, if you get out | Median death | Worst peak | Median lowest HP |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 s | 12/12 | 507 | 507 | — | 160 | 96 |
| 60 s | 12/12 | 1337 | 1337 | — | 160 | 58 |
| 120 s | 10/12 | **1713** | 1754 | 107 s | 160 | 56 |
| 180 s | 2/12 | **380** | 2306 | 150 s | 160 | 0 |

**Two columns because one of them lies, and it is the one this instrument has printed for four
phases.** `median banked` was computed over survivors only. At 180 s that is the median of the two
runs out of twelve that walked out — 2306, which reads as the best payout in the table and is in fact
a statement about the two that made it. Counting every attempt at what it actually banked (a dead run
keeps whatever was secured into the safe box, and nothing else), the same tier is **380**: the worst
row here by a factor of four. A player takes attempts, not survivors. Both columns are printed now.

**The peak moved from 60 s to 120 s, and the supply caches are why.** The old reading of this curve —
payout peaks at 60 s and collapses after — is no longer what the game does: 507 → 1337 → **1713** →
380 per attempt. Staying to two minutes is now the best decision on the board, and it is the second
cache landing at 58% of the clock that pays for it. The third minute is where it falls off, and it
falls off through *death* rather than through a spent bag: ten of twelve die, at a median of 150 s.

**Nothing about the survival wall is gentle.** 12/12, 12/12, 10/12, 2/12. The median lowest health
goes 96 → 58 → 56 → 0. There is no tier where the player is merely uncomfortable; the run is either
comfortably survivable or it is a coin toss you lose.

### Given the choice, the bot leaves before the second half

`lingers:auto`, twelve layouts, and the answer depends on what it is holding.

| Weapon | Walked out | Banked, per attempt | Median run | Longest | Median lowest HP |
| :--- | ---: | ---: | ---: | ---: | ---: |
| starting kit | 23/24 | 944 | 60 s | 135 s | 59 |
| Service Rifle | 11/12 | 1521 | 117 s | **180 s** | 54 |

**On the starting kit the 180 s target is not met, and the entry claiming it was is about a different
weapon.** The bot given the choice leaves at around a minute with the kit and at around two with the
Service Rifle, and only the Service Rifle arm produced a run that reached three minutes and walked out.
That is not a contradiction of the older reading — it recorded 158 s *on the Service Rifle* — but the
sentence had lost the weapon by the time it reached the roadmap.

It also means an `auto`-only sweep exits 1 on the starting kit, every time, because the verdict asks
whether anything reached 180 s and `auto` treats itself as capable of it. The failure is real and it is
about the kit rather than about the sweep.

### A pair is 133% of one weapon, and the budget says 115%

`lingers:auto slots:both`, twelve layouts, 24 runs.

| Firing | Walked out | Banked, per attempt | Median run | Median lowest HP |
| :--- | ---: | ---: | ---: | ---: |
| one weapon | 11/12 | 927 | 59 s | 59 |
| two weapons | 12/12 | 1233 | 60 s | 59 |

**133%.** The rule is "a pair inside about 115% of one weapon"; it measured 110% when the sidearm slot
was reworked and 138% four phases later. It is 133% now, on numbers nobody moved on purpose since —
so the drift did not continue, and it did not come back inside the budget either. The second slot is
still worth a third of a run rather than a seventh, and that is the number to move the next time a
weapon changes.

---

**Twelve of twelve layouts extract on the starting kit, and two of them never used to arrive at
all.** Both failures belonged to the driver rather than to the game — a turning circle it could not
close inside, and an escape rule that undid its own progress; both are in Decisions. Between them they
had been contributing two zeroes to the survival column of every balance table this project has
printed, for reasons that had nothing to do with what was being measured.

| Seed | | Seed | | Seed | |
| :--- | ---: | :--- | ---: | :--- | ---: |
| `0x51E5D0A7` | 757 at 46 s | `0x9E3779B9` | 598 at 116 s | `0xC17E4A9B` | 2315 at 155 s |
| `0x2545F491` | 1337 at 47 s | `0xBF58476D` | 2449 at 113 s | `0x94D049BB` | 1215 at 70 s |
| `0x1B873593` | 349 at 68 s | `0x85EBCA6B` | 664 at 135 s | `0xCC9E2D51` | 568 at 54 s |
| `0x27D4EB2F` | **3166 at 167 s** | `0x165667B1` | 1345 at 38 s | `0xD6E8FEB1` | **2695 at 63 s** |

Median 1276 banked at 69 s, 12/12 out. The two bold rows were `Stuck, 120 at 70 s` and
`Stuck, 440 at 71 s`; the other ten moved too, because a routing change moves every route.

The tables further down that section are still the older set. The three above the divider replace what
they were about; the ones this instrument does not produce — the terrain comparison, the weapon rows,
the survivor rows — are still the right shape and the right comparisons, and are still owed a re-take.
**Re-take the one that is about to settle something before it settles it.**

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

**That calibration is also what melee is priced against, and it is why melee is behind.** What a melee
weapon buys is that it can never run out — and the starting rifle does not run out either, so the
insurance is against something the design already removed. Measured on a long run: the Scavenged Rifle
never went dry across 150 seconds, and the Reaper Scythe banked 1217 against its 2870. The one weapon
that does empty is the Service Rifle, at 149 s, because seven rounds a second is what 320 of them
costs — a real cost, on a real weapon, invisible on any run shorter than two minutes.

**Cover makes the horde accumulate.** 206 enemies alive at 120 s where the old open arena held 55: a
crowd that has to route around fifty blocks arrives slower than it spawns. The field the player is
kiting through is denser than the same run used to be, and the reason is the map, not the rate.

### The baseline

Twelve cells — four loiter tiers across three terrains, one seed, the bot from `test/AutoPlay.cs`.

| Loiter | Rail Yard | Old Town | The Flats |
| ---: | :--- | :--- | :--- |
| 0 s | 421 at 26 s | 339 at 24 s | 332 at 32 s |
| 60 s | **987** at 74 s | **978** at 74 s | 383 at 71 s |
| 120 s | 392 at 130 s | 492 at 134 s | 378 at 133 s |
| 180 s | 479 at 193 s | **died** at 147 s | **456** at 192 s |

**These are not the numbers this table had a phase ago, and the game did not change — the driver did.**
The bot used to route to `found[0]` and `found[1]`, whichever two crates the generator happened to
place first, and during the linger phase it walked to the *nearest* unlooted crate. Both rules are
uncorrelated with where the value is: `RarityBias` runs from 1 at the spawn to the biome's depth figure
at the edge, so "nearest" is a rule for systematically collecting the cheapest loot on the map. Every
balance number this project has ever printed came from a bot that could not see the one mechanic the
level generator is built around.

It picks by worth-per-metre now, and The Flats — which existed to reward going deep and measured as
the worst terrain in the game — climbs with time instead of falling. That column was left untouched at
Phase 26 on the grounds that it might be the bot. It was the bot.

**The bot is not uniformly better at surviving, and that is correct.** Old Town now dies at 180 s where
it used to walk out at 187 s: going deep in a dense biome means being far from the pad when it goes
wrong. It takes more risk for more value, which is what a player does, and a driver that never took a
risk was measuring a game nobody plays.

### The second minute

The payout used to peak at 60 s and collapse. The mechanism was measurable: the bag holds 528 at 60 s
and 40 at 120 s — not capped, *spent*. Every valuable thing in the backpack is also what keeps you
alive, so surviving the second minute converts the payout into survival, and the extraction
multiplier's 1.0 → 1.56 cannot buy back ninety percent of a bag. Loot is fuel, and the horde's growth
outruns what the map was stocked with.

**Arithmetic could not fix that.** Covering a spent bag with the multiplier would need it somewhere
past 3x, which makes leaving late simply correct and deletes the decision the multiplier exists to
create. The answer had to be supply. Two caches land during the run — at 25% and 58% of the clock, 26 m
out, announced — using the same object the boss already drops.

Four seeds, four loiter tiers, banked credits, before and after:

| Seed | 0 s | 60 s | 120 s | 180 s |
| :--- | ---: | ---: | ---: | ---: |
| `1374015655` | 421 | 987 | 392 → **1214** | 479 → **1498** |
| `3246279323` | 518 | 896 | 832 → **1324** | 853 → **1735** |
| `2654435769` | 335 | 1911 | died | died |
| `625341585` | 481 | 1023 | died | died |

**Those numbers were taken with the caches biased at 2.4, and that was wrong.** `RarityBias` multiplies
an item's draw weight once per rarity step, so 2.4 makes a treasure chest: it rolls serums and circuit
boards, the only two entries in the table with no use at all. The payout curve went up beautifully and
did nothing for the problem — a run diagnosed at 144 s was dry since 69 s and died holding 640 credits
of loot it could not spend on anything. Naming a thing "supply" does not make it one. They are at 1.4
now, where rounds and canned food are the heaviest entries and medkits are reachable, and the payout
above is correspondingly lower.

**Fixing the payout did not fix survival, and the two are separate levers.** Four of eight seeds reach
180 s. Recording both is the point.

### Why the second half kills you

Not the crowd. A death at 144 s, read off the ten-second trace: **dry since 69 s**, weapon at level 0
of 8 after five picks, and a bag holding 640 credits of loot with no use — circuit boards and serums,
the two entries in the item table you cannot spend on staying alive. The horde goes from 35 enemies at
10 s to 160 at 100 s while the weapon's whole climb is 12 damage to 17.8, and `GrowthProbe` puts that
climb at eleven picks. Enemy throughput grows about 4.5x; player damage grows about 1.5x, and only if
the deck cooperates.

**Three things were tried against that and two of them made it worse.** Written down because a phase
that only records what worked is a phase that will make the same mistakes again:

- **Teaching the bot to fight the boss.** It guards the richest thing in the run, so the reward for
  staying past 40% of the clock is behind it. Walking to it died in twenty seconds (26 contact damage
  a second, and the orbit target was its exact position); holding at 13 m died anyway, because it
  arrives at the same moment the horde reaches its cap; engaging only while healthy with fewer than 25
  things close turned a seed that banked 1735 into a death at 114 s. Fighting it wants kiting and
  cover, which is combat AI and not a target-selection rule. **So the boss cache is content no
  measurement here can reach, and the payout for staying past two minutes is _unverified_ rather than
  verified-as-bad.**
- **Preferring damage cards.** The bot's preference list was written in Phase 8 and never learned about
  pierce, crit, fire rate or area, which arrived in Phase 18 — so it looked stale, and damage-first
  looked like the obvious correction. Eight seeds, 180 s linger, same map each time:

  | Ordering | Walked out of 8 |
  | :--- | ---: |
  | original list, random fallback | **4** |
  | damage options as the fallback | 3 |
  | damage options first | 2 |

  Monotone in the direction of "more damage, fewer survivors", and small: four against three is one
  seed. Not a result to build on, but nothing beat the list that was already there, so it stayed.
  The mechanism is at least coherent — damage converts into survival only if you can use the range it
  buys, and this bot cannot dodge or kite; it walks to a point and stands there. Its max health came
  out at 100–124 on the damage-first runs against 136–148 on the originals. For an agent with no
  movement skill, health and armour *are* its damage cards, and even the random fallback was picking
  them more often than a damage-first list did.

  **A real player's ordering is almost certainly the opposite**, which is the most useful thing to know
  about every balance number in this file.
- **Fixing the cache contents.** This one worked, and it is the change that shipped.

**The first schedule was 46% and 72% and it was wrong for a reason worth writing down.** Evenly spaced
is tidy; 46% of a 300 s run is 138 s, comfortably after the window the drop was written to fix, and a
run that ends at 130 s never saw one at all. Placed against the measurement instead — the bag is full
at 60 s and empty at 120 s, so the first cache lands at 75 s, inside the window where the opening haul
is being spent.

The bot now paths with a flow field of its own rather than steering straight. On a hand-made arena
with five blocks that was enough; on a generated one it walked into the first wall between it and the
crate and reported the route as blocked. A player looks at the screen and goes around, and the
closest thing to that this project already owns is the field.

**`FlowField.BlockBox` marked every footprint a full cell too large in all four directions**, and had
since it was written. A cell index is the cell *containing* a coordinate, so the last cell a box
overlaps is `floor` of its far edge; the code used `ceil`, which names the cell after it, and the loop
is inclusive. At a 1.5 m cell that is up to 1.5 m of phantom wall per side on top of whatever the
caller asked to inflate by — invisible, because a route that goes slightly too far around still gets
there.

Two things it had already caused, both of which had been explained as something else:

- **`AutoPlay` argued itself down from 0.9 to 0.55 of body-radius inflation** because "0.9 either side
  turns a 2.2 m doorway into no doorway". True, and the field was quietly adding two to three times the
  number under discussion. The constant was tuned against the bug.
- **One seed in twelve returned `Stuck — banked 0` on every arm of every balance table this project
  has printed.** Seed `3432918353`: the bot stood 2.45 m clear of the nearest real obstacle, inside a
  footprint that was not there, two metres from a crate it needed to be 1.8 m from, with `Sample`
  returning zero. It now extracts at 52 s with a peak of 160 enemies instead of 33 — it plays the
  level rather than jittering in a corner.

The fix changes enemy routing as well as the bot's, so every balance number moves with it.

`AutoPlay.Navigate` had a second half of the same failure. `EscapeFrom` outranked the flow whenever
the bot stood in a footprint, which is right when the goal is across the map and wrong when it is two
metres away: escaping walks away from the crate, the flow pulls it back, and the loop is indefinite.
Its own comment named that oscillation as "a third thing again" and nothing acted on it. Inside
`FinalApproach` — 2.6 m, past a crate's 1.8 m reach and far short of the 7.5 m wall-lean the escape
was written for — the straight line is the answer, because there is nothing to route around at that
range and the collider resolves any real overlap.

That was the *arrival* half. The same loop happens in *transit*, nineteen metres out, where the field
is still the authority and the margin only has to be crossed — and it needed a different answer, which
is `RouteMemory` further down.

**And they were measured with the starting kit, every one of them, until the weapon arm existed.** A
play-test runs on a fresh ephemeral profile for the reason below, and a fresh profile owns the
Scavenged Rifle and the Combat Knife — so every number this file printed before that arm was about two
weapons out of nine, and the shop was a thing the balance table had never once looked at.
`AutoPlay -- weapon:<file>` carries one; `BalanceSweep -- weapons:a,b,c` makes it a column. Both
report **what the run actually carried** rather than what was asked for, which is the rule `zoneTier`
paid for in C3.

**A fixed linger is a control that blindfolds.** The bot left at whatever second the flag said,
healthy or not — so a weapon whose whole value is that the run stays calm had nowhere to put it, and
the Service Rifle finished on 93 health against the starting rifle's 70 and banked *less*. That is not
a result about the rifle. `linger:auto` stays while the run is going well and leaves at 0.6 health,
and it is what first carried a run past the 180 s the sweep has always been judged against.

Twelve layouts on `lingers:auto`, median banked: **Service Rifle 1850, Scavenged Rifle 1844**, Reaper
Scythe 1166, Pump Shotgun 946, Fire Axe 673, Marksman Rifle 542. The paid rifle and the free one are
level to a third of a per cent and differ in how they get there, which is what the shop is supposed
to sell. Given the choice, four of the six leave the field in under two minutes and two of them at 56
seconds — a gap that was invisible while every weapon was made to stand there for the same length of
time.

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
| `assets/sprites/bolts/*.png` | `BuildProjectileSprites.cs` | 6 × 160×40 | slug, arrow, lance, pellet, charge, spit |
| `assets/sprites/blob_shadow.png` | generated | 128×128 | ground decal |
| `assets/shaders/horde_billboard.gdshader` | hand-written | — | horde + projectiles |
| `assets/shaders/vignette.gdshader` | hand-written | — | full-screen damage tint |
| `assets/shaders/ground.gdshader` | hand-written | — | tiled floor, tinted per grid cell, cel shaded at four bands |
| `assets/shaders/prop.gdshader` | hand-written | — | cover and scenery: vertex colour, cel shaded |
| `assets/shaders/cel.gdshaderinc` | hand-written | — | the ramp, the shadow floor and the rim, included by all three |
| `assets/shaders/effect.gdshader` | hand-written | — | the additive half of the puffs: flashes, sparks, fire, blasts |
| `assets/shaders/effect_soft.gdshader` | hand-written | — | the blending half: smoke, powder, the spatter a kill throws |
| `assets/shaders/effect_body.gdshaderinc` | hand-written | — | everything the two share — the billboard rebuild, the spin, the shape lookup |
| `assets/shaders/ground_stain.gdshader` | hand-written | — | what a fight leaves on the floor, laid to the ground's own normal rather than billboarded |
| `assets/shaders/ground_marker.gdshader` | hand-written | — | burning ground and the extraction ring |
| `assets/textures/ground.png` | `art-src/textures/ground_raw.png`, via `make_ground.py` | 1024×1024, tileable | 4.5 m tile |
| `assets/textures/skin_infected.png` | `art-src/textures/skin_infected_raw.png`, via `make_body_skin.py` | 1024×1024, tileable | walker, runner, spitter, stalker |
| `assets/textures/skin_mutant.png` | `art-src/textures/skin_mutant_raw.png`, via `make_body_skin.py` | 1024×1024, tileable | brute, bloater, bulwark, boss, lantern |
| `assets/textures/skin_survivor.png` | `art-src/textures/skin_survivor_raw.png`, via `make_body_skin.py` | 1024×1024, tileable | every survivor |
| `assets/textures/body/*.png` | the skin plates plus a painted face, via `make_body_atlas.py` | 6 layers × 3 categories, 512×512 | the body atlas, stacked per category |
| `assets/ui/portraits/*.png` | the roster design sheet, via `art-src/ui/cut_portraits.py` | 5 x 292x619 | the survivor select cards |
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

Everything the player looks at has had a pass, the numbers behind it are recorded above, and 48 probes
say the systems do what they claim. What is left is almost entirely **things that need a device or a
person**, not things that need code.

**This list was read against the code on 2026-09-09, and six of its entries were wrong.** Not
subtly: the audio bus had grown a limiter, `physics_ticks_per_second` had been pinned, the 4.7.1
export template had been installed, the proof video had gained elites, a boss and music, one bullet's
headline was contradicted by its own second paragraph, and the probe count in the sentence above said
45. Every one of those was fixed by a phase that did the work and did not come back here to say so.

That is worth a paragraph rather than a quiet correction, because a stale roadmap is worse than no
roadmap: it is a list of things to do, and six of them were already done. The next phase reading it
would have spent a day fixing what was not broken and would have found out only by looking, which is
exactly the failure mode this file exists to prevent everywhere else. **Re-read this section against
the code before starting anything from it.** It is half an hour and it has now paid for itself once.

- **Three of the balance tables are re-taken; the rest are not.** The four-tier sweep, the auto arm
  and the pair budget were re-measured on 2026-09-09 under the control scheme the game actually has
  and with the driver that can reach all twelve layouts — see Balance, above the divider. What is
  still owed is everything `BalanceSweep` does not produce on its own: the three-terrain comparison,
  the per-weapon rows, and the survivor rows. Those are still the right *shape* — the comparisons
  between weapons are between weapons — and every absolute second in them was measured by a driver
  that could not strafe. **Re-take the one that is about to settle something before it settles it.**
- **The pair budget is 133% against a rule of 115%, and it is a design decision nobody has taken.**
  Re-measured 2026-09-09: 927 banked per attempt on one weapon against 1233 on two. The rule is "a
  pair inside about 115% of one weapon"; it was 110% at the step-1 rework, 138% four phases later, and
  133% now — so the drift stopped and the excess did not go away. A probe cannot own this: it is a
  twenty-minute play-test, not an assertion, and it is caught only when somebody runs
  `lingers:auto slots:both` by hand. What is left is not the measurement, it is deciding whether to
  move the second slot down or to move the rule. See `WEAPONS.md`.
- **The horde is the only half of the cast still procedural, and holding it there is a decision.**
  Eight authored bodies were deleted before the first one that worked: seven horde
  variants cut in three.js, and the Drifter's predecessor cut from the blend below. Stood in a row by
  `BodyShot` every one was worse than the `MeshBuilder` body it replaced — the runner came apart into
  scattered sticks, the boss wore its head and both arms detached from the shoulders, the bloater lost
  its legs and was a bare ball, and the Drifter had no hands and its arms welded to its torso. The
  baker was never at fault: `screenshots/bake_vs_raw.png` shows a bake reproducing its input exactly.
  The models were bad, and the ninth is not — see Decisions.

  **All five survivors are authored bodies now and the horde is the only split left.** The roster
  arrived as one production — RIN, MIKA, AKIRA, SORA and YUNA at 38,815 to 45,816 triangles — so what
  was "one survivor and two placeholders" is five people with a height spread from 2.02 m to 2.24 m.
  It cost two files per survivor rather than a modelling project, because the shelf is a directory.

  **The horde has the same split available and it is held back on purpose.** One polyart zombie
  bakes into the walker slot and looks better than the procedural walker; 150 of it read as a crowd
  of pale survivors rather than as infected, because colour is how a variant is told apart at 25
  pixels. See Decisions and `assets/models/SOURCE.md`.

  `CharacterResource.BakedBodyPath` and `EnemyTypeResource.BakedBodyPath` still work and are still
  read — `Player.CreateBody` loads the bake, appends the held weapon's procedural silhouette to the
  same surface, and falls back to `SoloBody` when the path is empty or the bake will not build; an
  empty path *is* the procedural path. **Two bakes are pointed at**: the stalker, because a quadruped
  is a silhouette `MeshBuilder` cannot express, and the Drifter. Three dead `.res` files are still on
  the shelf — `kenney_blocky_a`, `kenney_survivor_a`, `polyart_male_c` — and `BakeProbe` checks
  whatever is in the directory rather than a list, so they are checked and nothing loads them.

  **The new one has the same provenance gap as the old one and it is not fixed, only bounded.**
  `assets/models/SOURCE.md` records the sha256 and two blank fields — no page, no licence — so the
  3.4 MB `.glb` is in `.gitignore` and the 1.2 MB bake is committed, which is the call every licence
  in `ART.md §6` permits and none forbid. This is the one entry in that register that fails its own
  checklist. Ask whoever downloaded it for the URL.

  What blocks re-authoring the horde is provenance too. `art-src/models/build_roster.py` cuts all seven
  from `art-src/models/base/rigged_anime_girl_cc0.blend`, and that file is 10 MB of third-party
  geometry whose only claim to a licence is its own filename — no URL, no hash, nothing anyone can
  check. CC0 is a dedication to the public domain, so nothing here is a licence breach; what is
  missing is the project's own record, which is the gap the OFL font had and has the same fix: an
  `art-src/models/base/SOURCE.md` on the pattern of `art-src/fonts/SOURCE.md`, carrying the URL and
  the sha256. **Only the source is unverifiable, and only the source is large**, so the Drifter's
  176 KB output is committed while ten megabytes of unattributable binary stays out of the history
  until that file exists. Nothing loads that output now, which makes the gap a smaller one than it
  was: it blocks a *better* body arriving, not the body the game draws.

- **The APK has never been built, let alone run.** Blocked on **two** installs this machine does not
  have: an Android SDK and a JDK. `java` is not on the path and there is no SDK at the default
  location. The third blocker this entry used to name is gone — `4.7.1.stable.mono` is in
  `%APPDATA%/Godot/export_templates` alongside the old 4.6.3, and has been for long enough that
  nobody noticed. `export_presets.cfg` is written and committed — arm64, landscape locked, no
  permissions, `art-src/` excluded — so with those two in place it is one
  `godot --headless --export-debug "Android"`. A preset that has never produced an APK is a plan, not
  a build, and it is listed here as one.
- **Mobile performance is unmeasured**, and it is now the only place the triangle budget is still a
  guess. 150–200 concurrent enemies is a desktop measurement and an estimate everywhere else, and
  `ART.md §2`'s horde ceiling of ~4,000 triangles is a margin against a mobile GPU rather than
  anything this machine objected to — it drew 200 bodies at 23,822 each. If a real device falls
  short, cut the distance-tiering thresholds before cutting enemy count, and re-take the table in
  Performance with the same three bodies.
- **A textured survivor's legibility in a crowd is unasserted, and RIN fails the rule that used to
  cover it.** `PaletteProbe` stage 3 wants 0.35 of chroma between the player and every horde torso,
  value deliberately excluded because a dark biome takes value away. RIN v2's drawn mean is `78686b` at
  0.203 from the nearest, and the stalker's own bake — a variant in the game since Phase 8 and never
  questioned — reads `6b5f52` at 0.202, which is the same place. The metric is what broke: a survivor's colour used to be three
  authored constants and is now forty thousand sampled texels of skin, black cloth and an
  ivory top, whose mean is near-grey for *any* authored character. What separates her instead is
  value — 0.151 against every authored horde torso's 0.279 and up — and silhouette, and `BakeProbe`
  prints both numbers per bake now without ruling on them. The
  place it should have broken *was* checked: Cold Storage's dark floor and 7–28 m fog is where a
  dark survivor was expected to vanish, and she is one of the easiest figures to find there, because
  additive fog lightens a dark body and because an ivory panel and a hip-length ponytail are the only
  shapes of their kind on screen. A good outcome from an unasserted property, not a safe one — the
  next survivor could be dark, matte and short-haired and nothing would catch it. `CHARACTERS.md`
  carries the table.
- ~~**No survivor has irises.**~~ Fixed, and by the models rather than by the engine. The ask was
  §10.1 of `ART.md`: per-vertex sampling cannot reproduce a painting a few millimetres across, so
  give the iris *geometry*. It came back as an iris, a pupil and a highlight per eye as separate
  meshes bound to `Head`, and all five survivors now bake with eyes — red on RIN, blue on MIKA and
  YUNA, violet on SORA.

  **The engine-side fix is therefore not needed and is still the right thing to know about.** A
  survivor-only textured path — real UVs and the model's albedo on a mesh that draws once — would
  have fixed faces for any model rather than for models that were asked. It is not scheduled: five
  characters with eyes is the whole of what it would have bought, and the geometry answer costs
  nothing at runtime while a second material path costs a code path forever.
- **`test/TouchProbe.cs` needs a real display** and so has never run in the regression sweep. The
  headless dummy DisplayServer does not dispatch GUI input, so the touch layer is the one system
  whose tests are green only when someone runs them by hand.
- **The clock is unvalidated by a human.** The bot picks crates by worth, breaks contact when it is
  losing, knows not to do that on the way to the exit, and under `linger:auto` decides for itself when
  a run has turned — but it still does not use cover and does not kite. Range is therefore worth
  nothing to it, which is most of what the Marksman Rifle and the Pump Shotgun are sold on, and their
  rows in the weapon table should be read as measurements of the driver. See Balance.
- **The bot given the choice will not play the second half on the starting kit, and that is the
  clearest thing the re-take found.** Under `lingers:auto` it leaves at a median of 60 s with the kit
  and 117 s with the Service Rifle, and only the Service Rifle arm produced a run that reached 180 s
  and walked out. Told to stand there instead, ten of twelve die at 180 s. So the second half of the
  clock is reachable, by a better weapon, and it is not somewhere the game currently gives a player a
  reason to be. Whether a *person* can hold that ground longer than the bot is the same question as
  before — the bot still does not use cover and does not kite — and it now has a floor under it and a
  weapon attached to it.
- **The proof video films one biome, and that is the only true quarter of what this entry used to
  say.** It claimed a game without elites, a boss, biomes or music. `Presentation.cs` cues three
  elites — swift, armoured and volatile — at eight seconds, and cues the boss by dropping `BossAt` to
  an intensity the run reaches; `MusicDirector` is in `Main.tscn` and the movie writer's `frame.wav`
  carries what it plays. What is actually missing is the *place*: nothing sets `GameSession.Biome`, so
  every take is biome 0, the Rail Yard, and the four others have never been filmed. One line in
  `_Initialize` fixes it, or two takes cut together do it better.
- ~~**What a *busy* frame costs is unmeasured.**~~ `HordePerf -- 500 mixed fight warmup:600` measures
  it: 1.51 ms mean and 131 draw calls with every pool at or near its ceiling, against 1.13 ms and 89
  idle. See Performance. What that run also found is that the *old* warm-up of one second was too
  short to measure anything — two runs of the same command differed by more than the two modes differ
  from each other — so any row in that table taken before this one should be read as ±2x until
  re-taken at `warmup:600`.
- **Hitstop has never been felt by a person.** Its safety is argued from arithmetic — the whole clock
  scales together, so damage per game second is unchanged — and `ImpactProbe` asserts the clock comes
  back. What no probe can say is whether 0.14 for seven and a half hundredths of a second reads as
  weight or as a dropped frame, and it is the one number in this phase that only a player can settle.
- ~~**`physics_ticks_per_second` is not pinned in `project.godot`.**~~ It is —
  `common/physics_ticks_per_second=60`, and `GameRoot` prints the tick rate at startup so a silent
  revert would be visible in the first line of every run. The caution behind the entry is still worth
  keeping: moving to 30 Hz means re-checking every damping constant, and the reason that is survivable
  is that damping here is exponential rather than a per-tick multiplier.
- ~~**The audio bus has no limiter.**~~ It has one. `AudioBus.Install` puts an
  `AudioEffectHardLimiter` on bus 0, with no pre-gain — deliberately, because a limiter that also made
  everything louder would be a mastering decision smuggled in as a safety net. It guards against being
  installed twice, both by a static flag and by scanning the bus, because a run and the base each
  build their own sound director and ten limiters in series is nine unnecessary gain stages rather
  than ten times the protection. The measured headroom the entry quoted is still true: four music
  layers at once peak at 0.33.
- **Kenney is CC0 and safe to commit; Quaternius is no longer CC0.** This line used to name both, and
  as of 2026-08-28 Quaternius ships under the Quaternius Asset License v1.0 instead — generous about
  *use* (commercial, no fee, no attribution) and forbidding redistribution of the assets themselves
  "regardless of how much the Assets have been modified". Some of their pack pages still link to the
  CC0 deed while the site-wide licence page does not. That restriction is aimed at asset resellers,
  not games, but this repo is public and commits its assets as files, so a raw `.glb` here is closer
  to the thing it names than to the finished product the licence permits. Fine in a build, awkward in
  a source tree. Aggregators like Poly Pizza license per item and would need checking per file.
  `assets/models/SOURCE.md` carries the full reasoning and the provenance record.
