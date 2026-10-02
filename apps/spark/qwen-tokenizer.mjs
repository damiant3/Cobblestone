const encoder=new TextEncoder(),decoder=new TextDecoder('utf-8',{fatal:true});
const expression=/(?:'[sS]|'[tT]|'[rR][eE]|'[vV][eE]|'[mM]|'[lL][lL]|'[dD])|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+/gu;
const eogNames=new Set(['<|eot_id|>','<|im_end|>','<|end|>','<|return|>','<|call|>','<|flush|>','<|calls|>','<end_of_turn>','<|endoftext|>','</s>','<|eom_id|>','<EOT>','_<EOT>','[EOT]','[EOS]','<|end_of_text|>','<end_of_utterance>','<eos>','<turn|>','<|tool_response>','<｜end▁of▁sentence｜>','[e~[']);
function* strings(header,descriptor){
  const v=new DataView(header);let p=descriptor.offset;
  for(let i=0;i<descriptor.count;i++){const n=Number(v.getBigUint64(p,true));p+=8;yield decoder.decode(new Uint8Array(header,p,n));p+=n;}
  if(p!==descriptor.end)throw new Error('GGUF tokenizer span differs');
}
function byteValue(c){
  const n=c.codePointAt(0);
  if(n>=33&&n<=126||n>=161&&n<=172||n>=174&&n<=255)return n;
  if(n>=256&&n<=288)return n-256;
  if(n>=289&&n<=322)return n-289+127;
  if(n===323)return 173;
  throw new Error('Invalid GPT2 byte-alphabet symbol');
}
function specialPattern(map){
  const names=[...map.keys()].sort((a,b)=>b.length-a.length);
  return names.length?new RegExp(names.map(s=>s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')).join('|'),'gu'):null;
}

export async function createQwenTokenizer(header,parsed,wasm){
  let memory;
  const module=wasm instanceof WebAssembly.Module?wasm:await WebAssembly.compile(wasm);
  const instance=await WebAssembly.instantiate(module,{wasi_snapshot_preview1:{fd_write(fd,vs,n,p){const v=new DataView(memory.buffer);let bytes=0;for(let i=0;i<n;i++)bytes+=v.getUint32(vs+i*8+4,true);v.setUint32(p,bytes,true);return 0;},fd_read(fd,vs,n,p){new DataView(memory.buffer).setUint32(p,0,true);return 0;}}});
  const api=instance.exports;memory=api.memory;api._start();
  const {vocab,types,merges}=parsed.tokenizer,model=api.bpe_new(BigInt(merges.count),BigInt(vocab.count));
  if(model===0n)throw new Error('Tokenizer table limits exceeded');
  const ids=new Int32Array(256).fill(-1),words=new Map(),specials=new Map(),users=new Map(),stop=new Set([parsed.config.eos]);
  const raw=new Uint8Array(vocab.end-vocab.offset),offsets=new Uint32Array(vocab.count+1),v=new DataView(header);
  let used=0,id=0;
  for(const text of strings(header,vocab)){
    if(words.has(text))throw new Error('Duplicate GGUF token');
    words.set(text,id);offsets[id]=used;
    const type=v.getInt32(types.offset+id*4,true),end=eogNames.has(text),control=type===3||end,user=type===4;
    if(control||user){specials.set(text,id);if(user)users.set(text,id);if(end)stop.add(id);const bytes=encoder.encode(text);raw.set(bytes,used);used+=bytes.length;}
    else {for(const c of text)raw[used++]=byteValue(c);if(used-offsets[id]===1){const b=raw[offsets[id]];if(ids[b]!==-1)throw new Error('Duplicate byte token');ids[b]=id;}}
    id++;
  }
  offsets[id]=used;if(ids.some(n=>n<0))throw new Error('GGUF vocabulary lacks byte tokens');
  const pairs=new Int32Array(memory.buffer,Number(api.bpe_pairs(model)),merges.count*3);
  let rank=0;
  for(const merge of strings(header,merges)){
    const split=merge.indexOf(' ');if(split<1||split!==merge.lastIndexOf(' '))throw new Error('Invalid GGUF merge');
    const left=merge.slice(0,split),right=merge.slice(split+1),a=words.get(left),b=words.get(right),c=words.get(left+right);
    if(a===undefined||b===undefined||c===undefined)throw new Error('GGUF merge token is missing');
    pairs[rank*3]=a;pairs[rank*3+1]=b;pairs[rank*3+2]=c;rank++;
  }
  new Int32Array(memory.buffer,Number(api.bpe_bytes(model)),256).set(ids);
  if(api.bpe_init(model)!==1n)throw new Error('Invalid or duplicate GGUF merge table');
  words.clear();
  const allPattern=specialPattern(specials),userPattern=specialPattern(users),data=raw.slice(0,used),input=Number(api.bpe_input(model));
  function ordinary(text,out){
    for(const match of text.matchAll(expression)){
      const bytes=encoder.encode(match[0]);if(bytes.length>262144)throw new Error('Tokenizer segment exceeds256KiB');
      new Uint8Array(memory.buffer,input,bytes.length).set(bytes);
      const result=Number(api.bpe_encode(model,BigInt(bytes.length)));if(!result)throw new Error('Tokenizer refused segment');
      const view=new DataView(memory.buffer),n=view.getUint32(result,true);if(n>bytes.length)throw new Error('Tokenizer output count differs');
      for(let i=0;i<n;i++)out.push(view.getUint32(result+4+i*4,true));
    }
  }
  return {
    encode(text,{parseSpecial=true,addBos=parsed.config.addBos}={}){
      if(typeof text!=='string'||text.length>262144||!text.isWellFormed())throw new Error('Invalid or oversized tokenizer input');
      const out=addBos?[parsed.config.bos]:[],pattern=parseSpecial?allPattern:userPattern;let p=0;
      if(pattern)for(const match of text.matchAll(pattern)){ordinary(text.slice(p,match.index),out);out.push(specials.get(match[0]));p=match.index+match[0].length;}
      ordinary(text.slice(p),out);return out;
    },
    piece(id){if(!Number.isInteger(id)||id<0||id>=vocab.count)throw new Error('Token ID outside vocabulary');return data.subarray(offsets[id],offsets[id+1]);},
    decode(ids){const chunks=ids.map(id=>this.piece(id));const out=new Uint8Array(chunks.reduce((n,b)=>n+b.length,0));let p=0;for(const b of chunks){out.set(b,p);p+=b.length;}return new TextDecoder().decode(out);},
    isEnd:id=>stop.has(id),
    memory:()=>({arenaBytes:memory.buffer.byteLength,usedBytes:Number(api.bpe_used(model)),vocabularyBytes:data.byteLength})
  };
}
