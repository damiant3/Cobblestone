// The html plug's Text is CCE-faithful (codex/plugs/plugs-backlog.md 2.108,
// root 2026-09-29): every character code a page hands a program is a CCE code
// point. Driven by the compiled page codex/plugs/html/arms/CceArm.codex in
// headless Edge: code-to-char then char-code answers what CCE's own
// from-unicode (to-unicode cp), compiled into the page, answers, for every tier
// 0 and tier 1 code and a tier 2 sample (CCE's tables overlap, so that is not
// always cp, natively either); char literals agree with char-code-at over the same
// text, with CCE's own from-unicode compiled into the page, and in a pattern;
// and a real SDXL checkpoint's header, parsed by SafeTensors' st-parse-file in
// the page, names the tensors JSON.parse finds in the same bytes.
// Usage: node codex/plugs/html/arms/cce.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, openSync, readSync, closeSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const ckpt = join(repo, 'build-output', 'diffusion-models', 'dreamshaperXL_lightningDPMSDE.safetensors');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'cce-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'CceArm.codex'), '-Out', join(work, 'cce.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'cce.codex'), '-Out', join(work, 'cce.html')]);
  const html = readFileSync(join(work, 'cce.html'), 'utf8');

  const fd = openSync(ckpt, 'r'), lenBuf = Buffer.alloc(8); readSync(fd, lenBuf, 0, 8, 0);
  const hlen = Number(lenBuf.readBigUInt64LE(0)), hbuf = Buffer.alloc(hlen); readSync(fd, hbuf, 0, hlen, 8); closeSync(fd);
  const header = JSON.parse(hbuf.toString('utf8'));
  const want = Object.entries(header).filter(([k]) => k !== '__metadata__').map(([k, v]) => `${k}:${v.shape.length}:${v.data_offsets[0]}-${v.data_offsets[1]}`).sort();

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
    else if (m.method === 'Runtime.exceptionThrown') pageErrors.push(String(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text).split('\n').slice(0, 8).join(' / ').slice(0, 600));
    else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') pageErrors.push(m.params.args.map(a => a.value ?? a.description).join(' ').slice(0, 300));
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [ckpt], backendNodeId: m.params.backendNodeId });
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
  const state = () => evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 240; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.armed) === 1')) break; } catch {} }
  const s0 = await state();
  ok('code-to-char then char-code agrees with CCE from-unicode (to-unicode cp) for every tier 0 and tier 1 code', s0['rt-bad-01'] === 0, `${s0['rt-bad-01']} of 2176 differ`);
  ok('... and 4096 tier 2 codes', s0['rt-bad-2'] === 0, `${s0['rt-bad-2']} of 4096 differ`);
  const lit = String(s0.literals || '').split(',');
  const names = ['char-code-at agrees with the literal A', 'with the literal {', 'with the literal é', 'the literal é is from-unicode 233', 'Ж read from text is from-unicode 1046, the literal д 1076', '日 read from text is from-unicode 26085', 'A is from-unicode 65', 'a char pattern matches é read from text', 'a char pattern matches {', 'a char pattern falls through for A'];
  const wantLit = ['True', 'True', 'True', 'True', 'True', 'True', 'True', '1', '2', '0'];
  names.forEach((n, i) => ok(n, lit[i] === wantLit[i], lit[i]));

  for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
  for (let i = 0; i < 1200; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const st = await state();
  ok('the checkpoint header length is read', st.hlen === hlen, `${st.hlen}, want ${hlen}`);
  ok('st-parse-file accepts the SDXL header', st.valid === 1, `valid ${st.valid}${pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : ''}`);
  const got = String(st.tensors || '').split('\n').filter(Boolean).sort();
  ok('the tensor count matches JSON.parse', st.count === want.length, `${st.count}, want ${want.length}`);
  const miss = want.filter((w, i) => got[i] !== w).length;
  ok('every tensor\'s name, rank and data offsets match', got.length === want.length && miss === 0, `${miss} of ${want.length} differ`);
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
