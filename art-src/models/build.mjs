// Authors the three arena landmarks and writes glTF to assets/models/.
//
//   npm install && npm run build
//   godot --headless --import        <- required, see below
//
// **Godot will not notice a rewritten .glb on its own.** The import cache is
// keyed on the file, and a game started without re-importing shows the previous
// model with the previous materials — so a colour change looks like a colour
// change that did not apply, and the next thing edited is the wrong file.
//
// **three.js is a modelling tool here, not a renderer.** It runs offline, on a
// developer's machine, and writes three .glb files. Nothing at runtime knows it
// exists; there is no JavaScript in the shipped game, no web view, and no
// dependency on this directory to build or run anything. The `.gdignore` beside
// this file keeps Godot from even scanning it.
//
// The reason it is here at all is the shape test: `MeshBuilder` builds boxes,
// tubes and wedges from code, and that covers every prop in the game. It does
// not do cones, it does not do lathed bodies of revolution, and it does not make
// a fifty-strut lattice pleasant to write. Those are the three landmarks. If a
// fourth one can be built with `MeshBuilder`, it belongs in `MeshBuilder`.
//
// Colour lives in materials, not in `COLOR_0`. Godot's glTF importer turns a
// material into a `StandardMaterial3D` and a vertex-colour stream into an
// attribute that needs `vertex_color_use_as_albedo` set on the other side — one
// of which survives a round trip through the importer without anyone having to
// remember it.

import * as THREE from 'three'
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js'
import * as BufferGeometryUtils from 'three/examples/jsm/utils/BufferGeometryUtils.js'
import { writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

// GLTFExporter reaches for the browser's FileReader. Node has Blob and has had
// it since 18, but not this; nine lines of shim is cheaper than pulling in a DOM
// implementation to export three static meshes.
if (typeof globalThis.FileReader === 'undefined') {
  globalThis.FileReader = class FileReader {
    readAsArrayBuffer(blob) {
      blob.arrayBuffer().then((buffer) => {
        this.result = buffer
        this.onloadend?.({ target: this })
      })
    }
  }
}

const here = dirname(fileURLToPath(import.meta.url))
const out = join(here, '..', '..', 'assets', 'models')

// --- materials --------------------------------------------------------------
//
// Flat shading throughout. Everything else in the game is faceted — the bodies,
// the props, the scatter — and a smoothly shaded landmark next to them reads as
// an asset from a different game rather than as a smoother object.
//
// Named, because the name is what Godot's importer calls the resulting
// `StandardMaterial3D`. An unnamed material imports as "material_0" and the next
// export renumbers it.

const materials = {
  steel: new THREE.MeshStandardMaterial({
    name: 'steel', color: 0x6d7278, roughness: 0.82, metalness: 0.15, flatShading: true,
  }),
  // Darker than it wants to be. At 0x8a5133 the pylon's bracing read as
  // safety orange against a tan floor and pulled the eye off everything the
  // player is actually meant to be looking at.
  rust: new THREE.MeshStandardMaterial({
    name: 'rust', color: 0x6f412a, roughness: 0.95, metalness: 0.05, flatShading: true,
  }),
  paint: new THREE.MeshStandardMaterial({
    name: 'paint', color: 0xb9b3a4, roughness: 0.78, metalness: 0.0, flatShading: true,
  }),
  // The coach gets its own colour rather than sharing the silo's. Two landmarks
  // in the same cream at opposite ends of the map read as the same object seen
  // twice, which is the one thing a landmark must not do.
  livery: new THREE.MeshStandardMaterial({
    name: 'livery', color: 0x4e6156, roughness: 0.85, metalness: 0.0, flatShading: true,
  }),

  glass: new THREE.MeshStandardMaterial({
    name: 'glass', color: 0x2b3a3d, roughness: 0.35, metalness: 0.1, flatShading: true,
  }),
  tyre: new THREE.MeshStandardMaterial({
    name: 'tyre', color: 0x1c1c1e, roughness: 1.0, metalness: 0.0, flatShading: true,
  }),
}

// --- the lattice tool -------------------------------------------------------

/// One strut of a lattice: a box stretched between two points.
///
/// This is the whole reason the pylon is authored out here. Writing it as forty
/// hand-placed boxes with hand-computed yaw and pitch is possible and it is what
/// `MeshBuilder` would require; `lookAt` on a throwaway Object3D does it in four
/// lines and the result is a lattice whose bracing actually meets its legs.
function strut(from, to, thickness) {
  const a = new THREE.Vector3(...from)
  const b = new THREE.Vector3(...to)
  const length = a.distanceTo(b)

  const geometry = new THREE.BoxGeometry(thickness, thickness, length)

  const anchor = new THREE.Object3D()
  anchor.position.copy(a).add(b).multiplyScalar(0.5)
  anchor.lookAt(b)
  anchor.updateMatrix()

  geometry.applyMatrix4(anchor.matrix)
  return geometry
}

function box(size, at, rotation = [0, 0, 0]) {
  const geometry = new THREE.BoxGeometry(...size)
  const anchor = new THREE.Object3D()
  anchor.position.set(...at)
  anchor.rotation.set(...rotation)
  anchor.updateMatrix()
  geometry.applyMatrix4(anchor.matrix)
  return geometry
}

function place(geometry, at, rotation = [0, 0, 0]) {
  const anchor = new THREE.Object3D()
  anchor.position.set(...at)
  anchor.rotation.set(...rotation)
  anchor.updateMatrix()
  geometry.applyMatrix4(anchor.matrix)
  return geometry
}

/// Merges the parts sharing one material into a single mesh.
///
/// One mesh per material rather than one mesh per strut: forty `Mesh` nodes
/// import as forty children, and every one of them is a draw call and a node the
/// landmark has to carry into every scene it appears in.
function surface(parts, material, name) {
  const merged = BufferGeometryUtils.mergeGeometries(parts, false)
  merged.deleteAttribute('uv')

  // Flat shading is **baked here**, not left to the material.
  //
  // `flatShading: true` is a three.js render-time flag with no glTF equivalent:
  // the format has no such property, so the exporter drops it silently and the
  // model arrives in Godot smoothly shaded. Splitting the geometry so every
  // triangle owns its vertices and then computing normals is the only version of
  // flat shading that survives the file — and it is what makes these agree with
  // every faceted thing `MeshBuilder` produces.
  const geometry = merged.toNonIndexed()
  geometry.computeVertexNormals()

  const mesh = new THREE.Mesh(geometry, material)
  mesh.name = name
  return mesh
}

/// Drops a group so its lowest point is exactly y = 0.
///
/// Applied rather than checked. A lattice built from struts overshoots its own
/// corners by half a strut, so the pylon's feet land 3 cm below where the maths
/// says — which is not worth hand-correcting and is exactly the kind of small
/// wrongness that turns into a landmark hovering once someone changes a
/// thickness.
function ground(group) {
  const drop = new THREE.Box3().setFromObject(group).min.y

  group.traverse((node) => {
    if (node.isMesh)
      node.geometry.translate(0, -drop, 0)
  })

  return group
}

// --- the pylon --------------------------------------------------------------
//
// A lattice transmission tower, 13.1 m, tapering from a 3.2 m base to a 0.9 m
// waist with one crossarm. Sited to be seen from across the arena and to tell
// the player which way they are facing before the fog gives them anything else.

function buildPylon() {
  const legs = []
  const braces = []

  const height = 12.6
  const baseHalf = 1.6
  const topHalf = 0.45
  const levels = [0.0, 3.4, 6.9, 10.0, height]

  const halfAt = (y) => THREE.MathUtils.lerp(baseHalf, topHalf, y / height)
  const corner = (i, y) => {
    const h = halfAt(y)
    const sx = i === 0 || i === 3 ? -1 : 1
    const sz = i < 2 ? -1 : 1
    return [sx * h, y, sz * h]
  }

  // Four legs, each a chain of straight struts between belt heights, because a
  // single strut from the ground to the top would not taper.
  for (let i = 0; i < 4; i++) {
    for (let l = 0; l + 1 < levels.length; l++)
      legs.push(strut(corner(i, levels[l]), corner(i, levels[l + 1]), 0.17))
  }

  // Belts, and one diagonal per face per bay. A full X on every face is twice
  // the triangles for a silhouette that at this distance is the same lattice.
  for (let l = 1; l + 1 < levels.length; l++) {
    for (let i = 0; i < 4; i++)
      braces.push(strut(corner(i, levels[l]), corner((i + 1) % 4, levels[l]), 0.11))
  }

  for (let l = 0; l + 1 < levels.length - 1; l++) {
    for (let i = 0; i < 4; i++) {
      braces.push(strut(
        corner(i, levels[l]),
        corner((i + 1) % 4, levels[l + 1]),
        0.09))
    }
  }

  // The crossarm, which is what makes it read as a pylon rather than as a
  // derrick. Asymmetric on purpose: one arm is bent down, the way one is in
  // every photograph of a line that has been left standing too long.
  const armY = 10.9
  braces.push(strut([-3.3, armY, 0], [3.3, armY, 0], 0.15))
  braces.push(strut([-3.3, armY, 0], [0, height, 0], 0.08))
  braces.push(strut([3.3, armY - 0.55, 0], [0, height, 0], 0.08))
  braces.push(strut([3.0, armY, 0], [3.3, armY - 0.55, 0], 0.12))

  // Insulators. Three little stacks under the arm — the detail that reads at
  // distance is the gap between the arm and the wire, not the wire.
  for (const x of [-2.6, -1.2, 2.5]) {
    braces.push(box([0.16, 0.55, 0.16], [x, armY - 0.35, 0]))
  }

  const group = new THREE.Group()
  group.name = 'Pylon'
  group.add(surface(legs, materials.steel, 'PylonLegs'))
  group.add(surface(braces, materials.rust, 'PylonBracing'))
  return group
}

// --- the silo ---------------------------------------------------------------
//
// A ribbed grain silo, 10.6 m: a body of revolution with a cone on top and a
// caged ladder up one side. The cone is the part `MeshBuilder` has no primitive
// for, and a twelve-sided cone hand-written as twelve triangles is the kind of
// code that is wrong by one vertex and looks fine.

function buildSilo() {
  const sides = 12
  const radius = 2.05
  const bodyHeight = 8.1

  const shell = []
  const trim = []

  shell.push(place(
    new THREE.CylinderGeometry(radius, radius, bodyHeight, sides, 1, false),
    [0, bodyHeight * 0.5, 0]))

  // The cone. Slightly wider than the body so it overhangs — a roof flush with
  // the wall reads as a cap rather than as a roof.
  shell.push(place(
    new THREE.ConeGeometry(radius + 0.28, 2.1, sides, 1, false),
    [0, bodyHeight + 1.05, 0]))

  // A vent at the peak, so the silhouette does not end in a mathematical point.
  trim.push(place(
    new THREE.CylinderGeometry(0.28, 0.34, 0.5, 6),
    [0, bodyHeight + 2.3, 0]))

  // Ribs. Flat rings, not tori: a torus at this size is 200 triangles for a
  // band that is four pixels tall on screen.
  for (const y of [1.7, 3.6, 5.5, 7.4]) {
    trim.push(place(
      new THREE.CylinderGeometry(radius + 0.09, radius + 0.09, 0.16, sides, 1, true),
      [0, y, 0]))
  }

  // The ladder, on the +Z face. Two rails and four rungs — enough to give the
  // wall a vertical line and a sense of how tall the thing is.
  const rail = radius + 0.14
  trim.push(box([0.07, bodyHeight - 0.6, 0.07], [-0.28, bodyHeight * 0.5 - 0.2, rail]))
  trim.push(box([0.07, bodyHeight - 0.6, 0.07], [0.28, bodyHeight * 0.5 - 0.2, rail]))
  for (const y of [1.4, 3.2, 5.0, 6.8])
    trim.push(box([0.62, 0.06, 0.06], [0, y, rail]))

  const group = new THREE.Group()
  group.name = 'Silo'
  group.add(surface(shell, materials.paint, 'SiloShell'))
  group.add(surface(trim, materials.rust, 'SiloTrim'))
  return group
}

// --- the coach --------------------------------------------------------------
//
// A crushed service coach, 2.7 m at its highest and nine long. The only one of
// the three that is cover rather than a beacon, and the only one that reads at
// close range, so it is the one that gets the deformation.

function buildCoach() {
  const body = []
  const glass = []
  const rubber = []

  // The hull is a segmented box so the roof can be pushed in. A plain box would
  // have to be crushed by scaling, which moves the wheels and the windows with
  // it.
  const hull = new THREE.BoxGeometry(2.42, 2.5, 8.6, 1, 2, 4)
  const position = hull.attributes.position

  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i)
    const y = position.getY(i)
    const z = position.getZ(i)

    if (y > 0.6) {
      // The roof caves in towards the middle and folds down over the far end.
      const along = (z + 4.3) / 8.6
      const dent = Math.sin(along * Math.PI) * 0.62 + Math.max(0, along - 0.55) * 1.35
      position.setY(i, y - dent)
      position.setX(i, x * (1.0 - dent * 0.16))
    }
  }

  position.needsUpdate = true
  hull.computeVertexNormals()
  body.push(place(hull, [0, 1.32, 0]))

  // A skirt under the hull, so it does not float when the ground under it is
  // not flat — this is the one landmark small enough for a 1.75 m height field
  // to show daylight beneath.
  body.push(box([2.2, 0.7, 8.1], [0, 0.45, 0]))

  // Windows: one strip a side, broken by the crush at the far end.
  for (const side of [-1, 1]) {
    glass.push(box([0.06, 0.72, 4.4], [side * 1.2, 1.72, -1.4]))
    glass.push(box([0.06, 0.5, 1.5], [side * 1.16, 1.36, 2.2], [side * 0.2, 0, 0]))
  }

  glass.push(box([2.1, 0.85, 0.07], [0, 1.8, -4.3]))

  // Wheels. Six-sided, and half-buried: a coach with round wheels standing at
  // its design ride height has not been abandoned, it is parked.
  for (const z of [-3.0, 0.6, 2.9]) {
    for (const side of [-1, 1]) {
      rubber.push(place(
        new THREE.CylinderGeometry(0.62, 0.62, 0.34, 6),
        [side * 1.12, 0.34, z],
        [0, 0, Math.PI * 0.5]))
    }
  }

  const group = new THREE.Group()
  group.name = 'Coach'
  group.add(surface(body, materials.livery, 'CoachBody'))
  group.add(surface(glass, materials.glass, 'CoachGlass'))
  group.add(surface(rubber, materials.tyre, 'CoachWheels'))
  return group
}

// --- export -----------------------------------------------------------------

function report(group) {
  const bounds = new THREE.Box3().setFromObject(group)
  const size = bounds.getSize(new THREE.Vector3())

  let triangles = 0
  group.traverse((node) => {
    if (!node.isMesh)
      return

    // Indexed or not. Counting positions on an indexed geometry gives a
    // fractional answer, which is how the first run of this reported "138.33
    // tris" and made it obvious.
    const geometry = node.geometry
    triangles += (geometry.index ? geometry.index.count : geometry.attributes.position.count) / 3
  })

  return { triangles, size, bounds }
}

async function write(group, file) {
  const exporter = new GLTFExporter()
  const glb = await exporter.parseAsync(group, { binary: true, onlyVisible: false })

  mkdirSync(out, { recursive: true })
  writeFileSync(join(out, file), Buffer.from(glb))

  const { triangles, size, bounds } = report(group)
  console.log(
    `${file.padEnd(14)} ${String(triangles).padStart(4)} tris  ` +
    `${size.x.toFixed(1)} x ${size.y.toFixed(1)} x ${size.z.toFixed(1)} m  ` +
    `(base at y=${bounds.min.y.toFixed(2)})`)
}

// The base of every landmark sits at y = 0. Godot plants these on the terrain by
// setting the node's Y, so a model authored around its own centre buries half of
// itself in the ground and there is nothing in the scene to say so — it just
// looks like a shorter landmark.
for (const [group, file] of [
  [buildPylon(), 'pylon.glb'],
  [buildSilo(), 'silo.glb'],
  [buildCoach(), 'coach.glb'],
]) {
  await write(ground(group), file)
}
