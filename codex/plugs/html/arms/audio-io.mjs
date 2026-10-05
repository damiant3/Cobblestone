import { readFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../../..');
const supplied = process.argv[2] && !process.argv[2].startsWith('--') ? process.argv[2] : null;
const reuseOnly = process.argv.includes('--reuse-only');
const work = mkdtempSync(join(tmpdir(), 'html-audio-io-')), pause = ms => new Promise(r => setTimeout(r, ms));
let server, edge, socket;
try {
  let htmlPath = supplied;
  if (!htmlPath) {
    execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'build/bundle-app.ps1'), '-Src', join(repo, 'codex/plugs/html/arms/AudioIoArm.codex'), '-Out', join(work, 'arm.codex')], { stdio: 'inherit' });
    htmlPath = join(work, 'arm.html');
    execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex/plugs/html/run.ps1'), '-Src', join(work, 'arm.codex'), '-Out', htmlPath, '-Compiler', join(repo, 'seed/Codex.cdx')], { stdio: 'inherit' });
  }
  const html = readFileSync(htmlPath, 'utf8');
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--enable-unsafe-webgpu', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`Audio IO browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try { const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]; target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
    if (!target) await pause(250);
  }
  if (!target) throw new Error('Browser startup timed out');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { socket.addEventListener('open', r); socket.addEventListener('error', j); });
  const pending = new Map(); let sequence = 0;
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout')); }, 60000);
    pending.set(id, reply => { clearTimeout(timer); reply.error || reply.result?.exceptionDetails ? reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))) : resolve(reply.result); });
    socket.send(JSON.stringify({ id, method, params }));
  });
  socket.addEventListener('message', event => { const r = JSON.parse(event.data); if (pending.has(r.id)) { pending.get(r.id)(r); pending.delete(r.id); } });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result?.value;
  await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });
  for (let i = 0; i < 120; i++) { if (await evaluate("typeof gpu_open_then === 'function'")) break; await pause(250); }
  const reuse = await evaluate(`(async()=>{let requests=0;const original=GPUAdapter.prototype.requestDevice;GPUAdapter.prototype.requestDevice=function(){return Promise.reject(new Error('injected open refusal'))};
    const refused=JSON.parse(await new Promise(resolve=>gpu_open_then(resolve)));GPUAdapter.prototype.requestDevice=function(...a){requests++;return original.apply(this,a)};let h;
    const first=new Promise(resolve=>gpu_open_then(r=>{const s=JSON.parse(r);if(s.ok){h=gpu_buf_alloc(16n);gpu_buf_write_words(h,0n,[11n,22n,33n,44n])}resolve(s)}));
    const second=new Promise(resolve=>gpu_open_then(r=>resolve(JSON.parse(r))));const opens=await Promise.all([first,second]);
    const third=JSON.parse(await new Promise(resolve=>gpu_open_then(resolve)));const read=await new Promise(resolve=>gpu_buf_download_then(h,0n,16n,resolve));gpu_buf_free(h);
    GPUAdapter.prototype.requestDevice=original;return {refused,requests,opens,third,read,error:gpu_error(0n)}})()`);
  console.log(JSON.stringify(reuse));
  if (reuse.refused.ok !== false || !reuse.refused.err?.includes('injected open refusal') || reuse.requests !== 1 || reuse.opens.some(s => !s.ok) || !reuse.third.ok || reuse.read !== '[11,22,33,44]' || reuse.error) throw new Error('WebGPU device reuse/retry failed');
  if (reuseOnly) { console.log('PASS: concurrent/repeated opens preserve one device and existing buffers'); }
  else {
    await evaluate("navigator.storage.getDirectory().then(r=>r.getDirectoryHandle('AudioIo',{create:true})).then(d=>{_gdir=d;return true})");
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    let done = false; for (let i = 0; i < 120 && !done; i++) { done = await evaluate('Number(_st.done||0)===1'); if (!done) await pause(250); }
    if (!done) throw new Error('Compiled byte-save arm did not finish');
    const saved = JSON.parse(await evaluate('String(_st.save)'));
    const want = [0, 255, 65, 13, 10, 200, 0, 7];
    if (!saved.ok || saved.size !== 8 || await evaluate('String(_st.read)') !== JSON.stringify(want)) throw new Error('Compiled binary save/readback differs');
    const disk = () => evaluate("_gdir.getFileHandle('probe.bin').then(h=>h.getFile()).then(f=>f.arrayBuffer()).then(b=>Array.from(new Uint8Array(b)))");
    if (JSON.stringify(await disk()) !== JSON.stringify(want)) throw new Error('Persisted binary bytes differ');
    const overwrite = JSON.parse(await evaluate("(async()=>{poke_byte(_st.address,0n,123n);return await new Promise(r=>file_save_bytes_then('probe.bin',_st.address,8n,r))})()"));
    if (!overwrite.error || JSON.stringify(await disk()) !== JSON.stringify(want)) throw new Error('Overwrite was not refused safely');
    const snapshot = JSON.parse(await evaluate("(async()=>{poke_byte(_st.address,0n,77n);const saved=new Promise(r=>file_save_bytes_then('snapshot.bin',_st.address,8n,r));poke_byte(_st.address,0n,88n);return await saved})()"));
    const snapBytes = await evaluate("_gdir.getFileHandle('snapshot.bin').then(h=>h.getFile()).then(f=>f.arrayBuffer()).then(b=>Array.from(new Uint8Array(b)))");
    if (!snapshot.ok || JSON.stringify(snapBytes) !== JSON.stringify([77, ...want.slice(1)])) throw new Error('Save did not snapshot source memory at call');
    const concurrent = await evaluate("(async()=>{poke_byte(_st.address,0n,17n);const a=new Promise(r=>file_save_bytes_then('race.bin',_st.address,8n,r));poke_byte(_st.address,0n,18n);const b=new Promise(r=>file_save_bytes_then('race.bin',_st.address,8n,r));return (await Promise.all([a,b])).map(JSON.parse)})()");
    const raceBytes = await evaluate("_gdir.getFileHandle('race.bin').then(h=>h.getFile()).then(f=>f.arrayBuffer()).then(b=>Array.from(new Uint8Array(b)))");
    if (!concurrent[0].ok || !concurrent[1].error || JSON.stringify(raceBytes) !== JSON.stringify([17, ...want.slice(1)])) throw new Error('Concurrent same-path saves overwrote data');
    const capturedDir = await evaluate("(async()=>{const first=_gdir;const other=await (await navigator.storage.getDirectory()).getDirectoryHandle('Other',{create:true});const saved=new Promise(r=>file_save_bytes_then('captured.bin',_st.address,8n,r));_gdir=other;try{const result=JSON.parse(await saved);const inFirst=await first.getFileHandle('captured.bin').then(()=>true,()=>false);const inOther=await other.getFileHandle('captured.bin').then(()=>true,()=>false);return {result,inFirst,inOther}}finally{_gdir=first}})()");
    if (!capturedDir.result.ok || !capturedDir.inFirst || capturedDir.inOther) throw new Error('Save did not capture the selected directory');
    for (const [path, addr] of [['../bad.bin', '_st.address'], ['bad\\file.bin', '_st.address'], ['bad.bin', 'BigInt(_hp+1)']]) {
      const result = JSON.parse(await evaluate(`new Promise(r=>file_save_bytes_then(${JSON.stringify(path)},${addr},8n,r))`));
      if (!result.error) throw new Error('Invalid path/range was not refused');
    }
    const absent = await evaluate("_gdir.getFileHandle('bad.bin').then(()=>false,e=>e.name==='NotFoundError')");
    if (!absent) throw new Error('Refused save created a file');
    const end = JSON.parse(await evaluate("new Promise(r=>file_save_bytes_then('end.bin',_st.address,BigInt(_hp-Number(_st.address)+1),r))"));
    if (!end.error || !(await evaluate("_gdir.getFileHandle('end.bin').then(()=>false,e=>e.name==='NotFoundError')"))) throw new Error('Range end was not refused');
    const noFolder = JSON.parse(await evaluate("(async()=>{const d=_gdir;_gdir=null;try{return await new Promise(r=>file_save_bytes_then('none.bin',_st.address,8n,r))}finally{_gdir=d}})()"));
    if (!noFolder.error || !(await evaluate("_gdir.getFileHandle('none.bin').then(()=>false,e=>e.name==='NotFoundError')"))) throw new Error('Missing directory was not refused');
    console.log('PASS: one WebGPU device; exact binary bytes; overwrite, path and range refusals');
  }
} finally {
  if (socket) socket.close();
  if (edge) execFileSync('pwsh', ['-NoProfile', '-Command', `Stop-Process -Id ${edge.pid} -Force -ErrorAction SilentlyContinue;Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`], { stdio: 'ignore' });
  if (server) server.close();
}
