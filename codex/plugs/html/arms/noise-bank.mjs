import {readFileSync,mkdtempSync,writeFileSync} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {spawn,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import http from 'node:http';

const [pagePath,wasmPath,evidenceDir]=process.argv.slice(2);
if(!evidenceDir)throw new Error('Usage: noise-bank.mjs arm.html noise.wasm native-evidence-directory');
const hash=b=>createHash('sha256').update(b).digest('hex');
const evidence=JSON.parse(readFileSync(join(evidenceDir,'evidence.json'),'utf8'));
const wasm=readFileSync(wasmPath),html=readFileSync(pagePath,'utf8');
if(hash(wasm)!==evidence.wasmSha256||hash(readFileSync(join(evidenceDir,'native.cdx')))!==evidence.nativeCdxSha256)throw new Error('Noise provenance differs');
if(hash(readFileSync(join(evidenceDir,'native-source.codex')))!==evidence.nativeBundledSha256||hash(readFileSync(join(evidenceDir,'noise.codex')))!==evidence.wasmBundledSha256)throw new Error('Bundled source provenance differs');
const work=mkdtempSync(join(tmpdir(),'noise-bank-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
let edge,server,socket;const results=[];
try{
  server=http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html.replace('<script>',`<script>window.__DATA=${JSON.stringify({'sdxl-noise':wasm.toString('base64')})};</script><script>`));});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--no-first-run','about:blank'],{stdio:'ignore'});
  let target;for(let i=0;i<120&&!target;i++){try{const port=readFileSync(join(work,'profile/DevToolsActivePort'),'utf8').split('\n')[0];target=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{}if(!target)await pause(250);}
  if(!target)throw new Error('Browser startup failed');
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((r,j)=>{socket.onopen=r;socket.onerror=j;});
  let id=0;const pending=new Map();socket.onmessage=e=>{const m=JSON.parse(e.data);if(pending.has(m.id)){pending.get(m.id)(m);pending.delete(m.id);}};
  const send=(method,params={})=>new Promise((r,j)=>{const key=++id,t=setTimeout(()=>{pending.delete(key);j(new Error('CDP timeout '+method));},120000);pending.set(key,m=>{clearTimeout(t);m.error||m.result?.exceptionDetails?j(new Error(JSON.stringify(m.error||m.result.exceptionDetails))):r(m.result);});socket.send(JSON.stringify({id:key,method,params}));});
  const evaluate=async expression=>(await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true})).result?.value;
  await send('Page.enable');await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});
  for(let i=0;i<120;i++){try{if(await evaluate("typeof _st!=='undefined'&&Number(_st.armed)===1"))break;}catch{}await pause(250);}
  const opened=await evaluate(`new Promise(r=>gpu_open_then(s=>r(JSON.parse(s))))`);if(!opened.ok)throw new Error(JSON.stringify(opened));
  await evaluate(`window.__workers={made:0,stopped:0};const BaseWorker=Worker;window.Worker=class extends BaseWorker{constructor(...a){super(...a);__workers.made++;}terminate(){__workers.stopped++;return super.terminate();}};window.__noiseErrors=[];addEventListener('error',e=>{if(String(e.message).includes('expected callback throw'))e.preventDefault();else __noiseErrors.push(e.message);});`);
  const baseline=await evaluate('_hp');
  const draw=async(seed,n,kind,steps,key='sdxl-noise',throws=false,cancel=false)=>evaluate(`new Promise(resolve=>{let calls=0;gpu_noise_bank_then(${JSON.stringify(key)},${seed}n,${n}n,${kind}n,${steps}n,()=>${cancel?'-1n':'0n'},s=>{calls++;const result=JSON.parse(s);setTimeout(()=>resolve({result,calls,live:_noiseJobs.size,heap:_hp,workers:{...__workers}}),30);if(${throws})throw new Error('expected callback throw');});})`);
  const download=async(h,bytes)=>Buffer.from(await evaluate(`new Promise((resolve,reject)=>gpu_buf_download_then(${h},0,${bytes},s=>{try{const words=JSON.parse(s);if(!Array.isArray(words)||words.length!==${bytes/4}||words.some(v=>!Number.isInteger(v)||v < -2147483648||v > 2147483647))throw new Error('bad words');const b=new Uint8Array(new Int32Array(words).buffer);let text='';for(let i=0;i<b.length;i+=8192)text+=String.fromCharCode(...b.subarray(i,i+8192));resolve(btoa(text));}catch(e){reject(e);}}))`),'base64');
  for(const row of evidence.rows){
    const native=readFileSync(join(evidenceDir,row.file));if(hash(native)!==row.sha256)throw new Error('Native bytes changed');
    const t=performance.now(),got=await draw(row.seed,row.n,row.kind,row.steps);
    if(!got.result.ok||got.calls!==1||got.live!==0||got.heap!==baseline||got.workers.made!==got.workers.stopped)throw new Error('Worker/arena contract differs: '+JSON.stringify(got));
    const bytes=await download(got.result.handle,row.bytes);if(!bytes.equals(native))throw new Error('GPU noise bytes differ: '+row.file);
    await evaluate(`gpu_buf_free(${got.result.handle})`);
    const result={file:row.file,ms:performance.now()-t,arenaBytes:got.result.arenaBytes,bytes:bytes.length,sha256:hash(bytes)};results.push(result);console.log('EXACT '+JSON.stringify(result));
  }
  for(let i=0;i<8;i++){const got=await draw(7201,256,1,6);if(!got.result.ok||got.heap!==baseline||got.live||got.workers.made!==got.workers.stopped)throw new Error('Repeated arena retained');await evaluate(`gpu_buf_free(${got.result.handle})`);}
  const thrown=await draw(7201,256,1,6,'sdxl-noise',true);if(!thrown.result.ok||thrown.calls!==1||thrown.live)throw new Error('Throwing callback repeated');await evaluate(`gpu_buf_free(${thrown.result.handle})`);
  const empty=await draw(7201,256,1,1);if(!empty.result.ok||empty.result.bytes!==0||empty.result.handle!==0)throw new Error('Terminal schedule bank differs');
  const before=await evaluate(`new Promise(resolve=>{const made=__workers.made;let calls=0;bd_prepare_bank(1n,7201n,256n,6n,[0n],()=>-1n,s=>{calls++;resolve({s,calls,made,after:__workers.made,live:_noiseJobs.size});});})`);
  if(before.s!=='SDXL generation cancelled'||before.calls!==1||before.made!==before.after||before.live)throw new Error('Prelaunch cancellation differs');
  const cancelStart=performance.now(),cancelled=await draw(7201,65536,1,50,'sdxl-noise',false,true);
  if(cancelled.result.ok||!cancelled.result.cancelled||cancelled.calls!==1||cancelled.live||cancelled.workers.made!==cancelled.workers.stopped||performance.now()-cancelStart>2000)throw new Error('Active cancellation failed to release worker');
  for(const event of ['onerror','onmessageerror']){const got=await evaluate(`new Promise(resolve=>{let calls=0;gpu_noise_bank_then('sdxl-noise',7201n,65536n,1n,50n,()=>0n,s=>{calls++;resolve({result:JSON.parse(s),calls,live:_noiseJobs.size});});[..._noiseJobs.values()][0][${JSON.stringify(event)}]({preventDefault(){},message:'injected worker error'});})`);if(got.result.ok||got.calls!==1||got.live)throw new Error('Worker failure retained arena');}
  await evaluate(`window.__allocate=gpu_buf_alloc;gpu_buf_alloc=()=>0;`);const allocation=await draw(7201,256,1,6);await evaluate(`gpu_buf_alloc=__allocate;`);if(allocation.result.ok||allocation.calls!==1||allocation.live)throw new Error('Allocation refusal cleanup differs');
  await evaluate(`window.__writeBuffer=_gd.queue.writeBuffer;_gd.queue.writeBuffer=()=>{throw new Error('injected upload refusal')};`);const upload=await draw(7201,256,1,6);await evaluate(`_gd.queue.writeBuffer=__writeBuffer;`);if(upload.result.ok||upload.calls!==1||upload.live)throw new Error('Upload refusal cleanup differs');
  for(const args of [[7201,255,1,6],[7201,256,2,6],[7201,256,1,0],[7201,256,1,6,'missing']]){const got=await draw(...args);if(got.result.ok||got.calls!==1||got.live)throw new Error('Refusal leaked worker');}
  await evaluate(`window.__DATA['broken-noise']='AA==';`);const broken=await draw(7201,256,1,6,'broken-noise');if(broken.result.ok||broken.calls!==1||broken.live)throw new Error('Bad module accepted or retained');
  writeFileSync(join(work,'bad.wat'),'(module (memory (export "memory") 1) (func (export "_start")) (func (export "__heap_reset")) (func (export "noise_bank") (param i64 i64 i64 i64) (result i64) (i64.const 16)))');
  execFileSync('wat2wasm',[join(work,'bad.wat'),'-o',join(work,'bad.wasm')]);
  await evaluate(`window.__DATA['bad-shape']=${JSON.stringify(readFileSync(join(work,'bad.wasm')).toString('base64'))};`);
  const shape=await draw(7201,256,1,6,'bad-shape');if(shape.result.ok||shape.calls!==1||shape.live)throw new Error('Bad bank shape accepted or retained');
  await evaluate(`window.__realTimeout=setTimeout;window.setTimeout=(fn,ms,...a)=>__realTimeout(fn,ms===120000?0:ms,...a);`);
  const timeout=await draw(7201,65536,1,6);await evaluate(`window.setTimeout=__realTimeout;`);if(timeout.result.ok||timeout.calls!==1||timeout.live||!timeout.result.error.includes('timed out'))throw new Error('Timeout failed to release worker');
  const copy=await evaluate(`new Promise((resolve,reject)=>{const a=gpu_buf_alloc(16),b=gpu_buf_alloc(16);gpu_buf_write_words(a,0,[0x80000000n,0x7fc01234n,1n,0xffffffffn]);const ok=gpu_buf_copy_range(a,0,b,0,16),refused=!gpu_buf_copy_range(a,1,b,0,4)&&!gpu_buf_copy_range(a,0,a,0,4)&&!gpu_buf_copy_range(a,0,b,0,20);gpu_buf_download_then(b,0,16,s=>{gpu_buf_free(a);gpu_buf_free(b);resolve({ok,refused,words:JSON.parse(s)});});})`);
  if(!copy.ok||!copy.refused||JSON.stringify(copy.words)!==JSON.stringify([-2147483648,2143294004,1,-1]))throw new Error('Bit copy/control differs: '+JSON.stringify(copy));
  const end=await evaluate(`({heap:_hp,live:_noiseJobs.size,active:_gb.filter(Boolean).length,workers:__workers,errors:__noiseErrors,gpuError:_gerr})`);
  if(end.heap!==baseline||end.live||end.active||end.workers.made!==end.workers.stopped||end.errors.length||end.gpuError)throw new Error('Lifetime/error check failed: '+JSON.stringify(end));
  writeFileSync(join(dirname(resolve(pagePath)),'noise-browser-evidence.json'),JSON.stringify({pageSha256:hash(Buffer.from(html)),wasmSha256:hash(wasm),results,end},null,2)+'\n');
  console.log('PASS: exact native GPU noise bytes, repeated arena release, callback/refusal controls and raw bit copies');
}finally{
  if(socket)socket.close();if(edge&&edge.exitCode===null)edge.kill();
  execFileSync('pwsh',['-NoProfile','-Command',`function Owned { @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*' }) };foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup did not finish'`],{stdio:'inherit'});
  if(server)server.close();
}
