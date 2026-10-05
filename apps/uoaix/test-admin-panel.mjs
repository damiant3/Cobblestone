import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,mkdirSync,mkdtempSync,existsSync} from 'node:fs';
import {resolve,join} from 'node:path';
const arg=name=>{const at=process.argv.indexOf(name);return at<0?null:process.argv[at+1];};
const url=arg('--url'),input=arg('--input'),out=resolve(arg('--out'));
if(!url||!input)throw Error('--url and --input are required');
const key=readFileSync(input,'utf8').trim().split(/\r?\n/)[3];
mkdirSync(out,{recursive:true});
const profile=mkdtempSync(join(out,'browser-'));
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{windowsHide:true,stdio:'ignore'});
console.log('Browser PID='+browser.pid);
const pause=ms=>new Promise(r=>setTimeout(r,ms));
let ws,next=0;
const pending=new Map(),checks=[],errors=[];
const watchdog=setTimeout(()=>{console.error('Panel browser proof timed out');browser.kill();process.exitCode=1;},300000);
function send(method,params={}){return new Promise((resolve,reject)=>{const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout '+method));},20000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result?.value;}
function check(name,pass){if(!pass)throw Error('FAIL '+name);checks.push(name);console.log('PASS '+name);}
async function until(expression){for(let n=0;n<200;n++){if(await evaluate(expression))return;await pause(100);}throw Error('Condition timed out: '+expression);}
try{
 const portFile=join(profile,'DevToolsActivePort');for(let i=0;i<100&&!existsSync(portFile);i++)await pause(100);if(!existsSync(portFile))throw Error('Browser did not start');
 const port=readFileSync(portFile,'utf8').split(/\r?\n/)[0],targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
 ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);await new Promise((r,j)=>{ws.addEventListener('open',r,{once:true});ws.addEventListener('error',j,{once:true});});
 ws.addEventListener('message',event=>{const m=JSON.parse(event.data);if(m.id&&pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);clearTimeout(p.timer);m.error?p.reject(Error(m.error.message)):p.resolve(m.result||{});}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);});
 await send('Page.enable');await send('Runtime.enable');await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1080,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url});await until('document.readyState === "complete" && !!document.getElementById("login-form")');
 check('unauthenticated browser sees only login',await evaluate('!document.getElementById("login").hidden && document.getElementById("app").children.length===0 && !document.body.textContent.includes("uoaixtest")'));
 await evaluate('document.getElementById("key").value="0".repeat(64);document.getElementById("login-form").requestSubmit();');await until('document.getElementById("message").textContent.includes("refused")');
 check('wrong key leaves panel unavailable',await evaluate('document.getElementById("app").children.length===0 && !document.getElementById("login").hidden'));
 await evaluate('document.getElementById("key").value='+JSON.stringify(key)+';document.getElementById("login-form").requestSubmit();');
 await until('document.getElementById("message").textContent.startsWith("Authenticated as")');
 check('authenticated panel shows live health and connection',await evaluate('document.getElementById("population").textContent==="1" && document.getElementById("connection-rows").textContent.includes("uoaixtest") && document.getElementById("connection-rows").textContent.includes("Iolo")'));
 check('key is removed from input and browser storage',await evaluate('document.getElementById("key").value==="" && localStorage.length===0 && sessionStorage.length===0'));
 const royalBefore=await evaluate('(async()=>await request({command:"treasury-view"}))()');
 check('treasury shows bound balance and all chain shares',royalBefore.available&&royalBefore.writable&&royalBefore.chains.length===7&&await evaluate('document.getElementById("treasury-chains").textContent.includes("No turnover")'));
 check('live currency view renders distinct balances and all tender selectors',royalBefore.backend==='currency'&&await evaluate('document.getElementById("treasury-balance").textContent.includes("800 copper, 80 silver")&&Array.from(document.getElementById("treasury-metal").options).map(o=>o.value).join(",")==="1,2,3"'));
 await evaluate('document.getElementById("treasury-copper-rate").value="300";document.getElementById("treasury-silver-rate").value="30";document.getElementById("treasury-rates-form").requestSubmit();');await until('document.getElementById("message").textContent==="Coin rates changed and logged."');
 check('owner changes coin valuation with old and new rates logged',await evaluate('(async()=>{const view=await request({command:"treasury-view"});return view.copper_per_gold===300&&view.silver_per_gold===30&&view.copper===800&&view.silver===80&&view.balance===8&&document.getElementById("actions").textContent.includes("treasury-rates");})()'));
 await evaluate('document.getElementById("treasury-tax").value="1.25";document.getElementById("treasury-tax-form").requestSubmit();');await until('document.getElementById("message").textContent==="Tax rate changed and logged."');
 check('owner changes tax with an admin log entry',await evaluate('(async()=>(await request({command:"treasury-view"})).tax_basis_points===125&&document.getElementById("actions").textContent.includes("treasury-tax"))()'));
 await evaluate('document.getElementById("treasury-amount").value="25";document.getElementById("treasury-grant-form").requestSubmit();');await until('document.getElementById("message").textContent==="Treasury grant transferred and logged."');
 const royalAfter=await evaluate('(async()=>await request({command:"treasury-view"}))()');check('royal-business copper grant debits treasury and credits the selected purse',royalAfter.copper===royalBefore.copper-25&&royalAfter.businesses[0].copper===royalBefore.businesses[0].copper+25&&royalAfter.balance===royalBefore.balance&&await evaluate('document.getElementById("actions").textContent.includes("treasury-grant")'));
 check('GM view names the stand-in and shows a conserved favor',await evaluate('document.getElementById("gm-mode").textContent.includes("In-memory stand-in") && document.getElementById("gm-treasury").textContent.includes("995") && gmAccounts.find(a=>a.account===101).coins===5 && document.getElementById("gm-actions").textContent.includes("IoloTester") && document.getElementById("gm-actions").textContent.includes("favor-coins")'));
 check('NPC accounts cannot be selected for GM grants',await evaluate('!Array.from(document.getElementById("gm-account").options).some(o=>o.value==="9001")'));
 await evaluate('document.getElementById("gm-account").value="101";selectGm();document.getElementById("gm-power-1").checked=true;document.getElementById("gm-power-512").checked=true;document.getElementById("gm-coin-each").value="10";document.getElementById("gm-coin-day").value="20";document.getElementById("gm-form").requestSubmit(document.getElementById("gm-dub"));');
 await until('gmAccounts.find(a=>a.account===101).policy.gm===1');
 check('owner can dub with exact powers and favor limits',await evaluate('gmAccounts.find(a=>a.account===101).policy.powers===513 && gmAccounts.find(a=>a.account===101).policy.coin_each===10 && gmAccounts.find(a=>a.account===101).policy.coin_day===20 && document.getElementById("gm-actions").textContent.includes("gm-dub")'));
 await evaluate('document.getElementById("gm-power-1").checked=false;document.getElementById("gm-power-512").checked=false;document.getElementById("gm-power-8").checked=true;document.getElementById("gm-form").requestSubmit(document.getElementById("gm-set"));');await until('gmAccounts.find(a=>a.account===101).policy.powers===8');
 check('owner can narrow an existing GM grant',await evaluate('gmAccounts.find(a=>a.account===101).policy.revision===2'));
 await evaluate('document.getElementById("gm-form").requestSubmit(document.getElementById("gm-revoke"));');await until('gmAccounts.find(a=>a.account===101).policy.gm===0 && document.getElementById("gm-actions").textContent.includes("gm-revoke")');
 check('revocation clears powers and is visible in action rows',await evaluate('gmAccounts.find(a=>a.account===101).policy.powers===0 && gmAccounts.find(a=>a.account===101).policy.coin_day===0 && document.getElementById("gm-actions").textContent.includes("gm-revoke")'));
 await evaluate('document.querySelector("#support button").click()');await until('!document.getElementById("report-detail").hidden');
 check('hostile support evidence is shown as text',await evaluate('document.getElementById("report-evidence").textContent.includes("<img") && !document.getElementById("report-evidence").querySelector("img") && !window.__panelInjected'));
 writeFileSync(join(out,'support.png'),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));
 await evaluate('document.getElementById("ack-report").click()');await until('document.getElementById("support-count").textContent==="2"');
 check('acknowledgement appears in action log',await evaluate('document.getElementById("actions").textContent.includes("report-ack") && document.getElementById("actions").textContent.includes("Shard owner")'));
 for(const [index,label]of ['NPC #1','World'].entries()){
  await evaluate('document.querySelector("#support button").click()');await until('document.getElementById("report-title").textContent.includes('+JSON.stringify(label)+')');
  check(label+' report needs no account',await evaluate('(async()=>{const report=await request({command:"report-read",id:selected});return !Object.hasOwn(report,"account")&&report.subject_kind==='+JSON.stringify(index===0?'npc':'world')+';})()'));
  writeFileSync(join(out,index===0?'npc-report.png':'world-report.png'),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));
  await evaluate('document.getElementById("ack-report").click()');await until('document.getElementById("support-count").textContent==='+JSON.stringify(String(1-index)));
 }
 await evaluate('document.getElementById("action").value="inspect";document.getElementById("target").value="1";document.getElementById("action-form").requestSubmit();');
 await until('document.getElementById("action-state").textContent.includes("awaits")');
 check('action is logged and shown as queued',await evaluate('document.getElementById("actions").textContent.includes("inspect") && document.getElementById("actions").textContent.includes("Queued") && document.getElementById("submit-action").disabled'));
 check('authenticated browser still rejects plaintext requests',await evaluate('(async()=>{const r=await fetch("/admin",{method:"POST",body:JSON.stringify({command:"panel-data",session})});return r.status===401&&(await r.text())==="";})()'));
 check('desktop has no document overflow',await evaluate('document.documentElement.scrollWidth<=innerWidth'));
 check('page contains no broken wire characters',await evaluate('!document.body.textContent.includes("\ufffd")'));
 writeFileSync(join(out,'desktop.png'),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await pause(100);
 check('narrow panel has no document overflow',await evaluate('document.documentElement.scrollWidth<=innerWidth'));
 writeFileSync(join(out,'narrow.png'),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));
 await evaluate('document.getElementById("logout").click()');await until('!document.getElementById("login").hidden && document.getElementById("app").children.length===0');
 check('logout removes panel data',await evaluate('document.getElementById("message").textContent.includes("closed")'));
 await evaluate('document.getElementById("key").value='+JSON.stringify(key)+';document.getElementById("login-form").requestSubmit();');await until('document.getElementById("message").textContent.startsWith("Authenticated as")');
 check('reconnect recovers authenticated sequence and cancels old work',await evaluate('document.getElementById("actions").textContent.includes("Cancelled") && !document.getElementById("submit-action").disabled'));
 check('browser renews an exhausted nonce reservation',await evaluate('(async()=>{const previous=leaseEnd;sequence=leaseEnd;await request({command:"panel-data"});return sequence>previous&&leaseEnd>=sequence;})()'));
 check('default game day is two real hours',await evaluate('document.getElementById("day-seconds").value==="7200"'));
 await evaluate('document.getElementById("day-seconds").value="3600";document.getElementById("day-form").requestSubmit();');await until('document.getElementById("message").textContent.includes("Game-day length changed to 3600")');
 check('live day change is reflected and logged with old and new values',await evaluate('document.getElementById("day-seconds").value==="3600" && document.getElementById("actions").textContent.includes("game-day-length") && document.getElementById("actions").textContent.includes("7200") && document.getElementById("actions").textContent.includes("3600")'));
 await evaluate('(async()=>{await request({command:"gm-dub",account:101,powers:513,coin_each:10,coin_day:20,item_each:0,item_day:0,title_day:0,plot_day:0});await refreshGms();document.getElementById("gm-account").value="101";selectGm();document.getElementById("gm-key-issue").click();})()');
 await until('document.getElementById("gm-issued-key").value.startsWith("UOAIXGM1:")');
 const gmKey=await evaluate('document.getElementById("gm-issued-key").value');
 check('owner issues personal key without putting the secret in audit',await evaluate('(async()=>!JSON.stringify((await request({command:"gm-log",before:0})).rows).includes(document.getElementById("gm-issued-key").value.split(":")[2]))()'));
 async function signIn(credential){await evaluate('document.getElementById("key").value='+JSON.stringify(credential)+';document.getElementById("login-form").requestSubmit();');await until('document.getElementById("login").hidden && document.getElementById("message").textContent.startsWith("Authenticated as")');}
 async function signOut(){await evaluate('document.getElementById("logout").click()');await until('!document.getElementById("login").hidden');}
 await signOut();await signIn(gmKey);
 check('GM signs in as their account with only delegated actions',await evaluate('document.getElementById("principal").textContent==="MaraTester" && document.getElementById("gm-controls").hidden && document.getElementById("action-form").closest(".card").hidden && Array.from(document.getElementById("gm-work-action").options).map(x=>x.value).join(",")==="favor-coins"'));
 check('GM cannot invoke owner treasury clock or mind commands directly',await evaluate('(async()=>{for(const command of ["gm-dub","gm-key-set","panel-action","panel-day-set","treasury-view","treasury-tax","treasury-grant","treasury-rates","mind-next","gm-log","gm-list"]){try{await request({command,account:101,action:"lord-british-enter",seconds:24,basis_points:10000,purse:56,amount:1});return false;}catch(e){if(e.message!=="role")return false;}}return document.getElementById("day-controls").hidden&&document.getElementById("treasury-controls").hidden;})()'));
 await evaluate('document.getElementById("gm-work-target").value="102";document.getElementById("gm-work-amount").value="3";document.getElementById("gm-work-detail").value="loyal service";document.getElementById("gm-work-form").requestSubmit();');
 await until('document.getElementById("gm-own-actions").textContent.includes("favor-coins accepted")');
 check('GM favor is audited against authenticated account',await evaluate('(async()=>(await request({command:"gm-own-log",before:0})).rows.some(x=>x.action==="favor-coins"&&x.actor_account===101&&x.target_account===102&&x.amount===3))()'));
 check('GM cannot forge a power or actor in JSON',await evaluate('(async()=>{try{await request({command:"gm-action",action:"kick",actor:102,powers:8191,target:102,amount:1,subject:0,detail:"forged"});return false;}catch(e){return e.message==="power-not-granted";}})()'));
 check('GM log excludes other accounts action history',await evaluate('(async()=>(await request({command:"gm-own-log",before:0})).rows.every(x=>x.actor_account===101||(x.actor_account===0&&x.target_account===101)))()'));
 writeFileSync(join(out,'gm-narrow.png'),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));
 await signOut();await signIn(key);
 check('owner sees GM action and conserved treasury balances',await evaluate('(async()=>gmAccounts.find(a=>a.account===102).coins===3 && document.getElementById("gm-treasury").textContent.includes("992 coin") && (await request({command:"gm-log",before:0})).rows.some(x=>x.action==="favor-coins"&&x.actor_account===101))()'));
 await evaluate('(async()=>{await request({command:"gm-set",account:101,powers:1,coin_each:0,coin_day:0,item_each:0,item_day:0,title_day:0,plot_day:0});})()');
 await signOut();await signIn(gmKey);
 check('narrowed GM credential has no favor action',await evaluate('document.getElementById("gm-work-action").options.length===0 && document.getElementById("gm-work-submit").disabled'));
 await signOut();await signIn(key);
 await evaluate('(async()=>{await request({command:"gm-key-revoke",account:101});})()');await signOut();
 await evaluate('document.getElementById("key").value='+JSON.stringify(gmKey)+';document.getElementById("login-form").requestSubmit();');await until('document.getElementById("message").textContent.includes("Authentication refused")');
 check('revoked personal key receives no panel',await evaluate('!document.getElementById("login").hidden && document.getElementById("app").children.length===0'));
 await signIn(key);
 await evaluate('request=()=>Promise.reject(Error("injected-close-failure"));document.getElementById("logout").click();');await until('!document.getElementById("login").hidden');
 check('failed server close still clears browser credentials and panel data',await evaluate('session===0 && incoming.every(x=>x===0) && outgoing.every(x=>x===0) && document.getElementById("app").children.length===0 && document.getElementById("key").value==="" && document.getElementById("message").textContent.includes("not confirmed")'));
 check('no browser exceptions',errors.length===0);
 writeFileSync(join(out,'browser-result.json'),JSON.stringify({passed:true,checks,errors},null,2));console.log('UOAIX PANEL BROWSER PASS');
}catch(error){let pageMessage='';try{pageMessage=await evaluate('document.getElementById("message")?.textContent||""');writeFileSync(join(out,'failure.png'),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));}catch{}writeFileSync(join(out,'browser-result.json'),JSON.stringify({passed:false,checks,errors,error:String(error),pageMessage},null,2));throw error;}
finally{clearTimeout(watchdog);if(ws?.readyState===WebSocket.OPEN){try{await send('Browser.close');}catch{}ws.close();}browser.kill();}
