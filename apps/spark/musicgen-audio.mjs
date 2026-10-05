import { readFileSync, writeFileSync, mkdtempSync, createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const [oraclePath, htmlPath, basePath, textPath, modelPath, audioPath] = process.argv.slice(2);
if (!audioPath) throw new Error('Usage: node apps/spark/musicgen-audio.mjs oracle.json arm.html base.wgsl text.wgsl model.wgsl audio.wgsl');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8'));
if (oracle.torch !== '2.1.0+cu121' || oracle.device !== 'NVIDIA GeForce RTX 4060 Ti' || oracle.dtype !== 'float32' || oracle.tf32 !== false || oracle.sample_rate !== 32000 || oracle.channels !== 1 || oracle.top_k !== 250) throw new Error('Reference profile differs');
if (JSON.stringify(oracle.cases.map(c => c.name)) !== '["music","sfx","settings"]') throw new Error('Reference cases differ');
const prompts = ['A punchy electronic drum groove with a deep bass line.', 'A short metallic bell ringing once.', 'A punchy electronic drum groove with a deep bass line.'];
for (const [i,c] of oracle.cases.entries()) if(c.prompt!==prompts[i]||c.frames!==(i===2?8:50)||c.seed!==(i===2?'42':'1234')||c.temperature!==(i===2?0.8:1)||c.cfg!==(i===2?4:3)) throw new Error('Reference prompt/settings inventory differs');
for (const [name, file] of Object.entries(oracle.inputs)) {
  const hash = createHash('sha256'); for await (const bytes of createReadStream(file)) hash.update(bytes);
  if (hash.digest('hex') !== oracle.sha256[name]) throw new Error(`Input hash differs: ${name}`);
}
for (const c of oracle.cases) {
  const bytes = readFileSync(join(dirname(oraclePath), c.audio));
  if (createHash('sha256').update(bytes).digest('hex') !== c.audio_sha256 || bytes.length !== c.samples * 4 || c.samples !== c.frames * 640 || c.tokens.length !== 4 || c.tokens.some(r => r.length !== c.frames)) throw new Error('Reference shape/hash differs');
}
const html = readFileSync(htmlPath, 'utf8');
const data = { count: String(oracle.cases.length) };
for (const [name, path] of Object.entries({base:basePath,text:textPath,model:modelPath,audio:audioPath})) data[name] = readFileSync(path, 'utf8');
oracle.cases.forEach((c,i) => { for (const key of ['prompt','frames','seed']) data[key+i] = String(c[key]); data['temperature'+i] = String(Math.round(c.temperature*1000)); data['cfg'+i] = String(Math.round(c.cfg*1000)); });
console.log('Artifacts '+JSON.stringify(Object.fromEntries([['html',html],...Object.entries(data).filter(([k])=>['base','text','model','audio'].includes(k))].map(([k,v])=>[k,createHash('sha256').update(v).digest('hex')]))));
const work = mkdtempSync(join(tmpdir(), 'musicgen-audio-')), pause = ms => new Promise(r=>setTimeout(r,ms));
let server, edge, socket;
try {
  server = http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html.replace('<script>',`<script>window.__DATA=${JSON.stringify(data)};</script><script>`));});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--no-first-run','about:blank'],{stdio:'ignore'});
  console.log(`Audio browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for(let i=0;i<120&&!target;i++){try{const port=readFileSync(join(work,'profile/DevToolsActivePort'),'utf8').split('\n')[0];target=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{} if(!target)await pause(250);}
  if(!target)throw new Error('Browser startup timed out');
  socket=new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r,j)=>{socket.addEventListener('open',r);socket.addEventListener('error',j);});
  const pending=new Map(),errors=[];let sequence=0,pick=0;
  const files=['tokenizer','text','model','audio'].map(k=>oracle.inputs[k]);
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence,timer=setTimeout(()=>{pending.delete(id);reject(new Error(`CDP timeout ${method}`));},120000);pending.set(id,reply=>{clearTimeout(timer);reply.error||reply.result?.exceptionDetails?reject(new Error(JSON.stringify(reply.error||reply.result.exceptionDetails))):resolve(reply.result);});socket.send(JSON.stringify({id,method,params}));});
  socket.addEventListener('message',event=>{const r=JSON.parse(event.data);if(pending.has(r.id)){pending.get(r.id)(r);pending.delete(r.id);}if(r.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(r.params));if(r.method==='Page.fileChooserOpened')send('DOM.setFileInputFiles',{files:[files[pick++]],backendNodeId:r.params.backendNodeId}).catch(e=>errors.push(String(e)));});
  for(const domain of ['Page','DOM','Runtime'])await send(`${domain}.enable`);
  await send('Page.setInterceptFileChooserDialog',{enabled:true});
  const evaluate=async expression=>(await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true})).result?.value;
  const wait=async expression=>{let last='';for(let i=0;i<3600;i++){if(errors.length)throw new Error(errors.join('\n'));const s=await evaluate(`({ok:(${expression}),error:typeof _st==='undefined'?'':String(_st.error||''),progress:typeof _st==='undefined'?'':String(_st.case||0)+':'+String(_st.phase||'')+':'+String(_st.progress||0)})`);if(s.error)throw new Error(s.error);if(s.ok)return;if(i%40===0&&s.progress!==last){console.log(s.progress);last=s.progress;}await pause(250);}throw new Error('Audio generation timed out');};
  await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});
  await wait("typeof _st!=='undefined' && Number(_st.need)===0");
  const setup=await evaluate(`(async()=>{_gdir=await navigator.storage.getDirectory();const r=JSON.parse(await new Promise(r=>gpu_open_then(r)));if(!r.ok)return r;window.canary=gpu_buf_alloc(16n);gpu_buf_write_words(canary,0n,[11n,22n,33n,44n]);window.firstDevice=_gd;return r})()`);
  if(!setup.ok)throw new Error('Initial GPU open failed');
  await evaluate(`(()=>{const original=ea_ready;ea_ready=engine=>{const settings=ea_settings(0n);window.guardResults=[];const attempt=s=>{let calls=0;musicgen_generate(engine,'guard',s,()=>0n,()=>{throw new Error('Invalid request accepted')},why=>{guardResults.push(String(why));calls++;return 0n});if(calls!==1)throw new Error('Guard callback count differs')};attempt({...settings,mg_frames:0n});attempt({...settings,mg_temperature:NaN});_gerr='injected diagnostic error';attempt(settings);_gerr='';return original(engine)};return true})()`);
  for(let i=0;i<4;i++){await wait(`Number(_st.need)===${i}`);for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});}
  await wait('Number(_st.done)===1');
  const state=await evaluate(`(async()=>({closed:Number(_st.closed),closedError:_st['closed-error'],busyClose:Number(_st['busy-close']),busyError:_st['busy-error'],cancel:_st['cancel-error'],active:_gb.filter(Boolean).length,same:_gd===firstDevice,canary:await new Promise(r=>gpu_buf_download_then(canary,0n,16n,r))}))()`);
  console.log(JSON.stringify(state));
  const guards=await evaluate('guardResults');
  if(JSON.stringify(guards)!==JSON.stringify(['MusicGen settings are outside the supported range','MusicGen settings are outside the supported range','MusicGen GPU error: injected diagnostic error']))throw new Error('Invalid settings/GPU error guards differ');
  if(state.closed!==1||state.busyClose!==0||state.busyError!=='Audio generation is already running'||state.closedError!=='The MusicGen engine is closed'||state.cancel!=='MusicGen generation cancelled'||state.active!==1||!state.same||state.canary!=='[11,22,33,44]')throw new Error('Lifecycle/device contract differs');
  for(const [i,c] of oracle.cases.entries()){
    const tokens=JSON.parse(await evaluate(`String(_st.tokens${i})`));
    if(JSON.stringify(tokens)!==JSON.stringify(c.tokens.flat()))throw new Error(`Full-pipeline tokens differ: ${c.name}`);
    const bytes=Buffer.from(await evaluate(`(async()=>{const d=await _gdir.getDirectoryHandle('audio');const f=await(await d.getFileHandle('case${i}.wav')).getFile();return Array.from(new Uint8Array(await f.arrayBuffer()))})()`));
    writeFileSync(join(work,c.name+'.wav'),bytes);
    if(bytes.toString('ascii',0,4)!=='RIFF'||bytes.readUInt32LE(4)!==bytes.length-8||bytes.toString('ascii',8,12)!=='WAVE')throw new Error('Invalid RIFF');
    const chunks=new Map();for(let off=12;off+8<=bytes.length;){const name=bytes.toString('ascii',off,off+4),size=bytes.readUInt32LE(off+4);if(off+8+size>bytes.length||chunks.has(name))throw new Error('Invalid WAVE chunk');chunks.set(name,bytes.subarray(off+8,off+8+size));off+=8+size+(size&1);if(off===bytes.length)break;if(off+8>bytes.length)throw new Error('WAVE trailing bytes');}
    const fmt=chunks.get('fmt '),fact=chunks.get('fact'),samples=chunks.get('data');
    if(!fmt||fmt.length!==18||fmt.readUInt16LE(0)!==3||fmt.readUInt16LE(2)!==1||fmt.readUInt32LE(4)!==32000||fmt.readUInt32LE(8)!==128000||fmt.readUInt16LE(12)!==4||fmt.readUInt16LE(14)!==32||fmt.readUInt16LE(16)!==0||!fact||fact.length!==4||fact.readUInt32LE(0)!==c.samples||!samples||samples.length!==c.samples*4)throw new Error('WAVE format differs');
    const ref=readFileSync(join(dirname(oraclePath),c.audio));let maxAbs=0;
    for(let j=0;j<c.samples;j++){const a=samples.readFloatLE(j*4),b=ref.readFloatLE(j*4);if(!Number.isFinite(a)||!Number.isFinite(b))throw new Error('Nonfinite audio');maxAbs=Math.max(maxAbs,Math.abs(a-b));}
    console.log(JSON.stringify({case:c.name,exactTokens:true,samples:c.samples,maxAbs}));
    if(maxAbs>1e-4)throw new Error('Audio exceeds fixed 1e-4 absolute bound');
    const decoded=await evaluate(`(async()=>{const d=await _gdir.getDirectoryHandle('audio');const f=await(await d.getFileHandle('case${i}.wav')).getFile();const ctx=new AudioContext({sampleRate:32000});try{const b=await ctx.decodeAudioData(await f.arrayBuffer());return {samples:b.length,rate:b.sampleRate,channels:b.numberOfChannels}}finally{await ctx.close()}})()`);
    if(decoded.samples!==c.samples||decoded.rate!==32000||decoded.channels!==1)throw new Error('Browser WAVE decoding differs');
  }
  console.log('PASS: full text-to-wave reference, saved WAVE decode, cancellation/retry, busy/closed guards and shared GPU lifetime');
} finally {
  if(socket)socket.close();
  if(edge&&edge.exitCode===null)execFileSync('pwsh',['-NoProfile','-Command',`$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`],{stdio:'ignore'});
  if(server)server.close();
}
