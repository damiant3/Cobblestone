import {readFileSync,writeFileSync,mkdtempSync,createReadStream,statSync} from 'node:fs';
import {join,resolve,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {spawn,spawnSync,execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import http from 'node:http';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../../../..');
const [pagePath,basePath,loraCodePath,nativeDir,outDir]=process.argv.slice(2);
if(!outDir)throw new Error('Usage: lora.mjs arm.html base.wgsl lora.wgsl native-evidence-dir output-dir');
const hash=b=>createHash('sha256').update(b).digest('hex');
const fileHash=async p=>{const h=createHash('sha256');for await(const b of createReadStream(p))h.update(b);return h.digest('hex');};
const nativeRecord=readFileSync(join(nativeDir,'evidence.json')),nativeEvidenceSha256=hash(nativeRecord),evidence=JSON.parse(nativeRecord.toString('utf8'));
const family=evidence.family||'sd15',xl=family==='sdxl',size=xl?1024:512;
const lycoris=['LoHa','LoKr','GLoRA','Diff'].includes(evidence.variant);
const dora=evidence.variant==='DoRA',complex=lycoris||dora;
const extraFaults=lycoris?['work-alloc','input-alloc','pack',...(['LoHa','LoKr'].includes(evidence.variant)?['tucker']:[]),...(evidence.variant==='LoHa'?['tucker-second']:[]),...(['LoKr','GLoRA'].includes(evidence.variant)?['matmul']:[]),...(evidence.variant==='LoKr'?['matmul-second']:[])]:[];
const doraFaults=dora?['work-alloc','input-alloc','pack','dora-alloc','dora-zero','dora-norm','dora-apply','delta-completion']:[];
const baseFaults=['upload','merge','completion',...(xl&&(!complex||evidence.variant==='Diff')?['upload-g','merge-unet','completion-g']:[]),'trim'];
const faultNames=[...baseFaults,...(evidence.variant==='LoCon'?['mid-alloc','mid-upload','fold','fold-completion']:[]),...extraFaults,...doraFaults];
const faultIndex=process.argv.indexOf('--fault'),onlyFault=faultIndex<0?'':process.argv[faultIndex+1];
const recordName=(key='',fault='')=>'browser-evidence'+(key?'-'+key:'')+(fault?'-'+fault:'')+'.json';
const caseIndex=process.argv.indexOf('--case'),single=caseIndex<0?'':process.argv[caseIndex+1];
if(onlyFault&&(single!=='controls'||!['ancillary',...faultNames].includes(onlyFault)))throw new Error('Unknown isolated fault');

if(caseIndex>=0&&![...evidence.images.map(r=>String(r.weight100)),'plain','controls'].includes(single))throw new Error('Unknown isolated case');
if(xl&&!single){
  const records=[];
  const cases=[...evidence.images.map(r=>({key:String(r.weight100),fault:''})),{key:'plain',fault:''},...['ancillary',...faultNames].map(fault=>({key:'controls',fault}))];
  for(const {key,fault} of cases){
    const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));
    if(m.FreePhysicalMemory<=1572864||m.FreeVirtualMemory<=26214400)throw new Error('Isolated case RAM/commit admission refused');
    console.log('ISOLATED '+key+(fault?':'+fault:'')+' '+JSON.stringify(m));
    const r=spawnSync(process.execPath,[...process.argv.slice(1),'--case',key,...(fault?['--fault',fault]:[])],{stdio:'inherit',windowsHide:true});if(r.error)throw r.error;if(r.status!==0)throw new Error('Isolated case failed '+key+': '+r.status);
    const record=JSON.parse(readFileSync(join(outDir,recordName(key,fault)),'utf8'));
    if(record.nativeEvidenceSha256!==nativeEvidenceSha256)throw new Error('Native evidence moved between cases');
    records.push(record);
  }
  const rows=records.flatMap(r=>r.rows);
  if(new Set(rows.filter(r=>!r.plain).map(r=>r.sha256)).size!==evidence.images.length||rows.find(r=>r.plain).sha256!==rows.find(r=>!r.plain&&r.weight100===0).sha256)throw new Error('Isolated strength/plain controls differ');
  writeFileSync(join(outDir,'browser-evidence.json'),JSON.stringify({family,isolated:true,records,rows},null,2)+'\n');console.log('PASS isolated SDXL LoRA images and lifecycle controls');process.exit(0);
}
const checkpoint=join(repo,'build-output/diffusion-models',xl?'dreamshaperXL_lightningDPMSDE.safetensors':'realisticVisionV60B1_v20Novae.safetensors');
const lora=evidence.loraFile?resolve(nativeDir,evidence.loraFile):xl?'D:/AI/DiffusionForge/webui/models/Lora/9HNHMWJZDSGD8WE7FFJCRVB8M0.safetensors':join(repo,'codex/test/gpu-files/lora-sd15.safetensors');
if(!['sd15','sdxl'].includes(family)||evidence.nativeExit!==0||evidence.seed!==(xl?7201:7)||evidence.steps!==6||evidence.cfg!==(xl?2:7)||evidence.width!==size||evidence.height!==size||evidence.prompt!=='a photo of a cat'||evidence.negative!==''||evidence.sampler!=='Euler'||evidence.scheduler!==(xl?'Karras':'Automatic'))throw new Error('Native request differs');
for(const [file,want] of [[checkpoint,evidence.checkpointSha256],[lora,evidence.loraSha256],[join(nativeDir,'reference.codex'),evidence.bundleSha256],[join(nativeDir,'reference.cdx'),evidence.cdxSha256],[join(nativeDir,'clip.img'),evidence.diskSha256]])if(await fileHash(file)!==want)throw new Error('Native provenance differs '+file);
const tokenDir='D:/AI/DiffusionForge/webui/backend/huggingface/stabilityai/stable-diffusion-xl-base-1.0/tokenizer';
const vocab=readFileSync(join(tokenDir,'vocab.json')),merges=readFileSync(join(tokenDir,'merges.txt')),disk=readFileSync(join(nativeDir,'clip.img'));
const vl=disk.readUInt32LE(0),ml=disk.readUInt32LE(4),mo=(1+Math.ceil(vl/512))*512;
if(!vocab.equals(disk.subarray(512,512+vl))||!merges.equals(disk.subarray(mo,mo+ml)))throw new Error('Native/browser tokenizer differs');
const html=readFileSync(pagePath,'utf8'),code=readFileSync(loraCodePath,'utf8');
const data={family,bk:readFileSync(basePath,'utf8'),'lora-code':code,vocab:JSON.stringify(Array.from(vocab)),merges:JSON.stringify(Array.from(merges)),prompt:evidence.prompt,weight100:'0'};
if(single==='controls')data['checks-only']='1';
if(xl)data['clip-xl']=readFileSync(join(repo,'build-output/sdxl-browser/clip-extra.wgsl'),'utf8');
const loraSize=statSync(lora).size,checkpointSize=statSync(checkpoint).size;
let malformedMid,malformedKind,malformedDora,midOffset=0;
if(dora){
  const bytes=readFileSync(lora),offset=8+Number(bytes.readBigUInt64LE(0)),meta=JSON.parse(bytes.toString('utf8',8,offset)),key='lora_unet_input_blocks_1_0_in_layers_2.dora_scale';
  if(meta[key].dtype!=='BF16'||meta[key].shape.join(',')!=='1,320,1,1')throw new Error('DoRA convolution scale differs');
  meta[key].shape=[319];meta[key].data_offsets[1]-=2;const json=Buffer.from(JSON.stringify(meta)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));malformedDora=Buffer.concat([len,json,bytes.subarray(offset)]);
}
if(evidence.variant==='LoCon'){
  const bytes=readFileSync(lora),offset=8+Number(bytes.readBigUInt64LE(0)),meta=JSON.parse(bytes.toString('utf8',8,offset)),key=Object.keys(meta).find(k=>k.endsWith('.lora_mid.weight'));
  if(!key||meta[key].shape.join(',')!=='4,4,3,3')throw new Error('LoCon fixture mid shape differs');
  midOffset=offset+meta[key].data_offsets[0];
  meta[key].shape=[8,2,3,3];const json=Buffer.from(JSON.stringify(meta)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));malformedMid=Buffer.concat([len,json,bytes.subarray(offset)]);
}
if(lycoris){
  const bytes=readFileSync(lora),offset=8+Number(bytes.readBigUInt64LE(0)),meta=JSON.parse(bytes.toString('utf8',8,offset));
  const suffix={LoHa:'.hada_w1_b',LoKr:'.lokr_w1',GLoRA:'.a1.weight',Diff:'.diff'}[evidence.variant],key=Object.keys(meta).find(k=>k.endsWith(suffix));if(!key)throw new Error('Missing malformed-kind subject');
  const m=meta[key];if(evidence.variant==='Diff'){const n=m.shape.reduce((a,b)=>a*b,1),width=(m.data_offsets[1]-m.data_offsets[0])/n;m.shape=[n-1];m.data_offsets[1]-=width;}
  else {if(m.shape.length!==2||m.shape[1]%2)throw new Error('Shape control requires an even matrix');m.shape=[m.shape[0]*2,m.shape[1]/2];}
  const json=Buffer.from(JSON.stringify(meta)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));malformedKind=Buffer.concat([len,json,bytes.subarray(offset)]);
}
const load=(checkpointExpr,fileExpr,sizeExpr,success,failure)=>xl?
  `browser_sdxl_lora_open(${checkpointExpr},${checkpointSize}n,page_data('bk'),page_data('clip-xl'),${fileExpr},${sizeExpr},0.6,page_data('lora-code'),${success},${failure})`:
  `browser_sd15_lora_load_then(${checkpointExpr},${fileExpr},${sizeExpr},0.75,page_data('lora-code'),()=>0n,${failure},${success})`;
const work=mkdtempSync(join(tmpdir(),'lora-arm-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
let edge,server,socket;const rows=[],faults=[];
try{
  server=http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html.replace('<script>',()=>`<script>window.__DATA=${JSON.stringify(data)};</script><script>`));});
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--no-first-run','about:blank'],{stdio:'ignore'});
  let target;for(let i=0;i<120&&!target;i++){try{const port=readFileSync(join(work,'profile/DevToolsActivePort'),'utf8').split('\n')[0];target=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{}if(!target)await pause(250);}
  if(!target)throw new Error('Browser startup failed');
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((r,j)=>{socket.onopen=r;socket.onerror=j;});
  let id=0,pick=0;const pending=new Map(),errors=[];
  const send=(method,params={})=>new Promise((r,j)=>{const key=++id,t=setTimeout(()=>{pending.delete(key);j(new Error('CDP timeout '+method));},120000);pending.set(key,m=>{clearTimeout(t);m.error||m.result?.exceptionDetails?j(new Error(JSON.stringify(m.error||m.result.exceptionDetails))):r(m.result);});socket.send(JSON.stringify({id:key,method,params}));});
  socket.onmessage=e=>{const m=JSON.parse(e.data);if(pending.has(m.id)){pending.get(m.id)(m);pending.delete(m.id);}if(m.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(m.params));if(m.method==='Page.fileChooserOpened')send('DOM.setFileInputFiles',{files:[pick++===0?checkpoint:lora],backendNodeId:m.params.backendNodeId}).catch(e=>errors.push(String(e)));};
  const evaluate=async expression=>(await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true})).result?.value;
  const wait=async expression=>{let last='';for(let i=0;i<2400;i++){if(errors.length)throw new Error(errors.join('\n'));const state=await evaluate(`({ok:(${expression}),error:typeof _st==='undefined'?'':String(_st.error||''),phase:typeof _st==='undefined'?'':String(_st.phase||'')+':'+String(_st.weights||0)})`);if(state.error){const gpu=await evaluate('window.__gpuErrors||[]');throw new Error(state.error+'\nDevice errors: '+JSON.stringify(gpu));}if(state.ok)return;if(state.phase!==last){console.log('progress '+state.phase);last=state.phase;}await pause(250);}throw new Error('LoRA arm timed out');};
  for(const d of ['Page','DOM','Runtime'])await send(d+'.enable');await send('Page.setInterceptFileChooserDialog',{enabled:true});await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});await wait("typeof _st!=='undefined'&&Number(_st.armed)===1");
  const opened=await evaluate(`new Promise(r=>gpu_open_then(s=>r(JSON.parse(s))))`);if(!opened.ok)throw new Error(JSON.stringify(opened));
  const abi=await evaluate(`(()=>{const h=gpu_buf_alloc(4n);if(h===0n)return 'allocation-failed';const kind=typeof h;gpu_buf_free(h);return kind;})()`);if(abi!=='bigint')throw new Error('GPU Integer ABI differs: '+abi);
  const kernelNames=[...code.matchAll(/\/\/ cx-kernel ([A-Za-z0-9_]+) wg=/g)].map(m=>m[1]);
  const layouts=await evaluate(`(async()=>{const rows=[];for(const name of ${JSON.stringify(kernelNames)}){const k=_gkernel(page_data('lora-code'),name),hs=[],ps=k.ps.map(p=>{if(p.s!=='b')return gpu_arg_u32(0n);const h=gpu_buf_alloc(4);hs.push(h);return gpu_arg_buf(BigInt(h),0n);});_gd.pushErrorScope('validation');const result=gpu_launch(page_data('lora-code'),name,bs_grid(1n),ps);_gflush();await _gd.queue.onSubmittedWorkDone();const error=await _gd.popErrorScope();for(const h of hs)gpu_buf_free(h);rows.push({name,result:Number(result),error:error?error.message:''});if(error||result<0)break;}return rows;})()`);
  if(layouts.length!==kernelNames.length||layouts.some(r=>r.result<0||r.error))throw new Error('LoRA kernel layout failed '+JSON.stringify(layouts));
  console.log('PASS all '+layouts.length+' LoRA kernel layouts through zero-element dispatch');
  await evaluate(`window.__gpuErrors=[];_gd.addEventListener('uncapturederror',e=>{if(__gpuErrors.length<16)__gpuErrors.push({message:String(e.error&&e.error.message||e.error),target:window.__target||''});});`);
  const halves=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command',`$r=[Collections.Generic.List[object]]::new();for($i=0;$i -lt 65536;$i++){if(($i -band 31744) -ne 31744){$h=[BitConverter]::UInt16BitsToHalf([uint16]$i);$r.Add(@([BitConverter]::SingleToInt32Bits([single]$h),$i))}};for($i=0;$i -lt 31743;$i++){$a=[single][BitConverter]::UInt16BitsToHalf([uint16]$i);$b=[single][BitConverter]::UInt16BitsToHalf([uint16]($i+1));$mid=[BitConverter]::SingleToInt32Bits([single](($a+$b)/2));foreach($d in @(-1,0,1)){foreach($sign in @(0,-2147483648)){$w=($mid+$d) -bor $sign;$h=[Half][BitConverter]::Int32BitsToSingle($w);$r.Add(@($w,[BitConverter]::HalfToUInt16Bits($h)))}}};foreach($w in @(1199570943,1199570944,1199570945)){$r.Add(@($w,[BitConverter]::HalfToUInt16Bits([Half][BitConverter]::Int32BitsToSingle($w))))};$r|ConvertTo-Json -Compress`],{encoding:'utf8',windowsHide:true,maxBuffer:16777216}));
  const packed=await evaluate(`new Promise((resolve,reject)=>{const words=${JSON.stringify(halves.map(r=>r[0]))};const a=gpu_buf_alloc(words.length*4),b=gpu_buf_alloc(Math.ceil(words.length/2)*4);gpu_buf_write_words(a,0,words.map(BigInt));const ok=gpu_launch(page_data('lora-code'),'blw_pack_main',bs_grid(BigInt(Math.ceil(words.length/2))),[gpu_arg_buf(BigInt(a),0n),gpu_arg_buf(BigInt(b),0n),gpu_arg_u32(BigInt(words.length))]);if(ok<0){reject(new Error(_gerr));return;}gpu_buf_download_then(b,0,Math.ceil(words.length/2)*4,s=>{gpu_buf_free(a);gpu_buf_free(b);resolve(JSON.parse(s));});})`);
  const shaderError=await evaluate('_gerr');if(shaderError)throw new Error('LoRA shader validation failed: '+shaderError);
  for(let i=0;i<halves.length;i++)if(((packed[i>>1]>>>(16*(i%2)))&65535)!==halves[i][1])throw new Error('Finite half roundtrip differs '+i);
  console.log('PASS '+halves.length+' f16 roundtrip, midpoint-neighbor and overflow packing values against .NET Half');
  for(const [dtype,width,count] of [[2,2,65537],[5,1,257],[6,1,257]]){
    const converted=await evaluate(`new Promise(resolve=>{const dtype=${dtype},width=${width},count=${count},bytes=new Uint8Array(count*width),view=new DataView(bytes.buffer);for(let i=0;i<count;i++){const value=i===count-1?(dtype===2?16256:dtype===5?56:60):i;if(width===2)view.setUint16(i*2,value,true);else bytes[i]=value;}_gfiles['dtype-control']=new File([bytes],'dtype-control');const before=_gb.filter(Boolean).length,h=gpu_buf_alloc(count*4);blo_upload(page_data('lora-code'),{lf_path:'dtype-control',lf_file:{st_data_offset:0n}},{stm_dtype:BigInt(dtype),stm_shape:[BigInt(count)],stm_ndim:1n,stm_offset_start:0n,stm_offset_end:BigInt(bytes.length)},BigInt(h),why=>{if(why){gpu_buf_free(h);resolve({why});return 0n;}gpu_buf_download_then(h,0,count*4,s=>{gpu_buf_free(h);resolve({words:JSON.parse(s),before,after:_gb.filter(Boolean).length});});return 0n;});})`);
    if(converted.why||converted.before!==converted.after||converted.words?.length!==count)throw new Error('Dtype conversion lifetime failed '+JSON.stringify(converted));
    const f=new Float32Array(1),w=new Uint32Array(f.buffer);
    for(let b=0;b<count;b++){
      const pattern=b===count-1?(dtype===2?16256:dtype===5?56:60):b;
      if(dtype===2)w[0]=pattern*65536;
      else {const mb=dtype===6?2:3,bias=dtype===6?15:7,e=(pattern&127)>>>mb,m=pattern&((1<<mb)-1);f[0]=(pattern&128?-1:1)*(e===0?m*2**(1-bias-mb):e===(dtype===6?31:15)&&(dtype===6||m===7)?(dtype===6&&m===0?Infinity:NaN):(1+m/2**mb)*2**(e-bias));}
      const got=converted.words[b]>>>0,want=w[0];
      if(Number.isNaN(f[0])?((got&0x7f800000)!==0x7f800000||(got&0x7fffff)===0):got!==want)throw new Error('Dtype '+dtype+' pattern '+b+' differs: '+got+' vs '+want);
    }
    const scalars=await evaluate(`(()=>{const words=new Uint32Array(${JSON.stringify(converted.words)}),values=new Float32Array(words.buffer);let calls=0;for(let i=0;i<${count};i++){const b=i===${count-1}?${dtype===2?16256:dtype===5?56:60}:i,bytes=${width}===2?[b&255,b>>>8]:[b];let value,why='';blo_alpha_read(${dtype}n,v=>{calls++;value=v;return 0n;},s=>{calls++;why=s;return 0n;},JSON.stringify(bytes));if(Number.isFinite(values[i])?(why!==''||value!==values[i]):why!=='LoRA alpha is nonfinite')return {bad:i,value,why};}return {calls};})()`);
    if(scalars.bad!==undefined||scalars.calls!==count)throw new Error('Dtype alpha scalar differs '+JSON.stringify(scalars));
    console.log('PASS dtype '+dtype+': all '+count+' encodings through file upload/GPU widening; finite bits and nonfinite classes match');
  }
  const refused=await evaluate(`new Promise(resolve=>{const m={'lora_te_text_model_encoder_layers_3_self_attn_v_proj.lora_down.weight':{dtype:'I32',shape:[1],data_offsets:[0,4]}};const h=new TextEncoder().encode(JSON.stringify(m)),b=new Uint8Array(h.length+12);new DataView(b.buffer).setBigUint64(0,BigInt(h.length),true);b.set(h,8);_gfiles['format-control']=new File([b],'format-control');browser_lora_open('format-control',BigInt(b.length),${JSON.stringify(family)},1.0,()=>{resolve('accepted');return 0n;},s=>{resolve(s);return 0n;});})`);
  if(refused!=='Browser LoRA does not support I32')throw new Error('Unnamed dtype refusal '+refused);
  console.log('PASS unsupported I32 is refused by name');
  const wrong=xl?['lora_unet_input_blocks_1_1_transformer_blocks_0_attn1_to_q','sd15']:['lora_te2_text_model_encoder_layers_0_self_attn_q_proj','sdxl'];
  for(const [prefix,actual] of [wrong,['lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_q','ambiguous'],['lora_unet_not_a_weight','none']]){
    const rejected=await evaluate(`new Promise(resolve=>{const prefix=${JSON.stringify(prefix)},meta={};meta[prefix+'.lora_down.weight']={dtype:'F16',shape:[1,1280],data_offsets:[0,2560]};meta[prefix+'.lora_up.weight']={dtype:'F16',shape:[1280,1],data_offsets:[2560,5120]};const h=new TextEncoder().encode(JSON.stringify(meta)),b=new Uint8Array(8+h.length+5120);new DataView(b.buffer).setBigUint64(0,BigInt(h.length),true);b.set(h,8);_gfiles['mismatch']=new File([b],'mismatch');const held=gpu_buf_alloc(4),object=_gb[held];state_set_text('bl-weights',String(held));const before=_gb.filter(Boolean).length;let calls=0;${load("'must-not-load'","'mismatch'",'BigInt(b.length)','()=>resolve({accepted:true})',"s=>{calls++;const result={s,calls,before,after:_gb.filter(Boolean).length,preserved:_gb[held]===object};gpu_buf_free(held);state_set_text('bl-weights','');resolve(result);}")};})`);
    if(rejected.accepted||rejected.calls!==1||rejected.before!==rejected.after||!rejected.preserved||!rejected.s.includes('family mismatch: expected '+family+', found '+actual))throw new Error('Family mismatch control failed '+JSON.stringify(rejected));
    console.log('PASS Spark classifier refuses '+actual+' LoRA before changing existing GPU ownership');
  }
  await evaluate(`window.__merges=[];window.__target='';window.__kind=false;window.__originalPatch=browser_lora_patch__tb;browser_lora_patch__tb=function(...a){const slots=blo_slots(a[2]),module=slots.length?lora_module(a[1],slots[0],0n):'';if(module){__target=a[2].bt_name;__kind=!!(slots[0].ls_bias||lora_has(a[1],module+'.dora_scale')||!lora_has(a[1],module+'.lora_down.weight'));const next=a[4];a[4]=why=>{if(why==='')__merges.push(a[2].bt_name);return next(why);};}return __originalPatch(...a);};`);
  const imageCases=[...evidence.images,{...evidence.images.find(r=>r.weight100===0),plain:true}].filter(r=>!single||(r.plain?'plain':String(r.weight100))===single);
  for(const row of imageCases){
    const native=readFileSync(join(nativeDir,row.file));if(hash(native)!==row.sha256)throw new Error('Native image changed');
    const memory=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));if(memory.FreePhysicalMemory<=1572864||memory.FreeVirtualMemory<=26214400)throw new Error('RAM/commit admission refused');
    await evaluate(`window.__DATA.weight100=${JSON.stringify(String(row.weight100))};window.__DATA.plain=${JSON.stringify(row.plain?'1':'0')};_st.done=0n;_st.error='';__merges.length=0;`);
    const started=performance.now();
    if(pick===0){for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});await wait('Number(_st.picked)===1');for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});}
    else await evaluate(`la_lora(JSON.stringify({name:String(_st.lora),size:${loraSize}}))`);
    await wait('Number(_st.done)===1');
    const state=await evaluate(`({active:_gb.filter(Boolean).length,error:_gerr,merges:__merges.slice(),heap:_hp,summary:String(_st.summary)})`);
    const count=row.plain?0:(evidence.browserTargets||3);
    if(state.active||state.error||state.merges.length!==count||new Set(state.merges).size!==count)throw new Error('Merge/cleanup contract differs '+JSON.stringify(state));
    const png=Buffer.from(await evaluate("document.querySelector('#out').toDataURL('image/png').split(',')[1]"),'base64');writeFileSync(join(outDir,'browser-'+(row.plain?'plain':row.weight100)+'.png'),png);
    const comparison=await evaluate(`new Promise((resolve,reject)=>{const im=new Image();im.onload=()=>{if(im.width!==${size}||im.height!==${size}){reject(new Error('Native dimensions differ'));return;}const c=document.createElement('canvas');c.width=${size};c.height=${size};const ctx=c.getContext('2d');ctx.drawImage(im,0,0);const a=ctx.getImageData(0,0,${size},${size}).data,b=document.querySelector('#out').getContext('2d').getImageData(0,0,${size},${size}).data;let sum=0,max=0;for(let i=0;i<a.length;i++)if(i%4!==3){const d=Math.abs(a[i]-b[i]);sum+=d;max=Math.max(max,d)}resolve({mean:sum/(${size}*${size}*3),max});};im.onerror=reject;im.src=${JSON.stringify('data:image/png;base64,'+native.toString('base64'))};})`);
    if(!(comparison.mean<4))throw new Error('LoRA exceeds native mean<4/255 grade '+JSON.stringify(comparison));
    const result={weight100:row.weight100,plain:!!row.plain,ms:performance.now()-started,comparison,state,sha256:hash(png),memory};rows.push(result);console.log('PASS image '+JSON.stringify({...result,state:{...state,merges:state.merges.length}}));
    const trimmed=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);if(trimmed!=='')throw new Error('Trim failed '+trimmed);
  }
  if(!single){
    if(new Set(rows.filter(r=>!r.plain).map(r=>r.sha256)).size!==evidence.images.length)throw new Error('Browser LoRA strength control did not change pixels');
    if(rows.find(r=>r.plain).sha256!==rows.find(r=>!r.plain&&r.weight100===0).sha256)throw new Error('Plain loader differs from zero-strength LoRA');
  }
  if(single==='controls'){
    for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});await wait('Number(_st.picked)===1');
    for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:5,y:5,button:'left',clickCount:1});await wait('Number(_st.picked)===2');
  }
  if(!single||single==='controls')for(const fault of faultNames.filter(f=>!onlyFault||f===onlyFault)){
    const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));
    if(m.FreePhysicalMemory<=1572864||m.FreeVirtualMemory<=26214400)throw new Error('Fault case RAM/commit admission refused '+fault);
    const failure=await evaluate(`new Promise(resolve=>{const upload=gpu_buf_upload_then,launch=gpu_launch,done=gpu_done_then,trim=gpu_trim_then,alloc=gpu_buf_alloc,copy=gpu_buf_copy_range,work=blo_work__tb,finish=blo_work_finish__tb,inputs=blo_inputs__tb,merged=blo_merged__tb;let hits=0,calls=0;const events=[],hit=phase=>{hits++;events.push({phase,target:__target,lycoris:__kind});};const restore=()=>{gpu_buf_upload_then=upload;gpu_launch=launch;gpu_done_then=done;gpu_trim_then=trim;gpu_buf_alloc=alloc;gpu_buf_copy_range=copy;blo_work__tb=work;blo_work_finish__tb=finish;blo_inputs__tb=inputs;blo_merged__tb=merged};
      const fault=${JSON.stringify(fault)},wanted=()=>(!${complex}||__kind)&&(!fault.includes('-')||(fault.endsWith('-g')?__target.startsWith('conditioner.embedders.1.'):__target.startsWith('model.diffusion_model.')));
      if(fault.startsWith('upload'))gpu_buf_upload_then=function(...a){if(a[2]===String(_st.lora)&&wanted()){hit('upload');a[7](JSON.stringify({ok:false,error:'injected LoRA upload'}));return 0n;}return upload(...a)};
      if(fault.startsWith('merge'))gpu_launch=function(...a){if(['blw_delta_main','blw_axpy_main','blw_hada_main','blw_prod_main','blw_sum2_main','blw_kron_main'].includes(a[1])&&wanted()){hit(a[1]);return -1n;}return launch(...a)};
      if(fault.startsWith('completion')&&!${complex})gpu_done_then=function(cb){if(wanted()){hit('completion');cb('injected completion failure');return 0n;}return done(cb);};
      if((fault.startsWith('completion')&&${complex})||fault==='pack')blo_work_finish__tb=function(...a){if(!wanted()||a[4])return finish(...a);if(fault==='pack'){gpu_launch=function(...q){if(q[1]==='blw_pack_main'){hit('pack');return -1n;}return launch(...q);};gpu_buf_copy_range=()=>{hit('pack-copy');return false;};}else gpu_done_then=cb=>{hit('final-completion');cb('injected final completion');return 0n;};try{return finish(...a);}finally{gpu_launch=launch;gpu_buf_copy_range=copy;gpu_done_then=done;}};
      if(${JSON.stringify(fault)}==='trim')gpu_trim_then=function(cb){hits++;cb('injected trim failure');return 0n;};
      if(fault==='mid-alloc')gpu_buf_alloc=function(n){if(Number(n)===576&&__target==='model.diffusion_model.input_blocks.1.0.in_layers.2.weight'){hits++;return 0n;}return alloc(n);};
      if(fault==='mid-upload')gpu_buf_upload_then=function(...a){if(a[2]===String(_st.lora)&&Number(a[3])===${midOffset}){hits++;a[7](JSON.stringify({ok:false,error:'injected mid upload'}));return 0n;}return upload(...a);};
      if(fault==='fold'||fault==='fold-completion'){let folded=false;gpu_launch=function(...a){if(a[1]==='blw_mid_main'){folded=true;if(fault==='fold'){hits++;return -1n;}}return launch(...a);};gpu_done_then=function(cb){if(folded&&fault==='fold-completion'){hits++;cb('injected fold completion failure');return 0n;}return done(cb);};}
      if(fault==='work-alloc')blo_work__tb=function(...a){gpu_buf_alloc=()=>{hit('work-alloc');return 0n;};try{return work(...a);}finally{gpu_buf_alloc=alloc;}};
      if(fault==='input-alloc')blo_inputs__tb=function(...a){if(Number(a[3])!==(a[2].length>1?1:0))return inputs(...a);gpu_buf_alloc=()=>{hit('input-alloc');return 0n;};try{return inputs(...a);}finally{gpu_buf_alloc=alloc;}};
      if(fault==='dora-alloc')blo_work__tb=function(...a){if(a[3]||!wanted())return work(...a);let calls=0;gpu_buf_alloc=n=>{if(++calls===3){hit('dora-norm-allocation');return 0n;}return alloc(n);};try{return work(...a);}finally{gpu_buf_alloc=alloc;}};
      if(['dora-zero','dora-norm','dora-apply'].includes(fault))gpu_launch=function(...a){const name=fault==='dora-zero'?'blw_zero_main':fault==='dora-norm'?'blw_dora_norm_main':'blw_dora_apply_main';if(a[1]===name&&wanted()){hit(fault);return -1n;}return launch(...a);};
      if(fault==='delta-completion')blo_merged__tb=function(...a){if(!a[9]||!wanted())return merged(...a);gpu_done_then=cb=>{hit('delta-completion');cb('injected delta completion');return 0n;};try{return merged(...a);}finally{gpu_done_then=done;}};
      if(fault==='tucker'||fault==='matmul')gpu_launch=function(...a){if(a[1]==='blw_'+fault+'_main'){hit(fault);return -1n;}return launch(...a);};
      if(fault==='tucker-second'||fault==='matmul-second'){let seen=0,first='';const name=fault.split('-')[0];gpu_launch=function(...a){const eligible=name==='tucker'||__target==='model.diffusion_model.input_blocks.4.1.transformer_blocks.0.attn1.to_out.0.weight';if(eligible&&a[1]==='blw_'+name+'_main'){if(++seen===1)first=__target;if(seen===2&&first===__target){hit(fault);return -1n;}}return launch(...a);};}
      ${load('_st.checkpoint','String(_st.lora)',loraSize+'n','()=>{restore();resolve({accepted:true})}',"s=>{calls++;restore();setTimeout(()=>resolve({s,hits,calls,events,active:_gb.filter(Boolean).length}),30);}")};})`);
    if(failure.accepted||failure.hits!==1||failure.calls!==1||failure.active!==0||!(/LoRA|LoCon|LyCORIS|DoRA/.test(failure.s)))throw new Error('Injected failure leaked/repeated '+JSON.stringify({fault,failure}));
    const allocation={ 'mid-alloc':'LoCon scratch allocation failed', 'work-alloc':'LyCORIS work allocation failed', 'input-alloc':'LyCORIS input allocation failed', 'dora-alloc':'DoRA scratch allocation failed' }[fault];if(allocation&&!failure.s.includes(allocation))throw new Error('Allocation fault missed its guard '+JSON.stringify({fault,failure}));
    if(complex&&fault!=='trim'&&(failure.events.length!==1||!failure.events[0].lycoris))throw new Error('Fault missed LyCORIS '+JSON.stringify({fault,failure}));
    faults.push({fault,...failure});
    const trimmed=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);if(trimmed!=='')throw new Error('Failure trim failed '+trimmed);
    const device=await evaluate(`({error:_gerr,events:window.__gpuErrors||[]})`);if(device.error||device.events.length)throw new Error('Device error after '+fault+': '+JSON.stringify(device));
    console.log('PASS '+fault+' after allocation: one refusal callback and no live handles '+JSON.stringify(failure.events));
  }
  if((!single||single==='controls')&&(!onlyFault||onlyFault==='ancillary')){
  const missing=await evaluate(`new Promise(resolve=>{let calls=0;${load("'missing-checkpoint'",'String(_st.lora)',loraSize+'n','()=>resolve({accepted:true})',"s=>{calls++;setTimeout(()=>resolve({s,calls,active:_gb.filter(Boolean).length}),30);}")};})`);
  if(missing.accepted||missing.calls!==1||missing.active||!missing.s.includes(xl?'SDXL header length is invalid':'checkpoint header length is invalid'))throw new Error('Missing checkpoint did not refuse cleanly '+JSON.stringify(missing));
  console.log('PASS missing checkpoint refuses exactly once without live handles');
  if(malformedMid){
    const bad=await evaluate(`new Promise(resolve=>{const bytes=Uint8Array.from(atob(${JSON.stringify(malformedMid.toString('base64'))}),c=>c.charCodeAt(0));_gfiles['bad-mid']=new File([bytes],'bad-mid');let calls=0;${load('_st.checkpoint',"'bad-mid'",malformedMid.length+'n','()=>resolve({accepted:true})',"s=>{calls++;setTimeout(()=>resolve({s,calls,active:_gb.filter(Boolean).length}),30);}")};})`);
    if(bad.accepted||bad.calls!==1||bad.active||!bad.s.includes('LoRA/LoCon shape mismatch'))throw new Error('Malformed LoCon accepted/leaked '+JSON.stringify(bad));
    const trimmed=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);if(trimmed!=='')throw new Error('Malformed LoCon trim failed');
    console.log('PASS incompatible mid rank refuses exactly once without live handles');
  }
  if(malformedDora){
    const bad=await evaluate(`new Promise(resolve=>{const bytes=Uint8Array.from(atob(${JSON.stringify(malformedDora.toString('base64'))}),c=>c.charCodeAt(0));_gfiles['bad-dora']=new File([bytes],'bad-dora');let calls=0;${load('_st.checkpoint',"'bad-dora'",malformedDora.length+'n','()=>resolve({accepted:true})',"s=>{calls++;setTimeout(()=>resolve({s,calls,active:_gb.filter(Boolean).length}),30);}")};})`);
    if(bad.accepted||bad.calls!==1||bad.active||!bad.s.includes('DoRA scale shape mismatch'))throw new Error('Malformed DoRA accepted/leaked '+JSON.stringify(bad));
    const trimmed=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);if(trimmed!=='')throw new Error('Malformed DoRA trim failed');
    console.log('PASS incompatible DoRA channel count refuses once without live handles');
  }
  if(malformedKind){
    const bad=await evaluate(`new Promise(resolve=>{const bytes=Uint8Array.from(atob(${JSON.stringify(malformedKind.toString('base64'))}),c=>c.charCodeAt(0));_gfiles['bad-kind']=new File([bytes],'bad-kind');let calls=0;${load('_st.checkpoint',"'bad-kind'",malformedKind.length+'n','()=>resolve({accepted:true})',"s=>{calls++;setTimeout(()=>resolve({s,calls,active:_gb.filter(Boolean).length}),30);}")};})`);
    if(bad.accepted||bad.calls!==1||bad.active||!bad.s.includes('LyCORIS')||!bad.s.includes('shape mismatch'))throw new Error('Malformed LyCORIS accepted/leaked '+JSON.stringify(bad));
    const trimmed=await evaluate(`new Promise(r=>gpu_trim_then(s=>r(s)))`);if(trimmed!=='')throw new Error('Malformed LyCORIS trim failed');
    console.log('PASS incompatible '+evidence.variant+' shape refuses exactly once without live handles');
  }
  }
  writeFileSync(join(outDir,recordName(single,onlyFault)),JSON.stringify({family,variant:evidence.variant||'',nativeEvidenceSha256,pageSha256:hash(html),shaderSha256:hash(code),baseShaderSha256:hash(data.bk),clipShaderSha256:xl?hash(data['clip-xl']):null,rows,faults},null,2)+'\n');console.log('PASS '+family+' LoRA '+(single||'images, picked files, native parity and cleanup'));
}finally{
  if(socket)socket.close();if(edge&&edge.exitCode===null)edge.kill();
  execFileSync('pwsh',['-NoProfile','-Command',`function Owned {@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"|Where-Object {$_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*'})};foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup failed'`],{stdio:'inherit'});
  if(server)server.close();
}
