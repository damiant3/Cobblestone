import {readFileSync,writeFileSync,mkdtempSync,openSync,readSync,closeSync,statSync,existsSync} from 'node:fs';
import {dirname,join} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {withGpuPage,SENTINEL} from './qwen-gpu.mjs';
import {parseQwenGguf} from './qwen-gguf.mjs';

const here=dirname(fileURLToPath(import.meta.url));
const [shaderArg,modelArg]=process.argv.slice(2);
if(!shaderArg)throw new Error('Usage: node apps/spark/test-qwen-matvec.mjs QwenKernels.wgsl [model.gguf]');
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-gguf-directory.json'),'utf8'));
const model=modelArg??'D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
if(!existsSync(model))throw new Error('Qwen3 GGUF reference model is missing: '+model);
const commit='7fe450e19305b828c199d602c23a8337aaa1f03b',python='D:/AI/DiffusionForge/system/python/python.exe',ggufPy=join(tmpdir(),'llama-gguf-py');
if(execFileSync('git',['-C',ggufPy,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!==commit)throw new Error('gguf-py oracle missing or at another revision; run test-qwen-dequant.mjs first');
if(execFileSync('git',['-C',ggufPy,'status','--porcelain'],{encoding:'utf8'}).trim())throw new Error('gguf-py oracle has local changes');

const size=statSync(model).size,fd=openSync(model,'r');
const read=(at,n)=>{const b=Buffer.alloc(n);if(readSync(fd,b,0,n,at)!==n)throw new Error('Model read');return b;};
const parsed=parseQwenGguf((b=>b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength))(read(0,8*1024*1024)),size);
const formats={12:{name:'Q4_K',kernel:'q4k_matvec_wg'},14:{name:'Q6_K',kernel:'q6k_matvec_wg'}};
const subjects=['blk.0.attn_q.weight','blk.35.ffn_up.weight','blk.0.ffn_down.weight'];
const work=mkdtempSync(join(tmpdir(),'qwen-matvec-')),cases=[];
let seed=12345;const next=()=>{seed=(Math.imul(seed,1103515245)+12345)>>>0;return seed/4294967296*2-1;};
try{
  for(const [i,name] of subjects.entries()){
    const t=parsed.tensors.get(name),f=formats[t?.type];if(!f)throw new Error('Subject is not Q4_K or Q6_K: '+name);
    const [cols,rows]=t.shape,raw=read(t.offset,t.bytes),x=Float32Array.from({length:cols},next);
    writeFileSync(join(work,i+'.bin'),raw);writeFileSync(join(work,i+'.x'),Buffer.from(x.buffer));
    cases.push({name,format:f,cols,rows,raw,x});
  }
} finally {closeSync(fd);}
writeFileSync(join(work,'oracle.py'),`import sys,numpy as np
sys.dont_write_bytecode=True
sys.path.insert(0,sys.argv[1])
from gguf.quants import dequantize
from gguf.constants import GGMLQuantizationType as T
for spec in sys.argv[3:]:
    i,kind,rows,cols=spec.split(':')
    w=dequantize(np.fromfile(sys.argv[2]+'/'+i+'.bin',dtype=np.uint8),T[kind]).reshape(int(rows),int(cols)).astype(np.float64)
    x=np.fromfile(sys.argv[2]+'/'+i+'.x',dtype=np.float32).astype(np.float64)
    (w@x).tofile(sys.argv[2]+'/'+i+'.y')
`);
execFileSync(python,[join(work,'oracle.py'),join(ggufPy,'gguf-py'),work,...cases.map((c,i)=>[i,c.format.name,c.rows,c.cols].join(':'))],{stdio:'inherit'});
for(const [i,c] of cases.entries()){const b=readFileSync(join(work,i+'.y'));c.expected=new Float64Array(b.buffer,b.byteOffset,b.byteLength/8);if(c.expected.length!==c.rows)throw new Error('Oracle length differs '+c.name);}

const blobs=new Map();cases.forEach((c,i)=>{blobs.set('w'+i,c.raw);blobs.set('x'+i,Buffer.from(c.x.buffer));});
const results=await withGpuPage(readFileSync(shaderArg,'utf8'),blobs,run=>run(cases.map((c,i)=>({kernel:c.format.kernel,inputs:{wb:'w'+i,xb:'x'+i},scalars:{cols:c.cols,rows:c.rows},threads:c.rows*256,out:'yb',outWords:c.rows}))));
const bound=1e-6;let failed=0;
for(const [i,c] of cases.entries()){
  const words=results[i],actual=new Float32Array(words.buffer,0,c.rows);
  let worst=0,over=0;for(let k=0;k<c.rows;k++){const e=Math.abs(actual[k]-c.expected[k])/(1+Math.abs(c.expected[k]));if(!(e<=bound))over++;worst=Math.max(worst,e);}
  const tail=words.subarray(c.rows).some(v=>v!==SENTINEL);
  console.log(JSON.stringify({tensor:c.name,format:c.format.name,cols:c.cols,rows:c.rows,overBound:over,maxNormalized:worst,tailTouched:tail}));
  if(over||tail)failed++;
}
if(failed)throw new Error(failed+' matvec subjects exceed '+bound+' against gguf-py float64');
console.log('PASS '+cases.length+' full tensors within '+bound+' normalized of gguf-py '+commit.slice(0,9)+' dequantization in float64');
