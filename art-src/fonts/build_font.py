"""Cuts a shipping UI font out of a full CJK face.

    python art-src/fonts/build_font.py --source <NotoSansMonoCJKtc-Regular.otf>

A full Traditional Chinese face is sixteen megabytes and sixty-five thousand
glyphs. This game will use a few hundred of them. So the source is *not* in the
repository — it is downloaded once, recorded in `SOURCE.md` beside this file with
its URL and hash — and what ships is a subset built from the characters the UI
actually asks for.

**The exit code is the point.** A character the UI can produce and the font
cannot draw is a blank box on somebody's screen, and the only place that is cheap
to catch is here. Ask for a glyph that is not in the source and this stops the
build, the same way `BuildEnemySprites` refuses a horde layer that is not exactly
176x256.

Today the wanted set is `wanted.txt`. At UI.md step 4 it becomes every locale
column of the translation table, and this tool changes by one function.
"""

import argparse
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
WANTED = os.path.join(HERE, "wanted.txt")
OUT = os.path.join(ROOT, "assets", "fonts", "ui.otf")

# Every printable ASCII character, always.
#
# The UI is overwhelmingly Latin today and stays that way in English, so this is
# not an optimisation worth making conditional — and a missing space or digit is
# a far more embarrassing hole than a missing hanzi.
ASCII = "".join(chr(c) for c in range(0x20, 0x7F))


def wanted_characters():
    """The union of ASCII and whatever `wanted.txt` declares.

    Newlines and ASCII whitespace in the file are separators, not content — the
    file is meant to be readable and edited by hand, so it must be possible to
    wrap a long line of hanzi without adding a glyph nobody asked for.
    """
    characters = set(ASCII)

    with open(WANTED, encoding="utf-8") as handle:
        for line in handle:
            # A `#` line is a note to the reader. Harmless to include today
            # because the notes are ASCII and ASCII is wanted anyway, which is
            # exactly why it is worth skipping deliberately: the first note
            # somebody writes in Chinese would otherwise silently enlarge the
            # font and nobody would connect the two.
            if line.lstrip().startswith("#"):
                continue

            for character in line:
                if not character.isspace():
                    characters.add(character)

    return sorted(characters)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, help="full CJK face to cut from")
    parser.add_argument("--out", default=OUT)
    arguments = parser.parse_args()

    characters = wanted_characters()
    font = TTFont(arguments.source)

    # What the source can actually draw, before asking for anything.
    #
    # `subset` silently drops a code point it cannot find, so checking afterwards
    # would mean comparing two glyph orders and guessing which absence was
    # deliberate. Asking the cmap first gives a list of exactly what is missing
    # and lets the message name the characters rather than a count.
    covered = set()
    for table in font["cmap"].tables:
        covered.update(table.cmap.keys())

    missing = [c for c in characters if ord(c) not in covered]
    if missing:
        print(f"FONT FAILED — {os.path.basename(arguments.source)} cannot draw "
              f"{len(missing)} wanted character(s): {''.join(missing)}")
        return 1

    options = subset.Options()

    # Layout features off. This is a UI font for short strings in two scripts,
    # not typesetting: ligatures, small caps and vertical writing are weight the
    # game never asks for, and vertical metrics in particular are most of what
    # makes a CJK face large.
    options.layout_features = ["kern"]
    options.drop_tables += ["vhea", "vmtx", "VORG"]
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.notdef_outline = True
    options.recalc_bounds = True

    subsetter = subset.Subsetter(options=options)
    subsetter.populate(text="".join(characters))
    subsetter.subset(font)

    os.makedirs(os.path.dirname(arguments.out), exist_ok=True)
    font.save(arguments.out)

    size = os.path.getsize(arguments.out)
    print(f"FONT OK — {len(characters)} characters, "
          f"{size / 1024:.0f} KB at {os.path.relpath(arguments.out, ROOT)} "
          f"(source {os.path.getsize(arguments.source) / 1048576:.1f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
