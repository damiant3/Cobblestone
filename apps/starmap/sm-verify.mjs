// Drive web/starmap.wasm with the same import object and the same catalogue the
// page supplies, and grade what the module actually did.
//
// RUN BY HAND, and one of the six that still are. Counted 2026-09-08: 43
// *-verify.mjs graders live under apps/ and 37 are invoked by the build that
// writes the module they grade, which is the only moment a red can be about
// the module rather than about a file nobody made.
// Its own gate is `wasmtime starmap.wasm` plus a header read of the catalogue in
// PowerShell; what that cannot do is deliver 3.79 MB into linear memory and ask
// the module what it made of it, which is every arm below.
//
//   node apps/starmap/sm-verify.mjs
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const modBytes = readFileSync(join(here, 'web', 'starmap.wasm'));
const dat = new Uint8Array(readFileSync(join(here, 'data', 'starmap.dat')));

const CELL_STARS = 256, CELL_VISIBLE = 260, CELL_READY = 264, CELL_DATLEN = 268,
      CELL_NAMED = 272, CELL_NAMEOFF = 284, CELL_ERROR = 288;
const CAM = 352, CAM_YAW = 24, CAM_PITCH = 28, CAM_MAG = 48, CAM_FOV = 56, CAM_FLAGS = 60;
const DAT_BASE = 1048576, HDR_SIZE = 64, REC = 32;
const R_X = 4, R_MAG = 16, R_BV = 20, R_FLAGS = 26, R_NAMEIDX = 27;

let i32, u8, dv, out = '';
const geti = a => i32[a >> 2];
const wasi = {
  fd_write(fd, iov, n, ret) {
    let w = 0;
    for (let k = 0; k < n; k++) {
      const p = geti(iov + k * 8), len = geti(iov + k * 8 + 4);
      out += new TextDecoder().decode(u8.subarray(p, p + len)); w += len;
    }
    i32[ret >> 2] = w; return 0;
  }, fd_read: () => 0,
};

const inst = (await WebAssembly.instantiate(modBytes, { wasi_snapshot_preview1: wasi })).instance;
const w = inst.exports;
const need = DAT_BASE + dat.length;
if (w.memory.buffer.byteLength < need) w.memory.grow(Math.ceil((need - w.memory.buffer.byteLength) / 65536));
i32 = new Int32Array(w.memory.buffer); u8 = new Uint8Array(w.memory.buffer);
dv = new DataView(w.memory.buffer);

w._start();
console.log('[starmap-check] module said: ' + out.trim());

let bad = 0;
const check = (name, ok, saw) => {
  console.log((ok ? '  ok   ' : '  FAIL ') + name.padEnd(54) + ' ' + saw);
  if (!ok) bad++;
};

// --- refusals, before the good file ----------------------------------
// Each of these must be REFUSED with its own code. A loader that accepts them
// draws a plausible sky out of noise, which is the failure worth engineering
// against here, so the refusals are graded before the acceptance is.
u8.fill(0, DAT_BASE, DAT_BASE + 128);
check('a file with no STAR magic is refused', w.sm_load(4096) < 0 && geti(CELL_ERROR) === 1, 'err ' + geti(CELL_ERROR));

u8.set(dat.subarray(0, 128), DAT_BASE);
dv.setInt32(DAT_BASE + 4, 99, true);
check('an unknown version is refused', w.sm_load(dat.length) < 0 && geti(CELL_ERROR) === 2, 'err ' + geti(CELL_ERROR));
dv.setInt32(DAT_BASE + 4, 2, true);

check('a file past the window is refused', w.sm_load(99999999) < 0 && geti(CELL_ERROR) === 3, 'err ' + geti(CELL_ERROR));

check('a header claiming more stars than delivered is refused',
  w.sm_load(1024) < 0 && geti(CELL_ERROR) === 4, 'err ' + geti(CELL_ERROR));

// --- the real catalogue ----------------------------------------------
u8.set(dat, DAT_BASE);
const n = w.sm_load(dat.length);
check('the real catalogue loads', n > 0 && geti(CELL_ERROR) === 0, n + ' stars, err ' + geti(CELL_ERROR));
check('117,931 stars', geti(CELL_STARS) === 117931, geti(CELL_STARS));
check('489 names', geti(CELL_NAMED) === 489, geti(CELL_NAMED));
check('ready cell set', geti(CELL_READY) === 1, geti(CELL_READY));

// The catalogue's own answer, computed here from the bytes, so the module's
// query is graded against an independent count rather than against itself.
const magOf = i => dv.getInt16(DAT_BASE + HDR_SIZE + i * REC + R_MAG, true);
let expect6000 = 0;
for (let i = 0; i < 117931; i++) if (magOf(i) <= 6000) expect6000++;
check('the default cut selects what the bytes say it should',
  geti(CELL_VISIBLE) === expect6000, geti(CELL_VISIBLE) + ' vs ' + expect6000);

const first = geti(6291456);
check('the first selected index is Sol (record 0, magnitude -26.7)',
  first === 0 && magOf(first) === -26700, 'idx ' + first + ' mag ' + magOf(first));

let expect4000 = 0;
for (let i = 0; i < 117931; i++) if (magOf(i) <= 4000) expect4000++;
w.sm_set_mag_limit(4000);
check('a tighter cut reselects', geti(CELL_VISIBLE) === expect4000, geti(CELL_VISIBLE) + ' vs ' + expect4000);
w.sm_set_mag_limit(6000);

// every selected index must actually pass the cut
let violation = -1;
for (let k = 0; k < geti(CELL_VISIBLE); k++) {
  const idx = geti(6291456 + k * 4);
  if (magOf(idx) > 6000) { violation = idx; break; }
}
check('every selected star passes the cut', violation === -1,
  violation === -1 ? 'none' : ('index ' + violation + ' at mag ' + magOf(violation)));

// --- camera ----------------------------------------------------------
const y0 = geti(CAM + CAM_YAW);
w.sm_orbit(5000, 0);
check('orbit moves yaw', geti(CAM + CAM_YAW) === y0 + 5000, geti(CAM + CAM_YAW));
w.sm_orbit(0, 900000);
check('pitch clamps at +89000', geti(CAM + CAM_PITCH) === 89000, geti(CAM + CAM_PITCH));

check('zoom clamps at the narrow end', (w.sm_zoom(-1000), geti(CAM + CAM_FOV)) === 5, geti(CAM + CAM_FOV));
check('zoom clamps at the wide end', (w.sm_zoom(1000), geti(CAM + CAM_FOV)) === 120, geti(CAM + CAM_FOV));

const f0 = geti(CAM + CAM_FLAGS);
w.sm_toggle_labels();
check('toggling labels flips one bit', geti(CAM + CAM_FLAGS) === (f0 ^ 1), geti(CAM + CAM_FLAGS));

// --- the control -----------------------------------------------------
// Every arm above agreed, which is when to ask whether any of them COULD
// disagree. Corrupting the catalogue in memory and re-asking is a sabotage the
// module cannot hide: an arm that stays green was never reading the file.
console.log('\n[starmap-check] control: sabotaging the catalogue in memory');
let blind = 0;
const control = (name, wentRed) => {
  console.log((wentRed ? '  ok   ' : '  BLIND') + ' ' + name +
    (wentRed ? ' went red under sabotage' : ' STAYED GREEN under sabotage'));
  if (!wentRed) blind++;
};

const keepCount = dv.getInt32(DAT_BASE + 8, true);
dv.setInt32(DAT_BASE + 8, 5, true);
control('the star-count arm', (w.sm_load(dat.length), geti(CELL_STARS)) !== 117931);
dv.setInt32(DAT_BASE + 8, keepCount, true);

// push Sol past the cut; the selection must shrink by exactly one and stop
// starting at Sol
w.sm_load(dat.length);
const before = geti(CELL_VISIBLE);
const keepMag = dv.getInt16(DAT_BASE + HDR_SIZE + R_MAG, true);
dv.setInt16(DAT_BASE + HDR_SIZE + R_MAG, 11000, true);
w.sm_select();
control('the magnitude-query arm', geti(CELL_VISIBLE) === before - 1 && geti(6291456) !== 0);
dv.setInt16(DAT_BASE + HDR_SIZE + R_MAG, keepMag, true);
w.sm_load(dat.length);

console.log('');
if (blind > 0) console.log(`[starmap-check] ${blind} arm(s) CANNOT FAIL; their passes above are worthless`);
console.log(bad === 0 && blind === 0 ? '[starmap-check] PASS' : `[starmap-check] ${bad} failed, ${blind} blind`);
process.exit(bad === 0 && blind === 0 ? 0 : 1);
