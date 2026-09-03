// Decode the safari page module's frame buffer and check the wire is sane.
//
//   node apps/safari/sf-decode.mjs apps/safari/build-output/safari-page.wasm
//
// This is the arm that has to pass before the page is worth looking at: a
// painter fed a malformed buffer draws nothing and says nothing about why.
// The assertion that carries the others is that the walk ends EXACTLY on the
// module's reported byte length. A layout that disagrees with the module's by
// one field does not fail where the field is; it desynchronises and reads a
// coordinate as a point count several commands later.
import { readFile } from 'node:fs/promises';

const WASM = process.argv[2] ?? 'apps/safari/build-output/safari-page.wasm';
const bytes = await readFile(WASM);

// Every module the plug emits declares the WASI descriptors whether or not it
// uses them, so a browser module gets throwing stubs. This one never calls them.
const stub = (n) => () => { throw new Error(`the module called ${n}; it should not`); };
const inst = new WebAssembly.Instance(new WebAssembly.Module(bytes), {
  wasi_snapshot_preview1: {
    fd_write: stub('fd_write'), fd_read: stub('fd_read'), fd_close: stub('fd_close'),
    fd_seek: stub('fd_seek'), proc_exit: stub('proc_exit'),
  },
});
const ex = inst.exports;
const base = ex.sfw_buffer_at(0) >>> 0;
const HEAD = 48;

const TAGS = { 0: 'SOLID', 2: 'SPAN_SHADE', 3: 'DISC', 4: 'RADIAL_POLY', 5: 'LINEAR_POLY', 6: 'ELLIPSE_POLY' };
// Colour words per tag, and whether a polygon follows. The geom LENGTH is on
// the wire, so nothing here has to know it.
const SPEC = {
  0: { cols: 1, poly: true }, 2: { cols: 2, poly: true }, 3: { cols: 1, poly: false },
  4: { cols: 2, poly: true }, 5: { cols: 2, poly: true }, 6: { cols: 2, poly: true },
};

const hex = (c) => '#' + (c & 0xffffff).toString(16).padStart(6, '0');

function frame(label) {
  const used = ex.sfw_render(0) >>> 0;
  const dv = new DataView(ex.memory.buffer);
  const head = {
    sky: dv.getUint32(base, true), horizon: dv.getUint32(base + 4, true),
    sunOn: dv.getUint32(base + 8, true), x: dv.getFloat64(base + 16, true),
    y: dv.getFloat64(base + 24, true), scale: dv.getFloat64(base + 32, true),
    roll: dv.getFloat64(base + 40, true),
  };
  let o = HEAD, n = 0, pts = 0, nonFinite = 0, inCanvas = 0;
  const seen = {};
  while (o < used) {
    const tag = dv.getUint32(base + o, true); o += 4;
    const spec = SPEC[tag];
    if (!spec) { console.log(`  BAD TAG ${tag} at byte ${o - 4} (command ${n})`); return null; }
    seen[tag] = (seen[tag] || 0) + 1;
    o += 4 * spec.cols;
    const strength = dv.getFloat64(base + o, true); o += 8;
    if (!Number.isFinite(strength)) nonFinite++;
    const ng = dv.getUint32(base + o, true); o += 4;
    if (ng > 64) { console.log(`  ABSURD geom count ${ng} at command ${n}`); return null; }
    for (let i = 0; i < ng; i++) { if (!Number.isFinite(dv.getFloat64(base + o, true))) nonFinite++; o += 8; }
    if (spec.poly) {
      const np = dv.getUint32(base + o, true); o += 4;
      if (np > 4096) { console.log(`  ABSURD point count ${np} at command ${n}`); return null; }
      pts += np;
      for (let i = 0; i < np; i++) {
        const x = dv.getFloat64(base + o, true), y = dv.getFloat64(base + o + 8, true); o += 16;
        if (!Number.isFinite(x) || !Number.isFinite(y)) { nonFinite++; continue; }
        if (x >= 0 && x <= 960 && y >= 0 && y <= 600) inCanvas++;
      }
    }
    n++;
  }
  const exact = o === used;
  console.log(`${label}: ${used} B, ${n} commands, ${pts} points, ends exactly on the length: ${exact}`);
  console.log(`  tags ${Object.entries(seen).map(([t, c]) => `${TAGS[t]}=${c}`).join(' ')}`);
  console.log(`  sky ${hex(head.sky)} horizon ${hex(head.horizon)} sun ${head.sunOn ? 'on' : 'off'} roll ${head.roll.toFixed(4)}`);
  console.log(`  points inside the canvas: ${inCanvas} of ${pts} (${(100 * inCanvas / pts).toFixed(1)}%)   non-finite ${nonFinite}`);
  return { used, n, exact, nonFinite, sky: head.sky, roll: head.roll, inCanvas, pts };
}

ex.sfw_reset(0);
const a = frame('frame   0');
for (let i = 0; i < 400; i++) ex.sfw_forward(0);
const b = frame('frame 400');

// The ride must actually go somewhere. sky-color is a function of the clock, so
// a clock that advances moves the sky; a ride that restarts every frame pins it.
ex.sfw_reset(0);
const skies = new Set(); let banked = 0;
for (let i = 0; i < 900; i++) {
  ex.sfw_render(0);
  const dv = new DataView(ex.memory.buffer);
  skies.add(dv.getUint32(base, true));
  if (Math.abs(dv.getFloat64(base + 40, true)) > 1e-9) banked++;
  ex.sfw_forward(0);
}

console.log('');
const ok = a && b && a.exact && b.exact && a.nonFinite === 0 && b.nonFinite === 0
        && skies.size > 1 && banked > 0 && a.inCanvas / a.pts > 0.8;
console.log(`CONTROL both frames end exactly on their length : ${a?.exact && b?.exact}`);
console.log(`CONTROL no non-finite coordinates              : ${a?.nonFinite === 0 && b?.nonFinite === 0}`);
console.log(`CONTROL the ride advances (${skies.size} sky colours, ${banked} banked frames of 900)`);
console.log(ok ? 'PASS' : 'FAIL');
process.exit(ok ? 0 : 1);
