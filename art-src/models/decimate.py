"""Cuts an authored character down to a triangle count a horde can afford.

    blender --background --python art-src/models/decimate.py -- <in> <out> <triangles>

A `MultiMesh` draws one mesh N times, so the cost of a variant is its triangle
count times how many of it are on screen — a hundred and fifty, at the cap. The
procedural bodies are about 460 triangles each, which is 70,000 for a full horde
and holds a 145 fps median. A downloaded character is typically twenty to thirty
thousand: at 150 instances that is three to four *million*, which is not a tuning
problem, it is a different game.

So anything authored has to come down before `BakeBody` sees it. This is that
step, and it is a Blender script rather than a Godot one because Godot exposes no
mesh simplifier to C# and Blender's Decimate has been the right answer to this
since before Godot existed.

**Collapse, not un-subdivide or planar.** Collapse is quadric edge collapse: it
keeps the silhouette, keeps vertex groups, and keeps UVs, which is every channel
`BakeBody` reads. Planar decimation would flatten a rounded limb into a prism and
is only correct for architecture; un-subdivide needs a subdivided source.

**One ratio for the whole model, not one per part.** Deciding per object gives
the head the same budget as the coat and reads as a smooth body with a faceted
face — the head is where the triangles should go and the only way to say so with
a global ratio is to let the model's own density say it. A model whose author put
detail in the face keeps proportionally more of it there.

Skinning survives because a collapse interpolates vertex groups, and the
animations survive untouched — they are on the armature, which this does not
open. That matters: `BakeBody`'s `pose:idle` runs one frame of the model's own
animation before reading the geometry, because a rigged model's rest pose is a
T-pose and a T-pose is not a body.
"""

import os
import sys

import bpy


def argv():
    if "--" not in sys.argv:
        raise SystemExit("usage: blender --background --python decimate.py -- "
                         "<in.glb> <out.glb> <triangles>")

    args = sys.argv[sys.argv.index("--") + 1:]
    if len(args) < 3:
        raise SystemExit(f"expected 3 arguments, got {len(args)}: {args}")

    return args[0], args[1], int(args[2])


def triangles(objects):
    total = 0
    for obj in objects:
        mesh = obj.data
        for polygon in mesh.polygons:
            total += max(1, len(polygon.vertices) - 2)

    return total


def main():
    source, target, budget = argv()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit(f"{source} has no mesh objects")

    before = triangles(meshes)
    if before <= budget:
        print(f"  {before} triangles is already inside {budget} — copied unchanged")
    else:
        ratio = budget / before

        for obj in meshes:
            modifier = obj.modifiers.new(name="Decimate", type="DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = ratio

            # `use_collapse_triangulate` makes the result all triangles rather
            # than a mix. `BakeBody` reads Godot's own surface arrays, which are
            # triangles either way, but a quad that the exporter has to split is
            # a split it makes rather than one this file chose.
            modifier.use_collapse_triangulate = True

            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.modifier_apply(modifier="Decimate")

    after = triangles([o for o in bpy.context.scene.objects if o.type == "MESH"])

    os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=target,
        export_format="GLB",

        # Everything `BakeBody` reads, and nothing it does not. Skins and
        # animations are the two that are easy to lose and impossible to notice:
        # a body exported without its skin bakes in its T-pose, and one exported
        # without its animations bakes in its T-pose as well, from the other
        # cause.
        export_skins=True,
        export_animations=True,
        export_materials="EXPORT",
        export_apply=False,
    )

    print(f"decimated {source}")
    print(f"  {before} -> {after} triangles against a budget of {budget}")
    print(f"  -> {target}")


main()
