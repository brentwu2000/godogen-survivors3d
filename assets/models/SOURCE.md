# Where the imported models came from

The record the project owes itself for anything it did not draw. Same shape as
`art-src/fonts/SOURCE.md`, and it exists because the survivor roster is blocked
on exactly this: `art-src/models/base/rigged_anime_girl_cc0.blend` is ten
megabytes of third-party geometry whose only claim to a licence is its own
filename — no URL, no hash, nothing anyone can check. Nothing there is a breach;
what is missing is the ability to demonstrate that.

A URL and a sha256 is the whole requirement. Both below are checkable in one
command.

## Kenney — Mini Characters 1.0

| | |
| :--- | :--- |
| Pack | Mini Characters (1.0), created 17-07-2024 |
| Author | Kenney — <https://kenney.nl> |
| Page | <https://kenney.nl/assets/mini-characters> |
| Archive | <https://kenney.nl/media/pages/assets/mini-characters/bfc7e272b4-1774770718/kenney_mini-characters.zip> |
| sha256 (archive) | `9e1d48e6d7b8479ebbe84df71eb5bd8e1b3f0da546dea641890dccc8a02d0999` |
| Licence | CC0 1.0 — <http://creativecommons.org/publicdomain/zero/1.0/> |
| Attribution | Not required. Kenney asks to be credited as support; this file is that credit. |

Files taken from `Models/GLB format/` in that archive:

| In repo | From | sha256 |
| :--- | :--- | :--- |
| `kenney_survivor_a.glb` | `character-male-a.glb` | `77572792bfe2773b715b8cd8e18644b52b3e1f155fe10450254b50f9c364382a` |
| `Textures/colormap.png` | `Models/GLB format/Textures/colormap.png` | shipped alongside; the `.glb` references it by relative path |

**`Textures/colormap.png` is not optional and is not a texture in the usual
sense.** The `.glb` references it externally rather than embedding it, so
importing the model without it succeeds, logs one `Can't open file from path`
line among the import spam, and produces a material with no albedo texture — from
which `BakeBody` reads a flat white and bakes a white character. That is
indistinguishable from a model whose author chose white. If a Kenney model ever
bakes white, this file is the first thing to check.

It is a 512×512 palette: every triangle is mapped onto a flat patch of colour
rather than onto drawn detail, which is the convention across Kenney's whole 3D
library. `BakeBody` samples it per vertex, and for a flat patch that is exact
rather than approximate — see `PaletteImage` there.

## Verifying

```bash
curl -sLO https://kenney.nl/media/pages/assets/mini-characters/bfc7e272b4-1774770718/kenney_mini-characters.zip
sha256sum kenney_mini-characters.zip     # must match the table above
unzip -p kenney_mini-characters.zip "Models/GLB format/character-male-a.glb" | sha256sum
```

## A note on Quaternius, who used to be the other safe source

`README.md` listed Kenney and Quaternius together as CC0 and safe to use
directly. **That is no longer true of Quaternius.** As of 2026-08-28 their assets
ship under the Quaternius Asset License (QAL) v1.0 rather than CC0
(<https://quaternius.com/license.html>), and some pack pages still link to the
CC0 deed while the site-wide licence page does not.

QAL is generous about *use* — commercial projects, no fee, no attribution — and
its §3(a) forbids redistributing the assets themselves "as a standalone asset,
asset pack, stock file, template, or similar product … regardless of how much the
Assets have been modified."

That restriction is aimed at asset resellers and not at games, but it lands
awkwardly on **this** repository specifically, because this repository is public
and commits its assets as files. A `.glb` sitting in `assets/models/` is
something a third party can take directly, which is closer to the thing §3(a)
names than to the "completed Product that merely incorporates the Assets" §2
permits. A shipped build is unambiguously fine; a public source tree carrying the
raw file is a judgement call nobody needs to make.

Kenney is CC0 — a public-domain dedication, with no downstream restriction at all
— so for anything that gets committed here, Kenney is the source to reach for.
Quaternius remains a good option for assets that are only ever compiled into a
build.
