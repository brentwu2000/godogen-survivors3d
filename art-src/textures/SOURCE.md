# Where the painted texture plates came from

Same purpose as `assets/models/SOURCE.md` and `art-src/fonts/SOURCE.md`: the record the project
owes itself for anything it did not compute. These plates are neither computed nor third-party,
which is a third case and is why this file exists rather than a row being added to one of those.

## The plates

| File | Made with | Output | Consumed by |
| :--- | :--- | :--- | :--- |
| `ground_raw.png` | OpenAI `gpt-image` via the Codex CLI's built-in image tool, 2026-09-07 | 1024×1024 | `make_ground.py` → `assets/textures/ground.png` |
| `skin_infected_raw.png` | same, 2026-09-07 | 1024×1024 | `make_body_skin.py` → `assets/textures/skin_infected.png` |
| `skin_mutant_raw.png` | same, 2026-09-07 | 1024×1024 | `make_body_skin.py` → `assets/textures/skin_mutant.png` |
| `skin_survivor_raw.png` | the project's own earlier painting, carried over unchanged | 1240×1240 | `make_body_skin.py` → `assets/textures/skin_survivor.png` |
| `face_infected_raw.png` | OpenAI `gpt-image` via the Codex CLI's built-in image tool, 2026-09-07 | 1024×1024 | `make_body_atlas.py` → the head layer |
| `face_mutant_raw.png` | same, 2026-09-07 | 1024×1024 | `make_body_atlas.py` → the head layer |
| `face_survivor_raw.png` | same, 2026-09-07 | 1024×1024 | `make_body_atlas.py` → the head layer |

`plate.py` is what the three `make_*.py` share: the wrap blend that makes a
painting tile, the tileable resize, the shoulder-and-toe curve, and the grade
that puts a plate's mean where its shader expects it.

## Licence

**Nothing to track, and that is the point rather than a shrug.** These are outputs of a generative
model run by this project from this project's own prompt; OpenAI assigns output ownership to the
requester, there is no upstream author to credit and no downstream restriction to inherit. That is
the same position `BuildAudio`'s synthesised sound and `PropLibrary`'s boxes are in, and a stronger
one than the Quaternius models are in — see the QAL discussion in `assets/models/SOURCE.md`.

The prompt is kept below rather than summarised, because it is the only reproducible thing about a
generated image. Re-running it gives a different plate; nothing downstream cares, because
`make_ground.py` normalises whatever it is handed to the mean and contrast the biome tints were
tuned against.

### The prompts

#### `ground_raw.png`

> Top-down orthographic view of weathered post-industrial ground, filling the entire square frame
> edge to edge. Cracked pale-grey asphalt broken up by patches of dry compacted brown dirt,
> scattered fine gravel and grit, hairline cracks packed with dark dust, a scatter of small pale
> stones, faint tyre-polished streaks. Flat even overcast lighting, no cast shadows from any object,
> no vignette, no depth of field. Evenly distributed detail with no focal point and no composition.
> Desaturated near-neutral grey-brown, low contrast, so it can be colour graded later. Photographic
> material study. No objects, no plants, no people, no text, no watermark, no frame, no border.

#### `skin_infected_raw.png`

> Flat top-down material study of rotting undead skin, filling the entire square frame edge to edge.
> Grey-green mottled flesh with darker bruised patches, raised veins, small splits showing dark red
> beneath, and scraps of filthy torn grey cloth adhering in places. Even overcast lighting, no cast
> shadows from any object, no vignette, no depth of field. Evenly distributed detail with no focal
> point and no composition. Desaturated, mid-value, low contrast so it can be colour graded later.
> No objects, no people, no faces, no text, no watermark, no frame, no border.

#### `skin_mutant_raw.png`

> Flat top-down material study of thick monstrous hide, filling the entire square frame edge to edge.
> Leathery grey skin with hardened calcified plates and knotted scar ridges, deep cracks between the
> plates packed with grime, coarse pores. Even overcast lighting, no cast shadows from any object, no
> vignette, no depth of field. Evenly distributed detail with no focal point and no composition.
> Desaturated, mid-value, low contrast so it can be colour graded later. No objects, no people, no
> faces, no text, no watermark, no frame, no border.

#### The three faces

All three share one frame, and the frame is most of the prompt: *the face fills
the frame edge to edge, is bilaterally symmetric, is seen straight on with no
perspective and no turn of the head, and is lit flatly and evenly with no cast
shadows and no vignette. The eyes are open and looking straight ahead. No hair
above the brow, no neck, no shoulders, no background scene.* Then one line each:

> **infected** — a rotting undead face. Sunken grey-green skin stretched over
> the skull, dark hollow eye sockets with small pale clouded eyes, a torn split
> lip showing teeth, dark veins at the temples, a bruised patch on one cheek.

> **mutant** — a heavy brutish mutant face. Thick leathery grey skin with
> hardened calcified ridges across the brow and cheekbones, deep-set small dark
> eyes, a broad heavy jaw with a wide clenched mouth, coarse pores and old scars.

> **survivor** — a weathered living human face, mid-thirties, tired and hard.
> Tanned dirt-streaked skin, dark eyes, stubble on the jaw, a small healed scar
> over one eyebrow.

"Fills the frame, no neck, no hair" is the load-bearing part. `make_body_atlas.py`
sets each face into an oval on the front of an equirectangular head, and anything
the model draws outside the face — a neck, a collar, a background — lands on the
head as a stripe of it.

## Two things a prompt for this game has to do

**Never ask for a transparent background.** The generator draws a checkerboard, every time, and it
is a checkerboard of pixels rather than an alpha channel — so it survives into the file and has to
be matted out of something that never had transparency to begin with. Ask for a plain background
and remove it afterwards.

**Never ask for "seamless" or "tileable".** It produces a picture *of* tiles, or a frame drawn
around the edge to mark where the repeat is. Ask for the material filling the frame and make it
tile in the script, where the result can be checked rather than hoped for.

## The one that did not pay, kept so nobody buys it twice

A cloud plate was painted for `ProceduralSkyMaterial.sky_cover`, mapped to an equirectangular upper
hemisphere and wired into `BuildMain`. Not one cloud was ever drawn. The camera tilts 26° down and
`Fov` is Godot's *vertical* field at 52, so the top edge of the frame sits at 0° elevation: the
horizon is the top of the screen, and the pale band that reads as sky is the fogged far end of the
arena. Both the plate and the script were deleted; the arithmetic is in `BuildMain.BuildSky` where
the next person will be standing when they have the same idea.
