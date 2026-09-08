"""Cuts the five character-select portraits out of the roster design sheet.

    python art-src/ui/cut_portraits.py <roster_design.png> assets/ui/portraits

**The roster sheet is the right source and the five individual sheets are not.**
Each character has a design sheet of their own with a hero illustration on it,
and cropping those gave five cards that did not match: the sheets differ in
aspect (1226x1283 for RIN, 1536x1024 for the rest), the name plate sits in a
different place on each, and RIN's is not in the left column at all. A select
screen wants five things that look like a set.

The roster sheet's top band already is that set — five equal panels, same
framing, same crop of the figure, each on its own identity colour with its name,
role, quote and three ability icons. Slicing it is the whole job.

The panel width is measured rather than assumed: the five panels end where the
right-hand text panel begins, at 92.05% of the sheet's width, so each is a fifth
of that. The band ends above the turnaround row at 60.5% of the height.
"""

import os
import subprocess
import sys

NAMES = ("rin", "mika", "akira", "yuna", "sora")

# **The five panels are not equally spaced, and assuming they were cost three
# attempts.** The obvious cut is a fifth of the band each, and every version of
# it put a slice of the previous character down the left edge of the next: AKIRA
# arrived with MIKA's drone in frame. The panels are hand-composed art, not a
# grid, so the left edge of each is measured off the sheet instead — the point
# where its background colour changes and its name plate begins.
#
# Fractions of the width rather than pixels, so a re-export at another
# resolution still cuts, and the height likewise: the band ends where the
# turnaround row starts, at 60.5%.
LEFT = (0.0000, 0.1953, 0.4049, 0.6042, 0.7995)
PANEL_WIDTH = 0.1901
BAND_END = 0.605


def main(sheet, out_dir):
    size = subprocess.run(["magick", "identify", "-format", "%w %h", sheet],
                          capture_output=True, text=True, check=True).stdout.split()
    width, height = int(size[0]), int(size[1])

    panel = int(round(width * PANEL_WIDTH))
    band = int(height * BAND_END)

    os.makedirs(out_dir, exist_ok=True)

    for i, name in enumerate(NAMES):
        left = int(round(width * LEFT[i]))
        out = os.path.join(out_dir, f"{name}.png")

        subprocess.run(["magick", sheet,
                        "-crop", f"{panel}x{band}+{left}+0", "+repage",
                        # Quantised to 8 bits per channel and stripped of
                        # metadata. The sheet is 2.8 MB of 16-bit PNG with an
                        # embedded colour profile; a 282x620 card does not need
                        # any of that, and five of them are committed.
                        "-depth", "8", "-strip", out], check=True)

        print(f"{out}  {panel}x{band}  from x={left}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)

    main(sys.argv[1], sys.argv[2])
