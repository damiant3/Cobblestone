// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: file-read-then
// (apps/webapp/WebGpu.codex), a range of a picked file handed to Codex as
// bytes, driven by the compiled page codex/plugs/html/arms/FileReadArm.codex in
// headless Edge. An interior range must come back byte for byte, a range past
// the end must answer the bytes that exist, and a callback that throws must
// run once: a promise's catch that calls the callback again would run it twice.
// Usage: node codex/plugs/html/arms/file-read.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
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

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'file-read-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'FileReadArm.codex'), '-Out', join(work, 'fra.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'fra.codex'), '-Out', join(work, 'fra.html')]);
  const html = readFileSync(join(work, 'fra.html'), 'utf8');
  const bytes = Buffer.from(Array.from({ length: 1000 }, (_, i) => (i * 37 + 11) & 255));
  const bin = join(work, 'in.bin');
  writeFileSync(bin, bytes);

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
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
  let id = 0; const waiting = new Map();
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [bin], backendNodeId: m.params.backendNodeId });
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.armed) === 1')) break; } catch {} }
  for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
  for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  await sleep(1000);
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);

  const parse = t => { try { const a = JSON.parse(t); return Array.isArray(a) ? a : null; } catch { return null; } };
  const mid = parse(st.mid), end = parse(st.end), wantMid = Array.from(bytes.subarray(5, 305)), wantEnd = Array.from(bytes.subarray(900));
  ok('an interior range, 300 bytes at 5, comes back byte for byte', mid && mid.length === 300 && mid.every((v, i) => v === wantMid[i]), mid ? `${mid.length} bytes, ${mid.filter((v, i) => v !== wantMid[i]).length} differ` : String(st.mid).slice(0, 120));
  ok('a range past the end answers the 100 bytes that exist', end && end.length === 100 && end.every((v, i) => v === wantEnd[i]), end ? `${end.length} bytes` : String(st.end).slice(0, 120));
  ok('a callback that throws runs once', st.calls === 1, `calls ${st.calls}`);
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
