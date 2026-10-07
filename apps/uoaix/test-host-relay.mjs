import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {PassThrough} from 'node:stream';
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {AdminClient,ContextTools,McpServer,SpeechProvider,SpeechRelay} from './host-relay.mjs';

const arg=name=>{const at=process.argv.indexOf(name);return at<0?null:process.argv[at+1];};
const url=arg('--url'),input=arg('--input'),out=resolve(arg('--out'));
if(!url||!input)throw Error('url/input/out required');mkdirSync(out,{recursive:true});
const keys=readFileSync(input,'utf8').trim().split(/\r?\n/),checks=[],events=[];
const check=(name,pass)=>{assert.ok(pass,name);checks.push(name);console.log('PASS '+name);};
let child,providerServer;const admin=new AdminClient(url,keys[1],2),context=new ContextTools(admin);
try{
 await admin.connect();
 const before=await context.call('uoaix_npc',{id:1});check('authenticated context reads real guest NPC and inventory',before.data.name==='Mara'&&before.data.bread===10);
 check('persona context reads actual configured budget',(await context.call('uoaix_persona',{id:1})).data.token_budget===128);
 for(const name of ['uoaix_memory','uoaix_family','uoaix_opinions','uoaix_town','uoaix_prices_stock','uoaix_town_culture','uoaix_character'])check(name+' returns bounded named context',Object.hasOwn(await context.call(name,{id:1}),'data'));
 check('registry and events have explicit source scopes',(await context.call('uoaix_registry',{after:0})).data.scope==='layer1-npcs'&&(await context.call('uoaix_recent_events',{id:0,after:0})).data.scope==='unacknowledged-town-events');
 check('unbound culture is explicit rather than invented',(await context.call('uoaix_town_culture',{id:1})).data.available===false);
 await assert.rejects(()=>context.call('world_kick',{id:1}),/unknown-tool/);await assert.rejects(()=>context.call('uoaix_npc',{id:1,command:'report-ack'}),/tool-arguments/);check('context catalogue rejects mutations and extra arguments',true);
 const wrong=new AdminClient(url,keys[0],2);try{await assert.rejects(()=>wrong.connect(),/admin-refused/);}finally{wrong.close();}check('wrong role key cannot fetch context',true);
 const mcp=new McpServer(context);check('MCP refuses tools before initialization',(await mcp.handle({jsonrpc:'2.0',id:1,method:'tools/list'})).error.code===-32000);
 const interrupted=new McpServer({call:async()=>{throw new DOMException('cancelled','AbortError');}});await interrupted.handle({jsonrpc:'2.0',id:1,method:'initialize',params:{protocolVersion:'2025-11-25'}});await interrupted.handle({jsonrpc:'2.0',method:'notifications/initialized'});const interruption=await interrupted.handle({jsonrpc:'2.0',id:2,method:'tools/call',params:{name:'fixture',arguments:{}}});check('MCP cancellation error remains valid text content',interruption.result.isError&&typeof interruption.result.content[0].text==='string');
 const idleInput=new PassThrough(),idleOutput=new PassThrough(),stop=new AbortController();
 const idle=new McpServer(context).stdio(idleInput,idleOutput,stop.signal);stop.abort();await idle;check('MCP cancellation closes input without waiting for EOF',idleInput.destroyed);idleOutput.destroy();
 const blockedInput=new PassThrough(),blockedOutput=new PassThrough({highWaterMark:1}),cancelBlocked=new AbortController();
 const blocked=new McpServer(context).stdio(blockedInput,blockedOutput,cancelBlocked.signal),readable=once(blockedOutput,'readable');blockedInput.write('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}\n');await readable;cancelBlocked.abort();await blocked;check('MCP cancellation also releases output backpressure',blockedInput.destroyed);blockedOutput.destroy();
 let attempts=0,reconnected=0;const restarting=new ContextTools({connect:async()=>{reconnected++;},request:async query=>{assert.equal(query.command,'context-read');if(attempts++===0)throw Object.assign(Error('old epoch'),{code:'admin-refused'});return before;}});
 check('read-only context reconnects and retries once after stale authentication',(await restarting.call('uoaix_npc',{id:1})).data.name==='Mara'&&reconnected===1&&attempts===2);
 child=spawn(process.execPath,['apps/uoaix/host-relay.mjs','mcp','--admin-url',url],{windowsHide:true,env:{...process.env,UOAIX_ADMIN_KEY:keys[2],UOAIX_ADMIN_ROLE:'3'},stdio:['pipe','pipe','pipe']});
 writeFileSync(join(out,'mcp-run.json'),JSON.stringify({pid:child.pid,owner:process.env.CODEX_SESSION_ID,guests:0}));console.log('MCP PID='+child.pid);
 const pending=new Map();let buffer='',stderr='',next=0;
 child.stderr.on('data',chunk=>{stderr+=chunk;});child.stdout.on('data',chunk=>{buffer+=chunk;let end;while((end=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,end);buffer=buffer.slice(end+1);const message=JSON.parse(line),wait=pending.get(message.id);if(wait){clearTimeout(wait.timer);pending.delete(message.id);wait.resolve(message);}}});
 const rpc=(method,params)=>new Promise((resolve,reject)=>{const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('MCP timeout'));},20000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');});
 const initialized=await rpc('initialize',{protocolVersion:'2025-11-25',capabilities:{},clientInfo:{name:'UOAIX proof',version:'1'}});check('stdio MCP negotiates documented protocol',initialized.result.protocolVersion==='2025-11-25');child.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');
 const listed=await rpc('tools/list',{});check('stdio MCP exposes only declared read-only tools',listed.result.tools.length===context.tools.length&&listed.result.tools.every(tool=>tool.annotations.readOnlyHint&&!tool.annotations.destructiveHint));
 const called=await rpc('tools/call',{name:'uoaix_town_culture',arguments:{id:1}});check('stdio MCP reads the shard through authenticated admin',called.result.structuredContent.data.available===false);
 const forbidden=await rpc('tools/call',{name:'report-ack',arguments:{id:1}});check('MCP cannot dispatch arbitrary admin commands',forbidden.result.isError===true);
 const exited=once(child,'exit');child.stdin.end();const [code]=await exited;check('stdio EOF stops the owned host and outputs no secrets',code===0&&!keys.slice(0,4).some(key=>stderr.includes(key)));child=null;
 let mode='good',calls=0,toolOutput=false;const providerKey='fixture-provider-secret';
 providerServer=createServer(async(req,res)=>{
  let text='';for await(const chunk of req){text+=chunk;if(text.length>100000){res.writeHead(413).end();return;}}
  const body=JSON.parse(text);assert.equal(req.headers.authorization,'Bearer '+providerKey);assert.equal(body.model,'fixture-model');assert.equal(body.store,false);assert.ok(body.max_output_tokens<=128);calls++;
  if(mode==='http'){res.writeHead(503).end();return;}
  let output,tokens=10;
  if(mode==='usage')tokens=1000;
  if(mode==='good'&&calls===1)output=[{type:'function_call',name:'uoaix_persona',arguments:'{"id":1}',call_id:'fixture_call'}];
  else {toolOutput=body.input.some(item=>item.type==='function_call_output'&&JSON.parse(item.output).data?.description==='Britain baker');output=[{type:'message',role:'assistant',content:[{type:'output_text',text:mode==='long'?'x'.repeat(257):'The ovens need tending, friend.'}]}];}
  res.writeHead(200,{'Content-Type':'application/json'}).end(JSON.stringify({status:'completed',output,usage:{output_tokens:tokens}}));
 });
 providerServer.listen(0,'127.0.0.1');await once(providerServer,'listening');const providerUrl='http://127.0.0.1:'+providerServer.address().port+'/v1/responses';
 const makeProvider=maxCalls=>new SpeechProvider({url:providerUrl,model:'fixture-model',key:providerKey,maxCalls},context),job=await admin.request({command:'mind-next'});
 mode='usage';calls=0;await assert.rejects(()=>makeProvider(3).speech(job),/provider-usage-or-status/);check('provider usage outside server budget is refused',true);
 mode='long';calls=0;await assert.rejects(()=>makeProvider(3).speech(job),/provider-speech-limit/);check('oversized provider speech is refused',true);
 mode='http';calls=0;await assert.rejects(()=>makeProvider(3).speech(job),/provider-http-503/);check('provider HTTP failure has a bounded diagnostic',true);
 mode='good';calls=0;await assert.rejects(()=>makeProvider(1).speech(job),/provider-budget/);check('host provider call ceiling prevents another billed request',calls===1);
 const fakeReplies=[];let generations=0,failOnce=true;
 const fake={epoch:'fixture',request:async command=>{if(command.command==='mind-next')return job;fakeReplies.push(command);if(failOnce){failOnce=false;throw Error('lost reply');}return {outcome:'accepted',fallback:0};}};
 const cached=new SpeechRelay(fake,{speech:async()=>{generations++;return {speech:'Fixture speech',tokens:3,actor:999,kind:1};}});
 await assert.rejects(()=>cached.tick(),/lost reply/);await cached.tick();check('lost admin reply reuses speech and cannot forge actor or action',generations===1&&fakeReplies.every(reply=>reply.actor===job.context.npc&&reply.kind===0&&reply.target===0&&reply.quantity===0));
 let fallback;
 const unavailable=new SpeechRelay({epoch:'fallback',request:async command=>{if(command.command==='mind-next')return job;fallback=command;return {outcome:'model-unavailable',fallback:1};}},{speech:async()=>{throw Error('fixture unavailable');}});
 await unavailable.tick();check('provider failure returns explicit unavailable fallback',fallback.provider===0&&fallback.kind===0&&fallback.speech==='');
 mode='good';calls=0;const relay=new SpeechRelay(admin,makeProvider(3),event=>events.push(event));await relay.tick();
 check('provider can pull context before returning speech',calls===2&&toolOutput);
 check('real guest accepts speech-only reply and retains inventory',events.some(event=>event.outcome==='accepted'&&event.fallback===0)&&(await context.call('uoaix_npc',{id:1})).data.bread===before.data.bread);
 const again=await relay.tick();check('completed job is not sent to provider twice',again===false&&calls===2);
 await admin.connect();check('host reconnect recovers authenticated sequence',(await context.call('uoaix_npc',{id:1})).data.name==='Mara');
 writeFileSync(join(out,'result.json'),JSON.stringify({passed:true,checks,events,liveProvider:false},null,2));console.log('UOAIX HOST RELAY PASS');
}catch(e){writeFileSync(join(out,'result.json'),JSON.stringify({passed:false,checks,error:String(e),liveProvider:false},null,2));throw e;}
finally{child?.kill();providerServer?.closeAllConnections();providerServer?.close();admin.close();}
