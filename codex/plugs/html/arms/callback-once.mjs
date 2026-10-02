// plugs-backlog 2.107: every asynchronous html runtime function that answers
// through a callback must run that callback once. A promise's .catch that calls
// the callback again runs a callback that threw a second time, with an error.
// The compiled page codex/plugs/html/arms/CallbackOnceArm.codex carries the
// eight functions; this arm calls each in headless Edge over stand-in fetch and
// WebGPU objects: a callback that throws on success, a rejected promise, and
// (for the GPU transfers) a throw inside the runtime's own work, each of which
// must reach the callback exactly once.
// Usage: node codex/plugs/html/arms/callback-once.mjs   Exit 0 = every arm passed.
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

// Runs in the page. Each case installs its stand-ins, calls the runtime function
// with a counting callback, waits for the promises to settle, and reports.
const inPage = `(async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  window.addEventListener('unhandledrejection', e => e.preventDefault());
  if (!document.querySelector('#c')) { const c = document.createElement('canvas'); c.id = 'c'; document.body.appendChild(c); }
  const buf = (bytes, o) => ({ mapAsync: () => o.mapFail ? Promise.reject(new Error('map failed')) : Promise.resolve(),
    getMappedRange: () => { if (o.rangeFail) throw new Error('range failed'); return new ArrayBuffer(bytes); }, unmap() {}, destroy() {} });
  const dev = (bytes, o) => ({ createBuffer: () => buf(bytes, o), createCommandEncoder: () => ({ copyBufferToBuffer() {}, finish() {} }), queue: { submit() {}, writeBuffer() {} } });
  const gpu = (adapter) => { _gopening=null;_gd=null;_gad=null;Object.defineProperty(navigator, 'gpu', { value: { requestAdapter: adapter }, configurable: true }); };
  const adapter = () => Promise.resolve({ limits: { maxBufferSize: 4, maxStorageBufferBindingSize: 4 }, info: { vendor: 'v', architecture: 'a', description: 'd' }, features: new Set(), requestDevice: () => Promise.resolve({ limits: { maxBufferSize: 4, maxStorageBufferBindingSize: 4 } }) });
  const file = (n, fail) => ({ slice: () => ({ arrayBuffer: () => fail ? Promise.reject(new Error('read failed')) : Promise.resolve(new ArrayBuffer(n)) }) });
  const directory = (fail, inner) => { _gdir={getFileHandle:(_name,opts)=>!opts?.create?Promise.reject(Object.assign(new Error('absent'),{name:'NotFoundError'})):Promise.resolve({createWritable:()=>fail?Promise.reject(new Error('writer rejected')):Promise.resolve({write(){if(inner)throw new Error('write threw')},close(){}}),getFile:()=>Promise.resolve({size:8})})}; };
  const memory = alloc_bytes(8n);
  const cases = [
    ['gpu_trim_then', 'success', () => { _gpend=null;_gpool={};_gd={queue:{onSubmittedWorkDone:()=>Promise.resolve()}}; }, cb => gpu_trim_then(cb)],
    ['gpu_trim_then', 'rejected', () => { _gpend=null;_gpool={};_gd={queue:{onSubmittedWorkDone:()=>Promise.reject(new Error('queue error'))}}; }, cb => gpu_trim_then(cb)],
    ['gpu_trim_then', 'inner throw', () => { _gpend=null;_gpool={};_gd={queue:{onSubmittedWorkDone:()=>{throw new Error('queue error')}}}; }, cb => gpu_trim_then(cb)],
    ['fetch_then', 'success', () => { window.fetch = () => Promise.resolve({ json: () => Promise.resolve({ a: 1 }) }); }, cb => fetch_then('u', 'GET', '', cb)],
    ['fetch_then', 'rejected', () => { window.fetch = () => Promise.reject(new Error('net')); }, cb => fetch_then('u', 'GET', '', cb)],
    ['fetch_with_then', 'success', () => { window.fetch = () => Promise.resolve({ text: () => Promise.resolve('t') }); }, cb => fetch_with_then('u', 'GET', '', '', cb)],
    ['fetch_with_then', 'rejected', () => { window.fetch = () => Promise.reject(new Error('net')); }, cb => fetch_with_then('u', 'GET', '', '', cb)],
    ['gpu_open_then', 'success', () => gpu(adapter), cb => gpu_open_then(cb)],
    ['gpu_open_then', 'rejected', () => gpu(() => Promise.reject(new Error('adapter'))), cb => gpu_open_then(cb)],
    ['gpu_probe_then', 'success', () => gpu(() => Promise.resolve(null)), cb => gpu_probe_then(0, cb)],
    ['gpu_probe_then', 'rejected', () => gpu(() => Promise.reject(new Error('adapter'))), cb => gpu_probe_then(0, cb)],
    ['file_save_bytes_then', 'success', () => directory(false,false), cb => file_save_bytes_then('a.bin',memory,8,cb)],
    ['file_save_bytes_then', 'rejected', () => directory(true,false), cb => file_save_bytes_then('a.bin',memory,8,cb)],
    ['file_save_bytes_then', 'inner throw', () => directory(false,true), cb => file_save_bytes_then('a.bin',memory,8,cb)],
    ['gpu_buf_download_then', 'success', () => { _gd = dev(8, {}); _gb[1] = {}; }, cb => gpu_buf_download_then(1, 0, 8, cb)],
    ['gpu_buf_download_then', 'rejected', () => { _gd = dev(8, { mapFail: 1 }); _gb[1] = {}; }, cb => gpu_buf_download_then(1, 0, 8, cb)],
    ['gpu_buf_download_then', 'inner throw', () => { _gd = dev(8, { rangeFail: 1 }); _gb[1] = {}; }, cb => gpu_buf_download_then(1, 0, 8, cb)],
    ['gpu_buf_upload_then', 'success', () => { _gd = dev(8, {}); _gb[1] = {}; _gfiles.f = file(8); }, cb => gpu_buf_upload_then(1, 0, 'f', 0, 2, 1, 1, cb)],
    ['gpu_buf_upload_then', 'rejected', () => { _gd = dev(8, {}); _gb[1] = {}; _gfiles.f = file(8, 1); }, cb => gpu_buf_upload_then(1, 0, 'f', 0, 2, 1, 1, cb)],
    ['gpu_buf_upload_then', 'inner throw', () => { _gd = dev(8, {}); _gb[1] = {}; _gfiles.f = file(4); }, cb => gpu_buf_upload_then(1, 0, 'f', 0, 2, 1, 1, cb)],
    ['gpu_buf_show_then', 'success', () => { _gd = dev(12, {}); _gb[1] = {}; }, cb => gpu_buf_show_then(1, 1, 1, '#c', cb)],
    ['gpu_buf_show_then', 'rejected', () => { _gd = dev(12, { mapFail: 1 }); _gb[1] = {}; }, cb => gpu_buf_show_then(1, 1, 1, '#c', cb)],
    ['gpu_buf_show_then', 'inner throw', () => { _gd = dev(12, { rangeFail: 1 }); _gb[1] = {}; }, cb => gpu_buf_show_then(1, 1, 1, '#c', cb)],
  ];
  const out = [];
  {
    const previous={name:'previous'};
    const entry={name:'selected',async *entries(){yield ['x.txt',{kind:'file',getFile:async()=>({name:'x.txt',size:1})}];}};
    const empty={name:'empty',async *entries(){}};
    const broken={name:'broken',async *entries(){throw new Error('read failed');}};
    for(const [label,picker,expected,selected] of [
      ['selected',()=>Promise.resolve(entry),'',entry],
      ['cancel',()=>Promise.reject(new DOMException('cancelled','AbortError')),'AbortError',previous],
      ['permission',()=>Promise.reject(new DOMException('denied','NotAllowedError')),'NotAllowedError',previous],
      ['sync throw',()=>{throw new DOMException('blocked','SecurityError')},'SecurityError',previous],
      ['empty',()=>Promise.resolve(empty),'EmptyDirectory',previous],
      ['walk error',()=>Promise.resolve(broken),'Error',previous],
      ['unsupported',undefined,'NotSupportedError',previous]
    ]){
      _gdir=previous;window.showDirectoryPicker=picker;let calls=0,payload;
      try{dir_open_then(r=>{calls++;payload=JSON.parse(r);throw new Error('callback threw');});}catch{}
      await sleep(20);
      const pass=calls===1&&_gdir===selected&&(expected?payload?.code===expected:Array.isArray(payload)&&payload[0]?.path==='selected/x.txt');
      out.push({fn:'dir_open_then '+label,kind:'success',calls,got:pass?'ok':'error '+JSON.stringify(payload)});
    }
    const original=document.createElement;
    try{
      for(const fn of [pick_file_then,pick_dir_then])for(const mode of ['selected','cancel','empty','throw']){
        let input,calls=0,payload;
        document.createElement=function(tag){
          if(tag!=='input')return original.call(document,tag);
          input={files:mode==='selected'?[{name:'x.txt',webkitRelativePath:'folder/x.txt',size:1}]:[],click(){
            if(mode==='throw')throw new DOMException('blocked','SecurityError');
            const change=this.onchange,cancel=this.oncancel;
            if(mode==='cancel')cancel();else change();
            if(cancel)cancel();if(change)change();
          }};return input;
        };
        fn('',r=>{calls++;payload=JSON.parse(r)});
        const selected=fn===pick_file_then?payload?.file==='x.txt':payload?.[0]?.path==='folder/x.txt';
        const pass=calls===1&&input.onchange===null&&input.oncancel===null&&(mode==='selected'?selected:payload?.code===(mode==='throw'?'SecurityError':'AbortError'));
        out.push({fn:fn.name+' '+mode,kind:'success',calls,got:pass?'ok':'error '+JSON.stringify(payload)});
      }
    }finally{document.createElement=original;}
  }
  {
    let complete, destroyed=0, activeDestroyed=0, lateDestroyed=0, calls=0;
    const pooled={destroy(){destroyed++;}}, active={destroy(){activeDestroyed++;}}, late={destroy(){lateDestroyed++;}};
    _gpend=null;_gpool={4:[pooled]};_gb[99]=active;
    _gd={queue:{onSubmittedWorkDone:()=>new Promise(r=>{complete=r;})}};
    gpu_trim_then(()=>{calls++;});
    const detached=Object.values(_gpool).flat().length===0 && destroyed===0;
    _gpool={4:[late]};complete();await sleep(10);
    out.push({fn:'gpu_trim_then snapshot and live ownership',kind:'success',calls,got:detached&&destroyed===1&&activeDestroyed===0&&lateDestroyed===0&&_gb[99]===active&&_gpool[4][0]===late?'ok':'error ownership'});
    _gb[99]=null;_gpool={};_gd=null;
    let absent=0;gpu_trim_then(()=>{absent++;});await sleep(10);
    out.push({fn:'gpu_trim_then without device',kind:'success',calls:absent,got:'ok'});
  }
  for (const [fn, kind, setup, call] of cases) {
    let calls = 0, got = null;
    try {
      setup();
      call(r => { calls++; got = String(r); if (kind === 'success') throw new Error('the callback threw'); return 0; });
    } catch (e) { got = 'SYNC ' + String(e && e.message || e); }
    await sleep(150);
    out.push({ fn, kind, calls, got });
  }
  return JSON.stringify(out);
})()`;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'callback-once-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'CallbackOnceArm.codex'), '-Out', join(work, 'cba.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'cba.codex'), '-Out', join(work, 'cba.html')]);
  const html = readFileSync(join(work, 'cba.html'), 'utf8');

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
  let id = 0; const waiting = new Map();
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => { const m = JSON.parse(ev.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } });
  await send('Page.enable');
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.armed) === 1')) break; } catch {} }
  const present = await evalIn('["fetch_then","fetch_with_then","gpu_open_then","gpu_probe_then","gpu_buf_download_then","gpu_buf_upload_then","gpu_buf_show_then","file_save_bytes_then","dir_open_then","pick_file_then","pick_dir_then"].filter(n => typeof window[n] !== "function").join(",")');
  ok('the page carries all tested functions', present === '', present ? `missing ${present}` : '');
  const results = JSON.parse(await evalIn(inPage));
  for (const r of results) {
    const isErr = /err/i.test(r.got || '');
    // A success case must reach the success payload: an error there means the stand-ins, not the callback, failed.
    const want = r.kind === 'success' ? (r.calls === 1 && (!isErr || /no-adapter/.test(r.got))) : (r.calls === 1 && isErr);
    ok(`${r.fn}, ${r.kind}: the callback runs once${r.kind === 'success' ? '' : ' with an error'}`, want, `calls ${r.calls}, ${String(r.got).slice(0, 90)}`);
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
