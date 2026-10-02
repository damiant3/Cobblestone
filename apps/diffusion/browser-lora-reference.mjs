import {readFileSync,writeFileSync,mkdirSync,existsSync,createReadStream,copyFileSync,openSync,readSync,closeSync} from 'node:fs';
import {resolve,dirname,join,relative,isAbsolute} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {execFileSync,spawnSync} from 'node:child_process';
import {inflateSync} from 'node:zlib';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../..'),out=process.argv[2]&&resolve(process.argv[2]);
const family=process.argv[3]||'sd15',xl=family==='sdxl',size=xl?1024:512;
const variant=process.argv[4]||'',locon=variant==='LoCon',dora=variant==='DoRA',lycoris=['LoHa','LoKr','GLoRA','Diff'].includes(variant),format=locon||lycoris||dora,dtype=format?'':variant;
if(!out||/\s/.test(out)||existsSync(out)||!['sd15','sdxl'].includes(family)||!['','BF16','F8_E4M3','F8_E5M2'].includes(dtype))throw new Error('Usage: browser-lora-reference.mjs new-output-directory-without-spaces [sd15|sdxl] [BF16|F8_E4M3|F8_E5M2|LoCon|LoHa|LoKr|GLoRA|Diff|DoRA]');
const local=out&&relative(repo,out);if((xl||dtype||format)&&(local.startsWith('..')||isAbsolute(local)))throw new Error('Converted/SDXL evidence directory must be inside the repository');
mkdirSync(out,{recursive:true});
const hash=async path=>{const h=createHash('sha256');for await(const b of createReadStream(path))h.update(b);return h.digest('hex');};
const memory=()=>{const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));if(m.FreePhysicalMemory<=1572864||m.FreeVirtualMemory<=26214400)throw new Error('RAM/commit admission refused');console.log('Memory '+JSON.stringify(m));return m;};
const run=(script,args,label)=>{const r=spawnSync('pwsh',['-NoProfile','-File',join(repo,script),...args],{cwd:repo,encoding:'utf8',windowsHide:true,maxBuffer:16777216});writeFileSync(join(out,label+'.out'),r.stdout||'');writeFileSync(join(out,label+'.err'),r.stderr||'');if(r.stdout)console.log(r.stdout.trim());if(r.stderr)console.error(r.stderr.trim());if(r.error)throw r.error;if(r.status!==0)throw new Error(label+' failed '+r.status);};
const source=join(repo,'apps/diffusion/BrowserLoraReference.codex'),kernel=join(repo,'seed/Codex.cdx');
const checkpoint=join(repo,'build-output/diffusion-models',xl?'dreamshaperXL_lightningDPMSDE.safetensors':'realisticVisionV60B1_v20Novae.safetensors');
const originalLora=format?join(repo,'codex/test/gpu-files/lora-kinds.safetensors'):xl?'D:/AI/DiffusionForge/webui/models/Lora/9HNHMWJZDSGD8WE7FFJCRVB8M0.safetensors':join(repo,'codex/test/gpu-files/lora-sd15.safetensors');
const lora=xl||dtype||format?join(out,'lora.safetensors'):originalLora;
if(xl&&!dtype&&!format)copyFileSync(originalLora,lora);
if(locon){
  const src=readFileSync(originalLora),offset=8+Number(src.readBigUInt64LE(0)),meta=JSON.parse(src.toString('utf8',8,offset)),target={},chunks=[];let at=0;
  const add=(name,shape,bytes)=>{target[name]={dtype:'F16',shape,data_offsets:[at,at+bytes.length]};at+=bytes.length;chunks.push(bytes);};
  const prefix='lora_unet_input_blocks_1_0_in_layers_2';
  for(const suffix of ['.alpha','.lora_down.weight','.lora_mid.weight','.lora_up.weight']){const m=meta[prefix+suffix];if(!m||m.dtype!=='F16')throw new Error('LoCon fixture differs');add(prefix+suffix,m.shape,src.subarray(offset+m.data_offsets[0],offset+m.data_offsets[1]));}
  const anchor=xl?'lora_te2_text_model_encoder_layers_0_self_attn_q_proj':'lora_unet_input_blocks_1_1_transformer_blocks_0_attn1_to_q',n=xl?1280:320,one=Buffer.alloc(2);one.writeUInt16LE(0x3c00);
  add(anchor+'.alpha',[],one);add(anchor+'.lora_down.weight',[1,n],Buffer.alloc(n*2));add(anchor+'.lora_up.weight',[n,1],Buffer.alloc(n*2));
  const json=Buffer.from(JSON.stringify(target)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));writeFileSync(lora,Buffer.concat([len,json,...chunks]));
}
if(dora){
  const src=readFileSync(originalLora),offset=8+Number(src.readBigUInt64LE(0)),meta=JSON.parse(src.toString('utf8',8,offset)),target={},chunks=[];let at=0;
  const add=(name,dtype,shape,bytes)=>{target[name]={dtype,shape,data_offsets:[at,at+bytes.length]};at+=bytes.length;chunks.push(bytes);};
  const matrix='lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_out_0',conv='lora_unet_input_blocks_1_0_in_layers_2';
  for(const [key,m]of Object.entries(meta))if(key.startsWith(matrix+'.')||key.startsWith(conv+'.'))add(key,m.dtype,m.shape,src.subarray(offset+m.data_offsets[0],offset+m.data_offsets[1]));
  const scale=meta[matrix+'.dora_scale'];if(scale.dtype!=='BF16'||scale.shape.join(',')!=='1,640')throw new Error('DoRA fixture scale differs');
  add(conv+'.dora_scale','BF16',[1,320,1,1],src.subarray(offset+scale.data_offsets[0],offset+scale.data_offsets[0]+640));
  const anchor=xl?'lora_te2_text_model_encoder_layers_0_self_attn_q_proj':'lora_unet_input_blocks_1_1_transformer_blocks_0_attn1_to_q',n=xl?1280:320,one=Buffer.alloc(2);one.writeUInt16LE(0x3c00);
  add(anchor+'.alpha','F16',[],one);add(anchor+'.lora_down.weight','F16',[1,n],Buffer.alloc(n*2));add(anchor+'.lora_up.weight','F16',[n,1],Buffer.alloc(n*2));
  const json=Buffer.from(JSON.stringify(target)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));writeFileSync(lora,Buffer.concat([len,json,...chunks]));
}
if(lycoris){
  const src=readFileSync(originalLora),offset=8+Number(src.readBigUInt64LE(0)),meta=JSON.parse(src.toString('utf8',8,offset)),target={},chunks=[];let at=0;
  const width={F16:2,BF16:2,F32:4},selected=new Set();
  const add=(name,dtype,shape,bytes)=>{if(!width[dtype])throw new Error('Unsupported fixture dtype');target[name]={dtype,shape,data_offsets:[at,at+bytes.length]};at+=bytes.length;chunks.push(bytes);};
  for(const key of Object.keys(meta)){const suffix=key.slice(key.indexOf('.'));if((variant==='LoHa'&&suffix.startsWith('.hada_'))||(variant==='LoKr'&&suffix.startsWith('.lokr_'))||(variant==='GLoRA'&&/^[.][ab][12][.]weight$/.test(suffix))||(variant==='Diff'&&['.diff','.diff_b','.w_norm','.b_norm'].includes(suffix)))selected.add(key.slice(0,key.indexOf('.')));}
  for(const [key,m] of Object.entries(meta)){
    const module=key.slice(0,key.indexOf('.')),suffix=key.slice(key.indexOf('.'));if(!selected.has(module))continue;
    const wanted=suffix==='.alpha'||(variant==='LoHa'&&suffix.startsWith('.hada_'))||(variant==='LoKr'&&suffix.startsWith('.lokr_'))||(variant==='GLoRA'&&/^[.][ab][12][.]weight$/.test(suffix))||(variant==='Diff'&&['.diff','.diff_b','.w_norm','.b_norm'].includes(suffix));if(!wanted)continue;
    let name=key,shape=m.shape,bytes=src.subarray(offset+m.data_offsets[0],offset+m.data_offsets[1]);
    if(!xl&&key.startsWith('lora_te2_')){name=key.replace('lora_te2_text_model_encoder_layers_20_','lora_te_text_model_encoder_layers_3_');if(name===key||suffix!=='.diff_b'||shape.join(',')!=='1280')throw new Error('CLIP bias fixture differs');shape=[768];bytes=bytes.subarray(0,768*width[m.dtype]);}
    add(name,m.dtype,shape,bytes);
  }
  if(variant==='LoKr'){
    const p='lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_v',q='lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_q',a=meta[p+'.lokr_w2_a'],b=meta[p+'.lokr_w2_b'],w1=meta[p+'.lokr_w1'];
    if(a.shape.join(',')!=='80,4'||b.shape.join(',')!=='4,80'||w1.shape.join(',')!=='8,8')throw new Error('LoKr factor fixture differs');
    const word=new Uint32Array(1),float=new Float32Array(word.buffer),half=h=>{const s=h&32768?-1:1,e=(h>>>10)&31,m=h&1023;return s*(e===0?m*2**-24:e===31?(m?NaN:Infinity):(1+m/1024)*2**(e-15));};
    const value=(m,i)=>{const pos=offset+m.data_offsets[0]+i*width[m.dtype];if(m.dtype==='F32')return src.readFloatLE(pos);const h=src.readUInt16LE(pos);if(m.dtype==='F16')return half(h);word[0]=h*65536;return float[0];};
    const dense=Buffer.alloc(80*80*4);for(let i=0;i<80;i++)for(let j=0;j<80;j++){let sum=0;for(let k=0;k<4;k++)sum+=value(a,i*4+k)*value(b,k*80+j);dense.writeFloatLE(sum,(i*80+j)*4);}
    add(q+'.lokr_w1',w1.dtype,w1.shape,src.subarray(offset+w1.data_offsets[0],offset+w1.data_offsets[1]));add(q+'.lokr_w2','F32',[80,80],dense);const alpha=Buffer.alloc(4);alpha.writeFloatLE(7);add(q+'.alpha','F32',[],alpha);
    const both='lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_out_0',first='lora_unet_input_blocks_4_1_transformer_blocks_0_attn2_to_q';
    for(const [from,suffix] of [[first,'.lokr_w1_a'],[first,'.lokr_w1_b'],[p,'.lokr_w2_a'],[p,'.lokr_w2_b']]){const m=meta[from+suffix];add(both+suffix,m.dtype,m.shape,src.subarray(offset+m.data_offsets[0],offset+m.data_offsets[1]));}
    const bothAlpha=Buffer.alloc(4);bothAlpha.writeFloatLE(3);add(both+'.alpha','F32',[],bothAlpha);
  }
  const anchor=xl?'lora_te2_text_model_encoder_layers_0_self_attn_q_proj':'lora_unet_input_blocks_1_1_transformer_blocks_0_attn1_to_q',n=xl?1280:320,one=Buffer.alloc(2);one.writeUInt16LE(0x3c00);
  add(anchor+'.alpha','F16',[],one);add(anchor+'.lora_down.weight','F16',[1,n],Buffer.alloc(n*2));add(anchor+'.lora_up.weight','F16',[n,1],Buffer.alloc(n*2));
  const json=Buffer.from(JSON.stringify(target)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));writeFileSync(lora,Buffer.concat([len,json,...chunks]));
}
if(dtype){
  const src=readFileSync(originalLora),offset=8+Number(src.readBigUInt64LE(0)),meta=JSON.parse(src.toString('utf8',8,offset)),chunks=[],target={};let at=0;
  const half=h=>{const s=h&32768?-1:1,e=(h>>>10)&31,m=h&1023;return s*(e===0?m*2**-24:e===31?(m?NaN:Infinity):(1+m/1024)*2**(e-15));};
  const fp8=b=>{if(dtype==='F8_E5M2')return half(b<<8);const e=b>>>3,m=b&7;return e===15&&m===7?NaN:e===0?m*2**-9:(m+8)*2**(e-10);};
  const levels=Array.from({length:128},(_,b)=>fp8(b)),lut=new Uint16Array(65536),f=new Float32Array(1),w=new Uint32Array(f.buffer);
  for(let h=0;h<65536;h++){
    const v=half(h);if(!Number.isFinite(v))continue;
    if(dtype==='BF16'){f[0]=v;lut[h]=((w[0]+32767+((w[0]>>>16)&1))>>>16)&65535;}
    else {const a=Math.abs(v);let lo=0,hi=levels.findIndex(x=>!Number.isFinite(x))-1;if(hi<0)hi=127;while(lo<hi){const mid=(lo+hi+1)>>>1;if(levels[mid]<=a)lo=mid;else hi=mid-1;}const upper=lo+1;if(upper<128&&Number.isFinite(levels[upper])&&(a-levels[lo]>levels[upper]-a||(a-levels[lo]===levels[upper]-a&&(lo&1))))lo=upper;lut[h]=lo|((h>>>8)&128);}
  }
  for(const [name,m] of Object.entries(meta)){
    if(name==='__metadata__')continue;
    if(m.dtype!=='F16')throw new Error('Conversion fixture requires original F16: '+name);
    const n=(m.data_offsets[1]-m.data_offsets[0])/2,bytes=Buffer.alloc(n*(dtype==='BF16'?2:1));
    for(let i=0;i<n;i++){const h=src.readUInt16LE(offset+m.data_offsets[0]+i*2);if(!Number.isFinite(half(h)))throw new Error('Nonfinite fixture input');if(dtype==='BF16')bytes.writeUInt16LE(lut[h],i*2);else bytes[i]=lut[h];}
    target[name]={dtype,shape:m.shape,data_offsets:[at,at+bytes.length]};at+=bytes.length;chunks.push(bytes);
  }
  const json=Buffer.from(JSON.stringify(target)),len=Buffer.alloc(8);len.writeBigUInt64LE(BigInt(json.length));writeFileSync(lora,Buffer.concat([len,json,...chunks]));
}
const fd=openSync(lora,'r');let header;
try{const len=Buffer.alloc(8);if(readSync(fd,len,0,8,0)!==8)throw new Error('LoRA length read failed');const n=Number(len.readBigUInt64LE(0));if(n<=0||n>16777216)throw new Error('LoRA header length refused');const b=Buffer.alloc(n);if(readSync(fd,b,0,n,8)!==n)throw new Error('LoRA header truncated');header=JSON.parse(b.toString('utf8'));}finally{closeSync(fd);}
const weightSuffixes=['.lora_down.weight','.hada_w1_a','.lokr_w1','.lokr_w1_a','.lokr_w2','.lokr_w2_a','.diff','.w_norm','.a1.weight'],biasSuffixes=['.diff_b','.b_norm'],modules=new Set();
for(const key of Object.keys(header)){for(const [suffixes,role] of [[weightSuffixes,'weight'],[biasSuffixes,'bias']])for(const suffix of suffixes)if(key.endsWith(suffix))modules.add(key.slice(0,-suffix.length)+'|'+role);}
const targets=new Set([...modules].map(k=>k.startsWith('lora_te2_')?k.replace(/self_attn_[qkv]_proj[|]/,'self_attn_qkv|'):k)).size;
const evidence={family,dtype,variant,loraFile:xl||dtype||format?'lora.safetensors':null,sourceSha256:await hash(source),kernelSha256:await hash(kernel),checkpointSha256:await hash(checkpoint),loraSha256:await hash(lora),originalLoraSha256:await hash(originalLora),browserTargets:modules.size,nativeTargets:targets,prompt:'a photo of a cat',negative:'',seed:xl?7201:7,steps:6,cfg:xl?2:7,width:size,height:size,sampler:'Euler',scheduler:xl?'Karras':'Automatic'};
if(!dtype&&!format&&evidence.loraSha256!==evidence.originalLoraSha256)throw new Error('Staged LoRA differs from original');
const cases=xl?[[0,'lora-0.png'],[60,'lora-60.png']]:[[0,'lora-0.png'],[75,'lora-75.png'],[-50,'lora-minus50.png']];
if(dora)cases.push([100,'lora-100.png']);
let root=readFileSync(source,'utf8');
const change=(a,b)=>{if(!root.includes(a))throw new Error('Reference template changed: '+a);root=root.replaceAll(a,()=>b);};
if(xl||dtype||format)change('codex/test/gpu-files/lora-sd15.safetensors',relative(repo,lora).replaceAll('\\','/'));
if(xl||format){change('applied /= 3','applied /= '+targets);change('expected three native LoRA targets','expected '+targets+' native LoRA targets');}
if(xl){
  change('build-output/diffusion-models/realisticVisionV60B1_v20Novae.safetensors',relative(repo,checkpoint).replaceAll('\\','/'));
  change('scheduler=Automatic\\nsteps=6\\ncfg=7\\nseed=7\\nwidth=512\\nheight=512','scheduler=Karras\\nsteps=6\\ncfg=2\\nseed=7201\\nwidth=1024\\nheight=1024');
  root=root.replace(/      blr-case bpe m "0\.75" "lora-75\.png"\r?\n      blr-case bpe m "-0\.5" "lora-minus50\.png"/,'      blr-case bpe m "0.6" "lora-60.png"');
  if(root.includes('lora-minus50.png'))throw new Error('Reference case template changed');
}
if(dora)change('      let closed = img2img-model-free m','      blr-case bpe m "1" "lora-100.png"\n      let closed = img2img-model-free m');
writeFileSync(join(out,'reference-root.codex'),root);evidence.generatedSourceSha256=await hash(join(out,'reference-root.codex'));
run('build/bundle-app.ps1',['-Src',join(out,'reference-root.codex'),'-Out',join(out,'reference.codex')],'bundle');
const bundlePath=join(out,'reference.codex'),bundle=readFileSync(bundlePath,'utf8');
const start=bundle.indexOf('Chapter: Diffusion--DiffusionDriver\n'),end=bundle.indexOf('\nChapter:',start+1);
if(start<0||end<0)throw new Error('Driver chapter boundary changed');
const driver=bundle.slice(start,end);
if([...driver.matchAll(/^  opening\b/gm)].length!==2)throw new Error('Driver entry shape changed');
writeFileSync(bundlePath,bundle.slice(0,start)+driver.replace(/^  opening\b/gm,'  dr-resident-opening')+bundle.slice(end));
evidence.bundleSha256=await hash(join(out,'reference.codex'));
evidence.compileMemory=memory();run('build/compile.ps1',['-Src',join(out,'reference.codex'),'-Out',join(out,'reference.cdx'),'-Log',join(out,'compile.log'),'-Kernel',kernel],'compile');
run('build/mint-clip-bpe-disk.ps1',['-Out',join(out,'clip.img')],'mint');
evidence.diskSha256=await hash(join(out,'clip.img'));evidence.cdxSha256=await hash(join(out,'reference.cdx'));
writeFileSync(join(out,'reference.vmargs'),'-gpu-files '+repo.replaceAll('\\','/')+'\n-gpu-out '+out.replaceAll('\\','/')+'\n');
evidence.runMemory=memory();run('build/test-run.ps1',['-Kernel',join(out,'reference.cdx'),'-OutFile',join(out,'result.txt'),'-DiskFile',join(out,'clip.img'),'-VmArgsFile',join(out,'reference.vmargs')],'run');
const result=readFileSync(join(out,'result.txt'),'utf8').replaceAll('\r','');
evidence.images=[];
for(const [weight100,file] of cases){
  if(!result.split('\n').some(s=>new RegExp('^'+file.replaceAll('.', '\\.')+': ok png '+size+'x'+size+': [0-9]+ bytes$').test(s)))throw new Error('Native image failed: '+result);
  const png=readFileSync(join(out,file)),idat=[];
  if(png.subarray(0,8).toString('hex')!=='89504e470d0a1a0a'||png.readUInt32BE(16)!==size||png.readUInt32BE(20)!==size||png[24]!==8||png[25]!==2)throw new Error('Native PNG shape/format changed');
  for(let off=8;off<png.length;){const n=png.readUInt32BE(off);if(off+12+n>png.length)throw new Error('Native PNG truncated');if(png.toString('ascii',off+4,off+8)==='IDAT')idat.push(png.subarray(off+8,off+8+n));off+=12+n;}
  const scanlines=inflateSync(Buffer.concat(idat));if(scanlines.length!==(size*3+1)*size)throw new Error('Native PNG payload shape changed');
  for(let y=0;y<size;y++)if(scanlines[y*(size*3+1)]!==0)throw new Error('Native PNG filter changed');
  evidence.images.push({weight100,file,sha256:await hash(join(out,file)),scanlineSha256:createHash('sha256').update(scanlines).digest('hex')});
}
if(new Set(evidence.images.map(r=>r.scanlineSha256)).size!==cases.length)throw new Error('Native LoRA strength control did not change pixels');
if(await hash(source)!==evidence.sourceSha256||await hash(kernel)!==evidence.kernelSha256||await hash(lora)!==evidence.loraSha256)throw new Error('Native inputs moved');
evidence.nativeExit=0;writeFileSync(join(out,'evidence.json'),JSON.stringify(evidence,null,2)+'\n');console.log('PASS: native DiffusionDriver '+family+' lora= images and strength controls');
