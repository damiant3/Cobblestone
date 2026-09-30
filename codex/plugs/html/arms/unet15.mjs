// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: one SD1.5 UNet
// step in a compiled page, codex/plugs/html/arms/UNet15Arm.codex, in headless
// Edge: the picked checkpoint's UNet uploaded (rank 2 and up as packed f16), unet15-forward-with over
// browser-unet-ops on codex/test/apps/sd15-unet-step's inputs, every block
// graded as that test grades it against apps/diffusion/UNet15Reference.codex
// (Forge's own step in f32): 64 samples within 1% of the block's rms and the
// mean square within 2%. Controls: input block 1 at t = 900, and with a zero
// context, must miss.
// Usage: node codex/plugs/html/arms/unet15.mjs   Exit 0 = every arm passed.
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

// UNet15Reference.codex: one UNetRefBlock per line; a negative is (0.0 - x).
const num = (t) => { const m = /^\(0\.0 - (.+)\)$/.exec(t.trim()); return m ? -Number(m[1]) : Number(t); };
const refs = readFileSync(join(repo, 'apps', 'diffusion', 'UNet15Reference.codex'), 'utf8').split('\n').filter(l => l.includes('= UNetRefBlock {')).map(l => {
  const f = (k) => new RegExp(`${k} = ([^,]+?)(?:,|\\s})`).exec(l)[1];
  const samples = /urb-samples = \[(.*)\]/.exec(l)[1].split(/,\s*(?![^(]*\))/).map(num);
  return { name: /urb-name = "([^"]+)"/.exec(l)[1], n: Number(f('urb-c')) * Number(f('urb-h')) * Number(f('urb-w')), rms: num(f('urb-rms')), samples };
});
const bits = (x) => new Int32Array(new Float32Array([x]).buffer)[0];
const latent = Array.from({ length: 1024 }, (_, i) => bits(((i * 37 + 11) % 257 - 128) / 128));
const context = Array.from({ length: 59136 }, (_, i) => bits(((i * 13 + 5) % 251 - 125) / 256));
const sizes = [...refs.map(r => r.n), refs[1].n, refs[1].n];

const grade = (words, ref) => {
  if (!Array.isArray(words) || words.length < ref.n) return { near: 0, rmsNear: false, text: 'NO OUTPUT' };
  const got = words.slice(0, ref.n).map(v => new Float32Array(new Int32Array([v]).buffer)[0]);
  let near = 0; for (let j = 0; j < 64; j++) if (Math.abs(got[(j * 7919 + 13) % ref.n] - ref.samples[j]) <= 0.01 * ref.rms) near++;
  const ms = got.reduce((a, v) => a + v * v, 0) / ref.n, rmsNear = Math.abs(ms - ref.rms * ref.rms) <= 0.02 * ref.rms * ref.rms;
  return { near, rmsNear, text: `${near}/64 within 1% of rms, rms ${rmsNear ? 'near' : 'FAR'}` };
};

let edge = null, server = null, work = null;
try {
  const fd = openSync(ckpt, 'r'), lb = Buffer.alloc(8); readSync(fd, lb, 0, 8, 0); const hl = Number(lb.readBigUInt64LE(0)), hb = Buffer.alloc(hl); readSync(fd, hb, 0, hl, 8); closeSync(fd);
  const want = Object.keys(JSON.parse(hb.toString())).filter(k => k.startsWith('model.diffusion_model.')).length;
  work = mkdtempSync(join(tmpdir(), 'unet15-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'UNet15Arm.codex'), '-Out', join(work, 'u1.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'u1.codex'), '-Out', join(work, 'u1.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const data = { bk: readFileSync(join(work, 'bk.wgsl'), 'utf8'), latent: JSON.stringify(latent), context: JSON.stringify(context), sizes: JSON.stringify(sizes) };
  const html = readFileSync(join(work, 'u1.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
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
  const t0 = Date.now();
  for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
  for (let i = 0; i < 2400; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  const errs = pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : '';
  const parse = t => { try { const a = JSON.parse(t); return Array.isArray(a) ? a : null; } catch { return null; } };

  ok(`the UNet binds the file's ${want} model.diffusion_model. tensors, every one uploaded (rank 2 and up as packed f16)`, st.tensors === want && st.failed === 0, `${st.tensors} bound, ${st.failed} failed${errs}`);
  ok('the step runs with no WebGPU error', st.done === 1 && st['err-run'] === '' && st.err === '', `error '${st['err-run'] || st.err}', ${secs} s from the click to every block read back`);
  refs.forEach((r, k) => { const g = grade(parse(st['b' + k]), r); ok(r.name, g.near === 64 && g.rmsNear, g.text); });
  const far = grade(parse(st['b' + refs.length]), refs[1]), zero = grade(parse(st['b' + (refs.length + 1)]), refs[1]);
  ok('control, input 1 at t = 900, misses', far.near < 64 || !far.rmsNear, far.text);
  ok('control, input 1 with a zero context, misses', zero.near < 64 || !zero.rmsNear, zero.text);
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
