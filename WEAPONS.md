# Weapons — two in hand, and what happens between them

**Asked for by the owner on 2026-08-25:** carry two weapons and use both at once. Melee hits harder;
short blades are quick and light, heavy weapons slow and huge. Firearms trade damage for reach. Bows
charge, fire slowly and hit enormously. Room for high-tech weapons — lasers and the like. And builds
that produce a *chemical reaction* between the two things you are holding.

This is the plan. It is also, by accident, the answer to a problem the balance table measured last:
**melee is buying insurance against something that does not happen.**

---

## What the game does today, so the change is a change to something real

`WeaponHandler` holds two slots and `[Tab]` swaps which one is live. Only `_slots[_active]` fires;
the other sits idle except for its charge timer. `Profile` already stores both — `LoadoutWeapon` and
`LoadoutSecondary` — so the shop already sells into two slots and the second one is a knife by
default.

So the structure exists and only one half of it ever does anything. Everything below is about turning
the idle half on and then making the pair mean something.

---

## The measurement this starts from

H4e priced what a melee weapon buys. Cleave, bleed, no reload, and that it can never run out — and
**the starting rifle does not run out either**, because the reserve is calibrated to empty only if the
player stops looting and no player who is looting stops. Measured over a long run: the Scavenged Rifle
never went dry across 150 seconds, and the Reaper Scythe banked 1217 against its 2870.

Melee's compensating advantage is priced against a threat the design already removed. That is why it
sits at two thirds of a rifle in every table, and it is not a numbers problem — no damage figure fixes
a trade whose other half is worth nothing.

**Two weapons at once is the structural fix.** A sidearm that is always working *is* the answer to a
primary that reloads, runs dry, or cannot reach what is already touching you. Melee stops competing
with the gun and starts covering it.

---

## The rule that stops this being "twice the damage"

If both slots simply fire, the correct loadout is the two highest-output weapons in the shop, and the
weapon table goes straight back to having one answer — which is what `WeaponProbe`'s dominance stage
was added to prevent.

**A weapon is a Primary or a Sidearm, and a loadout is one of each.**

| | Hands | What it is | Examples |
| :--- | :---: | :--- | :--- |
| **Primary** | two | the build's damage, and the decision | rifles, bows, heavy melee, launchers, beams |
| **Sidearm** | one | always working, small, covers the primary's gap | knife, katana, pistol, hand emitter |

This is the owner's own distinction promoted from a description to a slot type: heavy-slow-huge on one
side, light-fast-small on the other. As a slot type it cannot be gamed — you cannot carry two heavies
because you do not have four hands.

### The DPS budget — measured, and it is not the number this section first guessed

Every number in this game is balanced against **one** weapon firing. The spawn curve, the 160 ceiling,
the zone tiers, the extraction multiplier and every table in `RECOVERY.md` assume it.

This section originally said "a pair should be worth about 110% of one weapon, so re-tune everything
to Primary ≈ 80% and Sidearm ≈ 30%". Step 1 shipped both slots firing with **no numbers changed at
all** and measured it — twelve layouts, `lingers:auto`, `WeaponHandler.LiveSlots` as the only variable:

| firing | survived | median banked | median seconds | median lowest HP |
| :--- | ---: | ---: | ---: | ---: |
| one weapon | 10/12 | 1703 | 136 | 56 |
| **two weapons** | 10/12 | **1877** | 146 | 60 |

**+10.2% banked, +7% duration, four points of health, identical survival.** The estimate was right
about the number and wrong about what to do with it.

**The game is already at 110%, so nothing needs re-tuning down.** Two weapons is not close to double
because a sidearm is not a second primary: the Combat Knife is 6 damage at 1.6 m against a rifle's 12
at 18, so it only ever connects with what has already closed. And the payout ceiling is the *bag*
rather than the damage — more killing buys a longer run, and a longer run banks what it can carry.

So the budget rule inverts. It is not a target to rebalance toward; **it is a ceiling to hold**:

> Every Sidearm added in step 3 must leave the pair inside about 115% of one weapon. The knife's
> contribution is the reference, and the arm that measured it is `slots:both`.

That matters most for the Sidearm Pistol below, which is the one with range — a sidearm that connects
at 14 m rather than 1.6 does not have the knife's natural limiter and is the most likely thing in this
plan to break the number that was just measured.

### Melee is above firearms on damage, and that is the trade being bought

The owner's ordering, made specific: **melee Primaries carry about 125% of a firearm Primary's
sustained damage**, and pay for it with reach and with contact.

A firearm Primary works at 16–30 m. A melee Primary works at 3 m, which means standing inside the
crowd, which costs health continuously — and under `linger:auto` health is what decides how long a run
lasts, so the cost is real and it is already measured by the table. That is a trade. What melee has
today, "it never runs out", is not.

---

## The table

Nine weapons exist. The plan keeps all nine, moves them into slots, and adds five — two Sidearms the
game has never had, and three high-tech Primaries.

### Primaries

| | Class | Damage | Rate | Reach | Signature |
| :--- | :--- | ---: | ---: | ---: | :--- |
| Fire Axe | heavy melee | very high | slow | 3.0 | one chop, hardest shove in the game |
| Reaper Scythe | heavy melee | high | medium | 3.4 | 160° sweep, three quarters carries through |
| **War Hammer** *(new)* | heavy melee | highest | slowest | 2.8 | **shatters** the chilled; knocks a line down |
| Scavenged Rifle | firearm | low | fast | 18 | the baseline everything is read against |
| Service Rifle | firearm | low | fastest | 16 | never stops: biggest magazine and reserve |
| Pump Shotgun | firearm | medium | slow | 13 | damage is a *distance*, eight pellets |
| Marksman Rifle | firearm | very high | slowest | 30 | charges while holstered; pierces three |
| Hunting Bow | bow | high | slow | 14 | no ammunition, ricochets to a new target |
| Bolt Launcher | bow | high | slow | 24 | 4 m blast where it connects |
| **Arc Lance** *(new)* | beam | medium | continuous | 12 | a held beam; **shocks** what it dwells on |
| **Pulse Rifle** *(new)* | tech | medium | fast | 20 | overheats instead of reloading — venting is the reload |

### Sidearms

| | Class | Damage | Rate | Reach | Signature |
| :--- | :--- | ---: | ---: | ---: | :--- |
| Combat Knife | blade | very low | very fast | 1.6 | **bleeds**; rewards touching many things once |
| **Katana** *(new)* | blade | low | fast | 2.4 | **bleeds** harder and reaches further; no cleave |
| **Sidearm Pistol** *(new)* | firearm | low | medium | 14 | the only ranged Sidearm; **marks** what it hits |
| **Hand Emitter** *(new)* | tech | very low | fast | 8 | **chills**; the cheapest way to open a reaction |

**The bow charges and that stays.** `WeaponTrait.Charge` already multiplies a shot by 3.5 after three
seconds idle and already ticks for a *holstered* weapon, which was built for exactly this: carry the
Marksman Rifle as your Primary, fight with the Sidearm, and the big shot is ready when you need it.
Under simultaneous fire that trait becomes far more interesting and needs re-tuning, not redesigning.

**Why five new weapons and not two.** Sidearms need a real choice or the slot is a knife with extra
steps, and each of the four opens a different reaction below. The three tech Primaries exist because
the owner asked for them and because *overheat* and *a held beam* are two firing models this game does
not have — a weapon that stops because it is hot is a different decision from one that stops because
it is empty.

---

## The reactions

**One weapon applies, the other consumes.** That is the chemistry, and it is why the pair is the build
rather than two independent damage numbers.

The game already has three statuses — bleed, burn and chill — and a vocabulary of delivery traits.
Four reactions, no more, because this project's rule is a few things that mean a lot.

| Reaction | Applied by | Consumed by | What happens |
| :--- | :--- | :--- | :--- |
| **Shatter** | chill *(Hand Emitter, Frost Cell, Chill card)* | any heavy impact *(hammer, axe, shotgun, blast)* | the chill is spent for a burst of damage scaled to how chilled it was |
| **Spread** | bleed *(knife, katana)* | cleave *(axe, scythe, hammer)* | the bleed jumps to everything the sweep touches |
| **Cook off** | burn *(molotov, Ignite card)* | blast *(Bolt Launcher, Shockwave, pipe bomb)* | the burn detonates in a radius instead of ticking out |
| **Conduct** | shock *(Arc Lance, Pulse Rifle)* | any hit on a chilled target | the hit chains to two more, and shock is spent |

Three properties this set has on purpose:

- **Every reaction crosses the slot line.** Bleed is a Sidearm's job and cleave a Primary's; chill is a
  Sidearm's and shatter a Primary's. So a reaction is something a *loadout* does, never something one
  weapon does on its own.
- **The status is spent.** A reaction that leaves the status behind is a permanent damage multiplier
  with a story attached, and the player stops making a decision about when to trigger it.
- **Each one has a non-weapon source too.** Chill is on the Frost Cell and the Chill card, burn on the
  molotov and Ignite, blast on the Shockwave. So the growth deck and the trinket slot can reach a
  reaction the loadout did not buy — which is what stops the shop dictating the run, and it is the
  same principle H4a used to make gear tilt the deck.

**Shock is the one new status** and it belongs to the tech weapons. It exists because Conduct needs an
applier that is neither bleed nor chill, and because a beam that leaves something crackling is the
clearest way to make a laser feel different from a fast gun rather than just quieter.

---

## The skills, and where they meet the weapons

**Asked for by the owner on 2026-08-25:** finish this against the skill system rather than beside it.

Right now they are two systems that do not speak. A growth card grants a number on `RunModifiers` and
whatever is firing reads it; a weapon carries a trait and nothing in the deck knows the trait exists.
Nowhere is *this weapon plus that card* worth more than the two of them added up — which is the same
complaint the outside assessment made about the whole game and the same one H1 to H4 have been
answering everywhere else.

Three joins, smallest first.

### One: a weapon leans the deck, exactly as a piece of gear does

`GearResource.Favours` names a growth line and `RunGrowth.FavourLine` feeds it into the same term a
pick feeds, so wearing two Ward pieces starts a run as though two Ward cards had already been taken.
**A weapon does not do this, and it is the larger commitment of the two.**

`WeaponResource.Favours`, on the machinery that already exists. **Primary at full strength, Sidearm at
half** — the Sidearm is the smaller half of the loadout and should tilt the deck by less than the
thing filling both hands.

| Weapon | Line | Why that one |
| :--- | :--- | :--- |
| Scavenged / Service / Marksman Rifle | Gunnery | the weapon hits harder, faster or through more |
| Hunting Bow | Gunnery | penetration and a ricochet are both "one shot reaches more" |
| Pump Shotgun | Ordnance | a cone is one hit becoming several, which is the line's definition |
| Bolt Launcher | Ordnance | it detonates where it connects |
| Fire Axe / Reaper Scythe | Ordnance | cleave is the same sentence with a blade in it |
| Combat Knife | Retinue | **bleed is damage that happens without you**, which is what the line is |

The knife is the one worth arguing about, and the argument is the point: Retinue currently has one
trinket and one new backpack and no weapon at all, so a player who wants things that fight on their
own has almost nothing to commit with before a run. A bleeding sidearm is that, and it makes "knife
plus orbiting blades" a build rather than two unrelated purchases.

### Two: every line either applies a status or consumes one

The reactions above name weapons as their sources. Half of those sources should be **cards**, or a
reaction is something the shop sells and the run cannot reach — the failure H4a fixed for gear by
letting the deck reach what the loadout did not buy.

| Status | From a weapon | From the deck | From gear or an item |
| :--- | :--- | :--- | :--- |
| **Bleed** | knife, katana | **Orbit** — the ring cuts, so blades bleed | — |
| **Burn** | — | **Ignite** | molotov |
| **Chill** | hand emitter | **Chill** | Frost Cell |
| **Shock** | arc lance, pulse rifle | **Chain** — it is already electricity in everything but name | — |

That is two new behaviours on cards that exist — Orbit bleeds, Chain shocks — and no new card. Both
are what the card already looks like; neither changes what it costs.

**And the two lines with no status are not left out, they have the other job:**

- **Gunnery is the consumer that hits hardest.** Crit and penetration make the shot that spends a
  status bigger, so Gunnery is what turns a reaction from a trick into a build.
- **Scavenging is what pays for it.** Reactions want consumables — a molotov to start a burn, a
  medkit to survive standing in the crowd that is bleeding — and Scavenging is the line that keeps the
  bag full.

Written down because the tempting fix is to give all five lines a status, and five statuses is a
system nobody can read in a fight.

### Three: a weapon the deck cannot improve is a weapon nobody should carry

The deck is twenty-two options and a run buys a handful. If a weapon's output is moved by only two or
three of them, the player carrying it spends most of their level-ups on cards that do nothing for what
is in their hands — and that is invisible, because the card still says +12% of something.

**Probe stage, in `GrowthProbe`: every weapon in the table has at least five growth options that
measurably change its damage over a fixed window.** Measured by firing it at a wall of brutes with the
option applied and without, which is the shape `ModifierProbe` already uses. A weapon below the line
is a weapon whose build does not exist yet.

This is the stage that would have caught the melee problem two phases before the balance table did:
cleave scales with area, pierce does nothing for it, crit does, fire rate does — count them and the
gap shows up in the table rather than in a play-test.

## Balance, stated as the things that must stay true

Written as assertions because that is what `WeaponProbe` will hold them to.

1. **No Primary dominates another Primary, and no Sidearm dominates another Sidearm.** The existing
   dominance stage, with the comparison moved from category to slot type. It already caught the
   Service Rifle at thirteen axes to nothing and the Fire Axe at eight to nothing; slot type is the
   correct grouping once category stops deciding what competes with what.
2. **A pair lands within 100–120% of one of today's weapons.** The budget above. This is the number
   that decides whether the last twenty phases of tuning survive, and it wants a `BalanceSweep` arm of
   its own — `weapon:` becomes `weapon:primary+sidearm`.
3. **No pair is the answer everywhere.** Four Sidearms times eleven Primaries is forty-four loadouts;
   the claim is not that they are equal but that the best one differs by biome, by knot share and by
   growth line. The tools to check that all exist now — `gear:`, `lines:`, `weapons:`, `characters:`,
   `lingers:auto`.
4. **A reaction is worth taking and never mandatory.** A loadout with no reaction should sit inside the
   band of one that has it; the reaction is a way to play, not a tax on not knowing about it.
5. **Melee ends up above firearms on sustained damage and below on health at the exit.** That is the
   trade this whole change exists to create, and both halves are already columns in the sweep.

---

## What this breaks, which is most of the work

Listed because the plan is worth less than the list.

- **`WeaponHandler` fires one slot.** The tick reads `_slots[_active]`, and four of its five early
  returns — burst, reload, dry, cooldown — are written as "the weapon" rather than "this weapon". Both
  slots need the whole path, per-slot.
- **`Weapon`, `Ammo`, `Reserve`, `Level`, `Reloading` and `RunUpgrades` all mean "the active one".**
  `Hud`, `Player`, `AutoPlay`, `BalanceSweep`, `GrowthProbe`, `ItemProbe` and `ShopProbe` read them.
  Each site has to say which slot it means, and the HUD has to show two.
- **`[Tab]` stops having a job.** Either it goes, or it becomes which weapon the body is *holding* —
  which matters because of the next item.
- **F2 built the held weapon into the body mesh.** `BodyMeshLibrary` draws one of three silhouettes
  and rebuilds the mesh on swap. Two weapons means drawing two, or drawing the Primary and accepting
  that the Sidearm is invisible. At the distance this is seen — a dozen pixels — the honest answer is
  probably the Primary only, and that is a decision to make with a screenshot rather than in advance.
- **Proficiency is per category and both categories now level.** `start = min(practice, ceiling/2) +
  gear tier` was written for one weapon per run. Two weapons levelling at once doubles what a run
  teaches unless the rate moves.
- **Ammunition arrives per weapon.** Looted rounds go to "whichever slot takes a magazine"; with two
  live slots that rule has to pick, and picking wrong is invisible.
- **Every balance table restarts.** Every number in `RECOVERY.md` from H4b onward was measured with one
  weapon firing. They are not wrong, they are about a different game.

---

## Order

Each step is a phase that closes against the build gate and a probe, and each one is playable before
the next starts.

1. ~~**Both slots fire.**~~ **Done.** `TickSlot(slot, step)` lifted out of `_PhysicsProcess` — all five
   of its exits were written as `return` against the one active weapon, so leaving them would have made
   the first slot's reload end the second slot's turn. Per-slot levels, because a sidearm swinging on a
   rifle's numbers is the wrong answer to a question with two. `LiveSlots` as the control variable,
   `WeaponIn`/`AmmoIn`/`FiringIn` for the readings, and a HUD line per slot with the held one marked.
   Sweep clean at 45 probes, and the measurement is in the budget section above.
2. ~~**Slot types.**~~ **Done.** `WeaponSlot` on the resource, and the shop reads it instead of
   guessing: `IsMelee` was the proxy that decided where a bought weapon went, and it is wrong in the
   case this design is about — a fire axe is melee and takes both hands. Asking for a Primary as a
   sidearm is refused with a reason rather than silently honoured, because a loadout holding two
   Primaries is the dominance the slot exists to prevent. The dominance stage groups by slot **and**
   category — a Primary and a Sidearm are both carried, so they never compete and scoring them
   against each other would report a trade where no choice is being made — and a new stage asserts
   neither shelf is empty and the starting kit is a legal pair. No numbers moved: step 1 measured the
   pair at 110% and the budget is a ceiling to hold rather than a target to tune toward.
3. ~~**The weapon leans the deck.**~~ **Done.** `WeaponResource.Favours` on the machinery gear already
   uses, applied in the same pass so `ClearFavour` cannot wipe the loadout's half of the lean.
   Measured: Ordnance is 26% of cards offered carrying nothing, **32% with an Ordnance primary and 28%
   with the same weapon as a sidearm** — the halving is visible rather than asserted, and
   `GrowthProbe` now holds all three numbers plus "every weapon names a line".

   It also found that `AutoPlay -- weapon:` was **only pretending to change the loadout**: it called
   `Equip` and left the profile saying something else, which was invisible until a weapon started
   leaning the deck and then every `weapon:` run in the sweep was tilted by whatever the *profile* was
   carrying. A scythe and a service rifle came back with identical offers, which is the correct output
   of a flag that changes nothing. It goes through the profile now, into the slot the weapon's own type
   asks for.
4. **Every line applies or consumes.** Orbit bleeds, Chain shocks. Two behaviours on cards that
   already exist, and `GrowthProbe` gains the stage that counts how many options move each weapon.
5. **The four Sidearms.** The slot is a real choice or it is a knife with extra steps.
6. **The reactions**, one at a time, each with a probe stage that asserts the status is *spent* — the
   failure mode is a permanent multiplier, and it would read as the reaction working.
7. **The three tech Primaries.** Last, because overheat and a held beam are new firing models rather
   than new numbers, and they should land on a table that already balances.

**Step 1 before anything else, and stop there for a measurement.** Everything after it is priced
against a number this project does not have yet: what two weapons firing is actually worth. Guessing
it and building four phases on the guess is how the fixed linger produced a year of tables about a
weapon that could not spend what it bought.
