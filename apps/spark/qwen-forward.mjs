import {parseQwenGguf,GgufHeaderIncomplete} from './qwen-gguf.mjs';
import {createQwenTokenizer} from './qwen-tokenizer.mjs';

export function qwenRuntime(){
  const QW=window.QW={bufs:new Map(),pipes:new Map(),uniforms:[]};
  QW.init=async(code,profile=false)=>{
    const adapter=await navigator.gpu.requestAdapter();if(!adapter)throw new Error('No WebGPU adapter');
    if(profile&&!adapter.features.has('timestamp-query'))throw new Error('Profiling needs the timestamp-query feature');
    QW.device=await adapter.requestDevice({requiredFeatures:profile?['timestamp-query']:[],requiredLimits:{maxStorageBufferBindingSize:adapter.limits.maxStorageBufferBindingSize,maxBufferSize:adapter.limits.maxBufferSize}});
    QW.profile=profile?new Map():null;
    QW.module=QW.device.createShaderModule({code});
    const errs=(await QW.module.getCompilationInfo()).messages.filter(m=>m.type==='error');if(errs.length)throw new Error(errs.map(m=>m.message).join('\n'));
    QW.manifest=new Map([...code.matchAll(/^\/\/ cx-kernel (\w+)_main (.*)$/gm)].map(m=>[m[1],Object.fromEntries(m[2].split(' ').map(p=>p.split('=')))]));
    QW.uniformAt=new Map([...code.matchAll(/@binding\((\d+)\) var<uniform> u_(\w+) /g)].map(m=>[m[2],Number(m[1])]));
    await Promise.all([...QW.manifest.keys()].map(async k=>QW.pipes.set(k,await QW.device.createComputePipelineAsync({layout:'auto',compute:{module:QW.module,entryPoint:k+'_main'}}))));
    return adapter.info?.description??'';
  };
  QW.alloc=(name,bytes)=>{QW.bufs.get(name)?.destroy();QW.bufs.set(name,QW.device.createBuffer({size:Math.max(4,Math.ceil(bytes/4)*4),usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST|GPUBufferUsage.COPY_SRC}));return 0;};
  QW.upload=async(name,url)=>{const data=new Uint8Array(await (await fetch(url)).arrayBuffer());QW.alloc(name,data.byteLength);QW.device.queue.writeBuffer(QW.bufs.get(name),0,data);return data.byteLength;};
  QW.write=(name,floats)=>{QW.device.queue.writeBuffer(QW.bufs.get(name),0,new Float32Array(floats));return 0;};
  QW.run=async list=>{
    const enc=QW.device.createCommandEncoder(),qs=QW.profile&&QW.device.createQuerySet({type:'timestamp',count:2*list.length});
    let pass=qs?null:enc.beginComputePass();
    QW.device.pushErrorScope('validation');
    for(const [index,[kernel,binds,scalars,threads]] of list.entries()){
      if(qs){pass?.end();pass=enc.beginComputePass({timestampWrites:{querySet:qs,beginningOfPassWriteIndex:2*index,endOfPassWriteIndex:2*index+1}});}
      const m=QW.manifest.get(kernel);if(!m)throw new Error('No kernel '+kernel);
      if(!QW.pipes.has(kernel))QW.pipes.set(kernel,QW.device.createComputePipeline({layout:'auto',compute:{module:QW.module,entryPoint:kernel+'_main'}}));
      const entries=[],values=new ArrayBuffer(32),ints=new Int32Array(values),floats=new Float32Array(values);
      for(const [param,slot] of Object.entries(m)){
        if(slot.startsWith('b')){const b=QW.bufs.get(binds[param]);if(!b)throw new Error(kernel+'.'+param+' unbound: '+binds[param]);entries.push({binding:Number(slot.slice(1)),resource:{buffer:b}});}
        else if(slot.startsWith('u')){const v=scalars[param],i=Number(slot.slice(1));if(v===undefined)throw new Error(kernel+' scalar '+param);if(typeof v==='object')floats[i]=v.f;else ints[i]=v;}
      }
      if(QW.uniformAt.has(kernel)){
        if(!QW.uniforms[index])QW.uniforms[index]=QW.device.createBuffer({size:32,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
        QW.device.queue.writeBuffer(QW.uniforms[index],0,values);entries.push({binding:QW.uniformAt.get(kernel),resource:{buffer:QW.uniforms[index]}});
      }
      const pipe=QW.pipes.get(kernel),groups=Math.ceil(threads/Number(m.wg??64)),gx=Math.min(groups,65535);
      pass.setPipeline(pipe);pass.setBindGroup(0,QW.device.createBindGroup({layout:pipe.getBindGroupLayout(0),entries}));pass.dispatchWorkgroups(gx,Math.ceil(groups/gx));
    }
    pass?.end();
    let stamps=null;
    if(qs){const rb=QW.device.createBuffer({size:16*list.length,usage:GPUBufferUsage.QUERY_RESOLVE|GPUBufferUsage.COPY_SRC});stamps=QW.device.createBuffer({size:16*list.length,usage:GPUBufferUsage.MAP_READ|GPUBufferUsage.COPY_DST});enc.resolveQuerySet(qs,0,2*list.length,rb,0);enc.copyBufferToBuffer(rb,0,stamps,0,16*list.length);}
    QW.device.queue.submit([enc.finish()]);
    const error=await QW.device.popErrorScope();if(error)throw new Error(error.message);
    await QW.device.queue.onSubmittedWorkDone();
    if(stamps){
      await stamps.mapAsync(GPUMapMode.READ);const t=new BigInt64Array(stamps.getMappedRange());
      list.forEach(([kernel,,scalars],i)=>{const shape=scalars.cols?scalars.cols+'x'+scalars.rows:'',key=kernel+' '+shape,row=QW.profile.get(key)??{kernel,shape,ns:0,dispatches:0};row.ns+=Number(t[2*i+1]-t[2*i]);row.dispatches++;QW.profile.set(key,row);});
      stamps.unmap();stamps.destroy();qs.destroy();
    }
    return 0;
  };
  QW.argmaxGpu=async(name,n)=>{
    const g=Math.ceil(n/4096);if(QW.amGroups!==g){QW.alloc('am-part',g*8);QW.alloc('am-out',8);QW.amGroups=g;}
    await QW.run([['qw_argmax_part',{xb:name,pb:'am-part'},{n},g*256],['qw_argmax_final',{pb:'am-part',ob:'am-out'},{m:g},256]]);
    const rb=QW.device.createBuffer({size:8,usage:GPUBufferUsage.MAP_READ|GPUBufferUsage.COPY_DST}),enc=QW.device.createCommandEncoder();
    enc.copyBufferToBuffer(QW.bufs.get('am-out'),0,rb,0,8);QW.device.queue.submit([enc.finish()]);
    await rb.mapAsync(GPUMapMode.READ);const w=new Int32Array(rb.getMappedRange().slice(0));rb.unmap();rb.destroy();
    return JSON.stringify({best:w[0],top:new Float32Array(w.buffer)[1],logprobs:[]});
  };
  QW.profileTake=()=>{const rows=[...QW.profile.values()].sort((a,b)=>b.ns-a.ns);QW.profile.clear();return JSON.stringify(rows);};
  QW.read=async(name,words)=>{
    const rb=QW.device.createBuffer({size:words*4,usage:GPUBufferUsage.MAP_READ|GPUBufferUsage.COPY_DST}),enc=QW.device.createCommandEncoder();
    enc.copyBufferToBuffer(QW.bufs.get(name),0,rb,0,words*4);QW.device.queue.submit([enc.finish()]);
    await rb.mapAsync(GPUMapMode.READ);const out=Array.from(new Float32Array(rb.getMappedRange().slice(0)));rb.unmap();rb.destroy();return out;
  };
  QW.argmax=async(name,words,ids=[])=>{const v=await QW.read(name,words);let best=0;for(let i=1;i<v.length;i++)if(v[i]>v[best])best=i;let sum=0;for(const x of v)sum+=Math.exp(x-v[best]);const lse=v[best]+Math.log(sum);return JSON.stringify({best,top:v[best],logprobs:ids.map(id=>v[id]-lse)});};
}

export const qwOverCdp=evaluate=>new Proxy({},{get:(_,method)=>(...args)=>evaluate('QW.'+String(method)+'('+args.map(a=>JSON.stringify(a)).join(',')+')')});

const matvec={12:'q4k_matvec_wg',14:'q6k_matvec_wg',1:'f16_matvec_wg'},matmat={12:'q4k_matmat_wg',14:'q6k_matmat_wg',1:'f16_matmat_wg'},perRow={q4k_matvec_wg:32,q6k_matvec_wg:16,f16_matvec_wg:256,q4k_matmat_wg:32,q6k_matmat_wg:16,f16_matmat_wg:256};

export function ropeTable(position,dim,base){
  const half=dim/2,table=new Array(dim),scale=Math.fround(Math.pow(base,-2/dim));
  let theta=Math.fround(position);
  for(let i=0;i<half;i++){table[i]=Math.fround(Math.cos(theta));table[half+i]=Math.fround(Math.sin(theta));theta=Math.fround(theta*scale);}
  return table;
}

export class QwenDecoder {
  constructor(parsed,qw,context){
    this.p=parsed;this.c=parsed.config;this.qw=qw;this.context=context;this.chunk=32;
    for(const t of parsed.tensors.values())if(t.type!==0&&!matvec[t.type]&&t.name!=='token_embd.weight')throw new Error('No kernel for tensor type '+t.type+' '+t.name);
    if(parsed.tensors.get('token_embd.weight').type!==12)throw new Error('Embedding must be Q4_K');
  }
  async load(code,urlFor,profile=false){
    const description=await this.qw.init(code,profile);
    let bytes=0;for(const t of this.p.tensors.values())bytes+=await this.qw.upload(t.name,urlFor(t));
    const {embedding:e,ffn,heads,kvHeads,headSize:d,layers,vocab}=this.c,kv=kvHeads*d;
    const C=this.chunk,sizes={x:e*C,x2:e*C,h:e*C,h2:e*C,s:heads*C,q:heads*d*C,q2:heads*d*C,q3:heads*d*C,k:kv*C,k2:kv*C,k3:kv*C,v:kv*C,ao:heads*d*C,o:e*C,g:ffn*C,u:ffn*C,a:ffn*C,f:e*C,rope:d,ropes:d*C,xl:e,sc:heads*this.context*C,pr:heads*this.context*C,logits:vocab};
    for(const [name,n] of Object.entries(sizes))await this.qw.alloc(name,n*4);
    for(let l=0;l<layers;l++){await this.qw.alloc('kc'+l,this.context*kv*4);await this.qw.alloc('vc'+l,this.context*kv*4);}
    return {description,bytes};
  }
  mv(list,name,input,output,cols,rows){const t=this.p.tensors.get(name);const k=matvec[t.type];list.push([k,{wb:name,xb:input,yb:output},{cols,rows},rows*(perRow[k]??1)]);}
  mm(list,name,input,output,cols,rows,n){const t=this.p.tensors.get(name),k=matmat[t.type];if(!k)throw new Error('No batched kernel for '+name);list.push([k,{wb:name,xb:input,yb:output},{cols,rows,ntok:n},rows*perRow[k]]);}
  planRows(tokens,start,last){
    const {embedding:e,ffn,heads,kvHeads,headSize:d,layers,vocab}=this.c,kv=kvHeads*d,n=tokens.length,len=start+n,list=[];
    tokens.forEach((token,i)=>list.push(['q4k_row_at',{wb:'token_embd.weight',yb:'x'},{row:token,cols:e,at:i*e},e]));
    for(let l=0;l<layers;l++){
      const b='blk.'+l+'.';
      this.norm(list,'x',b+'attn_norm.weight','h',e,n);
      this.mm(list,b+'attn_q.weight','h','q',e,heads*d,n);this.mm(list,b+'attn_k.weight','h','k',e,kv,n);this.mm(list,b+'attn_v.weight','h','v',e,kv,n);
      this.norm(list,'q',b+'attn_q_norm.weight','q2',d,heads*n);this.norm(list,'k',b+'attn_k_norm.weight','k2',d,kvHeads*n);
      list.push(['rope_neox_rows',{xb:'q2',tb:'ropes',yb:'q3'},{dim:d,per:heads,n:heads*d*n},heads*d*n]);list.push(['rope_neox_rows',{xb:'k2',tb:'ropes',yb:'k3'},{dim:d,per:kvHeads,n:kv*n},kv*n]);
      list.push(['kv_store',{xb:'k3',cb:'kc'+l},{offset:start*kv,n:kv*n},kv*n]);list.push(['kv_store',{xb:'v',cb:'vc'+l},{offset:start*kv,n:kv*n},kv*n]);
      list.push(['attn_score_rows',{qb:'q3',kb:'kc'+l,sb:'sc'},{start,np:n,heads,kvheads:kvHeads,dim:d,scale:{f:1/Math.sqrt(d)}},n*heads*len]);
      list.push(['attn_softmax',{sb:'sc',pb:'pr'},{len,heads:heads*n},heads*n]);
      list.push(['attn_out_rows',{pb:'pr',vb:'vc'+l,ob:'ao'},{len,heads,kvheads:kvHeads,dim:d,np:n},n*heads*d]);
      this.mm(list,b+'attn_output.weight','ao','o',heads*d,e,n);
      list.push(['vec_add',{ab:'o',bb:'x',yb:'x2'},{n:e*n},e*n]);
      this.norm(list,'x2',b+'ffn_norm.weight','h2',e,n);
      this.mm(list,b+'ffn_gate.weight','h2','g',e,ffn,n);this.mm(list,b+'ffn_up.weight','h2','u',e,ffn,n);
      list.push(['swiglu',{gb:'g',ub:'u',yb:'a'},{n:ffn*n},ffn*n]);
      this.mm(list,b+'ffn_down.weight','a','f',ffn,e,n);
      list.push(['vec_add',{ab:'f',bb:'x2',yb:'x'},{n:e*n},e*n]);
    }
    if(last){
      list.push(['vec_copy_from',{xb:'x',yb:'xl'},{src:(n-1)*e,n:e},e]);
      this.norm(list,'xl','output_norm.weight','h',e,1);
      this.mv(list,this.p.tensors.has('output.weight')?'output.weight':'token_embd.weight','h','logits',e,vocab);
    }
    return list;
  }
  async prefill(tokens,start,ids=[]){
    if(start+tokens.length>this.context)throw new Error('Context exhausted');
    for(let at=0;at<tokens.length;at+=this.chunk){
      const part=tokens.slice(at,at+this.chunk),table=[];
      for(let i=0;i<part.length;i++)table.push(...ropeTable(start+at+i,this.c.headSize,this.c.ropeBase));
      await this.qw.write('ropes',table);
      await this.qw.run(this.planRows(part,start+at,at+this.chunk>=tokens.length));
    }
    return this.pick(ids);
  }
  async pick(ids){return JSON.parse(ids.length?await this.qw.argmax('logits',this.c.vocab,ids):await this.qw.argmaxGpu('logits',this.c.vocab));}
  norm(list,input,weight,output,dim,rows){list.push(['rms_stat',{xb:input,sb:'s'},{dim,rows,eps:{f:this.c.epsilon}},rows*256]);list.push(['rms_apply',{xb:input,sb:'s',nb:weight,yb:output},{dim,n:dim*rows},dim*rows]);}
  plan(token,position){
    const {embedding:e,ffn,heads,kvHeads,headSize:d,layers,vocab}=this.c,kv=kvHeads*d,len=position+1,list=[];
    list.push(['q4k_row',{wb:'token_embd.weight',yb:'x'},{row:token,cols:e},e]);
    for(let l=0;l<layers;l++){
      const b='blk.'+l+'.';
      this.norm(list,'x',b+'attn_norm.weight','h',e,1);
      this.mv(list,b+'attn_q.weight','h','q',e,heads*d);this.mv(list,b+'attn_k.weight','h','k',e,kv);this.mv(list,b+'attn_v.weight','h','v',e,kv);
      this.norm(list,'q',b+'attn_q_norm.weight','q2',d,heads);this.norm(list,'k',b+'attn_k_norm.weight','k2',d,kvHeads);
      list.push(['rope_neox',{xb:'q2',tb:'rope',yb:'q3'},{dim:d,n:heads*d},heads*d]);list.push(['rope_neox',{xb:'k2',tb:'rope',yb:'k3'},{dim:d,n:kv},kv]);
      list.push(['kv_store',{xb:'k3',cb:'kc'+l},{offset:position*kv,n:kv},kv]);list.push(['kv_store',{xb:'v',cb:'vc'+l},{offset:position*kv,n:kv},kv]);
      list.push(['attn_score',{qb:'q3',kb:'kc'+l,sb:'sc'},{len,heads,kvheads:kvHeads,dim:d,scale:{f:1/Math.sqrt(d)}},heads*len]);
      list.push(['attn_softmax',{sb:'sc',pb:'pr'},{len,heads},heads]);
      list.push(['attn_out',{pb:'pr',vb:'vc'+l,ob:'ao'},{len,heads,kvheads:kvHeads,dim:d},heads*d]);
      this.mv(list,b+'attn_output.weight','ao','o',heads*d,e);
      list.push(['vec_add',{ab:'o',bb:'x',yb:'x2'},{n:e},e]);
      this.norm(list,'x2',b+'ffn_norm.weight','h2',e,1);
      this.mv(list,b+'ffn_gate.weight','h2','g',e,ffn);this.mv(list,b+'ffn_up.weight','h2','u',e,ffn);
      list.push(['swiglu',{gb:'g',ub:'u',yb:'a'},{n:ffn},ffn]);
      this.mv(list,b+'ffn_down.weight','a','f',ffn,e);
      list.push(['vec_add',{ab:'f',bb:'x2',yb:'x'},{n:e},e]);
    }
    this.norm(list,'x','output_norm.weight','h',e,1);
    this.mv(list,this.p.tensors.has('output.weight')?'output.weight':'token_embd.weight','h','logits',e,vocab);
    return list;
  }
  async step(token,position,ids=[]){
    if(position>=this.context)throw new Error('Context exhausted');
    await this.qw.write('rope',ropeTable(position,this.c.headSize,this.c.ropeBase));
    await this.qw.run(this.plan(token,position));
    return this.pick(ids);
  }
}

export function createLocalQwenProvider({kernels,tokenizerWasm,pickFile,context=4096,maxTokens=1024}){
  let model=null,loading=null,busy=false;
  const aborted=()=>{const error=new Error('cancelled');error.name='AbortError';return error;};
  async function load(){
    const file=await pickFile();if(!file)throw new Error('No GGUF file chosen.');
    let need=8*1024*1024,parsed,header;
    for(;;){
      header=await file.slice(0,Math.min(need,file.size)).arrayBuffer();
      try{parsed=parseQwenGguf(header,file.size);break;}catch(error){if(error instanceof GgufHeaderIncomplete&&error.required>need&&error.required<=16777216){need=error.required;continue;}throw error;}
    }
    const tokenizer=await createQwenTokenizer(header,parsed,tokenizerWasm);
    if(!window.QW)qwenRuntime();
    const decoder=new QwenDecoder(parsed,window.QW,Math.min(context,parsed.config.context)),urls=[];
    try{await decoder.load(kernels,t=>{const url=URL.createObjectURL(file.slice(t.offset,Math.min(file.size,t.offset+Math.ceil(t.bytes/4)*4)));urls.push(url);return url;});}
    finally{for(const url of urls)URL.revokeObjectURL(url);}
    return {name:file.name,parsed,tokenizer,decoder};
  }
  return {
    id:'local',label:'Local Qwen3 GGUF (WebGPU)',
    get unavailableReason(){return typeof navigator==='undefined'||!navigator.gpu?'WebGPU is not available in this browser.':'';},
    get model(){return model?.name??'';},
    async stream(messages,options={},onEvent=()=>{}){
      if(busy)throw new Error('The local model is already generating.');
      if(options.signal?.aborted)throw aborted();
      busy=true;
      try{
        if(!model){loading??=load();try{model=await loading;}finally{loading=null;}}
        const {tokenizer,decoder}=model,signal=options.signal;
        const ids=[],special=text=>ids.push(...tokenizer.encode(text,{parseSpecial:true,addBos:false})),plain=text=>ids.push(...tokenizer.encode(text,{parseSpecial:false,addBos:false}));
        if(options.system){special('<|im_start|>system\n');plain(options.system);special('<|im_end|>\n');}
        for(const m of messages){if(m.role!=='user'&&m.role!=='assistant')throw new Error('Unsupported message role '+m.role);special('<|im_start|>'+m.role+'\n');plain(typeof m.content==='string'?m.content:'');special('<|im_end|>\n');}
        special('<|im_start|>assistant\n<think>\n\n</think>\n\n');
        if(ids.length+1>=decoder.context)throw new Error('Prompt of '+ids.length+' tokens exceeds the local context of '+decoder.context+'.');
        let result;
        if(signal?.aborted)throw aborted();result=await decoder.prefill(ids,0);
        const out=[];let stopReason='max_tokens';
        while(out.length<maxTokens&&ids.length+out.length<decoder.context){
          if(signal?.aborted)throw aborted();
          if(tokenizer.isEnd(result.best)){stopReason='end_turn';break;}
          out.push(result.best);
          const text=tokenizer.decode(out);onEvent({type:'text'},{text,thinking:''});
          result=await decoder.step(result.best,ids.length+out.length-1);
        }
        return {stopReason,text:tokenizer.decode(out),thinking:'',tools:[],refusal:null,tokens:out,promptTokens:ids};
      } finally {busy=false;}
    }
  };
}
