// PRISM-7 stage 6a: the compile page's in-tab WAT assembler, graded byte for
// byte against wabt's wat2wasm. The assembler is loaded out of the page
// template (between its BEGIN and END markers), so the code graded is the code
// shipped. Subjects: a module using every opcode in the assembler's tables
// (wat2wasm --no-check, so no operands are needed), and the WAT that head's
// wasm plug emits for every program in bench/codex. The control assembles the
// opcode module with ONE opcode changed and must differ.
// Usage: node apps/prism/test-wat-assembler.mjs   Needs wat2wasm on PATH.
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const page = fs.readFileSync(join(repo, 'codex', 'plugs', 'wasm', 'page', 'prism.html'), 'utf8');
const a = page.indexOf('/* WAT-ASSEMBLER BEGIN'), b = page.indexOf('/* WAT-ASSEMBLER END */');
if (a < 0 || b < a) { console.log('REFUSE: no assembler block in the page'); process.exit(2); }
const block = page.slice(a, b);
const load = (src) => new Function(src + '\nreturn { watAssemble, WAT_OPS, WAT_MEM };')();
try { execFileSync('wat2wasm', ['--version']); } catch { console.log('REFUSE: wat2wasm is not on PATH'); process.exit(2); }

const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const work = mkdtempSync(join(tmpdir(), 'prism-wat-'));
const oracle = (wat, extra) => {
  const src = join(work, 'o.wat'), out = join(work, 'o.wasm');
  fs.writeFileSync(src, wat);
  execFileSync('wat2wasm', [...extra, '--enable-tail-call', src, '-o', out]);
  return fs.readFileSync(out);
};
const firstDiff = (x, y) => { for (let i = 0; i < Math.max(x.length, y.length); i++) if (x[i] !== y[i]) return i; return -1; };

try {
  const asm = load(block);
  const lines = ['(module', '  (memory 1)', '  (func $f'];
  for (const op of Object.keys(asm.WAT_OPS)) lines.push(`    (${op})`);
  for (const op of Object.keys(asm.WAT_MEM)) { lines.push(`    (${op})`); lines.push(`    (${op} offset=24)`); }
  lines.push('    (i32.const -2147483648) (i64.const -9223372036854775808) (i64.const 0x7fffffffffffffff) (f64.const -0.5) (f64.const 1e300) (f64.const inf) (f64.const -nan)');
  lines.push('  ))');
  const table = lines.join('\n');
  const want = oracle(table, ['--no-check']);
  const d = firstDiff(asm.watAssemble(table), want);
  ok(`every opcode in the tables (${Object.keys(asm.WAT_OPS).length + Object.keys(asm.WAT_MEM).length})`, d < 0, d < 0 ? 'byte-identical' : `first difference at byte ${d}`);

  const sab = load(block.replace("t['select'] = [0x1b];", "t['select'] = [0x1c];"));
  ok('control: one changed opcode is caught', firstDiff(sab.watAssemble(table), want) >= 0);

  const benches = fs.readdirSync(join(repo, 'bench', 'codex')).filter(f => f.endsWith('.codex')).sort();
  let same = 0; const bad = [];
  for (const f of benches) {
    const wat = join(work, f.replace(/\.codex$/, '.wat'));
    execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex', 'plugs', 'wasm', 'run.ps1'), '-Src', join(repo, 'bench', 'codex', f), '-Out', wat, '-Kernel', join(repo, 'seed', 'Codex.cdx')], { stdio: 'ignore' });
    if (!fs.existsSync(wat)) { bad.push(f + ': the wasm plug emitted nothing'); continue; }
    const text = fs.readFileSync(wat, 'utf8');
    let got;
    try { got = asm.watAssemble(text); } catch (e) { bad.push(f + ': ' + e.message); continue; }
    const dd = firstDiff(got, oracle(text, []));
    if (dd < 0) same++; else bad.push(f + ': first difference at byte ' + dd);
  }
  ok(`head's wasm plug output for bench/codex (${benches.length})`, same === benches.length && benches.length > 0, bad.join('; ') || `${same} byte-identical`);
} catch (e) {
  ok('ran to the end', false, String(e && e.message || e));
} finally {
  rmSync(work, { recursive: true, force: true });
}
const failed = arms.filter(x => !x).length;
console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
process.exit(failed ? 1 : 0);
