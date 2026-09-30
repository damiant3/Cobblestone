import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {resolve,join} from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {createServer} from 'node:net';
import {createRequire} from 'node:module';
const require=createRequire(import.meta.url),linker=require('./winhost.js');
const repo=resolve(fileURLToPath(new URL('../../..',import.meta.url)));
const source=((process.argv[2].endsWith('Helper.codex') ? readFileSync(new URL('./WindowsHost.codex',import.meta.url),'utf8')+'\n'+readFileSync(new URL('./Deploy.codex',import.meta.url),'utf8')+'\n'+readFileSync(new URL('./Cleanup.codex',import.meta.url),'utf8')+'\n' : '')+readFileSync(process.argv[2],'utf8')).replace(/\r\n/g,'\n'),output=resolve(process.argv[3]);
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const probe=createServer();await new Promise(r=>probe.listen(0,'127.0.0.1',r));const port=probe.address().port;await new Promise(r=>probe.close(r));
const profile=mkdtempSync(join(tmpdir(),'modbuilder-native-'));
const edge=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--no-first-run',`--remote-debugging-port=${port}`,`--user-data-dir=${profile}`,'about:blank'],{stdio:'ignore',windowsHide:true});
let ws;
try{
  let target;for(let i=0;i<80&&!target;i++){try{target=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{}if(!target)await sleep(200);}
  if(!target)throw new Error('Browser did not start');
  ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r));let id=0;const pending=new Map();
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)?.(m);pending.delete(m.id);}});
  const send=(method,params={})=>new Promise(r=>{const n=++id;pending.set(n,r);ws.send(JSON.stringify({id:n,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.result?.exceptionDetails||r.error)throw new Error(JSON.stringify(r));return r.result.result.value;};
  const prism=process.argv.find(a=>a.startsWith('--prism='));
  await send('Page.navigate',{url:pathToFileURL(prism?resolve(prism.slice(8)):join(repo,'apps/landing/web/compile/prism.html')).href});
  let ready=false;for(let i=0;i<240&&!ready;i++){ready=await evaluate('(async()=>typeof resolveUnit==="function"&&typeof libraryImage==="function"&&!!(await libraryImage()))()');if(!ready)await sleep(250);}if(!ready)throw new Error('Compiler library did not finish loading');
  const result=await evaluate(`(async()=>{const unit=await resolveUnit({text:${JSON.stringify(source)},regions:[]});if(unit.missing.length)throw new Error(unit.missing.join(', '));const r=await runW(await moduleBytes('codex-compiler.wasm'),'CDX map hosted-windows passes=none\\n'.replace('\\\\n','\\n')+unit.text);const cdx=cdxPayload(r.bytes);if(!cdx)throw new Error(r.text.slice(0,3000));const wire=new Uint8Array(cdx.length+1);wire[0]=3;wire.set(cdx,1);const pe=await runW(await moduleBytes('pe-bytes.wasm'),wire);let s='';for(const b of pe.bytes)s+=String.fromCharCode(b);return {pe:btoa(s),map:r.text.slice(r.text.lastIndexOf('MAP:')),log:r.text.slice(0,300)};})()`);
  writeFileSync(output+'.map',result.map);writeFileSync(output+'.base',Buffer.from(result.pe,'base64'));
  const url=process.argv.find(a=>a.startsWith('--return='));
  const loader=process.argv.find(a=>a.startsWith('--loader=')),listen=process.argv.find(a=>a.startsWith('--port='));
  const bytes=linker.specialize(Buffer.from(result.pe,'base64'),result.map,process.argv.includes('--console'),{returnUrl:url?url.slice(9):'',uninstall:process.argv.includes('--uninstall'),loader:loader?readFileSync(resolve(loader.slice(9))):undefined,port:listen?Number(listen.slice(7)):8789});
  writeFileSync(output,bytes);console.log('Built native helper through browser compiler and PE plug: '+output);
}finally{ws?.close();edge.kill();await sleep(500);try{rmSync(profile,{recursive:true,force:true});}catch{}}
