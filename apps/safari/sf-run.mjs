// Run the safari intake module and print what it says.
// The module is a WASI program driven by _start; output leaves on fd_write.
// Shim copied from apps/mathbook/mb-verify.mjs so this measures the module
// rather than a driving convention I invented.
import { readFile } from 'node:fs/promises';

const WASM = process.argv[2];
const bytes = await readFile(WASM);
console.log(`module: ${bytes.length} bytes`);

let mod;
try {
  mod = new WebAssembly.Module(bytes);
} catch (e) {
  console.log(`REFUSE: module did not validate: ${e.message}`);
  process.exit(2);
}

let mem = null;
const chunks = [];
const imports = { wasi_snapshot_preview1: {
  fd_write(fd, iovs, n, outp) {
    const v = new DataView(mem.buffer); let t = 0;
    for (let i = 0; i < n; i++) {
      const p = v.getUint32(iovs + i * 8, true), l = v.getUint32(iovs + i * 8 + 4, true);
      chunks.push(new Uint8Array(mem.buffer.slice(p, p + l))); t += l;
    }
    v.setUint32(outp, t, true); return 0;
  },
  fd_read(fd, iovs, n, outp) { new DataView(mem.buffer).setUint32(outp, 0, true); return 0; }
}};

const t0 = Date.now();
let inst;
try {
  inst = new WebAssembly.Instance(mod, imports);
} catch (e) {
  console.log(`FAIL at instantiation: ${e.message}`);
  process.exit(3);
}
mem = inst.exports.memory;
console.log(`linear memory: ${mem.buffer.byteLength} bytes`);

try {
  inst.exports._start();
} catch (e) {
  console.log(`TRAPPED in _start: ${e.message}`);
  let total = 0; for (const c of chunks) total += c.length;
  const all = new Uint8Array(total); let off = 0;
  for (const c of chunks) { all.set(c, off); off += c.length; }
  const partial = new TextDecoder().decode(all).trim();
  if (partial) console.log(`output before the trap:\n${partial}`);
  process.exit(4);
}

let total = 0; for (const c of chunks) total += c.length;
const all = new Uint8Array(total); let off = 0;
for (const c of chunks) { all.set(c, off); off += c.length; }
console.log(`ran in ${Date.now() - t0} ms`);
console.log('--- module output ---');
console.log(new TextDecoder().decode(all).trim());
