const utf8=new TextDecoder('utf-8',{fatal:true});
const blocks=new Map([[0,[1,4]],[1,[1,2]],[12,[256,144]],[14,[256,210]]]);
export class GgufHeaderIncomplete extends Error {
  constructor(required){super('GGUF header is incomplete');this.required=required;}
}

export function parseQwenGguf(buffer,fileSize){
  if(!(buffer instanceof ArrayBuffer)||!Number.isSafeInteger(fileSize)||fileSize<24)throw new Error('Invalid GGUF file size');
  const view=new DataView(buffer);let pos=0;
  const need=n=>{if(!Number.isSafeInteger(n)||n<0||pos>fileSize||n>fileSize-pos)throw new Error('GGUF field exceeds file');if(pos+n>16777216)throw new Error('GGUF header exceeds16MiB');if(pos+n>buffer.byteLength)throw new GgufHeaderIncomplete(pos+n);};
  const u32=()=>{need(4);const n=view.getUint32(pos,true);pos+=4;return n;};
  const u64=()=>{need(8);const n=view.getBigUint64(pos,true);pos+=8;if(n>BigInt(Number.MAX_SAFE_INTEGER))throw new Error('GGUF integer exceeds exact range');return Number(n);};
  const text=()=>{const n=u64();need(n);const p=pos;pos+=n;return utf8.decode(new Uint8Array(buffer,p,n));};
  const widths=[1,1,2,2,4,4,4,1,0,0,8,8,8];
  function value(type){
    if(type===8)return text();
    if(type===9){
      const elementType=u32(),count=u64(),offset=pos;
      if(count>1000000||elementType>12||elementType===9)throw new Error('Unsupported GGUF metadata array');
      if(elementType===8){for(let i=0;i<count;i++){const n=u64();need(n);pos+=n;}}
      else {need(count*widths[elementType]);pos+=count*widths[elementType];}
      return {elementType,count,offset,end:pos};
    }
    if(type<0||type>12)throw new Error('Unsupported GGUF metadata type '+type);
    need(widths[type]);let n;
    if(type===0||type===7)n=view.getUint8(pos);
    else if(type===1)n=view.getInt8(pos);
    else if(type===2)n=view.getUint16(pos,true);
    else if(type===3)n=view.getInt16(pos,true);
    else if(type===4)n=view.getUint32(pos,true);
    else if(type===5)n=view.getInt32(pos,true);
    else if(type===6)n=view.getFloat32(pos,true);
    else if(type===10){const b=view.getBigUint64(pos,true);if(b>BigInt(Number.MAX_SAFE_INTEGER))throw new Error('GGUF integer exceeds exact range');n=Number(b);}
    else if(type===11){const b=view.getBigInt64(pos,true);if(b>BigInt(Number.MAX_SAFE_INTEGER)||b<BigInt(Number.MIN_SAFE_INTEGER))throw new Error('GGUF integer exceeds exact range');n=Number(b);}
    else n=view.getFloat64(pos,true);
    pos+=widths[type];if(type===7){if(n>1)throw new Error('Invalid GGUF boolean');return n===1;}return n;
  }
  if(u32()!==0x46554747)throw new Error('File is not GGUF');
  const version=u32();if(version!==2&&version!==3)throw new Error('Unsupported GGUF version '+version);
  const tensorCount=u64(),metadataCount=u64();if(tensorCount<1||tensorCount>4096||metadataCount>4096)throw new Error('GGUF header counts exceed limits');
  const metadata=new Map();
  for(let i=0;i<metadataCount;i++){const key=text();if(metadata.has(key))throw new Error('Duplicate GGUF metadata key '+key);metadata.set(key,value(u32()));}
  if(metadata.get('general.architecture')!=='qwen3')throw new Error('Unsupported GGUF architecture: '+metadata.get('general.architecture'));
  if((metadata.get('qwen3.rope.scaling.type')??'none')!=='none'||(metadata.get('qwen3.rope.scaling.factor')??1)!==1||(metadata.get('qwen3.attention.sliding_window')??0)!==0)throw new Error('Unsupported Qwen rotary scaling or sliding attention');
  if(metadata.get('tokenizer.ggml.model')!=='gpt2'||metadata.get('tokenizer.ggml.pre')!=='qwen2')throw new Error('Unsupported Qwen tokenizer');
  const integer=(key,min,max)=>{const v=metadata.get(key);if(!Number.isSafeInteger(v)||v<min||v>max)throw new Error('Invalid GGUF parameter '+key);return v;};
  const real=(key,min,max)=>{const v=metadata.get(key);if(typeof v!=='number'||!Number.isFinite(v)||v<min||v>max)throw new Error('Invalid GGUF parameter '+key);return v;};
  const vocab=metadata.get('tokenizer.ggml.tokens'),types=metadata.get('tokenizer.ggml.token_type'),merges=metadata.get('tokenizer.ggml.merges');
  if(!vocab||vocab.elementType!==8||vocab.count<256||!types||types.elementType!==5||types.count!==vocab.count||!merges||merges.elementType!==8)throw new Error('Invalid Qwen tokenizer tables');
  const config={layers:integer('qwen3.block_count',1,80),embedding:integer('qwen3.embedding_length',1,16384),ffn:integer('qwen3.feed_forward_length',1,65536),heads:integer('qwen3.attention.head_count',1,128),kvHeads:integer('qwen3.attention.head_count_kv',1,128),headSize:integer('qwen3.attention.key_length',16,256),valueSize:integer('qwen3.attention.value_length',16,256),context:integer('qwen3.context_length',1,1048576),epsilon:real('qwen3.attention.layer_norm_rms_epsilon',1e-12,1),ropeBase:real('qwen3.rope.freq_base',1,1e9),vocab:vocab.count,eos:integer('tokenizer.ggml.eos_token_id',0,vocab.count-1),bos:integer('tokenizer.ggml.bos_token_id',0,vocab.count-1),addBos:metadata.get('tokenizer.ggml.add_bos_token')??false};
  if(config.heads%config.kvHeads||config.headSize!==config.valueSize||config.headSize%2||typeof config.addBos!=='boolean')throw new Error('Unsupported Qwen attention/tokenizer shape');
  const tensors=new Map();
  for(let i=0;i<tensorCount;i++){
    const name=text(),rank=u32();if(rank<1||rank>4||tensors.has(name))throw new Error('Invalid/duplicate GGUF tensor '+name);
    const shape=Array.from({length:rank},u64),type=u32(),offset=u64(),format=blocks.get(type);
    if(!format)throw new Error('Unsupported GGUF quantization type '+type+' in '+name);
    if(shape.some(n=>n<1)||shape[0]%format[0])throw new Error('Invalid quantized GGUF shape '+name);
    let bytes=shape[0]/format[0]*format[1];
    for(const n of shape.slice(1)){if(bytes>Math.floor(fileSize/n))throw new Error('GGUF tensor size exceeds file');bytes*=n;}
    if(!Number.isSafeInteger(bytes)||bytes>fileSize)throw new Error('GGUF tensor size exceeds file');
    tensors.set(name,{name,shape,type,offset,bytes});
  }
  const alignment=metadata.get('general.alignment')??32;
  if(!Number.isSafeInteger(alignment)||alignment<1||alignment>4096||(alignment&(alignment-1)))throw new Error('Invalid GGUF alignment');
  const dataOffset=Math.ceil(pos/alignment)*alignment;
  if(dataOffset>fileSize)throw new Error('GGUF data offset exceeds file');
  for(const t of tensors.values()){if(t.offset%alignment||t.offset>fileSize-dataOffset||t.bytes>fileSize-dataOffset-t.offset)throw new Error('GGUF tensor range exceeds file: '+t.name);t.offset+=dataOffset;}
  const tensor=(name,shape)=>{const t=tensors.get(name);if(!t||t.shape.length!==shape.length||t.shape.some((n,i)=>n!==shape[i]))throw new Error('Missing/incompatible Qwen tensor '+name);return t;};
  const e=config.embedding,k=config.kvHeads*config.headSize,q=config.heads*config.headSize;
  const norm=(name,shape)=>{const t=tensor(name,shape);if(t.type!==0&&t.type!==1)throw new Error('Unsupported quantized Qwen norm '+name);};
  tensor('token_embd.weight',[e,config.vocab]);norm('output_norm.weight',[e]);
  if(tensors.has('output.weight'))tensor('output.weight',[e,config.vocab]);
  for(let i=0;i<config.layers;i++){
    const p='blk.'+i+'.';
    for(const key of ['attn_norm.weight','ffn_norm.weight'])norm(p+key,[e]);
    for(const key of ['attn_q_norm.weight','attn_k_norm.weight'])norm(p+key,[config.headSize]);
    tensor(p+'attn_q.weight',[e,q]);tensor(p+'attn_k.weight',[e,k]);tensor(p+'attn_v.weight',[e,k]);tensor(p+'attn_output.weight',[q,e]);
    tensor(p+'ffn_gate.weight',[e,config.ffn]);tensor(p+'ffn_up.weight',[e,config.ffn]);tensor(p+'ffn_down.weight',[config.ffn,e]);
  }
  return {version,headerBytes:pos,dataOffset,metadata,tensors,config,tokenizer:{vocab,types,merges}};
}
