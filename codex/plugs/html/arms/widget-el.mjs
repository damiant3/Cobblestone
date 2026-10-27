// The HTML plug's el: widget tag (_wkCustom), compiled from WidgetElArm.codex and run against a recording DOM under
// Node: an element is made by createElement with its attributes set one by one, event attributes and javascript: URLs
// are dropped, a script tag becomes a div, and no text reaches innerHTML.
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });
const work = mkdtempSync(join(tmpdir(), 'widget-el-arm-'));
let failed = 0;
try {
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'WidgetElArm.codex'), '-Out', join(work, 'arm.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'arm.codex'), '-Out', join(work, 'arm.html')]);
  const html = readFileSync(join(work, 'arm.html'), 'utf8');
  if (!html.includes("tag.indexOf('el:')===0")) throw new Error('the plug emitted no el: tag (stale html-plug.cdx?)');
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  const parsed = [];
  const made = [];
  const element = tag => {
    const el = { tag, children: [], style: {}, dataset: {}, attrs: {}, classList: { add() {}, remove() {} }, textContent: '', className: '', id: '',
      appendChild(c) { this.children.push(c); return c; }, addEventListener() {}, setAttribute(k, v) { this.attrs[k] = String(v); }, removeAttribute() {},
      querySelector: () => null, querySelectorAll: () => [], getBoundingClientRect: () => ({ width: 0, height: 0, top: 0, left: 0 }) };
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
  const a = made.find(e => e.tag === 'a' && e.className === 'link');
  const img = made.find(e => e.tag === 'img' && e.className === 'pic');
  const s = made.find(e => e.className === 's');
  const text = el => el ? el.children.map(c => c.textContent).join('') : '';
  const checks = [
    ['the a element exists: ' + JSON.stringify(a && a.attrs), !!a],
    ['its title is set, its entity decoded', !!a && a.attrs.title === 'kept & decoded' && a.attrs['data-x'] === '1'],
    ['its javascript: href is dropped', !!a && !('href' in a.attrs)],
    ['its event attributes are dropped, whatever their case', !!a && !Object.keys(a.attrs).some(k => k.toLowerCase().startsWith('on'))],
    ['its child is the markup as text: ' + JSON.stringify(text(a)), text(a) === '<b onclick=alert(1)>bold?</b>'],
    ['a script tag is built as a div: ' + (s && s.tag), !!s && s.tag === 'div'],
    ['an img keeps its src and alt: ' + JSON.stringify(img && img.attrs), !!img && img.attrs.src === 'img/a.jpg' && img.attrs.alt === 'an apple'],
    ['nothing reached innerHTML: ' + JSON.stringify(parsed.map(p => p.v)), parsed.length === 0],
  ];
  for (const [name, ok] of checks) { if (!ok) failed++; console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${name}`); }
} catch (e) { failed++; console.log('  FAIL  ' + (e && e.message || e)); }
finally { rmSync(work, { recursive: true, force: true }); }
console.log(failed === 0 ? 'widget el arm: all checks pass' : `widget el arm: ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
