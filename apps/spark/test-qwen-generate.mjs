import {readFileSync,openSync,readSync,closeSync,statSync,existsSync,createReadStream} from 'node:fs';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {withGpuPage} from './qwen-gpu.mjs';
import {parseQwenGguf} from './qwen-gguf.mjs';
import {createQwenTokenizer} from './qwen-tokenizer.mjs';
import {buildQwenTokenizer} from './build-qwen-tokenizer.mjs';
import {qwenRuntime,QwenDecoder,qwOverCdp} from './qwen-forward.mjs';

const here=dirname(fileURLToPath(import.meta.url));
const [shaderArg,modelArg]=process.argv.slice(2).filter(a=>!a.startsWith('--'));
if(!shaderArg)throw new Error('Usage: node apps/spark/test-qwen-generate.mjs QwenKernels.wgsl [model.gguf] [--profile]');
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-generation-reference.json'),'utf8'));
const model=modelArg??'D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
if(!existsSync(model))throw new Error('Qwen3 GGUF reference model is missing: '+model);
const size=statSync(model).size,fd=openSync(model,'r'),b=Buffer.alloc(8*1024*1024);
try{readSync(fd,b,0,b.length,0);}finally{closeSync(fd);}
const header=b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength),parsed=parseQwenGguf(header,size);
const tokenizer=await createQwenTokenizer(header,parsed,buildQwenTokenizer(join(here,'../../build-output/spark-local/tokenizer.wasm')));
const prompt=tokenizer.encode(reference.prompt,{parseSpecial:true});
if(prompt.join(',')!==reference.promptTokens.join(','))throw new Error('Prompt tokens differ from llama.cpp: '+JSON.stringify(prompt));
const tensors=new Map([...parsed.tensors.values()].map(t=>[t.name,t]));
const route=(q,r)=>{
  const m=/^\/tensor\/(.+)$/.exec(decodeURIComponent(q.url));if(!m)return false;
  const t=tensors.get(m[1]);if(!t){r.writeHead(404);r.end();return true;}
  const end=Math.min(size,t.offset+Math.ceil(t.bytes/4)*4)-1;
  r.writeHead(200,{'Content-Type':'application/octet-stream'});createReadStream(model,{start:t.offset,end}).pipe(r);return true;
};
const context=prompt.length+reference.tokens.length+1;
await withGpuPage(readFileSync(shaderArg,'utf8'),new Map(),async(run,evaluate)=>{
  await evaluate(`(${qwenRuntime.toString()})()`);
  const decoder=new QwenDecoder(parsed,qwOverCdp(evaluate),context);
  let started=performance.now();
  const profile=process.argv.includes('--profile'),qw=qwOverCdp(evaluate),report=async phase=>{if(profile)for(const r of JSON.parse(await qw.profileTake()))console.log(JSON.stringify({profile:phase,...r,ms:Math.round(r.ns/1e4)/100}));};
  const loaded=await decoder.load(readFileSync(shaderArg,'utf8'),t=>'/tensor/'+encodeURIComponent(t.name),profile);
  console.log(JSON.stringify({adapter:loaded.description,uploadedBytes:loaded.bytes,loadMs:Math.round(performance.now()-started)}));
  started=performance.now();let result;
  const aligned=[];let next=0;
  for(const id of reference.tokens){if(reference.steps[next]?.id===id)aligned.push(reference.steps[next++]);else aligned.push(null);}
  const ids=i=>(aligned[i]?.top??[]).map(t=>t.id),deviations=[],bound=1.5;
  result=await decoder.prefill(prompt,0,ids(0));
  console.log(JSON.stringify({promptTokens:prompt.length,prefillMs:Math.round(performance.now()-started)}));
  await report('prefill');
  const generated=[];started=performance.now();
  for(let i=0;i<reference.tokens.length;i++){
    generated.push(result.best);
    const want=reference.tokens[i];
    const onGpu=JSON.parse(await qw.argmaxGpu('logits',parsed.config.vocab)).best;if(onGpu!==result.best)throw new Error('GPU argmax '+onGpu+' differs from the host argmax '+result.best+' at step '+i);
    const deviation=Math.max(0,...(aligned[i]?.top??[]).map((t,k)=>Math.abs(result.logprobs[k]-t.logprob)));deviations.push(deviation);
    console.log(JSON.stringify({step:i,token:result.best,want,logit:result.top,logprobDeviation:deviation}));
    if(result.best!==want)throw new Error(`Token ${i} differs from llama.cpp: ${result.best} != ${want}; generated so far ${JSON.stringify(tokenizer.decode(generated))}`);
    if(tokenizer.isEnd(result.best))break;
    result=await decoder.step(result.best,prompt.length+i,ids(i+1));
  }
  console.log(JSON.stringify({generatedTokens:generated.length,decodeMs:Math.round(performance.now()-started),maxLogprobDeviation:Math.max(...deviations)}));
  await report('decode');
  if(aligned.filter(Boolean).length<reference.steps.filter(s=>s.top.length>1).length)throw new Error('Reference probability steps did not align with the tokens');
  if(Math.max(...deviations)>bound)throw new Error('Top-2 log-probabilities deviate from llama.cpp by more than '+bound+' nats');
  console.log('PASS GPU argmax equals the host argmax at all '+generated.length+' steps');
  console.log('PASS '+generated.length+' greedy tokens equal '+reference.oracle+', top-2 log-probabilities within '+bound+' nats: '+JSON.stringify(tokenizer.decode(generated)));
},route);
