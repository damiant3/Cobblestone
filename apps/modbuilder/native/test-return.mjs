import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,mkdtempSync,mkdirSync} from 'node:fs';
import {createServer} from 'node:http';
import {createConnection} from 'node:net';
import {resolve,join} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {randomUUID} from 'node:crypto';
const repo=resolve(fileURLToPath(new URL('../../..',import.meta.url)));
const root=join(repo,'build-output/native-helper');mkdirSync(root,{recursive:true});
const scratch=mkdtempSync(join(root,'browser-return-')),exe=join(scratch,'ModBuilder.exe'),page=join(scratch,'pairing workspace.html');
const legacy=process.argv.includes('--legacy-control');
const env={...process.env,LOCALAPPDATA:scratch};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const run=(args)=>new Promise((resolve,reject)=>{const p=spawn(process.execPath,args,{cwd:repo,windowsHide:true,stdio:'inherit'});p.once('error',reject);p.once('exit',code=>code===0?resolve():reject(new Error('Build failed: '+code)));});
await new Promise((resolve,reject)=>{const p=createConnection({host:'127.0.0.1',port:8789});p.once('connect',()=>{p.destroy();reject(new Error('Native helper already running; return test refuses replacement'));});p.once('error',e=>e.code==='ECONNREFUSED'?resolve():reject(e));});
const nonce=randomUUID();let verdict,fail,child,timer;
const result=new Promise((resolve,reject)=>{verdict=resolve;fail=reject;});
result.catch(()=>{});
const server=createServer((q,r)=>{
  r.setHeader('Access-Control-Allow-Origin','null');r.setHeader('Access-Control-Allow-Private-Network','true');r.setHeader('Connection','close');
  if(q.method==='OPTIONS'){r.setHeader('Access-Control-Allow-Methods','POST');r.end();return;}
  if(q.url!=='/result/'+nonce){r.writeHead(404);r.end();return;}
  let body='';q.on('data',b=>{body+=b;if(body.length>4096)q.destroy();});q.on('end',()=>{try{const value=JSON.parse(body);r.end('{}',()=>verdict(value));}catch{r.writeHead(400);r.end();}});
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const address='http://127.0.0.1:'+server.address().port+'/result/'+nonce;
const before='<script>window.__returnTest={token:new URLSearchParams(location.hash.slice(1)).get("bridge"),priorUrl:localStorage.getItem("prism.bridge.url"),priorToken:localStorage.getItem("prism.bridge.token")};</script>';
const after='<script>(()=>{const state=window.__returnTest;let sent=false;const finish=async ok=>{if(sent)return;sent=true;if(localStorage.getItem("prism.bridge.token")===state.token){for(const [key,value] of [["prism.bridge.url",state.priorUrl],["prism.bridge.token",state.priorToken]]){if(value===null)localStorage.removeItem(key);else localStorage.setItem(key,value);}}const restored=localStorage.getItem("prism.bridge.url")===state.priorUrl&&localStorage.getItem("prism.bridge.token")===state.priorToken;try{await fetch('+JSON.stringify(address)+',{method:"POST",body:JSON.stringify({ok,fragmentPresent:!!state.token,restored,title:document.title})});}finally{window.close();}};const poll=setInterval(()=>{if(!state.token){clearInterval(poll);finish(false);}else if(document.getElementById("mb-setup-card")?.classList.contains("done")&&document.getElementById("features")?.open){clearInterval(poll);finish(true);}},100);setTimeout(()=>{clearInterval(poll);finish(false);},20000);})();</script>';
try{
  let html=readFileSync(join(repo,'apps/modbuilder/web/workspace.html'),'utf8');
  html=html.replace('<head>','<head>'+before).replace('</body>',after+'</body>');writeFileSync(page,html);
  let source=join(repo,'apps/modbuilder/native/Helper.codex');
  if(legacy){const original=readFileSync(source,'utf8');const changed=original.replace('mb-open-page h url','mb-call (h.shell) 0 (mb-wide "open") (mb-wide url) 0 0 1 0 0');if(changed===original)throw new Error('Legacy-control call site missing');source=join(scratch,'LegacyHelper.codex');writeFileSync(source,changed);}
  await run([join(repo,'apps/modbuilder/native/build-helper.mjs'),source,exe,'--return='+pathToFileURL(page).href]);
  timer=setTimeout(()=>fail(new Error('Default-browser return did not complete setup')),30000);
  child=spawn(exe,['--test-launch'],{env,windowsHide:true,stdio:'ignore'});
  child.once('error',e=>{clearTimeout(timer);verdict({ok:false,error:e.message});});
  const outcome=await result;clearTimeout(timer);
  if(!outcome.restored)throw new Error('Browser bridge storage was not restored');
  if(legacy){if(outcome.ok||outcome.fragmentPresent)throw new Error('Legacy control did not reproduce lost pairing');console.log('PASS legacy file-document launch reproduces the missing pairing fragment (negative control)');}
  else{if(!outcome.ok||!outcome.fragmentPresent||outcome.title!=='Mod Builder')throw new Error('Native default-browser handoff failed: '+JSON.stringify(outcome));console.log('PASS native helper opens the real default browser with pairing intact; authenticated discovery collapses Setup and opens Features');}
}finally{
  clearTimeout(timer);
  if(child&&child.exitCode===null){
    await new Promise(resolve=>{const u=spawn(exe,['--test','--uninstall'],{env,windowsHide:true,stdio:'ignore'});const deadline=setTimeout(()=>{u.kill();resolve();},10000);const done=()=>{clearTimeout(deadline);resolve();};u.once('error',done);u.once('exit',done);});
    for(let i=0;i<30&&child.exitCode===null;i++)await delay(100);
    if(child.exitCode===null)child.kill();
  }
  server.close();
}
