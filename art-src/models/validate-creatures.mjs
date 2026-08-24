import { readFileSync } from 'node:fs'

const names = ['walker','runner','brute','bloater','spitter','boss','lantern']
const bytes = { 5121:1, 5123:2, 5125:4, 5126:4 }, comps = { SCALAR:1,VEC2:2,VEC3:3,VEC4:4,MAT4:16 }
for (const name of names) {
  const glb=readFileSync(new URL(`../../assets/models/${name}.glb`,import.meta.url)), jl=glb.readUInt32LE(12)
  const j=JSON.parse(glb.subarray(20,20+jl)), bh=20+jl, bin=glb.subarray(bh+8,bh+8+glb.readUInt32LE(bh))
  if(j.meshes.length!==1) throw Error(`${name}: ${j.meshes.length} meshes`)
  const prims=j.meshes[0].primitives, expected=name==='lantern'?4:3
  if(prims.length!==expected) throw Error(`${name}: ${prims.length} surfaces`)
  if(j.skins?.length!==1) throw Error(`${name}: missing single skin`)
  const joints=j.skins[0].joints.map(i=>j.nodes[i].name), required=['Thigh.L','Shin.L','Foot.L','Thigh.R','Shin.R','Foot.R','UpperArm.L','Forearm.L','Hand.L','UpperArm.R','Forearm.R','Hand.R']
  for(const n of required) if(!joints.includes(n)) throw Error(`${name}: missing ${n}`)
  let triangles=0
  for(const [pi,p] of prims.entries()) {
    if(p.indices!==undefined) throw Error(`${name}: surface ${pi} indexed`)
    for(const a of ['POSITION','NORMAL','JOINTS_0','WEIGHTS_0']) if(p.attributes[a]===undefined) throw Error(`${name}: surface ${pi} missing ${a}`)
    const count=j.accessors[p.attributes.POSITION].count; if(count%3) throw Error(`${name}: non-triangle vertex count`); triangles+=count/3
    const read=(semantic)=>{const a=j.accessors[p.attributes[semantic]],v=j.bufferViews[a.bufferView],size=bytes[a.componentType]*comps[a.type],stride=v.byteStride||size,start=(v.byteOffset||0)+(a.byteOffset||0);return {a,v,size,stride,start}}
    const P=read('POSITION'), J=read('JOINTS_0'), W=read('WEIGHTS_0'), edges=new Map()
    const key=i=>{const o=P.start+i*P.stride;return `${bin.readFloatLE(o).toFixed(6)},${bin.readFloatLE(o+4).toFixed(6)},${bin.readFloatLE(o+8).toFixed(6)}`}
    for(let i=0;i<count;i++) { const wo=W.start+i*W.stride, jo=J.start+i*J.stride, w=[0,4,8,12].map(o=>bin.readFloatLE(wo+o)); if(Math.abs(w.reduce((a,b)=>a+b,0)-1)>1e-5||w[0]!==1)throw Error(`${name}: non-dominant weight`); const joint=J.a.componentType===5123?bin.readUInt16LE(jo):bin.readUInt8(jo); if(joint>=joints.length)throw Error(`${name}: bad joint`) }
    for(let i=0;i<count;i+=3){const v=[key(i),key(i+1),key(i+2)];for(const [a,b] of [[0,1],[1,2],[2,0]]){const e=[v[a],v[b]].sort().join('|');edges.set(e,(edges.get(e)||0)+1)}}
    const open=[...edges.values()].filter(n=>n!==2).length; if(open)throw Error(`${name}: surface ${pi} has ${open} non-manifold/open geometric edges`)
  }
  const budget=name==='boss'?900:600;if(triangles>budget)throw Error(`${name}: ${triangles}/${budget}`)
  console.log(`${name}: ${triangles} tris, ${prims.length} surfaces, 1 skinned mesh, ${joints.length} bones, closed/non-indexed`)
}
