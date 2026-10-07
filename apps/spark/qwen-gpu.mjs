import {readFileSync,mkdtempSync} from 'node:fs';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {spawn,execFileSync} from 'node:child_process';
import http from 'node:http';

export const SENTINEL=1234567;

async function pageRun(jobs){
  const adapter=await navigator.gpu.requestAdapter();if(!adapter)throw new Error('No WebGPU adapter');
  const device=await adapter.requestDevice({requiredLimits:{maxStorageBufferBindingSize:adapter.limits.maxStorageBufferBindingSize,maxBufferSize:adapter.limits.maxBufferSize}});
  const code=await (await fetch('/code')).text(),module=device.createShaderModule({code});
  const errs=(await module.getCompilationInfo()).messages.filter(m=>m.type==='error');if(errs.length)throw new Error(errs.map(m=>m.message).join('\n'));
  const manifest=k=>{const line=new RegExp('^// cx-kernel '+k+'_main (.*)$','m').exec(code);if(!line)throw new Error('No manifest for '+k);return Object.fromEntries(line[1].split(' ').map(p=>p.split('=')));};
  const out=[],pipelines=new Map(),blobs=new Map();
  try{
    for(const job of jobs){
      const m=manifest(job.kernel),entries=[],owned=[];let target;
      if(!pipelines.has(job.kernel))pipelines.set(job.kernel,await device.createComputePipelineAsync({layout:'auto',compute:{module,entryPoint:job.kernel+'_main'}}));
      for(const [name,slot] of Object.entries(m)){
        if(!slot.startsWith('b'))continue;
        let buffer;
        if(name===job.out){buffer=device.createBuffer({size:(job.outWords+64)*4,usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST|GPUBufferUsage.COPY_SRC});device.queue.writeBuffer(buffer,0,new Int32Array(job.outWords+64).fill(1234567));owned.push(buffer);target=buffer;}
        else{
          const id=job.inputs[name];if(id===undefined)throw new Error('No input for '+job.kernel+'.'+name);
          if(!blobs.has(id)){const data=new Uint8Array(await (await fetch('/blob/'+id)).arrayBuffer());const b=device.createBuffer({size:Math.max(4,data.byteLength),usage:GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST});device.queue.writeBuffer(b,0,data);blobs.set(id,b);}
          buffer=blobs.get(id);
        }
        entries.push({binding:Number(slot.slice(1)),resource:{buffer}});
      }
      if(!target)throw new Error('No output binding '+job.out);
      const scalars=Object.entries(m).filter(([,s])=>s.startsWith('u')).sort((a,b)=>Number(a[1].slice(1))-Number(b[1].slice(1)));
      const values=new Int32Array(Math.max(4,Math.ceil(scalars.length/4)*4));scalars.forEach(([n],i)=>{if(!(n in job.scalars))throw new Error('No scalar '+n);values[i]=job.scalars[n];});
      if(scalars.length){
        const at=Number(new RegExp('@binding\\((\\d+)\\) var<uniform> u_'+job.kernel+'\\b').exec(code)[1]);
        const ub=device.createBuffer({size:values.byteLength,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});device.queue.writeBuffer(ub,0,values);owned.push(ub);entries.push({binding:at,resource:{buffer:ub}});
      }
      const pipeline=pipelines.get(job.kernel),group=device.createBindGroup({layout:pipeline.getBindGroupLayout(0),entries});
      const rb=device.createBuffer({size:(job.outWords+64)*4,usage:GPUBufferUsage.MAP_READ|GPUBufferUsage.COPY_DST});owned.push(rb);
      device.pushErrorScope('validation');
      const enc=device.createCommandEncoder(),pass=enc.beginComputePass(),wg=Number(m.wg??64),groups=Math.ceil((job.threads+wg)/wg),gx=Math.min(groups,65535);
      pass.setPipeline(pipeline);pass.setBindGroup(0,group);pass.dispatchWorkgroups(gx,Math.ceil(groups/gx));pass.end();
      enc.copyBufferToBuffer(target,0,rb,0,(job.outWords+64)*4);device.queue.submit([enc.finish()]);
      const error=await device.popErrorScope();if(error)throw new Error(error.message);
      await rb.mapAsync(GPUMapMode.READ);out.push(Array.from(new Int32Array(rb.getMappedRange().slice(0))));rb.unmap();
      for(const b of owned)b.destroy();
    }
  } finally {device.destroy();}
  return JSON.stringify(out);
}

export async function withGpuPage(code,blobs,body,route=()=>false){
  const work=mkdtempSync(join(tmpdir(),'qwen-gpu-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
  const server=http.createServer((q,r)=>{
    if(route(q,r))return;
    const m=/^\/blob\/(\w+)$/.exec(q.url);
    if(m&&blobs.has(m[1])){r.writeHead(200,{'Content-Type':'application/octet-stream'});r.end(blobs.get(m[1]));return;}
    if(q.url==='/code'){r.writeHead(200,{'Content-Type':'text/plain'});r.end(code);return;}
    r.writeHead(200,{'Content-Type':'text/html'});r.end('<!doctype html><title>qwen gpu</title>');
  });
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  const edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${join(work,'profile')}`,'--enable-unsafe-webgpu','--enable-webgpu-developer-features','--no-first-run','about:blank'],{stdio:'ignore'});
  let socket;
  try{
    let target;
    for(let i=0;i<120&&!target;i++){
      try{const port=readFileSync(join(work,'profile/DevToolsActivePort'),'utf8').split('\n')[0];target=(await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{}
      if(!target)await pause(250);
    }
    if(!target)throw new Error('Browser startup timed out');
    socket=new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((r,j)=>{socket.addEventListener('open',r);socket.addEventListener('error',j);});
    const pending=new Map();let sequence=0;
    socket.addEventListener('message',e=>{const reply=JSON.parse(e.data);if(pending.has(reply.id)){pending.get(reply.id)(reply);pending.delete(reply.id);}});
    const send=(method,params={})=>new Promise((res,rej)=>{const id=++sequence,timer=setTimeout(()=>{pending.delete(id);rej(new Error('CDP timeout '+method));},300000);pending.set(id,reply=>{clearTimeout(timer);reply.error||reply.result?.exceptionDetails?rej(new Error(JSON.stringify(reply.error||reply.result.exceptionDetails))):res(reply.result);});socket.send(JSON.stringify({id,method,params}));});
    await send('Page.enable');await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/`});await pause(500);
    const run=async jobs=>JSON.parse((await send('Runtime.evaluate',{expression:`(${pageRun.toString()})(${JSON.stringify(jobs)})`,awaitPromise:true,returnByValue:true})).result.value).map(w=>new Uint32Array(Int32Array.from(w).buffer));
    const evaluate=async expression=>(await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true})).result.value;
    return await body(run,evaluate,send);
  } finally {
    try{socket?.close();}catch{}
    server.close();
    try{execFileSync('taskkill',['/PID',String(edge.pid),'/T','/F'],{stdio:'ignore'});}catch{}
  }
}
