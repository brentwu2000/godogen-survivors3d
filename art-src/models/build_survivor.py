import bpy
import math
from mathutils import Vector

ROOT = r"C:\Projcet\godogen-survivors3d"
OUT = ROOT + r"\assets\models\survivor_apocalypse.glb"
PREVIEW = ROOT + r"\screenshots\survivor_apocalypse_preview.png"
DETAIL = ROOT + r"\assets\textures\survivor_handpainted.png"

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def mat(name, color, metallic=0.0, rough=0.72, texture=False):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = rough
    if texture:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(DETAIL, check_existing=True)
        tex.projection = 'BOX'; tex.projection_blend = 0.24
        coord = m.node_tree.nodes.new('ShaderNodeTexCoord')
        scale = m.node_tree.nodes.new('ShaderNodeMapping')
        scale.inputs['Scale'].default_value = (3.4, 3.4, 3.4)
        mix = m.node_tree.nodes.new('ShaderNodeMixRGB')
        mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 0.32
        mix.inputs[2].default_value = (*color, 1)
        m.node_tree.links.new(coord.outputs['Generated'], scale.inputs['Vector'])
        m.node_tree.links.new(scale.outputs['Vector'], tex.inputs['Vector'])
        m.node_tree.links.new(tex.outputs['Color'], mix.inputs[1])
        m.node_tree.links.new(mix.outputs['Color'], bs.inputs['Base Color'])
    return m

skin = mat('Skin_warm_dusty', (0.56, 0.36, 0.27), rough=.78)
skin_shadow = mat('Skin_shadow', (0.26, 0.12, 0.10), rough=.85)
hair = mat('Hair_ash_black', (0.035, 0.045, 0.055), rough=.48)
cloth = mat('Jacket_blue_canvas', (0.055, 0.11, 0.18), rough=.82, texture=True)
cloth2 = mat('Undershirt_charcoal', (0.055, 0.06, 0.065), rough=.92, texture=True)
leather = mat('Leather_weathered', (0.16, 0.075, 0.035), rough=.72, texture=True)
armor = mat('Armor_chipped_steel', (0.12, 0.15, 0.17), metallic=.58, rough=.48, texture=True)
rubber = mat('Boot_rubber', (0.025, 0.028, 0.03), rough=.9)
accent = mat('Emergency_orange', (0.48, 0.12, 0.035), metallic=.1, rough=.65)
eye = mat('Eyes', (0.045, 0.075, 0.085), rough=.24)
white = mat('Eye_white', (0.7, 0.66, 0.58), rough=.55)

def smooth(obj, bevel=0.0):
    if hasattr(obj.data, 'polygons'):
        for p in obj.data.polygons: p.use_smooth = True
    if bevel:
        mod = obj.modifiers.new('Edge softness', 'BEVEL'); mod.width = bevel; mod.segments = 2
    return obj

def uv(name, loc, scale, material, seg=32, rings=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, location=loc)
    o=bpy.context.object; o.name=name; o.scale=scale; bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material); return smooth(o)

def cube(name, loc, scale, material, rot=(0,0,0), bevel=.018):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    o=bpy.context.object; o.name=name; o.scale=scale; bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material); return smooth(o, bevel)

def cyl(name, a, b, radius, material, vertices=20):
    a,b=Vector(a),Vector(b); d=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=d.length, location=(a+b)/2)
    o=bpy.context.object; o.name=name; o.rotation_mode='QUATERNION'; o.rotation_quaternion=d.to_track_quat('Z','Y')
    o.data.materials.append(material); return smooth(o, .006)

# Anatomy: 1.78 m survivor, stylized realistic proportions with a slightly larger
# head so facial information survives the top-down camera.
uv('Head', (0,-.012,1.585), (.122,.108,.145), skin)
uv('Jaw', (0,-.052,1.525), (.09,.075,.085), skin)
uv('Neck', (0,0,1.405), (.061,.058,.085), skin)

# Face geometry, not painted dots.
for x in (-.044,.044):
    uv('Eye', (x,-.105,1.61), (.031,.012,.018), white, 20, 12)
    uv('Iris', (x,-.116,1.61), (.012,.006,.013), eye, 16, 10)
    cyl('Brow', (x-.025,-.111,1.641),(x+.025,-.113,1.645),.006,hair,10)
uv('Nose', (0,-.122,1.575), (.018,.022,.036), skin_shadow, 18, 12)
cyl('Mouth', (-.036,-.119,1.523),(.036,-.119,1.523),.006,skin_shadow,12)

# Layered asymmetrical hair clumps and tied back tail.
uv('Hair_cap',(0,.008,1.64),(.132,.119,.135),hair)
for x,z,rz,s in [(-.085,1.60,-.16,.72),(-.045,1.68,-.08,1.0),(0,1.70,.02,1.05),(.052,1.68,.1,.92),(.095,1.60,.2,.7)]:
    cube('Hair_clump',(x,-.07,z),(.035,.025,.13*s),hair,(0,rz,0),.025)
cyl('Hair_tail',(0,.08,1.62),(.055,.12,1.31),.047,hair,16)

# Torso layers: undershirt, canvas jacket, raised collar and armor panels.
uv('Torso',(0,.012,1.19),(.205,.125,.285),cloth2)
# Rounded jacket volumes follow the ribcage instead of replacing it with a box.
uv('Jacket_left',(-.092,-.016,1.205),(.122,.112,.275),cloth,28,18)
uv('Jacket_right',(.092,-.016,1.205),(.122,.112,.275),cloth,28,18)
for x in (-.105,.105):
    cube('Lapel',(x,-.125,1.30),(.075,.018,.145),cloth,(0,0,x*.9),.022)
for x in (-.105,.105): cube('Collar',(x,-.055,1.405),(.06,.06,.085),cloth,(0,x*.8,0),.035)
cube('Chest_armor',(0,-.135,1.22),(.145,.028,.125),armor,(0,0,0),.038)
cube('Chest_stripe',(0,-.175,1.19),(.125,.012,.022),accent,(0,0,-.08),.008)
cyl('Belt',(-.2,-.01,.94),(.2,-.01,.94),.034,leather,16)
cube('Buckle',(0,-.135,.94),(.042,.025,.034),armor,bevel=.01)

# Arms with shoulder protection, gloves and visible elbow articulation.
for side in (-1,1):
    x=side*.255
    uv('Shoulder', (x,0,1.35),(.105,.105,.105),cloth)
    uv('Shoulder_plate',(x,-.04,1.37),(.105,.09,.062),armor,24,14)
    cyl('Upper_arm',(x,0,1.31),(side*.30,-.005,1.12),.075,cloth,20)
    uv('Elbow',(side*.30,-.005,1.10),(.077,.074,.07),armor)
    cyl('Forearm',(side*.30,-.005,1.08),(side*.285,-.045,.88),.066,cloth2,20)
    cube('Glove',(side*.285,-.05,.84),(.073,.07,.078),leather,bevel=.025)

# Trousers, knee pads and substantial boots.
uv('Pelvis',(0,.01,.86),(.175,.115,.13),cloth2,28,16)
for side in (-1,1):
    x=side*.105
    cyl('Thigh',(x,0,.82),(side*.12,.015,.55),.105,cloth2,22)
    uv('Knee',(side*.12,-.06,.50),(.11,.085,.095),armor)
    cyl('Shin',(side*.12,.02,.47),(side*.13,.01,.20),.086,cloth2,22)
    uv('Boot',(side*.13,-.025,.13),(.105,.13,.145),rubber,24,14)
    uv('Boot_toe',(side*.13,-.14,.075),(.11,.14,.072),rubber,24,14)

# Survival pack, bedroll, canteen and antenna create the gameplay silhouette.
cube('Backpack',(0,.145,1.18),(.18,.12,.255),leather,(0,0,0),.055)
cube('Pack_flap',(0,.272,1.285),(.17,.025,.085),cloth,bevel=.025)
cyl('Bedroll',(-.17,.18,.97),(.17,.18,.97),.068,cloth,18)
uv('Canteen',(.22,.12,1.02),(.055,.045,.085),armor,20,12)
cyl('Antenna',(.14,.2,1.36),(.18,.2,1.68),.008,armor,8)

# Readable service rifle mounted across the front.
cyl('Rifle_barrel',(-.22,-.24,1.22),(.30,-.24,.91),.024,armor,12)
cube('Rifle_body',(.0,-.245,1.08),(.17,.045,.055),armor,(0,-.52,0),.018)
cube('Rifle_stock',(-.22,-.23,1.23),(.11,.052,.075),leather,(0,-.52,0),.022)
cube('Rifle_mag',(.08,-.25,.98),(.04,.035,.075),armor,(0,0,-.16),.012)
cube('Sight',(.07,-.27,1.16),(.035,.022,.025),accent,bevel=.008)

# Grounded studio preview.
bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.01))
ground=bpy.context.object; ground.data.materials.append(mat('Preview_ground',(0.055,.06,.055),rough=.9))

bpy.ops.object.light_add(type='AREA', location=(-3,-4,5)); key=bpy.context.object; key.data.energy=900; key.data.shape='DISK'; key.data.size=4
bpy.ops.object.light_add(type='AREA', location=(3,1,3)); fill=bpy.context.object; fill.data.energy=650; fill.data.color=(.35,.52,1); fill.data.size=3
bpy.ops.object.light_add(type='POINT', location=(0,2,2)); bpy.context.object.data.energy=180

bpy.ops.object.camera_add(location=(3.15,-5.4,2.55))
cam=bpy.context.object; bpy.context.scene.camera=cam
def track(obj, point): obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()
track(cam,(0,0,1.0)); cam.data.lens=66

scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=900; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=PREVIEW
scene.world.color=(.018,.022,.025)
scene.view_settings.look='AgX - Medium High Contrast'
bpy.ops.wm.save_as_mainfile(filepath=ROOT+r"\art-src\models\survivor_apocalypse.blend")
bpy.ops.render.render(write_still=True)

# Do not export preview-only ground/lights/camera.
for o in list(bpy.context.scene.objects): o.select_set(o.type=='MESH' and o.name!='Plane')
bpy.context.view_layer.objects.active=bpy.context.selected_objects[0]
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True,
                          export_apply=True, export_materials='EXPORT')
print('SURVIVOR_OK', OUT, PREVIEW)
