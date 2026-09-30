// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: a picked
// checkpoint bound by CheckpointLayout in a compiled Codex page,
// codex/plugs/html/arms/LayoutArm.codex, in headless Edge. The two header
// fixtures of codex/test/apps/diffusion-layout (SDXL and SD1.5) are picked in
// turn, and every line the page reports, its own family's binding and the other
// family's as the control, must equal that test's .expected line for line: the
// native answer is the oracle.
// Usage: node codex/plugs/html/arms/layout.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

const expected = readFileSync(join(repo, 'codex', 'test', 'apps', 'diffusion-layout.expected'), 'utf8').split(/\r?\n/);
const runs = [
  { family: 'sdxl', file: join(repo, 'codex', 'test', 'gpu-files', 'sdxl-dreamshaper-header.safetensors'), want: expected.filter(l => l.startsWith('sdxl')) },
  { family: 'sd15', file: join(repo, 'codex', 'test', 'gpu-files', 'sd15-realvis-header.safetensors'), want: expected.filter(l => l.startsWith('sd15')) },
];
// The page reports its family's lines, then its control; the .expected holds
// each family's lines and then both controls, so reorder the want to match.
for (const r of runs) { const ctl = r.want.filter(l => l.includes(' as ')); r.want = r.want.filter(l => !l.includes(' as ')).concat(ctl); }

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'layout-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'LayoutArm.codex'), '-Out', join(work, 'la.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'la.codex'), '-Out', join(work, 'la.html')]);
  const html = readFileSync(join(work, 'la.html'), 'utf8');
  const page = (family) => html.replace('<script>', `<script>window.__DATA=${JSON.stringify({ family })};</script><script>`);
  let current = runs[0];
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page(current.family)); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map(); const pageErrors = [];
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Runtime.exceptionThrown') pageErrors.push(String(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text).split('\n').slice(0, 4).join(' / '));
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [current.file], backendNodeId: m.params.backendNodeId });
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  for (const r of runs) {
    current = r;
    await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
    for (let i = 0; i < 240; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.armed) === 1')) break; } catch {} }
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    for (let i = 0; i < 480; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
    const got = String(await evalIn('typeof _st !== "undefined" ? String(_st.lines || "") : ""')).split('\n').filter(Boolean);
    const diff = r.want.map((w, i) => w === got[i] ? null : `line ${i + 1}: got [${got[i]}] want [${w}]`).filter(Boolean);
    ok(`${r.family}: every line equals diffusion-layout.expected (${r.want.length} lines, the control included)`, got.length === r.want.length && diff.length === 0, `${diff.length ? diff[0] : got.length + ' lines'}${pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : ''}`);
  }
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
