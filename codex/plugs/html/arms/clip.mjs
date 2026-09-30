// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: SD1.5's CLIP-L in
// a compiled page, codex/plugs/html/arms/ClipArm.codex, in headless Edge: the
// picked checkpoint's text encoder uploaded as f32, clip-encode-with over
// browser-clip-ops on the ids Forge chose for "a photo of a cat" and for the
// empty prompt (codex/test/apps/clip-sd15-ref, whose weights are all 1, where
// Forge's emphasis is the identity), graded against Forge's f32 cond by
// codex/test/apps/clip-sd15-prompt's bounds (the f16 model's own deviation),
// over all rows and over the rows that are not the BOS. Control: cat at clip
// skip 2 without the final LayerNorm must miss. The emphasis prompt is chunked
// in the page (ClipPromptText over ClipBpe) and must give Forge's ids and
// weights, and its cond with Forge's emphasis must meet that test's bounds;
// the same ids unweighted must miss.
// Usage: node codex/plugs/html/arms/clip.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
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

// A .ref is the chunk count (i32), 77 ids a chunk (i32), 77 weights a chunk
// (f32), then cond (f32, 77 x 768 a chunk).
const ref = (name) => {
  const b = readFileSync(join(repo, 'codex', 'test', 'apps', 'clip-sd15-ref', name + '.ref')), n = b.readInt32LE(0);
  return { n, ids: Array.from({ length: 77 }, (_, i) => b.readInt32LE(4 + i * 4)), weights: Array.from({ length: 77 * n }, (_, i) => b.readFloatLE(4 + n * 308 + i * 4)), cond: Array.from({ length: 77 * 768 }, (_, i) => b.readFloatLE(4 + n * 616 + i * 4)) };
};
const cat = ref('cat'), empty = ref('empty'), emph = ref('emphasis');
const tokDir = 'D:\\AI\\DiffusionForge\\webui\\backend\\huggingface\\stabilityai\\stable-diffusion-xl-base-1.0\\tokenizer';
const emphPrompt = '(masterpiece:1.2), ((best quality)), a [red] cat on a \\(wooden\\) table, (glowing runes:0.8), [[blurry]] (sharp:1.5 edges';
const maxErr = (words, want, skipBos) => {
  if (!Array.isArray(words) || words.length < want.length) return Infinity;
  let m = 0;
  for (let i = 0; i < want.length; i++) { if (skipBos && Math.floor(i / 768) % 77 === 0) continue; const e = Math.abs(new Float32Array(new Int32Array([words[i]]).buffer)[0] - want[i]); if (!(e <= m)) m = e; }
  return m;
};

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'clip-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'ClipArm.codex'), '-Out', join(work, 'ca.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'ca.codex'), '-Out', join(work, 'ca.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const data = { bk: readFileSync(join(work, 'bk.wgsl'), 'utf8'), cat: JSON.stringify(cat.ids), empty: JSON.stringify(empty.ids), emph: emphPrompt, vocab: JSON.stringify(Array.from(readFileSync(join(tokDir, 'vocab.json')))), merges: JSON.stringify(Array.from(readFileSync(join(tokDir, 'merges.txt')))) };
  const html = readFileSync(join(work, 'ca.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
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

  ok('the oracle prompts are one chunk each with every weight 1', cat.n === 1 && empty.n === 1 && [...cat.weights, ...empty.weights].every(w => w === 1), `cat ${cat.n}, empty ${empty.n}`);
  ok('CLIP-L binds its 196 tensors, every one uploaded as f32, and loads', st.tensors === 196 && st.failed === 0 && st.load === 0, `${st.tensors} bound, ${st.failed} failed, load ${st.load}${errs}`);
  ok('three encodes run with status 0 and no WebGPU error', st.status === '0,0,0' && st['err-run'] === '' && st.err === '', `status ${st.status}, error '${st['err-run'] || st.err}', ${secs} s from the click`);
  const c0 = parse(st.c0), c1 = parse(st.c1), c2 = parse(st.c2);
  const r = [['cat', c0, cat, 0.03046, 0.01266], ['empty', c1, empty, 0.03046, 0.01251]];
  for (const [label, got, want, all, body] of r) {
    const ea = maxErr(got, want.cond, false), eb = maxErr(got, want.cond, true);
    ok(`${label} cond, all rows within Forge f16 (${all})`, ea <= all, `max ${ea.toExponential(3)}`);
    ok(`${label} cond, non-BOS rows within Forge f16 (${body})`, eb <= body, `max ${eb.toExponential(3)}`);
  }
  const es = maxErr(c2, cat.cond, true);
  ok('control, cat at clip skip 2 without the final LayerNorm, misses the non-BOS bound', es > 0.01266, `max ${es.toExponential(3)}`);
  const ids = (st['emph-ids'] || '').split(',').map(Number), mults = (st['emph-mults'] || '').split(',').map(Number);
  const idsSame = st['emph-chunks'] === 1 && ids.length === 77 && ids.every((v, i) => v === emph.ids[i]);
  const multsSame = mults.length === 77 && mults.every((v, i) => Math.fround(v) === emph.weights[i]);
  ok('the emphasis prompt chunks as Forge\'s: one chunk, the same 77 ids and weights', idsSame && multsSame, `${st['emph-chunks']} chunks, ids ${idsSame ? 'same' : 'differ'}, weights ${multsSame ? 'same' : 'differ: ' + mults.slice(0, 8).join(',')}`);
  const c3 = parse(st.c3), c4 = parse(st.c4);
  const ea = maxErr(c3, emph.cond, false), eb = maxErr(c3, emph.cond, true), en = maxErr(c4, emph.cond, true);
  ok('emphasis cond, all rows within Forge f16 (0.0303)', ea <= 0.0303, `max ${ea.toExponential(3)}`);
  ok('emphasis cond, non-BOS rows within Forge f16 (0.01499)', eb <= 0.01499, `max ${eb.toExponential(3)}`);
  ok('control, the same ids with every weight 1, misses the non-BOS bound', en > 0.01499, `max ${en.toExponential(3)}`);
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
