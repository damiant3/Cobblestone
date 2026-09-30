// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: the VAE decoder
// run in a compiled page, codex/plugs/html/arms/VaeDecodeArm.codex, in headless
// Edge: dreamshaperXL's decoder uploaded as f32 from the picked checkpoint, then
// vae-decode-with over browser-vae-ops on codex/test/apps/diffusion-vae-decode's
// latent, against that test's reference (Forge's own f32 decode) by that test's
// pixel rule and bar. A control decodes the latent with channels 0 and 1
// exchanged and must miss.
// Usage: node codex/plugs/html/arms/vae-decode.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const ckpt = join(repo, 'build-output', 'diffusion-models', 'dreamshaperXL_lightningDPMSDE.safetensors');
const fixtures = join(repo, 'codex', 'test', 'gpu-files');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

const lat = readFileSync(join(fixtures, 'vae-latent.bin'));
const latent = Array.from({ length: 4096 }, (_, i) => lat.readInt32LE(i * 4));
const swapped = [...latent.slice(1024, 2048), ...latent.slice(0, 1024), ...latent.slice(2048)];
const ref = readFileSync(join(fixtures, 'vae-reference.bin'));

// diffusion-vae-decode's pixel-of: clamp((v + 1) / 2, 0, 1) * 255 rounded, from
// the f32 bits in units of 2^-24; 1000 marks a NaN or infinity.
const pixelOf = (raw) => {
  const bits = raw >>> 0, neg = (bits & 0x80000000) !== 0, e = (bits >>> 23) & 255;
  if (e === 255) return 1000;
  if (e >= 128) return neg ? 0 : 255;
  const m = (bits & 0x7FFFFF) | 0x800000;
  const fx = e === 0 ? 0 : e >= 126 ? m * 2 ** (e - 126) : Math.floor(m / 2 ** (126 - e));
  const v = neg ? -fx : fx;
  if (v <= -16777216) return 0;
  if (v >= 16777216) return 255;
  return Math.floor(((v + 16777216) * 255 + 16777216) / 33554432);
};
const diff = (words) => {
  const hw = 65536, d = { max: 0, sum: 0, one: 0, three: 0, nan: 0 };
  if (!words || words.length !== 3 * hw) return null;
  for (let i = 0; i < hw * 3; i++) {
    const pix = Math.floor(i / 3), c = i - pix * 3, got = pixelOf(words[c * hw + pix]), a = Math.abs(got - ref[i]);
    d.max = Math.max(d.max, a); d.sum += a; d.one += a <= 1 ? 1 : 0; d.three += a <= 3 ? 1 : 0; d.nan += got === 1000 ? 1 : 0;
  }
  return d;
};
const fmt = (d) => d ? `max ${d.max}, mean ${Math.floor(d.sum * 1000 / 196608)}/1000, within 1: ${d.one}, within 3: ${d.three} of 196608, nan ${d.nan}` : 'no result';

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'vae-decode-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'VaeDecodeArm.codex'), '-Out', join(work, 'vd.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'vd.codex'), '-Out', join(work, 'vd.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const wgsl = readFileSync(join(work, 'bk.wgsl'), 'utf8');
  const data = { bk: wgsl, latent: JSON.stringify(latent), swapped: JSON.stringify(swapped) };
  const html = readFileSync(join(work, 'vd.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
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

  ok('the decoder binds the 140 tensors diffusion-vae-decode uploads, every one uploaded as f32', st.tensors === 140 && st.failed === 0, `${st.tensors} bound, ${st.failed} failed${errs}`);
  ok('two decodes run with no WebGPU error', st.done === 1 && st.err === '', `error '${st.err}', ${secs} s from the click to both results read back`);
  ok('the decode is 3 x 256 x 256', st.shape === '3x256x256', st.shape);
  const d = diff(parse(st.out)), c = diff(parse(st.swapped));
  ok('the decode matches Forge within diffusion-vae-decode\'s bar (max 2, no NaN)', d && d.max <= 2 && d.nan === 0, fmt(d));
  ok('control, channels 0 and 1 exchanged, misses', c && c.max > 32 && c.three < 196608 / 2, fmt(c));
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
