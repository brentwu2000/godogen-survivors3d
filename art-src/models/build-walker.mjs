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

const bones = []
const bone = (name, parent, at) => {
  const b = new THREE.Bone(); b.name = name; b.position.set(...at)
  if (parent) parent.add(b)
  bones.push(b); return b
}

const root = bone('Root', null, [0, 0, 0])
const hips = bone('Hips', root, [0, 0.92, 0])
const spine = bone('Spine', hips, [0, 0.34, -0.048])
const chest = bone('Chest', spine, [0, 0.34, -0.048])
const head = bone('Head', chest, [0, 0.35, -0.049])
const thighL = bone('Thigh.L', hips, [-0.09, 0, 0]); const shinL = bone('Shin.L', thighL, [0, -0.46, 0]); bone('Foot.L', shinL, [0, -0.46, -0.02])
const thighR = bone('Thigh.R', hips, [0.09, 0, 0]); const shinR = bone('Shin.R', thighR, [0, -0.46, 0]); bone('Foot.R', shinR, [0, -0.46, -0.02])
const armL = bone('UpperArm.L', chest, [-0.235, 0, 0]); const foreL = bone('Forearm.L', armL, [0, -0.31, 0]); bone('Hand.L', foreL, [0, -0.29, 0])
const armR = bone('UpperArm.R', chest, [0.235, 0, 0]); const foreR = bone('Forearm.R', armR, [0, -0.31, 0]); bone('Hand.R', foreR, [0, -0.29, 0])

const indexOf = name => bones.findIndex(b => b.name === name)
const parts = [[], [], []] // torso, limbs, head
function skinned(geometry, joint, material) {
  const count = geometry.attributes.position.count
  const joints = new Uint16Array(count * 4), weights = new Float32Array(count * 4)
  for (let i = 0; i < count; i++) { joints[i * 4] = indexOf(joint); weights[i * 4] = 1 }
  geometry.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(joints, 4))
  geometry.setAttribute('skinWeight', new THREE.Float32BufferAttribute(weights, 4))
  geometry.deleteAttribute('uv'); parts[material].push(geometry)
}

// Closed elliptical frustum between arbitrary endpoints. Wide/deep radii can differ,
// and an optional middle ring gives the torso and head a non-primitive silhouette.
function tube(joint, points, radii, sides = 8, material = 1) {
  const pos = [], idx = []
  for (let r = 0; r < points.length; r++) {
    const [cx, cy, cz] = points[r], [rx, rz] = radii[r]
    for (let s = 0; s < sides; s++) {
      const a = s * Math.PI * 2 / sides
      pos.push(cx + Math.cos(a) * rx, cy, cz + Math.sin(a) * rz)
    }
  }
  for (let r = 0; r + 1 < points.length; r++) for (let s = 0; s < sides; s++) {
    const n = (s + 1) % sides, a = r * sides + s, b = r * sides + n, c = (r + 1) * sides + s, d = (r + 1) * sides + n
    idx.push(a, c, b, b, c, d)
  }
  const bottom = pos.length / 3; pos.push(...points[0])
  const top = pos.length / 3; pos.push(...points.at(-1))
  for (let s = 0; s < sides; s++) { const n = (s + 1) % sides; idx.push(bottom, s, n); const o=(points.length-1)*sides; idx.push(top, o+n, o+s) }
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(idx)
  skinned(g, joint, material)
}

// Feet at y=0, body leaning toward -Z (the creature's facing direction).
const leanZ = y => -(y - 0.92) * Math.tan(THREE.MathUtils.degToRad(8))
for (const side of [-1, 1]) {
  const x = side * 0.09
  tube(side < 0 ? 'Foot.L' : 'Foot.R', [[x,0,-0.12],[x,0.08,-0.02]], [[0.050,0.12],[0.044,0.060]], 6)
  tube(side < 0 ? 'Shin.L' : 'Shin.R', [[x,0.07,-0.01],[x,0.46,0]], [[0.034,0.034],[0.047,0.044]], 6)
  tube(side < 0 ? 'Thigh.L' : 'Thigh.R', [[x,0.44,0],[x,0.92,0]], [[0.044,0.044],[0.055,0.052]], 6)
}

// Procedural walker landmarks: hip 46%, shoulder 80%, with a narrow ribcage
// whose full width/depth are exactly 0.42/0.20 m at the shoulder line.
tube('Hips', [[0,0.86,leanZ(0.86)],[0,1.05,leanZ(1.05)]], [[0.145,0.075],[0.125,0.068]], 8, 0)
tube('Spine', [[0,1.00,leanZ(1.00)],[0,1.30,leanZ(1.30)],[0,1.60,leanZ(1.60)]], [[0.122,0.070],[0.165,0.086],[0.210,0.100]], 8, 0)
tube('Chest', [[0,1.48,leanZ(1.48)],[0,1.60,leanZ(1.60)]], [[0.195,0.096],[0.208,0.098]], 8, 0)

// A roughly one-head-height head: 0.22 m across and capped at exactly 2.00 m.
tube('Head', [[0,1.76,leanZ(1.76)],[0,1.89,leanZ(1.89)],[0,2.00,leanZ(2.00)]], [[0.088,0.082],[0.110,0.100],[0.060,0.055]], 8, 2)

for (const side of [-1, 1]) {
  const suffix = side < 0 ? 'L' : 'R'
  const x = side * 0.235
  const zShoulder = leanZ(1.60)
  tube(`UpperArm.${suffix}`, [[x,1.60,zShoulder],[x + side*0.015,1.29,zShoulder+0.012]], [[0.055,0.052],[0.045,0.043]], 6)
  tube(`Forearm.${suffix}`, [[x + side*0.015,1.31,zShoulder+0.012],[x + side*0.006,1.03,zShoulder-0.004]], [[0.045,0.043],[0.032,0.030]], 6)
  // The distal cap is the measured hanging-hand landmark: y = 0.95 m.
  tube(`Hand.${suffix}`, [[x + side*0.006,1.05,zShoulder-0.004],[x,0.95,zShoulder-0.018]], [[0.038,0.030],[0.028,0.024]], 6, 2)
}

// Collapse all components of each colour to one group, yielding exactly three
// GLB primitives/material surfaces on a single SkinnedMesh.
const materialGeometries = parts.map(group => BufferGeometryUtils.mergeGeometries(group, false))
let geometry = BufferGeometryUtils.mergeGeometries(materialGeometries, true).toNonIndexed()
geometry.computeVertexNormals()
geometry.name = 'WalkerGeometry'
const makeMaterial = (name, r, g, b) => new THREE.MeshStandardMaterial({ name, color: new THREE.Color(r, g, b), roughness: 1, metalness: 0, flatShading: true })
const materials = [
  makeMaterial('Torso', 0.36, 0.40, 0.34),
  makeMaterial('Limbs', 0.44, 0.42, 0.38),
  makeMaterial('Head', 0.62, 0.58, 0.50),
]
const mesh = new THREE.SkinnedMesh(geometry, materials); mesh.name = 'WalkerMesh'
const skeleton = new THREE.Skeleton(bones); mesh.add(root); mesh.bind(skeleton)
const scene = new THREE.Group(); scene.name = 'Walker'; scene.add(mesh)
scene.updateMatrixWorld(true)

const triangles = geometry.attributes.position.count / 3
if (triangles > 600) throw new Error(`triangle budget exceeded: ${triangles}`)
const exporter = new GLTFExporter()
let glb = await exporter.parseAsync(scene, { binary: true, onlyVisible: false })

// GLTFExporter represents material groups with an index accessor even when the
// source BufferGeometry is non-indexed. Expand each primitive after export so
// the delivered GLB retains the required flat-shaded, genuinely non-indexed
// representation while still carrying three surfaces on one skinned mesh.
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
      const oldAccessor = json.accessors[accessorIndex]
      const oldView = json.bufferViews[oldAccessor.bufferView]
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
        for (let i = 0; i < floats.length; i += 3) for (let axis = 0; axis < 3; axis++) {
          replacement.min[axis] = Math.min(replacement.min[axis], floats[i + axis])
          replacement.max[axis] = Math.max(replacement.max[axis], floats[i + axis])
        }
      }
      primitive.attributes[semantic] = json.accessors.push(replacement) - 1
    }
    delete primitive.indices
  }

  const binary = Buffer.concat([originalBin, ...additions])
  json.buffers[0].byteLength = binary.length
  let jsonBytes = Buffer.from(JSON.stringify(json))
  jsonBytes = Buffer.concat([jsonBytes, Buffer.alloc((4 - jsonBytes.length % 4) % 4, 0x20)])
  const binaryPadding = Buffer.alloc((4 - binary.length % 4) % 4)
  const totalLength = 12 + 8 + jsonBytes.length + 8 + binary.length + binaryPadding.length
  const result = Buffer.alloc(totalLength)
  result.writeUInt32LE(0x46546c67, 0); result.writeUInt32LE(2, 4); result.writeUInt32LE(totalLength, 8)
  result.writeUInt32LE(jsonBytes.length, 12); result.writeUInt32LE(0x4e4f534a, 16); jsonBytes.copy(result, 20)
  const newBinHeader = 20 + jsonBytes.length
  result.writeUInt32LE(binary.length + binaryPadding.length, newBinHeader); result.writeUInt32LE(0x004e4942, newBinHeader + 4)
  binary.copy(result, newBinHeader + 8); binaryPadding.copy(result, newBinHeader + 8 + binary.length)
  return result
}
glb = expandMaterialGroups(glb)
const out = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'assets', 'models')
mkdirSync(out, { recursive: true }); writeFileSync(join(out, 'walker.glb'), Buffer.from(glb))
console.log(`walker.glb ${triangles} triangles; ${geometry.attributes.position.count} flat-shaded vertices; ${bones.length} bones; hip 0.920 m; shoulder 1.600 m; hand 0.950 m; head 2.000 m`)
