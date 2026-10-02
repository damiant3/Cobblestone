import {readFileSync,mkdtempSync,createReadStream} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {spawn,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import http from 'node:http';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../../../..');
const [htmlPath,basePath,extraPath]=process.argv.slice(2);
if(!extraPath)throw new Error('Usage: node codex/plugs/html/arms/clip-xl-prompt.mjs arm.html BrowserKernels.wgsl BrowserClipKernels.wgsl');
const ckpt=join(repo,'build-output/diffusion-models/dreamshaperXL_lightningDPMSDE.safetensors');
const checkpointHash=createHash('sha256');for await(const bytes of createReadStream(ckpt))checkpointHash.update(bytes);
console.log('Checkpoint SHA256 '+checkpointHash.digest('hex'));
const prompts=[
'text, letters, words, watermark, signature, logo, ui, blurry, deformed, ugly, lowres, jpeg artifacts, photo, photograph, modern',
'(masterpiece:1.2), ((best quality)), a [red] cat on a \\(wooden\\) table, (glowing runes:0.8), [[blurry]] (sharp:1.5 edges',
'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window, carved dragon heads on the rafters, frost on the windows, a sleeping wolf by the hearth BREAK a longship on a calm fjord at dawn'
];
const bounds=[[0.55157,0.01006,0.21764,0.04752,0.00612],[0.58484,0.0153,0.21808,0.04066,0.00388],[0.55157,0.01527,0.21764,0.08168,0.00369]];
const refs=['negative','emphasis','long'].map((name,i)=>{
  const b=readFileSync(join(repo,'codex/test/apps/clip-sdxl-prompt-ref',name+'.ref')),n=b.readInt32LE(0);
  if(n!==(i===2?3:1)||b.length!==4+n*616+n*77*2048*4+1280*4)throw new Error('Reference shape differs');
  const ids=Array.from({length:77*n},(_,j)=>b.readInt32LE(4+j*4)),weights=Array.from({length:77*n},(_,j)=>b.readUInt32LE(4+n*308+j*4));
  const floats=(off,count)=>Array.from({length:count},(_,j)=>b.readFloatLE(off+j*4));
  const arrays=[floats(4+n*616,n*77*768),floats(4+n*616+n*77*768*4,n*77*1280),floats(4+n*616+n*77*2048*4,1280)];
  if(ids.some(v=>v<0||v>49407)||arrays.flat().some(v=>!Number.isFinite(v)))throw new Error('Invalid reference');
  console.log('Reference '+name+' SHA256 '+createHash('sha256').update(b).digest('hex'));
  return {name,n,ids,weights,arrays,bounds:bounds[i]};
});
const tokenizer='D:/AI/DiffusionForge/webui/backend/huggingface/stabilityai/stable-diffusion-xl-base-1.0/tokenizer';
const html=readFileSync(htmlPath,'utf8'),data={base:readFileSync(basePath,'utf8'),extra:readFileSync(extraPath,'utf8'),vocab:JSON.stringify(Array.from(readFileSync(join(tokenizer,'vocab.json')))),merges:JSON.stringify(Array.from(readFileSync(join(tokenizer,'merges.txt'))))};
prompts.forEach((p,i)=>data['prompt'+i]=p);
console.log('Artifacts '+JSON.stringify(Object.fromEntries([['html',html],['base',data.base],['extra',data.extra]].map(([k,v])=>[k,createHash('sha256').update(v).digest('hex')]))));
const work=mkdtempSync(join(tmpdir(),'clip-xl-prompt-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
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
  for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});
  await wait('Number(_st.done)===1');
  const actual=async(caseIndex,part,n)=>{const ws=JSON.parse(await evaluate(`String(_st['case${caseIndex}-${part}'])`));if(!Array.isArray(ws)||ws.length!==n||ws.some(v=>!Number.isInteger(v)||v < -2147483648||v>2147483647))throw new Error('Malformed output');const xs=Array.from(new Float32Array(new Int32Array(ws).buffer));if(xs.some(v=>!Number.isFinite(v)))throw new Error('Nonfinite output');return xs;};
  const max=(a,b,width,skipBos)=>a.reduce((m,v,i)=>skipBos&&Math.floor(i/width)%77===0?m:Math.max(m,Math.abs(v-b[i])),0);
  const chunks=async index=>{const n=await evaluate('Number(_st.chunks'+index+')');if(!Number.isInteger(n)||n<1||n>10)throw new Error('Invalid chunk count');const ids=[],weights=[];for(let j=0;j<n;j++){const v=JSON.parse(await evaluate(`String(_st['ids${index}-${j}'])`)),w=JSON.parse(await evaluate(`String(_st['weights${index}-${j}'])`));if(!Array.isArray(v)||!Array.isArray(w)||v.length!==77||w.length!==77||v.some(x=>!Number.isInteger(x)||x<0||x>49407)||v[0]!==49406||v[76]!==49407||w.some(x=>!Number.isInteger(x)||x<0||x>4294967295)||Array.from(new Float32Array(new Uint32Array(w).buffer)).some(x=>!Number.isFinite(x)||x<=0))throw new Error('Invalid chunk/control structure');ids.push(...v);weights.push(...w);}return {n,ids,weights};};
  for(const [i,r] of refs.entries()){
    const outputs=[];
    const got=await chunks(i);if(got.n!==r.n||JSON.stringify(got.ids)!==JSON.stringify(r.ids)||JSON.stringify(got.weights)!==JSON.stringify(r.weights))throw new Error('Prompt chunks differ from Forge');
    for(let part=0;part<3;part++){
      const xs=await actual(i,part,r.arrays[part].length),width=part===0?768:1280,all=max(xs,r.arrays[part],width,false),body=part===2?all:max(xs,r.arrays[part],width,true),a=part===2?r.bounds[4]:r.bounds[part*2],b=part===2?a:r.bounds[part*2+1];
      outputs.push(xs);console.log(JSON.stringify({case:r.name,part,all,body,bound:a,bodyBound:b}));if(all>a||body>b)throw new Error('Prompt output exceeds native bounds');
    }
    const cross=await actual(i,3,r.n*77*2048),want=Array.from({length:r.n*77*2048},(_,j)=>{const row=Math.floor(j/2048),col=j%2048;return col<768?r.arrays[0][row*768+col]:r.arrays[1][row*1280+col-768]});
    for(let j=0;j<cross.length;j++){const row=Math.floor(j/2048),col=j%2048,value=col<768?outputs[0][row*768+col]:outputs[1][row*1280+col-768];if(!Object.is(cross[j],value))throw new Error('Cross-attention copy is not exact, including BOS');}
    const e=max(cross,want,2048,true),bound=Math.max(r.bounds[1],r.bounds[3]);console.log(JSON.stringify({case:r.name,crossError:e,bound,copyExact:true}));if(e>bound)throw new Error('Cross-attention layout differs');
  }
  if((await chunks(3)).n!==1)throw new Error('Empty negative must be one chunk');
  for(const [part,n] of [77*768,77*1280,1280,77*2048].entries()){const xs=await actual(3,part,n);if(xs.some(v=>v!==0))throw new Error('Empty negative is not zero');}
  const wrong=await actual(4,0,77*768),e=max(wrong,refs[1].arrays[0],768,true);if(e<=refs[1].bounds[1])throw new Error('Unweighted emphasis control escaped detection');
  const changed=await chunks(5);if(changed.n===refs[2].n&&JSON.stringify(changed.ids)===JSON.stringify(refs[2].ids)&&JSON.stringify(changed.weights)===JSON.stringify(refs[2].weights))throw new Error('No-backtrack chunk control escaped detection');
  if(await evaluate('_gb.filter(Boolean).length')!==0)throw new Error('Live handles remain');
  console.log('PASS: SDXL prompt IDs/weights, all conditioning and pooled values, cross-attention, empty negative and emphasis/backtrack controls');
}finally{
  if(socket)socket.close();
  if(edge&&edge.exitCode===null)edge.kill();
  execFileSync('pwsh',['-NoProfile','-Command',`$ErrorActionPreference='Stop';function Owned { @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*' }) };foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup did not finish'`],{stdio:'inherit'});
  if(server)server.close();
}
