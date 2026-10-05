import {readFileSync,openSync,readSync,closeSync,statSync,existsSync} from 'node:fs';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {parseQwenGguf,GgufHeaderIncomplete} from './qwen-gguf.mjs';

const here=dirname(fileURLToPath(import.meta.url));
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-gguf-directory.json'),'utf8'));
const model=process.argv[2]??'D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
if(!existsSync(model))throw new Error('Qwen3 GGUF reference model is missing: '+model);
const size=statSync(model).size,fd=openSync(model,'r'),buf=Buffer.alloc(8*1024*1024);
try{if(readSync(fd,buf,0,buf.length,0)!==buf.length)throw new Error('Header read');}finally{closeSync(fd);}
const bytes=b=>b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength);
const parsed=parseQwenGguf(bytes(buf),size);
if(parsed.tensors.size!==reference.tensorCount||parsed.dataOffset!==reference.dataOffset||parsed.headerBytes!==reference.headerEnd)throw new Error('Independent header dimensions differ');
for(let i=0;i<reference.tensors.length;i++){
  const r=reference.tensors[i],t=parsed.tensors.get(r.name),end=i+1<reference.tensors.length?reference.tensors[i+1].offset+parsed.dataOffset:size;
  if(!t||t.type!==r.type||t.shape.join(',')!==r.shape.join(',')||t.offset!==r.offset+parsed.dataOffset||end-t.offset<t.bytes||end-t.offset-t.bytes>=32)throw new Error('Independent tensor directory differs '+r.name);
}
const faults=[];
function bad(name,mutate,match,fileSize=size){const b=Buffer.from(buf);mutate(b);try{parseQwenGguf(bytes(b),fileSize);}catch(e){if(!match.test(String(e)))throw e;faults.push(name);return;}throw new Error('Accepted '+name);}
bad('magic',b=>b[0]=0,/not GGUF/);
bad('version',b=>b.writeUInt32LE(1,4),/version/);
bad('count-overflow',b=>b.writeBigUInt64LE(1n<<63n,8),/exact range/);
bad('architecture',b=>{const at=b.indexOf('qwen3');if(at<0)throw new Error('Architecture missing');b.write('qwen4',at);},/architecture: qwen4/);
bad('nonfinite-epsilon',b=>{const key='qwen3.attention.layer_norm_rms_epsilon',at=b.indexOf(key);b.writeUInt32LE(0x7fc00000,at+key.length+4);},/Invalid GGUF parameter/);
bad('gqa-head-count',b=>{const key='qwen3.attention.head_count',at=b.indexOf(key);b.writeUInt32LE(7,at+key.length+4);},/attention.*shape/);
bad('truncated-file',()=>{},/range exceeds file|size exceeds file/,size-1);
try{parseQwenGguf(bytes(buf.subarray(0,24)),size);throw new Error('Accepted short header');}catch(e){if(!(e instanceof GgufHeaderIncomplete)||e.required!==32)throw e;faults.push('incomplete-header');}
console.log('PASS '+parsed.tensors.size+' tensors against llama.cpp gguf-py and '+faults.length+' refusal/incomplete controls');
