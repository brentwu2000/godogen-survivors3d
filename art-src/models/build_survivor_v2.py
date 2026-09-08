import bpy
import bmesh
from mathutils import Vector
from bl_ext.blender_org.mpfb.services import HumanService, TargetService

ROOT = r"C:\Projcet\godogen-survivors3d"
BLEND = ROOT + r"\art-src\models\survivor_apocalypse_v2.blend"
PREVIEW = ROOT + r"\screenshots\survivor_body_v2.png"
GLB = ROOT + r"\assets\models\survivor_apocalypse_v2.glb"
ATLAS = ROOT + r"\assets\textures\survivor_material_atlas_v2.png"

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
atlas_image = bpy.data.images.load(ATLAS, check_existing=True)

def connect_atlas(mat, bsdf, region):
    """Map an object's regular 0..1 UVs into one material cell of the atlas."""
    x, y, w, h = region
    nodes=mat.node_tree.nodes; links=mat.node_tree.links
    uv=nodes.new('ShaderNodeTexCoord')
    mul=nodes.new('ShaderNodeVectorMath'); mul.operation='MULTIPLY'; mul.inputs[1].default_value=(w,h,1)
    add=nodes.new('ShaderNodeVectorMath'); add.operation='ADD'; add.inputs[1].default_value=(x,y,0)
    tex=nodes.new('ShaderNodeTexImage'); tex.image=atlas_image; tex.interpolation='Linear'
    links.new(uv.outputs['UV'],mul.inputs[0]); links.new(mul.outputs[0],add.inputs[0]); links.new(add.outputs[0],tex.inputs['Vector'])
    links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])

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

# MPFB keeps fitting cages and joint cubes in the same source mesh even when
# their viewport masks are disabled. Delete those vertices before any garment
# is extracted or exported; otherwise they render as a long solid skirt.
helper_group_ids = {g.index for g in body.vertex_groups if g.name in {'HelperGeometry','JointCubes'}}
helper_vertex_ids = {v.index for v in body.data.vertices
                     if any(link.group in helper_group_ids for link in v.groups)}
bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in helper_vertex_ids], context='VERTS')
bm.to_mesh(body.data); bm.free(); body.data.update()

# A neutral technical-suit preview material. The final body is never exposed in
# game; this keeps anatomy review focused on silhouette and joint topology while
# the authored jacket, trousers and armor are built over it.
mat = bpy.data.materials.new('Technical under suit')
mat.diffuse_color = (.018, .025, .032, 1)
mat.use_nodes = True
bs = mat.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value = (.018, .025, .032, 1)
bs.inputs['Roughness'].default_value = 0.86
connect_atlas(mat, bs, (.50,.50,.50,.50))
body.data.materials.clear(); body.data.materials.append(mat)

# Preserve the full-resolution sculpt source, while previewing the same smooth
# surface that garments will be fitted against.
subd = body.modifiers.new('Preview subdivision', 'SUBSURF')
subd.levels = 1; subd.render_levels = 1
for p in body.data.polygons: p.use_smooth = True

def material(name, color, metallic=0.0, roughness=0.65, region=None):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    if region: connect_atlas(m, p, region)
    return m

navy = material('Jacket weathered navy', (.045,.095,.125), 0.05, .8, (0,.50,.50,.50))
charcoal = material('Reinforced charcoal trousers', (.025,.028,.032), 0.02, .88, (.50,.50,.50,.50))
orange = material('Signal orange scarf', (.55,.09,.018), 0.0, .72, (.67,0,.33,.25))
steel = material('Prosthetic gunmetal', (.07,.085,.09), .72, .34, (0,0,.33,.25))
rubber = material('Prosthetic joint rubber', (.012,.014,.015), .1, .9, (.50,.50,.50,.50))
hairmat = material('Blue black hair', (.006,.012,.018), .08, .3, (.50,.25,.50,.25))
leather = material('Aged utility leather', (.12,.055,.025), .0, .82, (.33,0,.34,.25))
skin = material('Survivor warm skin', (.38,.22,.17), 0.0, .7, (0,.25,.50,.25))
eye_white = material('Eye sclera', (.72,.70,.64), 0.0, .35)
iris = material('Desaturated amber iris', (.16,.075,.025), 0.0, .28)

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
jacket = shell_from_body('Cropped field jacket', lambda v: .77 < v.co.z < 1.31, navy, .012, .65)
trousers = shell_from_body('Armoured field trousers', lambda v: .24 < v.co.z < .94, charcoal, .010, .6)
boots = shell_from_body('Survivor combat boots', lambda v: v.co.z < .30, leather, .014, .55)
face = shell_from_body('Visible face and neck', lambda v: v.co.z > 1.285, skin, .003, .7)
hands = shell_from_body('Visible hands', lambda v: abs(v.co.x) > .34 and v.co.z < .84, skin, .003, .7)
prosthetic = shell_from_body('Fitted mechanical left forearm',
                             lambda v: v.co.x < -.25 and .63 < v.co.z < 1.03,
                             steel, .018, .75)

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

# Signature asymmetric split coat tails. Thin tapered panels preserve the legs
# and create movement instead of reading as a solid skirt.
def coat_panel(name, verts):
    mesh=bpy.data.meshes.new(name+'Mesh')
    mesh.from_pydata(verts, [], [(0,1,2,3)]); mesh.update()
    o=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(o)
    o.data.materials.append(navy)
    sol=o.modifiers.new('Cloth thickness','SOLIDIFY'); sol.thickness=.012
    bev=o.modifiers.new('Worn soft edge','BEVEL'); bev.width=.012; bev.segments=3
    return o
coat_panel('Long left coat tail', [(-.25,.075,.91),(-.025,.075,.91),(-.07,.08,.45),(-.27,.08,.38)])
coat_panel('Short right coat tail', [(.025,.074,.91),(.22,.074,.91),(.20,.08,.62),(.07,.08,.56)])
cube('Orange waist tab', (.18,-.07,.77), (.035,.014,.105), orange, .01, (-.08,0,.10))
cube('Utility belt', (0,-.045,.89), (.29,.022,.024), leather, .01)
for x in (-.235,.235): cube('Belt pouch', (x,-.045,.80), (.042,.022,.060), leather, .012)

# Scarf: two overlapping rounded bands plus a wind-swept loose end.
bpy.ops.mesh.primitive_torus_add(major_radius=.112, minor_radius=.019, major_segments=40, minor_segments=10,
                                location=(0,0,1.33), rotation=(0,0,0))
scarf=bpy.context.object; scarf.name='Signal scarf'; scarf.scale=(1.08,.88,.72); scarf.data.materials.append(orange)
cube('Scarf loose end', (-.12,.055,1.20), (.038,.014,.14), orange, .018, (-.16,.08,-.18))

# The mechanical forearm is surface-fitted above. A small signal plate keeps
# the orange visual language without stacking floating cylinders around it.
cube('Prosthetic signal plate', (-.43,-.065,.82), (.032,.012,.07), orange, .008, (0,.10,-.16))

# Face-framing bob and the concept's distinct braided side strand.
bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, location=(0,.045,1.465), scale=(.142,.120,.150))
hair=bpy.context.object; hair.name='Asymmetric cropped hair'; hair.data.materials.append(hairmat)
bm=bmesh.new(); bm.from_mesh(hair.data)
# Sphere-local front is -Y. Remove the lower-front region to expose the face,
# retaining a rounded crown, temples and the hair mass behind the skull.
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < -.12 and v.co.z < .45], context='VERTS')
bm.to_mesh(hair.data); bm.free()
for p in hair.data.polygons: p.use_smooth=True
for i,z in enumerate((1.34,1.25,1.16,1.08)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, location=(-.16,.045,z), scale=(.026,.024,.047))
    bead=bpy.context.object; bead.name=f'Braided hair {i+1}'; bead.data.materials.append(hairmat)

# Separate eyeballs restore the facial focal point lost in the raw base mesh.
for side,x in (('L',-.035),('R',.035)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, location=(x,-.113,1.456), scale=(.017,.010,.013))
    eye=bpy.context.object; eye.name=f'{side} eye'; eye.data.materials.append(eye_white)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, location=(x,-.123,1.456), scale=(.007,.003,.007))
    pupil=bpy.context.object; pupil.name=f'{side} amber iris'; pupil.data.materials.append(iris)

# Compact apocalypse backpack and mask canister make the rear silhouette useful.
cube('Field backpack', (0,.13,1.08), (.17,.06,.205), charcoal, .028)
cyl('Filter canister', (-.25,.045,.70), .050, .13, steel, (1.5708,0,0))
cube('Shoulder armour', (.25,-.005,1.23), (.09,.045,.042), steel, .018, (0,.08,.12))

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
