// The HTML plug's WebCrypto primitives (crypto-hmac-then, crypto-hmac-verify-then, crypto-seal-then,
// crypto-open-then, crypto-random-hex, crypto-ready), compiled from CryptoArm.codex and run under Node's
// WebCrypto (the W3C API the browser implements). HMAC is graded against RFC 4231 test case 1.
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });
const work = mkdtempSync(join(tmpdir(), 'crypto-arm-'));
let failed = 0;
try {
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'CryptoArm.codex'), '-Out', join(work, 'arm.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'arm.codex'), '-Out', join(work, 'arm.html')]);
  const html = readFileSync(join(work, 'arm.html'), 'utf8');
  if (!html.includes('function crypto_hmac_then(')) throw new Error('the plug emitted no crypto runtime (stale html-plug.cdx?)');
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  const store = {};
  const node = () => new Proxy(function () {}, { get: (t, k) => k === Symbol.toPrimitive ? () => '' : node(), apply: () => node(), set: () => true });
  globalThis.window = { isSecureContext: true, addEventListener() {}, __DATA: {} };
  globalThis.document = node();
  globalThis.localStorage = { setItem: (k, v) => { store[k] = String(v); }, getItem: k => store[k] ?? null };
  globalThis.requestAnimationFrame = () => 0;
  (0, eval)(scripts.join('\n'));
  await new Promise(r => setTimeout(r, 500));
  const checks = [
    ['hmac RFC 4231 case 1', store.hmac === 'b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7'],
    ['verify answers 1 for the right tag', store.verify1 === '1'],
    ['verify answers 0 for other text', store.verify0 === '0'],
    ['seal gives cipher hex 66 and tag hex 32', store.seal === '66 32'],
    ['open restores the sealed text', store.open === '{"ok":true,"name":"Lord British"}'],
    ['open refuses a tampered tag with !', (store.tamper || '').startsWith('!')],
  ];
  for (const [name, ok] of checks) { if (!ok) failed++; console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${name}`); }
} catch (e) { failed++; console.log('  FAIL  ' + (e && e.message || e)); }
finally { rmSync(work, { recursive: true, force: true }); }
console.log(failed === 0 ? 'crypto arm: all checks pass' : `crypto arm: ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
