import {readFileSync,mkdtempSync,createReadStream} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {spawn,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import http from 'node:http';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../../../..');
const [htmlPath,basePath,extraPath]=process.argv.slice(2);
if(!extraPath)throw new Error('Usage: node codex/plugs/html/arms/clip-xl.mjs arm.html BrowserKernels.wgsl BrowserClipKernels.wgsl');
const ckpt=join(repo,'build-output/diffusion-models/dreamshaperXL_lightningDPMSDE.safetensors');
const checkpointHash=createHash('sha256');for await(const bytes of createReadStream(ckpt))checkpointHash.update(bytes);
console.log('Checkpoint SHA256 '+checkpointHash.digest('hex'));
const refs=['cat','hero'].map(name=>{
  const b=readFileSync(join(repo,'codex/test/apps/clip-sdxl-ref',name+'.ref'));
  if(b.length!==308+77*2048*4+1280*4)throw new Error('Reference byte length differs');
  const ids=Array.from({length:77},(_,i)=>b.readInt32LE(i*4));
  if(ids.some(v=>v<0||v>49407)||ids[0]!==49406||!ids.includes(49407))throw new Error('Reference token IDs differ');
  const floats=(off,n)=>Array.from({length:n},(_,i)=>b.readFloatLE(off+i*4));
  const arrays=[floats(308,77*768),floats(308+77*768*4,77*1280),floats(308+77*2048*4,1280)];
  if(arrays.flat().some(v=>!Number.isFinite(v)))throw new Error('Nonfinite reference');
  console.log(`Reference ${name} SHA256 ${createHash('sha256').update(b).digest('hex')}`);
  return {name,ids,arrays,bounds:name==='cat'?[0.55182,0.0139,0.21781,0.05502,0.00672]:[0.55182,0.00832,0.21781,0.04259,0.00463]};
});
const tokenizer='D:/AI/DiffusionForge/webui/backend/huggingface/stabilityai/stable-diffusion-xl-base-1.0/tokenizer';
const html=readFileSync(htmlPath,'utf8'),data={base:readFileSync(basePath,'utf8'),extra:readFileSync(extraPath,'utf8'),cat:'a photo of a cat',hero:'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window',vocab:JSON.stringify(Array.from(readFileSync(join(tokenizer,'vocab.json')))),merges:JSON.stringify(Array.from(readFileSync(join(tokenizer,'merges.txt'))))};
console.log('Artifacts '+JSON.stringify(Object.fromEntries([['html',html],['base',data.base],['extra',data.extra]].map(([k,v])=>[k,createHash('sha256').update(v).digest('hex')]))));
const work=mkdtempSync(join(tmpdir(),'clip-xl-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
let server,edge,socket;
try{
  server=http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html.replace('<script>',`<script>window.__DATA=${JSON.stringify(data)};</script><script>`));});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--no-first-run','about:blank'],{stdio:'ignore'});
  console.log(`CLIP XL browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for(let i=0;i<120&&!target;i++){try{const port=readFileSync(join(work,'profile/DevToolsActivePort'),'utf8').split('\n')[0];target=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{}if(!target)await pause(250);}
  if(!target)throw new Error('Browser startup timed out');
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((r,j)=>{socket.addEventListener('open',r);socket.addEventListener('error',j);});
  const pending=new Map(),errors=[];let sequence=0;
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence,timer=setTimeout(()=>{pending.delete(id);reject(new Error(`CDP timeout ${method}`));},120000);pending.set(id,reply=>{clearTimeout(timer);reply.error||reply.result?.exceptionDetails?reject(new Error(JSON.stringify(reply.error||reply.result.exceptionDetails))):resolve(reply.result);});socket.send(JSON.stringify({id,method,params}));});
  socket.addEventListener('message',event=>{const r=JSON.parse(event.data);if(pending.has(r.id)){pending.get(r.id)(r);pending.delete(r.id);}if(r.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(r.params));if(r.method==='Page.fileChooserOpened')send('DOM.setFileInputFiles',{files:[ckpt],backendNodeId:r.params.backendNodeId}).catch(e=>errors.push(String(e)));});
  for(const d of ['Page','DOM','Runtime'])await send(`${d}.enable`);
  await send('Page.setInterceptFileChooserDialog',{enabled:true});
  const evaluate=async expression=>(await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true})).result?.value;
  const wait=async expression=>{for(let i=0;i<2400;i++){if(errors.length)throw new Error(errors.join('\n'));const s=await evaluate(`({ok:(${expression}),error:typeof _st==='undefined'?'':String(_st.error||''),case:typeof _st==='undefined'?null:String(_st.case||0)})`);if(s.error)throw new Error(s.error);if(s.ok)return;if(i%80===0)console.log('case '+s.case);await pause(250);}throw new Error('CLIP XL timed out');};
  await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});
  await wait("typeof _st!=='undefined' && Number(_st.armed)===1");
  const transpose=await evaluate(`(async()=>{const opened=JSON.parse(await new Promise(r=>gpu_open_then(r)));if(!opened.ok)throw new Error(JSON.stringify(opened));const a=gpu_buf_alloc(72n),b=gpu_buf_alloc(80n);const words=Array.from({length:18},(_,i)=>BigInt(((i*2+1)<<16)|(i*2)));gpu_buf_write_words(a,0n,words);gpu_buf_write_words(b,0n,[...Array(18).fill(0n),123456n,789012n]);const status=bc_run(page_data('extra'),'bcx_transpose_main',20n,[gpu_arg_buf(a,0n),gpu_arg_buf(b,0n),gpu_arg_u32(6n)]);const out=JSON.parse(await new Promise(r=>gpu_buf_download_then(b,0n,80n,r)));gpu_buf_free(a);gpu_buf_free(b);return {status:Number(status),out,error:gpu_error(0n)}})()`);
  if(transpose.status!==0||transpose.error||transpose.out.length!==20)throw new Error('Projection kernel failed');
  const halves=new Uint16Array(new Int32Array(transpose.out).buffer);
  for(let i=0;i<36;i++)if(halves[i]!==(i%6)*6+Math.floor(i/6))throw new Error('Packed transpose differs');
  if(transpose.out[18]!==123456||transpose.out[19]!==789012)throw new Error('Packed transpose overdispatch wrote tail');
  for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});
  await wait('Number(_st.done)===1');
  for(const r of refs)if(await evaluate(`String(_st['ids-${r.name}'])`)!==JSON.stringify(r.ids))throw new Error('Browser token IDs differ from Forge');
  const actual=async(caseIndex,part,n)=>{const ws=JSON.parse(await evaluate(`String(_st['case${caseIndex}-${part}'])`));if(!Array.isArray(ws)||ws.length!==n||ws.some(v=>!Number.isInteger(v)||v < -2147483648||v>2147483647))throw new Error('Malformed output');const xs=Array.from(new Float32Array(new Int32Array(ws).buffer));if(xs.some(v=>!Number.isFinite(v)))throw new Error('Nonfinite output');return xs;};
  const max=(a,b,skip)=>a.reduce((m,v,i)=>i<skip?m:Math.max(m,Math.abs(v-b[i])),0);
  for(const [i,r] of refs.entries())for(let part=0;part<3;part++){
    const xs=await actual(i,part,r.arrays[part].length),width=part===0?768:1280;
    const all=max(xs,r.arrays[part],0),nonBos=part===2?all:max(xs,r.arrays[part],width),bound=part===2?r.bounds[4]:r.bounds[part*2],rowBound=part===2?bound:r.bounds[part*2+1];
    console.log(JSON.stringify({case:r.name,part,all,nonBos,bound,rowBound}));if(all>bound||nonBos>rowBound)throw new Error('CLIP XL exceeds native reference bound');
  }
  for(let i=2;i<5;i++){const part=i===2?0:1,width=part===0?768:1280,xs=await actual(i,0,77*width),err=max(xs,refs[0].arrays[part],width),bound=refs[0].bounds[part*2+1];console.log(JSON.stringify({control:i,error:err,bound}));if(err<=bound)throw new Error('Wrong activation/padding control was not detected');}
  if(await evaluate('_gb.filter(Boolean).length')!==0)throw new Error('Live GPU handles remain');
  console.log('PASS: dual SDXL CLIP hidden states and pooled projection within native bounds; all controls fail; packed transpose exact with tail intact');
}finally{
  if(socket)socket.close();
  if(edge&&edge.exitCode===null)edge.kill();
  execFileSync('pwsh',['-NoProfile','-Command',`$ErrorActionPreference='Stop';function Owned { @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*' }) };foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup did not finish'`],{stdio:'inherit'});
  if(server)server.close();
}
