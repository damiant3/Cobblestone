// Dump the crown-region vertices of each fireworks city straight out of WASM
// linear memory: every vertex above a y line, with its colour and kind, and
// the triangles they form. Nothing is inferred from a picture.
//   node apps/fireworks/fw-dump.mjs apps/fireworks/web/fireworks-show.wasm [yMaxPx=120]
// The FW-1 finding was read from this output (fireworks-backlog.md).
import { readFile } from 'node:fs/promises';
const WASM = process.argv[2];
const yMax = Number(process.argv[3] ?? 120) * 16;
const bytes = await readFile(WASM);
const m = await WebAssembly.instantiate(bytes, {
  wasi_snapshot_preview1: { fd_write: () => 0, fd_read: () => 0 },
  env: { blit_framebuf: () => {}, on_key: () => 0 },
});
const show = m.instance.exports;
const mem = new Int32Array(show.memory.buffer);
const VTX_COUNT = 131072 / 4, VTX_BASE = 262144 / 4;
for (let c = 0; c < 4; c++) {
  const n = show.build_city(c);
  const count = mem[VTX_COUNT];
  let minY = 1e9, minYi = -1;
  const kinds = [0, 0, 0, 0];
  const above = [];
  for (let i = 0; i < count; i++) {
    const o = VTX_BASE + i * 8;
    const x = mem[o], y = mem[o + 1], r = mem[o + 2], g = mem[o + 3], b = mem[o + 4], k = mem[o + 5];
    kinds[Math.min(k, 3)]++;
    if (y < minY) { minY = y; minYi = i; }
    if (y < yMax) above.push({ i, x, y, r, g, b, k, tri: Math.floor(i / 3) });
  }
  console.log(`city ${c}: build_city=${n} count=${count} minY=${(minY / 16).toFixed(1)}px (vertex ${minYi}) kinds masonry=${kinds[0]} window=${kinds[1]} beacon=${kinds[2]}`);
  // Group the above-line vertices by triangle and print each triangle once.
  const tris = new Map();
  for (const v of above) { if (!tris.has(v.tri)) tris.set(v.tri, []); tris.get(v.tri).push(v); }
  let printed = 0;
  for (const [t, vs] of [...tris.entries()].sort((a, b) => Math.min(...a[1].map(v => v.y)) - Math.min(...b[1].map(v => v.y)))) {
    if (printed++ >= 24) { console.log(`  ... ${tris.size - 24} more triangles above ${yMax / 16}px`); break; }
    const full = [];
    for (let j = 0; j < 3; j++) { const o = VTX_BASE + (t * 3 + j) * 8; full.push(`(${(mem[o] / 16).toFixed(1)},${(mem[o + 1] / 16).toFixed(1)})`); }
    const v = vs[0];
    console.log(`  tri ${t} kind ${v.k} rgb ${v.r},${v.g},${v.b} ${full.join(' ')}`);
  }
  // Beacons: every kind-2 vertex's y against the nearest masonry vertex sharing its x within 8 px.
  const beacons = [];
  for (let i = 0; i < count; i++) { const o = VTX_BASE + i * 8; if (mem[o + 5] === 2) beacons.push({ x: mem[o], y: mem[o + 1] }); }
  const seen = new Set(); let shown = 0;
  for (const bv of beacons) {
    const key = `${Math.round(bv.x / 64)}:${Math.round(bv.y / 64)}`; if (seen.has(key)) continue; seen.add(key);
    let nearest = null;
    for (let i = 0; i < count; i++) { const o = VTX_BASE + i * 8; if (mem[o + 5] !== 0) continue; if (Math.abs(mem[o] - bv.x) > 128) continue; const dy = mem[o + 1] - bv.y; if (dy > 0 && (nearest === null || dy < nearest)) nearest = dy; }
    if (shown++ < 12) console.log(`  beacon at (${(bv.x / 16).toFixed(1)},${(bv.y / 16).toFixed(1)}) px; nearest masonry below at +${nearest === null ? 'none' : (nearest / 16).toFixed(1)} px`);
  }
  console.log(`  beacons (distinct): ${seen.size}`);
}
