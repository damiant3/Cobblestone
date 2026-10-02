// Spark Studio step 2: the family the page reads from each model file's
// header (apps/spark/SparkModelFile.codex), against the family the file
// declares. A LoRA made by kohya carries ss_base_model_version in its
// metadata; every other file's family is declared below with its reason.
// The page reads only a header, so each file here is its header alone,
// written into build-output/spark-family: the local Forge models, one SD1.5
// LoRA fetched from Hugging Face by a Range request, and two made here.
// Usage: node apps/spark/model-family.mjs [--models <dir>]   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync, mkdirSync, openSync, readSync, closeSync, readdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const models = resolve(arg('--models', 'D:\\AI\\DiffusionForge\\webui\\models'));
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

// A header file: the 8-byte length, then the JSON.
const headerOf = (path) => { const fd = openSync(path, 'r'); try { const b = Buffer.alloc(8); readSync(fd, b, 0, 8, 0); const n = Number(b.readBigUInt64LE(0)); const h = Buffer.alloc(n); readSync(fd, h, 0, n, 8); return Buffer.concat([b, h]); } finally { closeSync(fd); } };
const headerFile = (json) => { const h = Buffer.from(JSON.stringify(json), 'utf8'); const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(h.length)); return Buffer.concat([b, h]); };
const kohya = { 'sdxl_base_v0-9': 'sdxl LoRA', 'sdxl_base_v1-0': 'sdxl LoRA', 'sdxl_1.0': 'sdxl LoRA', 'sd_v1': 'sd15 LoRA', 'flux1': 'flux LoRA', 'zimage': 'none' };

// Files without kohya metadata, and why each is what it is.
const declared = {
  'realisticVisionV60B1_v20Novae.safetensors': 'sd15 checkpoint',       // CivitAI: SD 1.5
  'realisticVisionV60B1_v51HyperVAE.safetensors': 'sd15 checkpoint',    // CivitAI: SD 1.5
  'cyberrealisticXL_v4.safetensors': 'sdxl checkpoint',                 // CivitAI: SDXL 1.0
  'dreamshaperXL_lightningDPMSDE.safetensors': 'sdxl checkpoint',       // CivitAI: SDXL Lightning
  'flux1-schnell-fp8-e4m3fn.safetensors': 'flux checkpoint',            // Black Forest Labs FLUX.1 schnell
  '0.5(flux1-schnell-fp8-e4m3fn) + 0.5(uberRealisticPornMerge_v23Final).safetensors': 'flux checkpoint', // a Forge merge onto the schnell file
  'papercut-xl.safetensors': 'sdxl LoRA',                               // Hugging Face TheLastBen/Papercut_SDXL
  'AntiBlur.safetensors': 'none',                                       // Flux in diffusers (PEFT lora_A/lora_B) names, which codex_image's merge does not read
  'lcm-lora-sdv1-5.safetensors': 'sd15 LoRA',                           // Hugging Face latent-consistency/lcm-lora-sdv1-5
  'made-te-only.safetensors': 'ambiguous LoRA',                         // CLIP-L keys only: every family's CLIP-L takes them
  'made-sdxl-plus-unknown.safetensors': 'none',                         // an SDXL LoRA's header with one key no family names
};

let edge = null, server = null, work = null;
const watchdog = setTimeout(() => { console.log('  FAIL  the arm finished within 20 minutes'); console.log('FAIL: watchdog'); try { edge && edge.kill(); } catch {} process.exit(1); }, 20 * 60000);
try {
  const dir = join(repo, 'build-output', 'spark-family');
  rmSync(dir, { recursive: true, force: true }); mkdirSync(dir, { recursive: true });
  const expect = {};
  for (const sub of ['Stable-diffusion', 'Lora']) {
    for (const f of readdirSync(join(models, sub)).filter(n => n.endsWith('.safetensors'))) {
      const h = headerOf(join(models, sub, f));
      writeFileSync(join(dir, f), h);
      const md = JSON.parse(h.subarray(8).toString('utf8')).__metadata__ || {};
      expect[f] = declared[f] || kohya[md.ss_base_model_version] || '?';
    }
  }
  const lcm = 'https://huggingface.co/latent-consistency/lcm-lora-sdv1-5/resolve/main/pytorch_lora_weights.safetensors';
  const n8 = Buffer.from(await (await fetch(lcm, { headers: { Range: 'bytes=0-7' } })).arrayBuffer());
  const n = Number(n8.readBigUInt64LE(0));
  const lh = Buffer.from(await (await fetch(lcm, { headers: { Range: `bytes=8-${7 + n}` } })).arrayBuffer());
  writeFileSync(join(dir, 'lcm-lora-sdv1-5.safetensors'), Buffer.concat([n8, lh]));
  expect['lcm-lora-sdv1-5.safetensors'] = declared['lcm-lora-sdv1-5.safetensors'];
  const t = (name) => ({ dtype: 'F16', shape: [4, 768], data_offsets: [0, 0] });
  const te = {};
  for (const m of ['mlp_fc1', 'self_attn_q_proj']) { te[`lora_te_text_model_encoder_layers_0_${m}.lora_down.weight`] = t(); te[`lora_te_text_model_encoder_layers_0_${m}.lora_up.weight`] = t(); te[`lora_te_text_model_encoder_layers_0_${m}.alpha`] = { dtype: 'F16', shape: [], data_offsets: [0, 0] }; }
  writeFileSync(join(dir, 'made-te-only.safetensors'), headerFile(te));
  expect['made-te-only.safetensors'] = declared['made-te-only.safetensors'];
  const xl = JSON.parse(headerOf(join(models, 'Lora', 'pixel-art-xl.safetensors')).subarray(8).toString('utf8'));
  xl['lora_unet_not_a_module.lora_down.weight'] = t();
  writeFileSync(join(dir, 'made-sdxl-plus-unknown.safetensors'), headerFile(xl));
  expect['made-sdxl-plus-unknown.safetensors'] = declared['made-sdxl-plus-unknown.safetensors'];

  work = mkdtempSync(join(tmpdir(), 'spark-family-'));
  execFileSync('node', [join(repo, 'apps', 'spark', 'build-studio-page.mjs'), '--out', join(work, 'page.html')], { stdio: 'inherit' });
  const html = readFileSync(join(work, 'page.html'), 'utf8');
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const tg = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (tg) url = tg.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map(); const errors = [];
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; const tm = setTimeout(() => { waiting.delete(i); rej(new Error(`${method} unanswered after 90 s`)); }, 90000); waiting.set(i, (m) => { clearTimeout(tm); res(m); }); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [dir], backendNodeId: m.params.backendNodeId });
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }); return r.result?.result?.value; };
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; }
  await evalIn('document.getElementById("models-pick").click(), 1');
  let done = '';
  for (let i = 0; i < 1200 && !/Checked \d+ files/.test(done || ''); i++) { await sleep(500); done = await evalIn('document.getElementById("log").textContent'); }
  const got = await evalIn('[...document.querySelectorAll("#models > div")].map((e, i) => [e.firstChild.textContent.split("/").pop().replace("  (not SD1.5)", ""), _st["fam" + i] || ""])');
  const names = Object.keys(expect);
  ok('the page reads every file', (got || []).length === names.length, `${(got || []).length} of ${names.length}`);
  const byName = Object.fromEntries(got || []);
  const wrong = names.filter(f => byName[f] !== expect[f]);
  for (const fam of ['sd15 checkpoint', 'sdxl checkpoint', 'flux checkpoint', 'sd15 LoRA', 'sdxl LoRA', 'flux LoRA', 'ambiguous LoRA', 'none'])
    ok(`${fam}: every file declared so reads so`, names.some(f => expect[f] === fam) && names.filter(f => expect[f] === fam).every(f => byName[f] === fam), names.filter(f => expect[f] === fam).map(f => `${f} -> ${byName[f]}`).join('; '));
  ok('no file reads a family it does not declare', wrong.length === 0, wrong.map(f => `${f}: page ${byName[f]}, declared ${expect[f]}`).join('; '));
  ok('the page threw nothing', errors.length === 0, errors.slice(0, 2).map(e => String(e).split('\n')[0]).join(' | '));
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
clearTimeout(watchdog);
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
