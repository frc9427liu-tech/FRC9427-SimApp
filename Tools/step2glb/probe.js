// 抽樣檢查:STEP 零件的包圍盒(判斷零件座標是否已是整車座標)
const fs = require('fs');
const path = require('path');
const occt = require('occt-import-js');

(async () => {
  const dir = process.argv[2];
  const n = parseInt(process.argv[3] || '8', 10);
  const files = fs.readdirSync(dir).filter(f => f.endsWith('.step'))
    .map(f => ({ f, s: fs.statSync(path.join(dir, f)).size }))
    .sort((a, b) => b.s - a.s);
  // 取最大的 n 個 + 隨機 n 個
  const pick = files.slice(0, n).concat(files.filter((_, i) => i % Math.floor(files.length / n) === 0).slice(0, n));
  const o = await occt();
  for (const { f, s } of pick) {
    const buf = fs.readFileSync(path.join(dir, f));
    const t0 = Date.now();
    const r = o.ReadStepFile(buf, { linearUnit: 'meter', linearDeflectionType: 'bounding_box_ratio', linearDeflection: 0.005, angularDeflection: 0.5 });
    let mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9], tris = 0;
    for (const m of r.meshes || []) {
      const p = m.attributes.position.array;
      for (let i = 0; i < p.length; i += 3) for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], p[i + k]); mx[k] = Math.max(mx[k], p[i + k]); }
      tris += m.index.array.length / 3;
    }
    console.log(`${f} ${(s / 1024).toFixed(0)}KB ok=${r.success} meshes=${(r.meshes || []).length} tris=${tris} ${Date.now() - t0}ms bbox=[${mn.map(x => x.toFixed(3))}]..[${mx.map(x => x.toFixed(3))}]`);
  }
})();
