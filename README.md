# Survivors 3D

An extraction horde-survivor in Godot 4.7.1 (C# / .NET 9). A turnable third-person camera,
Vampire-Survivors crowd density, Tarkov's loot-fight-extract stakes: what you carry out is banked,
what you die holding is gone.

Status: both loops close, the surface is on, the run reports itself, the arena is a place, and things
happen when you shoot. A run is fight, loot, grow, extract on an arena generated fresh each time; it
ends on a debrief rather than a timer; between runs a walkable shelter turns what came back into gear
that changes the next one, offers three contracts and keeps your records, and dying in that gear loses
it.

**Nine enemy variants** as solid low-poly bodies rigged in the vertex stage, across **five places**
that ask different questions of a build, from a rail yard to a laboratory interior with its own light.
**Three survivors**, chosen before the loadout. **Fifteen weapons**, none of them a strictly better
version of another. Threat is a *place* — danger zones you choose to enter — rather than a spawn rate,
and a map leans toward one kind of them rather than holding one of each. The growth deck has five
lines and both the shop and the run tilt it. Finite ammo, items you can use or throw, synthesised
audio and music that follows the run's shape, a HUD of bars rather than labels, a minimap that records
where you have been rather than revealing the map, and hit feedback.

The billboard sprite path is still there and still works, behind `Horde.SolidBodies` — it is the
fallback for hardware that cannot afford a hundred and fifty meshes, and `ShadowProbe` builds the
scene with it so it cannot quietly rot.

Sweep clean at 45 probes; the table below lists 34 of them and is the older set. Build gate
re-verified 2026-08-29.

## Running it

```bash
dotnet build                         # 0 warnings, 0 errors
godot --headless --import            # import the assets
godot --headless --quit              # scene loads clean
godot                                # play — opens at the base
```

WASD/arrows move, weapons fire themselves and reload themselves, `[F]` secures the top item into the
safe box, `[Q]` uses a carried item, `[G]` throws one, `[Tab]` swaps weapons, and `[1]`/`[2]`/`[3]`
answer a level-up — or click the card. At the base, `[1]`/`[2]`/`[3]` take a contract and `[R]` rerolls
the board for credits.

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
| `test/MovementProbe.cs` | yes | Synthetic input moves the player; the camera follows within its lag budget; and a driver closes on something 2 m away and 90° off, which a driver that advances while turning cannot |
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

**Every table below was taken with a driver that could not close on anything inside 2.3 m**, and one
of the twelve layouts spent most of its run circling a crate rather than playing. The fix is in
Decisions; what it is worth is that seed `0x27D4EB2F` went from `Stuck, 120 banked at 70 s` to
`Extracted, 3172 banked at 167 s` — the largest single change any of these numbers has ever taken from
something that is not a design decision. Eleven of the twelve now extract on the starting kit:

| Seed | | Seed | | Seed | |
| :--- | ---: | :--- | ---: | :--- | ---: |
| `0x51E5D0A7` | 764 at 48 s | `0x9E3779B9` | 712 at 109 s | `0xC17E4A9B` | 1454 at 113 s |
| `0x2545F491` | 1923 at 122 s | `0xBF58476D` | 1803 at 69 s | `0x94D049BB` | 1217 at 70 s |
| `0x1B873593` | 1444 at 71 s | `0x85EBCA6B` | 950 at 141 s | `0xCC9E2D51` | 267 at 50 s |
| `0x27D4EB2F` | **3172 at 167 s** | `0x165667B1` | 1987 at 144 s | `0xD6E8FEB1` | *stuck — see What's left* |

The tables further down are not re-taken here, because a driver change is not a design change and
re-running them is a quarter of an hour each. They are still the right shape and the right
comparisons; read them as a floor. **Re-take them before any of them settles a decision.**

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

Everything the player looks at has had a pass, the numbers behind it are recorded above, and 45 probes
say the systems do what they claim. What is left is almost entirely **things that need a device or a
person**, not things that need code.

- **One sweep seed in twelve is still lost to the escape-and-return oscillation**, and it is the half
  of the crate problem the turning circle did not explain. `0xD6E8FEB1` gives up 19 m from `Crate7`
  having moved 3.7 m in ten seconds:

  ```
  godot --headless --fixed-fps 60 --script test/AutoPlay.cs -- \
        linger:auto seed:3605593777 weapon:scavenged_rifle
  # AUTOPLAY FAILED — could not reach Crate7 in 60s (still 19.4m away)
  #   flow (-0.71, -0.71) toward a target 19 m in +z
  #   source: escape (-0.71, -0.71), sample (0.00, 0.00); standing in a footprint = True
  ```

  Every number there is consistent and the behaviour still is not. The bot is standing inside an
  inflated block footprint, so its own cell was never reached by the flood and `Sample` honestly
  returns zero; `EscapeFrom` outranks the flow in exactly that case and pushes it clear; outside the
  footprint the flow routes it back past the same block; repeat. `Navigate` already carries a comment
  naming this — *"a bot that escapes and is then pulled straight back in is a third thing again — an
  oscillation, which reads as stuck and is not the same bug as either"* — and nothing has ever acted
  on it. What it needs is a memory of having escaped, so the flow cannot immediately undo it.

  **It is in `BalanceSweep`'s default set** and `Survived` is `Outcome == "Extracted"`, so that run is
  currently averaged into the table as a two-thirds-length failure — one of every twelve layouts is
  measuring the driver rather than the game.
- **The pair budget's denominator drifts, and nothing watches it.** The rule is "a pair inside about
  115% of one weapon"; the starting kit measured 110% at the step-1 rework and **138%** four phases
  later, on numbers nobody moved on purpose. It is caught only when someone re-takes the solo arm by
  hand (`lingers:auto slots:both`, twenty-four runs), which happened this time because a new Sidearm
  forced it. A probe cannot own this — it is a twenty-minute play-test, not an assertion — so it is a
  thing to re-take whenever a weapon changes, and it is written here because that is the only place
  that will say so. See `WEAPONS.md`.
- **One survivor of the seven has an authored body, and the base mesh it came from has no recorded
  source.** `CharacterResource.BakedBodyPath` is read by `Player.CreateBody`, which loads the bake,
  appends the held weapon's procedural silhouette to the same surface, and falls back to `SoloBody`
  when the path is empty or the bake will not build — so a survivor without a model is an ordinary
  state rather than an error. The Drifter is baked; the other six are not.

  What blocks the rest is provenance, not modelling. `art-src/models/build_roster.py` cuts all seven
  from `art-src/models/base/rigged_anime_girl_cc0.blend`, and that file is 10 MB of third-party
  geometry whose only claim to a licence is its own filename — no URL, no hash, nothing anyone can
  check. CC0 is a dedication to the public domain, so nothing here is a licence breach; what is
  missing is the project's own record, which is the gap the OFL font had and has the same fix: an
  `art-src/models/base/SOURCE.md` on the pattern of `art-src/fonts/SOURCE.md`, carrying the URL and
  the sha256. **Only the source is unverifiable, and only the source is large**, so the Drifter's
  176 KB output is committed — the game loads it — while ten megabytes of unattributable binary stays
  out of the history until that file exists.

- **The APK has never been built, let alone run.** Blocked on three installs this machine does not
  have: an Android SDK, a JDK, and an export template matching 4.7.1 (the only one present is 4.6.3).
  `export_presets.cfg` is written and committed — arm64, landscape locked, no permissions, `art-src/`
  excluded — so with those three in place it is one `godot --headless --export-debug "Android"`. A
  preset that has never produced an APK is a plan, not a build, and it is listed here as one.
- **Mobile performance is unmeasured.** 150–200 concurrent enemies is a desktop measurement and an
  estimate everywhere else. If a real device falls short, cut the distance-tiering thresholds before
  cutting enemy count.
- **`test/TouchProbe.cs` needs a real display** and so has never run in the regression sweep. The
  headless dummy DisplayServer does not dispatch GUI input, so the touch layer is the one system
  whose tests are green only when someone runs them by hand.
- **The clock is unvalidated by a human.** The bot picks crates by worth, breaks contact when it is
  losing, knows not to do that on the way to the exit, and under `linger:auto` decides for itself when
  a run has turned — but it still does not use cover and does not kite. Range is therefore worth
  nothing to it, which is most of what the Marksman Rifle and the Pump Shotgun are sold on, and their
  rows in the weapon table should be read as measurements of the driver. See Balance.
- **Under a fixed linger, half the seeds died in the second half; under `linger:auto` the 180 s target
  is met.** A bot told to stand in the worst part of the run until a stopwatch says otherwise is not
  doing what a player does, and the difference is the whole gap: given the choice it leaves at 0.6
  health, reaches 158 s on the Service Rifle and walks out. What is still unmeasured is whether a
  *person* can hold that ground longer, which is the same question as before and now has a floor
  under it.
- **The proof video is three phases stale.** `test/Presentation.cs` films a game without elites, a
  boss, biomes, or music.
- **`physics_ticks_per_second` is not pinned in `project.godot`** — 60 is the default and Godot strips
  it. Behaviour is correct today, but moving to 30 Hz means re-checking every damping constant.
- **The audio bus has no limiter.** The mix keeps its headroom by the master volume alone, set
  against a captured run; a louder moment than any capture happened to catch would clip rather than
  compress. The four music layers are quiet enough that all of them at once peak at 0.33, which is
  measured, but it is headroom by arithmetic rather than by a compressor.
- Third-party CC0 sources (Kenney, Quaternius) are safe to use directly; aggregators like Poly Pizza
  license per item and would need checking per file. Nothing from either is currently in the repo.
