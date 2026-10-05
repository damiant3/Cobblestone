// Spark Studio step 1, simple mode: the page built by build-studio-page.mjs
// classifies a real models folder by header, loads the SD1.5 checkpoint in it
// through apps/diffusion/BrowserLoad, and generates one Draft image in headless
// Edge on this machine's GPU. The picture must be drawn and must not be flat.
// Usage: node apps/spark/studio-generate.mjs [--models <dir>] [--page <html>]
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const models = resolve(arg('--models', 'D:\\AI\\DiffusionForge\\webui\\models\\Stable-diffusion'));
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

let edge = null, server = null, work = null;
const watchdog = setTimeout(() => { console.log('  FAIL  the arm finished within 40 minutes'); console.log('FAIL: watchdog'); try { edge && edge.kill(); } catch {} process.exit(1); }, 40 * 60000);
try {
  work = mkdtempSync(join(tmpdir(), 'spark-studio-gen-'));
  const page = arg('--page', '');
  if (!page) execFileSync('node', [join(repo, 'apps', 'spark', 'build-studio-page.mjs'), '--out', join(work, 'page.html')], { stdio: 'inherit' });
  const html = readFileSync(page || join(work, 'page.html'), 'utf8');
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
  let id = 0; const waiting = new Map(); const errors = []; let nextFolder = models;
  // A CDP call the page never answers (a crashed renderer, a blocked main
  // thread) otherwise hangs the arm forever with nothing in the log.
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; const t = setTimeout(() => { waiting.delete(i); rej(new Error(`${method} unanswered after 90 s${crashed ? ' (renderer crashed)' : ''}: ${String(params.expression || '').slice(0, 80)}`)); }, 90000); waiting.set(i, (m) => { clearTimeout(t); res(m); }); ws.send(JSON.stringify({ id: i, method, params })); });
  let crashed = false;
  ws.addEventListener('close', () => { crashed = true; });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.method === 'Inspector.targetCrashed') { crashed = true; console.log('  renderer crashed'); }
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [nextFolder], backendNodeId: m.params.backendNodeId });
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Inspector.enable'); await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }); return r.result?.result?.value; };
  const log = () => evalIn('document.getElementById("log").textContent');
  const waitLog = async (re, seconds) => { for (let i = 0; i < seconds * 4; i++) { const t = await log(); if (re.test(t || '')) return t; await sleep(250); } return await log(); };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; }
  await evalIn('document.getElementById("models-pick").click(), 1');
  const checked = await waitLog(/Checked \d+ files/, 180);
  const sd15 = await evalIn('[...document.querySelectorAll("#models > div")].filter(e => e.dataset.family === "sd15 checkpoint").map(e => e.id)');
  ok('the models folder is classified by header', /Checked \d+ files: [1-9]\d* supported checkpoints/.test(checked || ''), (checked || '').split('\n').pop());
  await evalIn(`document.getElementById(${JSON.stringify((sd15 || [])[0] || 'none')}).click(), 1`);
  const ready = await waitLog(/ready: |refused: /, 600);
  ok('an SD1.5 checkpoint loads', /ready: /.test(ready || ''), (ready || '').match(/(ready|refused): .*/)?.[0]);
  await evalIn('document.getElementById("prompt").value = "a red apple on a wooden table, photo", document.getElementById("seed").value = "42", document.getElementById("draft").click(), document.getElementById("go").click(), 1');
  const done = await waitLog(/job 1: done|refused|failed/, 900);
  ok('Generate draws a Draft image', /job 1: done/.test(done || ''), (done || '').match(/job 1: (done.*|.*)|refused.*|failed.*/)?.[0]);
  const stats = await evalIn('(() => { const c = document.getElementById("out"); const d = c.getContext("2d").getImageData(0, 0, c.width, c.height).data; let min = 255, max = 0, sum = 0; for (let i = 0; i < d.length; i += 4) { const v = (d[i] + d[i + 1] + d[i + 2]) / 3; if (v < min) min = v; if (v > max) max = v; sum += v; } return { w: c.width, h: c.height, min, max, mean: sum / (d.length / 4) }; })()');
  ok('the picture is 512 x 512 and not flat', stats && stats.w === 512 && stats.h === 512 && stats.max - stats.min > 64, JSON.stringify(stats));
  // Generate all over a two-prompt project whose prompts fit one CLIP chunk:
  // 2 runs each is 4 images, a prompt's runs together; then 3 runs each,
  // cancelled after the first image, stops short of 6.
  // The queue project lives in an origin-private folder standing in for the
  // directory picker, so the page opens it writable and writes each image.
  await evalIn(`(async () => { const root = await navigator.storage.getDirectory(); try { await root.removeEntry('QueueProject', { recursive: true }); } catch {} const d = await root.getDirectoryHandle('QueueProject', { create: true });
    const w = await (await d.getFileHandle('ArtPrompts.txt', { create: true })).createWritable(); await w.write('PROMPT 01 \\u2014 "Apple"\\n\\na red apple on a table\\n\\nPROMPT 02 \\u2014 "Boat"\\n\\na small boat on a lake\\n'); await w.close();
    window.showDirectoryPicker = async () => d; return 1; })()`);
  await evalIn('document.getElementById("open").click(), 1');  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('Number(_st.pcount) === 2')) break; }
  await evalIn('document.getElementById("runs").value = "2", document.getElementById("draft").click(), document.getElementById("go-all").click(), 1');
  const fin = await waitLog(/queue: finished \d+ of \d+|queue: cancelled/, 900);
  const q = await evalIn('({ images: Number(_st["q-images"]), order: _st["q-order"] })');
  const runsOf = (q.order || '').split('|').filter(Boolean);
  const together = runsOf.length === 4 && runsOf[0].split(' r')[0] === runsOf[1].split(' r')[0] && runsOf[2].split(' r')[0] === runsOf[3].split(' r')[0] && runsOf[0].split(' r')[0] !== runsOf[2].split(' r')[0];
  ok('Generate all draws every prompt\'s runs, a prompt\'s runs together', /queue: finished 4 of 4/.test(fin || '') && q.images === 4 && together, `${q.images} images; order ${q.order}`);
  const qcat = JSON.parse(await evalIn(`(async () => { const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('QueueProject'); const c = await d.getDirectoryHandle('Concept'); return await (await (await c.getFileHandle('catalog.json')).getFile()).text(); })()`) || '[]');
  const tag = 'Concept/512x512_s12_cfg7_Euler_Automatic_seed-1/';
  const wantPaths = [tag + 'prompt_01_apple.png', tag + 'prompt_01_apple_r01.png', tag + 'prompt_02_boat.png', tag + 'prompt_02_boat_r01.png'];
  const sizes = await evalIn(`(async () => { const out = []; const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('QueueProject'); for (const p of ${JSON.stringify(wantPaths)}) { try { let h = d; const parts = p.split('/'); for (const x of parts.slice(0, -1)) h = await h.getDirectoryHandle(x); out.push((await (await h.getFileHandle(parts[parts.length - 1])).getFile()).size); } catch { out.push(0); } } return out; })()`);
  ok('each image is written with its catalog record', qcat.length === 4 && JSON.stringify(qcat.map(r => r.filePath).sort()) === JSON.stringify([...wantPaths].sort()) && sizes.every(n => n > 1000) && qcat.every(r => r.seed > 0 && r.sourceWidth === 512), `${qcat.length} records; ${JSON.stringify(qcat.map(r => r.filePath))}; sizes ${JSON.stringify(sizes)}`);  await evalIn('document.getElementById("runs").value = "3", document.getElementById("go-all").click(), 1');
  for (let i = 0; i < 600; i++) { await sleep(250); if (await evalIn('Number(_st["q-images"]) >= 1')) break; }
  await evalIn('document.getElementById("cancel").click(), 1');
  const can = await waitLog(/queue: cancelled after \d+ of 6|queue: finished 6 of 6/, 300);
  const done2 = await evalIn('Number(_st["q-done"])');
  ok('Cancel stops the queue after the job in flight', /queue: cancelled after [12] of 6/.test(can || '') && done2 < 6, `q-done ${done2}; ${(can || '').match(/queue: (cancelled|finished)[^q]*/)?.[0]}`);
  // A rating of 2 on a prompt's newest run soft-deletes it and queues the
  // prompt again. Its file stays on disk, so the regen must take the next
  // free run index rather than report the deleted image (the original's Cached).
  for (let i = 0; i < 120; i++) { if (await evalIn('Number(_st["q-busy"])') !== 1) break; await sleep(250); }
  const readCat = async () => JSON.parse(await evalIn(`(async () => { const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('QueueProject'); const c = await d.getDirectoryHandle('Concept'); return await (await (await c.getFileHandle('catalog.json')).getFile()).text(); })()`) || '[]');
  const sizeOf = (p) => evalIn(`(async () => { try { let h = await (await navigator.storage.getDirectory()).getDirectoryHandle('QueueProject'); const parts = ${JSON.stringify(p)}.split('/'); for (const x of parts.slice(0, -1)) h = await h.getDirectoryHandle(x); return (await (await h.getFileHandle(parts[parts.length - 1])).getFile()).size; } catch { return 0; } })()`);
  const cat0 = await readCat();
  const live1 = cat0.filter(r => r.promptNumber === 1 && !r.deletedUtc).sort((a, b) => a.generatedUtc < b.generatedUtc ? 1 : -1);
  const target = live1[0] || {};
  const targetSize = await sizeOf(target.filePath || '');
  await evalIn('document.getElementById("open").click(), 1');
  let j = -1;
  for (let i = 0; i < 120 && j < 0; i++) { await sleep(250); j = await evalIn(`(() => { for (let k = 0; k < 64; k++) if (_st["lb-id" + k] === ${JSON.stringify(target.id || '?')}) return k; return -1; })()`); }
  await evalIn(`document.getElementById("t${j}").click(), 1`);
  for (const type of ['keyDown', 'keyUp']) await send('Input.dispatchKeyEvent', { type, key: '2', code: 'Digit2', text: type === 'keyDown' ? '2' : undefined, windowsVirtualKeyCode: 50 });
  const rg = await waitLog(/queue: regen[\s\S]*?(written: |not written|regen failed)/, 300);
  for (let i = 0; i < 40; i++) { if (await evalIn('Number(_st["q-busy"])') !== 1) break; await sleep(250); }
  const cat1 = await readCat();
  const gone = cat1.find(r => r.id === target.id) || {};
  const fresh = cat1.filter(r => !cat0.some(o => o.id === r.id));
  const newPath = fresh[0]?.filePath || '';
  const newSize = await sizeOf(newPath);
  ok('a rating of 2 on the newest run regenerates its prompt into a free run', j >= 0 && !!gone.deletedUtc && fresh.length === 1 && fresh[0].promptNumber === 1 && !cat0.some(o => o.filePath === newPath) && newSize > 1000 && (await sizeOf(target.filePath)) === targetSize && /queue: regen/.test(rg || ''),
    `lightbox ${j}; deleted ${target.filePath} ${gone.deletedUtc ? 'yes' : 'no'}; new ${JSON.stringify(fresh.map(r => r.filePath))} ${newSize} bytes; old file ${targetSize} bytes`);
  // Advanced mode is a view over the same request: Good shows 25 in its steps
  // field, a CFG that is not a number is refused, and steps 8 at CFG 5.5 write
  // both prompts under that request's settings tag.
  const setIn = (id, v) => evalIn(`(() => { const e = document.getElementById(${JSON.stringify(id)}); e.value = ${JSON.stringify(v)}; e.dispatchEvent(new Event('input')); return 1; })()`);
  await evalIn('document.getElementById("adv-toggle").click(), document.getElementById("good").click(), 1');
  const advShown = await evalIn('getComputedStyle(document.getElementById("adv")).display + " " + document.getElementById("steps-in").value');
  // Match only log text written after the click: the log already holds the
  // earlier queues' finished and cancelled lines.
  const waitNew = async (re, seconds) => { const from = ((await log()) || '').length; return async () => { for (let i = 0; i < seconds * 4; i++) { const t = ((await log()) || '').slice(from); if (re.test(t)) return t; await sleep(250); } return ((await log()) || '').slice(from); }; };
  await setIn('cfg', 'abc');
  const wRefused = await waitNew(/refused: CFG must be a number/, 10);
  await evalIn('document.getElementById("runs").value = "1", document.getElementById("go-all").click(), 1');
  const refused = await wRefused();
  await setIn('steps-in', '8'); await setIn('cfg', '5.5');
  const catA = await readCat();
  const wFin = await waitNew(/queue: (finished|cancelled)/, 600);
  await evalIn('document.getElementById("go-all").click(), 1');
  const fin3 = await wFin();
  const catB = await readCat();
  const advTag = '512x512_s8_cfg5.5_Euler_Automatic_seed-1';
  const advNew = catB.filter(r => !catA.some(o => o.id === r.id));
  const advSizes = []; for (const r of advNew) advSizes.push(await sizeOf(r.filePath));
  ok('advanced mode sets the steps and CFG of the request it shares with simple mode', advShown === 'block 25' && /refused: CFG must be a number/.test(refused || '') && /queue: finished 2 of 2/.test(fin3 || '') && advNew.length === 2 && advNew.every(r => r.settingsTag === advTag && r.filePath.includes(advTag + '/')) && advSizes.every(n => n > 1000),
    `panel ${advShown}; refusal ${/refused: CFG/.test(refused || '')}; ${(fin3 || '').match(/queue: (finished|cancelled)[^q]*/)?.[0] || 'no queue end'}; ${advNew.length} new; tags ${JSON.stringify([...new Set(advNew.map(r => r.settingsTag))])}; sizes ${JSON.stringify(advSizes)}`);
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
