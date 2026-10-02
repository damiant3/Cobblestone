import {readFileSync,writeFileSync,mkdtempSync,createReadStream} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {spawn,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import http from 'node:http';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../../../..');
const [pagePath,basePath,noisePath,nativeDir,outDir]=process.argv.slice(2);
if(!outDir)throw new Error('Usage: sd15-samplers.mjs arm.html base.wgsl noise.wasm native-evidence-dir output-dir');
const hash=b=>createHash('sha256').update(b).digest('hex');
const fileHash=async p=>{const h=createHash('sha256');for await(const b of createReadStream(p))h.update(b);return h.digest('hex');};
const evidence=JSON.parse(readFileSync(join(nativeDir,'evidence.json'),'utf8'));
const checkpoint=join(repo,'build-output/diffusion-models/realisticVisionV60B1_v20Novae.safetensors');
if(evidence.nativeExit!==0||evidence.seed!==7||evidence.steps!==6||evidence.cfg!==7||evidence.width!==512||evidence.height!==512||evidence.prompt!=='a photo of a cat'||evidence.negative!==''||evidence.images.length!==7)throw new Error('Native request differs');
for(const [file,want] of [[checkpoint,evidence.checkpointSha256],[join(nativeDir,'reference.codex'),evidence.bundleSha256],[join(nativeDir,'reference.cdx'),evidence.cdxSha256],[join(nativeDir,'clip.img'),evidence.diskSha256]])if(await fileHash(file)!==want)throw new Error('Native provenance differs '+file);
const tok='D:/AI/DiffusionForge/webui/backend/huggingface/stabilityai/stable-diffusion-xl-base-1.0/tokenizer',vocab=readFileSync(join(tok,'vocab.json')),merges=readFileSync(join(tok,'merges.txt')),disk=readFileSync(join(nativeDir,'clip.img'));
const vl=disk.readUInt32LE(0),ml=disk.readUInt32LE(4),mo=(1+Math.ceil(vl/512))*512;if(!vocab.equals(disk.subarray(512,512+vl))||!merges.equals(disk.subarray(mo,mo+ml)))throw new Error('Tokenizer differs');
const html=readFileSync(pagePath,'utf8'),base=readFileSync(basePath,'utf8'),noise=readFileSync(noisePath);
const data={bk:base,'sdxl-noise':noise.toString('base64'),vocab:JSON.stringify(Array.from(vocab)),merges:JSON.stringify(Array.from(merges)),sampler:'0',steps:'6'};
const memory=()=>{const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));if(m.FreePhysicalMemory<=1572864||m.FreeVirtualMemory<=26214400)throw new Error('RAM/commit admission refused');return m;};
const work=mkdtempSync(join(tmpdir(),'sd15-samplers-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
let edge,server,socket;const rows=[];
try{
  const admission=memory();console.log('Memory '+JSON.stringify(admission));
  server=http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html.replace('<script>',()=>`<script>window.__DATA=${JSON.stringify(data)};</script><script>`));});await new Promise(r=>server.listen(0,'127.0.0.1',r));
  edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--disable-sync','--no-first-run','about:blank'],{stdio:'ignore'});
  let target;for(let i=0;i<120&&!target;i++){try{const port=readFileSync(join(work,'profile/DevToolsActivePort'),'utf8').split('\n')[0];target=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page'&&t.url==='about:blank');}catch{}if(!target)await pause(250);}if(!target)throw new Error('Browser startup failed');
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((r,j)=>{socket.onopen=r;socket.onerror=j;});
  let id=0;const pending=new Map(),errors=[];
  const send=(method,params={})=>new Promise((r,j)=>{const key=++id,t=setTimeout(()=>{pending.delete(key);j(new Error('CDP timeout '+method+' '+errors.join('\n')));},120000);pending.set(key,m=>{clearTimeout(t);m.error||m.result?.exceptionDetails?j(new Error(JSON.stringify(m.error||m.result.exceptionDetails))):r(m.result);});socket.send(JSON.stringify({id:key,method,params}));});
  socket.onmessage=e=>{const m=JSON.parse(e.data);if(pending.has(m.id)){pending.get(m.id)(m);pending.delete(m.id);}if(m.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(m.params));if(m.method==='Page.fileChooserOpened')send('DOM.setFileInputFiles',{files:[checkpoint],backendNodeId:m.params.backendNodeId}).catch(e=>errors.push(String(e)));};
  const evaluate=async expression=>(await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true})).result?.value;
  const click=async selector=>{const targets=await send('Target.getTargets');for(const t of targets.targetInfos)if(t.type==='page'&&t.url.startsWith('edge://sync-confirmation-dialog/'))await send('Target.closeTarget',{targetId:t.targetId});await send('Page.bringToFront');const p=await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,...p,button:'left',clickCount:1});};
  const wait=async expression=>{let last='';for(let i=0;i<2400;i++){if(errors.length)throw new Error(errors.join('\n'));const s=await evaluate(`({ok:(${expression}),error:typeof _st==='undefined'?'':String(_st.error||''),phase:typeof _st==='undefined'?'':String(_st.phase||'')+':'+String(_st.weights||0)})`);if(s.error)throw new Error(s.error);if(s.ok)return;if(s.phase!==last){console.log('progress '+s.phase);last=s.phase;}await pause(250);}throw new Error('Sampler timed out');};
  for(const d of ['Page','DOM','Runtime'])await send(d+'.enable');await send('Page.setInterceptFileChooserDialog',{enabled:true});await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});await wait("typeof _st!=='undefined'&&Number(_st.armed)===1");await click('#pick');await wait('Number(_st.loaded)===1');
  const baseline=await evaluate(`({active:_gb.filter(Boolean).length,sampler:Number(_st['sampler-refused']),dimensions:Number(_st['dimensions-refused']),singleStep:Number(_st['one-step-schedule'])})`);if(baseline.active<100||baseline.sampler!==1||baseline.dimensions!==1||baseline.singleStep!==1)throw new Error('Loader/refusal/single-step controls failed '+JSON.stringify(baseline));
  const promptFailures=await evaluate(`new Promise(resolve=>{
    const before=_gb.filter(Boolean).length,results=[];
    for(const fault of ['allocation','copy']){const hs=[gpu_buf_alloc(77*768*4),gpu_buf_alloc(77*768*4)],allocate=gpu_buf_alloc,launch=gpu_launch;let result;
      try{if(fault==='allocation')gpu_buf_alloc=()=>0n;else gpu_launch=()=>-1n;result=bt_join({bm_code:page_data('bk')},hs.map(BigInt));}finally{gpu_buf_alloc=allocate;gpu_launch=launch;}
      results.push({fault,result:Number(result),active:_gb.filter(Boolean).length});}
    const broken=gpu_buf_alloc(4);results.push({fault:'emphasis-read',result:Number(bt_emphasised(BigInt(broken),[],'[]')),active:_gb.filter(Boolean).length});
    const words=Array(77*768).fill(1065353216),h=gpu_buf_alloc(words.length*4),write=gpu_buf_write_words;let result;
    try{gpu_buf_write_words=()=>false;result=bt_emphasised(BigInt(h),Array(77).fill(1),JSON.stringify(words));}finally{gpu_buf_write_words=write;}
    results.push({fault:'emphasis-write',result:Number(result),active:_gb.filter(Boolean).length});
    const first=gpu_buf_alloc(4),entry=bt_cond,body=typeof bt_cond__tb==='function'?bt_cond__tb:null;let calls=0;
    const fake=(m,c,next)=>next(++calls===1?BigInt(first):0n);bt_cond=fake;if(body)bt_cond__tb=fake;
    bt_conds({},[{},{}],0n,[],value=>{bt_cond=entry;if(body)bt_cond__tb=body;results.push({fault:'later-chunk',result:Number(value),active:_gb.filter(Boolean).length,calls});resolve({before,results});});
  })`);
  if(promptFailures.before!==baseline.active||promptFailures.results.some(r=>r.result!==0||r.active!==baseline.active)||(promptFailures.results.find(r=>r.fault==='later-chunk').calls!==2))throw new Error('Prompt failure ownership differs '+JSON.stringify(promptFailures));
  console.log('PASS prompt join, emphasis and later-chunk failure cleanup');
  await evaluate(`window.__activeNoiseCancels=0;window.__originalNoise=gpu_noise_bank_then;gpu_noise_bank_then=function(...a){const progress=a[5];a[5]=function(...p){const active=_noiseJobs.size>0,result=progress(...p);if(active&&Number(result)<0)__activeNoiseCancels++;return result};return __originalNoise(...a)};`);
  for(const row of [...evidence.images,evidence.boundary]){
    if(row.kind<0||row.kind>6||row.scheduler!==(row.kind===0?'Automatic':'Karras'))throw new Error('Reference sampler metadata differs');const native=readFileSync(join(nativeDir,row.file));if(hash(native)!==row.sha256)throw new Error('Native image changed');
    const steps=row.steps||6,admission=memory();await evaluate(`window.__DATA.sampler=${JSON.stringify(String(row.kind))};window.__DATA.steps=${JSON.stringify(String(steps))};__activeNoiseCancels=0;for(const k of ['done','steps','order-error','busy-refused','noise-polls','noise-cancelled','sampling-cancelled'])_st[k]=0n;_st.phase='';`);
    const started=performance.now();await click('#generate');await click('#generate');await wait('Number(_st.done)===1');
    const state=await evaluate(`({active:_gb.filter(Boolean).length,workers:_noiseJobs.size,activeNoiseCancels:__activeNoiseCancels,error:_gerr,steps:Number(_st.steps),order:Number(_st['order-error']),busy:Number(_st['busy-refused']),noiseCancelled:Number(_st['noise-cancelled']),samplingCancelled:Number(_st['sampling-cancelled']),noisePolls:Number(_st['noise-polls']),heap:_hp})`);
    if(state.active!==baseline.active||state.workers||state.error||state.steps!==steps||state.order||state.busy!==1||state.noiseCancelled!==(row.kind===6?1:0)||state.samplingCancelled!==(row.kind===6?1:0)||(row.kind===6&&(state.noisePolls!==2||state.activeNoiseCancels!==1)))throw new Error('Job lifetime/order differs '+JSON.stringify(state));
    const png=Buffer.from(await evaluate("document.querySelector('#out').toDataURL('image/png').split(',')[1]"),'base64');writeFileSync(join(outDir,'sampler-'+row.kind+'-'+steps+'.png'),png);
    const comparison=await evaluate(`new Promise((resolve,reject)=>{const im=new Image();im.onload=()=>{if(im.width!==512||im.height!==512){reject(new Error('Native dimensions differ'));return;}const c=document.createElement('canvas');c.width=512;c.height=512;const ctx=c.getContext('2d');ctx.drawImage(im,0,0);const a=ctx.getImageData(0,0,512,512).data,b=document.querySelector('#out').getContext('2d').getImageData(0,0,512,512).data;let sum=0,max=0,mean=0,sq=0;for(let i=0;i<a.length;i++)if(i%4!==3){const d=Math.abs(a[i]-b[i]);sum+=d;max=Math.max(max,d);mean+=b[i];sq+=b[i]*b[i]}const n=512*512*3;resolve({mean:sum/n,max,sd:Math.sqrt(sq/n-(mean/n)**2)});};im.onerror=reject;im.src=${JSON.stringify('data:image/png;base64,'+native.toString('base64'))};})`);
    if(!(comparison.mean<4)||comparison.sd<5)throw new Error('Sampler image grade failed '+JSON.stringify(comparison));const r={kind:row.kind,steps,ms:performance.now()-started,comparison,state,sha256:hash(png),admission};rows.push(r);console.log('PASS sampler '+JSON.stringify(r));
    const trim=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);if(trim!=='')throw new Error('Scratch trim failed '+trim);
  }
  await click('#close');const trim=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);const final=await evaluate(`({active:_gb.filter(Boolean).length,pool:Object.values(_gpool).flat().length,workers:_noiseJobs.size,closed:Number(_st.closed),error:_gerr})`);
  if(trim!==''||final.active||final.pool||final.workers||final.closed!==1||final.error)throw new Error('Final cleanup failed '+JSON.stringify(final));
  writeFileSync(join(outDir,'evidence.json'),JSON.stringify({pageSha256:hash(html),shaderSha256:hash(base),noiseSha256:hash(noise),rows,final},null,2)+'\n');console.log('PASS all seven SD1.5 samplers, cancellation/regeneration and ownership controls');
}finally{
  if(socket)socket.close();if(edge&&edge.exitCode===null)edge.kill();
  execFileSync('pwsh',['-NoProfile','-Command',`function Owned {@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"|Where-Object {$_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*'})};foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup failed'`],{stdio:'inherit'});
  if(server)server.close();
}
