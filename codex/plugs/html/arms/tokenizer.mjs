// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: ClipBpe in a
// compiled page, codex/plugs/html/arms/TokenizerArm.codex, in headless Edge,
// over the html plug's byte heap, against the ids Forge chose in
// codex/test/apps/clip-sd15-ref: "a photo of a cat" and the empty prompt whole;
// for the long prompt, Forge's first chunk (it cuts at 75 tokens, or back to a
// comma) must be a prefix of the page's ids for the text before BREAK. Control:
// the same load with merges.txt cut after 1000 merges must miss the cat.
// Usage: node codex/plugs/html/arms/tokenizer.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const tokDir = 'D:\\AI\\DiffusionForge\\webui\\backend\\huggingface\\stabilityai\\stable-diffusion-xl-base-1.0\\tokenizer';
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

const refIds = (name) => { const b = readFileSync(join(repo, 'codex', 'test', 'apps', 'clip-sd15-ref', name + '.ref')); return Array.from({ length: 77 }, (_, i) => b.readInt32LE(4 + i * 4)); };
const upToEos = (ids) => ids.slice(0, ids.indexOf(49407) + 1);
const long = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window, carved dragon heads on the rafters, frost on the windows, a sleeping wolf by the hearth';
const vocab = readFileSync(join(tokDir, 'vocab.json')), merges = readFileSync(join(tokDir, 'merges.txt'));
let cut = 0; for (let lines = 0; cut < merges.length && lines < 1001; cut++) if (merges[cut] === 10) lines++;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'tokenizer-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'TokenizerArm.codex'), '-Out', join(work, 'tk.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'tk.codex'), '-Out', join(work, 'tk.html')]);
  const data = { vocab: JSON.stringify(Array.from(vocab)), merges: JSON.stringify(Array.from(merges)), cut: String(cut), p0: 'a photo of a cat', p1: '', p2: long };
  const html = readFileSync(join(work, 'tk.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
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
  });
  await send('Page.enable'); await send('Runtime.enable');
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
  const t0 = Date.now();
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 2400; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  const errs = pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : '';
  const parse = t => { try { const a = JSON.parse(t); return Array.isArray(a) ? a : []; } catch { return []; } };
  const same = (a, b) => a.length === b.length && a.every((v, i) => v === b[i]);

  ok('ClipBpe loads in the page', st.status === 0 && st.done === 1, `status ${st.status}, ${secs} s to load and tokenize${errs}`);
  const cat = parse(st.t0), empty = parse(st.t1), lg = parse(st.t2), c0 = upToEos(refIds('cat')), e0 = upToEos(refIds('empty')), l0 = upToEos(refIds('long'));
  ok('"a photo of a cat" is Forge\'s ids', same(cat, c0), JSON.stringify(cat));
  ok('the empty prompt is Forge\'s ids', same(empty, e0), JSON.stringify(empty));
  const body = l0.slice(0, -1);
  ok(`the long prompt's first chunk (${body.length - 1} tokens) is a prefix of the page's ids`, body.length > 60 && same(lg.slice(0, body.length), body), `${lg.length - 2} tokens`);
  ok('control, merges cut after 1000, misses the cat', !same(parse(st['t-cut']), c0), st['t-cut']);
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
