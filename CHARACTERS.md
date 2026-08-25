# Survivors — design, and the brief the models are built from

Seven survivors, five growth lines, one baseline. This file is both halves of that: the numbers and
why they settled where they did, and the modelling brief Codex builds against.

Read `RECOVERY.md` §D3 first. It records why three survivors exist and what they were allowed to be;
this file extends that roster and **overturns exactly one of its decisions**, which is called out
below rather than quietly reversed.

---

## The rules a survivor already obeys

These are not proposals. They are enforced by `test/CharacterProbe.cs` and by
`scripts/resources/CharacterResource.cs`, and every number in this file was chosen to satisfy them.

1. **The Drifter is the zero and never moves.** 100 health, 6.0 m/s, 20 bulk. Eleven phases of
   balance work, forty-odd probes and every price in the shop were tuned against those three
   numbers. `StageDefaultUnchanged` fails if any of them drifts.

2. **Nothing is strictly better than the Drifter.** Every survivor that beats it on health, speed
   *and* bulk with no cost on any of the three fails `StageNoStrictlyBetter`.

3. **The largest of the three differences must be at least 15%.** Below that it is a rounding error
   the player never notices, and a roster of those is a menu that exists to look like a feature.
   Note the probe checks the *largest*, not all three — a survivor may sit inside the band on two
   axes if the third is decisive.

4. **An ability is an existing `RunModifiers` field, granted at the start of a run.** Never a
   mechanic of its own. A mechanic only one survivor has is a mechanic the deck, the gear and the
   trinkets cannot interact with, and the point of a survivor is a *head start on a strategy* the
   rest of the game can be built around.

5. **Never damage and never fire rate.** Those are what the shop sells. A survivor selling them
   again is a difficulty setting with a name on it.

6. **Gated on extractions, not credits.** Buying a way to play with money earned by the other way to
   play is a strange sentence, and it makes the second survivor a reward for being good at the first.

The legal ability fields, in full, from `scripts/systems/RunModifiers.cs`: `Pierce`, `CritChance`,
`CritMultiplier`, `AreaScale`, `Knockback`, `IgniteChance`, `DetonateChance`, `Lifesteal`, `Regen`,
`Dodge`, `Thorns`, `SearchRadiusBonus`, `LootValueScale`, `OrbitBlades`, `PulseStacks`,
`ChainChance`, `Chill`. `AttackDelayScale` is on that list and is excluded by rule 5.

---

## The decision this overturns

`BodyMeshLibrary.ForPlayer` and `CharacterResource` both carry the same comment:

> Proportions are shared and only the palette moves […] The player is the one body that must never be
> mistaken for the horde for even a frame, and what carries that is hue. Three survivors that were
> three *silhouettes* would each have to win that fight separately, and two of them would lose it.

**That reasoning is sound and the conclusion is now wrong, for a reason that did not exist when it
was written.** It was written when the horde was five upright bipeds of roughly human proportion. The
horde is now nine, and D2a/D2b deliberately broke that: the stalker is a quadruped, the bulwark is
the first *horizontal* silhouette in the game, the lantern arrives lit. Shape is already how the
player reads the crowd. A player whose only distinguishing feature is hue is now the *least*
distinguished thing on screen — everything else got a silhouette and the survivor did not.

So silhouettes may differ, and the protection moves from "there is one player shape" to a rule each
body has to pass on its own:

**A survivor is manufactured; the horde is grown.** Straight edges, bilateral symmetry, hard kit with
a flat face on it, and a head clear of the shoulder line. The horde is asymmetric mass — leaning,
swollen, spilling, lit from inside. Whatever else a survivor's shape does, it must look like
something a person put on.

And hue still carries it, now stated as a number rather than as an intention: **every survivor's
torso is hue 190°–260° with saturation at or above 0.25.** Nothing in the horde is in that band, and
the saturation floor is what stops a "steel" survivor collapsing into the grey the crowd is full of.

The empirical gate is in §How it is judged. It is the one that decides.

---

## The seven

| Survivor | HP | Speed | Bulk | Height | Opens | Line | Ability |
| :--- | ---: | ---: | ---: | ---: | ---: | :--- | :--- |
| **Drifter** | 100 | 6.0 | 20 | 2.20 | 0 | — | none |
| **Courier** | 80 | 6.6 | 28 | 2.10 | 3 | Scavenging | loot ×1.15, search +0.9 m |
| **Scout** | 70 | 7.1 | 17 | 2.05 | 5 | — | dodge 0.12 |
| **Warden** | 140 | 5.3 | 14 | 2.25 | 8 | Retinue | 1 blade, chill 0.25 |
| **Gunsmith** | 90 | 6.2 | 12 | 2.25 | 12 | Gunnery | pierce +1 |
| **Revenant** | 125 | 5.6 | 16 | 2.20 | 16 | Ward | thorns 0.30, regen 0.5 |
| **Sapper** | 90 | 5.5 | 15 | 2.15 | 22 | Ordnance | 1 pulse stack, area ×1.25 |

Drifter, Courier and Warden are unchanged to the digit. The four new ones are below.

**Five lines, five survivors, one each.** `RunGrowth.GrowthLine` has exactly five members and H3 made
them the shape of a run. A survivor that favours one is the earliest possible commitment to a build —
earlier than the shop, earlier than the first card — which is precisely the H4 complaint that
*nothing earned between runs changes how a run is played, only how easy it is.*

**The Drifter and the Scout favour nothing, for opposite reasons.** The Drifter is the zero and a
tilt would make it something. The Scout is about *leaving*, and nothing in the deck is about leaving —
its identity sits outside the five lines rather than beside them.

### Scout · opens after 5

**The question: everything else asks how long you stay. This asks how fast you can be gone.**

The run's central tension is the extraction multiplier climbing 1.0 → 3.0 against a horde that
reaches its cap at 160. Every survivor so far answers it by getting stronger. The Scout answers it by
being somewhere else — 7.1 m/s is a fifth faster than the Drifter and faster than a runner (4.6) by
a margin that makes breaking contact a decision rather than a hope.

Seventy health is the lowest in the game and it is the whole cost. Two brute contacts and one
mistake, and 70 is not a health bar so much as a count of how many times you may be wrong. Dodge 0.12
is not compensation for that — it is rolled per tick against contact damage, so it removes about a
tenth of a rate the Scout cannot afford to be inside at all.

Its bad map is Cold Storage: speed buys nothing in a room, and 24 m of fog means you break contact
into somewhere you cannot see.

### Gunsmith · opens after 12

**The question: can one weapon carry a whole run?**

Twelve bulk is the smallest bag in the game — 40% under the Drifter, less than half the Courier's. It
cannot run the loot economy, which means it cannot buy its way out of trouble with medkits and cannot
refill from what it finds. `README.md` records the mechanism it is walking into deliberately: the bag
holds 528 at 60 s and 40 at 120 s, *spent* rather than capped, and every valuable thing in it is also
what keeps you alive. The Gunsmith starts that race already out of room.

What it gets is pierce +1 before the first level-up and a deck that keeps offering Gunnery. Pierce is
a rule rather than a number — it is the difference between killing the thing in front and killing the
line behind it — and it is the one growth axis that scales with how well the player has positioned
rather than with how much they have collected.

Its bad map is Old Town: 166 blocks and 17 m of sight line, so a pierce build spends the run hitting
a bin.

### Revenant · opens after 16

**The question: can you survive being surrounded, rather than avoiding it?**

Every other survivor treats contact damage as the thing to not have. Thorns 0.30 makes it an input:
the crowd standing on you is the crowd killing itself. Regen 0.5 is what makes that sustainable
rather than a slower death, and 125 health is the buffer the pair needs to have time to work.

**It is not a second Warden.** The Warden holds ground with things that fight for it — a blade
already turning and cold ground underfoot, which is Retinue, and which works whether or not anything
touches it. The Revenant has to be *touched*, which is the only place in this game where the player
wants what the design has spent thirty phases making expensive. Against the Warden it trades 15
health for half a metre a second and two bulk.

Its bad map is The Flats: 12 blocks and 36 m of line of fire, where nothing has to come to you and a
crowd that arrives strung out never delivers the pile thorns is paid by.

### Sapper · opens after 22

**The question: is the crowd a problem or a resource?**

Ordnance wants a crowd to stand in front of it, and the Sapper starts with a shockwave already
pulsing and every area in the game a quarter wider — the blast, the molotov patch, the chill radius,
the pulse itself. It is the survivor that gets better as the run gets worse, which is the inverse of
everything else on this list.

Ninety health, 5.5 m/s and 15 bulk is three costs, and it needs all three. With any one of them at
baseline the "gets better as it gets worse" clause has no floor under it — a survivor that is
strongest at 160 enemies and merely normal at 40 is a survivor with no bad half of the run.

It opens last because it is the most map-dependent thing in the roster, and that is the point rather
than a flaw: H1 made maps lean, H3 made lines ask different things of them, and a run that draws
Ordnance onto The Flats is *supposed* to be a bad run. The Sapper is the strongest statement of that
principle the roster can make, and it should be met by a player who already knows what a map is
telling them.

---

## What Codex builds

Seven `.glb` files, one per survivor, and one script that generates all of them.

**Build the Drifter first and stop.** It is the only body whose current appearance is known-good, so
it is the calibration: bake it, shoot it, and hold it against a screenshot of today's procedural
player. If those two do not read as the same character, nothing after it is measurable.

### The pipeline

```bash
# 1. author — art-src/models/build-survivors.mjs, modelled on build-walker.mjs
node art-src/models/build-survivors.mjs drifter          # → assets/models/drifter.glb

# 2. validate — art-src/models/validate-survivors.mjs, modelled on validate-creatures.mjs
node art-src/models/validate-survivors.mjs

# 3. bake — glb → the vertex rig the shader reads
godot --headless --script scripts/tools/BakeBody.cs -- \
      res://assets/models/drifter.glb res://resources/bodies/drifter.res \
      2.20 0.60 0.33 0.040 385785,424d61,b89a7a,2e3a4a

# 4. inspect
godot --headless --script test/ModelReport.cs -- res://assets/models/drifter.glb
godot --script test/BodyShot.cs -- model:res://resources/bodies/drifter.res
```

Steps 3 and 4 need Godot and NuGet, which the Codex sandbox does not have. **Codex writes steps 1 and
2; this session runs 3 and 4 and reports back.** That split has held for four rounds of asset work.

### Hard constraints

Every one of these is enforced by `validate-creatures.mjs` or by `BakeBody.cs`, and every one has
already cost this project a body that rendered perfectly and was wrong.

| | |
| :--- | :--- |
| **Exactly one mesh node** | The baker walks the tree and reads the first mesh it reaches. A model exported as body + coat + pack bakes the body and silently omits the rest. It refuses and names the nodes now, but design for one merged mesh. |
| **Exactly one skin** | |
| **Bones, by exact name** | `Root`, `Hips`, `Spine`, `Chest`, `Head`, and `Thigh`/`Shin`/`Foot` `.L`/`.R`, `UpperArm`/`Forearm`/`Hand` `.L`/`.R`. The name *is* the rig channel: thigh, shin and foot swing about the hip; upper arm, forearm and hand swing about the shoulder; everything else is torso and only bobs. `.L`/`.R` sets the half-turn phase that stops a walk reading as a march. |
| **One weight of exactly 1.0 per vertex** | No blending. `w[0] === 1` and the rest zero. The baker takes the heaviest joint regardless, so a blended weight is a vertex whose channel is decided by a rounding order. |
| **Non-indexed primitives** | Run the `expandMaterialGroups` step `build-walker.mjs` already has. A non-indexed surface merged with an indexed one contributes its vertices and nothing pointing at them: correctly placed, correctly coloured, referenced by no triangle. |
| **POSITION, NORMAL, JOINTS_0, WEIGHTS_0 on every surface** | |
| **Closed and manifold** | Every geometric edge shared by exactly two triangles. This is the check that fails most often and the one `BakeProbe` repeats on the Godot side. |
| **Four surfaces, in order** | `Torso`, `Limbs`, `Head`, `Kit`. Fixed for all seven, so one palette argument shape works for the whole roster. Author them as neutral grey — `BakeBody` owns the palette. |
| **900 triangles** | Generous against the horde's 600 and cheap against one instance and one draw call. The reason to stay low is not performance: everything else in this game is 400–600 triangles, and a 3,000-triangle survivor standing next to a 500-triangle walker reads as two games spliced together. |

### Three things the rig cannot do

**There is no elbow and no knee.** `SetRig` turns a vertex about a *fixed* Y by `swing · sin(phase)`.
A forearm given its own pivot separates from the upper arm the moment the upper arm swings. The whole
arm turns as one piece about the shoulder and the whole leg about the hip. Do not author a silhouette
that depends on a joint bending — model the pose it holds.

**Arm swing has a ceiling of about 0.33 rad**, and past it a rigid arm at full swing reads as a plank
rather than as a stride. Legs do not have this problem: a straightening knee is what a leg does at the
top of a stride, so the same rigidity reads as correct there.

**A garment below the hip must be weighted to `Hips` or `Spine`, never to a thigh.** Weighted to a
thigh it scissors open with every step. This matters for exactly one survivor and it is the one whose
silhouette depends on it — see the Gunsmith.

### Colour

Hand-typed hex is sRGB and `BakeBody.Tint` converts it once. The model's own `COLOR_0` and
`baseColorFactor` are linear by glTF specification and are **not** converted. The asymmetry is
correct rather than an oversight, and skipping the conversion is how the first stalker — written as
dark brown — arrived at roughly twice the brightness and rendered a washed near-white, through a bake
that passed every soundness check there is.

So: author every material as neutral grey, exactly as `build-walker.mjs` does, and pass the palette
to `BakeBody` as the fourth argument.

| Survivor | Torso | Limbs | Head | Kit |
| :--- | :--- | :--- | :--- | :--- |
| Drifter | `385785` | `424d61` | `b89a7a` | `2e3a4a` |
| Courier | `336b75` | `3d4d57` | `bd9e80` | `7a6a4a` |
| Scout | `5a7f96` | `4a5a66` | `c0a184` | `3f5260` |
| Warden | `4d4770` | `3d3d4d` | `b39475` | `6b6480` |
| Gunsmith | `2b3a63` | `333a4a` | `ad8f70` | `1f2740` |
| Revenant | `46596b` | `3a4650` | `a88a6c` | `7d8b94` |
| Sapper | `3d5566` | `3a464f` | `b2947a` | `8a6f2e` |

The first three rows are today's colours to the digit; `Kit` is new for all seven.

**The Sapper's ochre kit is the only warm surface in the roster and it must not drift toward red.**
Red is the hit flash, and a survivor with red on it is a survivor that looks permanently hurt — the
same failure the elite tint hit at 0.55 flat, where an armoured brute became a solid blue silhouette.

### The specs

Same vocabulary as `build-walker.mjs`'s `specs` object. `hip`, `shoulder` and `head` are fractions of
height; `headR` is the head radius as a fraction of height.

```js
const specs = {
  drifter:  { height: 2.20, width: .48, limb: .065, depth: .24, lean:  4, hip: .48, shoulder: .80, head: .93, headR: .070 },
  courier:  { height: 2.10, width: .44, limb: .058, depth: .22, lean:  8, hip: .50, shoulder: .80, head: .93, headR: .066 },
  scout:    { height: 2.05, width: .40, limb: .052, depth: .19, lean: 12, hip: .50, shoulder: .80, head: .93, headR: .062 },
  warden:   { height: 2.25, width: .55, limb: .074, depth: .27, lean:  2, hip: .46, shoulder: .79, head: .92, headR: .070 },
  gunsmith: { height: 2.25, width: .46, limb: .060, depth: .22, lean:  0, hip: .46, shoulder: .82, head: .94, headR: .066 },
  revenant: { height: 2.20, width: .58, limb: .078, depth: .26, lean:  2, hip: .47, shoulder: .78, head: .90, headR: .068 },
  sapper:   { height: 2.15, width: .52, limb: .070, depth: .30, lean:  8, hip: .48, shoulder: .79, head: .92, headR: .066 },
}
```

The Drifter's row is `BodyMeshLibrary.ForPlayer` transcribed: shoulder width 0.48, limb radius 0.065,
torso depth 0.24, lean 4°. It is the calibration and should not be improved.

Bake arguments, in the order `BakeBody` takes them (`height legSwing armSwing bob`):

| Survivor | legSwing | armSwing | bob |
| :--- | ---: | ---: | ---: |
| Drifter | 0.60 | 0.33 | 0.040 |
| Courier | 0.64 | 0.36 | 0.044 |
| Scout | 0.68 | 0.36 | 0.048 |
| Warden | 0.54 | 0.28 | 0.032 |
| Gunsmith | 0.58 | 0.28 | 0.034 |
| Revenant | 0.52 | 0.26 | 0.030 |
| Sapper | 0.56 | 0.30 | 0.044 |

### What the `Kit` surface is, per survivor

The kit is where the silhouette lives. The body proportions above separate the seven at close range;
the kit is what separates them at the twenty metres the game is actually played at, and it is the
surface that has to say *manufactured*.

**One dominant vertical mass and everything else low.** E2a learned this on a market kiosk that
arrived as five coloured trays stacked into a cake, and a survivor is a smaller version of the same
problem: strong horizontal banding at this scale reads as layers rather than as a person.

- **Drifter** — a webbing belt and one shoulder strap crossing the chest. Deliberately the least
  distinctive kit in the set, for the same reason the walker is the least distinctive body: it is
  what the other six are read against.

- **Courier** — the bag *is* the character. A tall pack rising to the shoulder line, squared off, with
  two thigh pouches and a bedroll across the top. It should be obvious from behind that this one
  carries more, because carrying more is the whole of what it does.

- **Scout** — a slim chest rig with two flat pouches, one thigh pouch, and a hood that reads as a
  hood in profile rather than as a lump. Nothing above the shoulder line and nothing below the hip.
  It is the smallest and the only survivor that leans, and both should be visible before any detail
  is.

- **Warden** — heavy plates hanging at the hip, a gorget at the throat and forearm guards. The mass
  sits low and around, never on the shoulders: shoulder mass is what the Revenant owns and these two
  are the roster's nearest neighbours.

- **Gunsmith** — a long coat skirt from the hips to mid-thigh, tapering, and a bandolier diagonally
  across the chest. **It is the only survivor whose torso mass continues past the hip**, which is a
  silhouette move nothing in the horde makes and it costs about forty triangles. Weight the skirt to
  `Hips`. The tallest, the straightest, and the only one with a vertical line unbroken from shoulder
  to knee.

- **Revenant** — shoulder plates, a chest plate, shin plates. The widest shoulders in the roster at
  0.58, and **this is the design closest to failing the horde test**: plated, heavy and low is three
  quarters of the way to a brute. Three things hold it apart and all three are required — it stands
  upright at 2° where a brute is at −2° and reads as looming; the head sits clear *above* the
  pauldrons rather than sunk between them; and the plates are flat-faced and symmetric where the
  brute's mass is asymmetric. Shoot this one against the brute before anything else in the set.

- **Sapper** — two canisters on the back, vertical, rising just above the shoulder line, and a belt of
  pouches. **The only survivor with a load above the shoulders**, which is what makes it readable from
  the front despite being the second-widest. Vertical canisters rather than horizontal tanks, because
  horizontal is banding and banding is the cake.

---

## What this session wires up

None of this is Codex's work. It is listed so the modelling is not delivered into a game that cannot
draw it.

1. **`CharacterResource.BakedBodyPath`** — the same string field `EnemyTypeResource` already has, and
   `SoloBody` already has the constructor that takes a baked `ArrayMesh` and a height. `Player.BodySpec`
   branches on whether the path is set, so a survivor without a model keeps the procedural body and
   the roster can land one at a time.

2. **`CharacterResource.Kit`** — a fourth colour, and the fourth surface in the bake.

3. **The held weapon.** F2 built the carried weapon *into* the player mesh and rebuilds it on swap. A
   baked body has no weapon, so shipping the bake alone silently un-ships F2 and the survivor goes
   back to fighting bare-handed on screen. The merge step builds the carry geometry with
   `MeshBuilder` exactly as `Build3D` does today and appends it to the baked mesh as a fifth surface.
   Codex leaves both hands empty and prints the shoulder and hand landmarks, as `build-walker.mjs`
   already does, so the weapon has something to be placed from.

4. **The carrying arm's swing.** `Build3D` drops it to a quarter, because at full swing a held weapon
   scythes across the torso every stride and reads as animated rather than held. `BakeBody` takes one
   `armSwing` for both arms, so the merge step rewrites the carrying side's swing in the baked rig
   data — it is UV data and trivially rewritable, but it is not automatic and a bake that skips it
   produces a body that walks correctly and waves its rifle.

5. **A list at the gate.** D3b already recorded that three survivors is where cycling on a key stops
   being obviously right. Seven is emphatically past it. `[C]` opens a list.

6. **`CharacterProbe` gains two stages** — that a declared `BakedBodyPath` loads and stands at
   `BodyHeight`, and that every torso colour is inside the hue band. A survivor whose model is missing
   currently falls back to the procedural body, which is a correct game and a silently unfinished one.

7. **`BalanceSweep` character arm.** Still open from D3b, and seven survivors is the point at which
   "do these actually produce different runs" stops being answerable by reading the table.

---

## How it is judged

**The lineup, against the horde, at horde distance.** `BodyShot` reads `Horde.TypeNames` and skips
baked variants; it needs a survivor mode. The shot that matters is seven survivors and nine creatures
in one frame at the distance the game draws them, and the test is one sentence: *can you point at the
players without being told?* If a survivor needs a second look, its silhouette failed, and no amount
of correct hue rescues it. Every judgement made about an asset viewed on its own has been wrong so
far.

**The Revenant against the brute, on its own, first.** It is the one design that could fail this, and
finding out in a seven-body lineup is finding out too late to know which of the three mitigations was
the one that was needed.

**The Drifter against today's screenshot.** Same character or the calibration is broken.

**Then the game, moving.** A gait is not judgeable from a still. `test/Screenshot.cs` needs a display;
so does `BodyShot`.

The order is deliberate. Every one of these is cheaper than the one after it, and each has caught
something the next would have blamed on the wrong thing.
