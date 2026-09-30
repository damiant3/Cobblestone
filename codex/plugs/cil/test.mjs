import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,mkdirSync,mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {resolve,join} from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {createServer} from 'node:net';
import assert from 'node:assert/strict';
const repo=resolve(fileURLToPath(new URL('../../..',import.meta.url)));
const out=join(repo,'codex/plugs/cil/build-output/test');mkdirSync(out,{recursive:true});
const moduleBytes64=readFileSync(join(repo,'codex/plugs/cil/build-output/cil-stdio.wasm')).toString('base64');
const runtimeIndex=process.argv.indexOf('--unity-runtime');
const runtimeBytes64=runtimeIndex<0?'':readFileSync(resolve(process.argv[runtimeIndex+1])).toString('base64');
const cases=[
  {name:'refuse-void-field',refuse:true,source:'Chapter: VoidField\n  Box = record { value : Nothing }\n  opening : [Console] Box = Box { value = print-line "test" }\n'},
  {name:'tail',source:'Chapter: Tail\n  swap-loop : Integer, Integer, Integer -> Integer\n  swap-loop (a) (b) (n) = if n == 0 then a * 10 + b else let next = n - 1 in swap-loop b a next\n  opening : Integer = swap-loop 1 2 100001\n'},
  {name:'callbacks',source:'Chapter: Callbacks\n  inc : Integer -> Integer\n  inc (x) = x + 1\n  add : Integer, Integer -> Integer\n  add (a) (b) = a + b\n  three : Integer, Integer, Integer -> Integer\n  three (a) (b) (c) = a * 100 + b * 10 + c\n  apply-one : (Integer -> Integer), Integer -> Integer\n  apply-one (f) (x) = f x\n  apply-two : (Integer, Integer -> Integer), Integer, Integer -> Integer\n  apply-two (f) (a) (b) = f a b\n  apply-three : (Integer, Integer, Integer -> Integer), Integer, Integer, Integer -> Integer\n  apply-three (f) (a) (b) (c) = f a b c\n  choose-fn : Boolean -> (Integer -> Integer)\n  choose-fn (b) = if b then inc else add 10\n  shadow : (Integer -> Integer), Integer -> Integer\n  shadow (inc) (x) = inc x\n  opening : Integer = apply-one inc 1 + apply-two add 2 3 + apply-three three 1 2 3 + apply-one (choose-fn False) 4 + shadow (add 5) 2\n'},
  {name:'storage',source:readFileSync(join(repo,'apps/modbuilder/mods/valheim/LinkedStorage.codex'),'utf8')+'\n  apply-plan : (List StorageSlot, Integer -> StoragePlan), List StorageSlot, Integer -> StoragePlan\n  apply-plan (f) (slots) (amount) = f slots amount\n  opening : Integer =\n   let slots = [StorageSlot { ss-index = 0, ss-quantity = 8, ss-capacity = 10, ss-compatible = True }, StorageSlot { ss-index = 1, ss-quantity = 0, ss-capacity = 10, ss-compatible = True }]\n   in let deposit = apply-plan storage-plan-deposit slots 7\n   in let withdrawal = storage-plan-withdraw slots 5\n   in if storage-plan-current deposit slots 0 & storage-plan-current withdrawal slots 0 then list-length (deposit.sp-moves) + list-length (withdrawal.sp-moves) else -1\n'},
  {name:'scalar',source:'Chapter: Scalar\n  add : Integer, Integer -> Integer\n  add (a) (b) = a + b\n  factorial : Integer -> Integer\n  factorial (n) = if n <= 1 then 1 else n * factorial (n - 1)\n  choose : Integer -> Integer\n  choose (a) = let b = a + 3 in if a < 0 then let b = a - 2 in b else b\n  opening : Integer = add (choose 5) (factorial 5)\n'},
  {name:'real',source:'Chapter: Real\n  less-equal : Real, Real -> Boolean\n  less-equal (a) (b) = a <= b\n  add-real : Real, Real -> Real\n  add-real (a) (b) = a + b\n  opening : Boolean = less-equal (add-real 1.25 2.5) 4.0\n'},
  {name:'literal',source:'Chapter: Literal\n  opening : Text = "hello"\n'},
  {name:'list',source:'Chapter: List\n  sum-list : List Integer, Integer -> Integer\n  sum-list (xs) (i) = if i >= list-length xs then 0 else list-at xs i + sum-list xs (i + 1)\n  make-list : Integer -> List Integer\n  make-list (n) = [n, n + 1] & [n + 2]\n  grow-list : List Integer -> List Integer\n  grow-list (xs) = list-push xs 9\n  set-list : List Integer -> List Integer\n  set-list (xs) = list-set-at xs 1 99\n  append-lists : List Integer, List Integer -> List Integer\n  append-lists (a) (b) = a & b\n  opening : Integer = let exercised = set-list (grow-list (make-list 0)) in if list-length (append-lists exercised exercised) == 8 then sum-list (make-list 10) 0 else -1\n'},
  {name:'record',source:'Chapter: Record\n  Point = record { x : Integer, y : Integer }\n  total : Point -> Integer\n  total (p) = p.x + p.y\n  update-point : Point -> Point\n  update-point (p) = __record-set p "x" (p.x + 5)\n  make-point : Integer, Integer -> Point\n  make-point (a) (b) = Point { y = b, x = a }\n  opening : Integer = total (update-point (make-point 10 20))\n'},
  {name:'refuse-wrapping-negate',refuse:true,source:'Chapter: Wrap\n  negate-byte : Integer between 0 and 255 wrapping -> Integer between 0 and 255 wrapping\n  negate-byte (x) = -x\n  opening : Integer between 0 and 255 wrapping = negate-byte 1\n'},
  {name:'closure',source:'Chapter: Closure\n  apply-fn : (Integer -> Integer), Integer -> Integer\n  apply-fn (f) (x) = f x\n  make : Integer -> (Integer -> Integer)\n  make (x) = \\y -> x + y\n  opening : Integer = apply-fn (make 2) 3\n'}
];
if(runtimeBytes64){
  const features=JSON.parse(readFileSync(join(repo,'apps/modbuilder/mods/valheim/features.json'),'utf8')).features;
  const fixture=process.argv.includes('--unity-fixture');
  const packs=fixture?[{name:'fixture',features:features.filter(f=>['hud','storage'].includes(f.id))}]:[...features.map(f=>({name:f.id,features:[f]})),{name:'all',features}];
  for(const pack of packs){
    const sources=[...new Set(pack.features.flatMap(f=>f.sources))].map(path=>readFileSync(join(repo,'apps/modbuilder/mods/valheim',path),'utf8')).join('\n');
    const entry='\nSection: Engine Entry\n\n  effect Process where\n'+pack.features.map(f=>'    '+f.effect).join('\n')+'\n\nSection: Entry\n\n  opening : [Process] Nothing = act\n'+pack.features.map(f=>'    '+f.start).join('\n')+'\n  end\n';
    cases.push({name:'unity-'+pack.name,unity:true,source:sources+entry});
  }
  const storage=cases.find(c=>c.name===(fixture?'unity-fixture':'unity-storage'));
  cases.push({name:'refuse-adapter-record',unity:true,refuse:true,source:storage.source.replace('ss-capacity : Integer,','ss-capacity : Integer,\n   ss-extra : Integer,')});
  const hud=cases.find(c=>c.name===(fixture?'unity-fixture':'unity-hud'));
  cases.push({name:'refuse-adapter-effect',unity:true,refuse:true,source:hud.source.replace('unity-hud-layout-start : (Integer -> Integer), (Integer, Integer -> Integer), (Integer -> Integer) ->','unity-hud-layout-start : (Integer -> Integer), (Integer, Integer -> Integer), (Integer -> Integer), (Integer -> Integer) ->').replace('unity-hud-layout-start hud-food-slot hud-food-x hud-row-y','unity-hud-layout-start hud-food-slot hud-food-x hud-row-y hud-row-y')});
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const probe=createServer();await new Promise(r=>probe.listen(0,'127.0.0.1',r));const port=probe.address().port;await new Promise(r=>probe.close(r));
const profile=mkdtempSync(join(tmpdir(),'codex-cil-'));
const edge=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--no-first-run','--remote-debugging-port='+port,'--user-data-dir='+profile,'about:blank'],{stdio:'ignore',windowsHide:true});
let ws;
try{
  let target;for(let i=0;i<80&&!target;i++){try{target=(await(await fetch('http://127.0.0.1:'+port+'/json/list')).json()).find(t=>t.type==='page');}catch{}if(!target)await sleep(200);}
  if(!target)throw new Error('Browser did not start');
  ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r));let id=0;const pending=new Map();
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)?.(m);pending.delete(m.id);}});
  const send=(method,params={})=>new Promise(r=>{const n=++id;pending.set(n,r);ws.send(JSON.stringify({id:n,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.result?.exceptionDetails||r.error)throw new Error(JSON.stringify(r));return r.result.result.value;};
  await send('Page.navigate',{url:pathToFileURL(join(repo,'apps/modbuilder/web/workspace.html')).href});
  let ready=false;for(let i=0;i<120&&!ready;i++){ready=await evaluate('window.__prismReady===true');if(!ready)await sleep(250);}assert(ready,'workspace ready');
  if(process.argv.includes('--export-unity')){
    const unity=readFileSync(join(repo,'codex/plugs/unity/build-output/unity-stdio.wasm')).toString('base64');
    await evaluate('window.__CIL_UNITY_MODULE='+JSON.stringify(unity));
    const exported=await evaluate(`(async()=>{
      window.confirm=()=>true;
      const picked=window.__VALHEIM.features,files={};
      for(const feature of picked)for(const path of feature.sources)files[path]=window.__TEMPLATES[feature.id].files[path];
      files[window.__VALHEIM.entry]='Chapter: Valheim Runtime\\n\\nSection: Engine Entry\\n\\n  effect Process where\\n'+picked.map(f=>'    '+f.effect).join('\\n')+'\\n\\nSection: Entry\\n\\n  opening : [Process] Nothing = act\\n'+picked.map(f=>'    '+f.start).join('\\n')+'\\n  end\\n';
      startProject({title:'Runtime source',main:window.__VALHEIM.entry,files});
      const unit=await resolveUnit(assembleUnit());
      if(unit.missing.length)throw new Error(unit.missing.join(', '));
      const compiled=await runW(await moduleBytes('codex-compiler.wasm'),'IR-UNI decks=125\\n'+unit.text);
      const lines=compiled.text.split('\\n'),first=lines.indexOf('IR-BEGIN'),last=lines.indexOf('IR-END');
      if(first<0||last<=first)throw new Error(compiled.text);
      const ir=lines.slice(first+1,last).join('\\n');
      const emitted=await runW(Uint8Array.from(atob(window.__CIL_UNITY_MODULE),c=>c.charCodeAt(0)),plugInput('unity',ir));
      return {ir,source:emitted.text,unit:unit.text};
    })()`);
    writeFileSync(join(out,'unity-all.ir'),exported.ir);writeFileSync(join(out,'unity-all.cs'),exported.source);
    writeFileSync(join(out,'unity-all.codex'),exported.unit);
    assert(exported.source.includes('public static class PrismUnityEntry'),exported.source.slice(0,1000));
    console.log('Exported Unity adapter source and model IR');
  }
  const compile=async function(source,plug,runtime){
    const unit=await resolveUnit({text:source,regions:[]});
    const result=await runW(await moduleBytes('codex-compiler.wasm'),'IR-UNI passes=text-plug\n'+unit.text);
    const lines=result.text.split('\n'),first=lines.indexOf('IR-BEGIN'),last=lines.indexOf('IR-END');
    if(first<0||last<=first)throw new Error(result.text);
    const ir=lines.slice(first+1,last).join('\n');
    const bytes=Uint8Array.from(atob(plug),c=>c.charCodeAt(0));
    const input=runtime?'CIL-UNITY\n'+runtime+'\n'+ir:ir;
    const a=await runW(bytes,input),b=await runW(bytes,input);
    return {ir,output:btoa(Array.from(a.bytes,c=>String.fromCharCode(c)).join('')),diagnostics:a.text,stats:{compilerMs:result.ms,compilerMemoryMiB:result.mb,cilMs:a.ms,cilMemoryMiB:a.mb},deterministic:a.bytes.length===b.bytes.length&&a.bytes.every((v,i)=>v===b.bytes[i])};
  };
  for(const c of cases){
    const r=await evaluate('('+compile.toString()+')('+JSON.stringify(c.source)+','+JSON.stringify(moduleBytes64)+','+JSON.stringify(c.unity?runtimeBytes64:'')+')');
    writeFileSync(join(out,c.name+'.ir'),r.ir);writeFileSync(join(out,c.name+'.log'),r.diagnostics);
    writeFileSync(join(out,c.name+'.stats.json'),JSON.stringify(r.stats,null,2));
    const bytes=Buffer.from(r.output,'base64');
    if(c.refuse){assert(/^REFUSED CIL [^\r\n]+\r?\n$/.test(bytes.toString('utf8')),c.name+' must emit only its refusal diagnostic');}
    else{assert.equal(bytes.subarray(0,2).toString(),'MZ',c.name+': '+r.diagnostics.slice(0,1500));assert(r.deterministic,c.name+' deterministic');writeFileSync(join(out,c.name+'.dll'),bytes);}
    console.log('PASS browser CIL emission '+c.name);
  }
}finally{ws?.close();edge.kill();await sleep(500);if(!profile.startsWith(tmpdir()))throw new Error('Unexpected browser profile');try{rmSync(profile,{recursive:true,force:true});}catch{}}
