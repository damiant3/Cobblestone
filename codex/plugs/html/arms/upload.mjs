// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: a picked SD1.5
// checkpoint's header parsed and bound in a compiled Codex page,
// codex/plugs/html/arms/UploadArm.codex, and every tensor of its VAE part put
// on the device by a chain of gpu-buf-upload-then, in headless Edge. The first
// 64 bytes of the first and last tensors are read back and must equal the
// file's bytes at the offsets the page bound; a control reads the file 64
// bytes further on and must differ.
// Usage: node codex/plugs/html/arms/upload.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, openSync, readSync, closeSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const ckpt = join(repo, 'build-output', 'diffusion-models', 'realisticVisionV60B1_v20Novae.safetensors');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });
const fileWords = (off, n = 64) => { const fd = openSync(ckpt, 'r'), b = Buffer.alloc(n); readSync(fd, b, 0, n, off); closeSync(fd); return Array.from({ length: n / 4 }, (_, i) => b.readInt32LE(i * 4)); };

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'upload-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'UploadArm.codex'), '-Out', join(work, 'ua.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'ua.codex'), '-Out', join(work, 'ua.html')]);
  const html = readFileSync(join(work, 'ua.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify({ part: 'vae' })};</script><script>`);
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', 'about:blank'], { stdio: 'ignore' });
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
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [ckpt], backendNodeId: m.params.backendNodeId });
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 240; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.armed) === 1')) break; } catch {} }
  for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
  for (let i = 0; i < 1200; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  const errs = pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : '';

  ok('the VAE part binds 248 tensors', st.tensors === 248, `${st.tensors}${errs}`);
  ok('every tensor uploads, with no WebGPU error', st.failed === 0 && st.err === '', `failed ${st.failed}, ${st.bytes} bytes, error '${st.err}'`);
  const parse = t => { try { const a = JSON.parse(t); return Array.isArray(a) ? a : null; } catch { return null; } };
  const first = parse(st.first), last = parse(st.last);
  const wantFirst = st['first-offset'] ? fileWords(st['first-offset'], st['first-bytes']) : [], wantLast = st['last-offset'] ? fileWords(st['last-offset'], st['last-bytes']) : [];
  const same = (a, b) => a && b.length > 0 && a.length >= b.length && b.every((v, i) => v === a[i]);
  ok(`the first tensor (${st['first-name']}) reads back as the file's bytes`, same(first, wantFirst), JSON.stringify(first?.slice(0, 4)));
  ok(`the last tensor (${st['last-name']}) reads back as the file's bytes`, same(last, wantLast), JSON.stringify(last?.slice(0, 4)));
  ok('control, the file 64 bytes further on, differs', st['first-offset'] > 0 && !same(first, fileWords(st['first-offset'] + 64, st['first-bytes'])), '');
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
