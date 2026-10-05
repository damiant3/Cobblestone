import {readFileSync,mkdtempSync,createReadStream,openSync,readSync,closeSync} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {spawn,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import http from 'node:http';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../../../..');
const [htmlPath,basePath]=process.argv.slice(2);
if(!basePath)throw new Error('Usage: node codex/plugs/html/arms/unet-xl.mjs arm.html BrowserKernels.wgsl');
const ckpt=join(repo,'build-output/diffusion-models/dreamshaperXL_lightningDPMSDE.safetensors');
const checkpointHash=createHash('sha256');for await(const bytes of createReadStream(ckpt))checkpointHash.update(bytes);
console.log('Checkpoint SHA256 '+checkpointHash.digest('hex'));
const source=readFileSync(join(repo,'apps/diffusion/UNetReference.codex'),'utf8');
const num=t=>{const m=/^\(0\.0 - (.+)\)$/.exec(t.trim());return m?-Number(m[1]):Number(t);};
const definitions=source.split('\n').filter(l=>l.includes('= UNetRefBlock {')).map(l=>{
  const field=k=>new RegExp(k+' = ([^,]+?)(?:,|\\s})').exec(l)[1];
  return {key:/^\s*(\S+) = UNetRefBlock/.exec(l)[1],name:/urb-name = "([^"]+)"/.exec(l)[1],n:Number(field('urb-c'))*Number(field('urb-h'))*Number(field('urb-w')),rms:num(field('urb-rms')),samples:/urb-samples = \[(.*)\]/.exec(l)[1].split(/,\s*(?![^(]*\))/).map(num)};
});
const order=/unet-ref-blocks = \[([^\]]+)\]/.exec(source)?.[1].split(',').map(s=>s.trim());
if(!order||definitions.length!==20||new Set(definitions.map(r=>r.key)).size!==20||new Set(order).size!==20)throw new Error('Reference declarations/order differ');
const refs=order.map(k=>definitions.find(r=>r.key===k));
const names=[...Array.from({length:9},(_,i)=>'input '+i),'middle',...Array.from({length:9},(_,i)=>'output '+i),'out'];
if(refs.some((r,i)=>!r||r.name!==names[i]))throw new Error('Canonical block names/order differ');
for(const [key,value] of Object.entries({t:500,samples:64,stride:7919,offset:13}))if(Number(new RegExp('unet-ref-'+key+' : (?:Real|Integer) = ([0-9.]+)').exec(source)?.[1])!==value)throw new Error('Reference timestep/sampling constants differ');
if(refs.length!==20||refs.some(r=>!Number.isInteger(r.n)||r.n<=0||!Number.isFinite(r.rms)||r.rms<=0||r.samples.length!==64||r.samples.some(v=>!Number.isFinite(v))))throw new Error('Native block reference inventory differs');
console.log('Reference SHA256 '+createHash('sha256').update(source).digest('hex'));
const fd=openSync(ckpt,'r'),lb=Buffer.alloc(8);if(readSync(fd,lb,0,8,0)!==8)throw new Error('Checkpoint length read failed');const len=Number(lb.readBigUInt64LE(0));if(!Number.isSafeInteger(len)||len<2||len>16777216)throw new Error('Checkpoint header size invalid');const header=Buffer.alloc(len);if(readSync(fd,header,0,len,8)!==len)throw new Error('Checkpoint header truncated');closeSync(fd);
const expectedWeights=Object.keys(JSON.parse(header.toString('utf8'))).filter(k=>k.startsWith('model.diffusion_model.')).length;
const bits=x=>new Int32Array(new Float32Array([x]).buffer)[0];
const latent=Array.from({length:1024},(_,i)=>bits(((i*37+11)%257-128)/128));
const label=Array.from({length:2816},(_,i)=>bits(((i*29+3)%241-120)/256));
const context=Array.from({length:157696},(_,i)=>bits(((i*13+5)%251-125)/256));
const sizes=[...refs.map(r=>r.n),refs[1].n,refs[4].n];
const html=readFileSync(htmlPath,'utf8'),data={bk:readFileSync(basePath,'utf8'),latent:JSON.stringify(latent),label:JSON.stringify(label),context:JSON.stringify(context),sizes:JSON.stringify(sizes)};
console.log('Artifacts '+JSON.stringify(Object.fromEntries([['html',html],['base',data.bk]].map(([k,v])=>[k,createHash('sha256').update(v).digest('hex')]))));
const work=mkdtempSync(join(tmpdir(),'unet-xl-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
let server,edge,socket;
try{
  server=http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html.replace('<script>',`<script>window.__DATA=${JSON.stringify(data)};</script><script>`));});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--no-first-run','about:blank'],{stdio:'ignore'});
  console.log(`UNet XL browser PID ${edge.pid}; evidence ${work}`);
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
  for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});
  await wait('Number(_st.done)===1');
  const status=await evaluate("({failed:Number(_st.failed),tensors:Number(_st.tensors),blocks:Number(_st.blocks),error:String(_st['err-run']||_st.err||'')})");
  if(status.failed!==0||status.tensors!==expectedWeights||status.blocks!==refs.length||status.error)throw new Error('UNet upload/run failed: '+JSON.stringify(status));
  const grade=async(i,r)=>{const words=JSON.parse(await evaluate("String(_st.b"+i+")"));if(!Array.isArray(words)||words.length!==r.n||words.some(v=>!Number.isInteger(v)||v< -2147483648||v>2147483647))throw new Error('Block output shape differs');const values=Array.from(new Float32Array(new Int32Array(words).buffer));if(values.some(v=>!Number.isFinite(v)))throw new Error('Nonfinite block output');let near=0;for(let j=0;j<64;j++)if(Math.abs(values[(j*7919+13)%r.n]-r.samples[j])<=0.01*r.rms)near++;const ms=values.reduce((a,v)=>a+v*v,0)/r.n,msRelative=Math.abs(ms-r.rms*r.rms)/(r.rms*r.rms);return {near,msRelative};};
  for(const [i,r] of refs.entries()){const result=await grade(i,r);console.log(JSON.stringify({block:r.name,...result}));if(result.near!==64||result.msRelative>0.02)throw new Error('UNet block exceeds native bound');}
  for(const [i,k] of [1,4].entries()){const result=await grade(20+i,refs[k]);console.log(JSON.stringify({control:i,...result}));if(result.near===64&&result.msRelative<=0.02)throw new Error('Wrong timestep/context was not detected');}
  if(await evaluate('_gb.filter(Boolean).length')!==0)throw new Error('Live UNet handles remain');
  console.log('PASS: all20 SDXL UNet blocks meet native64-sample and mean-square bounds; timestep/context controls detected; handles released');
}finally{
  if(socket)socket.close();
  if(edge&&edge.exitCode===null)edge.kill();
  execFileSync('pwsh',['-NoProfile','-Command',`$ErrorActionPreference='Stop';function Owned { @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*' }) };foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup did not finish'`],{stdio:'inherit'});
  if(server)server.close();
}
