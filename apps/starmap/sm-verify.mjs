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
      CELL_NAMED = 272, CELL_DSOCOUNT = 276, CELL_DSOOFF = 280, CELL_NAMEOFF = 284,
      CELL_ERROR = 288, CELL_DSOVIS = 292, CELL_CONLINES = 296, CELL_CONCOUNT = 300;
const CON_BUF = 6438912;
const DSO_BUF = 6422528, DSO_REC = 80, D_MAG = 16, D_KIND = 18, D_NAMELEN = 22, D_NAME = 23;
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

u8.set(dat, DAT_BASE);
const keepDso = dv.getInt32(DAT_BASE + 20, true);
dv.setInt32(DAT_BASE + 20, keepDso + 100, true);
check('a header claiming more deep-sky records than delivered is refused',
  w.sm_load(dat.length) < 0 && geti(CELL_ERROR) === 5, 'err ' + geti(CELL_ERROR));
dv.setInt32(DAT_BASE + 20, keepDso, true);

const keepLines = dv.getInt32(DAT_BASE + 36, true);
dv.setInt32(DAT_BASE + 36, keepLines + 1, true);
check('a header claiming more constellation lines than the section holds is refused',
  w.sm_load(dat.length) < 0 && geti(CELL_ERROR) === 6, 'err ' + geti(CELL_ERROR));
dv.setInt32(DAT_BASE + 36, keepLines, true);

// --- the real catalogue ----------------------------------------------
u8.set(dat, DAT_BASE);
const n = w.sm_load(dat.length);
check('the real catalogue loads', n > 0 && geti(CELL_ERROR) === 0, n + ' stars, err ' + geti(CELL_ERROR));
check('117,931 stars', geti(CELL_STARS) === 117931, geti(CELL_STARS));
check('489 names', geti(CELL_NAMED) === 489, geti(CELL_NAMED));
// The colour ramp the page draws with is the module's, so it is graded here
// against the palette the page shipped, written out again independently
// (GAME-42): each threshold is checked on both sides.
const RAMP = [[-200, [155, 176, 255]], [0, [170, 191, 255]], [300, [202, 215, 255]],
              [600, [248, 247, 255]], [900, [255, 244, 234]], [1300, [255, 210, 161]],
              [1700, [255, 179, 119]], [Infinity, [255, 143, 92]]];
const oracle = bv => RAMP.find(([t]) => bv < t)[1];
const rgbOf = bv => { const c = w.sm_bv_rgb(bv) >>> 0; return [(c >> 16) & 255, (c >> 8) & 255, c & 255]; };
for (const bv of [-201, -200, -1, 0, 299, 300, 599, 600, 899, 900, 1299, 1300, 1699, 1700, 2500]) {
  const got = rgbOf(bv), want = oracle(bv);
  check(`B-V ${bv} colour`, got.join() === want.join(), `got ${got.join()} want ${want.join()}`);
}
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

// --- deep-sky objects --------------------------------------------------
// Counted here from the header and the 80-byte records, independently of the
// module's own pass.
const dsoN = dv.getInt32(DAT_BASE + 20, true), dsoOff = dv.getInt32(DAT_BASE + 24, true);
const dsoAt = j => DAT_BASE + dsoOff + j * DSO_REC;
const dsoMag = j => dv.getInt16(dsoAt(j) + D_MAG, true);
const dsoName = j => new TextDecoder().decode(u8.subarray(dsoAt(j) + D_NAME, dsoAt(j) + D_NAME + u8[dsoAt(j) + D_NAMELEN]));
const dsoUnder = lim => { let c = 0; for (let j = 0; j < dsoN; j++) if (dsoMag(j) <= lim) c++; return c; };
check('125 deep-sky records published', geti(CELL_DSOCOUNT) === 125 && dsoN === 125, geti(CELL_DSOCOUNT));
check('the first record is M1 Crab Nebula, a supernova remnant',
  dsoName(0) === 'M1 Crab Nebula' && u8[dsoAt(0) + D_KIND] === 7, dsoName(0) + ' kind ' + u8[dsoAt(0) + D_KIND]);
check('the default cut selects the deep-sky records the bytes say it should',
  geti(CELL_DSOVIS) === dsoUnder(6000), geti(CELL_DSOVIS) + ' vs ' + dsoUnder(6000));
w.sm_set_mag_limit(10000);
check('a wider cut reselects the deep-sky records', geti(CELL_DSOVIS) === dsoUnder(10000),
  geti(CELL_DSOVIS) + ' vs ' + dsoUnder(10000));
let dsoViolation = -1;
for (let k = 0; k < geti(CELL_DSOVIS); k++) {
  const j = geti(DSO_BUF + k * 4);
  if (j < 0 || j >= dsoN || dsoMag(j) > 10000) { dsoViolation = j; break; }
}
check('every selected deep-sky record exists and passes the cut', dsoViolation === -1,
  dsoViolation === -1 ? 'none' : ('record ' + dsoViolation));
w.sm_set_mag_limit(6000);
w.sm_select_obj(117931 + 124);
check('a deep-sky record is selectable after the stars', geti(CAM + 40) === 117931 + 124, geti(CAM + 40));
w.sm_select_obj(117931 + 125);
check('one past the last deep-sky record is refused', geti(CAM + 40) === 65535, geti(CAM + 40));

// --- constellation figures ---------------------------------------------
// The shipped header keeps the section offset at 28 and 0 at 32. Walked here
// independently of the module: 27 bytes of head, a 16-bit line count at 25,
// then 8 bytes per line.
const conOff = dv.getInt32(DAT_BASE + 32, true) > 0 ? dv.getInt32(DAT_BASE + 32, true) : dv.getInt32(DAT_BASE + 28, true);
const conWant = dv.getInt32(DAT_BASE + 36, true);
const segIds = [];
let cp = conOff, conN = 0;
while (segIds.length < conWant) {
  const lc = dv.getUint16(DAT_BASE + cp + 25, true);
  for (let k = 0; k < lc; k++) segIds.push([dv.getInt32(DAT_BASE + cp + 27 + k * 8, true), dv.getInt32(DAT_BASE + cp + 31 + k * 8, true)]);
  cp += 27 + lc * 8; conN++;
}
const idOf = i => dv.getInt32(DAT_BASE + HDR_SIZE + i * REC, true);
check('695 constellation lines published', geti(CELL_CONLINES) === 695 && conWant === 695, geti(CELL_CONLINES));
check('88 constellations walked', geti(CELL_CONCOUNT) === 88 && conN === 88, geti(CELL_CONCOUNT));
let segBad = -1;
for (let k = 0; k < geti(CELL_CONLINES); k++) {
  const a = geti(CON_BUF + k * 8), b = geti(CON_BUF + k * 8 + 4);
  if (idOf(a) !== segIds[k][0] || idOf(b) !== segIds[k][1]) { segBad = k; break; }
}
check('every line joins the two stars its HYG ids name', segBad === -1,
  segBad === -1 ? 'none' : ('line ' + segBad));

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

// push the last deep-sky record (Sgr A*, magnitude 0) past the cut; the
// deep-sky selection must shrink by exactly one
const dsoBefore = geti(CELL_DSOVIS);
const keepDsoMag = dv.getInt16(dsoAt(124) + D_MAG, true);
dv.setInt16(dsoAt(124) + D_MAG, 11000, true);
w.sm_select();
control('the deep-sky query arm', geti(CELL_DSOVIS) === dsoBefore - 1);
dv.setInt16(dsoAt(124) + D_MAG, keepDsoMag, true);
w.sm_load(dat.length);

// point the first line at an id no star carries; that line must drop
const firstFrom = DAT_BASE + conOff + 27;
const keepFrom = dv.getInt32(firstFrom, true);
dv.setInt32(firstFrom, 999999999, true);
w.sm_load(dat.length);
control('the constellation resolution arm', geti(CELL_CONLINES) === conWant - 1);
dv.setInt32(firstFrom, keepFrom, true);
w.sm_load(dat.length);

console.log('');
if (blind > 0) console.log(`[starmap-check] ${blind} arm(s) CANNOT FAIL; their passes above are worthless`);
console.log(bad === 0 && blind === 0 ? '[starmap-check] PASS' : `[starmap-check] ${bad} failed, ${blind} blind`);
process.exit(bad === 0 && blind === 0 ? 0 : 1);
