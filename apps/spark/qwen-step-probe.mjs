// Times one Qwen3 decode step inside the page: encode (QW.run up to its submit),
// the wait for the GPU, and the logits readback and argmax, against the Node-side
// step time (the rest is CDP transport and the RoPE write, which the page provider
// does not pay). Usage: node apps/spark/qwen-step-probe.mjs QwenKernels.wgsl
import {readFileSync,openSync,readSync,closeSync,statSync,createReadStream} from 'node:fs';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {withGpuPage} from './qwen-gpu.mjs';
import {parseQwenGguf} from './qwen-gguf.mjs';
import {createQwenTokenizer} from './qwen-tokenizer.mjs';
import {buildQwenTokenizer} from './build-qwen-tokenizer.mjs';
import {qwenRuntime,QwenDecoder,qwOverCdp} from './qwen-forward.mjs';

const here=dirname(fileURLToPath(import.meta.url));
const shader=process.argv[2];
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-generation-reference.json'),'utf8'));
const model='D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
const size=statSync(model).size,fd=openSync(model,'r'),b=Buffer.alloc(8*1024*1024);
try{readSync(fd,b,0,b.length,0);}finally{closeSync(fd);}
const header=b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength),parsed=parseQwenGguf(header,size);
const tokenizer=await createQwenTokenizer(header,parsed,buildQwenTokenizer(join(here,'../../build-output/spark-local/tokenizer.wasm')));
const prompt=tokenizer.encode(reference.prompt,{parseSpecial:true});
const tensors=new Map([...parsed.tensors.values()].map(t=>[t.name,t]));
const route=(q,r)=>{const m=/^\/tensor\/(.+)$/.exec(decodeURIComponent(q.url));if(!m)return false;const t=tensors.get(m[1]);if(!t){r.writeHead(404);r.end();return true;}const end=Math.min(size,t.offset+Math.ceil(t.bytes/4)*4)-1;r.writeHead(200,{'Content-Type':'application/octet-stream'});createReadStream(model,{start:t.offset,end}).pipe(r);return true;};
await withGpuPage(readFileSync(shader,'utf8'),new Map(),async(run,evaluate)=>{
  await evaluate(`(${qwenRuntime.toString()})()`);
  const decoder=new QwenDecoder(parsed,qwOverCdp(evaluate),prompt.length+20);
  await decoder.load(readFileSync(shader,'utf8'),t=>'/tensor/'+encodeURIComponent(t.name));
  await evaluate(`(()=>{window.__t={encode:0,wait:0,argmax:0,steps:0};const dev=QW.device,q=dev.queue,sub=q.submit.bind(q);let mark=0;let inRun=false;q.submit=x=>{if(inRun){const now=performance.now();__t.encode+=now-mark;mark=now;}return sub(x)};const run=QW.run;QW.run=async l=>{mark=performance.now();inRun=true;let r;try{r=await run(l);}finally{inRun=false;}__t.wait+=performance.now()-mark;__t.steps++;return r};const am=QW.argmax;QW.argmax=async(...a)=>{const s=performance.now();const r=await am(...a);__t.argmax+=performance.now()-s;return r};return 0})()`);
  let r=await decoder.prefill(prompt,0);
  await evaluate('(()=>{__t={encode:0,wait:0,argmax:0,steps:0};return 0})()');
  const node=[];
  for(let i=0;i<12;i++){const s=performance.now();r=await decoder.step(r.best,prompt.length+i);node.push(performance.now()-s);}
  const t=await evaluate('__t');
  const per=k=>Math.round(t[k]/12*10)/10;
  console.log(JSON.stringify({steps:t.steps,nodeMsPerStep:Math.round(node.reduce((a,b)=>a+b)/12*10)/10,encodeMsPerStep:per('encode'),waitMsPerStep:per('wait'),argmaxMsPerStep:per('argmax')}));
},route);
