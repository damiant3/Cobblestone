import {readFileSync,writeFileSync,mkdtempSync,openSync,readSync,closeSync,statSync,existsSync} from 'node:fs';
import {dirname,join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {withGpuPage,SENTINEL} from './qwen-gpu.mjs';
import {parseQwenGguf} from './qwen-gguf.mjs';

const here=dirname(fileURLToPath(import.meta.url)),repo=resolve(here,'../..');
const [shaderArg,modelArg]=process.argv.slice(2);
if(!shaderArg)throw new Error('Usage: node apps/spark/test-qwen-dequant.mjs QwenKernels.wgsl [model.gguf]');
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-gguf-directory.json'),'utf8'));
const model=modelArg??'D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
if(!existsSync(model))throw new Error('Qwen3 GGUF reference model is missing: '+model);
const commit='7fe450e19305b828c199d602c23a8337aaa1f03b',python='D:/AI/DiffusionForge/system/python/python.exe';
const ggufPy=join(tmpdir(),'llama-gguf-py');
if(!existsSync(join(ggufPy,'gguf-py/gguf/quants.py'))){
  execFileSync('git',['clone','--quiet','--filter=blob:none','--no-checkout','https://github.com/ggml-org/llama.cpp.git',ggufPy],{stdio:'inherit'});
  execFileSync('git',['-C',ggufPy,'sparse-checkout','set','gguf-py'],{stdio:'inherit'});
  execFileSync('git',['-C',ggufPy,'checkout','--quiet',commit],{stdio:'inherit'});
}
if(execFileSync('git',['-C',ggufPy,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!==commit)throw new Error('gguf-py revision differs from the pinned oracle');
if(execFileSync('git',['-C',ggufPy,'status','--porcelain'],{encoding:'utf8'}).trim())throw new Error('gguf-py oracle has local changes');

const size=statSync(model).size,fd=openSync(model,'r');
const read=(at,n)=>{const b=Buffer.alloc(n);if(readSync(fd,b,0,n,at)!==n)throw new Error('Model read');return b;};
const parsed=parseQwenGguf((b=>b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength))(read(0,8*1024*1024)),size);
const formats={12:{name:'Q4_K',bytes:144,kernel:'q4k_dequant'},14:{name:'Q6_K',bytes:210,kernel:'q6k_dequant'}};
const subjects=['token_embd.weight','blk.0.attn_q.weight','blk.35.ffn_up.weight','output.weight','blk.0.ffn_down.weight'];
const work=mkdtempSync(join(tmpdir(),'qwen-dequant-')),cases=[];
try{
  for(const name of subjects){
    const t=parsed.tensors.get(name),f=formats[t?.type];if(!f)throw new Error('Subject is not Q4_K or Q6_K: '+name);
    const total=t.bytes/f.bytes,picks=[];
    for(let i=0;i<32;i++)picks.push(i,total-32+i);
    for(let i=0;i<64;i++)picks.push(Math.floor((i*2654435761%4294967296)/4294967296*total));
    const raw=Buffer.concat(picks.map(b=>read(t.offset+b*f.bytes,f.bytes)));
    writeFileSync(join(work,cases.length+'.bin'),raw);
    cases.push({name,format:f,blocks:picks.length,raw});
  }
} finally {closeSync(fd);}
const oracle=`import sys,numpy as np
sys.dont_write_bytecode=True
sys.path.insert(0,sys.argv[1])
from gguf.quants import dequantize
from gguf.constants import GGMLQuantizationType as T
for spec in sys.argv[3:]:
    i,kind=spec.split(':')
    raw=np.fromfile(sys.argv[2]+'/'+i+'.bin',dtype=np.uint8)
    dequantize(raw,T[kind]).astype(np.float32).tofile(sys.argv[2]+'/'+i+'.f32')
`;
writeFileSync(join(work,'oracle.py'),oracle);
execFileSync(python,[join(work,'oracle.py'),join(ggufPy,'gguf-py'),work,...cases.map((c,i)=>i+':'+c.format.name)],{stdio:'inherit'});
for(const [i,c] of cases.entries()){const b=readFileSync(join(work,i+'.f32'));c.expected=new Uint32Array(b.buffer,b.byteOffset,b.byteLength/4);if(c.expected.length!==c.blocks*256)throw new Error('Oracle length differs '+c.name);}

const code=readFileSync(shaderArg,'utf8'),blobs=new Map(cases.map((c,i)=>{const b=Buffer.alloc(Math.ceil(c.raw.length/4)*4);c.raw.copy(b);return ['w'+i,b];}));
const results=await withGpuPage(code,blobs,run=>run(cases.map((c,i)=>({kernel:c.format.kernel,inputs:{wb:'w'+i},scalars:{n:c.blocks*256},threads:c.blocks*256,out:'yb',outWords:c.blocks*256}))));
let failed=0;
for(const [i,c] of cases.entries()){
  const words=results[i],n=c.blocks*256,actual=new Float32Array(words.buffer),expected=new Float32Array(c.expected.buffer,c.expected.byteOffset,n);
  let bits=0,worst=0;for(let k=0;k<n;k++){if(words[k]!==c.expected[k])bits++;worst=Math.max(worst,Math.abs(actual[k]-expected[k]));}
  const tail=words.subarray(n).some(v=>v!==SENTINEL);
  console.log(JSON.stringify({tensor:c.name,format:c.format.name,blocks:c.blocks,values:n,bitMismatches:bits,maxAbs:worst,tailTouched:tail}));
  if(bits||tail)failed++;
}
if(failed)throw new Error(failed+' dequantization subjects differ from gguf-py');
console.log('PASS '+cases.length+' tensors, '+cases.reduce((s,c)=>s+c.blocks*256,0)+' values bit-identical to llama.cpp gguf-py '+commit.slice(0,9));
