// Spark Studio step 2's grading of the catalogue (SparkStudio.md section 4):
// the entries are read from the page as it shows them; every page link must
// answer 200, every file must answer a Range request with its header and a
// total size equal to the entry's, and the page must classify every header as
// the family the entry declares. A copy of one header declared as another
// family is the sabotage, and must be reported as a mismatch.
// Usage: node apps/spark/catalogue-check.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const range = async (url, a, b) => { const r = await fetch(url, { headers: { Range: `bytes=${a}-${b}` } }); return { status: r.status, total: Number((r.headers.get('content-range') || '').split('/')[1] || 0), body: Buffer.from(await r.arrayBuffer()) }; };

let edge = null, server = null, work = null;
const watchdog = setTimeout(() => { console.log('  FAIL  the arm finished within 20 minutes'); console.log('FAIL: watchdog'); try { edge && edge.kill(); } catch {} process.exit(1); }, 20 * 60000);
try {
  work = mkdtempSync(join(tmpdir(), 'spark-catalogue-'));
  execFileSync('node', [join(repo, 'apps', 'spark', 'build-studio-page.mjs'), '--out', join(work, 'page.html')], { stdio: 'inherit' });
  const html = readFileSync(join(work, 'page.html'), 'utf8');
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  const dir = join(work, 'headers'); mkdirSync(dir);
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
  const entries = await evalIn('[...document.querySelectorAll("#catalogue [data-family]")].map(e => ({ name: e.querySelector("a").textContent, page: e.querySelector("a").href, target: e.querySelector("a").target, fileUrl: e.dataset.fileUrl, family: e.dataset.family, mb: Number(e.dataset.mb) }))') || [];
  ok('the page shows the catalogue', entries.length >= 6 && new Set(entries.map(e => e.family)).size === 6, `${entries.length} entries over ${new Set(entries.map(e => e.family)).size} families`);

  const pageBad = [], fileBad = [], sizeBad = [], expect = {};
  for (let i = 0; i < entries.length; i++) {
    const e = entries[i];
    const p = await fetch(e.page); await p.arrayBuffer();
    if (p.status !== 200 || e.target !== '_blank') pageBad.push(`${e.name}: ${p.status} ${e.target}`);
    const a = await range(e.fileUrl, 0, 7);
    const n = a.body.length === 8 ? Number(a.body.readBigUInt64LE(0)) : 0;
    const h = n > 0 && n < 100000000 ? await range(e.fileUrl, 8, 7 + n) : { status: 0, body: Buffer.alloc(0) };
    if (a.status !== 206 || h.status !== 206 || h.body.length !== n) fileBad.push(`${e.name}: ${a.status}/${h.status}, header ${h.body.length} of ${n}`);
    else { const name = `${i}-${e.fileUrl.split('/').pop()}`; writeFileSync(join(dir, name), Buffer.concat([a.body, h.body])); expect[name] = e.family; }
    if (Math.round(a.total / 1048576) !== e.mb) sizeBad.push(`${e.name}: ${Math.round(a.total / 1048576)} MB, entry ${e.mb}`);
  }
  ok('every page link answers 200 and opens apart', pageBad.length === 0, pageBad.join('; '));
  ok('every file answers its header by Range', fileBad.length === 0, fileBad.join('; '));
  ok('every file\'s size is the entry\'s', sizeBad.length === 0, sizeBad.join('; '));

  // The sabotage: the first entry's header, declared as another family.
  const first = Object.keys(expect)[0];
  writeFileSync(join(dir, 'control-wrong.safetensors'), readFileSync(join(dir, first)));
  const wrongFamily = expect[first] === 'sdxl checkpoint' ? 'sd15 checkpoint' : 'sdxl checkpoint';

  await evalIn('document.getElementById("models-pick").click(), 1');
  let log = '';
  for (let i = 0; i < 600 && !/Checked \d+ files/.test(log || ''); i++) { await sleep(500); log = await evalIn('document.getElementById("log").textContent'); }
  const got = Object.fromEntries(await evalIn('[...document.querySelectorAll("#models > div")].map((e, i) => [e.firstChild.textContent.split("/").pop().replace("  (not SD1.5)", ""), _st["fam" + i] || ""])') || []);
  const wrong = Object.keys(expect).filter(f => got[f] !== expect[f]);
  ok('every entry\'s file reads the family the entry declares', wrong.length === 0 && Object.keys(expect).length === entries.length, wrong.length ? wrong.map(f => `${f}: page ${got[f]}, entry ${expect[f]}`).join('; ') : `${Object.keys(expect).length} files`);
  ok('a header declared as the wrong family is reported as a mismatch', got['control-wrong.safetensors'] === expect[first] && got['control-wrong.safetensors'] !== wrongFamily, `control reads ${got['control-wrong.safetensors']}, declared ${wrongFamily}`);
  // The LoRA search: the page's rows against CivitAI's answer to the URL the
  // original builds, read here by the original's rules (the first SDXL
  // version else the first, SDXL rows first, then more downloads first).
  await evalIn('document.getElementById("lora-q").value = "pixel", document.getElementById("lora-rated").click(), 1');
  for (let i = 0; i < 120; i++) { await sleep(500); if (await evalIn('Number(_st["lr-done"]) === 1')) break; }
  const shown = await evalIn('[...document.querySelectorAll("#lora-results > div")].map(e => ({ name: e.querySelector("a").textContent, page: e.querySelector("a").href }))') || [];
  const want = async () => {
    const j = await (await fetch('https://civitai.com/api/v1/models?types=LORA&sort=Highest%20Rated&limit=30&nsfw=false&query=pixel')).json();
    const rows = [];
    for (const it of j.items || []) {
      const vs = it.modelVersions || []; if (vs.length === 0) continue;
      const v = vs.find(x => /sdxl/i.test(x?.baseModel || '')) || vs[0];
      rows.push({ name: it.name || '', page: it.id > 0 ? `https://civitai.com/models/${it.id}?modelVersionId=${v?.id || 0}` : '', sdxl: /sdxl/i.test(v?.baseModel || 'Unknown'), dl: it.stats?.downloadCount || 0 });
    }
    return rows.map((r, i) => ({ ...r, i })).sort((a, b) => (b.sdxl - a.sdxl) || (b.dl - a.dl) || (a.i - b.i)).map(r => ({ name: r.name, page: r.page }));
  };
  let expected = await want();
  if (JSON.stringify(expected) !== JSON.stringify(shown)) { await sleep(2000); expected = await want(); }
  const diff = expected.map((e, i) => JSON.stringify(e) === JSON.stringify(shown[i]) ? '' : `${i}: page ${JSON.stringify(shown[i])}, CivitAI ${JSON.stringify(e)}`).filter(Boolean);
  ok('the LoRA search shows CivitAI\'s answer by the original\'s rules', shown.length > 0 && shown.length === expected.length && diff.length === 0, `${shown.length} rows shown, ${expected.length} expected${diff.length ? '; ' + diff.slice(0, 3).join('; ') : ''}`);
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
