# UI — a language the player picks, and two places that were keys

**Asked for by the owner on 2026-08-26:** the UI must switch language, Traditional Chinese among them,
settable before a run goes out; and there should be somewhere to choose the survivor and somewhere to
choose the kit.

Two of those three already exist in some form, and saying which is the first useful thing this plan
does — a plan that rebuilds working screens is a plan that costs a phase and changes nothing.

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
- **Choosing a survivor does not.** It is `[C]` at the Gate — a cycle through three, with no page
  showing what they differ by. It works, it is undiscoverable, and it is the one verb in the room that
  is not about the fitting it is bound to.
- **Language does not exist at all.** Not a setting, not a string table, not a font. Every label is a
  Godot `Label` at the project default font with a size override.

---

## The five constraints that decide this design

Written first because four of the five are why this is a phase and not an afternoon.

### 1. The default font has no Chinese glyphs

Godot's built-in font is a Noto Sans subset covering Latin, Greek and Cyrillic. Every Han character
renders as a blank box. **Nothing in the plan below matters until a font is on screen**, which is why
it is step 1 rather than step 4.

This would be the **first third-party binary asset in the repository**. Everything the player currently
sees is generated: audio is synthesised from recipes by `BuildAudio`, the ground texture by
`BuildGroundTexture`, cover is boxes, bodies are procedural. A CJK typeface cannot be generated, and
the README's asset rule already anticipates the case — CC0 and OFL sources are usable directly.

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
the recommendation: it keeps the terminal look the base was built around, it is one phase rather than
three, and B stays available afterwards because the string table lands either way.

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

Godot's own `TranslationServer` and a CSV, one row per key, one column per locale — `en` and `zh_TW` to
begin with. It is the engine's supported path, it is what the `.csv` → `.translation` import is for, and
it means a third locale is a column rather than a code change.

Keys read as what the string is for, not as the English text, so an English rewording is not a
retranslation.

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

1. **A Chinese character on screen.** Source an OFL face, wire it into the project theme, render one
   hardcoded string. No translation, no table, no settings. **This is first because it is the only step
   that can fail for reasons outside the plan** — licence, glyph coverage, monospace availability — and
   finding that out after 320 strings have been extracted is the expensive order.
   *Probe:* the font resource loads and reports a glyph for a known Han code point.
2. **The string table, English only.** Every user-visible literal becomes a key; `tr()` at the call
   site; the CSV has one locale. Nothing on screen changes, which is the point — a step that changes
   the words *and* the mechanism cannot be reviewed.
   *Probe:* no user-visible literal remains outside the table, asserted by scanning the UI sources the
   way `TraitProbe` scans the weapons directory rather than from a hand-kept list.
3. **Width-aware alignment.** One helper that counts East Asian Wide as two, used by every page that
   pads. Still English only.
   *Probe:* a page built from strings of known width aligns identically in both locales.
4. **The Traditional Chinese column, and the glyph gate.** Translate; subset; make a missing glyph a
   build failure.
   *Probe:* every key has a `zh_TW` value, and every code point in that column has a glyph in the
   subset font.
5. **The Console fitting.** Language switches at runtime, persists to the profile at Version 3, and
   defaults from `OS.GetLocale()` on a fresh save.
   *Probe:* switching re-renders the room; a v2 save migrates and lands on the OS locale; a fresh save
   on a Chinese OS starts Chinese.
6. **The Quarters fitting.** The roster as a page; the Gate loses `[C]`.
   *Probe:* the survivor taken in Quarters is the survivor the run starts with, and the Gate has one
   verb.

**Steps 1 and 2 are worth doing even if the rest is deferred.** The font is the risk, and the table is
the thing every later locale is free against. Steps 5 and 6 are small once 1–4 exist, and 6 is the only
one of the six that would be worth doing on its own today — the character cycle is the weakest verb in
the room regardless of what language it is in.
