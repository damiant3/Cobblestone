import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtempSync,mkdirSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {createServer} from 'node:net';

const exe=resolve(process.argv.find(a=>a.startsWith('--exe='))?.slice(6)||'build-output/native-cors/ModBuilder.exe');
const page=process.argv.find(a=>a.startsWith('--url='))?.slice(6)||'https://cobblestoneproject.com/modbuilder/';
const origin=new URL(page).origin,port=18789,base='http://127.0.0.1:'+port;
const probe=createServer();await new Promise((r,j)=>{probe.once('error',j);probe.listen(port,'127.0.0.1',r);});await new Promise(r=>probe.close(r));
const scratch=mkdtempSync(join(tmpdir(),'modbuilder-cors-')),steam=join(scratch,'Steam'),game=join(steam,'steamapps/common/Valheim'),local=join(scratch,'local');
for(const name of ['valheim.exe','UnityPlayer.dll','valheim_Data/Managed/assembly_valheim.dll','MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll']){const path=join(game,name);mkdirSync(resolve(path,'..'),{recursive:true});writeFileSync(path,'CORS discovery fixture');}
mkdirSync(local);
const helper=spawn(exe,['--test'],{windowsHide:true,env:{...process.env,LOCALAPPDATA:local,MODBUILDER_TEST_STEAM:steam},stdio:['ignore','pipe','pipe']});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));let edge,ws;
try{
  const ready=await new Promise((r,j)=>{let text='';const timer=setTimeout(()=>j(new Error('Helper startup timeout')),10000);helper.stdout.on('data',b=>{text+=b.toString();if(text.includes('\n')){clearTimeout(timer);try{r(JSON.parse(text.trim()));}catch(error){j(error);}}});helper.on('error',j);helper.on('exit',code=>{clearTimeout(timer);j(new Error('Helper exited '+code));});});
  for(let i=0;i<4;i++){
    await sleep(600);
    const preflight=await fetch(base+'/health',{method:'OPTIONS',headers:{Origin:origin,'Access-Control-Request-Method':'GET','Access-Control-Request-Headers':'x-bridge-token','Access-Control-Request-Private-Network':'true'}});
    assert.equal(preflight.headers.get('access-control-allow-origin'),origin,'hosted origin survives idle polls and request heap resets');
    assert.equal(preflight.status,200);assert.equal(preflight.headers.get('access-control-allow-private-network'),'true');
    const health=await fetch(base+'/health',{headers:{Origin:origin,'X-Bridge-Token':ready.token}});
    assert.equal(health.headers.get('access-control-allow-origin'),origin);const data=await health.json();assert(data.ok&&data.tokenOk&&data.inventory.found);
  }
  const wrong=await fetch(base+'/health',{method:'OPTIONS',headers:{Origin:'https://untrusted.example','Access-Control-Request-Method':'GET'}});assert.equal(wrong.status,403);assert.notEqual(wrong.headers.get('access-control-allow-origin'),'https://untrusted.example');
  const anonymous=await(await fetch(base+'/health',{headers:{Origin:origin}})).json();assert(!anonymous.tokenOk&&anonymous.inventory===null);
  console.log('PASS repeated HTTPS-origin preflight, authenticated health and origin/token refusal controls');
  if(!process.argv.includes('--native-only')){
    const debug=createServer();await new Promise(r=>debug.listen(0,'127.0.0.1',r));const debugPort=debug.address().port;await new Promise(r=>debug.close(r));
    edge=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--no-first-run','--remote-debugging-port='+debugPort,'--user-data-dir='+join(scratch,'browser'),'about:blank'],{windowsHide:true,stdio:'ignore'});
    let target;for(let i=0;i<80&&!target;i++){try{target=(await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json()).find(t=>t.type==='page');}catch{}if(!target)await sleep(200);}assert(target,'browser started');
    ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r));let id=0;const pending=new Map();
    const failures=[];
    ws.addEventListener('message',event=>{const m=JSON.parse(event.data);if(m.id){pending.get(m.id)?.(m);pending.delete(m.id);}if(m.method==='Network.loadingFailed')failures.push({error:m.params.errorText,cors:m.params.corsErrorStatus});});
    const send=(method,params={})=>new Promise(r=>{const n=++id;pending.set(n,r);ws.send(JSON.stringify({id:n,method,params}));});
    const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.error||r.result?.exceptionDetails)throw new Error('Browser evaluation failed');return r.result.result.value;};
    const permission=await send('Browser.grantPermissions',{origin,permissions:['localNetworkAccess','loopbackNetwork']});if(permission.error)throw new Error('Local network permission could not be granted: '+JSON.stringify(permission.error));
    await send('Network.enable');
    const url=new URL(page);url.hash=new URLSearchParams({bridge:ready.token,bridgeUrl:base}).toString();await send('Page.navigate',{url:url.href});
    let paired=false;for(let i=0;i<240&&!paired;i++){paired=await evaluate(`document.getElementById('mb-setup-card')?.classList.contains('done')===true`);if(!paired)await sleep(250);}
    const status=await evaluate(`document.getElementById('mb-connection-status')?.textContent||document.title`);
    assert(paired,'actual HTTPS page pairs with the real native helper: '+JSON.stringify({status,failures}));
    await evaluate(`window.helperChecks=0;const check=forgeFrame.contentWindow.mbw.check;forgeFrame.contentWindow.mbw.check=async()=>{try{return await check();}finally{window.helperChecks++;}}`);
    for(let i=0;i<3;i++){await sleep(600);await evaluate(`window.dispatchEvent(new Event('focus'))`);let checked=false;for(let j=0;j<60&&!checked;j++){checked=await evaluate(`window.helperChecks>${i}`);if(!checked)await sleep(100);}assert(checked,'returning to the page automatically checks the helper');assert(await evaluate(`!document.getElementById('mb-check-helper')&&document.getElementById('mb-setup-card').classList.contains('done')&&!document.getElementById('mb-create-project').disabled&&!location.hash.includes('bridge=')`),'automatic connection check remains paired after heap reuse');}
    console.log('PASS live HTTPS page pairs and automatically rechecks the real native helper on return');
  }
}finally{ws?.close();edge?.kill();helper.kill();await sleep(600);assert(scratch.startsWith(tmpdir()));try{rmSync(scratch,{recursive:true,force:true});}catch{}}
