"""Builds the layered body atlas: one square image per part, per body category.

    python art-src/textures/make_body_atlas.py

Reads  assets/textures/skin_<category>.png   (the graded, tileable material)
       art-src/textures/face_<category>_raw.png (a painted front-on face)
Writes assets/textures/body/<category>_<layer>.png

`BodyAtlas` is the layout and `BodyAtlas.LayerNames` is the order — these files
are stacked into one `Texture2DArray` at load, so **every layer must be square
and the same size**, and a `Texture2DArray` that is handed layers which disagree
refuses them with a warning and leaves the bodies untextured. `BodyProbe` checks
it rather than trusting this comment.

**A layer per part rather than one packed atlas, because of mipmaps.** A body is
about 120 pixels tall at this camera, so its head samples a 16- or 32-pixel mip;
in a packed atlas the filter at that level averages across cell boundaries and
the face acquires a fringe of whatever was painted beside it. Layers have no
boundaries to bleed across.

The head layer is the only one that is *composed* rather than copied. It is
equirectangular — `MeshBuilder.Ball` runs u round the azimuth from +X toward +Z
and v from the crown down — and a body faces -Z, so the face goes at u = 0.75.
The head is eight segments around, whose front two span exactly u 0.625 to 0.875,
which is why the face is placed to fill that and no more: painted wider it would
wrap onto a facet that is turning away, and the eye that lands on that facet
would be on the side of the head.
"""

import os

import numpy as np
from PIL import Image

import plate as pl

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "assets", "textures", "body")

# `BodyAtlas.LayerSize`, and `BodyAtlas.LayerNames` in order.
SIZE = 512
LAYERS = ["flat", "head", "torso", "limb", "cloth", "kit"]

# What "no texture at all" is worth. The shader centres its modulation on
# `surface_detail_mid` in decoded space, so a flat layer has to sit exactly
# there or every undressed part comes out lighter or darker than its own colour.
NEUTRAL_STORED = float(pl.linear_to_srgb(np.array([[[0.21]]])).ravel()[0])

CATEGORIES = ["infected", "mutant", "survivor"]

# Where the face sits on the equirectangular head, as fractions.
#
# Horizontally: the two front facets of an eight-segment ball, and no wider.
# Vertically: from the brow line down to under the chin. The crown and the nape
# are skin, which is what a face map is mostly made of.
# Slightly wider than the two front facets, so the painting runs onto the pair
# that are already turning away rather than ending in a hard edge exactly on a
# facet boundary — which is the one place a seam is guaranteed to be visible.
FACE_U = (0.570, 0.930)

# How far down the head the face sits, per category.
#
# The head is five rings, so its bands are polar 0-36-72-108-144-180 degrees,
# and below about 0.66 the sphere has turned under far enough that the camera —
# which looks *down* at 26 degrees — sees the surface at a grazing angle and the
# ambient-occlusion term has darkened it. A mouth painted there is a mouth in
# shadow, which is where the first attempt put every one of them.
#
# The survivor is lower because of the cap. Its brim sits at 0.46 of a head
# radius above centre, which is `acos(0.46)` = 63 degrees of polar, or v = 0.35 —
# so a face starting any higher has its eyes underneath a hat.
FACE_V = {
    "infected": (0.18, 0.64),
    "mutant": (0.18, 0.64),
    "survivor": (0.35, 0.79),
}


def square(image, size=SIZE):
    return np.asarray(
        Image.fromarray((np.clip(image, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8))
        .resize((size, size), Image.LANCZOS)).astype(np.float64) / 255.0


def oval_mask(height, width, softness=0.28):
    """A feathered ellipse, so the face is set into the skin rather than pasted.

    A rectangular paste puts four straight seams across a head, and a head is the
    one part of a body anybody looks at. The falloff is on the radius rather than
    per-axis so the corners go first, which is where a face has no business
    being anyway.
    """
    ys = (np.arange(height) + 0.5) / height * 2.0 - 1.0
    xs = (np.arange(width) + 0.5) / width * 2.0 - 1.0
    radius = np.sqrt(ys[:, None] ** 2 + xs[None, :] ** 2)

    mask = np.clip((1.0 - radius) / softness, 0.0, 1.0)
    return (mask * mask * (3.0 - 2.0 * mask))[..., None]


def head_layer(skin, face_path, band):
    """Skin all the way round, with a face set into the front of it."""
    head = skin.copy()

    if not os.path.exists(face_path):
        print(f"  {os.path.basename(face_path)} is missing — head layer is plain skin")
        return head

    u0, u1 = int(FACE_U[0] * SIZE), int(FACE_U[1] * SIZE)
    v0, v1 = int(band[0] * SIZE), int(band[1] * SIZE)

    face = pl.load(face_path)
    face = np.asarray(
        Image.fromarray((face * 255.0 + 0.5).astype(np.uint8))
        .resize((u1 - u0, v1 - v0), Image.LANCZOS)).astype(np.float64) / 255.0

    # Graded to the skin it is being set into, so the face is the same material
    # as the head rather than a photograph stuck to one. Only the mean moves:
    # the features are the whole point and flattening their contrast to match
    # would erase them.
    face *= float(pl.luminance(skin).mean()) / max(1e-6, float(pl.luminance(face).mean()))
    face = pl.shoulder(face)

    mask = oval_mask(v1 - v0, u1 - u0)
    head[v0:v1, u0:u1] = head[v0:v1, u0:u1] * (1.0 - mask) + face * mask

    return head


def build(category):
    skin_path = os.path.join(ROOT, "assets", "textures", f"skin_{category}.png")
    if not os.path.exists(skin_path):
        print(f"  {skin_path} is missing — skipped")
        return

    skin = square(pl.load(skin_path))

    layers = {
        "flat": np.full((SIZE, SIZE, 3), NEUTRAL_STORED),
        "head": head_layer(skin, os.path.join(HERE, f"face_{category}_raw.png"),
                           FACE_V[category]),
        "torso": skin,

        # Rolled a third and a half turn, so an arm and a leg standing side by
        # side are not the same picture. Rolling is free and tiles by
        # construction, where a second painting is another plate to grade.
        "limb": np.roll(skin, (SIZE // 3, SIZE // 7), axis=(0, 1)),
        "cloth": np.roll(skin, (SIZE // 2, SIZE // 3), axis=(0, 1)),
        "kit": np.roll(skin, (SIZE // 5, SIZE // 2), axis=(0, 1)),
    }

    os.makedirs(OUT, exist_ok=True)
    for name in LAYERS:
        pl.save(layers[name], os.path.join(OUT, f"{category}_{name}.png"))

    print(f"{category}: {len(LAYERS)} layers at {SIZE}x{SIZE}")


for name in CATEGORIES:
    build(name)

print(f"neutral stored value {NEUTRAL_STORED:.3f}")
