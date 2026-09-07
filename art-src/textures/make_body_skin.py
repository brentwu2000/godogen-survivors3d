"""Turns the painted skin plates into the surfaces every body is drawn through.

    python art-src/textures/make_body_skin.py

Reads  art-src/textures/skin_*_raw.png     (1024x1024, painted, not tileable)
Writes assets/textures/skin_*.png          (1024x1024, tileable, graded)

`body.gdshader` projects one of these triplanar in model space at 1.1 tiles per
metre of body, so a plate covers most of a torso and repeats about twice down a
walker. Two things follow and both are `plate.py`'s job:

**It has to tile**, because a seam on a surface that repeats twice per body is
a stripe on every body in the horde at the same height.

**Its mean has to be `surface_detail_mid`**, measured after the sRGB decode the
sampler performs, because the shader centres its modulation there. A plate that
arrives off that mark scales every body in its category up or down uniformly —
which is indistinguishable from the palette being wrong, and is where two rounds
of palette-lifting went before anyone measured the plate.

**And it keeps its colour.** The shader used to read only the luminance, so a
painting of rotting flesh — grey-green skin, bruised purple, dark red where it
has split — arrived as one grey number. `surface_detail_chroma` now takes the
plate's hue and saturation with its brightness divided out, which is why these
are graded rather than desaturated: the variation *is* the point.
"""

import os

import plate as pl

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

SIZE = 1024
FEATHER = 72

# What `surface_detail_mid` in `body.gdshader` says it is.
TARGET_MEAN = 0.21

# Wider than the floor's, because this one is not competing with a biome tint —
# a body's palette is a flat colour and everything that makes it look like a
# material has to come from here.
CONTRAST = 1.45

PLATES = [
    ("skin_infected", 0.10),
    ("skin_mutant", 0.18),

    # The survivor plate is the project's own earlier painting of patched cloth,
    # stitched leather and riveted plate, carried over rather than repainted --
    # it was already the best of the three and the only thing wrong with it was
    # that the shader was reading one channel of it.
    ("skin_survivor", 0.06),
]


def build(name, desaturate):
    source = os.path.join(HERE, f"{name}_raw.png")
    output = os.path.join(ROOT, "assets", "textures", f"{name}.png")

    if not os.path.exists(source):
        print(f"  {source} is missing — skipped")
        return

    image = pl.load(source)
    image = pl.wrap_blend(image, FEATHER)
    image = pl.resize_tileable(image, SIZE)
    image = pl.grade(image, TARGET_MEAN, contrast=CONTRAST,
                     desaturate=desaturate, linear=True)

    pl.save(image, output)

    decoded = pl.luminance(pl.srgb_to_linear(image))
    print(f"wrote {output} ({SIZE}x{SIZE}, tileable)")
    print(f"  decoded mean {decoded.mean():.3f}, target {TARGET_MEAN}")
    print(f"  decoded range {decoded.min():.3f}..{decoded.max():.3f}")


for plate_name, desat in PLATES:
    build(plate_name, desat)
