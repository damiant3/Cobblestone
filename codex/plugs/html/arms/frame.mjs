// The HTML plug's framing primitives (frame-mount, frame-serve, host-request-then) in headless Edge: a host page
// (FrameHostArm) mounts a child page (FrameChildArm) in a scripts-only sandboxed iframe; the child asks "ping",
// the host answers "pong:ping", the child reports "done:pong:ping" back and the host shows it. A message the host
// posts to itself must be ignored.
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
const build = (work, name) => {
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', name + '.codex'), '-Out', join(work, name + '.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, name + '.codex'), '-Out', join(work, name + '.html')]);
  return readFileSync(join(work, name + '.html'), 'utf8');
};

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'frame-arm-'));
  const host = build(work, 'FrameHostArm'), child = build(work, 'FrameChildArm');
  if (!host.includes('function frame_serve(') || !child.includes('function host_request_then(')) throw new Error('the plug emitted no framing runtime (stale html-plug.cdx?)');
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(q.url === '/child' ? child : host); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', '--disable-gpu', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map(); const errors = [];
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('Runtime.enable');
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  let got = '';
  for (let i = 0; i < 80 && got === ''; i++) { await sleep(250); try { got = await evalIn('(document.getElementById("got")||{}).textContent||""'); } catch {} }
  ok('the pages load with no exception', errors.length === 0, errors.length ? String(errors[0]).split('\n')[0] : '');
  ok('the child asks, the host answers by id, and the child reports back', got === 'done:pong:ping', JSON.stringify(got));
  const sandbox = await evalIn('(()=>{const f=document.querySelector("#host iframe");return f?f.getAttribute("sandbox"):null})()');
  ok('the frame is sandboxed to scripts alone (opaque origin)', sandbox === 'allow-scripts', JSON.stringify(sandbox));
  await evalIn('window.postMessage({id:1,body:"done:spoof"},"*")'); await sleep(500);
  const after = await evalIn('document.getElementById("got").textContent');
  ok('a message from any window but the frame is ignored', after === 'done:pong:ping', JSON.stringify(after));
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
