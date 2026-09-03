// Grade the C64 wasm module against what the real machine must do.
//
//   node apps/c64/c64-verify.mjs
//
// "It assembled" is not "it computes", so the oracle here is not a pixel and
// not a checksum: it is the KERNAL's own boot screen. Reaching
// "COMMODORE 64 BASIC V2" and "READY." from the reset vector means the 6502
// executed some millions of real ROM instructions, the processor port banked
// BASIC and the KERNAL in at the right moments, the zero page and stack
// behaved, and the screen editor wrote where the VIC says it is looking. No
// arrangement of a broken CPU prints that text.
//
// The control is a sabotage and it has to fire: with the KERNAL ROM zeroed the
// same run must NOT produce the banner. A grader whose control cannot go red
// is measuring nothing (L-CONSTRUCT), and this one is cheap to check because
// the sabotage is one memset away from the passing arm.

import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

// Every module this plug emits declares the two WASI file descriptors, the
// arcade's included. The emulator uses neither: it renders into linear memory
// and takes keys through a ring. So these are the arcade's throwing stubs
// rather than working ones -- if the module ever asks to read or write, that
// is a defect to surface, not a hole to fill quietly.
const IMPORTS = {
  wasi_snapshot_preview1: {
    fd_write: () => { throw new Error('fd_write: the c64 module must not write'); },
    fd_read:  () => { throw new Error('fd_read: the c64 module must not read'); },
  },
};

const HERE = dirname(fileURLToPath(import.meta.url));
// An explicit path lets the same grader run against a candidate module built
// somewhere else, which is how a plug fix is checked before it lands.
const WASM = process.argv[2]
  ? resolve(process.argv[2])
  : join(HERE, '..', 'landing', 'web', 'c64', 'c64.wasm');

// The machine's band, as apps/c64/Memory.codex declares it.
const KERNAL_ROM = 0xA12000;
const KERNAL_LEN = 0x2000;

const MAX_FRAMES = 600;   // ~10 s of emulated time; the KERNAL reaches READY in ~2
const SAMPLE_EVERY = 5;

// Screen codes are not PETSCII and not ASCII. Codes 32..63 coincide with
// ASCII; 1..26 are the letters, so 'A' is 1 rather than 65; 0 is '@'.
function screenText(read) {
  let out = '';
  for (let i = 0; i < 1000; i++) {
    const c = read(i) & 0x7F;
    if (c === 0) out += '@';
    else if (c >= 1 && c <= 26) out += String.fromCharCode(64 + c);
    else if (c >= 32 && c <= 63) out += String.fromCharCode(c);
    else out += ' ';
  }
  return out;
}

async function load() {
  let bytes;
  try {
    bytes = await readFile(WASM);
  } catch {
    console.log(`REFUSE: no module at ${WASM}`);
    console.log('  build it with: pwsh apps/c64/build-wasm.ps1');
    process.exit(2);
  }
  const { instance } = await WebAssembly.instantiate(bytes, IMPORTS);
  return instance.exports;
}

// Run from reset until the banner appears or the frame budget runs out.
// Answers the frame it appeared on, or -1.
function runUntilBanner(x, wanted) {
  x.c64_reset(0);
  for (let f = 1; f <= MAX_FRAMES; f++) {
    x.c64_frame(0);
    if (f % SAMPLE_EVERY) continue;
    const text = screenText(i => x.c64_screen(i));
    if (wanted.every(w => text.includes(w))) return f;
  }
  return -1;
}

const x = await load();

const results = [];
function check(name, ok, detail) {
  results.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
}

// -- the exports exist and answer the geometry the page assumes ----------
const W = x.c64_w(0), H = x.c64_h(0), FB = x.c64_fb(0);
check('geometry is 640x480', W === 640 && H === 480, `${W}x${H}`);
check('framebuffer is inside linear memory',
      FB > 0 && FB + W * H * 4 <= x.memory.buffer.byteLength,
      `fb=0x${FB.toString(16)} memory=${x.memory.buffer.byteLength} bytes`);

// -- the arm: the KERNAL boots -------------------------------------------
const BANNER = ['COMMODORE 64 BASIC', 'READY.'];
const bootFrame = runUntilBanner(x, BANNER);
check('the KERNAL reaches its banner and READY.', bootFrame > 0,
      bootFrame > 0 ? `frame ${bootFrame}` : `not within ${MAX_FRAMES} frames`);

if (bootFrame > 0) {
  const text = screenText(i => x.c64_screen(i));
  // The first line of a C64 boot screen is blank, so print the first line
  // that has something on it: that is the banner, and seeing it is what tells
  // a reader the substring match above was not a coincidence.
  for (let r = 0; r < 25; r++) {
    const line = text.slice(r * 40, r * 40 + 40).trimEnd();
    if (line.trim()) { console.log(`      screen: "${line}"`); break; }
  }
  check('the 6502 is running in ROM after boot', (x.c64_pc(0) >>> 0) >= 0xA000,
        `PC=$${(x.c64_pc(0) >>> 0).toString(16).toUpperCase()}`);
  check('the machine did not halt', x.c64_halted(0) === 0);
}

// -- the control: zero the KERNAL and the banner must NOT appear ----------
// If this passes, every arm above measured the ROM rather than the harness.
new Uint8Array(x.memory.buffer, KERNAL_ROM, KERNAL_LEN).fill(0);
// c64_reset reloads the ROMs from the source literals, so the sabotage has to
// land after it. Run the frames by hand rather than through runUntilBanner.
x.c64_reset(0);
new Uint8Array(x.memory.buffer, KERNAL_ROM, KERNAL_LEN).fill(0);
let sabotaged = false;
for (let f = 1; f <= MAX_FRAMES; f++) {
  x.c64_frame(0);
  if (f % SAMPLE_EVERY) continue;
  const text = screenText(i => x.c64_screen(i));
  if (BANNER.every(w => text.includes(w))) { sabotaged = true; break; }
}
check('CONTROL: with the KERNAL zeroed the banner does not appear', !sabotaged,
      sabotaged ? 'the banner appeared anyway, so the arm above is not reading the ROM'
                : 'control fires');

const failed = results.filter(r => !r.ok);
console.log(`\n${results.length - failed.length} of ${results.length} checks passed.`);
process.exit(failed.length ? 1 : 0);
