"""What every painted plate in this folder has to be put through before the game can tile it.

A generative model paints a picture. A picture is not a texture, and the two
differences are the whole of this file:

**A picture does not tile.** Its left edge knows nothing about its right, and
every surface here repeats — the floor every four and a half metres, a body
every ninety centimetres. A seam is therefore never one line; it is a grid of
identical lines across the whole arena, which reads as a rendering bug rather
than as a material.

**A picture has whatever brightness it was painted at.** Every shader that
consumes one of these multiplies it over a colour that was tuned somewhere else
— the biome tints over the floor, the per-variant palette over a body — so the
plate's job is to carry *variation* and the authored colour's job is to carry
level. A plate that arrives dark darkens the palette; one that arrives bright
washes it out; and in both cases the failure looks like a palette problem.

So: tile it, then put its mean where the shader expects it.
"""

import numpy as np
from PIL import Image

# Rec. 709. The mean that matters is the one the eye reads rather than the
# average of three channels — a plate with a warm cast has a channel mean above
# its luminance and would come out dark.
LUMA = np.array([0.2126, 0.7152, 0.0722])


def load(path):
    return np.asarray(Image.open(path).convert("RGB")).astype(np.float64) / 255.0


def save(plate, path):
    Image.fromarray((np.clip(plate, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)).save(path)


def luminance(plate):
    return plate @ LUMA


def srgb_to_linear(plate):
    """What the GPU does to a texture imported as `source_color`, in numpy.

    Both consumers of these plates read them after this conversion, so a mean
    that is meant to land somewhere in the shader has to be measured here.
    """
    return np.where(plate <= 0.04045, plate / 12.92, ((plate + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(plate):
    plate = np.clip(plate, 0.0, 1.0)
    return np.where(plate <= 0.0031308, plate * 12.92, 1.055 * plate ** (1 / 2.4) - 0.055)


def wrap_blend(plate, feather):
    """Crossfades each edge into the opposite one, returning a tileable crop.

    The last `feather` rows are ramped over the first `feather` and then dropped.
    Row 0 of the result is the source's row `H - feather` and its last row is
    `H - feather - 1` — adjacent in the plate, so they meet without a step. Then
    the same down the other axis.

    A crossfade rather than a mirror. Mirroring tiles perfectly and puts an axis
    of symmetry through the middle of every tile, and the eye finds symmetry
    faster than it finds a seam.
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


def shoulder(plate, knee=0.88, toe=0.10):
    """Rolls the ends off instead of clipping them.

    A hard clamp turns every over-bright texel into pure white, and on a surface
    tiled a thousand times that is a field of specks no tint can pull back — the
    kind of thing that is invisible in the texture and obvious in the game.
    `tanh` past the knee is continuous in value and nearly so in slope, so
    highlights stay highlights and simply stop getting brighter. The toe does the
    same for the crack network at the other end, which is most of what survives
    a downsample.
    """
    plate = plate.copy()

    high = plate > knee
    plate[high] = knee + (1.0 - knee) * np.tanh((plate[high] - knee) / (1.0 - knee))

    low = plate < toe
    plate[low] = toe - toe * np.tanh((toe - plate[low]) / toe)

    return np.clip(plate, 0.0, 1.0)


def grade(plate, target_mean, contrast=1.0, desaturate=0.0, linear=False):
    """Puts the plate's mean where the shader expects it.

    `linear=True` measures and sets the mean *after* the sRGB decode the GPU
    does, which is the space a shader that reads `texture(...)` is working in.
    The floor's shader multiplies the stored value directly and wants the mean in
    storage space; a body's remaps the decoded one and wants it here. Getting
    this backwards is a plate that looks correct in an image viewer and lands
    half a stop out in the game.
    """
    if desaturate > 0.0:
        grey = luminance(plate)
        plate = plate * (1.0 - desaturate) + grey[..., None] * desaturate

    if linear:
        work = srgb_to_linear(plate)
        work *= target_mean / max(1e-6, float(luminance(work).mean()))
        work = target_mean + (work - target_mean) * contrast
        return shoulder(linear_to_srgb(work))

    plate = plate * (target_mean / max(1e-6, float(luminance(plate).mean())))
    plate = target_mean + (plate - target_mean) * contrast
    return shoulder(plate)
