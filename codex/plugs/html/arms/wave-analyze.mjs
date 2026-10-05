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
  work = mkdtempSync(join(tmpdir(), 'wave-analyze-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'WaveAnalyzeArm.codex'), '-Out', join(work, 'dpa.codex')]);
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
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; }
  const chunk=(tag,data)=>{const b=Buffer.alloc(8+data.length+(data.length&1));b.write(tag);b.writeUInt32LE(data.length,4);data.copy(b,8);return b};
  const wave=(values,float,ch=1)=>{
    const fmt=Buffer.alloc(float?18:16);fmt.writeUInt16LE(float?3:1,0);fmt.writeUInt16LE(ch,2);fmt.writeUInt32LE(32000,4);fmt.writeUInt32LE(32000*ch*(float?4:2),8);fmt.writeUInt16LE(ch*(float?4:2),12);fmt.writeUInt16LE(float?32:16,14);
    const data=Buffer.alloc(values.length*(float?4:2));values.forEach((v,i)=>float?data.writeFloatLE(v,i*4):data.writeInt16LE(v*32768,i*2));
    const fact=Buffer.alloc(4);fact.writeUInt32LE(values.length/ch);const body=Buffer.concat([Buffer.from('WAVE'),chunk('fmt ',fmt),chunk('JUNK',Buffer.from([1,2,3])),...(float?[chunk('fact',fact)]:[]),chunk('data',data)]);
    return Buffer.concat([Buffer.from('RIFF'),Buffer.from(Uint32Array.of(body.length).buffer),body]);
  };
  const values=Array.from({length:8192},(_,i)=>Math.round(Math.sin(i*Math.PI/32)*16384)/32768);
  const analyze=async bytes=>{await evalIn(`_gfiles['probe.wav']=new File([new Uint8Array(${JSON.stringify([...bytes])})],'probe.wav');_st.done=0n;document.getElementById('analyze').click();1`);for(let i=0;i<120;i++){if(await evalIn('Number(_st.done)===1'))return JSON.parse(await evalIn('_st.analysis'));await sleep(100)}throw Error('Analysis callback timed out')};
  const pcm=await analyze(wave(values,false)),float=await analyze(wave(values,true));
  ok('compiled analysis callback accepts PCM16 and MusicGen-style float32 chunks',pcm.ok===true&&float.ok===true&&pcm.sampleRate===32000&&pcm.duration===8192/32000);
  ok('equivalent samples produce identical complete analysis',JSON.stringify(pcm)===JSON.stringify(float));
  const stereo=values.flatMap(v=>[v/2,v*1.5]);
  ok('float32 stereo mixing preserves the equivalent mono analysis',JSON.stringify(await analyze(wave(stereo,true,2)))===JSON.stringify(pcm));
  const extremes=wave([-1.25,-0.5,0,0.25,1.5],true);
  ok('float32 reads preserve values beyond unit amplitude',await evalIn(`JSON.stringify(Array.from(_gwav(new Uint8Array(${JSON.stringify([...extremes])})).raw))==='[-1.25,-0.5,0,0.25,1.5]'`));
  const badMagic=Buffer.from(extremes);badMagic.write('NOPE');
  const badBits=Buffer.from(extremes);badBits.writeUInt16LE(64,34);
  const badRate=Buffer.from(extremes);badRate.writeUInt32LE(0,24);
  const badAlign=Buffer.from(extremes);badAlign.writeUInt16LE(2,32);
  const shortFmt=Buffer.from(extremes);shortFmt.writeUInt32LE(8,16);
  for(const [label,bytes] of [['magic',badMagic],['bits',badBits],['rate',badRate],['alignment',badAlign],['short fmt',shortFmt],['truncated',extremes.subarray(0,extremes.length-1)],['empty',wave([],true)],['nonfinite',wave([NaN],true)]]){
    const result=await analyze(bytes);ok(label+' is refused through the callback',!result.ok&&typeof result.error==='string');
  }
  ok('the page throws no exception',errors.length===0,errors.join(' | '));
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
