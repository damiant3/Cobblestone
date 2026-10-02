import {spawn} from 'node:child_process';
import {createServer} from 'node:http';
import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import {resolve,join} from 'node:path';
if(!process.argv[2]||!process.argv[3]||!process.argv[4])throw Error('Usage: node ai-browser.mjs <generated page> <artifact directory> <browser executable>');
const html=readFileSync(resolve(process.argv[2]),'utf8');
const output=resolve(process.argv[3]);
const browser=resolve(process.argv[4]);
if(!existsSync(browser))throw Error('Browser executable missing: '+browser);
const work=join(output,'browser-profile');
mkdirSync(work,{recursive:true});
const requests=[];
let stepInFlight=0,maxStepInFlight=0;
let pauseOverlapped=false;
let rejectCommitment=false;
let rejectSkill=false;
let rejectCardOrder=false;
let rejectAttack=false;
let rejectManualTarget=false,manualTargetOptions=[];
let rejectTargetPolicy=false,lastDecisionTarget=null;
let rejectCombatPolicy=false,lastCombatPlan=[];
let rejectActivation=false,abilityTargetOptions=[],previewStateOnce=false;
let rejectCancel=false;
let rejectDisruption=false,rejectDisruptionPolicy=false,lastDisruptionReply=null;
let lastCantripTarget=null,cantripFizzle=false;
let queueTargetOptions=[],rejectQueuedOrder=false,rejectQueuedCancel=false;
let rejectOrder=false,rejectTarget=false,rejectPhase=false,rejectSpells=false,rejectPlay=false,rejectMana=false,rejectFluor=false,rejectLoyalty=false,rejectBlocks=false;
const names=['Upkeep','Draw','Pre-combat main','Declare attackers','Declare blockers','Combat damage','Post-combat main','End step'];
const phases=()=>names.map((label,phase)=>({phase,label,enabled:false}));
const state={turn:1,step:0,'active-player':0,'game-active':false,'game-over':false,winner:-1,paused:false,'turn-stage':0,'phase-label':'Upkeep',
 generals:[{name:'Warlord',life:30},{name:'Dawn Commander',life:30}],
 battlefield:[{id:10,name:'Bear',controller:0,type:'Creature',power:2,toughness:2,'attack-ready':true},{id:20,name:'Small',controller:1,type:'Creature',power:1,toughness:1,'attack-ready':false}],
 libraries:[50,50],'prismatic-state':[{raysTotal:3,raysUsed:0},{raysTotal:2,raysUsed:0}],attackers:[],
 orders:[{stance:0,freeze:false,freezes:phases(),'auto-block':true,targets:[]},{stance:0,freeze:false,freezes:phases(),'auto-block':true,targets:[]}]};
state.orders[0].freezes[0].enabled=true;
state['loyalty-used']=false;state['loyalty-abilities']=[];
state['queued-abilities']=[{player:0,pending:false},{player:1,pending:false}];state['queue-options']=[];
state['queued-loyalty']=[{player:0,pending:false},{player:1,pending:false}];state['queue-loyalty-options']=[];
state['queued-cards']=[{player:0,pending:false}];state['queue-card-options']=[];
for(const p of state.orders){p['spell-policy']=0;p['ray-reserve']=0;p['preserved-stones']=0;p['manual-payment']=false;p['block-policy']=0;p['block-threshold']=10;p['commitment-pause']=true;p['target-pause']=false;p['combat-pause']=false;}
const server=createServer((q,r)=>{
 const u=new URL(q.url,'http://localhost');
 if(!u.pathname.startsWith('/api/')){r.writeHead(200,{'Content-Type':'text/html'});r.end(html);return;}
 requests.push(u.pathname+u.search);
 let result=state;
 if(u.pathname.endsWith('/skill')){if(rejectSkill){result={error:'invalid skill level'};rejectSkill=false;}else state.orders[+u.searchParams.get('player')].skill=+u.searchParams.get('level');}
 if(u.pathname.endsWith('/response-targets'))result={card:+u.searchParams.get('card'),recommended:manualTargetOptions[0]?.id??-3,targets:manualTargetOptions};
 if(u.pathname.endsWith('/response-ability-targets'))result={permanent:+u.searchParams.get('permanent'),index:+u.searchParams.get('index'),recommended:manualTargetOptions[0]?.id??-3,targets:manualTargetOptions};
 if(u.pathname.endsWith('/response-ability')){state.disruption=null;state['disruption-result']={name:'Pending spell','response-kind':'ability','reply-source':30,'reply-ability':0,reply:-1,chance:0,roll:0,fizzled:false,disrupted:false,'reply-fizzled':false,abandoned:false};}
 if(u.pathname.endsWith('/queue-card-targets'))result={player:+u.searchParams.get('player'),card:+u.searchParams.get('card'),recommended:manualTargetOptions[0]?.id??-3,targets:manualTargetOptions};
 if(u.pathname.endsWith('/queue-card')){
   const player=+u.searchParams.get('player');
   if(u.searchParams.get('choice')==='cancel')state['queued-cards']=[{player,pending:false}];
   else state['queued-cards']=[{player,pending:true,card:+u.searchParams.get('card'),target:u.searchParams.has('target')?+u.searchParams.get('target'):-3,label:'Queued exact card',reason:'Waiting for a later own main phase'}];
 }
 if(u.pathname.endsWith('/cancel')){
   if(rejectCancel){result={error:'cancel refused'};rejectCancel=false;}
   else Object.assign(state,{'game-active':false,'game-over':false,winner:-1,paused:false,'turn-stage':0,'phase-label':'Upkeep',turn:1,commitment:null,'combat-choice':null,disruption:null,'disruption-result':null,battlefield:[],attackers:[],'manual-hand':[],'hand-orders':[],'permanent-abilities':[],'loyalty-abilities':[],'queued-abilities':[{player:0,pending:false},{player:1,pending:false}],'queue-options':[]});
 }
 if(u.pathname.endsWith('/targets'))result={card:+u.searchParams.get('card'),recommended:manualTargetOptions[0]?.id??-3,targets:manualTargetOptions};
 if(u.pathname.endsWith('/ability-targets')){
   if(previewStateOnce){result=state;previewStateOnce=false;}
   else result={permanent:+u.searchParams.get('permanent'),index:+u.searchParams.get('index'),recommended:abilityTargetOptions[0]?.id??-3,targets:abilityTargetOptions};
 }
 if(u.pathname.endsWith('/queue-targets'))result={player:+u.searchParams.get('player'),permanent:+u.searchParams.get('permanent'),index:+u.searchParams.get('index'),recommended:queueTargetOptions[0]?.id??-3,targets:queueTargetOptions};
 if(u.pathname.endsWith('/queue-loyalty-targets'))result={player:+u.searchParams.get('player'),index:+u.searchParams.get('index'),recommended:queueTargetOptions[0]?.id??-3,targets:queueTargetOptions};
 if(u.pathname.endsWith('/queue-loyalty')){
   const player=+u.searchParams.get('player');
   if(u.searchParams.get('choice')==='cancel')state['queued-loyalty'][player]={player,pending:false};
   else {const index=+u.searchParams.get('index'),target=u.searchParams.has('target')?+u.searchParams.get('target'):-3;const option=state['queue-loyalty-options'].find(o=>o.player===player&&o.index===index);if(!option)result={error:'General ability cannot be queued'};else state['queued-loyalty'][player]={player,pending:true,index,target,label:option.label,reason:'Waiting for army loyalty 9'};}
 }
 if(u.pathname.endsWith('/queue-ability')){
   const player=+u.searchParams.get('player');
   if(u.searchParams.get('choice')==='cancel'){
     if(rejectQueuedCancel){result={error:'queue cancellation refused'};rejectQueuedCancel=false;}
     else state['queued-abilities'][player]={player,pending:false};
   }else if(rejectQueuedOrder){result={error:'invalid queued target'};rejectQueuedOrder=false;}
   else {const permanent=+u.searchParams.get('permanent'),index=+u.searchParams.get('index'),target=u.searchParams.has('target')?+u.searchParams.get('target'):-3;const option=state['queue-options'].find(o=>o.player===player&&o.permanent===permanent&&o.index===index);if(!option)result={error:'ability cannot be queued'};else state['queued-abilities'][player]={player,pending:true,permanent,index,target,label:option.label,reason:'Waiting for a later own main phase'};}
 }
 if(u.pathname.endsWith('/activate')){
   if(rejectActivation){result={error:'invalid ability target'};rejectActivation=false;}
   else {const source=+u.searchParams.get('permanent'),index=+u.searchParams.get('index');if(index===0){state['permanent-abilities'].find(a=>a.permanent===source&&a.index===index).ready=false;state.generals[0].life-=2;state['prismatic-state'][0].raysUsed++;state.battlefield.find(p=>p.id===source).tapped=true;}else state.generals[0].life+=2;}
 }
 if(u.pathname.endsWith('/new')) {state['game-active']=true;state.generals[0].name=u.searchParams.get('general-a')==='1'?'High Sage':'Warlord';}
 if(u.pathname.endsWith('/commitment-policy'))state.orders[+u.searchParams.get('player')]['commitment-pause']=u.searchParams.get('enabled')==='1';
 if(u.pathname.endsWith('/disruption-policy')){
   if(rejectDisruptionPolicy){result={error:'invalid disruption policy'};rejectDisruptionPolicy=false;}
   else state.orders[+u.searchParams.get('player')]['disruption-pause']=u.searchParams.get('enabled')==='1';
 }
 if(u.pathname.endsWith('/disruption')){
   if(rejectDisruption){result={error:'disruption is no longer payable; choose Pass'};rejectDisruption=false;}
   else if(u.searchParams.get('choice')==='cantrip'){lastDisruptionReply=+u.searchParams.get('card');lastCantripTarget=u.searchParams.has('target')?+u.searchParams.get('target'):-3;state['disruption-result']={name:state.disruption.name,reply:lastDisruptionReply,chance:0,roll:0,disrupted:false,fizzled:false,'response-kind':'cantrip','reply-fizzled':cantripFizzle,abandoned:false};state.disruption=null;}
   else {lastDisruptionReply=u.searchParams.get('choice')==='disrupt'?+u.searchParams.get('card'):-1;state['disruption-result']={name:state.disruption?.name??'Spell',reply:lastDisruptionReply,chance:lastDisruptionReply<0?0:100,roll:lastDisruptionReply<0?0:50,disrupted:lastDisruptionReply>=0,fizzled:false};state.disruption=null;}
 }
 if(u.pathname.endsWith('/sacrifice-policy'))state.orders[+u.searchParams.get('player')]['sacrifice-pause']=u.searchParams.get('enabled')==='1';
 if(u.pathname.endsWith('/combat-policy')){
   if(rejectCombatPolicy){result={error:'invalid combat policy'};rejectCombatPolicy=false;}
   else state.orders[+u.searchParams.get('player')]['combat-pause']=u.searchParams.get('enabled')==='1';
 }
 if(u.pathname.endsWith('/combat-choice')){
   lastCombatPlan=state.attackers.map(a=>a.id);state['combat-choice']=null;state.paused=true;state['turn-stage']=4;state['phase-label']='Declare blockers';
 }
 if(u.pathname.endsWith('/target-policy')){
   if(rejectTargetPolicy){result={error:'invalid target policy'};rejectTargetPolicy=false;}
   else state.orders[+u.searchParams.get('player')]['target-pause']=u.searchParams.get('enabled')==='1';
 }
 if(u.pathname.endsWith('/attack')){
   if(rejectAttack){result={error:'illegal attacker choice'};rejectAttack=false;}
   else {const id=+u.searchParams.get('attacker');state.attackers=state.attackers.filter(a=>a.id!==id);if(u.searchParams.get('enabled')==='1')state.attackers.push({id});state['combat-choice']=null;}
 }
 if(u.pathname.endsWith('/commitment')){
   if(rejectCommitment){result={error:'pending card is no longer playable; choose Hold'};rejectCommitment=false;}
   else {if(state.commitment&&u.searchParams.get('choice')!=='hold')lastDecisionTarget=u.searchParams.get('choice')==='timeout'?state.commitment.recommended:u.searchParams.has('target')?+u.searchParams.get('target'):state.commitment.recommended;state.commitment=null;}
 }
 if(u.pathname.endsWith('/override')){state.commitment=null;state['manual-override']=true;state.paused=true;}
 if(u.pathname.endsWith('/card-order')){
   if(rejectCardOrder){result={error:'invalid card order'};rejectCardOrder=false;}
   else {
     const card=+u.searchParams.get('card'),mode=+u.searchParams.get('order');
     const hand=state['hand-orders']?.find(c=>c.id===card),field=state.battlefield.find(p=>p['card-id']===card);
     if(hand)hand['play-order']=mode;
     if(field)field['play-order']=mode;
     const manual=state['manual-hand']?.find(c=>c.id===card);if(manual)manual['play-order']=mode;
   }
 }
 if(u.pathname.endsWith('/posture')){
   if(rejectOrder){result={error:'invalid posture'};rejectOrder=false;}
   else {const p=+u.searchParams.get('player');state.orders[p]={...state.orders[p],stance:+u.searchParams.get('stance'),freeze:u.searchParams.get('freeze')==='1'};if(u.searchParams.has('blocks')){state.orders[p]['auto-block']=u.searchParams.get('blocks')==='1';state.orders[p]['block-policy']=state.orders[p]['auto-block']?0:1;}state.orders[p].freezes[3].enabled=state.orders[p].freeze;}
 }
 if(u.pathname.endsWith('/blocks')){
   if(rejectBlocks){result={error:'invalid block policy'};rejectBlocks=false;}
   else {const p=+u.searchParams.get('player'),mode=+u.searchParams.get('policy');state.orders[p]['block-policy']=mode;state.orders[p]['block-threshold']=mode===3?+u.searchParams.get('threshold'):10;state.orders[p]['auto-block']=mode!==1;}
 }
 if(u.pathname.endsWith('/freeze')) {
   if(rejectPhase){result={error:'phase rejected'};rejectPhase=false;}
   else {const p=+u.searchParams.get('player'),phase=+u.searchParams.get('phase');state.orders[p].freezes[phase].enabled=u.searchParams.get('enabled')==='1';state.orders[p].freeze=state.orders[p].freezes[3].enabled;}
 }
 if(u.pathname.endsWith('/spells')) {
   if(rejectSpells){result={error:'spell policy rejected'};rejectSpells=false;}
   else {const p=+u.searchParams.get('player');state.orders[p]['spell-policy']=+u.searchParams.get('policy');state.orders[p]['ray-reserve']=state.orders[p]['spell-policy']===2?+u.searchParams.get('reserve'):0;}
 }
 if(u.pathname.endsWith('/play')) {
   if(rejectManualTarget){result={error:'invalid spell target'};rejectManualTarget=false;}
   else if(rejectPlay){result={error:state.orders[0]['manual-payment']?'invalid gemstone payment':'card cannot be played at this pause'};rejectPlay=false;}
   else if(state['combat-cantrip']){if(+u.searchParams.get('player')!==state['manual-player'])result={error:'wrong player for this combat freeze'};else {state['combat-cantrip-used']=true;state['manual-hand']=[];}}
   else {const index=state['manual-hand'].findIndex(c=>c.id===+u.searchParams.get('card'));if(index>=0){
     const played=state['manual-hand'].splice(index,1)[0];
     if(Array.isArray(state['hand-orders'])){
       const order=state['hand-orders'].find(c=>c.id===played.id)?.['play-order']||0;
       state['hand-orders']=state['hand-orders'].filter(c=>c.id!==played.id);
       state.battlefield.push({id:500+played.id,'card-id':played.id,'template-id':played['template-id'],name:played.name,controller:state['active-player'],owner:state['active-player'],type:'Creature',power:1,toughness:1,orderable:true,'play-order':order===2?0:order});
     }
   }}
 }
 if(u.pathname.endsWith('/mana')) {
   if(rejectMana){result={error:'mana order rejected'};rejectMana=false;}
   else {const p=+u.searchParams.get('player'),bit=1<<+u.searchParams.get('stone');if(u.searchParams.get('keep')==='1')state.orders[p]['preserved-stones']|=bit;else state.orders[p]['preserved-stones']&=~bit;}
 }
 if(u.pathname.endsWith('/payment-mode')){state.orders[+u.searchParams.get('player')]['manual-payment']=u.searchParams.get('manual')==='1';}
 if(u.pathname.endsWith('/fluorescence')){
   if(rejectFluor){result={error:'cannot activate Fluorescence'};rejectFluor=false;}
   else {state['prismatic-state'][0].fluorescenceActive=true;state['prismatic-state'][0].raysUsed++;state.battlefield.find(p=>p.id===+u.searchParams.get('stone')).tapped=true;}
 }
 if(u.pathname.endsWith('/loyalty')){
   if(rejectLoyalty){result={error:'General ability unavailable'};rejectLoyalty=false;}
   else {state.generals[0].life+=4;state['loyalty-used']=true;for(const a of state['loyalty-abilities'])a.ready=false;}
 }
 if(u.pathname.endsWith('/target')){
   if(rejectTarget){result={error:'target rejected'};rejectTarget=false;}
   else {const p=+u.searchParams.get('player'),kind=u.searchParams.get('kind'),ts=state.orders[p].targets;
     if(kind==='clear')state.orders[p].targets=[];
     else if(kind==='remove')ts.splice(+u.searchParams.get('index'),1);
     else {const key=kind+':'+(u.searchParams.get('id')||u.searchParams.get('type')||u.searchParams.get('level')||'');
       const label=kind==='general'?'Enemy General':kind==='permanent'?state.battlefield.find(b=>String(b.id)===u.searchParams.get('id')).name:kind==='type'?u.searchParams.get('type'):['High threat','Medium threat','Low threat'][+u.searchParams.get('level')];
       if(!ts.some(t=>t.key===key))ts.push({key,label});
     }
   }
 }
 if(u.pathname.endsWith('/pause')){pauseOverlapped ||= stepInFlight>0;state['pause-active']=true;}
 if(u.pathname.endsWith('/discard')){const card=+u.searchParams.get('card');state.cleanup.cards=state.cleanup.cards.filter(c=>c.id!==card);state.cleanup.count--;if(state.cleanup.count===0)state.cleanup=null;}
 if(u.pathname.endsWith('/resume'))state['pause-active']=false;
 if(u.pathname.endsWith('/step')){
   if(u.searchParams.get('resume')==='1'){state['manual-override']=false;state.paused=false;state['turn-stage']=0;state['phase-label']='Upkeep';state.turn++;}
   else {state.paused=true;state['turn-stage']=3;state['phase-label']='Declare attackers';state.attackers=[{id:10}];}
 }
 if(u.pathname.endsWith('/step')){
   stepInFlight++;maxStepInFlight=Math.max(maxStepInFlight,stepInFlight);
   setTimeout(()=>{r.writeHead(200,{'Content-Type':'application/json'});r.end(JSON.stringify(result));stepInFlight--;},200);
   return;
 }
 r.writeHead(200,{'Content-Type':'application/json'});r.end(JSON.stringify(result));
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const port=server.address().port;
const edge=spawn(browser,['--headless=new','--remote-debugging-port=0','--user-data-dir='+work,'--no-first-run','about:blank'],{stdio:'ignore',windowsHide:true});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
console.log('Browser PID '+edge.pid+'; profile '+work);
let ws;
const stopBrowser=()=>new Promise(done=>{
 if(edge.exitCode!==null||!edge.pid){done();return;}
 const killer=spawn('taskkill',['/PID',String(edge.pid),'/T','/F'],{stdio:'ignore',windowsHide:true});
 killer.on('error',()=>{edge.kill();done()});killer.on('exit',done);
});
const watchdog=setTimeout(async()=>{console.error('FAIL browser watchdog');await stopBrowser();server.close();process.exit(1)},60000);
try{
 let dbg;
 for(let i=0;i<100;i++){try{dbg=readFileSync(work+'/DevToolsActivePort','utf8').split('\n')[0];break}catch{}await sleep(100);}
 if(!dbg)throw Error('No debugger endpoint');
 const tabs=await(await fetch('http://127.0.0.1:'+dbg+'/json/list')).json();
 ws=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);
 await new Promise((r,j)=>{ws.addEventListener('open',r);ws.addEventListener('error',j)});
 let seq=0;const pending=new Map();const errors=[];
 ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)?.(m);pending.delete(m.id)}else if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.exception?.description||m.params.exceptionDetails.text);else if(m.method==='Runtime.consoleAPICalled'&&m.params.type==='error')errors.push(JSON.stringify(m.params.args));});
 const send=(method,params={})=>new Promise(r=>{const id=++seq;pending.set(id,r);ws.send(JSON.stringify({id,method,params}))});
 const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,userGesture:true});if(r.error||r.result.exceptionDetails)throw Error(JSON.stringify(r));return r.result.result.value;};
 const wait=async expression=>{for(let i=0;i<60;i++){if(await evaluate('('+expression+') && Number(state_get("g-busy"))===0'))return;await sleep(50)}throw Error('UI condition failed: '+expression+'; exceptions='+JSON.stringify(errors)+'; requests='+JSON.stringify(requests.slice(-5))+'; page='+await evaluate('location.href+" "+document.readyState+" busy="+state_get("g-busy")+" message="+document.getElementById("g-message")?.innerText'))};
 const click=id=>evaluate(`document.getElementById(${JSON.stringify(id)}).click()`);
 const assert=(label,pass)=>{if(!pass)throw Error(label);console.log('PASS '+label)};
 await send('Runtime.enable');await send('Page.enable');await send('Emulation.setDeviceMetricsOverride',{width:1280,height:900,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/'});
 await wait('!!document.getElementById("g-general-a")');
 await wait('document.getElementById("gfr0-0").innerText==="Upkeep: pause"');
 assert('saved freezes load before play',requests.some(q=>q.endsWith('/state')));
 await click('gfr0-0');await wait('document.getElementById("gfr0-0").innerText==="Upkeep: auto"');
 await click('g-general-a');await click('g-new');
 await wait('document.body.innerText.includes("High Sage | Life: 30")');
 assert('selected General reaches new-game API',requests.some(q=>q.includes('/new?general-a=1')));
 await click('gp0-blocks');await wait('document.getElementById("gp0-blocks").innerText==="Blocks: manual"');
 await click('gp0-blocks');await wait('document.getElementById("gp0-blocks").innerText==="Blocks: none"');
 await click('gp0-blocks');await wait('document.getElementById("gp0-blocks").innerText==="Blocks: protect life"');
 await click('gp0-life-up');await wait('document.getElementById("gp0-life-label").innerText==="Protect below: 11"');
 assert('block policy and threshold reach API',requests.some(q=>q.endsWith('/blocks?player=0&policy=3&threshold=11')));
 rejectBlocks=true;await click('gp0-life-up');await wait('document.body.innerText.includes("invalid block policy")');
 assert('rejected block threshold keeps acknowledgement',await evaluate('document.getElementById("gp0-life-label").innerText==="Protect below: 11"'));
 await click('gkeep0-0');await wait('document.getElementById("gkeep0-0").innerText==="AI Ruby: keep"');
 await click('gkeep1-5');await wait('document.getElementById("gkeep1-5").innerText==="AI Diamond: keep"');
 assert('independent mana orders reach API',requests.some(q=>q.endsWith('/mana?player=0&stone=0&keep=1'))&&requests.some(q=>q.endsWith('/mana?player=1&stone=5&keep=1')));
 rejectMana=true;await click('gkeep0-0');await wait('document.body.innerText.includes("mana order rejected")');
 assert('rejected mana order preserves acknowledgement',await evaluate('document.getElementById("gkeep0-0").innerText==="AI Ruby: keep"'));
 await click('gp0-spells');await wait('document.getElementById("gp0-spells").innerText==="Spells: Hold counters"');
 assert('hold counters reaches API',requests.some(q=>q.includes('/spells?player=0&policy=1&reserve=0')));
 await click('gp0-reserve-up');await wait('document.getElementById("gp0-reserve-label").innerText==="Reserve rays: 1"');
 assert('reserve selects conservation',await evaluate('document.getElementById("gp0-spells").innerText==="Spells: Conserve rays"'));
 rejectSpells=true;await click('gp0-reserve-up');await wait('document.body.innerText.includes("spell policy rejected")');
 assert('rejected reserve retains acknowledged value',await evaluate('document.getElementById("gp0-reserve-label").innerText==="Reserve rays: 1"'));
 await click('gp1-spells');await wait('document.getElementById("gp1-spells").innerText==="Spells: Hold counters"');
 await click('gp0-reserve-down');await wait('document.getElementById("gp0-reserve-label").innerText==="Reserve rays: 0"');
 await click('gp0-reserve-up');await wait('document.getElementById("gp0-reserve-label").innerText==="Reserve rays: 1"');
 await click('gp0-stance');await wait('document.getElementById("gp0-stance").innerText==="Aggressive"');
 assert('stance reaches API',requests.some(q=>q.includes('player=0&stance=1')));
 assert('stance update preserves protect-life policy',await evaluate('document.getElementById("gp0-blocks").innerText==="Blocks: protect life"&&document.getElementById("gp0-life-label").innerText==="Protect below: 11"'));
 rejectOrder=true;await click('gp0-stance');await wait('document.body.innerText.includes("invalid posture")');
 assert('rejected order keeps acknowledged stance',await evaluate('document.getElementById("gp0-stance").innerText==="Aggressive"'));
 await click('gt0-g');await wait('document.getElementById("gp0-target-list").innerText.includes("Enemy General")');
 await click('gt0-p-20');await wait('document.getElementById("gp0-target-list").innerText.includes("Small")');
 await click('gt0-p-20');await sleep(150);
 assert('duplicate targets do not grow the queue',await evaluate('document.getElementById("gp0-target-list").children.length===2'));
 await click('gt0-r-0');await wait('!document.getElementById("gp0-target-list").innerText.includes("Enemy General")');
 await click('gp0-kind');await click('gt0-t');await wait('document.getElementById("gp0-target-list").innerText.includes("Equipment")');
 await click('gp0-threat');await click('gt0-h');await wait('document.getElementById("gp0-target-list").innerText.includes("Medium threat")');
 assert('permanent, type and threat orders reach the API',requests.some(q=>q.includes('kind=permanent&id=20'))&&requests.some(q=>q.includes('kind=type&type=Equipment'))&&requests.some(q=>q.includes('kind=threat&level=1')));
 rejectTarget=true;await click('gt0-g');await wait('document.body.innerText.includes("target rejected")');
 assert('rejected target keeps acknowledged queue',await evaluate('document.getElementById("gp0-target-list").children.length===3'));
 await click('gt0-c');await wait('document.getElementById("gp0-target-list").children.length===0');
 assert('clear target order reaches the API',requests.some(q=>q.includes('kind=clear')));
 await click('gfr0-3');await wait('document.getElementById("gfr0-3").innerText.includes("pause")');
 await click('gfr0-7');await wait('document.getElementById("gfr0-7").innerText.includes("pause")');
 assert('independent phase freezes reach API',requests.some(q=>q.includes('/freeze?player=0&phase=3&enabled=1'))&&requests.some(q=>q.includes('/freeze?player=0&phase=7&enabled=1')));
 rejectPhase=true;await click('gfr0-1');await wait('document.body.innerText.includes("phase rejected")');
 assert('rejected freeze keeps acknowledged state',await evaluate('document.getElementById("gfr0-1").innerText==="Draw: auto"'));
 const beforeAuto=requests.filter(q=>q.includes('/step')).length;
 await click('g-auto');
 for(let i=0;i<30&&stepInFlight===0;i++)await sleep(5);
 assert('Auto request is pending for overlap check',stepInFlight===1);
 await click('g-step');
 await wait('document.getElementById("g-step").innerText==="Continue"');
 assert('Auto and manual step requests are serialized',maxStepInFlight===1&&requests.filter(q=>q.includes('/step')).length===beforeAuto+1);
 const steps=requests.filter(q=>q.includes('/step')).length;await sleep(900);
 assert('auto stops at freeze',requests.filter(q=>q.includes('/step')).length===steps);
 await wait('document.getElementById("gat-10")?.innerText==="Attacking: stay back"');
 assert('attacker freeze shows recommendation for legal attackers only',await evaluate('!document.getElementById("gat-20")&&document.body.innerText.includes("Review the suggested attackers")'));
 rejectAttack=true;await click('gat-10');await wait('document.body.innerText.includes("illegal attacker choice")');
 assert('rejected attacker edit preserves selection',await evaluate('document.getElementById("gat-10").innerText==="Attacking: stay back"'));
 await click('gat-10');await wait('document.getElementById("gat-10").innerText==="Stay back: attack"');
 await send('Page.reload');await wait('document.getElementById("gat-10")?.innerText==="Stay back: attack"');
 assert('reload preserves an empty attacker selection',state.attackers.length===0);
 await click('gat-10');await wait('document.getElementById("gat-10").innerText==="Attacking: stay back"');
 assert('explicit attacker choices reach API',requests.some(q=>q.endsWith('/attack?attacker=10&enabled=0'))&&requests.some(q=>q.endsWith('/attack?attacker=10&enabled=1')));
 const attackShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'attacker-choice.png'),Buffer.from(attackShot.result.data,'base64'));
 await click('g-step');await wait('document.getElementById("g-turn-lbl").innerText==="Turn 2"');
 assert('attacker controls leave with the phase',await evaluate('!document.getElementById("gat-10")'));
 assert('continue explicitly resumes',requests.some(q=>q.endsWith('/step?resume=1')));
 assert('combat choice defaults off',await evaluate('document.getElementById("gp0-combat-pause").innerText==="Combat choice: off"'));
 rejectCombatPolicy=true;await click('gp0-combat-pause');await wait('document.body.innerText.includes("invalid combat policy")');
 assert('rejected combat setting stays off',await evaluate('document.getElementById("gp0-combat-pause").innerText==="Combat choice: off"'));
 await click('gp0-combat-pause');await wait('document.getElementById("gp0-combat-pause").innerText==="Combat choice: on"');
 state.orders[0].freezes[3].enabled=false;state.paused=true;state['turn-stage']=3;state['phase-label']='Declare attackers';state.attackers=[{id:10}];state['combat-choice']={nonce:501,'remaining-ms':15000};
 await send('Page.reload');await wait('!!document.getElementById("g-combat-title")');
 assert('combat prompt shows the AI plan and edit-cancels rule',await evaluate('document.getElementById("gat-10").innerText.includes("Attacking")&&document.getElementById("g-combat-timeout").innerText.includes("Editing attackers cancels")&&!document.getElementById("g-auto")'));
 const combatShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'combat-choice.png'),Buffer.from(combatShot.result.data,'base64'));
 state['combat-choice']['remaining-ms']=700;await send('Page.reload');await wait('!!document.getElementById("g-combat-title")');await sleep(100);
 assert('combat timeout waits for remaining deadline',!requests.some(q=>q.includes('/combat-choice?nonce=501')));
 await wait('!document.getElementById("g-combat-title")');
 assert('untouched combat timeout accepts the shown AI plan once',lastCombatPlan.join(',')==='10'&&requests.filter(q=>q.endsWith('/combat-choice?nonce=501&choice=timeout')).length===1);
 state.paused=true;state['turn-stage']=3;state['phase-label']='Declare attackers';state.attackers=[{id:10}];state['combat-choice']={nonce:502,'remaining-ms':700};
 await send('Page.reload');await wait('!!document.getElementById("g-combat-title")');
 await click('gat-10');await wait('!document.getElementById("g-combat-title")');await sleep(900);
 assert('editing attackers cancels browser timeout',!requests.some(q=>q.includes('/combat-choice?nonce=502'))&&state.attackers.length===0&&await evaluate('document.getElementById("g-step").innerText==="Continue"'));
 await click('g-step');await wait('document.getElementById("g-phase").innerText==="Upkeep"');
 assert('edited combat plan uses manual Continue',state.attackers.length===0);
 state.paused=true;state['turn-stage']=3;state['phase-label']='Declare attackers';state.attackers=[{id:10}];state['combat-choice']={nonce:503,'remaining-ms':15000};
 await send('Page.reload');await wait('!!document.getElementById("g-combat-title")');await click('g-step');await wait('!document.getElementById("g-combat-title")');
 assert('combat Continue carries the decision nonce',requests.some(q=>q.endsWith('/combat-choice?nonce=503&choice=continue')));
 state.orders[0].freezes[3].enabled=true;
 state.paused=true;state['turn-stage']=4;state['phase-label']='Declare blockers';state.attackers=[{id:10}];
 await click('gp1-blocks');await wait('!!document.getElementById("gb-20")');
 await click('gb-20');await click('ga-10');await wait('document.body.innerText.includes("Block assigned.")');
 assert('manual assignment reaches API',requests.some(q=>q.endsWith('/block?blocker=20&attacker=10')));
 await send('Page.reload');await wait('document.getElementById("g-step")?.innerText==="Continue"');
 assert('reload recovers paused game and phase',await evaluate('document.getElementById("g-phase").innerText==="Declare blockers"&&document.getElementById("gfr0-7").innerText==="End: pause"'));
 assert('reload preserves independent spell policies',await evaluate('document.getElementById("gp0-spells").innerText==="Spells: Conserve rays"&&document.getElementById("gp0-reserve-label").innerText==="Reserve rays: 1"&&document.getElementById("gp1-spells").innerText==="Spells: Hold counters"'));
 assert('reload preserves both mana orders',await evaluate('document.getElementById("gkeep0-0").innerText==="AI Ruby: keep"&&document.getElementById("gkeep1-5").innerText==="AI Diamond: keep"'));
 assert('reload preserves block threshold',await evaluate('document.getElementById("gp0-blocks").innerText==="Blocks: protect life"&&document.getElementById("gp0-life-label").innerText==="Protect below: 11"'));
 state['turn-stage']=2;state['phase-label']='Pre-combat main';state['manual-hand']=[{id:101,'template-id':1,name:'Small',cost:'{1}',rays:1,ready:true},{id:103,'template-id':3,name:'Giant',cost:'{2}',rays:1,ready:true},{id:105,'template-id':5,name:'Disruption',cost:'{2}',rays:2,ready:false}];
 state.generals[0].loyalty=1;state['loyalty-abilities']=[{index:0,label:'Gain 4 life',cost:1,ready:true}];
 await click('gp0-spells');await wait('!!document.getElementById("gc-0")');
 await wait('!!document.getElementById("gly-0")');rejectLoyalty=true;await click('gly-0');await wait('document.body.innerText.includes("General ability unavailable")');
 assert('rejected loyalty activation preserves choice',await evaluate('!!document.getElementById("gly-0")'));
 await click('gly-0');await wait('document.getElementById("g-loyalty-status").innerText==="Used this turn."');
 assert('loyalty activation reaches API and removes second choice',requests.filter(q=>q.endsWith('/loyalty?index=0')).length===2&&await evaluate('!document.getElementById("gly-0")&&document.getElementById("g-loyalty-heading").innerText.includes("Army loyalty: 1")'));
 assert('manual policy exposes main-phase card choices',await evaluate('document.getElementById("gp0-spells").innerText==="Spells: Manual"&&document.getElementById("g-manual-hand").innerText.includes("Disruption: unavailable")'));
 state['permanent-abilities']=[{permanent:10,index:0,label:'Bear #10: Deal 3 damage',cost:'1 ray, tap, pay 2 life',ready:true},{permanent:10,index:1,label:'Bear #10: Gain 2 life',cost:'0 rays',ready:true}];
 abilityTargetOptions=[{id:20,label:'Small #20'},{id:-2,label:'Enemy General'}];
 await click('gp0-paymode');await wait('!!document.getElementById("gpa-0")');
 assert('permanent activation shows all costs',await evaluate('document.getElementById("gpa-0").innerText.includes("1 ray, tap, pay 2 life")'));
 previewStateOnce=true;const beforeActivation=requests.filter(q=>q.includes('/activate?')).length;
 await click('gpa-0');await wait('Number(state_get("g-busy"))===0');
 assert('state response to a preview resynchronizes without activating',requests.filter(q=>q.includes('/activate?')).length===beforeActivation);
 await click('gpa-0');await wait('!!document.getElementById("gmt-1")');
 await click('g-target-cancel');await wait('!document.getElementById("gmt-1")');
 assert('cancel ability targeting spends nothing',requests.filter(q=>q.includes('/activate?')).length===beforeActivation);
 await click('gpa-0');await wait('!!document.getElementById("gmt-1")');
 rejectActivation=true;await click('gmt-1');await wait('document.body.innerText.includes("invalid ability target")');
 assert('rejected ability keeps its source and picker',await evaluate('!!document.getElementById("gpa-0")&&!!document.getElementById("gmt-1")'));
 const abilityShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'permanent-ability.png'),Buffer.from(abilityShot.result.data,'base64'));
 await click('gmt-1');await wait('!document.getElementById("gpa-0")');
 assert('ability target carries permanent and ability IDs without chain drafting',requests.some(q=>q.endsWith('/activate?permanent=10&index=0&target=-2'))&&await evaluate('document.getElementById("g-payment").children.length===0'));
 await send('Page.reload');await wait('!!document.getElementById("gpa-held-0")');
 assert('reload keeps a paid tap ability unavailable',await evaluate('!document.getElementById("gpa-0")'));
 abilityTargetOptions=[];await click('gpa-1');await wait('Number(state_get("g-busy"))===0');
 assert('untargeted repeatable ability activates without a picker',requests.some(q=>q.endsWith('/activate?permanent=10&index=1'))&&await evaluate('!!document.getElementById("gpa-1")&&!document.getElementById("gmt-0")'));
 const queueRestore={paused:state.paused,stage:state['turn-stage'],phase:state['phase-label'],manual0:state.orders[0]['manual-payment'],manual1:state.orders[1]['manual-payment'],life1:state.generals[1].life};
 state.paused=false;state.orders[1]['manual-payment']=true;state.generals[1].life=31;
 state['queue-options']=[{player:1,permanent:20,index:0,label:'Small #20: Deal 2 damage | 4 rays, pay 30 life'}];queueTargetOptions=[{id:-1,label:'General: High Sage'}];
 const queuePayments=JSON.stringify(state['prismatic-state']);const queueActivations=requests.filter(q=>q.includes('/activate?')).length;
 await send('Page.reload');await wait('!!document.getElementById("gqa-0")');await click('gqa-0');await wait('!!document.getElementById("gmt-0")');
 assert('queue picker works outside a paused own main phase',!state.paused&&requests.some(q=>q.endsWith('/queue-targets?player=1&permanent=20&index=0')));
 rejectQueuedOrder=true;await click('gmt-0');await wait('document.body.innerText.includes("invalid queued target")');
 assert('rejected queued target keeps the picker',await evaluate('!!document.getElementById("gmt-0")'));
 await click('gmt-0');await wait('!!document.getElementById("gqc-1")&&!document.getElementById("gmt-0")');
 assert('queue records exact target without activation or payment drafting',state['queued-abilities'][1].target===-1&&requests.some(q=>q.endsWith('/queue-ability?choice=queue&player=1&permanent=20&index=0&target=-1'))&&JSON.stringify(state['prismatic-state'])===queuePayments&&requests.filter(q=>q.includes('/activate?')).length===queueActivations&&await evaluate('document.getElementById("g-payment").children.length===0'));
 state['queued-abilities'][1].reason='Waiting for enough rays';await send('Page.reload');await wait('document.getElementById("gqueue-reason-1")?.innerText==="Waiting for enough rays"');
 assert('retained queued order survives reload with a reason and cancel',await evaluate('!!document.getElementById("gqc-1")&&document.getElementById("gqueue-label-1").innerText.includes("Small #20")'));
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});await send('Page.reload');await wait('!!document.getElementById("gqc-1")');
 const queueShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'queued-ability-narrow.png'),Buffer.from(queueShot.result.data,'base64'));
 assert('queued controls fit a narrow viewport',await evaluate('[...document.querySelectorAll("#g-queue-abilities, #g-queue-abilities *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 state.generals[1].life=30;state['queued-abilities'][1].reason='Would pay last life';await send('Page.reload');await wait('document.getElementById("gqueue-reason-1")?.innerText==="Would pay last life"');
 rejectQueuedCancel=true;await click('gqc-1');await wait('document.body.innerText.includes("queue cancellation refused")');assert('rejected queue cancellation retains acknowledged order',state['queued-abilities'][1].pending&&await evaluate('!!document.getElementById("gqc-1")'));
 await click('gqc-1');await wait('!document.getElementById("gqc-1")');assert('player can cancel retained order',!state['queued-abilities'][1].pending);
 Object.assign(state,{paused:queueRestore.paused,'turn-stage':queueRestore.stage,'phase-label':queueRestore.phase});state.orders[0]['manual-payment']=queueRestore.manual0;state.orders[1]['manual-payment']=queueRestore.manual1;state.generals[1].life=queueRestore.life1;state['queue-options']=[];
 await send('Emulation.setDeviceMetricsOverride',{width:1280,height:900,deviceScaleFactor:1,mobile:false});await send('Page.reload');await wait('!!document.getElementById("gpa-1")');
 state['permanent-abilities']=[];await click('gp0-paymode');await wait('document.getElementById("gp0-paymode").innerText==="Payment: auto"');
 rejectPlay=true;await click('gc-0');await wait('document.body.innerText.includes("card cannot be played at this pause")');
 assert('rejected manual play keeps acknowledged hand',await evaluate('document.getElementById("gc-0").innerText.includes("Small")'));
 await click('gc-0');await wait('document.getElementById("gc-0").innerText.includes("Giant")');
 assert('manual play sends instance ID without resuming phase',requests.some(q=>q.endsWith('/play?card=101'))&&await evaluate('document.getElementById("g-step").innerText==="Continue"'));
 await send('Page.reload');await wait('document.getElementById("gc-0")?.innerText.includes("Giant")');
 assert('reload preserves loyalty use',await evaluate('document.getElementById("g-loyalty-status").innerText==="Used this turn."'));
 state.battlefield.push({id:50,name:'Ruby',controller:0,type:'Gemstone',power:0,toughness:0,tapped:false,'gem-stone':0,'attached-to':-1},{id:60,name:'Diamond',controller:0,type:'Gemstone',power:0,toughness:0,tapped:false,'gem-stone':5,'attached-to':-1},{id:68,name:'Obsidian',controller:0,type:'Gemstone',power:0,toughness:0,tapped:false,'gem-stone':6,'attached-to':-1});
 await click('gp0-paymode');await wait('document.getElementById("gp0-paymode").innerText==="Payment: manual"');
 await click('gkeep0-6');await wait('document.getElementById("gkeep0-6").innerText==="AI Obsidian: keep"');
 rejectFluor=true;await click('gfluor-68');await wait('document.body.innerText.includes("cannot activate Fluorescence")');
 assert('rejected Fluorescence retains activation choice',await evaluate('!!document.getElementById("gfluor-68")'));
 await click('gfluor-68');await wait('document.getElementById("g-status").innerText.includes("Fluorescence active")');
 assert('Obsidian activation reaches API once',requests.filter(q=>q.endsWith('/fluorescence?stone=68')).length===2&&await evaluate('!document.getElementById("gfluor-68")'));
 await click('gc-0');await wait('!!document.getElementById("g-pay-submit")');
 assert('Obsidian is excluded from payment chains',await evaluate('!document.getElementById("gstone-4")'));
 assert('manual payment shows required light cost',await evaluate('document.getElementById("g-pay-title").innerText.includes("Giant {2}")'));
 await click('gstone-2');await click('gstone-3');await click('g-pay-ray');await click('g-pay-ray');
 await wait('document.getElementById("g-pay-closed").innerText.includes("Ray 2")');
 assert('manual payment builds gemstone and White rays',await evaluate('document.getElementById("g-pay-closed").innerText.includes("Ray 1: Ruby + Diamond; Ray 2: White")'));
 const draftShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'manual-payment.png'),Buffer.from(draftShot.result.data,'base64'));
 rejectPlay=true;await click('g-pay-submit');await wait('document.body.innerText.includes("invalid gemstone payment")');
 assert('rejected payment retains local draft',await evaluate('document.getElementById("g-pay-closed").innerText.includes("Ruby + Diamond")&&document.getElementById("gc-0").innerText.includes("Giant")'));
 await send('Page.reload');await wait('document.getElementById("gp0-paymode")?.innerText==="Payment: manual"');
 assert('reload keeps mode and discards unsubmitted draft',await evaluate('document.getElementById("g-payment").children.length===0'));
 await click('gc-0');await wait('!!document.getElementById("gstone-3")');await click('gstone-3');await click('g-pay-submit');await wait('document.getElementById("g-payment").children.length===0');
 assert('manual chains send instance ID and stay paused',requests.some(q=>q.endsWith('/play?card=103&chains=60'))&&await evaluate('document.getElementById("g-step").innerText==="Continue"'));
 await click('g-step');await wait('document.getElementById("g-manual-hand").children.length===0');
 assert('Continue clears manual phase choices',true);
 state['active-player']=1;state['prismatic-state'][1]={raysTotal:7,raysUsed:2};
 await click('gp1-stance');await wait('document.getElementById("g-status").innerText.includes("Dawn Commander rays: 2 used of 7")');
 assert('ray counter follows the active player',true);
 assert('commitment choice defaults on',await evaluate('document.getElementById("gp0-commitment").innerText==="All-rays choice: on"'));
 await click('gp0-commitment');await wait('document.getElementById("gp0-commitment").innerText==="All-rays choice: off"');
 assert('commitment toggle reaches API',requests.some(q=>q.endsWith('/commitment-policy?player=0&enabled=0')));
 state.paused=true;state['turn-stage']=2;state['phase-label']='Pre-combat main';state.commitment={nonce:101,card:3,name:'Giant',rays:2,'remaining-ms':15000};
 await send('Page.reload');await wait('!!document.getElementById("g-commit-cast")');
 assert('reload preserves prompt and removes Continue bypass',await evaluate('!document.getElementById("g-step")&&!document.getElementById("g-auto")&&document.getElementById("g-commit-title").innerText.includes("Giant")'));
 assert('commitment choice is visible above the battlefield',await evaluate('document.getElementById("g-commit-buttons").getBoundingClientRect().bottom<innerHeight&&document.getElementById("g-commitment").getBoundingClientRect().bottom<=document.getElementById("g-field").getBoundingClientRect().top'));
 const promptShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'commitment.png'),Buffer.from(promptShot.result.data,'base64'));
 rejectCommitment=true;await click('g-commit-cast');await wait('document.body.innerText.includes("pending card is no longer playable")');
 assert('rejected Cast preserves pending choice',await evaluate('!!document.getElementById("g-commit-hold")'));
 await click('g-commit-cast');await wait('!document.getElementById("g-commit-cast")');
 assert('Cast sends the pending nonce',requests.filter(q=>q.endsWith('/commitment?nonce=101&choice=cast')).length===2);
 state.commitment={nonce:102,card:3,name:'Giant',rays:2,'remaining-ms':15000};
 await send('Page.reload');await wait('!!document.getElementById("g-commit-hold")');
 state.commitment['remaining-ms']=700;
 await send('Page.reload');await wait('!!document.getElementById("g-commit-hold")');
 const noEarly=requests.filter(q=>q.includes('/commitment?nonce=102')).length;
 await sleep(300);assert('timeout does not fire before deadline',requests.filter(q=>q.includes('/commitment?nonce=102')).length===noEarly);
 await evaluate('(()=>{const n=performance.now.bind(performance);Object.defineProperty(performance,"now",{value:()=>n()+1000});return true})()');
 await wait('!document.getElementById("g-commit-hold")');
 assert('timeout resolves to Hold and never Cast',requests.some(q=>q.endsWith('/commitment?nonce=102&choice=hold'))&&!requests.some(q=>q.endsWith('/commitment?nonce=102&choice=cast')));
 assert('reload uses remaining deadline instead of a new fifteen seconds',requests.filter(q=>q.includes('/commitment?nonce=102')).length===1);
 const settled=requests.filter(q=>q.includes('/commitment?nonce=102')).length;await sleep(350);
 assert('settled timeout does not repeat',requests.filter(q=>q.includes('/commitment?nonce=102')).length===settled);
 state['active-player']=0;state.orders[0]['manual-payment']=false;state.orders[0]['spell-policy']=0;
 assert('automatic target choice defaults off',await evaluate('document.getElementById("gp0-target-pause").innerText==="Target choice: off"'));
 rejectTargetPolicy=true;await click('gp0-target-pause');await wait('document.body.innerText.includes("invalid target policy")');
 assert('rejected target policy stays off',await evaluate('document.getElementById("gp0-target-pause").innerText==="Target choice: off"'));
 await click('gp0-target-pause');await wait('document.getElementById("gp0-target-pause").innerText==="Target choice: on"');
 state.commitment={nonce:104,card:3,name:'Bolt',rays:1,'all-rays':false,recommended:20,targets:[{id:-2,label:'Enemy General'},{id:20,label:'Small #20'}],'remaining-ms':15000};
 await send('Page.reload');await wait('!!document.getElementById("gct-1")');
 assert('automatic prompt shows candidates and recommendation',await evaluate('document.getElementById("gct-1").innerText.includes("recommended")&&document.getElementById("g-commit-timeout").innerText.includes("shown recommendation")&&!document.getElementById("g-step")'));
 assert('target policy survives reload',await evaluate('document.getElementById("gp0-target-pause").innerText==="Target choice: on"'));
 const autoTargetShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'automatic-target.png'),Buffer.from(autoTargetShot.result.data,'base64'));
 rejectCommitment=true;await click('gct-0');await wait('document.body.innerText.includes("pending card is no longer playable")');
 assert('rejected target keeps the pending candidate picker',await evaluate('!!document.getElementById("gct-1")'));
 await click('gct-0');await wait('!document.getElementById("gct-1")');
 assert('automatic prompt sends explicit target and nonce',lastDecisionTarget===-2&&requests.filter(q=>q.endsWith('/commitment?nonce=104&choice=cast&target=-2')).length===2);
 state.commitment={nonce:105,card:3,name:'Bolt',rays:1,'all-rays':false,recommended:20,targets:[{id:-2,label:'Enemy General'},{id:20,label:'Small #20'}],'remaining-ms':700};
 await send('Page.reload');await wait('!!document.getElementById("gct-1")');await sleep(100);
 assert('target timeout waits for remaining deadline',!requests.some(q=>q.includes('/commitment?nonce=105')));
 await wait('!document.getElementById("gct-1")');
 assert('target timeout requests the saved recommendation once',lastDecisionTarget===20&&requests.filter(q=>q.endsWith('/commitment?nonce=105&choice=timeout')).length===1);
 lastDecisionTarget=null;
 state.commitment={nonce:106,card:3,name:'Bolt',rays:1,'all-rays':true,recommended:20,targets:[{id:-2,label:'Enemy General'},{id:20,label:'Small #20'}],'remaining-ms':700};
 await send('Page.reload');await wait('!!document.getElementById("gct-1")');
 assert('all-rays target prompt explains Hold timeout',await evaluate('document.getElementById("g-commit-timeout").innerText.includes("After 15 seconds: Hold")'));
 await wait('!document.getElementById("gct-1")');
 assert('all-rays target timeout still Holds',lastDecisionTarget===null&&requests.filter(q=>q.endsWith('/commitment?nonce=106&choice=hold')).length===1);
 state.commitment={nonce:107,ability:true,source:10,index:0,name:'Channeler',description:'Deal 3 damage',cost:'1 ray, tap, sacrifice this permanent, pay 2 life',rays:1,'all-rays':false,recommended:-2,targets:[{id:-2,label:'Enemy General'}],'remaining-ms':15000};
 await send('Page.reload');await wait('document.getElementById("g-commit-cast")?.innerText==="Activate"');
 assert('ability prompt names source effect and sacrifice cost',await evaluate('document.getElementById("g-commit-title").innerText.includes("sacrifice this permanent")&&document.getElementById("gct-0").innerText.includes("Activate at")'));
 await click('gp0-sacrifice-pause');await wait('document.getElementById("gp0-sacrifice-pause")?.innerText==="Never sacrifice automatically"');
 assert('sacrifice policy keeps the pending nonce',state.commitment.nonce===107&&state.orders[0]['sacrifice-pause']===false);
 await click('gp0-sacrifice-pause');await wait('document.getElementById("gp0-sacrifice-pause")?.innerText==="Ask before sacrificing"');
 await click('gct-0');await wait('!document.getElementById("g-commit-cast")');
 assert('ability approval carries the shared nonce and target',requests.some(q=>q.endsWith('/commitment?nonce=107&choice=cast&target=-2')));
 state.commitment={nonce:108,ability:true,source:10,index:0,name:'Channeler',description:'Gain 4 life',cost:'sacrifice this permanent',rays:0,'all-rays':false,recommended:-3,targets:[],'remaining-ms':700};
 await send('Page.reload');await wait('!!document.getElementById("g-commit-cast")');
 await wait('!document.getElementById("g-commit-cast")');
 assert('ability timeout always Holds without all-rays',requests.filter(q=>q.endsWith('/commitment?nonce=108&choice=hold')).length===1&&!requests.some(q=>q.includes('/commitment?nonce=108&choice=cast')));
 state.disruption={nonce:110,card:300,name:'Paid incantation',player:1,'remaining-ms':15000,choices:[{card:401,name:'Strong answer',rays:1,chance:100},{card:402,name:'Weak answer',rays:2,chance:74}]};
 await send('Page.reload');await wait('!!document.getElementById("gdr-1")');
 assert('disruption shows chances after payment and hides Continue',await evaluate('document.getElementById("gdr-1").innerText.includes("74% chance")&&document.getElementById("g-disruption-help").innerText.includes("spell is paid")&&!document.getElementById("g-step")&&!document.getElementById("g-auto")'));
 const disruptionShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'disruption-window.png'),Buffer.from(disruptionShot.result.data,'base64'));
 rejectDisruptionPolicy=true;await click('gp1-disruption-pause');await wait('document.body.innerText.includes("invalid disruption policy")');
 assert('rejected disruption policy preserves acknowledged value',await evaluate('document.getElementById("gp1-disruption-pause").innerText==="Response choice: off"'));
 await click('gp1-disruption-pause');await wait('document.getElementById("gp1-disruption-pause").innerText==="Response choice: on"');
 assert('policy edits keep the decision nonce',state.disruption.nonce===110);
 rejectDisruption=true;await click('gdr-0');await wait('document.body.innerText.includes("choose Pass")');
 assert('rejected disruption keeps Pass available',await evaluate('!!document.getElementById("g-disruption-pass")'));
 await click('gdr-0');await wait('!document.getElementById("gdr-0")');
 assert('disruption sends selected card and reports outcome',lastDisruptionReply===401&&requests.filter(q=>q.endsWith('/disruption?nonce=110&choice=disrupt&card=401')).length===2&&await evaluate('document.getElementById("g-disruption-result").innerText.includes("was disrupted")'));
 state.disruption={nonce:111,card:301,name:'Second spell',player:1,'remaining-ms':700,choices:[{card:402,name:'Weak answer',rays:1,chance:5}]};
 await send('Page.reload');await wait('!!document.getElementById("gdr-0")');await sleep(100);
 assert('disruption timeout waits for remaining deadline',!requests.some(q=>q.includes('/disruption?nonce=111')));
 await wait('!document.getElementById("gdr-0")');
 assert('disruption timeout sends Pass rather than a reply',lastDisruptionReply===-1&&requests.filter(q=>q.endsWith('/disruption?nonce=111&choice=timeout')).length===1);
 state['active-player']=0;state.orders[0]['manual-payment']=false;state.orders[0]['spell-policy']=3;
 state['manual-hand']=[{id:203,name:'Bolt',cost:'{1}',rays:1,ready:true}];
 manualTargetOptions=[{id:-2,label:'Enemy General'},{id:20,label:'Small #20'}];
 await send('Page.reload');await wait('document.getElementById("gc-0")?.innerText.includes("Bolt")');
 const playsBeforeTarget=requests.filter(q=>q.includes('/play?')).length;
 await click('gc-0');await wait('!!document.getElementById("gmt-1")');
 assert('manual target picker shows recommendation before spending',requests.filter(q=>q.includes('/play?')).length===playsBeforeTarget&&await evaluate('document.getElementById("gmt-0").innerText.includes("recommended")'));
 await click('g-target-cancel');await wait('!document.getElementById("gmt-1")');
 assert('cancel target choice preserves hand',state['manual-hand'][0].id===203);
 await click('gc-0');await wait('!!document.getElementById("gmt-1")');
 rejectManualTarget=true;await click('gmt-1');await wait('document.body.innerText.includes("invalid spell target")');
 assert('rejected manual target preserves picker and card',await evaluate('!!document.getElementById("gmt-1")&&!!document.getElementById("gc-0")'));
 const targetShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'manual-target.png'),Buffer.from(targetShot.result.data,'base64'));
 await click('gmt-1');await wait('!document.getElementById("gc-0")');
 assert('manual target sends selected permanent instead of recommendation',requests.filter(q=>q.endsWith('/play?card=203&target=20')).length===2);
 state['manual-hand']=[{id:204,name:'Bolt',cost:'{1}',rays:1,ready:true}];state.orders[0]['manual-payment']=true;
 await send('Page.reload');await wait('!!document.getElementById("gc-0")');
 await click('gc-0');await wait('!!document.getElementById("gmt-0")');
 await click('gmt-0');await wait('!!document.getElementById("g-pay-submit")');
 assert('manual payment displays selected target',await evaluate('document.getElementById("g-pay-title").innerText.includes("Enemy General")'));
 await click('g-pay-ray');await click('g-pay-submit');await wait('!document.getElementById("gc-0")');
 assert('manual chain submission preserves General target',requests.some(q=>q.endsWith('/play?card=204&chains=w&target=-2')));
 manualTargetOptions=[];state.orders[0]['manual-payment']=false;
 state['manual-hand']=[{id:201,'template-id':1,name:'Twin',cost:'{1}',rays:1,ready:true},{id:202,'template-id':1,name:'Twin',cost:'{1}',rays:1,ready:true}];
 state['hand-orders']=state['manual-hand'].map(c=>({...c,'play-order':0}));
 Object.assign(state.battlefield[0],{'card-id':301,owner:0,orderable:true,'play-order':0});
 Object.assign(state.battlefield[1],{'card-id':302,owner:0,orderable:true,'play-order':0});
 await send('Page.reload');await wait('!!document.getElementById("gc-1")');
 await click('gco-h-0-1-201');await wait('document.getElementById("gflag-label-h-201").innerText.includes("Do not play")');
 assert('holding one copy leaves its twin automatic',await evaluate('document.getElementById("gflag-label-h-202").innerText.endsWith("AI: Auto")'));
 rejectCardOrder=true;await click('gco-h-0-2-201');await wait('document.body.innerText.includes("invalid card order")');
 assert('rejected card flag keeps acknowledged state',await evaluate('document.getElementById("gflag-label-h-201").innerText.includes("Do not play")'));
 await click('gco-h-0-2-202');await wait('document.getElementById("gflag-label-h-202").innerText.includes("Play next")');
 await click('gco-b-0-1-302');await wait('document.getElementById("gflag-label-b-302").innerText.includes("Do not play")');
 assert('recast controls send owner and CardId rather than controller and permanent ID',requests.some(q=>q.endsWith('/card-order?player=0&card=302&order=1')));
 await send('Page.reload');await wait('document.getElementById("gflag-label-h-202")?.innerText.includes("Play next")');
 assert('reload preserves per-instance flags',await evaluate('document.getElementById("gflag-label-h-201").innerText.includes("Do not play")&&document.getElementById("gflag-label-b-302").innerText.includes("Do not play")'));
 const flagShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'card-flags.png'),Buffer.from(flagShot.result.data,'base64'));
 state.generals[0].name='Warlord of the Iron Banner';
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});
 await send('Page.reload');await wait('!!document.getElementById("gflags-b-302")');
 const narrowFlags=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'card-flags-narrow.png'),Buffer.from(narrowFlags.result.data,'base64'));
 const flagBounds=await evaluate('["g-wrap","g-field","gf-20","gflags-h-201","gflags-b-302"].map(id=>{const r=document.getElementById(id).getBoundingClientRect();return {id,left:r.left,right:r.right,width:r.width,viewport:innerWidth}})');
 console.log('Flag layout '+JSON.stringify(flagBounds));
 console.log('Flag descendants '+JSON.stringify(await evaluate('[...document.querySelectorAll("#gflags-b-302, #gflags-b-302 *")].map(el=>{const r=el.getBoundingClientRect(),s=getComputedStyle(el);return {id:el.id,classes:el.className,width:r.width,scroll:el.scrollWidth,client:el.clientWidth,display:s.display,whiteSpace:s.whiteSpace,html:el.outerHTML.slice(0,400)}})')));
 assert('card flag controls fit a narrow viewport',flagBounds.slice(-2).every(r=>r.left>=0&&r.right<=r.viewport)&&await evaluate('[...document.querySelectorAll("[id^=gflags-], [id^=gflags-] *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 state.generals[0].name='High Sage';
 await send('Emulation.setDeviceMetricsOverride',{width:1280,height:900,deviceScaleFactor:1,mobile:false});
 await click('gc-1');await wait('!document.getElementById("gc-1")');
 assert('same-template copies stay distinct in manual selection',requests.some(q=>q.endsWith('/play?card=202'))&&state['manual-hand'].length===1&&state['manual-hand'][0].id===201);
 assert('played Next returns to Auto on the battlefield',await evaluate('document.getElementById("gflag-label-b-202").innerText.endsWith("AI: Auto")'));
 await click('gc-0');await wait('!document.getElementById("gc-0")');
 assert('manual play overrides a persistent hold',requests.some(q=>q.endsWith('/play?card=201'))&&await evaluate('document.getElementById("gflag-label-b-201").innerText.includes("Do not play")'));
 assert('battlefield stays above player summary',await evaluate('document.getElementById("g-field").getBoundingClientRect().bottom<=document.getElementById("g-player").getBoundingClientRect().top'));
 const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'game-page.png'),Buffer.from(shot.result.data,'base64'));
 state['game-over']=true;state['game-active']=false;state.winner=0;state.paused=false;state['turn-stage']=8;state['phase-label']='Game over';
 await send('Page.reload');await wait('document.body?.innerText?.includes("Game Over! Winner: High Sage")');
 assert('finished game exposes no advance control',await evaluate('!document.getElementById("g-step")&&!document.getElementById("g-auto")'));
 assert('finished game exposes no card flag controls',await evaluate('document.querySelectorAll("[id^=gco-]").length===0'));
 await click('g-new');await wait('!!document.getElementById("g-general-a")');
 assert('new game returns to General setup',await evaluate('document.body.innerText.includes("Your General:")'));
 await send('Page.reload');await wait('!!document.getElementById("g-general-a")');
 assert('reload after leaving a finished game stays in setup',!state['game-over']&&!state['game-active']);
 Object.assign(state,{'game-active':true,'game-over':false,paused:true,'turn-stage':2,'phase-label':'Pre-combat main',commitment:{nonce:901,card:3,name:'Old plan',rays:1,'all-rays':false,recommended:-2,targets:[{id:-2,label:'Enemy General'}],'remaining-ms':700}});
 await send('Page.reload');await wait('!!document.getElementById("g-commit-cast")');
 await evaluate('state_set_text("g-blocker","20");state_set_text("g-pay-card","201")');
 await click('g-new');await wait('!!document.getElementById("g-general-a")');await sleep(900);
 assert('new setup cancels the spell timer and local drafts',!requests.some(q=>q.includes('/commitment?nonce=901'))&&await evaluate('state_get_text("g-blocker")===""&&state_get_text("g-pay-card")===""'));
 await send('Page.reload');await wait('!!document.getElementById("g-general-a")');
 assert('reload cannot revive the abandoned spell choice',!state['game-active']&&state.commitment===null);
 Object.assign(state,{'game-active':true,'game-over':false,paused:true,'turn-stage':3,'phase-label':'Declare attackers',commitment:null,'combat-choice':{nonce:902,'remaining-ms':2500}});
 await send('Page.reload');await wait('!!document.getElementById("g-combat-title")');
 await click('g-new');await wait('!!document.getElementById("g-general-a")');await sleep(2700);
 assert('new setup cancels the combat timer',!requests.some(q=>q.includes('/combat-choice?nonce=902'))&&!state['game-active']);
 await send('Page.reload');await wait('!!document.getElementById("g-general-a")');
 assert('reload cannot revive the abandoned combat choice',state['combat-choice']===null);
 Object.assign(state,{'game-active':true,paused:true,'turn-stage':2,'phase-label':'Pre-combat main',commitment:{nonce:903,card:3,name:'Retained plan',rays:1,'remaining-ms':15000}});
 await send('Page.reload');await wait('!!document.getElementById("g-commit-cast")');rejectCancel=true;
 await click('g-new');await wait('document.body.innerText.includes("cancel refused")');
 assert('failed cancellation keeps the acknowledged game visible',state['game-active']&&await evaluate('!!document.getElementById("g-commit-cast")&&!document.getElementById("g-general-a")'));
 await click('g-new');await wait('!!document.getElementById("g-general-a")');
 assert('setup waits for server cancellation acknowledgement',requests.filter(q=>q.endsWith('/cancel')).length===5&&!state['game-active']);
 Object.assign(state,{'game-active':true,paused:true,'turn-stage':2,'phase-label':'Pre-combat main',disruption:{nonce:112,card:302,name:'Abandoned spell',player:1,'remaining-ms':15000,choices:[{card:402,name:'Weak answer',rays:1,chance:5}]}});
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});
 await send('Page.reload');await wait('!!document.getElementById("gdr-0")');
 const narrowDisruption=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'disruption-window-narrow.png'),Buffer.from(narrowDisruption.result.data,'base64'));
 assert('disruption controls fit a narrow viewport',await evaluate('[...document.querySelectorAll("#g-disruption, #g-disruption *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 state.disruption['remaining-ms']=700;await send('Page.reload');await wait('!!document.getElementById("gdr-0")');await click('g-new');await wait('!!document.getElementById("g-general-a")');await sleep(900);
 assert('New Game cancels the disruption timer',!requests.some(q=>q.includes('/disruption?nonce=112'))&&state.disruption===null);
 Object.assign(state,{'game-active':true,paused:true,'turn-stage':2,'phase-label':'Pre-combat main','queued-abilities':[{player:0,pending:true,label:'Retained order',reason:'Waiting for enough rays'},{player:1,pending:false}]});
 await send('Page.reload');await wait('!!document.getElementById("gqc-0")');await click('g-new');await wait('!!document.getElementById("g-general-a")');
 assert('new match setup clears queued orders',state['queued-abilities'].every(q=>!q.pending));
 Object.assign(state,{'game-active':true,'game-over':false,'pause-active':false,paused:true,'turn-stage':2,'phase-label':'Pre-combat main',commitment:{nonce:990,card:3,name:'Paused choice',rays:1,'all-rays':true,'remaining-ms':700},disruption:null,'combat-choice':null});
 await send('Page.reload');await wait('!!document.getElementById("g-pause-now")');
 await click('g-pause-now');await wait('!!document.getElementById("g-resume-now")');await sleep(900);
 assert('Pause suppresses the choice timer and hides immediate actions',!requests.some(q=>q.includes('/commitment?nonce=990'))&&await evaluate('!document.getElementById("g-commit-cast")'));
 await send('Page.reload');await wait('!!document.getElementById("g-resume-now")');await sleep(900);
 assert('reload keeps the choice timer suspended',!requests.some(q=>q.includes('/commitment?nonce=990')));
 await click('gp0-stance');await wait('Number(state_get("g-busy"))===0');
 assert('orders remain available during Pause',state['pause-active']);
 state['queue-loyalty-options']=[{player:1,index:2,label:'General: Destroy target | loyalty 9'}];queueTargetOptions=[{id:21,label:'Locked creature'}];
 await send('Page.reload');await wait('!!document.getElementById("gql-0")');await click('gql-0');await wait('!!document.getElementById("gmt-0")');await click('gmt-0');await wait('!!document.getElementById("gqlc-1")');
 assert('General queue picker sends exact player ability and target while paused',state['pause-active']&&requests.some(q=>q.endsWith('/queue-loyalty?choice=queue&player=1&index=2&target=21')));
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});await send('Page.reload');await wait('document.getElementById("gloyaltyq-reason-1")?.innerText==="Waiting for army loyalty 9"');
 assert('General queued reason and cancellation survive reload and fit narrow viewport',await evaluate('[...document.querySelectorAll("#g-queue-loyalty, #g-queue-loyalty *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 const loyaltyShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'queued-loyalty-narrow.png'),Buffer.from(loyaltyShot.result.data,'base64'));
 await click('gqlc-1');await wait('!document.getElementById("gqlc-1")');assert('General queued cancellation remains available during Pause',state['pause-active']&&!state['queued-loyalty'][1].pending);
 state['queue-loyalty-options']=[];await send('Page.reload');await wait('!!document.getElementById("g-resume-now")');
 const pauseShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'pause-now-narrow.png'),Buffer.from(pauseShot.result.data,'base64'));
 await click('g-resume-now');await wait('!!document.getElementById("g-commit-cast")');await sleep(1000);
 assert('Continue restarts the saved choice timer',requests.some(q=>q.includes('/commitment?nonce=990')&&q.includes('choice=hold')));
 Object.assign(state,{commitment:null,paused:false,'turn-stage':0,'phase-label':'Upkeep'});
 await send('Page.reload');await wait('!!document.getElementById("g-step")');
 await click('g-step');await click('g-pause-now');await wait('!!document.getElementById("g-resume-now")');
 assert('Pause queues through an in-flight request without overlap',!pauseOverlapped&&state['pause-active']);
 Object.assign(state,{'pause-active':false,paused:true,'turn-stage':7,'phase-label':'End step',cleanup:{count:2,manual:true,cards:[{id:501,name:'Twin'},{id:502,name:'Twin'},{id:503,name:'A very long cleanup card name that must wrap inside the narrow viewport'}]},commitment:null});
 await send('Page.reload');await wait('!!document.getElementById("gdiscard-502")');
 assert('cleanup card choices fit a narrow viewport',await evaluate('[...document.querySelectorAll("#g-cleanup, #g-cleanup *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 const cleanupShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'cleanup-narrow.png'),Buffer.from(cleanupShot.result.data,'base64'));
 await click('gdiscard-502');await wait('document.getElementById("g-cleanup-title").innerText.includes("Discard 1")');
 assert('manual cleanup selects one exact duplicate copy',state.cleanup.cards.some(c=>c.id===501)&&!state.cleanup.cards.some(c=>c.id===502));
 await send('Page.reload');await wait('!!document.getElementById("gdiscard-501")');
 await click('gdiscard-503');await wait('!document.getElementById("g-cleanup-title")');
 assert('manual cleanup survives reload and clears at the limit',state.cleanup===null&&requests.some(q=>q.endsWith('/discard?card=503')));
 Object.assign(state,{'turn-stage':6,'phase-label':'Post-combat main',paused:true,'pause-active':false,'budget-stop':'Automatic action budget stopped at 500; last action: Kind word','manual-hand':[{id:701,name:'Kind word',cost:'0',rays:0,gem:false,ready:true}]});state.orders[0]['spell-policy']=0;manualTargetOptions=[];
 await send('Page.reload');await wait('document.body&&document.body.innerText.includes("Automatic action budget stopped at 500")');
 assert('budget stop offers manual cards without changing spell policy',await evaluate('!!document.getElementById("gc-0")&&!!document.getElementById("g-step")'));
 await click('gc-0');await wait('Number(state_get("g-busy"))===0');
 assert('manual play remains available at a budget stop',requests.some(q=>q.includes('/play?card=701')));
 Object.assign(state,{'pause-active':true,'budget-stop':'','queue-card-options':[{player:0,card:901,label:'A long queued card name with an exact saved target and payment waiting reason'}]});manualTargetOptions=[{id:20,label:'First creature'},{id:21,label:'Second creature'}];
 await send('Page.reload');await wait('!!document.getElementById("gqf-0")');await click('gqf-0');await wait('!!document.getElementById("gmt-1")');await click('gmt-1');await wait('!!document.getElementById("gqfc-0")');
 assert('queued card shares the target picker while Pause now allows orders',state['queued-cards'][0].card===901&&state['queued-cards'][0].target===21&&state['pause-active']);
 await send('Page.reload');await wait('!!document.getElementById("gqfc-0")');
 assert('queued card label and controls fit a narrow viewport',await evaluate('[...document.querySelectorAll("#g-queued-cards, #g-queued-cards *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 const queueCardShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'queued-card-narrow.png'),Buffer.from(queueCardShot.result.data,'base64'));
 await click('gqfc-0');await wait('!document.getElementById("gqfc-0")');
 assert('queued card cancellation preserves hand options',!state['queued-cards'][0].pending&&state['queue-card-options'][0].card===901);
 Object.assign(state,{'pause-active':false,'budget-stop':'',paused:true,'turn-stage':2,'phase-label':'Pre-combat main',disruption:{nonce:131,card:301,name:'Held spell',player:1,'remaining-ms':15000,choices:[{card:601,name:'Quick answer',rays:1,chance:0,kind:'cantrip'},{card:602,name:'Counter answer',rays:1,chance:75,kind:'disruption'}]}});manualTargetOptions=[{id:-1,label:'Caster General'}];cantripFizzle=true;
 await send('Page.reload');await wait('!!document.getElementById("gdr-0")');
 assert('Cantrip response label distinguishes casting from a contest',await evaluate('document.getElementById("gdr-0").innerText.includes("Cast Quick answer")&&!document.getElementById("gdr-0").innerText.includes("% chance")'));
 await click('gdr-0');await wait('!!document.getElementById("gmt-0")');await click('gmt-0');await wait('!document.getElementById("gdr-0")');
 assert('Cantrip response uses the shared target picker and one response',lastCantripTarget===-1&&lastDisruptionReply===601&&requests.filter(q=>q.includes('/disruption?')&&q.includes('nonce=131')).length===1);
 assert('paid Cantrip fizzle is distinct from the original spell result',await evaluate('document.body.innerText.includes("Held spell resolved after the Cantrip response fizzled")'));
 Object.assign(state,{disruption:{nonce:132,card:301,name:'Expiring spell',player:1,'remaining-ms':700,choices:[{card:601,name:'Quick answer',rays:1,chance:0,kind:'cantrip'}]}});
 await send('Page.reload');await wait('!!document.getElementById("gdr-0")');await click('gdr-0');await wait('!!document.getElementById("gmt-0")');await sleep(1000);
 assert('response expiry clears its open target picker and passes once',state.disruption===null&&await evaluate('!document.getElementById("g-target-title")')&&requests.filter(q=>q.includes('/disruption?')&&q.includes('nonce=132')).length===1);
 assert('card choices default to Expert',await evaluate('document.getElementById("gp0-skill").innerText==="Card choices: Expert"'));
 rejectSkill=true;await click('gp0-skill');await wait('document.body.innerText.includes("invalid skill level")');
 assert('rejected skill change preserves acknowledged Expert',await evaluate('document.getElementById("gp0-skill").innerText==="Card choices: Expert"'));
 await click('gp0-skill');await wait('document.getElementById("gp0-skill").innerText==="Card choices: Master"');
 await click('gp0-skill');await wait('document.getElementById("gp0-skill").innerText==="Card choices: Beginner"');
 await click('gp0-skill');await wait('document.getElementById("gp0-skill").innerText==="Card choices: Intermediate"');
 await send('Page.reload');await wait('document.getElementById("gp0-skill")?.innerText==="Card choices: Intermediate"');
 assert('skill survives reload and fits a narrow viewport',await evaluate('["gp0-skill","gp1-skill"].every(id=>{const e=document.getElementById(id),r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&e.scrollWidth<=e.clientWidth})'));
 Object.assign(state,{'pause-active':false,paused:true,'turn-stage':2,'phase-label':'Pre-combat main',commitment:{nonce:1990,card:3,name:'Override candidate',rays:1,'all-rays':true,'remaining-ms':15000},disruption:null,'combat-choice':null,'manual-hand':[{id:3,name:'Manual alternative',cost:'W',ready:true}]});
 state.orders[0]['spell-policy']=0;state.orders[0]['manual-payment']=false;
 await send('Page.reload');await wait('document.getElementById("g-commit-override")');
 assert('Override fits narrow commitment controls',await evaluate('(()=>{const e=document.getElementById("g-commit-override"),r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth})()'));
 await click('g-commit-override');await wait('document.getElementById("g-manual-help")?.innerText.includes("Continue resumes automatic play")');
 assert('Override sends nonce and opens manual controls',requests.some(q=>q.endsWith('/override?nonce=1990'))&&await evaluate('!document.getElementById("g-commit-cast")&&document.getElementById("g-step").innerText==="Continue"'));
 await send('Page.reload');await wait('document.getElementById("g-manual-help")?.innerText.includes("Continue resumes automatic play")');
 await click('g-step');await wait('document.getElementById("g-step")?.innerText==="Step"');
 assert('Continue clears override',state['manual-override']===false);
 Object.assign(state,{paused:true,'turn-stage':2,'phase-label':'Pre-combat main',disruption:{nonce:2990,card:3,name:'Slow spell',player:1,'remaining-ms':15000,choices:[{card:-1,permanent:30,index:0,name:'Channeler',kind:'ability',rays:1,cost:'1 ray, tap',chance:0}]}});
 manualTargetOptions=[{id:-1,label:'Opposing General'},{id:30,label:'Friendly Channeler'}];
 await send('Page.reload');await wait('document.getElementById("gdr-0")?.innerText.includes("Activate Channeler")');
 assert('ability response cost fits narrow viewport',await evaluate('(()=>{const e=document.getElementById("gdr-0"),r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth})()'));
 await click('gdr-0');await wait('document.getElementById("gmt-0")');await click('gmt-0');
 await wait('document.getElementById("g-disruption-result")?.innerText.includes("after the ability response")');
 assert('ability response picker sends source index nonce and target',requests.some(q=>q.endsWith('/response-ability-targets?nonce=2990&permanent=30&index=0'))&&requests.some(q=>q.endsWith('/response-ability?nonce=2990&permanent=30&index=0&target=-1')));
 assert('ability consumes response UI without another choice',await evaluate('!document.getElementById("gdr-0")&&!document.getElementById("gmt-0")'));
 Object.assign(state,{'pause-active':false,paused:true,'turn-stage':2,disruption:{nonce:3990,card:-1,'pending-kind':'ability',source:10,index:0,name:'Sacrificed Channeler #10: gain life',player:1,'remaining-ms':2500,choices:[{card:8,name:'Quick blast',kind:'cantrip',rays:1,chance:0}]}});
 await send('Page.reload');await wait('document.getElementById("g-disruption-help")?.innerText.includes("Activation costs are paid")');
 assert('activation window names paid ability and refuses Disruption in help',await evaluate('document.getElementById("g-disruption-title").innerText.includes("Sacrificed Channeler #10")&&document.getElementById("g-disruption-help").innerText.includes("Disruption cannot target abilities")'));
 await click('g-pause-now');await wait('!!document.getElementById("g-resume-now")');await sleep(2700);await send('Page.reload');await wait('!!document.getElementById("g-resume-now")');
 assert('activation response timer stays suspended through Pause and reload',state.disruption?.nonce===3990&&!requests.some(q=>q.includes('/disruption?')&&q.includes('nonce=3990')));
 const activationShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'activation-response-paused.png'),Buffer.from(activationShot.result.data,'base64'));
 await click('g-resume-now');await wait('!!document.getElementById("g-disruption-pass")');await sleep(2800);await wait('!document.getElementById("g-disruption-pass")');
 assert('resumed activation timeout sends exactly one Pass request',requests.filter(q=>q.includes('/disruption?')&&q.includes('nonce=3990')&&q.includes('choice=timeout')).length===1);
 Object.assign(state,{disruption:{nonce:3991,card:-1,'pending-kind':'ability',source:10,index:0,name:'Channeler #10: gain life',player:1,'remaining-ms':15000,choices:[{card:8,name:'Quick blast',kind:'cantrip',rays:1,chance:0}]}});
 manualTargetOptions=[{id:-1,label:'Opposing General'}];cantripFizzle=false;
 await send('Page.reload');await wait('!!document.getElementById("gdr-0")');await click('gdr-0');await wait('!!document.getElementById("gmt-0")');await click('gmt-0');await wait('document.getElementById("g-disruption-result")?.innerText.includes("Channeler #10: gain life resolved after the Cantrip response")');
 assert('activation window uses the shared Cantrip picker and consumes one response',requests.some(q=>q.includes('nonce=3991')&&q.includes('choice=cantrip')&&q.includes('target=-1'))&&state.disruption===null);
 for(const [stage,player] of [[3,0],[4,1],[5,0]]){
   const card=500+stage,target=player===0?-2:-1;
   Object.assign(state,{paused:true,'active-player':0,'turn-stage':stage,'phase-label':'Combat freeze','manual-player':player,'combat-cantrip':true,'combat-cantrip-used':false,'manual-hand':[{id:card,name:'Combat Cantrip',cost:'W',rays:1,ready:true,gem:false}],disruption:null,commitment:null,'combat-choice':null});
   state.orders[0]['manual-payment']=false;state.orders[1]['manual-payment']=true;
   state.battlefield=[0,1].map(owner=>({id:600+owner,'card-id':600+owner,'template-id':0,name:'Payment stone',controller:owner,owner,type:'Gemstone','gem-stone':0,tapped:false,'attached-to':-1}));
   manualTargetOptions=[{id:target,label:'Opposing General'}];
   await send('Page.reload');await wait('document.getElementById("g-manual-help")?.innerText.includes("one Cantrip at this combat freeze")');
   await click('gc-0');await wait('!!document.getElementById("gmt-0")');await click('gmt-0');
   if(player===1){await wait('!!document.getElementById("g-pay-submit")');assert('defender payment offers only defender stones',await evaluate('!document.getElementById("gstone-0")&&!!document.getElementById("gstone-1")'));await click('g-pay-ray');await click('g-pay-submit');}
   await wait('document.getElementById("g-manual-help")?.innerText.includes("Cantrip used at this freeze")');
   assert('combat freeze '+stage+' sends its owner through target and payment routes',requests.some(q=>q.endsWith('/targets?card='+card+'&player='+player))&&requests.some(q=>q.startsWith('/api/magic/game/play?card='+card)&&q.includes('target='+target)&&q.endsWith('&player='+player)));
   await send('Page.reload');await wait('document.getElementById("g-manual-help")?.innerText.includes("Cantrip used at this freeze")');assert('combat allowance stays spent after reload '+stage,await evaluate('!document.getElementById("gc-0")'));
 }
 Object.assign(state,{paused:true,'turn-stage':2,'phase-label':'Pre-combat main','combat-cantrip':false,'combat-cantrip-used':false,'loyalty-used':true,disruption:{nonce:4990,card:-1,'pending-kind':'loyalty',source:0,index:0,name:'Ability General: gain 4 life',player:1,'remaining-ms':15000,choices:[{card:8,name:'Quick blast',kind:'cantrip',rays:1,chance:0}]}});
 manualTargetOptions=[{id:-1,label:'Opposing General'}];
 await send('Page.reload');await wait('document.getElementById("g-disruption-help")?.innerText.includes("General activation is committed for this turn")');
 assert('General window distinguishes counted use from resource payment',await evaluate('document.getElementById("g-disruption-help").innerText.includes("Disruption cannot target loyalty abilities")&&!document.getElementById("g-disruption-help").innerText.includes("costs are paid")'));
 assert('General response instructions fit the narrow viewport',await evaluate('[...document.querySelectorAll("#g-disruption, #g-disruption *")].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&el.scrollWidth<=el.clientWidth})'));
 const loyaltyResponseShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(output,'loyalty-response.png'),Buffer.from(loyaltyResponseShot.result.data,'base64'));
 await click('gdr-0');await wait('!!document.getElementById("gmt-0")');await click('gmt-0');await wait('document.getElementById("g-disruption-result")?.innerText.includes("Ability General: gain 4 life resolved after the Cantrip response")');
 assert('General window shares the one-response Cantrip picker',state.disruption===null&&state['loyalty-used']&&requests.some(q=>q.includes('nonce=4990')&&q.includes('choice=cantrip')&&q.includes('target=-1')));
 assert('no browser exceptions',errors.length===0);
 console.log('PASS browser posture controls');
 await send('Browser.close');
}finally{clearTimeout(watchdog);ws?.close();server.close();await stopBrowser();}
