# UI — a language the player picks, and two places that were keys

**Asked for by the owner on 2026-08-26:** the UI must switch language, Traditional Chinese among them,
settable before a run goes out; and there should be somewhere to choose the survivor and somewhere to
choose the kit.

Two of those three already exist in some form, and saying which is the first useful thing this plan
does — a plan that rebuilds working screens is a plan that costs a phase and changes nothing.

**Read against the code on 2026-09-10, and one of the three had quietly been done.** The section below
still described choosing a survivor as "a cycle through three, with no page showing what they differ
by". It is a roster page with five survivors, their numbers, their unlock conditions, a blurb on the
one under the cursor and the illustration each was designed from — built two phases after this file was
written, by a phase that did not come back here to say so. The same thing `README`'s *What's left* was
doing, found the same way. **Re-read this file against the code before starting anything from it.**

---

## What the game does today, so the change is a change to something real

**The base is a room you walk, not a menu you scroll.** `Shelter` places six fittings and the nearest
one is the selection: standing at the armoury *is* choosing the armoury. That leaves exactly two verb
keys, `[E]` and `[C]`, and the design note in `Shelter.cs` is explicit that this is the point — the flat
fifteen-row screen it replaced made selling the stash and launching the run cost the same effort.

| Fitting | Where | `[E]` | `[C]` |
| :--- | :--- | :--- | :--- |
| Armoury | back wall | buy or equip | sell it back |
| Locker | left, z 2 | sell the stash | — |
| Records | right, z 2 | — | — |
| Board | left, z −3.5 | take the contract | reroll |
| Map | centre | change terrain | today's run |
| Gate | front wall | launch | **cycle character** |

So:

- **Choosing equipment already has a place.** The Armoury buys, equips, carries a weapon as a sidearm,
  and sells back at half. It is the most developed screen in the game.
- ~~**Choosing a survivor does not.**~~ It does. `[C]` at the Gate opens a **roster page** over the
  shop screen: five survivors, health / speed / bulk, locked entries showing what opens them, a blurb
  and an ability line on the one under the cursor, and the illustration beside it. `[W]`/`[S]` look,
  `[E]` takes, `[C]` closes. The cycle this file described was right for three lines of text and wrong
  for five people with portraits.

  **This also supersedes step 6 below, and the reasoning is in `BaseScreen.cs` rather than here.** The
  roster deliberately opens *at the gate*, on top of the shop, and closes back onto it — because
  choosing a survivor and equipping one are one decision made in two rooms, and the Warden's fourteen
  bulk changes what is worth buying. A Quarters fitting on the far wall would separate them.
- **Language does not exist at all.** Not a setting, not a string table, not a font. Every label is a
  Godot `Label` at the project default font with a size override.

---

## The five constraints that decide this design

Written first because four of the five are why this is a phase and not an afternoon.

### 1. The default font has no Chinese glyphs — measured, not assumed

`test/FontProbe.cs` asks the engine for the 69 distinct characters the UI is already known to need —
the fittings, the verbs, the settings row — rather than a lorem-ipsum sample. A font that covers a
random Han sample and misses 繁 fails on the one screen this work exists for.

| Asked | Covers | One Han = |
| :--- | :--- | ---: |
| project default | **0 of 69** | — |
| Noto Sans Mono CJK TC · Sarasa Mono TC · Source Han Mono TC | 0 of 69 *(not installed here)* | — |
| **MingLiU · PMingLiU · MS Gothic** | **all 69** | **2.00 Latin cells** |
| Microsoft JhengHei · Microsoft YaHei · Noto Sans CJK TC | all 69 | 1.03 Latin cells |
| system fallback, shipping nothing | 0 of 69 | — |

Three things fall out of that table, and two of them change the plan:

- **The premise holds.** The engine default draws none of it. Nothing below matters until a font is on
  screen, which is why it is step 1 rather than step 4.
- **A monospace CJK face is exactly 2.00 Latin cells wide**, not approximately. That makes option A
  below arithmetic rather than a fudge: a padding helper that counts wide characters as two is *exact*,
  and the columns cannot drift. The proportional face at 1.03 confirms the other half — with it, no
  amount of counting keeps a column straight.
- **"Ship nothing and let the OS sort it out" is dead.** A generic request with `AllowSystemFallback`
  reports no coverage *on a machine that has three CJK families installed*, because the fallback
  resolves during text shaping rather than when a font is asked what it holds. So it cannot be checked
  ahead of time — and for this project a glyph that is a box on someone else's machine and undetectable
  on ours is the exact failure the build gate exists to prevent. `SystemFont` stays out.

The named Windows families are not an answer either: the export target is Android, and MingLiU is not
there. They are only evidence that the monospace path exists.

This would be the **first third-party binary asset in the repository**. Everything the player currently
sees is generated: audio is synthesised from recipes by `BuildAudio`, the ground texture is painted
and then made tileable by `art-src/textures/make_ground.py`, cover is boxes, bodies are procedural. A
CJK typeface cannot be generated, and the README's asset rule already anticipates the case — CC0 and
OFL sources are usable directly.

(Written before the Kenney and Quaternius props arrived, which are the actual answer to "first
third-party binary asset" — see `assets/models/SOURCE.md`. The argument for subsetting a font at
build time is unaffected and is the reason this section is still here.)

**Subset it at build time rather than shipping the whole thing.** A full Traditional Chinese face is
10–20 MB; the glyphs this game actually uses will be under a thousand. A build tool that reads the
translation table, subsets an OFL source to exactly those code points, and **fails the build when a
glyph is missing** gives a few hundred kilobytes and turns "a character renders as a box" from a
visual bug into a build error. That is the same shape as the horde array's "every layer must be exactly
176×256" rule, which is the project's existing answer to this class of problem.

### 2. The screens are space-aligned text pages, and Han characters are double-width

Every page is a `StringBuilder` assembled into one `Label`, with columns held apart by runs of spaces:

```
credits {Credits}      stash worth {StashValue}      runs {Survived} out / {Lost} lost
```

A Han glyph occupies two columns in a monospace cell and an arbitrary width in a proportional one, so
**every column in the game misaligns the moment the text is Chinese**. There are two honest answers and
they cost very differently:

| | What it means | Cost | What it constrains |
| :--- | :--- | :--- | :--- |
| **A. Width-aware padding** | keep the text pages; count East Asian Wide characters as two columns when padding | small — one helper, used everywhere a page pads | forces a **monospace** CJK font, which is a much smaller shelf to pick from |
| **B. Real layout** | replace the pages with `GridContainer`/`HBoxContainer` columns | large — every page in `BaseScreen`, `DebriefScreen`, `Hud` | frees the font choice entirely, and is where the UI ends up eventually anyway |

**This is the one decision in the plan that is taste rather than fact, and it wants the owner.** A is
the recommendation, and the measurement above strengthens it: a monospace CJK glyph came back at
exactly 2.00 Latin cells, so the padding is arithmetic rather than an approximation that drifts by a
pixel per column. It keeps the terminal look the base was built around, it is one phase rather than
three, and B stays available afterwards because the string table lands either way.

**Neither answer is blocked by the font choice**, which is why step 1 can proceed before this is
settled: a monospace CJK face serves both, so picking one forecloses nothing.

### 3. Every string is interpolated at its call site

`$"credits {_profile.Credits}      stash worth {…}"` is a sentence and its data welded together. A
translation table needs the sentence as a key and the data as placeholders, and it needs them
positional — Chinese does not put the number where English does. So the extraction is not a
find-and-replace; each string becomes a format string, and a few become two because English reuses one
word where Chinese needs two.

Rough count of user-visible literals, which is the size of the job:

| File | Strings |
| :--- | ---: |
| `BaseScreen.cs` | ~138 |
| `Hud.cs` | ~59 |
| `BuildMain.cs` | ~49 |
| `DebriefScreen.cs` | ~43 |
| `Shelter.cs` | ~22 |
| `BuildBase.cs` | ~12 |

Around 320, plus weapon, gear, character, contract and item names, which live in `.tres` files written
by the `Build*` tools and are a separate decision — see *What this breaks*.

### 4. A new place is a fitting, never a new key

Two verb keys is the room's design, not an accident of it. Anything the player must be able to *do*
gets a station on the floor and reuses `[E]`/`[C]`. There is room: the right wall at z −3.5 is empty and
mirrors the Board, and the corners beside the Gate are free.

### 5. The profile is the only persisted store, and it is at Version 2

`Profile` already holds `Character`, `Biome`, `EquippedGear` and both loadout slots, and `ShopProbe`
asserts that a v1 save migrates and a newer one is refused. A language field is a Version 3 bump down
that established path — cheaper than a second settings file, which would mean a second migration story
for one enum.

### 6. The bootstrap problem, which is the easy one to miss

**A player who cannot read English cannot find the room where the language lives.** So the language is
never asked for: it is taken from `OS.GetLocale()` on the first run — `zh_TW`, `zh_HK` and `zh_Hant`
map to Traditional Chinese — and the Console below only ever *overrides* a choice already made. The
first screen a Chinese player sees is in Chinese, and the setting is there to correct it, not to
establish it.

---

## The design

### Two new fittings

| Fitting | Where | `[E]` | `[C]` |
| :--- | :--- | :--- | :--- |
| **Quarters** | right wall, z −3.5 | take the selected survivor | — |
| **Console** | beside the Gate | next language | — |

**Quarters** is the roster as a page rather than a cycle: three survivors, what each starts with, what
each is for, and which one is taken. It is where `[C]`-at-the-Gate goes, and the Gate goes back to one
verb — which is the shape every other fitting already has.

**Console** is the settings fitting. Language is the only thing on it today; it is named for what it is
rather than for its one current row, because the second setting always arrives.

Neither needs a new key, and the room grows from six stations to eight without the input layer moving.

### Language

A CSV, one row per key, one column per locale — `en` and `zh_TW` to begin with — so a third locale is a
column rather than a code change. Keys read as what the string is for rather than as the English text,
so an English rewording is not a retranslation.

**Not Godot's `.csv` → `.translation` import, and the reason is the one the audio already learned.**
`BuildAudio` writes `AudioStreamWav` resources directly instead of shipping `.wav` files, because a
`.wav` goes through the importer whose loop flag lives in a generated `.import` file the tool does not
own — and the horde ambience is ruined if that flag comes back off. A translation import has fewer
settings to lose and the same shape: a build artefact whose contents depend on state nothing in this
repository writes. There is also a second trap in it — `export_filter="all_resources"` means files
Godot *imports*, so the `.csv` source may not reach the pack at all, only the `.translation` cut from
it, which is the same class of thing that shipped an OFL font without its licence.

So the pipeline is the one every other asset here uses. `art-src/ui/strings.csv` is what a translator
edits, `scripts/tools/BuildStrings.cs` renders it, `resources/strings.tres` is what ships, and
`art-src/` is excluded from the export so the CSV never reaches a player. `Strings.Get(key, args…)` is
the call site. Nothing under `resources/` is edited by hand, which is the rule this table now lives
under too.

### Font

One tool, `BuildFont.cs` or a Python equivalent beside the other art tools, that:

1. reads every locale column of the CSV,
2. collects the code points actually used,
3. subsets an OFL source to exactly those,
4. writes the result under `assets/fonts/`,
5. **exits non-zero if any code point has no glyph.**

The last line is the one that matters. It is the difference between "somebody will notice a box on a
screen nobody screenshots" and "the build stops".

---

## What this breaks, which is most of the work

- **Around 320 literals become keys**, and each one is a small decision about where the placeholder
  goes. This is the bulk of the phase and it is unavoidable.
- **Column alignment everywhere.** See constraint 2 — every padded column in every page.
- **Content names live in `.tres` files.** Weapon, gear, character, contract and item names are written
  by the `Build*` tools into resources, and read straight onto the screen. Either the tools write keys
  and the screens translate them, or the resources gain a per-locale field. The former is consistent
  with everything else here; it also means `WeaponProbe`'s dominance stage, which prints weapon names,
  starts printing keys.
- **Probes that assert on English text.** `HudProbe`, `DebriefProbe`, `ShopProbe` and `BaseLoopProbe`
  read rendered strings. They should force `en` at startup rather than be rewritten — a probe that
  passes in one locale and fails in another is testing the translation, not the game.
- **`Presentation.cs` and every capture script** film text. The proof video is already three phases
  stale; it will be four.
- **Profile Version 2 → 3.**
- **The touch layer.** `TouchHud`'s four buttons are labelled, and its labels are the one place where a
  longer translated word cannot simply wrap.

---

## Order

Each step is playable before the next starts and closes against a probe, per the project's rule.

1. ~~**A Chinese character on screen.**~~ **Done.** `assets/fonts/ui.otf` is a **31 KB** subset of Noto
   Sans Mono CJK TC Regular (SIL OFL 1.1), cut from a 15.6 MB source by
   `art-src/fonts/build_font.py` and wired in as `gui/theme/custom_font`. The source is not in the
   repository — sixteen megabytes to ship thirty-one kilobytes is a bad trade and git keeps it forever
   — so `art-src/fonts/SOURCE.md` records its URL and sha256 instead, and the cutter **fails** rather
   than dropping a character the source cannot draw.

   `FontProbe` flipped from survey to coverage gate on its own, and the transition caught a bug in the
   probe: once a project theme font exists it becomes the last-resort fallback for *every* `SystemFont`,
   so the survey began reporting families this machine does not have as fully covered. A survey that
   agrees with whatever was just done is worse than no survey, so it now runs only in the state where
   its answers mean anything. Sweep clean at 46 probes.

   Sarasa Mono TC remains the equally-good alternative — same licence, same 2:1 metric, a narrower
   Latin half that may suit the terminal look better. It lost on download shape alone. Swapping is one
   `--source` argument and a re-run.

   *Probe:* `FontProbe` holds the premise while no font is shipped — the engine default cannot draw
   this language — and becomes the coverage gate the moment one is declared. One probe across the
   transition rather than two.

2. **The string table.** **The mechanism is done and one screen is through it.** `StringTableResource`,
   `BuildStrings`, `Strings.Get` and `resources/strings.tres` exist; the **roster screen** is fully
   converted, including the five survivors' roles and blurbs — `BuildCharacters` writes keys into the
   `.tres` now, so there is one source of truth for the words rather than an English copy in the
   resource and another in the table.

   **What is left is the other ~300 literals**: `BaseScreen`'s shop pages, `Hud`, `DebriefScreen`,
   `Shelter`, and the names in the weapon, gear, contract and item resources. That is the bulk of the
   phase and it is mechanical now that the mechanism is proven end to end.

   **Every screen is through.** The roster, the Console fitting, the HUD, the growth cards, the
   debrief, the shelter's fitting prompts and now `BaseScreen`'s six pages and its twenty-three
   messages — 221 keys across two locales, against a 448-glyph subset.

   **The content nouns are done too, and not the way this file predicted.** The prediction was
   "either the tools write keys and the screens translate them, or the resources gain a per-locale
   field". Both are wrong, for a reason neither had looked for: **the names are identities.**
   `Profile.Stash` is a dictionary keyed by item name, `Collected` is an array of them, `ClaimedSets`
   holds a set's name, and `EnemyTypeResource.TypeName` resolves the path of a baked body. A tool that
   wrote `noun.wedding_ring` into the resource would have broken every save on disk, and a per-locale
   field would have made what a save *contains* depend on the language it was written in.

   So `Strings.Noun(identity)` is a lookup beside the name rather than a replacement for it, keyed on
   the name's own slug under `noun.`. The resources are untouched, the saves are untouched, and the
   call sites that changed are exactly the ones that draw. 63 nouns.

   The prediction's other half was right and is worth keeping: `WeaponProbe`'s dominance stage does
   print weapon names, and it prints them in English still, because it reads the resource and the
   resource is the identity. That is the correct outcome rather than a lucky one — a probe naming a
   weapon in whatever language the machine is set to would be a probe you cannot grep.

   **A noun with no row draws its English name rather than «key».** That is right on a screen and is
   also the exact shape of failure nobody notices — one English word among forty Chinese ones reads as
   a proper noun somebody chose to leave alone. `StringProbe` enumerates every noun by reading
   `resources/` rather than a list, so a weapon added next month is checked the day it is added.

   **Sixty lines of the base screen were deleted rather than translated.** `SideColumn` and the
   flat-screen branch drew every page's content at once for the case where `Base.tscn` has no
   `Shelter` sibling — which `BuildBase.cs` adds unconditionally, so nothing had rendered them since
   the room was built. Dead code that draws a screen is worse than dead code that does not: it reads
   as a supported layout, so it asks to be kept working, and this phase would have translated all of
   it before anybody asked whether a player can reach it. A missing shelter is a pushed error now.

   *Probe:* `StringProbe` — the table loads, every locale takes the same placeholders as English,
   every key formats in every locale, and no key comes back as `«key»`. It still does not scan for a
   user-visible literal left outside the table.

   **What did land is the assertion for the opposite mistake, which is the one that had already
   happened.** `BaseLoopProbe` walks all seven fittings, reads every `Label` on each page, and fails if
   the text contains a key from the table verbatim; the debrief gets the same read. That catches a
   field which *is* a key being interpolated instead of resolved — `$"{who.Blurb}"` where
   `Strings.Get(who.Blurb)` was meant — which draws "playing as RIN — character.rin.blurb" and passes
   every other check in the project: the table is intact, so `StringProbe` is happy; every character is
   ASCII, so `FontProbe` is happy; `Get` is never called, so there is no `«key»` to see.

   It had been on screen since the roster was extracted, in English as well as in Chinese, because the
   roster screen was updated and the base screen's copy of the same line was not. Every field
   `Build*.cs` turns into a key adds one more call site that can make it, which is why this is an
   assertion rather than a rule to remember.
3. ~~**Width-aware alignment.**~~ **Done.** `Strings.Pad` / `PadLeft` / `Cells` count East Asian Wide
   as two, and the roster uses them where it used C#'s `{x,-18}` — which counts *characters*, so every
   column after a translated one shifted by however many hanzi were in it. `StringProbe` asserts the
   measurement and asserts that two strings padded to the same width occupy the same width.
4. **The Traditional Chinese column, and the glyph gate.** **The gate is live and the column is
   partial.** `build_font.py` reads every locale column of the CSV instead of `wanted.txt` and fails
   rather than dropping a character; `FontProbe` asks the *shipped* font whether it can draw every
   character in the *shipped* table, which is the other end of the same question. It caught its own
   first regression immediately: eight ability strings were added to the CSV and the font was not
   re-cut, and the probe named the thirteen missing characters.

   The subset is 56 KB and 279 characters now, up from 31 KB and 172. The column is complete for every
   key that exists, which is every key the roster needs and nothing else yet.
5. ~~**The Console fitting.**~~ **Done.** It is a fitting on the wall like every other one: stand at
   it, `[E]` cycles the language, the room redraws, and the choice persists. It cost no profile
   version — the field defaults to empty and an empty field means "ask the system", so a save written
   before the fitting existed reads back as a player who has not chosen yet, which is exactly what
   they are.

   *Probe:* `StringProbe` for the table and `ShelterProbe` for the fitting.
6. ~~**The Quarters fitting.**~~ **Superseded.** The roster is a page and it opens at the Gate on
   purpose — see the correction at the top of this file. The Gate keeps `[C]`.

**Steps 1 and 2 are worth doing even if the rest is deferred.** The font is the risk, and the table is
the thing every later locale is free against.

**What remains is the two kinds of sentence that are composed rather than stored.** The unlock
conditions — `extract without firing a gun`, `kill the boss and walk out` — are prose in `UnlockBook`.
The shop's summary line under the cursor — `6 dmg  3.2/s  1.6 m  bleed` — is assembled in
`ShopCatalogue` out of four numbers and a trait name. Neither is a noun and neither is a whole string
anybody can hand a translator: they have to be split into a format and its arguments first, the way
`CharacterResource.AbilityLine` was, because Chinese does not put the number where English does.

`Contract.Describe` is the same shape and the same job. Its subject already resolves — a contract asks
for 行屍 rather than for `walker` — and the sentence around it does not.

**The column arithmetic held all the way through.** Every padded column on the armoury page lines up in
Chinese: `[已裝上]` is six cells where `[equipped]` is eleven, `格鬥刀` is six where `Combat Knife` is
twelve, and `Strings.Pad` counts cells. The last three places still counting characters — the shop
rows, the contract descriptions and the reward column — were converted with the pages.

The two hazards left both have an assertion under them. A field that holds a key and is drawn raw is
caught by `BaseLoopProbe`'s page walk; a noun with no row, and a character the subset cannot draw, are
caught by `StringProbe` and `FontProbe`. What none of them can see is whether a translated sentence
still *fits* — that is a screenshot, and `test/BaseShot.cs -- locale:zh_TW` is how it is taken.
