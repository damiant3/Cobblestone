// The HTML plug's dialogs (show-alert, show-confirm, show-prompt share _dlg), compiled from DialogArm.codex and run
// against a recording DOM under Node: a title and message holding markup must reach the page as text, never parsed.
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });
const work = mkdtempSync(join(tmpdir(), 'dialog-arm-'));
const title = '<img src=x onerror=alert(1)>';
const message = '<b>bold</b> & <script>x()</script>';
let failed = 0;
try {
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'DialogArm.codex'), '-Out', join(work, 'arm.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'arm.codex'), '-Out', join(work, 'arm.html')]);
  const html = readFileSync(join(work, 'arm.html'), 'utf8');
  if (!html.includes('function _dlg(')) throw new Error('the plug emitted no dialog runtime (stale html-plug.cdx?)');
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  const parsed = [];
  const made = [];
  const element = tag => {
    const el = { tag, children: [], style: {}, textContent: '', className: '', id: '', placeholder: '',
      appendChild(c) { this.children.push(c); return c; }, addEventListener() {}, setAttribute() {}, showModal() {}, close() {}, remove() {} };
    Object.defineProperty(el, 'innerHTML', { get: () => '', set: v => { if (String(v) !== '') parsed.push({ tag, v: String(v) }); } });
    made.push(el);
    return el;
  };
  const app = element('div');
  globalThis.window = { isSecureContext: true, addEventListener() {}, __DATA: {} };
  globalThis.document = { createElement: element, getElementById: () => app, body: element('body'), addEventListener() {},
    querySelector: () => null, querySelectorAll: () => [], documentElement: element('html'), head: element('head') };
  globalThis.localStorage = { setItem() {}, getItem: () => null };
  globalThis.requestAnimationFrame = () => 0;
  (0, eval)(scripts.join('\n'));
  await new Promise(r => setTimeout(r, 300));
  const dialogs = made.filter(e => e.tag === 'dialog');
  const heads = made.filter(e => e.tag === 'h3').map(e => e.textContent);
  const paras = made.filter(e => e.tag === 'p').map(e => e.textContent);
  const checks = [
    ['two dialogs were built (alert, prompt)', dialogs.length === 2],
    ['no dialog markup was parsed as HTML: ' + JSON.stringify(parsed.map(p => p.v)), parsed.filter(p => p.tag === 'dialog').length === 0],
    ['each heading holds the title as text', heads.length === 2 && heads.every(h => h === title)],
    ['each paragraph holds the message as text', paras.length === 2 && paras.every(p => p === message)],
  ];
  for (const [name, ok] of checks) { if (!ok) failed++; console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${name}`); }
} catch (e) { failed++; console.log('  FAIL  ' + (e && e.message || e)); }
finally { rmSync(work, { recursive: true, force: true }); }
console.log(failed === 0 ? 'dialog arm: all checks pass' : `dialog arm: ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
