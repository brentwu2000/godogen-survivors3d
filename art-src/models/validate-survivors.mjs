import { readFileSync } from 'node:fs'

const names = process.argv.slice(2)
const roster = names.length ? names : ['drifter']
const bytes = { 5121:1, 5123:2, 5125:4, 5126:4 }
const comps = { SCALAR:1, VEC2:2, VEC3:3,VEC4:4,MAT4:16 }

for (const name of roster) {
  const glb = readFileSync(new URL(`../../assets/models/survivors/${name}.glb`, import.meta.url))
  const jsonLength = glb.readUInt32LE(12)
  const json = JSON.parse(glb.subarray(20, 20 + jsonLength))
  const binaryHeader = 20 + jsonLength
  const bin = glb.subarray(binaryHeader + 8, binaryHeader + 8 + glb.readUInt32LE(binaryHeader))

  if (json.meshes?.length !== 1) throw Error(`${name}: expected one mesh, got ${json.meshes?.length ?? 0}`)
  if (json.skins?.length !== 1) throw Error(`${name}: expected one skin`)
  const primitives = json.meshes[0].primitives
  if (primitives.length !== 4) throw Error(`${name}: expected Torso/Limbs/Head/Kit, got ${primitives.length} surfaces`)

  const joints = json.skins[0].joints.map(i => json.nodes[i].name)
  const required = ['Root','Hips','Spine','Chest','Head','Thigh.L','Shin.L','Foot.L','Thigh.R','Shin.R','Foot.R','UpperArm.L','Forearm.L','Hand.L','UpperArm.R','Forearm.R','Hand.R']
  for (const joint of required) if (!joints.includes(joint)) throw Error(`${name}: missing ${joint}`)

  let triangles = 0
  for (const [surface, primitive] of primitives.entries()) {
    if (primitive.indices !== undefined) throw Error(`${name}: surface ${surface} is indexed`)
    for (const semantic of ['POSITION','NORMAL','JOINTS_0','WEIGHTS_0'])
      if (primitive.attributes[semantic] === undefined) throw Error(`${name}: surface ${surface} missing ${semantic}`)

    const count = json.accessors[primitive.attributes.POSITION].count
    if (count % 3) throw Error(`${name}: surface ${surface} is not triangles`)
    triangles += count / 3

    const read = semantic => {
      const accessor = json.accessors[primitive.attributes[semantic]]
      const view = json.bufferViews[accessor.bufferView]
      const size = bytes[accessor.componentType] * comps[accessor.type]
      return { accessor, stride:view.byteStride || size, start:(view.byteOffset || 0) + (accessor.byteOffset || 0) }
    }
    const position = read('POSITION'), weights = read('WEIGHTS_0')
    const edges = new Map()
    const key = i => {
      const o = position.start + i * position.stride
      return `${bin.readFloatLE(o).toFixed(6)},${bin.readFloatLE(o+4).toFixed(6)},${bin.readFloatLE(o+8).toFixed(6)}`
    }
    for (let i=0; i<count; i++) {
      const o = weights.start + i * weights.stride
      const weight = [0,4,8,12].map(n => bin.readFloatLE(o+n))
      if (Math.abs(weight.reduce((a,b)=>a+b,0)-1) > 1e-5 || weight[0] !== 1)
        throw Error(`${name}: vertex ${i} has blended weights`)
    }
    for (let i=0; i<count; i+=3) {
      const vertex=[key(i),key(i+1),key(i+2)]
      for (const [a,b] of [[0,1],[1,2],[2,0]]) {
        const edge=[vertex[a],vertex[b]].sort().join('|')
        edges.set(edge,(edges.get(edge)||0)+1)
      }
    }
    const open=[...edges.values()].filter(n=>n!==2).length
    if (open) throw Error(`${name}: surface ${surface} has ${open} open/non-manifold edges`)
  }

  if (triangles > 900) throw Error(`${name}: ${triangles}/900 triangles`)
  console.log(`${name}: ${triangles} tris, 4 surfaces, 1 skin, ${joints.length} bones, closed/non-indexed`)
}
