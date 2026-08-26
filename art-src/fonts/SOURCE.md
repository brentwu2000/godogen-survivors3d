# Where the UI font comes from

`assets/fonts/ui.otf` is a subset. This records what it was cut from, because a
generated asset whose input nobody can find again is a binary blob with a story
attached.

| | |
| :--- | :--- |
| Face | **Noto Sans Mono CJK TC**, Regular |
| Licence | SIL Open Font License 1.1 — shipped alongside as `assets/fonts/OFL.txt` |
| Source | `https://github.com/notofonts/noto-cjk/raw/main/Sans/Mono/NotoSansMonoCJKtc-Regular.otf` |
| Size | 15.6 MB, 65 535 glyphs |
| sha256 | `82a040aed900bba51b5990bc158a86b264c8ad5071a2d8911e8696350e0794b3` |

**The source is deliberately not in the repository.** Sixteen megabytes to ship
thirty-one kilobytes of glyphs is a bad trade, and git keeps it forever. Download
it, check the hash, and run the cutter:

```bash
curl -sSL -o /tmp/noto-mono-tc.otf \
  https://github.com/notofonts/noto-cjk/raw/main/Sans/Mono/NotoSansMonoCJKtc-Regular.otf
sha256sum /tmp/noto-mono-tc.otf          # must match the row above
python art-src/fonts/build_font.py --source /tmp/noto-mono-tc.otf
```

## Why this face

**Monospace, and exactly so.** `test/FontProbe.cs` measured a Han glyph in a
monospace CJK face at **2.00** Latin cells against a proportional face's 1.03.
Every screen in this game is a text page padded with spaces, so that 2.00 is what
lets `UI.md`'s cheap alignment answer be arithmetic instead of an approximation
that drifts a pixel per column. A proportional face would force the pages to be
rebuilt as real layout containers before a single Chinese string could go on
screen.

**OFL, so the only obligation is to carry the licence**, which is why `OFL.txt`
sits beside the font rather than in a credits screen nobody opens.

Sarasa Mono TC was the other candidate and is equally fine — same licence, same
2:1 metric, a narrower Latin half that arguably suits a terminal-styled UI
better. It lost on nothing but download shape: its releases are whole families in
`.7z`, forty-eight megabytes for the weights this game does not use, against a
single 15.6 MB file for one weight here. Worth revisiting if the Latin half ever
looks too wide beside the Han.

## What the subset contains

Whatever `wanted.txt` declares, plus all printable ASCII. 177 characters today.

`build_font.py` **fails** rather than silently dropping a character the source
cannot draw — that is the whole reason it is a tool and not a one-off command,
and it is the same rule as the horde array's "every layer must be exactly
176x256". At UI.md step 4 the wanted set becomes the translation table's locale
columns, and this pipeline changes by one function.
