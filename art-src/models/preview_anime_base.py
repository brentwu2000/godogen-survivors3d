import bpy
from mathutils import Vector

ROOT = r"C:\Projcet\godogen-survivors3d"
SOURCE = ROOT + r"\art-src\models\base\rigged_anime_girl_cc0.glb"
PREVIEW = ROOT + r"\screenshots\rigged_anime_girl_cc0_preview.png"
BLEND = ROOT + r"\art-src\models\base\rigged_anime_girl_cc0.blend"

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=SOURCE)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
points = []
for obj in meshes:
    for corner in obj.bound_box:
        points.append(obj.matrix_world @ Vector(corner))
    for poly in obj.data.polygons:
        poly.use_smooth = True

lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
center = (lo + hi) * .5
height = hi.z - lo.z

bpy.ops.mesh.primitive_plane_add(size=12, location=(center.x, center.y, lo.z-.01))
ground=bpy.context.object
gm=bpy.data.materials.new('Preview Ground'); gm.diffuse_color=(.025,.028,.032,1)
ground.data.materials.append(gm)

bpy.ops.object.light_add(type='AREA', location=(center.x-2.5, center.y-3.8, center.z+2.5))
key=bpy.context.object; key.data.energy=1150; key.data.size=3.2
bpy.ops.object.light_add(type='AREA', location=(center.x+2.7, center.y+.8, center.z+1.5))
fill=bpy.context.object; fill.data.energy=800; fill.data.color=(.25,.42,1); fill.data.size=2.5

bpy.ops.object.camera_add(location=(center.x+2.6, center.y-4.8, center.z+.35))
cam=bpy.context.object; bpy.context.scene.camera=cam
cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.lens=70

scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=900; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=PREVIEW
scene.world.color=(.008,.011,.016); scene.view_settings.look='AgX - Medium High Contrast'

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.render.render(write_still=True)
print('ANIME_BASE_PREVIEW_OK', len(meshes), round(height,3), PREVIEW, BLEND)
