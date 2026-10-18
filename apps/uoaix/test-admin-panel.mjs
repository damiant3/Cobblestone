import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,mkdirSync,mkdtempSync,existsSync} from 'node:fs';
import {resolve,join} from 'node:path';
const arg=name=>{const at=process.argv.indexOf(name);return at<0?null:process.argv[at+1];};
const url=arg('--url'),input=arg('--input'),out=resolve(arg('--out'));
if(!url||!input)throw Error('--url and --input are required');
const key=readFileSync(input,'utf8').trim().split(/\r?\n/)[3];
mkdirSync(out,{recursive:true});
const profile=mkdtempSync(join(out,'browser-'));
// Edge isolates a sandboxed srcdoc frame into its own process; these switches keep the panel frame in the page's process so
// Runtime.evaluate can address its execution context.
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+profile,'--disable-features=IsolateSandboxedIframes,site-per-process','--disable-site-isolation-trials','about:blank'],{windowsHide:true,stdio:'ignore'});
console.log('Browser PID='+browser.pid);
const pause=ms=>new Promise(r=>setTimeout(r,ms));
let ws,next=0,frame=0;
const pending=new Map(),checks=[],errors=[],contexts=new Map();
const watchdog=setTimeout(()=>{console.error('Panel browser proof timed out');browser.kill();process.exitCode=1;},300000);
function send(method,params={}){return new Promise((resolve,reject)=>{const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout '+method));},20000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression,contextId){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true,...(contextId?{contextId}:{})});if(r.exceptionDetails)throw Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result?.value;}
const inFrame=expression=>evaluate(expression,frame);
function check(name,pass){if(!pass)throw Error('FAIL '+name);checks.push(name);console.log('PASS '+name);}
async function until(expression,contextId){for(let n=0;n<200;n++){try{if(await evaluate(expression,contextId))return;}catch{}await pause(100);}throw Error('Condition timed out: '+expression);}
async function idle(){for(let n=0;n<200;n++){if(await inFrame('Number(state_get("busy"))')===0){await pause(300);if(await inFrame('Number(state_get("busy"))')===0)return;}await pause(100);}throw Error('Panel stayed busy');}
async function findFrame(marker){frame=0;for(let n=0;n<200&&!frame;n++){for(const id of [...contexts.keys()].reverse()){try{if(await evaluate('!!document.getElementById('+JSON.stringify(marker)+')',id)){frame=id;break;}}catch{}}if(!frame)await pause(100);}if(!frame)throw Error('Panel frame not found');await idle();}
// One request from inside the frame, through the login page's sealed channel; resolves to the parsed answer.
async function request(command){await idle();return JSON.parse(await inFrame('new Promise(r=>host_request_then('+JSON.stringify(JSON.stringify(command))+',x=>r(x)))'));}
const text=id=>inFrame('(document.getElementById('+JSON.stringify(id)+')||{}).textContent||""');
const loginShown='document.getElementById("login").style.display!=="none" && !document.querySelector("#panel iframe")';
async function shot(name){writeFileSync(join(out,name),Buffer.from((await send('Page.captureScreenshot',{captureBeyondViewport:true,format:'png'})).data,'base64'));}
async function signIn(credential,marker='treasury-card'){await evaluate('document.getElementById("key").value='+JSON.stringify(credential)+';document.getElementById("connect").click()');await until('document.getElementById("message").textContent.startsWith("Authenticated as") && !!document.querySelector("#panel iframe")');await findFrame(marker);}
async function signOut(){await idle();await inFrame('document.getElementById("logout").click()');await until(loginShown);}
try{
 const portFile=join(profile,'DevToolsActivePort');for(let i=0;i<100&&!existsSync(portFile);i++)await pause(100);if(!existsSync(portFile))throw Error('Browser did not start');
 const port=readFileSync(portFile,'utf8').split(/\r?\n/)[0],targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
 ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);await new Promise((r,j)=>{ws.addEventListener('open',r,{once:true});ws.addEventListener('error',j,{once:true});});
 ws.addEventListener('message',event=>{const m=JSON.parse(event.data);if(m.id&&pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);clearTimeout(p.timer);m.error?p.reject(Error(m.error.message)):p.resolve(m.result||{});}
  if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.exception?.description||m.params.exceptionDetails.text);
  if(m.method==='Runtime.executionContextCreated')contexts.set(m.params.context.id,m.params.context);
  if(m.method==='Runtime.executionContextDestroyed')contexts.delete(m.params.executionContextId);
  if(m.method==='Runtime.executionContextsCleared')contexts.clear();});
 await send('Page.enable');await send('Runtime.enable');await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1080,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url});await until('document.readyState === "complete" && !!document.getElementById("connect")');
 check('unauthenticated browser sees only login',await evaluate(loginShown+' && !document.body.textContent.includes("uoaixtest")'));
 await evaluate('document.getElementById("key").value="0".repeat(64);document.getElementById("connect").click()');await until('document.getElementById("message").textContent.includes("refused")');
 check('wrong key leaves panel unavailable',await evaluate(loginShown));
 await signIn(key);
 check('authenticated panel shows live health and connection',await text('population')==='1'&&(await text('connection-rows')).includes('uoaixtest')&&(await text('connection-rows')).includes('Iolo'));
 const drawn=ids=>inFrame(JSON.stringify(ids)+'.every(id=>!!document.getElementById(id).querySelector("svg,canvas"))');
 check('graphs draw the treasury by metal over the kept game hours',await text('graph-note')==='3 game hours kept, oldest first.'&&await drawn(['chart-gold','chart-silver','chart-copper'])&&!await inFrame('!!document.querySelector("#chart-price svg,#chart-price canvas")')&&await inFrame('Array.from(document.getElementById("item-pick").options).map(o=>o.textContent).join(",")==="bread,fish"'));
 await inFrame('document.getElementById("item-pick").value="2";document.getElementById("item-show").click()');await idle();
 check('an item graphs its lowest shop price and the stock all shops hold',await drawn(['chart-price','chart-stock']));
 check('key is removed from input and browser storage',await evaluate('document.getElementById("key").value==="" && localStorage.length===0 && sessionStorage.length===0'));
 const royalBefore=await request({command:'treasury-view'});
 check('treasury shows bound balance and all chain shares',royalBefore.available&&royalBefore.writable&&royalBefore.chains.length===7&&(await text('treasury-chains')).includes('No turnover'));
 check('live currency view renders distinct balances and all tender selectors',royalBefore.backend==='currency'&&(await text('treasury-balance')).includes('800 copper, 80 silver')&&await inFrame('Array.from(document.getElementById("treasury-metal").options).map(o=>o.value).join(",")==="1,2,3"'));
 check('treasury shows the mining tax rate and the ore it took',royalBefore.mining_tax_basis_points===1000&&(await text('treasury-balance')).includes('Ore taxed at mining (10.00%): '+royalBefore.gold_ore_taxed+' gold, '+royalBefore.silver_ore_taxed+' silver, '+royalBefore.copper_ore_taxed+' copper.'));
 await inFrame('document.getElementById("rate-copper").value="300";document.getElementById("rate-silver").value="30";document.getElementById("rates-submit").click()');await until('document.getElementById("message").textContent==="Coin rates changed and logged."',frame);await idle();
 const rated=await request({command:'treasury-view'});
 check('owner changes coin valuation with old and new rates logged',rated.copper_per_gold===300&&rated.silver_per_gold===30&&rated.copper===800&&rated.silver===80&&rated.balance===8&&(await text('actions')).includes('treasury-rates'));
 await inFrame('document.getElementById("treasury-tax").value="1.25";document.getElementById("tax-submit").click()');await until('document.getElementById("message").textContent==="Tax rate changed and logged."',frame);await idle();
 check('owner changes tax with an admin log entry',(await request({command:'treasury-view'})).tax_basis_points===125&&(await text('actions')).includes('treasury-tax'));
 await inFrame('document.getElementById("treasury-amount").value="25";document.getElementById("grant-submit").click()');await until('document.getElementById("message").textContent==="Treasury grant transferred and logged."',frame);await idle();
 const royalAfter=await request({command:'treasury-view'});
 check('royal-business copper grant debits treasury and credits the selected purse',royalAfter.copper===royalBefore.copper-25&&royalAfter.businesses[0].copper===royalBefore.businesses[0].copper+25&&royalAfter.balance===royalBefore.balance&&(await text('actions')).includes('treasury-grant'));
 const gmOf=async account=>(await request({command:'gm-list'})).accounts.find(a=>a.account===account);
 check('GM view names the stand-in and shows a conserved favor',(await text('gm-card')).includes('stand-in')&&(await text('gm-treasury')).includes('995')&&(await gmOf(101)).coins===5&&(await text('gm-actions')).includes('IoloTester')&&(await text('gm-actions')).includes('favor-coins'));
 check('NPC accounts cannot be selected for GM grants',await inFrame('!Array.from(document.getElementById("gm-account").options).some(o=>o.value==="9001")'));
 await inFrame('(()=>{const s=document.getElementById("gm-account");s.value="101";s.dispatchEvent(new Event("input"));document.getElementById("pw-1").click();document.getElementById("pw-512").click();document.getElementById("gm-coin-each").value="10";document.getElementById("gm-coin-day").value="20";document.getElementById("gm-dub").click();return 1})()');
 await idle();let mara=await gmOf(101);
 check('owner can dub with exact powers and favor limits',mara.policy.gm===1&&mara.policy.powers===513&&mara.policy.coin_each===10&&mara.policy.coin_day===20&&(await text('gm-actions')).includes('gm-dub'));
 await inFrame('document.getElementById("pw-1").click();document.getElementById("pw-512").click();document.getElementById("pw-8").click();document.getElementById("gm-set").click()');await idle();mara=await gmOf(101);
 check('owner can narrow an existing GM grant',mara.policy.powers===8&&mara.policy.revision===2);
 await inFrame('document.getElementById("gm-revoke").click()');await idle();mara=await gmOf(101);
 check('revocation clears powers and is visible in action rows',mara.policy.gm===0&&mara.policy.powers===0&&mara.policy.coin_day===0&&(await text('gm-actions')).includes('gm-revoke'));
 await inFrame('document.querySelector("#support button").click()');await until('document.getElementById("report-detail").textContent.includes("<img")',frame);
 check('hostile support evidence is shown as text',await inFrame('!document.getElementById("report-detail").querySelector("img") && !window.__panelInjected'));
 await shot('support.png');
 await inFrame('document.querySelector("#report-actions button").click()');await until('document.getElementById("support-count").textContent==="2"',frame);await idle();
 check('acknowledgement appears in action log',(await text('actions')).includes('report-ack')&&(await text('actions')).includes('Shard owner'));
 for(const [index,label]of ['NPC #1','World'].entries()){
  const reviewed=Number(await inFrame('document.querySelector("#support button").id.slice(7)'));
  await inFrame('document.querySelector("#support button").click()');await until('document.getElementById("report-detail").textContent.includes('+JSON.stringify(label)+')',frame);
  const report=await request({command:'report-read',id:reviewed});
  check(label+' report needs no account',!Object.hasOwn(report,'account')&&report.subject_kind===(index===0?'npc':'world'));
  await shot(index===0?'npc-report.png':'world-report.png');
  await inFrame('document.querySelector("#report-actions button").click()');await until('document.getElementById("support-count").textContent==='+JSON.stringify(String(1-index)),frame);await idle();
 }
 await inFrame('document.getElementById("act-action").value="inspect";document.getElementById("act-target").value="1";document.getElementById("act-submit").click()');
 await until('document.getElementById("action-state").textContent.includes("awaits")',frame);await idle();
 check('action is logged and shown as queued',(await text('actions')).includes('inspect')&&(await text('actions')).includes('Queued'));
 check('authenticated browser still rejects plaintext requests',await evaluate('(async()=>{const r=await fetch("/admin",{method:"POST",body:JSON.stringify({command:"panel-data",session:Number(state_get("session"))})});return r.status===401&&(await r.text())==="";})()'));
 check('desktop has no document overflow',await evaluate('document.documentElement.scrollWidth<=innerWidth')&&await inFrame('document.documentElement.scrollWidth<=innerWidth'));
 check('page contains no broken wire characters',!(await evaluate('document.body.textContent')).includes('\ufffd')&&!(await inFrame('document.body.textContent')).includes('\ufffd'));
 await shot('desktop.png');
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await pause(300);
 check('narrow panel has no document overflow',await evaluate('document.documentElement.scrollWidth<=innerWidth')&&await inFrame('document.documentElement.scrollWidth<=innerWidth'));
 await shot('narrow.png');
 await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1080,deviceScaleFactor:1,mobile:false});
 await signOut();
 check('logout removes panel data',await evaluate('document.getElementById("message").textContent.includes("closed") && document.getElementById("panel").children.length===0'));
 await signIn(key);
 check('reconnect recovers authenticated sequence and cancels old work',(await text('actions')).includes('Cancelled'));
 const lease=await evaluate('Number(state_get("lease"))');await evaluate('state_set("seq",'+lease+')');await request({command:'panel-data'});
 check('browser renews an exhausted nonce reservation',await evaluate('Number(state_get("seq"))>'+lease+' && Number(state_get("lease"))>=Number(state_get("seq"))'));
 check('the panel is tabbed: Overview shows first, the treasury tab shows the treasury and hides the overview (Damian, AdminPanel.md layout)',await inFrame('(()=>{const shown=id=>getComputedStyle(document.getElementById(id).closest(".tabpanel")).display!=="none";const first=document.querySelectorAll("nav.tabs:not(.subtabs) label").length===8&&shown("support")&&!shown("treasury-card");document.getElementById("label-tab-treasury").click();const moved=shown("treasury-card")&&!shown("support");document.getElementById("label-tab-world").click();return first&&moved&&shown("day-card");})()'));
 check('default game day is two real hours',await inFrame('document.getElementById("day-seconds").value==="7200"'));
 await inFrame('document.getElementById("day-seconds").value="3600";document.getElementById("day-submit").click()');await until('document.getElementById("message").textContent.includes("Game-day length changed to 3600")',frame);await idle();
 check('log levels list every subsystem at trace',await inFrame('document.getElementById("log-levels").rows.length===16 && document.getElementById("log-levels").rows[0].cells[1].textContent==="trace" && [...document.getElementById("log-levels").rows].some(r=>r.cells[0].textContent==="combat"&&r.cells[1].textContent==="inherit (trace)")'));
 await inFrame('document.getElementById("log-subsystem").value="combat";document.getElementById("log-level").value="debug";document.getElementById("level-submit").click()');await until('document.getElementById("message").textContent.includes("Log level for combat set to debug")',frame);await idle();
 check('a combat override is applied, shown and logged',await inFrame('[...document.getElementById("log-levels").rows].some(r=>r.cells[0].textContent==="combat"&&r.cells[1].textContent==="debug")')&&(await text('actions')).includes('log-level'));
 const acts=await text('actions');
 check('live day change is reflected and logged with old and new values',await inFrame('document.getElementById("day-seconds").value==="3600"')&&acts.includes('game-day-length')&&acts.includes('7200')&&acts.includes('3600'));
 await request({command:'gm-dub',account:101,powers:513,coin_each:10,coin_day:20,item_each:0,item_day:0,title_day:0,plot_day:0});
 await inFrame('document.getElementById("gm-latest").click()');await idle();
 await inFrame('(()=>{const s=document.getElementById("gm-account");s.value="101";s.dispatchEvent(new Event("input"));document.getElementById("gm-key-issue").click();return 1})()');
 await until('document.getElementById("gm-issued-key").value.startsWith("UOAIXGM1:")',frame);await idle();
 const gmKey=await inFrame('document.getElementById("gm-issued-key").value');
 check('owner issues personal key without putting the secret in audit',!JSON.stringify((await request({command:'gm-log',before:0})).rows).includes(gmKey.split(':')[2]));
 await signOut();await signIn(gmKey,'gm-work');
 check('GM signs in as their account with only delegated actions',await evaluate('document.getElementById("message").textContent==="Authenticated as MaraTester."')&&await inFrame('document.getElementById("gm-card").classList.contains("hide") && !document.getElementById("gm-work").classList.contains("hide") && document.getElementById("label-tab-actions").classList.contains("hide") && Array.from(document.getElementById("gm-work-action").options).map(x=>x.value).join(",")==="favor-coins"'));
 let refusedAll=true;for(const command of ['gm-dub','gm-key-set','panel-action','panel-day-set','panel-levels','panel-level-set','treasury-view','treasury-tax','treasury-grant','treasury-rates','mind-next','gm-log','gm-list']){const r=await request({command,account:101,action:'lord-british-enter',seconds:24,basis_points:10000,purse:56,amount:1});if(r.error!=='role')refusedAll=false;}
 check('GM cannot invoke owner treasury clock or mind commands directly',refusedAll&&await inFrame('["label-tab-world","label-tab-treasury"].every(id=>document.getElementById(id).classList.contains("hide"))'));
 await inFrame('document.getElementById("gm-work-target").value="102";document.getElementById("gm-work-amount").value="3";document.getElementById("gm-work-detail").value="loyal service";document.getElementById("gm-work-submit").click()');
 await until('document.getElementById("gm-own-actions").textContent.includes("favor-coins accepted")',frame);
 check('GM favor is audited against authenticated account',(await request({command:'gm-own-log',before:0})).rows.some(x=>x.action==='favor-coins'&&x.actor_account===101&&x.target_account===102&&x.amount===3));
 check('GM cannot forge a power or actor in JSON',(await request({command:'gm-action',action:'kick',actor:102,powers:8191,target:102,amount:1,subject:0,detail:'forged'})).error==='power-not-granted');
 check('GM log excludes other accounts action history',(await request({command:'gm-own-log',before:0})).rows.every(x=>x.actor_account===101||(x.actor_account===0&&x.target_account===101)));
 await shot('gm.png');
 await signOut();await signIn(key);
 check('owner sees GM action and conserved treasury balances',(await gmOf(102)).coins===3&&(await text('gm-treasury')).includes('992 coin')&&(await request({command:'gm-log',before:0})).rows.some(x=>x.action==='favor-coins'&&x.actor_account===101));
 await request({command:'gm-set',account:101,powers:1,coin_each:0,coin_day:0,item_each:0,item_day:0,title_day:0,plot_day:0});
 await signOut();await signIn(gmKey,'gm-work');
 check('narrowed GM credential has no favor action',await inFrame('document.getElementById("gm-work-action").options.length===0'));
 await signOut();await signIn(key);
 await request({command:'gm-key-revoke',account:101});await signOut();
 await evaluate('document.getElementById("key").value='+JSON.stringify(gmKey)+';document.getElementById("connect").click()');await until('document.getElementById("message").textContent.includes("Authentication refused")');
 check('revoked personal key receives no panel',await evaluate(loginShown));
 await signIn(key);
 await idle();await evaluate('window.fetch=()=>Promise.reject(Error("injected-close-failure"))');await inFrame('document.getElementById("logout").click()');await until(loginShown);
 check('failed server close still clears browser credentials and panel data',await evaluate('Number(state_get("session"))===0 && state_get_text("in")==="" && state_get_text("out")==="" && document.getElementById("panel").children.length===0 && document.getElementById("key").value==="" && document.getElementById("message").textContent.includes("not confirmed")'));
 check('no browser exceptions',errors.length===0);
 writeFileSync(join(out,'browser-result.json'),JSON.stringify({passed:true,checks,errors},null,2));console.log('UOAIX PANEL BROWSER PASS');
}catch(error){let pageMessage='';try{pageMessage=await evaluate('document.getElementById("message")?.textContent||""');await shot('failure.png');}catch{}writeFileSync(join(out,'browser-result.json'),JSON.stringify({passed:false,checks,errors,error:String(error),pageMessage},null,2));console.error(String(error));process.exitCode=1;}
finally{clearTimeout(watchdog);if(ws?.readyState===WebSocket.OPEN){try{await send('Browser.close');}catch{}ws.close();}browser.kill();}
