// Stage 4 of docs/Designs/Active/Apps/InBrowserDiffusion.md: an SD1.5 image
// generated in a compiled page, codex/plugs/html/arms/Txt2ImgArm.codex, in
// headless Edge: the picked checkpoint's CLIP-L, UNet and VAE decoder as f32,
// the seed's torch noise graded against build/torch-noise-oracle.py's draws,
// then browser-txt2img. The canvas is saved as a PNG; given --ref <png> (the
// native codex_image render of the same request) the two are compared in the
// page, pixel by pixel.
// Usage: node codex/plugs/html/arms/txt2img.mjs [--out <png>] [--ref <png>]
//        [--prompt <text>] [--seed <n>] [--steps <n>]   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const ckpt = join(repo, 'build-output', 'diffusion-models', 'realisticVisionV60B1_v20Novae.safetensors');
const tokDir = 'D:\\AI\\DiffusionForge\\webui\\backend\\huggingface\\stabilityai\\stable-diffusion-xl-base-1.0\\tokenizer';
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const outPng = resolve(arg('--out', join(repo, 'build-output', 'browser-txt2img.png'))), refPng = arg('--ref', null);
const req = { prompt: arg('--prompt', 'a photo of a cat'), negative: '', seed: arg('--seed', '0'), steps: arg('--steps', '20'), cfg10: '70', size: '512' };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });
const asF = v => new Float32Array(new Int32Array([v]).buffer)[0];

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'txt2img-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'Txt2ImgArm.codex'), '-Out', join(work, 'tx.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'tx.codex'), '-Out', join(work, 'tx.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const data = { bk: readFileSync(join(work, 'bk.wgsl'), 'utf8'), vocab: JSON.stringify(Array.from(readFileSync(join(tokDir, 'vocab.json')))), merges: JSON.stringify(Array.from(readFileSync(join(tokDir, 'merges.txt')))), ...req };
  const html = readFileSync(join(work, 'tx.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
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
  for (let i = 0; i < 7200; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  const errs = pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : '';
  const parse = t => { try { const a = JSON.parse(t); return Array.isArray(a) ? a : null; } catch { return null; } };

  ok('CLIP-L, the UNet and the VAE decoder bind 1022 tensors, every one uploaded as f32', st.tensors === 1022 && st.failed === 0, `${st.tensors} bound, ${st.failed} failed${errs}`);
  ok('ClipBpe loads', st.bpe === 0, `status ${st.bpe}`);
  const nz = parse(st.noise), tn = readFileSync(join(repo, 'codex', 'test', 'gpu-files', 'torch-noise.bin'));
  const nzBad = nz ? nz.slice(0, 16384).filter((v, i) => !(Math.abs(asF(v) - tn.readFloatLE(i * 4)) <= 1e-5)).length : 16384;
  ok(`seed ${req.seed}'s noise is torch's (4 x 64 x 64, draw 0, within 1e-5)`, req.seed !== '0' || nzBad === 0, req.seed === '0' ? `${nzBad} of 16384 outside` : 'not graded: the fixture is seed 0');
  ok('the image is made and shown with no WebGPU error', st.image > 0 && st['err-run'] === '' && st.err === '' && /"ok":true/.test(st.shown || ''), `image ${st.image}, error '${st['err-run'] || st.err}', shown ${st.shown}, ${secs} s from the click`);
  const px = parse(st.pixels);
  const f = px ? px.map(asF) : [];
  const nan = f.filter(v => !Number.isFinite(v)).length, mean = f.reduce((a, v) => a + v, 0) / (f.length || 1), sd = Math.sqrt(f.reduce((a, v) => a + (v - mean) ** 2, 0) / (f.length || 1));
  ok('the image is 3 x 512 x 512 values with no NaN, and not flat', f.length === 786432 && nan === 0 && sd > 0.05, `${f.length} values, ${nan} NaN, mean ${mean.toFixed(3)}, sd ${sd.toFixed(3)}`);
  const dataUrl = await evalIn('document.querySelector("#out").toDataURL("image/png")');
  if (dataUrl) { writeFileSync(outPng, Buffer.from(dataUrl.split(',')[1], 'base64')); console.log(`  saved ${outPng}`); }
  if (refPng) {
    const refUrl = 'data:image/png;base64,' + readFileSync(refPng).toString('base64');
    const cmp = await evalIn(`new Promise((res) => { const im = new Image(); im.onload = () => { const c = document.createElement('canvas'); c.width = im.width; c.height = im.height; const x = c.getContext('2d'); x.drawImage(im, 0, 0); const a = x.getImageData(0, 0, c.width, c.height).data, o = document.querySelector('#out'), b = o.getContext('2d').getImageData(0, 0, o.width, o.height).data; if (a.length !== b.length) { res({ size: [c.width, c.height, o.width, o.height] }); return; } let sum = 0, max = 0, w8 = 0, n = 0; for (let i = 0; i < a.length; i++) { if (i % 4 === 3) continue; const d = Math.abs(a[i] - b[i]); sum += d; if (d > max) max = d; if (d <= 8) w8++; n++; } res({ mean: sum / n, max, w8: w8 / n }); }; im.onerror = () => res({ error: 'ref did not load' }); im.src = ${JSON.stringify(refUrl)}; })`);
    ok('side by side with the native render: mean difference under 4 of 255', cmp && cmp.mean !== undefined && cmp.mean < 4, JSON.stringify(cmp));
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
