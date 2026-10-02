import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'random-int-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'RandomIntArm.codex'), '-Out', join(work, 'dpa.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'dpa.codex'), '-Out', join(work, 'dpa.html')]);
  const html = readFileSync(join(work, 'dpa.html'), 'utf8');

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', '--disable-gpu', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });

  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map(); const errors = [];
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('Runtime.enable');
  await send('Page.addScriptToEvaluateOnNewDocument',{source:'Math.random=()=>0.25;'});
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const st = await evalIn('({done:Number(_st.done),checked:Number(_st.checked),sample:Number(_st.sample),large:String(_st.large),kind:typeof _st.large})');

  ok('the page loads with no exception', errors.length === 0 && st.done === 1, errors.length ? String(errors[0]).split('\n')[0] : `done ${st.done}`);
  ok('compiled random integers call the runtime, compose with bit operations and retain bounds',st.checked===1&&st.sample>0&&st.sample<=2147483646);
  ok('fixed bounds beyond Number precision remain exact Integer values',st.kind==='bigint'&&st.large==='9007199254740993',JSON.stringify(st));
  const endpoints=await evalIn(`(()=>{const saved=Math.random;try{Math.random=()=>0;const low=random_int(-9223372036854775808n,9223372036854775807n);Math.random=()=>1-1/4294967296;const high=random_int(-9223372036854775808n,9223372036854775807n);return [String(low),String(high)]}finally{Math.random=saved}})()`);
  ok('the full signed Integer range preserves both endpoints',JSON.stringify(endpoints)==='["-9223372036854775808","9223372036854775807"]',JSON.stringify(endpoints));
  const rejected=await evalIn(`(()=>{const saved=Math.random,draws=[3/4294967296,1/4294967296];let n=0;try{Math.random=()=>{if(n===draws.length)throw Error('Unexpected rejection loop');return draws[n++]};const value=random_int(10n,12n);return {value:String(value),draws:n}}finally{Math.random=saved}})()`);
  ok('out-of-range random words are rejected before returning a sample',rejected.value==='11'&&rejected.draws===2,JSON.stringify(rejected));
  ok('reversed bounds refuse explicitly',await evalIn(`(()=>{try{random_int(3n,2n);return false}catch(e){return e instanceof RangeError}})()`));
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
