// Grade the mathbook wasm module against arithmetic, not against itself.
//
//   node apps/mathbook/mb-verify.mjs [path/to/mathbook.wasm]
//
// Every expected value below is a fact about mathematics rather than a
// property of this implementation, so the module cannot be "fixed" by
// re-recording what it currently says.
//
// The corpus is chosen so each row can DISAGREE with a plausible defect. That
// is the whole design, and it is not decoration: stage 1 first shipped through
// `fold-constants`, which folds only the top node, and `2+2` answers 4 under
// both that and the real `simplify`. Only a NESTED expression separates them,
// which is why `1+2*3` carries three named rivals below rather than one
// expected string. An operand pair whose two candidate answers agree is
// decoration.

import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const WASM = process.argv[2]
  ? resolve(process.argv[2])
  : join(HERE, '..', 'landing', 'web', 'mathbook', 'mathbook.wasm');

let mod;
try {
  mod = new WebAssembly.Module(await readFile(WASM));
} catch (e) {
  console.log(`REFUSE: no usable module at ${WASM} (${e.message})`);
  console.log('  build it with: pwsh apps/mathbook/build-wasm.ps1');
  process.exit(2);
}

// The module is a WASI program: the expression arrives on fd_read and the
// answer leaves on fd_write, both UTF-8, and _start runs once. A fresh
// instance per evaluation is the contract, not a workaround -- there is no
// collector in a wasm module, so each evaluation gets a clean heap.
function evaluate(line) {
  const input = new TextEncoder().encode(line + '\n');
  let mem = null, pos = 0;
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
    fd_read(fd, iovs, n, outp) {
      const v = new DataView(mem.buffer); let t = 0;
      for (let i = 0; i < n; i++) {
        const p = v.getUint32(iovs + i * 8, true), l = v.getUint32(iovs + i * 8 + 4, true);
        const dst = new Uint8Array(mem.buffer, p, l);
        let k = 0; while (k < l && pos < input.length) dst[k++] = input[pos++];
        t += k; if (k < l) break;
      }
      v.setUint32(outp, t, true); return 0;
    }
  }};
  const inst = new WebAssembly.Instance(mod, imports);
  mem = inst.exports.memory;
  inst.exports._start();
  let total = 0; for (const c of chunks) total += c.length;
  const all = new Uint8Array(total); let off = 0;
  for (const c of chunks) { all.set(c, off); off += c.length; }
  return new TextDecoder().decode(all).trim();
}

// expr, expected, why this row can fail
const CASES = [
  ['2+2',      '4',      'integer addition'],
  ['7-9',      '-2',     'a negative result, which a magnitude-only printer loses'],
  ['2^10',     '1024',   'exponentiation, not repeated addition'],
  ['(1+2)*3',  '9',      'parentheses beat precedence'],
  ['1/2+1/3',  '5/6',    'EXACT rationals; 0.833... would mean it went to floating point'],
  ['x+x',      '2 * x',  'symbolic collection, not evaluation'],
  ['x*1+0',    'x',      'identity folding'],
];

// The nested case, with its rivals named. Each rival is a real defect's
// signature rather than an arbitrary wrong string.
const NESTED = {
  expr: '1+2*3',
  expected: '7',
  rivals: {
    '1 + 2 * 3': 'the simplifier folded only the TOP node (fold-constants, not simplify)',
    '9':         'precedence ignored, evaluated left to right',
  },
};

const results = [];
function check(name, ok, detail) {
  results.push(ok);
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
}

for (const [expr, expected, why] of CASES) {
  const got = evaluate(expr);
  check(`${expr} = ${expected}`, got === expected, got === expected ? why : `got "${got}"`);
}

const gotNested = evaluate(NESTED.expr);
check(`${NESTED.expr} = ${NESTED.expected}`, gotNested === NESTED.expected,
      gotNested === NESTED.expected ? 'nested simplification' : `got "${gotNested}"`);
for (const [rival, meaning] of Object.entries(NESTED.rivals)) {
  check(`${NESTED.expr} is not "${rival}"`, gotNested !== rival, meaning);
}

// Bad input must be refused rather than answered. A CAS that returns something
// plausible for nonsense is the shape that ships a wrong answer as data.
const bad = evaluate('@@bogus');
check('nonsense is refused, not answered', bad.startsWith('parse error'), `got "${bad}"`);

// CONTROL, and it has to be one that can actually go red. "2+2 is not 5" is
// not a control: it is true whatever the module does, including a module that
// returns one constant forever, and an instrument that cannot fail is not
// evidence (L-FALSIF). What CAN fail is the claim that the answer tracks the
// input at all -- if fd_read never delivered, or the page held a stale buffer,
// every row above would be comparing against the same string and this line is
// the only one that would notice.
const varyA = evaluate('2+2'), varyB = evaluate('3+3');
check('CONTROL: the answer tracks the input',
      varyA !== varyB, `2+2 -> "${varyA}", 3+3 -> "${varyB}"`);

// The other half of the control is a SABOTAGE, and it does not live here
// because this grader loads a built module and cannot rebuild one. It is run
// against the source: point `mb-eval` at `fold-constants` instead of
// `simplify` and the nested row must go red while the flat rows stay green.
// Measured 2026-09-02 and it moved FIVE of the twelve rows: the nested row,
// its named rival, and the parenthesised, rational and symbolic rows, while
// 2+2, 7-9, 2^10 and the identity row stayed green. That split is the point --
// a sabotage that reddens everything says only that the module broke, and one
// that reddens nothing says the corpus never reached the branch. If a later
// change makes it stop moving any row, the repair is a different corpus,
// never a softer assertion.

const failed = results.filter(r => !r).length;
console.log(`\n${results.length - failed} of ${results.length} checks passed.`);
process.exit(failed ? 1 : 0);
