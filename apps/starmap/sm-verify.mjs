// Drive web/starmap.wasm with the same import object the page supplies, and
// grade what the module actually did. Every arm here is one that can go red on
// its own: a wide coordinate, a NEGATIVE coordinate (peek-32 zero-extends, so
// the sign correction is the arm that catches it), a decoded label (char-code-at
// answers CCE, so a missing to-unicode shows up as mojibake and not as a crash),
// the pitch clamp, and the magnitude filter moving the visible count.
//
// RUN BY HAND, like the other forty-four *-verify.mjs graders here: no build
// script invokes any of them, and apps/landing/build.ps1 must not start node.
// Its own gate on this module is `wasmtime starmap.wasm`, which runs the entry
// act and reads the counts it prints; wasmtime is already required by the wasm
// plug, so the build depends on nothing the module's own build did not. What
// that gate cannot do is read linear memory back, which is every arm below.
//
//   node apps/starmap/sm-verify.mjs
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const bytes = readFileSync(join(here, 'web', 'starmap.wasm'));

const CELL_STARS = 256, CELL_LABELS = 260, CELL_VISIBLE = 264, CELL_READY = 268;
const CAM = 352, CAM_FOCUS_X = 0, CAM_YAW = 24, CAM_PITCH = 28, CAM_MAG = 48, CAM_FLAGS = 60;
const STAR_TAB = 1048576, STAR_STRIDE = 48;
const S_X = 0, S_Y = 8, S_Z = 16, S_ID = 32, S_LABEL = 40;
const LABEL_BLOB = 1114112, LABEL_STRIDE = 80, LABEL_CAP = 76;
const ABSENT = 65535, WIDE = 1000000;

let i32, u8, out = '';
const geti = a => i32[a >> 2];
const wide = a => i32[a >> 2] + i32[(a + 4) >> 2] * WIDE;

const wasi = {
  fd_write(fd, iov, n, ret) {
    let w = 0;
    for (let k = 0; k < n; k++) {
      const p = geti(iov + k * 8), len = geti(iov + k * 8 + 4);
      out += new TextDecoder().decode(u8.subarray(p, p + len)); w += len;
    }
    i32[ret >> 2] = w; return 0;
  },
  fd_read: () => 0,
};

const inst = (await WebAssembly.instantiate(bytes, { wasi_snapshot_preview1: wasi })).instance;
const w = inst.exports;
i32 = new Int32Array(w.memory.buffer);
u8 = new Uint8Array(w.memory.buffer);
w._start();

const label = slot => {
  if (slot === ABSENT) return '';
  const b = LABEL_BLOB + slot * LABEL_STRIDE;
  const n = Math.min(geti(b), LABEL_CAP);
  return n > 0 ? new TextDecoder().decode(u8.subarray(b + 4, b + 4 + n)) : '';
};
const rows = () => {
  const n = geti(CELL_STARS), r = [];
  for (let i = 0; i < n; i++) {
    const a = STAR_TAB + i * STAR_STRIDE;
    r.push({ id: geti(a + S_ID), x: wide(a + S_X), y: wide(a + S_Y), z: wide(a + S_Z),
             name: label(geti(a + S_LABEL)) });
  }
  return r;
};

let bad = 0;
// The value is printed on a PASS too. A bare colour cannot tell a reader
// whether the arm reached its subject, and the number is what says so.
const check = (name, ok, saw) => {
  console.log((ok ? '  ok   ' : '  FAIL ') + name.padEnd(52) + ' ' + saw);
  if (!ok) bad++;
};

console.log('[starmap-check] module said: ' + out.trim());

check('ready cell set', geti(CELL_READY) === 1, geti(CELL_READY));
const n = geti(CELL_STARS);
check('80 objects in the table', n === 80, n);
check('labels written', geti(CELL_LABELS) > 0, geti(CELL_LABELS));
check('visible after init', geti(CELL_VISIBLE) > 0 && geti(CELL_VISIBLE) <= n, geti(CELL_VISIBLE));

const r = rows();
const sol = r.find(s => s.id === 1);
check('Sol is named "Sol", so CCE decoded to Unicode', sol && sol.name === 'Sol', sol && JSON.stringify(sol.name));

// Sirius: the catalog literal is (-1810) (-909) (-400). All three are NEGATIVE
// and small, which is exactly what a zero-extending read gets wrong.
const sirius = r.find(s => s.id === 2);
check('Sirius keeps its negative coordinates',
  sirius && sirius.x === -1810 && sirius.y === -909 && sirius.z === -400,
  sirius && [sirius.x, sirius.y, sirius.z].join(','));

// The widest literal in the catalog is 320,000,000,000, past a single word.
const widest = r.reduce((m, s) => Math.max(m, Math.abs(s.x), Math.abs(s.y), Math.abs(s.z)), 0);
check('a coordinate past 2^31 survives the wide split', widest > 2147483647, widest);

const before = geti(CAM + CAM_YAW);
w.sm_orbit(5000, 0);
check('orbit moves yaw', geti(CAM + CAM_YAW) === before + 5000, geti(CAM + CAM_YAW));

w.sm_orbit(0, 900000);
check('pitch clamps at +89000', geti(CAM + CAM_PITCH) === 89000, geti(CAM + CAM_PITCH));
w.sm_orbit(0, -9000000);
check('pitch clamps at -89000', geti(CAM + CAM_PITCH) === -89000, geti(CAM + CAM_PITCH));

w.sm_move_right(-50000);
check('a negative focus reads back negative', geti(CAM + CAM_FOCUS_X) < 0, geti(CAM + CAM_FOCUS_X));

const wideOpen = (w.sm_set_mag_limit(100000), geti(CELL_VISIBLE));
const shut = (w.sm_set_mag_limit(-100000), geti(CELL_VISIBLE));
check('the magnitude limit filters', wideOpen > shut && shut === 0, wideOpen + ' then ' + shut);

const f0 = geti(CAM + CAM_FLAGS);
w.sm_toggle_labels();
check('toggling labels flips one bit', geti(CAM + CAM_FLAGS) === (f0 ^ 1), geti(CAM + CAM_FLAGS));

const target = r.find(s => s.id === 3);
w.sm_fly_to(3);
check('fly-to moves the focus onto the target',
  wide(CAM + CAM_FOCUS_X) === target.x, wide(CAM + CAM_FOCUS_X) + ' vs ' + target.x);

// --- the control -----------------------------------------------------
// Every arm above agreed, which is exactly when to ask whether any of them
// COULD disagree. Corrupting the table in memory and re-reading is a sabotage
// the module cannot hide: an arm that stays green here was never reading the
// table, and its pass above meant nothing (L-VACUOUS).
console.log('\n[starmap-check] control: sabotaging the table in memory');
let blind = 0;
const control = (name, wentRed) => {
  console.log((wentRed ? '  ok   ' : '  BLIND') + ' ' + name +
    (wentRed ? ' went red under sabotage' : ' STAYED GREEN under sabotage'));
  if (!wentRed) blind++;
};

const solAddr = STAR_TAB + 0 * STAR_STRIDE;
const sirAddr = STAR_TAB + 1 * STAR_STRIDE;

// low word of Sirius's x, the half the sign correction reads
const keepX = i32[(sirAddr + S_X) >> 2];
i32[(sirAddr + S_X) >> 2] = 12345;
control('the negative-coordinate arm', rows().find(s => s.id === 2).x !== -1810);
i32[(sirAddr + S_X) >> 2] = keepX;

// the HIGH word only, which is the half a single-word read would never touch
const keepHi = i32[(sirAddr + S_X + 4) >> 2];
i32[(sirAddr + S_X + 4) >> 2] = 7;
control('the wide-split arm (high word alone)', rows().find(s => s.id === 2).x !== -1810);
i32[(sirAddr + S_X + 4) >> 2] = keepHi;

// first byte of Sol's name in the label blob
const solSlot = geti(solAddr + S_LABEL);
const nameAddr = LABEL_BLOB + solSlot * LABEL_STRIDE + 4;
const keepName = u8[nameAddr];
u8[nameAddr] = 90;
control('the label-decode arm', label(solSlot) !== 'Sol');
u8[nameAddr] = keepName;

console.log('');
if (blind > 0) console.log(`[starmap-check] ${blind} arm(s) CANNOT FAIL; their passes above are worthless`);
console.log(bad === 0 && blind === 0 ? '[starmap-check] PASS'
  : `[starmap-check] ${bad} failed, ${blind} blind`);
process.exit(bad === 0 && blind === 0 ? 0 : 1);
