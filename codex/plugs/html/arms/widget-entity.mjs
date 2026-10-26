// The HTML plug's widget labels and buttons (_wk WkLabel, WkButton), compiled from WidgetEntityArm.codex and run against a
// recording DOM under Node: a label holding markup and an & must reach the page as text, its listed entities decoded.
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });
const work = mkdtempSync(join(tmpdir(), 'widget-entity-arm-'));
const shown = '<img src=x onerror=alert(1)> × ✓ → &bogus; &#xD800;';
let failed = 0;
try {
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'WidgetEntityArm.codex'), '-Out', join(work, 'arm.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'arm.codex'), '-Out', join(work, 'arm.html')]);
  const html = readFileSync(join(work, 'arm.html'), 'utf8');
  if (!html.includes('function _wk(')) throw new Error('the plug emitted no widget runtime (stale html-plug.cdx?)');
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  const parsed = [];
  const made = [];
  const element = tag => {
    const el = { tag, children: [], style: {}, dataset: {}, classList: { add() {}, remove() {} }, textContent: '', className: '', id: '',
      appendChild(c) { this.children.push(c); return c; }, addEventListener() {}, setAttribute() {}, removeAttribute() {}, querySelector: () => null,
      querySelectorAll: () => [], getBoundingClientRect: () => ({ width: 0, height: 0, top: 0, left: 0 }) };
    Object.defineProperty(el, 'innerHTML', { get: () => '', set: v => { if (String(v) !== '') parsed.push({ tag, v: String(v) }); } });
    made.push(el);
    return el;
  };
  const app = element('div');
  globalThis.window = { isSecureContext: true, addEventListener() {}, __DATA: {}, innerWidth: 1024, innerHeight: 768, getComputedStyle: () => ({}) };
  globalThis.getComputedStyle = () => ({});
  globalThis.document = { createElement: element, getElementById: () => app, body: element('body'), addEventListener() {},
    querySelector: () => null, querySelectorAll: () => [], documentElement: element('html'), head: element('head') };
  globalThis.localStorage = { setItem() {}, getItem: () => null };
  globalThis.requestAnimationFrame = () => 0;
  (0, eval)(scripts.join('\n'));
  await new Promise(r => setTimeout(r, 300));
  const spans = made.filter(e => e.tag === 'span').map(e => e.textContent);
  const buttons = made.filter(e => e.tag === 'button').map(e => e.textContent);
  const checks = [
    ['no label or button was parsed as HTML: ' + JSON.stringify(parsed.filter(p => p.tag === 'span' || p.tag === 'button').map(p => p.v)), parsed.filter(p => p.tag === 'span' || p.tag === 'button').length === 0],
    ['the label shows the markup as text, its listed entities decoded and the others kept: ' + JSON.stringify(spans), spans.includes(shown)],
    ['the button does the same: ' + JSON.stringify(buttons), buttons.includes(shown)],
    ['a plain label is unchanged', spans.includes('plain')],
  ];
  for (const [name, ok] of checks) { if (!ok) failed++; console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${name}`); }
} catch (e) { failed++; console.log('  FAIL  ' + (e && e.message || e)); }
finally { rmSync(work, { recursive: true, force: true }); }
console.log(failed === 0 ? 'widget entity arm: all checks pass' : `widget entity arm: ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
