// Validate EVERY WGSL shader gpushow ships against FIREFOX's compiler, offline.
//
// WHY THIS EXISTS BESIDE validate-all.mjs, WHICH IS ALREADY CAREFUL. That one
// drives the pages so the population is what ships rather than what happens to
// be a file -- the right denominator, and it says so. But it measures against
// ONE compiler: Chrome's Tint, through headless Chrome. Firefox uses naga, and
// TINT IS THE PERMISSIVE ONE, so a shader that passes there is untested rather
// than correct. The same reasoning validate-all applies to the population
// applies to the ORACLE: green on 100% of shaders and 50% of browsers.
//
// Measured the day this was written, over the tree at Update 55: 19 of 44
// shaders passed naga. The gate was green.
//
// It needs no browser, no GPU and no adapter -- naga is a Rust binary and each
// file takes milliseconds, so this can run where headless Chrome cannot.
//
//   cargo install naga-cli      (once)
//   node validate-naga.mjs [--only <substr>] [--naga <path>]
//
// Exit 0 = every shader validated, 1 = at least one rejected, 2 = no naga.
import { spawnSync } from 'node:child_process';
import { readFileSync, readdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const appRoot = join(here, '..');
const args = process.argv.slice(2);
const only = args.includes('--only') ? args[args.indexOf('--only') + 1] : null;
const naga = args.includes('--naga')
  ? args[args.indexOf('--naga') + 1]
  : join(process.env.HOME || '', '.cargo', 'bin', 'naga');

if (spawnSync(naga, ['--version']).error) {
  console.error(`no naga at ${naga}. Install with: cargo install naga-cli`);
  console.error('or pass --naga <path>.');
  process.exit(2);
}

// THE POPULATION IS BOTH HALVES, for validate-all's own reason: a page creates
// two modules and only the compute one is a file. The render shader is a
// `const renderCode = ` template literal in the page, so it is read from there.
const shaders = [];
const kdir = join(appRoot, 'kernels');
for (const f of readdirSync(kdir).filter((f) => f.endsWith('.wgsl')).sort()) {
  shaders.push({ label: `kernels/${f}`, code: readFileSync(join(kdir, f), 'utf8') });
}
const wdir = join(appRoot, 'web');
for (const f of readdirSync(wdir).filter((f) => f.endsWith('.html')).sort()) {
  const html = readFileSync(join(wdir, f), 'utf8');
  const m = html.match(/const\s+renderCode\s*=\s*`([\s\S]*?)`\s*;/);
  if (!m) continue;
  // A RENDER SHADER IS A TEMPLATE LITERAL AND MAY INTERPOLATE. `${W}` is a
  // canvas dimension the page computes, so the extracted TEXT is not WGSL until
  // something stands in for it. A placeholder integer keeps the shape valid and
  // the type correct, which is what naga is being asked about; it does NOT
  // validate the real constant, and a shader whose validity depends on the
  // actual value is out of this gate's reach. Said here rather than discovered:
  // reporting these as failures without substituting would have blamed 33
  // shaders for a defect in this file.
  const code = m[1].replace(/\$\{[^}]*\}/g, '256');
  shaders.push({ label: `web/${f} renderCode`, code, interpolated: m[1] !== code });
}

const tmp = mkdtempSync(join(tmpdir(), 'naga-'));
let pass = 0;
const failures = [];
for (const s of shaders) {
  if (only && !s.label.includes(only)) continue;
  const path = join(tmp, basename(s.label).replace(/[^\w.-]/g, '_') + '.wgsl');
  writeFileSync(path, s.code);
  const r = spawnSync(naga, [path], { encoding: 'utf8' });
  if (r.status === 0) {
    pass++;
  } else {
    const why = (r.stderr || r.stdout).split('\n')
      .map((l) => l.replace(/\x1b\[[0-9;]*m/g, ''))
      .find((l) => l.trim().startsWith('= ')) || '(see naga output)';
    failures.push([s.label, why.trim().replace(/^=\s*/, '')]);
  }
}
rmSync(tmp, { recursive: true, force: true });

for (const [label, why] of failures) console.log(`FAIL  ${label}\n        ${why}`);
console.log(`\nnaga: ${pass} passed, ${failures.length} failed, of ${pass + failures.length} shaders`);
process.exit(failures.length ? 1 : 0);
