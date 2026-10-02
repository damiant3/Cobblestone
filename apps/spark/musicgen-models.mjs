import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const oracle = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const files = ['tokenizer','text','model','audio'].map(k => oracle.inputs[k]);
const pagePath = process.argv[3];
if (!pagePath) throw new Error('Usage: node apps/spark/musicgen-models.mjs oracle.json built-studio.html');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'musicgen-models-'));
  const html = readFileSync(pagePath, 'utf8');

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', '--enable-unsafe-webgpu', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });

  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0, pick = 0; const waiting = new Map(); const errors = [];
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', {files:[files[pick++]], backendNodeId:m.params.backendNodeId});
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog',{enabled:true});
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }); if(r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails)); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  const wait = async expression => {for(let i=0;i<2400;i++){if(await evalIn(expression))return true;await sleep(250);}return false;};
  await wait('typeof _st!=="undefined"&&Number(_st.ready)===1');
  await evalIn('document.getElementById("musicgen-load").click();1');
  ok('loading without files refuses', await evalIn('document.getElementById("musicgen-status").textContent.includes("all four")'));
  for(let i=0;i<4;i++){await evalIn('document.getElementById("musicgen-pick'+i+'").click();1');if(!await wait('!document.getElementById("musicgen-file'+i+'").textContent.includes("No file")'))throw Error('Picker did not complete');}
  const names=await evalIn('[0,1,2,3].map(i=>document.getElementById("musicgen-file"+i).textContent.trim())');
  ok('all model pickers retain their actual filenames', names.join('|') === files.map(f=>f.split(/[\\/]/).pop()).join('|'), names.join('|'));
  await evalIn('document.getElementById("musicgen-load").click();document.getElementById("musicgen-load").click();1');
  ok('duplicate Load is refused while loading', await evalIn('document.getElementById("musicgen-status").textContent.includes("already loading")'));
  const loaded=await wait('Number(_st["musicgen-ready"])===1||document.getElementById("musicgen-status").textContent.includes("could not load")');
  const state=await evalIn('({ready:Number(_st["musicgen-ready"]||0),status:document.getElementById("musicgen-status").textContent,handles:_gb.filter(Boolean).length})');
  ok('the four real medium-model files open through the UI',loaded&&state.ready===1&&state.handles>0,JSON.stringify(state));
  await evalIn('document.getElementById("musicgen-close").click();1');
  const closed=await evalIn('({ready:Number(_st["musicgen-ready"]||0),status:document.getElementById("musicgen-status").textContent,handles:_gb.filter(Boolean).length})');
  ok('Unload closes the engine and releases its handles',closed.ready===0&&closed.handles===0&&closed.status.includes('unloaded'),JSON.stringify(closed));
  await evalIn('document.getElementById("musicgen-close").click();1');
  ok('repeated Unload is safe',await evalIn('Number(_st["musicgen-ready"]||0)===0&&_gb.filter(Boolean).length===0'));
  ok('the page threw nothing',errors.length===0,errors.join(' | '));

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
