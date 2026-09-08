// Grade the fireworks skyline module: every city must build, and build to a
// vertex count a page can draw. This exists because a rebuild against the
// COMPILER-36 plug shipped a module whose hash multiply trapped `unreachable`
// on the first city, and nothing between the build and the push ran it.
//
//   node apps/fireworks/fw-verify.mjs [path/to/fireworks-show.wasm]
import { readFile } from 'node:fs/promises';
const WASM = process.argv[2] ?? 'apps/fireworks/web/fireworks-show.wasm';
const bytes = await readFile(WASM);
const m = await WebAssembly.instantiate(bytes, {
  wasi_snapshot_preview1: { fd_write: () => 0, fd_read: () => 0 },
  env: { blit_framebuf: () => {}, on_key: () => 0 },
});
const show = m.instance.exports;
let failed = 0;
for (let c = 0; c < 4; c++) {
  try {
    const v = show.build_city(c);
    if (v > 0 && v <= 60000) console.log('  ok    city', c, 'builds', v, 'vertices');
    else { console.log('  FAIL  city', c, 'answered', v, 'vertices'); failed++; }
  } catch (e) { console.log('  FAIL  city', c, 'trapped:', e.message); failed++; }
}
console.log(failed ? 'FAIL: ' + failed + ' of 4 cities' : 'PASS: 4 of 4 cities build');
process.exit(failed ? 1 : 0);
