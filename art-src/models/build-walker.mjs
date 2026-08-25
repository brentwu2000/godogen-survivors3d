import * as THREE from 'three'
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js'
import * as BufferGeometryUtils from 'three/examples/jsm/utils/BufferGeometryUtils.js'
import { writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

if (typeof globalThis.FileReader === 'undefined') {
  globalThis.FileReader = class FileReader {
    readAsArrayBuffer(blob) { blob.arrayBuffer().then(b => { this.result = b; this.onloadend?.({ target: this }) }) }
  }
}

// These measurements intentionally do not follow BodyMeshLibrary.ForVariant.
// The authored bodies are warnings drawn with mass and contour; BakeBody owns
// their palette and animation data, while this file owns the silhouette.
const specs = {
  walker:  { height: 2.0, width: .43, limb: .055, depth: .20, lean: 5,  hip: .47, shoulder: .79, head: .925, headR: .075 },
  runner:  { height: 1.8, width: .25, limb: .038, depth: .13, lean: 38, hip: .55, shoulder: .78, head: .91, headR: .050, runner: true },
  brute:   { height: 3.0, width: .99, limb: .162, depth: .47, lean: -2, hip: .38, shoulder: .78, head: .855,headR: .045, brute: true },
  bloater: { height: 2.4, width: 1.42,limb: .060, depth: 1.14, lean: 0,  hip: .29, shoulder: .68, head: .91, headR: .050, belly: true },
  spitter: { height: 2.0, width: .44, limb: .038, depth: .15, lean: 0,  hip: .49, shoulder: .79, head: .915,headR: .055, spitter: true },
  boss:    { height: 5.5, width: 2.15,limb: .145, depth: .72, lean: 2,  hip: .39, shoulder: .76, head: .88, headR: .050, boss: true },
  lantern: { height: 1.9, width: .31, limb: .038, depth: .15, lean: 25, hip: .48, shoulder: .75, head: .84, headR: .055, organ: true, lantern: true },
}

const neutral = (name) => new THREE.MeshStandardMaterial({
  name, color: new THREE.Color(.5, .5, .5), roughness: 1, metalness: 0, flatShading: true,
})

function expandMaterialGroups(arrayBuffer) {
  const source = Buffer.from(arrayBuffer)
  const jsonLength = source.readUInt32LE(12)
  const json = JSON.parse(source.subarray(20, 20 + jsonLength).toString())
  const binHeader = 20 + jsonLength
  const binLength = source.readUInt32LE(binHeader)
  const originalBin = source.subarray(binHeader + 8, binHeader + 8 + binLength)
  const additions = []
  let appendedLength = 0
  const componentBytes = { 5121: 1, 5123: 2, 5125: 4, 5126: 4 }
  const components = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 }
  const align4 = n => (n + 3) & ~3
  const readIndex = (view, offset, type) => type === 5121 ? view.getUint8(offset) : type === 5123 ? view.getUint16(offset, true) : view.getUint32(offset, true)

  for (const primitive of json.meshes[0].primitives) {
    const indexAccessor = json.accessors[primitive.indices]
    const indexView = json.bufferViews[indexAccessor.bufferView]
    const indexData = new DataView(originalBin.buffer, originalBin.byteOffset + (indexView.byteOffset || 0) + (indexAccessor.byteOffset || 0))
    const indexSize = componentBytes[indexAccessor.componentType]
    const indices = Array.from({ length: indexAccessor.count }, (_, i) => readIndex(indexData, i * indexSize, indexAccessor.componentType))
    for (const [semantic, accessorIndex] of Object.entries(primitive.attributes)) {
      const oldAccessor = json.accessors[accessorIndex], oldView = json.bufferViews[oldAccessor.bufferView]
      const elementSize = componentBytes[oldAccessor.componentType] * components[oldAccessor.type]
      const stride = oldView.byteStride || elementSize
      const start = (oldView.byteOffset || 0) + (oldAccessor.byteOffset || 0)
      const expanded = Buffer.alloc(indices.length * elementSize)
      indices.forEach((index, i) => originalBin.copy(expanded, i * elementSize, start + index * stride, start + index * stride + elementSize))
      const byteOffset = align4(originalBin.length + appendedLength)
      const padding = byteOffset - (originalBin.length + appendedLength)
      if (padding) { additions.push(Buffer.alloc(padding)); appendedLength += padding }
      additions.push(expanded); appendedLength += expanded.length
      const bufferView = json.bufferViews.push({ buffer: 0, byteOffset, byteLength: expanded.length }) - 1
      const replacement = { bufferView, componentType: oldAccessor.componentType, count: indices.length, type: oldAccessor.type }
      if (oldAccessor.normalized) replacement.normalized = true
      if (semantic === 'POSITION') {
        const floats = new Float32Array(expanded.buffer, expanded.byteOffset, expanded.length / 4)
        replacement.min = [Infinity, Infinity, Infinity]; replacement.max = [-Infinity, -Infinity, -Infinity]
        for (let i = 0; i < floats.length; i += 3) for (let a = 0; a < 3; a++) {
          replacement.min[a] = Math.min(replacement.min[a], floats[i + a]); replacement.max[a] = Math.max(replacement.max[a], floats[i + a])
        }
      }
      primitive.attributes[semantic] = json.accessors.push(replacement) - 1
    }
    delete primitive.indices
  }
  const binary = Buffer.concat([originalBin, ...additions]); json.buffers[0].byteLength = binary.length
  let jsonBytes = Buffer.from(JSON.stringify(json)); jsonBytes = Buffer.concat([jsonBytes, Buffer.alloc((4-jsonBytes.length%4)%4, 0x20)])
  const binaryPadding = Buffer.alloc((4-binary.length%4)%4)
  const totalLength = 12+8+jsonBytes.length+8+binary.length+binaryPadding.length, result = Buffer.alloc(totalLength)
  result.writeUInt32LE(0x46546c67,0); result.writeUInt32LE(2,4); result.writeUInt32LE(totalLength,8)
  result.writeUInt32LE(jsonBytes.length,12); result.writeUInt32LE(0x4e4f534a,16); jsonBytes.copy(result,20)
  const h=20+jsonBytes.length; result.writeUInt32LE(binary.length+binaryPadding.length,h); result.writeUInt32LE(0x004e4942,h+4)
  binary.copy(result,h+8); binaryPadding.copy(result,h+8+binary.length); return result
}

async function build(name, s) {
  const h=s.height, hipY=h*s.hip, shoulderY=h*s.shoulder, headC=h*s.head, headR=h*s.headR
  const leanZ = y => -(y-hipY)*Math.tan(THREE.MathUtils.degToRad(s.lean))
  const bones=[]
  const bone=(n,p,at)=>{ const b=new THREE.Bone(); b.name=n; b.position.set(...at); if(p)p.add(b); bones.push(b); return b }
  const root=bone('Root',null,[0,0,0]), hips=bone('Hips',root,[0,hipY,0])
  const spine=bone('Spine',hips,[0,(shoulderY-hipY)*.48,leanZ(hipY+(shoulderY-hipY)*.48)])
  const chest=bone('Chest',spine,[0,(shoulderY-hipY)*.52,leanZ(shoulderY)-leanZ(hipY+(shoulderY-hipY)*.48)])
  bone('Head',chest,[0,headC-shoulderY,leanZ(headC)-leanZ(shoulderY)])
  const legX=s.width*(s.belly?.17:s.brute?.13:s.boss?.16:.215), kneeY=hipY*(s.brute?.46:s.belly?.42:.52)
  const thighL=bone('Thigh.L',hips,[-legX,0,0]), shinL=bone('Shin.L',thighL,[0,kneeY-hipY,0]); bone('Foot.L',shinL,[0,-kneeY,0])
  const thighR=bone('Thigh.R',hips,[legX,0,0]), shinR=bone('Shin.R',thighR,[0,kneeY-hipY,0]); bone('Foot.R',shinR,[0,-kneeY,0])
  const armX=s.width*.5+s.limb*.35
  const armLength=s.spitter?shoulderY*.91:s.lantern?(shoulderY-hipY)*1.62:s.brute?(shoulderY-hipY)*.82:(shoulderY-hipY)*1.16
  const armL=bone('UpperArm.L',chest,[-armX,0,0]), foreL=bone('Forearm.L',armL,[0,-armLength*.52,0]); bone('Hand.L',foreL,[0,-armLength*.48,0])
  const armR=bone('UpperArm.R',chest,[armX,0,0]), foreR=bone('Forearm.R',armR,[0,-armLength*.52,0]); bone('Hand.R',foreR,[0,-armLength*.48,0])
  const indexOf=n=>bones.findIndex(b=>b.name===n), parts=[[],[],[],[]]
  function skinned(g,joint,material){ const n=g.attributes.position.count, js=new Uint16Array(n*4), ws=new Float32Array(n*4); for(let i=0;i<n;i++){js[i*4]=indexOf(joint);ws[i*4]=1} g.setAttribute('skinIndex',new THREE.Uint16BufferAttribute(js,4));g.setAttribute('skinWeight',new THREE.Float32BufferAttribute(ws,4));g.deleteAttribute('uv');parts[material].push(g) }
  function tube(joint,points,radii,sides=6,material=1){ const pos=[],idx=[]; for(let r=0;r<points.length;r++){const [x,y,z]=points[r],[rx,rz]=radii[r];for(let q=0;q<sides;q++){const a=q*Math.PI*2/sides;pos.push(x+Math.cos(a)*rx,y,z+Math.sin(a)*rz)}} for(let r=0;r+1<points.length;r++)for(let q=0;q<sides;q++){const n=(q+1)%sides,a=r*sides+q,b=r*sides+n,c=(r+1)*sides+q,d=(r+1)*sides+n;idx.push(a,c,b,b,c,d)} const bot=pos.length/3;pos.push(...points[0]);const top=pos.length/3;pos.push(...points.at(-1));for(let q=0;q<sides;q++){const n=(q+1)%sides,o=(points.length-1)*sides;idx.push(bot,q,n,top,o+n,o+q)}const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(pos,3));g.setIndex(idx);skinned(g,joint,material) }
  function ball(joint,c,r,material=0,sides=8){ const [cx,cy,cz]=c,pos=[cx,cy+r[1],cz,cx,cy-r[1],cz],idx=[];const rings=3;for(let y=1;y<=rings;y++){const p=y/(rings+1)*Math.PI;for(let q=0;q<sides;q++){const a=q*Math.PI*2/sides;pos.push(cx+Math.sin(p)*Math.cos(a)*r[0],cy+Math.cos(p)*r[1],cz+Math.sin(p)*Math.sin(a)*r[2])}}for(let q=0;q<sides;q++){const n=(q+1)%sides;idx.push(0,2+q,2+n);for(let y=0;y<rings-1;y++){const a=2+y*sides+q,b=2+y*sides+n,c0=a+sides,d=b+sides;idx.push(a,c0,b,b,c0,d)}const last=2+(rings-1)*sides;idx.push(1,last+n,last+q)}const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(pos,3));g.setIndex(idx);skinned(g,joint,material) }
  const point=(x,y,z=0)=>[x,y,z+leanZ(y)]

  // Legs and feet: exact hip landmark and floor contact, with the walker's six-sided economy.
  for(const side of [-1,1]){const sf=side<0?'L':'R',x=side*legX,r=s.limb
    tube(`Foot.${sf}`,[[x,0,-r*2.2],[x,r*1.45,-r*.25]],[[r*.95,r*2.2],[r*.82,r*1.05]],6)
    tube(`Shin.${sf}`,[[x,r*1.25,0],[x,kneeY,0]],[[r*.62,r*.62],[r*.92,r*.86]],6)
    tube(`Thigh.${sf}`,[[x,kneeY-r*.3,0],[x,hipY,0]],[[r*.86,r*.82],[r*1.18,r*1.10]],6)
  }
  // Pelvis and either ribcage or the bloater's singular round warning shape.
  tube('Hips',[point(0,hipY-(shoulderY-hipY)*.06),point(0,hipY+(shoulderY-hipY)*.25)],[[s.width*.34,s.depth*.46],[s.width*.29,s.depth*.40]],8,0)
  if(s.belly) ball('Spine',point(0,(hipY+shoulderY)*.48,-s.depth*.03),[s.width*.72,(shoulderY-hipY)*.94,s.depth*.70],0,12)
  else if(s.brute) tube('Spine',[point(0,hipY+(shoulderY-hipY)*.05),point(0,hipY+(shoulderY-hipY)*.68),point(0,shoulderY)],[[s.width*.18,s.depth*.34],[s.width*.43,s.depth*.56],[s.width*.52,s.depth*.60]],8,0)
  else if(s.spitter) tube('Spine',[point(0,hipY+(shoulderY-hipY)*.18),point(0,shoulderY)],[[s.width*.22,s.depth*.32],[s.width*.34,s.depth*.42]],6,0)
  else if(s.boss) tube('Spine',[point(0,hipY+(shoulderY-hipY)*.12),point(-s.width*.08,shoulderY)],[[s.width*.24,s.depth*.40],[s.width*.40,s.depth*.58]],8,0)
  else tube('Spine',[point(0,hipY+(shoulderY-hipY)*.20),point(0,hipY+(shoulderY-hipY)*.62),point(0,shoulderY)],[[s.width*.30,s.depth*.42],[s.width*.40,s.depth*.51],[s.width*.50,s.depth*.50]],8,0)
  // Deltoid bar makes the authored shoulder width explicit.
  const shoulderSpan=s.belly?s.width*.34:s.width*.5
  const shoulderLift=s.spitter?headR*.65:0
  tube('Chest',[point(-shoulderSpan,shoulderY+shoulderLift),point(0,shoulderY-(shoulderY-hipY)*.025),point(shoulderSpan,shoulderY+shoulderLift)],[[s.limb*1.15,s.depth*.28],[s.limb*1.45,s.depth*.52],[s.limb*1.15,s.depth*.28]],6,0)
  const headForward=s.runner?-h*.24:s.lantern?-h*.12:0
  ball('Head',point(s.boss?s.width*.05:0,headC,headForward),[headR*(s.brute?.70:1.0),headR,headR*(s.runner?1.35:.78)],2,s.brute?6:8)
  // Projecting jaw/brow read in profile without adding another material surface.
  tube('Head',[point(0,headC-headR*.38,headForward-headR*.54),point(0,headC-headR*.05,headForward-headR*.86)],[[headR*.50,headR*.34],[headR*(s.runner?1.05:.58),headR*.30]],5,2)

  let handY=shoulderY-armLength
  for(const side of [-1,1]){const sf=side<0?'L':'R',x=side*armX,r=s.limb*.9
    let elbow=point(x,shoulderY-armLength*.52,-r*.12), wrist=point(x,handY,-r*.03)
    if(name==='spitter') {
      // A bent, forward-reaching arm reads as anatomy in silhouette; two
      // parallel plumb lines made the old long arms look like reeds.
      elbow=point(x+side*r*2.15,shoulderY-armLength*.48,-r*2.0)
      wrist=point(x-side*r*.65,handY,-r*4.1)
    }
    if(name==='runner'){ elbow=point(x+side*r*1.8,shoulderY-armLength*.28,r*8.5); wrist=point(x+side*r*.5,shoulderY-armLength*.66,r*13.0); handY=wrist[1] }
    if(name==='boss'&&side<0){ elbow=point(x-side*r*.8,shoulderY-armLength*.50,0); wrist=point(x-side*r*1.8,handY-r*2.0,-r); handY=Math.min(handY,wrist[1]) }
    const mutate=name==='boss'&&side<0?2.55:name==='boss'?.48:(name==='brute'?1.45:1)
    tube(`UpperArm.${sf}`,[point(x,shoulderY),elbow],[[r*1.22*mutate,r*1.15*mutate],[r*.90*mutate,r*.86*mutate]],6)
    tube(`Forearm.${sf}`,[elbow,wrist],[[r*.94*mutate,r*.90*mutate],[r*.68*mutate,r*.62*mutate]],6)
    ball(`Hand.${sf}`,[wrist[0],wrist[1]-r*.72,wrist[2]],[r*.92*mutate,r*1.18*mutate,r*.72*mutate],1,6)
  }
  // Variant-specific closed details, selected for silhouette at horde distance.
  if(name==='brute'){for(const side of [-1,1]) ball('Chest',point(side*s.width*.34,shoulderY+headR*.20),[s.width*.24,headR*2.2,s.depth*.48],0,7)}
  if(name==='spitter') { for(const side of [-1,1]) ball('Chest',point(side*s.width*.30,shoulderY+headR*.55),[s.width*.18,headR*.75,s.depth*.44],0,6) }
  if(name==='boss'){
    // One continuous-looking back slab: broad, high on the giant shoulder, and
    // deliberately skewed so the boss never collapses into a scaled brute.
    tube('Chest',[point(-s.width*.60,shoulderY+headR*.85,s.depth*.12),point(-s.width*.12,shoulderY+headR*.48,s.depth*.24),point(s.width*.48,shoulderY+headR*.12,s.depth*.10)],[[headR*.72,s.depth*.24],[headR*.92,s.depth*.34],[headR*.46,s.depth*.20]],6,0)
    tube('Chest',[point(-s.width*.34,hipY+(shoulderY-hipY)*.35,s.depth*.26),point(-s.width*.18,shoulderY+headR*.42,s.depth*.28)],[[headR*.42,s.depth*.18],[headR*.68,s.depth*.28]],5,0)
  }
  if(name==='lantern'){
    const organY=hipY+(shoulderY-hipY)*.66, organZ=leanZ(organY)-s.depth*.57
    // Socket is torso; organ alone is the fourth, independently recoloured surface.
    ball('Chest',[0,organY,organZ+s.depth*.04],[s.width*.24,(shoulderY-hipY)*.24,s.depth*.22],0,7)
    ball('Chest',[0,organY,organZ-s.depth*.025],[s.width*.16,(shoulderY-hipY)*.17,s.depth*.16],3,7)
  }

  const active=parts.slice(0,s.organ?4:3)
  if(active.some(p=>p.length===0)) throw new Error(`${name}: empty required surface`)
  const groups=active.map(p=>BufferGeometryUtils.mergeGeometries(p,false))
  let geometry=BufferGeometryUtils.mergeGeometries(groups,true).toNonIndexed(); geometry.computeVertexNormals(); geometry.name=`${name}Geometry`
  const materials=['Torso','Limbs','Head','Organ'].slice(0,active.length).map(neutral)
  const mesh=new THREE.SkinnedMesh(geometry,materials);mesh.name=`${name}Mesh`;mesh.add(root);mesh.bind(new THREE.Skeleton(bones))
  const scene=new THREE.Group();scene.name=name;scene.add(mesh);scene.updateMatrixWorld(true)
  const triangles=geometry.attributes.position.count/3,budget=name==='boss'?900:600
  if(triangles>budget) throw new Error(`${name}: triangle budget exceeded by ${triangles-budget} (${triangles}/${budget})`)
  const exporter=new GLTFExporter();let glb=await exporter.parseAsync(scene,{binary:true,onlyVisible:false});glb=expandMaterialGroups(glb)
  const out=join(dirname(fileURLToPath(import.meta.url)),'..','..','assets','models');mkdirSync(out,{recursive:true});writeFileSync(join(out,`${name}.glb`),Buffer.from(glb))
  console.log(`${name}.glb ${triangles} triangles; ${active.length} surfaces; ${bones.length} bones; hip ${hipY.toFixed(3)} m; shoulder ${shoulderY.toFixed(3)} m; hand ${handY.toFixed(3)} m; head ${h.toFixed(3)} m`)
}

const requested=new Set(process.argv.slice(2))
for(const [name,spec] of Object.entries(specs)) if(!requested.size||requested.has(name)) await build(name,spec)
