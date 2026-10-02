import {readFileSync,openSync,readSync,closeSync,statSync,existsSync} from 'node:fs';
import {dirname,join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {parseQwenGguf} from './qwen-gguf.mjs';
import {createQwenTokenizer} from './qwen-tokenizer.mjs';
import {buildQwenTokenizer} from './build-qwen-tokenizer.mjs';

const here=dirname(fileURLToPath(import.meta.url));
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-tokenizer-reference.json'),'utf8'));
const model=process.argv[2]??'D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
if(!existsSync(model))throw new Error('Qwen3 GGUF reference model is missing: '+model);
const fd=openSync(model,'r'),b=Buffer.alloc(8*1024*1024);try{readSync(fd,b,0,b.length,0);}finally{closeSync(fd);}
const header=b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength),parsed=parseQwenGguf(header,statSync(model).size);
const wasm=buildQwenTokenizer(resolve(here,'../../build-output/spark-local/tokenizer.wasm'));
const tokenizer=await createQwenTokenizer(header,parsed,wasm);
for(const r of reference.cases){
  const ids=tokenizer.encode(r.text,{parseSpecial:r.parseSpecial,addBos:false}),want=r.tokens.map(t=>t.id);
  if(ids.join(',')!==want.join(','))throw new Error('Token mismatch '+JSON.stringify({text:r.text,parseSpecial:r.parseSpecial,got:ids,want}));
  if(tokenizer.decode(ids)!==r.text)throw new Error('Decode differs '+JSON.stringify(r.text));
}
const prompt='Repeat a fixed prompt: hello world! 12345.';
for(let i=0;i<100;i++)tokenizer.encode(prompt);const after=tokenizer.memory();
for(let i=0;i<100;i++)tokenizer.encode(prompt);const repeated=tokenizer.memory();
if(after.usedBytes!==repeated.usedBytes||after.arenaBytes!==repeated.arenaBytes)throw new Error('Tokenizer arena grows on repeated input');
console.log('PASS '+reference.cases.length+' '+reference.oracle+' token sequences and lossless decodes; arena '+JSON.stringify(repeated));
