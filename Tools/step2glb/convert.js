// 把 Onshape 匯出的一整批 STEP 零件檔轉成單一 glb(每個零件一個 node,保留名稱)
// 用法: node convert.js <STEP資料夾> <輸出.glb> [最小尺寸m=0.02]
const fs = require('fs');
const path = require('path');
const occt = require('occt-import-js');
const { Document, NodeIO } = require('@gltf-transform/core');

(async () => {
  const dir = process.argv[2], out = process.argv[3];
  const minDim = parseFloat(process.argv[4] || '0.02');
  const files = fs.readdirSync(dir).filter(f => f.toLowerCase().endsWith('.step')).sort();
  const o = await occt();

  const doc = new Document();
  const buffer = doc.createBuffer();
  const scene = doc.createScene('robot');
  doc.getRoot().setDefaultScene(scene);
  const mats = new Map();
  const matFor = (c) => {
    const key = c ? c.map(x => Math.round(x * 20)).join(',') : 'gray';
    if (!mats.has(key)) {
      const m = doc.createMaterial('m' + mats.size)
        .setBaseColorFactor(c ? [c[0], c[1], c[2], 1] : [0.6, 0.62, 0.66, 1])
        .setMetallicFactor(0.2).setRoughnessFactor(0.6);
      mats.set(key, m);
    }
    return mats.get(key);
  };

  const info = [];
  let kept = 0, skippedTiny = 0, skippedHardware = 0, failed = 0, totalTris = 0;
  const t0 = Date.now();
  for (let i = 0; i < files.length; i++) {
    const f = files[i];
    const name = f.replace(/^.*Top Level - /, '').replace(/\.step$/i, '');
    let r;
    try {
      r = o.ReadStepFile(fs.readFileSync(path.join(dir, f)),
        { linearUnit: 'meter', linearDeflectionType: 'bounding_box_ratio', linearDeflection: 0.004, angularDeflection: 0.6 });
    } catch (e) { failed++; continue; }
    if (!r.success || !r.meshes || r.meshes.length === 0) { failed++; continue; }

    let mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
    for (const m of r.meshes) {
      const p = m.attributes.position.array;
      for (let k = 0; k < p.length; k += 3) for (let a = 0; a < 3; a++) { mn[a] = Math.min(mn[a], p[k + a]); mx[a] = Math.max(mx[a], p[k + a]); }
    }
    const size = [mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]];
    const maxDim = Math.max(...size);
    const center = [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
    if (maxDim < minDim) { skippedTiny++; continue; }
    // 零件沒被擺到整車座標(中心在原點附近且很小)= 五金,略過
    const cd = Math.hypot(center[0], center[1], center[2]);
    if (cd < 0.02 && maxDim < 0.15) { skippedHardware++; continue; }

    const prims = [];
    const mesh = doc.createMesh(name);
    for (const m of r.meshes) {
      const pos = new Float32Array(m.attributes.position.array);
      const nor = m.attributes.normal ? new Float32Array(m.attributes.normal.array) : null;
      const idx = new Uint32Array(m.index.array);
      const prim = doc.createPrimitive()
        .setAttribute('POSITION', doc.createAccessor().setType('VEC3').setArray(pos).setBuffer(buffer))
        .setIndices(doc.createAccessor().setType('SCALAR').setArray(idx).setBuffer(buffer))
        .setMaterial(matFor(m.color));
      if (nor) prim.setAttribute('NORMAL', doc.createAccessor().setType('VEC3').setArray(nor).setBuffer(buffer));
      mesh.addPrimitive(prim);
      totalTris += idx.length / 3;
    }
    scene.addChild(doc.createNode(name).setMesh(mesh));
    info.push({ name, file: f, center: center.map(x => +x.toFixed(4)), size: size.map(x => +x.toFixed(4)) });
    kept++;
    if (i % 100 === 0) console.log(`${i}/${files.length} kept=${kept} tris=${totalTris} ${((Date.now() - t0) / 1000).toFixed(0)}s`);
  }
  fs.mkdirSync(path.dirname(out), { recursive: true });
  await new NodeIO().write(out, doc);
  fs.writeFileSync(out.replace(/\.glb$/i, '.parts.json'), JSON.stringify(info, null, 1));
  console.log(`DONE files=${files.length} kept=${kept} tiny=${skippedTiny} hardware=${skippedHardware} failed=${failed} tris=${totalTris} MB=${(fs.statSync(out).size / 1048576).toFixed(1)} time=${((Date.now() - t0) / 1000).toFixed(0)}s`);
})();
