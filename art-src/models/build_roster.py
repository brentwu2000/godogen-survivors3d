import bpy, os
from math import pi
from mathutils import Vector

ROOT = r"C:\Projcet\godogen-survivors3d"
SOURCE = ROOT + r"\art-src\models\base\rigged_anime_girl_cc0.blend"
OUT = ROOT + r"\assets\models\survivors"
BLENDS = ROOT + r"\art-src\models\survivors"
PREVIEWS = ROOT + r"\screenshots\survivors"
os.makedirs(OUT, exist_ok=True); os.makedirs(BLENDS, exist_ok=True); os.makedirs(PREVIEWS, exist_ok=True)

ROSTER = {
 'drifter':  {'torso':(.22,.34,.52),'limb':(.26,.30,.38),'kit':(.18,.23,.29)},
 'courier':  {'torso':(.20,.42,.46),'limb':(.24,.30,.34),'kit':(.48,.42,.29)},
 'scout':    {'torso':(.35,.50,.59),'limb':(.29,.35,.40),'kit':(.25,.32,.38)},
 'warden':   {'torso':(.30,.28,.44),'limb':(.24,.24,.30),'kit':(.42,.39,.50)},
 'gunsmith': {'torso':(.17,.23,.39),'limb':(.20,.23,.29),'kit':(.12,.15,.25)},
 'revenant': {'torso':(.27,.35,.42),'limb':(.23,.28,.31),'kit':(.49,.55,.58)},
 'sapper':   {'torso':(.24,.33,.40),'limb':(.23,.27,.31),'kit':(.54,.44,.18)},
}

def material(name, color, metal=0, rough=.72):
    m=bpy.data.materials.new(name); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    noise=m.node_tree.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=22; noise.inputs['Detail'].default_value=4; noise.inputs['Roughness'].default_value=.78
    ramp=m.node_tree.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].color=(*[c*.45 for c in color],1); ramp.color_ramp.elements[1].color=(*[min(1,c*1.16) for c in color],1)
    m.node_tree.links.new(noise.outputs['Fac'],ramp.inputs['Fac']); m.node_tree.links.new(ramp.outputs['Color'],p.inputs['Base Color'])
    return m

def tint(name, color):
    if name not in bpy.data.objects:return
    for slot in bpy.data.objects[name].material_slots:
        if not slot.material:continue
        m=slot.material.copy(); slot.material=m; m.use_nodes=True
        p=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
        if not p:continue
        base=p.inputs['Base Color']; link=base.links[0] if base.links else None
        mix=m.node_tree.nodes.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=.72; mix.inputs[2].default_value=(*color,1)
        if link: src=link.from_socket; m.node_tree.links.remove(link); m.node_tree.links.new(src,mix.inputs[1])
        else: mix.inputs[1].default_value=base.default_value
        m.node_tree.links.new(mix.outputs[0],base); p.inputs['Roughness'].default_value=.7

def build(kind, pal):
    bpy.ops.wm.open_mainfile(filepath=SOURCE)
    for o in list(bpy.data.objects):
        if o.type in {'CAMERA','LIGHT'} or o.name in {'Plane','Icosphere'}: bpy.data.objects.remove(o,do_unlink=True)
    tint('Jacket',pal['torso']); tint('Pant.002',pal['limb']); tint('Top',tuple(c*.8 for c in pal['kit'])); tint('Shoes',(.25,.12,.05))
    rig=bpy.data.objects['Rigged Anime Girl']; cloth=material(kind+'_weathered_cloth',pal['kit']); armor=material(kind+'_scratched_metal',tuple(c*.72 for c in pal['kit']),.62,.38)
    leather=material(kind+'_leather',(.16,.07,.025),0,.83); accent=material(kind+'_signal',(.72,.12,.025),.05,.58)
    def parent(o,b='DEF-spine.003'):
        w=o.matrix_world.copy(); o.parent=rig; o.parent_type='BONE'; o.parent_bone=b; o.matrix_world=w; return o
    def cube(n,loc,scale,mat,rot=(0,0,0),b='DEF-spine.003',bev=.018):
        bpy.ops.mesh.primitive_cube_add(location=loc,rotation=rot); o=bpy.context.object;o.name=n;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        mod=o.modifiers.new('worn_edges','BEVEL');mod.width=bev;mod.segments=2;o.data.materials.append(mat);return parent(o,b)
    def cyl(n,loc,r,d,mat,rot=(0,0,0),b='DEF-spine.003'):
        bpy.ops.mesh.primitive_cylinder_add(vertices=20,radius=r,depth=d,location=loc,rotation=rot);o=bpy.context.object;o.name=n;o.data.materials.append(mat)
        mod=o.modifiers.new('worn_edges','BEVEL');mod.width=.008;mod.segments=2;return parent(o,b)
    def torus(n,loc,major,minor,mat,b='DEF-spine.003'):
        bpy.ops.mesh.primitive_torus_add(major_radius=major,minor_radius=minor,major_segments=28,minor_segments=10,location=loc);o=bpy.context.object;o.name=n;o.data.materials.append(mat);return parent(o,b)
    # Common grounded survival details.
    cube('utility_belt',(0,-.07,.95),(.19,.018,.025),leather,b='hips')
    if kind=='drifter':
        cube('single_shoulder_strap',(-.10,-.075,1.25),(.025,.012,.29),leather,rot=(0,0,-.23)); cube('canteen',(.19,.02,.82),(.045,.025,.075),cloth,b='hips')
    elif kind=='courier':
        cube('tall_squared_pack',(0,.15,1.24),(.17,.075,.31),cloth); cube('bedroll',(0,.16,1.58),(.18,.07,.065),leather)
        cube('left_thigh_pouch',(-.19,-.04,.70),(.055,.035,.095),cloth,b='DEF-thigh.L'); cube('right_thigh_pouch',(.19,-.04,.70),(.055,.035,.095),cloth,b='DEF-thigh.R')
    elif kind=='scout':
        torus('close_hood',(0,0,1.48),.12,.026,cloth); cube('slim_chest_rig',(0,-.09,1.24),(.16,.025,.11),cloth)
        cube('flat_mag_left',(-.075,-.12,1.20),(.045,.014,.075),leather); cube('flat_mag_right',(.075,-.12,1.20),(.045,.014,.075),leather); cube('single_thigh_pouch',(.18,-.03,.72),(.045,.025,.075),cloth,b='DEF-thigh.R')
    elif kind=='warden':
        torus('throat_gorget',(0,0,1.45),.115,.034,armor); cube('left_hip_plate',(-.22,0,.86),(.075,.035,.14),armor,b='hips'); cube('right_hip_plate',(.22,0,.86),(.075,.035,.14),armor,b='hips')
        cyl('left_forearm_guard',(.55,0,1.25),.06,.26,armor,(0,pi/2,0),'DEF-forearm.L'); cyl('right_forearm_guard',(-.55,0,1.25),.06,.26,armor,(0,pi/2,0),'DEF-forearm.R')
    elif kind=='gunsmith':
        for x,z,h in [(-.13,.75,.30),(.13,.75,.30)]: cube('coat_skirt', (x,.06,z),(.13,.018,h),cloth,b='hips')
        cube('diagonal_bandolier',(0,-.10,1.23),(.035,.022,.34),leather,rot=(0,0,.50));
        for i in range(5): cyl('bandolier_round',( -.11+i*.055,-.135,1.35-i*.055),.012,.07,accent,(pi/2,0,0))
    elif kind=='revenant':
        for side,x,bone in [('L',.29,'DEF-upper_arm.L'),('R',-.29,'DEF-upper_arm.R')]:
            cube(side+'_pauldron',(x,0,1.38),(.11,.06,.055),armor,b=bone); cube(side+'_chest_plate',(x*.35,-.10,1.27),(.11,.025,.19),armor); cube(side+'_shin_plate',(x*.62,-.07,.42),(.07,.025,.16),armor,b='DEF-shin.'+side)
        cube('sternum_relic',(0,-.14,1.29),(.035,.018,.075),accent)
    elif kind=='sapper':
        for x in (-.105,.105):
            cyl('vertical_back_canister',(x,.17,1.44),.07,.48,armor); cyl('canister_cap',(x,.17,1.70),.078,.05,accent)
        for x in (-.16,-.08,0,.08,.16): cube('charge_pouch',(x,-.10,.89),(.035,.025,.065),cloth,b='hips')
        cube('detonator',(.22,-.08,.98),(.045,.025,.09),accent,b='hips')
    # Lower T-pose arms for all variants.
    for bn,rot in {'DEF-upper_arm.L':(0,0,-1.02),'DEF-upper_arm.R':(0,0,1.02),'DEF-forearm.L':(0,.18,-.18),'DEF-forearm.R':(0,-.18,.18)}.items():
        if bn in rig.pose.bones: rig.pose.bones[bn].rotation_mode='XYZ';rig.pose.bones[bn].rotation_euler=rot
    bpy.context.view_layer.update()
    blend=os.path.join(BLENDS,kind+'.blend'); glb=os.path.join(OUT,kind+'.glb'); bpy.ops.wm.save_as_mainfile(filepath=blend)
    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.context.scene.objects:
        if o.type in {'MESH','ARMATURE','EMPTY'}:o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=glb,export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT')
    print('ROSTER_MODEL_OK',kind,glb)

for kind,pal in ROSTER.items(): build(kind,pal)
print('ROSTER_ALL_OK')
