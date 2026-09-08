import bpy
from math import pi
from mathutils import Vector

ROOT = r"C:\Projcet\godogen-survivors3d"
SOURCE = ROOT + r"\art-src\models\base\rigged_anime_girl_cc0.blend"
BLEND = ROOT + r"\art-src\models\survivor_anime_apocalypse.blend"
GLB = ROOT + r"\assets\models\survivor_anime_apocalypse.glb"
PREVIEW = ROOT + r"\screenshots\survivor_anime_apocalypse_preview.png"

bpy.ops.wm.open_mainfile(filepath=SOURCE)

# Remove the old preview stage; the imported character and rig remain untouched.
for obj in list(bpy.data.objects):
    if obj.type in {'CAMERA','LIGHT'} or obj.name in {'Plane','Icosphere'}:
        bpy.data.objects.remove(obj, do_unlink=True)

def tint_object(object_name, tint, strength=.64, metallic=None, roughness=None):
    obj=bpy.data.objects[object_name]
    for slot in obj.material_slots:
        if not slot.material: continue
        mat=slot.material.copy(); mat.name=f'{object_name}_Apocalypse'
        slot.material=mat; mat.use_nodes=True
        nodes=mat.node_tree.nodes; links=mat.node_tree.links
        bs=next((n for n in nodes if n.type=='BSDF_PRINCIPLED'),None)
        if not bs: continue
        base=bs.inputs['Base Color']; incoming=base.links[0] if base.links else None
        gray=nodes.new('ShaderNodeRGBToBW')
        mix=nodes.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=1.0
        mix.inputs[2].default_value=(*tint,1)
        if incoming:
            source_socket=incoming.from_socket
            links.remove(incoming); links.new(source_socket,gray.inputs[0]); links.new(gray.outputs[0],mix.inputs[1])
        else: mix.inputs[1].default_value=base.default_value
        links.new(mix.outputs[0],base)
        if metallic is not None: bs.inputs['Metallic'].default_value=metallic
        if roughness is not None: bs.inputs['Roughness'].default_value=roughness

tint_object('Jacket', (.18,.48,.68), .78, .05, .66)
tint_object('Pant.002', (.15,.18,.22), .82, .0, .82)
tint_object('Top', (.42,.43,.32), .62, .0, .76)
tint_object('Shoes', (.31,.14,.065), .72, .08, .58)
for name in [o.name for o in bpy.data.objects if o.name.startswith('h') and o.type=='MESH']:
    tint_object(name, (.055,.11,.20), .74, .04, .34)

def mat(name,color,metal=.0,rough=.65):
    m=bpy.data.materials.new(name); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m

orange=mat('Burnt signal orange',(.62,.085,.012),0,.72)
gunmetal=mat('Scratched prosthetic gunmetal',(.055,.075,.085),.78,.34)
dark=mat('Apocalypse pack fabric',(.018,.025,.03),.02,.9)
leather=mat('Weathered utility leather',(.11,.045,.018),0,.83)
rig=bpy.data.objects['Rigged Anime Girl']

def parent_bone(obj,bone_name):
    world=obj.matrix_world.copy()
    obj.parent=rig; obj.parent_type='BONE'; obj.parent_bone=bone_name
    obj.matrix_world=world
    return obj

def cube(name,loc,scale,material,bevel=.02,rotation=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(location=loc,rotation=rotation)
    o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    b=o.modifiers.new('Worn rounded edges','BEVEL'); b.width=bevel; b.segments=3
    o.data.materials.append(material); return o

def cyl(name,loc,radius,depth,material,rotation=(0,0,0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=radius,depth=depth,location=loc,rotation=rotation)
    o=bpy.context.object; o.name=name; o.data.materials.append(material)
    b=o.modifiers.new('Machined edge','BEVEL'); b.width=.006; b.segments=2
    for p in o.data.polygons:p.use_smooth=True
    return o

# Distinctive survivor silhouette: scarf, compact pack and asymmetrical equipment.
bpy.ops.mesh.primitive_torus_add(major_radius=.112,minor_radius=.024,major_segments=40,minor_segments=12,location=(0,0,1.43))
scarf=bpy.context.object; scarf.name='Signal scarf'; scarf.scale=(1.05,.82,.8); scarf.data.materials.append(orange)
parent_bone(scarf,'DEF-spine.003')
parent_bone(cube('Scarf tail',(-.11,.055,1.32),(.035,.014,.14),orange,.018,(0,.08,-.18)),'DEF-spine.003')

parent_bone(cube('Compact field backpack',(0,.135,1.18),(.15,.055,.21),dark,.035),'DEF-spine.003')
parent_bone(cube('Radio module',(.12,.185,1.30),(.045,.025,.07),gunmetal,.015),'DEF-spine.003')
parent_bone(cube('Asymmetric upper arm plate',(.31,.005,1.37),(.060,.035,.028),gunmetal,.015,(0,.08,.08)),'DEF-upper_arm.L')

# A fitted plated forearm over the existing rigged sleeve, aligned to the T-pose.
parent_bone(cyl('Mechanical forearm shell',(.57,-.005,1.267),.054,.29,gunmetal,(0,pi/2,0)),'DEF-forearm.L')
for x in (.45,.56,.68): parent_bone(cyl('Mechanical arm ring',(x,-.005,1.267),.061,.022,gunmetal,(0,pi/2,0)),'DEF-forearm.L')
parent_bone(cube('Prosthetic orange strip',(.57,-.066,1.267),(.09,.008,.018),orange,.006),'DEF-forearm.L')

parent_bone(cube('Utility belt',(0,-.072,.96),(.18,.018,.018),leather,.009),'hips')
parent_bone(cube('Hip medical pouch',(-.19,-.075,.84),(.045,.024,.072),leather,.014),'hips')
parent_bone(cube('Orange flare pouch',(.18,-.075,.82),(.027,.020,.085),orange,.011),'hips')

def panel(name,verts,material):
    mesh=bpy.data.meshes.new(name+'Mesh'); mesh.from_pydata(verts,[],[(0,1,2,3)]); mesh.update()
    o=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(o); o.data.materials.append(material)
    s=o.modifiers.new('Cloth thickness','SOLIDIFY'); s.thickness=.009
    b=o.modifiers.new('Soft cloth edge','BEVEL'); b.width=.008; b.segments=2
    return o

# Split asymmetrical coat tails, thin enough to retain the leg silhouette.
parent_bone(panel('Long left coat tail',[(-.19,.07,1.02),(-.015,.07,1.02),(-.04,.075,.47),(-.23,.075,.40)],navy if 'navy' in globals() else dark),'hips')
parent_bone(panel('Short right coat tail',[(.015,.07,1.02),(.18,.07,1.02),(.16,.075,.67),(.05,.075,.61)],dark),'hips')

# Compact magnetic rifle carried diagonally across the backpack.
rifle=[]
rifle.append(cube('Rifle receiver',(0,.235,1.20),(.055,.035,.20),gunmetal,.015,(0.0,.0,-.35)))
rifle.append(cyl('Rifle barrel',(-.075,.235,1.47),.018,.42,gunmetal,(0,.0,-.35)))
rifle.append(cube('Rifle stock',(.075,.235,.96),(.065,.03,.12),dark,.018,(0,0,-.35)))
rifle.append(cube('Rifle orange power cell',(-.015,.198,1.21),(.025,.012,.065),orange,.008,(0,0,-.35)))
for part in rifle: parent_bone(part,'DEF-spine.003')

# Gameplay validation pose: lower the arms from the bind T-pose and add a mild
# ready stance. Rigify FK controls drive the original skinned meshes while all
# authored equipment follows its assigned deform bone.
pose=rig.pose.bones
for name,rot in {
    # GLB preserves the Rigify bone hierarchy and weights, but not Blender's
    # constraint drivers. Pose the weighted DEF chain directly for validation.
    'DEF-upper_arm.L':(0,0,-1.02), 'DEF-upper_arm.R':(0,0,1.02),
    'DEF-forearm.L':(0,.18,-.18), 'DEF-forearm.R':(0,-.18,.18),
}.items():
    if name in pose:
        pose[name].rotation_mode='XYZ'; pose[name].rotation_euler=rot
bpy.context.view_layer.update()

# Studio preview.
bpy.ops.mesh.primitive_plane_add(size=12,location=(0,0,-.012))
ground=bpy.context.object; ground.name='Preview Ground'; ground.data.materials.append(dark)
bpy.ops.object.light_add(type='AREA',location=(-2.6,-3.8,4.0)); bpy.context.object.data.energy=1200; bpy.context.object.data.size=3.5
bpy.ops.object.light_add(type='AREA',location=(3.0,.8,2.7)); bpy.context.object.data.energy=850; bpy.context.object.data.color=(.22,.38,1); bpy.context.object.data.size=3
bpy.ops.object.camera_add(location=(2.55,-4.6,2.02))
cam=bpy.context.object; bpy.context.scene.camera=cam; target=Vector((0,0, .88))
cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.lens=68

scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=900; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=PREVIEW
scene.world.color=(.006,.009,.013); scene.view_settings.look='AgX - Medium High Contrast'

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.render.render(write_still=True)

bpy.ops.object.select_all(action='DESELECT')
for o in bpy.context.scene.objects:
    if o is not ground and o.type in {'MESH','ARMATURE','EMPTY'}: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=GLB,export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT')
print('SURVIVOR_CC0_PASS1_OK',BLEND,GLB,PREVIEW)
