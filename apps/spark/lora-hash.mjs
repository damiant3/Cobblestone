// Spark Studio step 2: a LoRA's trigger words by hash, as the original's
// FetchMissingTriggerWordsAsync. The page's SHA256 (the html plug's
// file-sha256-then) against Node's (OpenSSL) over files at every block and
// chunk boundary and two real LoRAs; then the page's trigger words against
// CivitAI's by-hash answer for the same hash read here, joined by the
// original's rule (non-empty strings, ", ").
// Usage: node apps/spark/lora-hash.mjs [--loras <dir>]   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import { createHash } from 'node:crypto';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync, mkdirSync, createReadStream } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const loras = resolve(arg('--loras', 'D:\\AI\\DiffusionForge\\webui\\models\\Lora'));
// One LoRA CivitAI has no trigger words for, one it has several for.
const real = ['FLUX.1-dev-lora-Dark-Fantasy.safetensors', 'myststyle-slime-universe-xl_epoch_10.safetensors'];
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const sha = (path) => new Promise((res, rej) => { const h = createHash('sha256'); createReadStream(path).on('data', d => h.update(d)).on('end', () => res(h.digest('hex').toUpperCase())).on('error', rej); });

let edge = null, server = null, work = null;
const watchdog = setTimeout(() => { console.log('  FAIL  the arm finished within 20 minutes'); console.log('FAIL: watchdog'); try { edge && edge.kill(); } catch {} process.exit(1); }, 20 * 60000);
try {
  work = mkdtempSync(join(tmpdir(), 'spark-hash-'));
  const vec = join(work, 'vectors'); mkdirSync(vec);
  // 55, 56, 63, 64 and 65 bytes cross the one-block and two-block padding;
  // 4 MiB is the page's read chunk.
  const sizes = [0, 3, 55, 56, 63, 64, 65, 4194304, 4194305, 4194304 * 2 + 70];
  for (const n of sizes) { const b = Buffer.alloc(n); for (let i = 0; i < n; i++) b[i] = (i * 131 + 7) & 255; if (n === 3) b.write('abc'); writeFileSync(join(vec, `v${n}.bin`), b); }
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
  let id = 0; const waiting = new Map(); const errors = []; let pickDir = vec;
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; const tm = setTimeout(() => { waiting.delete(i); rej(new Error(`${method} unanswered after 300 s`)); }, 300000); waiting.set(i, (m) => { clearTimeout(tm); res(m); }); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [pickDir], backendNodeId: m.params.backendNodeId });
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }); return r.result?.result?.value; };
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; }

  // The vectors are not .safetensors, so no row lists them; the page's own
  // primitive hashes them by the names the folder picker gave them.
  await evalIn('document.getElementById("models-pick").click(), 1');
  for (let i = 0; i < 60; i++) { await sleep(250); if (await evalIn('Object.keys(_gfiles).length') >= sizes.length) break; }
  const pageVec = await evalIn(`(async () => { const out = {}; for (const [k, f] of Object.entries(_gfiles)) out[f.name] = JSON.parse(await new Promise(r => file_sha256_then(k, r))).sha256 || ''; return out; })()`);
  const bad = [];
  for (const n of sizes) { const want = await sha(join(vec, `v${n}.bin`)); if ((pageVec || {})[`v${n}.bin`] !== want) bad.push(`${n} bytes: page ${(pageVec || {})[`v${n}.bin`]}, node ${want}`); }
  ok('the page\'s SHA256 equals Node\'s at every block and chunk boundary', bad.length === 0 && Object.keys(pageVec || {}).length === sizes.length, bad.length ? bad.join('; ') : `${sizes.length} files, 0 to ${sizes[sizes.length - 1]} bytes`);

  // The real LoRAs, through the row the page shows for each.
  pickDir = loras;
  await evalIn('document.getElementById("models-pick").click(), 1');
  let log = '';
  for (let i = 0; i < 600 && !/Checked \d+ files/.test(log || ''); i++) { await sleep(500); log = await evalIn('document.getElementById("log").textContent'); }
  const rows = await evalIn('[...document.querySelectorAll("#models > div")].map((e, i) => [e.firstChild.textContent.split("/").pop().replace("  (not SD1.5)", ""), i])');
  let reached = 0;
  for (const name of real) {
    const i = (rows || []).find(r => r[0] === name)?.[1];
    const want = await sha(join(loras, name));
    await evalIn(`document.getElementById("tw${i}").click(), 1`);
    for (let t = 0; t < 600; t++) { await sleep(500); if (await evalIn(`Number(_st["tw-done${i}"]) === 1`)) break; }
    const got = await evalIn(`({ sha: _st["sha${i}"] || "", words: _st["tw${i}"] || "" })`);
    const resp = await fetch(`https://civitai.com/api/v1/model-versions/by-hash/${want}`);
    const body = resp.ok ? await resp.json() : {};
    const words = (Array.isArray(body.trainedWords) ? body.trainedWords : []).filter(w => typeof w === 'string' && w.length > 0).join(', ');
    if (words.length > 0) reached++;
    ok(`${name}: the page's hash and trigger words are the original's`, i !== undefined && got.sha === want && got.words === words, `row ${i}; hash ${got.sha === want ? 'equal' : got.sha + ' vs ' + want}; CivitAI ${resp.status}; words "${got.words}" vs "${words}"`);
  }
  ok('at least one LoRA has trigger words on CivitAI, so the words were compared', reached > 0, `${reached} of ${real.length}`);
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
