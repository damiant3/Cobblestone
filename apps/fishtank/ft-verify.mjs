// Grade the fishtank module the page ships: drive it through the page's calls
// and check the fish and particle arrays the page reads at fixed addresses.
// The shipped module sat behind head for weeks with nothing running either.
// Given a second module, also require identical state at every step.
//
//   node apps/fishtank/ft-verify.mjs [fishtank.wasm] [reference.wasm]
import { readFile } from 'node:fs/promises';
const WASM = process.argv[2] ?? 'apps/fishtank/web/fishtank.wasm';
const REF = process.argv[3];

// fishtank-wasm.html's layout, and FishTankWasm.codex's tank and spawn table.
const FISH_BASE = 0x20000, PARTICLE_BASE = 0x30000, REGION_END = 270336 + 4096;
const F_X = 0x004, F_Y = 0x404, F_Z = 0x804, F_SPECIES = 0x2004;
const MAX_FISH = 256, MAX_PARTICLES = 512, SPECIES = 8;
const SPAWNED = 18 + 5 + 12 + 6 + 2 + 3 + 2 + 4;
const LO = [-5000, -4000, -3000], HI = [5000, 3000, 3000];

async function load(path) {
  const m = await WebAssembly.instantiate(await readFile(path), {
    wasi_snapshot_preview1: { fd_write: () => 0, fd_read: () => 0 },
    env: { blit_framebuf: () => {}, on_key: () => 0 },
  });
  return m.instance.exports;
}
const words = (x) => new Int32Array(x.memory.buffer);
const ri = (x, a) => words(x)[a >> 2];

const ft = await load(WASM);
const ref = REF ? await load(REF) : null;
const steps = [['init_aquarium', []]];
for (let t = 1; t <= 600; t++) {
  steps.push(['tick', []]);
  if (t === 100) steps.push(['spawn_food', []]);
  if (t === 200) steps.push(['scatter', []]);
  if (t === 300) steps.push(['toggle_light', []]);
  if (t === 400) steps.push(['spawn_one_fish', [3]]);
}

const fails = [];
const fail = (s) => { if (fails.length < 8) fails.push(s); };
let fishMoved = false, x0 = null;
for (let s = 0; s < steps.length; s++) {
  const [name, args] = steps[s];
  try { ft[name](...args); } catch (e) { fail(`step ${s} ${name} trapped: ${e.message}`); break; }
  if (ref) {
    ref[name](...args);
    const a = words(ft), b = words(ref);
    for (let w = FISH_BASE >> 2; w < REGION_END >> 2; w++)
      if (a[w] !== b[w]) { fail(`step ${s} ${name}: word 0x${(w * 4).toString(16)} ${a[w]} vs reference ${b[w]}`); break; }
  }
  const n = ri(ft, FISH_BASE), want = SPAWNED + (steps.slice(0, s + 1).some(([k]) => k === 'spawn_one_fish') ? 1 : 0);
  if (n !== want) fail(`step ${s} ${name}: ${n} fish, want ${want}`);
  for (let i = 0; i < Math.min(n, MAX_FISH); i++) {
    const p = [F_X, F_Y, F_Z].map((f) => ri(ft, FISH_BASE + f + i * 4)), sp = ri(ft, FISH_BASE + F_SPECIES + i * 4);
    if (sp < 0 || sp >= SPECIES) fail(`step ${s}: fish ${i} species ${sp}`);
    if (p.some((v, k) => v < LO[k] || v > HI[k])) fail(`step ${s}: fish ${i} at ${p} outside the tank`);
  }
  const parts = [0, 4, 8].reduce((a, o) => a + ri(ft, PARTICLE_BASE + o), 0);
  if (parts < 0 || parts > MAX_PARTICLES) fail(`step ${s}: ${parts} particles`);
  if (x0 === null) x0 = ri(ft, FISH_BASE + F_X); else if (ri(ft, FISH_BASE + F_X) !== x0) fishMoved = true;
}
if (!fishMoved) fail('fish 0 never moved in 600 ticks');
for (const f of fails) console.log('  FAIL ', f);
console.log(fails.length ? `FAIL: ${WASM}` : `PASS: ${WASM}, ${steps.length} steps${ref ? ', identical to ' + REF : ''}`);
process.exit(fails.length ? 1 : 0);
