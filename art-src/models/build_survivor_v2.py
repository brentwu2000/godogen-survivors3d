import bpy
import bmesh
from mathutils import Vector
from bl_ext.blender_org.mpfb.services import HumanService, TargetService

ROOT = r"C:\Projcet\godogen-survivors3d"
BLEND = ROOT + r"\art-src\models\survivor_apocalypse_v2.blend"
PREVIEW = ROOT + r"\screenshots\survivor_body_v2.png"
GLB = ROOT + r"\assets\models\survivor_apocalypse_v2.glb"

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

macro = TargetService.get_default_macro_info_dict()
macro['race']['african'] = 0.08
macro['race']['asian'] = 0.72
macro['race']['caucasian'] = 0.20
macro['gender'] = 0.08       # female, with enough structure for a field scout
macro['age'] = 0.48          # young adult
macro['muscle'] = 0.56       # athletic rather than body-builder
macro['weight'] = 0.42
macro['proportions'] = 0.58  # long, readable action-game silhouette
macro['height'] = 0.53
macro['cupsize'] = 0.34
macro['firmness'] = 0.62

body = HumanService.create_human(mask_helpers=False, detailed_helpers=False,
                                 extra_vertex_groups=True, feet_on_ground=True,
                                 scale=0.1, macro_detail_dict=macro)
body.name = 'SurvivorBody'

# A neutral technical-suit preview material. The final body is never exposed in
# game; this keeps anatomy review focused on silhouette and joint topology while
# the authored jacket, trousers and armor are built over it.
mat = bpy.data.materials.new('TechnicalSuit_Blockout')
mat.diffuse_color = (0.035, 0.055, 0.075, 1)
mat.use_nodes = True
bs = mat.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value = (0.025, 0.045, 0.065, 1)
bs.inputs['Roughness'].default_value = 0.82
body.data.materials.clear(); body.data.materials.append(mat)

# Preserve the full-resolution sculpt source, while previewing the same smooth
# surface that garments will be fitted against.
subd = body.modifiers.new('Preview subdivision', 'SUBSURF')
subd.levels = 1; subd.render_levels = 1
for p in body.data.polygons: p.use_smooth = True

def material(name, color, metallic=0.0, roughness=0.65):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    return m

navy = material('Jacket weathered navy', (.028,.055,.075), 0.05, .8)
charcoal = material('Reinforced charcoal trousers', (.025,.028,.032), 0.02, .88)
orange = material('Signal orange scarf', (.55,.09,.018), 0.0, .72)
steel = material('Prosthetic gunmetal', (.07,.085,.09), .72, .34)
rubber = material('Prosthetic joint rubber', (.012,.014,.015), .1, .9)
hairmat = material('Blue black hair', (.006,.012,.018), .08, .3)
leather = material('Aged utility leather', (.12,.055,.025), .0, .82)

def shell_from_body(name, keep, mat, thickness=.006, offset=.004):
    obj = body.copy(); obj.data = body.data.copy(); bpy.context.collection.objects.link(obj)
    obj.name = name
    for mod in list(obj.modifiers): obj.modifiers.remove(mod)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep(v)], context='VERTS')
    bm.to_mesh(obj.data); bm.free()
    obj.data.materials.clear(); obj.data.materials.append(mat)
    sol = obj.modifiers.new('Tailored thickness', 'SOLIDIFY'); sol.thickness=thickness; sol.offset=offset
    bev = obj.modifiers.new('Soft garment edge', 'BEVEL'); bev.width=.0025; bev.segments=2
    for p in obj.data.polygons: p.use_smooth=True
    return obj

# Clothing is extracted from the anatomical surface, preserving folds and the
# correct shoulder, hip and limb contours rather than enclosing them in blocks.
jacket = shell_from_body('Cropped field jacket', lambda v: .83 < v.co.z < 1.35, navy, .009, .5)
trousers = shell_from_body('Armoured field trousers', lambda v: .08 < v.co.z < .94, charcoal, .008, .45)
boots = shell_from_body('Survivor combat boots', lambda v: v.co.z < .30, leather, .014, .55)

def cube(name, loc, scale, mat, bevel=.02, rotation=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rotation)
    o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    b=o.modifiers.new('Worn bevel','BEVEL'); b.width=bevel; b.segments=3
    o.data.materials.append(mat); return o

def cyl(name, loc, radius, depth, mat, rotation=(0,0,0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=radius, depth=depth, location=loc, rotation=rotation)
    o=bpy.context.object; o.name=name; o.data.materials.append(mat)
    b=o.modifiers.new('Machined bevel','BEVEL'); b.width=.008; b.segments=2
    for p in o.data.polygons: p.use_smooth=True
    return o

# Signature asymmetric coat tail and layered waist armour.
tail = cube('Asymmetric torn coat tail', (-.19,.045,.69), (.19,.025,.34), navy, .025, (0.10,0.02,-0.06))
cube('Orange waist tab', (.16,-.055,.80), (.055,.018,.16), orange, .012, (-.08,0,.10))
cube('Utility belt', (0,-.035,.89), (.33,.035,.035), leather, .012)
for x in (-.25,.25): cube('Belt pouch', (x,-.075,.80), (.075,.04,.10), leather, .018)

# Scarf: two overlapping rounded bands plus a wind-swept loose end.
bpy.ops.mesh.primitive_torus_add(major_radius=.125, minor_radius=.026, major_segments=40, minor_segments=10,
                                location=(0,0,1.33), rotation=(0,0,0))
scarf=bpy.context.object; scarf.name='Signal scarf'; scarf.scale=(1.08,.88,.72); scarf.data.materials.append(orange)
cube('Scarf loose end', (-.15,.07,1.17), (.055,.022,.19), orange, .025, (-.16,.08,-.18))

# Mechanical right forearm: slim layered plates retain an anime-action silhouette.
cyl('Mechanical forearm core', (-.40,-.015,.84), .055, .34, steel, (0,.18,-.18))
for z in (.72,.83,.95):
    cyl('Prosthetic armour ring', (-.40 + (z-.84)*.16,-.015,z), .071, .045, steel, (0,.18,-.18))
cyl('Orange prosthetic accent', (-.42,-.018,.87), .073, .025, orange, (0,.18,-.18))

# Face-framing bob and the concept's distinct braided side strand.
bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, location=(0,.045,1.465), scale=(.142,.120,.150))
hair=bpy.context.object; hair.name='Asymmetric cropped hair'; hair.data.materials.append(hairmat)
for p in hair.data.polygons: p.use_smooth=True
for i,z in enumerate((1.34,1.25,1.16,1.08)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, location=(.14,.035,z), scale=(.043,.038,.068))
    bead=bpy.context.object; bead.name=f'Braided hair {i+1}'; bead.data.materials.append(hairmat)

# Compact apocalypse backpack and mask canister make the rear silhouette useful.
cube('Field backpack', (0,.135,1.08), (.22,.085,.25), charcoal, .035)
cyl('Filter canister', (-.27,-.08,.68), .075, .17, steel, (1.5708,0,0))
cube('Shoulder armour', (.25,-.005,1.25), (.13,.065,.065), steel, .025, (0,.08,.12))

# Ground and restrained studio lighting.
bpy.ops.mesh.primitive_plane_add(size=20, location=(0,0,-0.006))
ground = bpy.context.object; ground.name='PreviewGround'
gm = bpy.data.materials.new('PreviewGroundMat'); gm.diffuse_color=(.035,.04,.038,1)
ground.data.materials.append(gm)

bpy.ops.object.light_add(type='AREA', location=(-3,-4,5))
bpy.context.object.data.energy=1000; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=4
bpy.ops.object.light_add(type='AREA', location=(3,0,3))
bpy.context.object.data.energy=700; bpy.context.object.data.color=(.32,.48,1); bpy.context.object.data.size=3

bpy.context.view_layer.update()
box = body.evaluated_get(bpy.context.evaluated_depsgraph_get()).bound_box
world = [body.matrix_world @ Vector(v) for v in box]
centre = sum(world, Vector()) / 8
height = max(v.z for v in world) - min(v.z for v in world)

bpy.ops.object.camera_add(location=(3.0,-5.3,centre.z+0.5))
cam=bpy.context.object; bpy.context.scene.camera=cam
cam.rotation_euler=(centre-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.lens=62

scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=900; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=PREVIEW
scene.world.color=(.012,.016,.02); scene.view_settings.look='AgX - Medium High Contrast'

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.render.render(write_still=True)

# Export authored gameplay meshes. Preview ground/lights/camera are excluded.
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH' and obj is not ground:
        obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=GLB, export_format='GLB', use_selection=True,
                          export_apply=True, export_materials='EXPORT')
print('SURVIVOR_V2_BODY_OK', 'height', round(height,3), BLEND, PREVIEW, GLB)
