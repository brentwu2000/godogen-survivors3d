"""Turns the painted ground plate into the tile the arena floor is drawn from.

    python art-src/textures/make_ground.py

Reads  art-src/textures/ground_raw.png   (1024x1024, painted, not tileable)
Writes assets/textures/ground.png        (1024x1024, tileable, normalised)

Same shape as `art-src/models/*.py`: the painted thing is a source, the thing
the game loads is the output of a script that can be re-run. Nothing under
`assets/` is edited by hand.

Two jobs, and both of them are the reason this file exists rather than the
plate being copied into place.

**Tiling.** `ground.gdshader` repeats this texture at 0.22 per metre and again
at 0.13 of that, so a visible seam is not one line — it is a grid of identical
lines every four and a half metres across a hundred-metre arena, which reads as
a rendering bug rather than as a floor. The plate has no such guarantee: it is
a picture of some ground, and its left edge knows nothing about its right. The
fix is a wrap blend rather than a mirror. Mirroring tiles perfectly and puts a
line of bilateral symmetry through the middle of every tile, and the eye finds
symmetry faster than it finds a seam.

**Brightness.** The floor is this texture times a per-biome tint near 1.0, so
its value is set here and its colour is set there. `BuildGroundTexture`, which
this replaces, wrote a mean of 0.56 and left a note saying that one number is
why the five biome tints did not have to be rewritten -- under a dusk sun 0.34
was asphalt and under a midday one it was asphalt photographed at night. The
plate comes out darker than that and more saturated than that, so it is scaled
to the same mean and pulled back toward neutral. Both tints survive untouched.
"""

import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

SOURCE = os.path.join(HERE, "ground_raw.png")
OUTPUT = os.path.join(ROOT, "assets", "textures", "ground.png")

SIZE = 1024

# How wide the wrap blend is, in pixels of the source.
#
# Wide enough that the ramp is gradual and narrow enough that it does not eat
# the plate: 64 of 1024 costs six per cent of the width and is invisible in the
# result, where 16 leaves a soft but findable band.
FEATHER = 64

# What `BuildGroundTexture` wrote, and what the biome tints were tuned against.
TARGET_MEAN = 0.56

# How far toward grey. The plate is a photograph of brown dirt over grey asphalt
# and carries more colour than a surface that is about to be multiplied by a
# biome tint should: the Ash District's grey and the Flats' olive both have to
# come from the tint, not from what is underneath it. Not all the way to
# neutral, because a perfectly grey floor looks like a debug material.
DESATURATE = 0.50

# Contrast, expanded about the target mean.
#
# The camera is fifteen metres up and the tile covers four and a half, so the
# plate arrives on screen at about a sixth of its own resolution: the gravel
# averages out and what survives is the crack network and the patch variation.
# Those are what a floor is read by at this distance, and normalising a
# photograph's mean shrinks them — the plate that replaced a hand-tuned noise
# field with +/-0.22 of patch swing came out flatter than the noise did. This
# puts the swing back without touching the mean.
CONTRAST = 1.30


def wrap_blend(plate, feather):
    """Crossfades each edge into the opposite one, returning a tileable crop.

    The right `feather` columns are ramped over the left `feather` columns and
    then dropped. Column 0 of the result is the source's column `W - feather`
    and its last column is the source's `W - feather - 1` — adjacent in the
    plate, so they meet without a step. Then the same down the other axis.
    """
    for axis in (1, 0):
        plate = np.swapaxes(plate, 0, axis)

        head, tail = plate[:feather], plate[-feather:]
        ramp = np.linspace(0.0, 1.0, feather).reshape(feather, 1, 1)

        plate = np.concatenate([head * ramp + tail * (1.0 - ramp),
                                plate[feather:-feather]])

        plate = np.swapaxes(plate, 0, axis)

    return plate


def resize_tileable(plate, size):
    """Resizes without breaking the wrap.

    A plain resize filters the outermost pixels against the border rather than
    against the far edge, which puts back a hairline of exactly the seam this
    file exists to remove. Tiling two by two first means every pixel that gets
    filtered has its true neighbours, and the centre quadrant of a periodic
    image is periodic.
    """
    doubled = np.tile(plate, (2, 2, 1))

    image = Image.fromarray(np.clip(doubled * 255.0, 0, 255).astype(np.uint8))
    image = image.resize((size * 2, size * 2), Image.LANCZOS)

    half = size // 2
    return np.asarray(image).astype(np.float64)[half:half + size,
                                                half:half + size] / 255.0


def main():
    plate = np.asarray(Image.open(SOURCE).convert("RGB")).astype(np.float64) / 255.0

    plate = wrap_blend(plate, FEATHER)
    plate = resize_tileable(plate, SIZE)

    # Rec. 709, because the mean that matters is the one the eye reads rather
    # than the average of three channels — a plate with a warm cast has a
    # channel mean above its luminance and would come out dark.
    luma = np.array([0.2126, 0.7152, 0.0722])

    grey = plate @ luma
    plate = plate * (1.0 - DESATURATE) + grey[..., None] * DESATURATE

    plate *= TARGET_MEAN / float((plate @ luma).mean())
    plate = TARGET_MEAN + (plate - TARGET_MEAN) * CONTRAST

    # A shoulder rather than a clamp, and the difference is worth the four
    # lines. Scaling a plate whose brightest gravel is already near white pushes
    # a third of a per cent of it past one, and a hard clamp turns every one of
    # those into pure white — on a floor tiled a thousand times that is a field
    # of specks that no biome tint can pull back, and it is exactly the kind of
    # thing that is invisible in the texture and obvious in the game. `tanh`
    # above the knee is continuous in value and nearly so in slope, so the
    # gravel stays gravel and simply stops getting brighter.
    knee = 0.88
    high = plate > knee
    plate[high] = knee + (1.0 - knee) * np.tanh((plate[high] - knee) / (1.0 - knee))

    # And a toe, for the same reason at the other end. Crushed cracks are less
    # obvious than blown gravel and they cost the same thing: the crack network
    # is most of what survives the downsample, so flattening its darkest pixels
    # to one black is throwing away the detail this plate was fetched for.
    toe = 0.10
    low = plate < toe
    plate[low] = toe - toe * np.tanh((toe - plate[low]) / toe)

    plate = np.clip(plate, 0.0, 1.0)

    Image.fromarray((plate * 255.0 + 0.5).astype(np.uint8)).save(OUTPUT)

    print(f"wrote {OUTPUT} ({SIZE}x{SIZE}, tileable)")
    print(f"  mean {(plate @ luma).mean():.3f}, target {TARGET_MEAN}")
    print(f"  min {plate.min():.3f}  max {plate.max():.3f}  shouldered {int(high.sum())} px  toed {int(low.sum())} px")


if __name__ == "__main__":
    main()
