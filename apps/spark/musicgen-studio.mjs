import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, createReadStream, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
if (!process.argv[2] || !process.argv[3]) throw new Error('Usage: node apps/spark/musicgen-studio.mjs oracle.json built-studio.html');
const oracle = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const files = ['tokenizer','text','model','audio'].map(k => oracle.inputs[k]);
const pagePath = process.argv[3];
const cases = oracle.cases.filter(c => c.name === 'music' || c.name === 'sfx');
if (cases.length !== 2 || oracle.sample_rate !== 32000 || oracle.top_k !== 250) throw Error('Reference profile differs');
cases.push({...cases[1],name:'single'});
cases.push({...cases[1],name:'random',frames:8,samples:5120,seed:'-1'});
for (const c of oracle.cases) {
  const bytes = readFileSync(join(dirname(process.argv[2]), c.audio));
  if (createHash('sha256').update(bytes).digest('hex') !== c.audio_sha256 || bytes.length !== c.samples * 4) throw Error('Reference waveform differs');
}
for (const [name,path] of Object.entries(oracle.inputs)) {
  const hash=createHash('sha256');for await (const chunk of createReadStream(path)) hash.update(chunk);
  if(hash.digest('hex')!==oracle.sha256[name])throw Error('Model input hash differs: '+name);
}
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const savedMetadata = new Map();
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const reference = (name,c,bytes,tokens) => {
  ok(name+' tokens exactly match the reference',JSON.stringify(tokens)===JSON.stringify(c.tokens.flat()));
  let samples=null;for(let off=12;off+8<=bytes.length;){const n=bytes.readUInt32LE(off+4);if(off+8+n>bytes.length)throw Error('Truncated WAVE');if(bytes.toString('ascii',off,off+4)==='data')samples=bytes.subarray(off+8,off+8+n);off+=8+n+(n&1)}
  if(!samples||samples.length!==c.samples*4)throw Error('Wave sample count differs');
  const ref=readFileSync(join(dirname(process.argv[2]),c.audio));let max=0;for(let i=0;i<c.samples;i++){const actual=samples.readFloatLE(i*4),expected=ref.readFloatLE(i*4);if(!Number.isFinite(actual))throw Error('Nonfinite sample');max=Math.max(max,Math.abs(actual-expected))}
  ok(name+' saved waveform matches the independent reference',max<=1e-4,'maxAbs='+max);
};
const remember=(k,track,bytes)=>{const directory=join(work,'analysis-project',k==='mus'?'Music':'SoundFX');mkdirSync(directory,{recursive:true});writeFileSync(join(directory,track.filePath.split('/').pop()),bytes);savedMetadata.set(track.filePath,{k,track})};
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'musicgen-studio-'));
  const html = readFileSync(pagePath, 'utf8');

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', '--enable-unsafe-webgpu', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });

  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0, pick = 0; const waiting = new Map(); const errors = [];
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', {files:[files[pick++]], backendNodeId:m.params.backendNodeId});
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog',{enabled:true});
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }); if(r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails)); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  const wait = async expression => {for(let i=0;i<4800;i++){if(errors.length)throw Error(errors.join('\n'));if(await evalIn(expression))return true;if(i%120===0)console.log(await evalIn('document.getElementById("mus-status")?.textContent+" | "+document.getElementById("sfx-status")?.textContent'));await sleep(250);}throw Error('Timed out: '+expression);};
  await wait('typeof _st!=="undefined"&&Number(_st.ready)===1');
  const attackTies=await evalIn(`[-10.3,-20.2,-5.1].map(n=>{const rms=Math.fround(n),edge=Math.fround(rms+3);return smg_sfx_vibe(sa_root(JSON.stringify({rmsDb:rms,envelope:[edge,edge],centroid:1000})))})`);
  ok('SFX attack equality stays soft at original float32 boundaries',attackTies.every(s=>s.startsWith('soft onset')),JSON.stringify(attackTies));
  await evalIn('document.getElementById("musicgen-load").click();1');
  ok('loading without files refuses', await evalIn('document.getElementById("musicgen-status").textContent.includes("all four")'));
  for(let i=0;i<4;i++){await evalIn('document.getElementById("musicgen-pick'+i+'").click();1');if(!await wait('!document.getElementById("musicgen-file'+i+'").textContent.includes("No file")'))throw Error('Picker did not complete');}
  const names=await evalIn('[0,1,2,3].map(i=>document.getElementById("musicgen-file"+i).textContent.trim())');
  ok('all model pickers retain their actual filenames', names.join('|') === files.map(f=>f.split(/[\\/]/).pop()).join('|'), names.join('|'));
  await evalIn('document.getElementById("musicgen-load").click();document.getElementById("musicgen-load").click();1');
  ok('duplicate Load is refused while loading', await evalIn('document.getElementById("musicgen-status").textContent.includes("already loading")'));
  const loaded=await wait('Number(_st["musicgen-ready"])===1||document.getElementById("musicgen-status").textContent.includes("could not load")');
  const state=await evalIn('({ready:Number(_st["musicgen-ready"]||0),status:document.getElementById("musicgen-status").textContent,handles:_gb.filter(Boolean).length})');
  ok('the four real medium-model files open through the UI',loaded&&state.ready===1&&state.handles>0,JSON.stringify(state));
  if (state.ready !== 1) throw Error('Model load failed');
  await evalIn(`(async()=>{const root=await navigator.storage.getDirectory();window.project=await root.getDirectoryHandle('StudioGrade',{create:true});const f=await project.getFileHandle('ArtPrompts.txt',{create:true}),w=await f.createWritable();await w.write('PROMPT 01 - Test');await w.close();window.picks=0;window.showDirectoryPicker=async()=>{picks++;return project};return true})()`);
  await evalIn(`(async()=>{window.existingTrack={id:7,title:'Existing',prompt:'preserve me',filePath:'Music/missing.wav',duration:2,rating:4};const d=await project.getDirectoryHandle('Music',{create:true});const w=await(await d.getFileHandle('.music_catalog.json',{create:true})).createWritable();await w.write(JSON.stringify([existingTrack]));await w.close();return true})()`);
  await evalIn('document.getElementById("open").click();1');
  await wait('Number(_st["audio-done"])===1&&Number(_st.writable)===1');
  await evalIn(`(()=>{window.audioTokens=[];window.seedTypes=[];const generate=musicgen_generate__tb;musicgen_generate__tb=(engine,prompt,settings,progress,ready,refuse)=>{seedTypes.push(typeof settings.mg_seed);return generate(engine,prompt,settings,progress,audio=>{audioTokens.push(audio.mga_codes.map(Number));return ready(audio)},refuse)};return true})()`);
  const settings = async (k,c) => evalIn(`(()=>{for(const [id,v] of Object.entries(${JSON.stringify({prompt:c.prompt,dur:c.frames/50,temp:c.temperature,cfg:c.cfg,seed:c.seed,batch:1})})){const e=document.getElementById(${JSON.stringify(k)}+'-'+id);if(e)e.value=String(v)}return true})()`);
  await settings('mus',cases[0]);
  await evalIn('document.getElementById("mus-dur").value="31";document.getElementById("mus-go").click();1');
  ok('overlong fresh generation refuses before running',await evalIn('Number(_st["audio-busy"]||0)===0&&document.getElementById("mus-status").textContent.includes("30 seconds")'));
  await settings('mus',cases[0]);
  await evalIn(`(()=>{const progress=smg_progress;window.cancelOnce=true;smg_progress=(job,phase,current,total)=>{if(cancelOnce&&phase==='generating'){cancelOnce=false;document.getElementById('mus-cancel').click()}return progress(job,phase,current,total)};document.getElementById('mus-go').click();document.getElementById('open').click();document.getElementById('sfx-go').click();return true})()`);
  ok('generation blocks project changes and competing generation',await evalIn('picks===1&&document.getElementById("sfx-status").textContent.includes("already running")'));
  await wait('Number(_st["audio-busy"])===0');
  ok('Cancel stops generation without creating a catalog entry',await evalIn('document.getElementById("mus-status").textContent.includes("cancelled")&&Number(_st["mus-generated"]||0)===0&&audioTokens.length===0'));
  ok('Cancel preserves disk contents without orphan audio',await evalIn(`(async()=>{const d=await project.getDirectoryHandle('Music');const names=[];for await(const [name] of d.entries())names.push(name);const text=await(await(await d.getFileHandle('.music_catalog.json')).getFile()).text();return names.length===1&&names[0]==='.music_catalog.json'&&text===JSON.stringify([existingTrack])})()`));
  const policyCases=[['ui button click and rain','Combat',1,1,'UI',[1,1]],['A short metallic bell ringing once.','Combat',1,1,'Combat',[1,1]],['A short metallic bell ringing once.','All',1,1,'',[1,1]],['bell toll','Combat',0.1,10,'Musical',[0.2,0.7]],['bell toll','Combat',2,10,'Musical',[1.25,1.9]]];
  const policies=await evalIn(`(()=>{const next=smg_next__tb,generate=musicgen_generate__tb,ids=['prompt','dur','temp','cfg','seed','batch','cat'],saved=Object.fromEntries(ids.map(id=>[id,document.getElementById('sfx-'+id).value])),out=[];try{for(const [prompt,cat,temp,count] of ${JSON.stringify(policyCases)}){let capture;smg_next__tb=(s,e,j)=>{capture={s,e,j};return 0n};for(const [id,value] of Object.entries({prompt,cat,temp,batch:count,dur:1,cfg:3,seed:1234}))document.getElementById('sfx-'+id).value=String(value);document.getElementById('sfx-go').click();if(!capture)throw Error(document.getElementById('sfx-status').textContent);smg_next__tb=next;const temperatures=[];musicgen_generate__tb=(e,p,settings)=>{temperatures.push(Number(settings.mg_temperature));return 0n};for(const index of [0,count-1]){capture.j.smg_index=BigInt(index);smg_next(capture.s,capture.e,capture.j)}out.push({category:capture.j.smg_category,selected:document.getElementById('sfx-cat').value,temperatures});_st['audio-busy']=0n}return out}finally{smg_next__tb=next;musicgen_generate__tb=generate;_st['audio-busy']=0n;for(const [id,value] of Object.entries(saved))document.getElementById('sfx-'+id).value=value}})()`);
  ok('category precedence/fallback and batch clamp boundaries match original policies',policies.length===policyCases.length&&policies.every((p,i)=>p.category===policyCases[i][4]&&p.selected===policyCases[i][1]&&p.temperatures.every((t,j)=>Math.abs(t-policyCases[i][5][j])<1e-9)),JSON.stringify(policies));
  for (const [index,c] of cases.entries()) {
    const k=c.name==='music'?'mus':'sfx',folder=k==='mus'?'Music':'SoundFX',catalog=k==='mus'?'.music_catalog.json':'.sfx_catalog.json';
    await settings(k,c);
    if(c.name==='sfx')await evalIn('document.getElementById("sfx-batch").value="2";document.getElementById("sfx-temp").value="1.15";document.getElementById("sfx-cat").value="Combat";document.getElementById("sfx-cat").dispatchEvent(new Event("input"));1');
    await evalIn(`document.getElementById('${k}-go').click();1`);
    await wait(`Number(_st['audio-busy'])===0`);
    const status=await evalIn(`document.getElementById('${k}-status').textContent`);
    if(!status.startsWith('Saved '))throw Error(status);
    const trackIndex=k==='mus'?1:c.name==='single'?2:c.name==='random'?3:0;
    const saved=await evalIn(`(async()=>{const d=await project.getDirectoryHandle('${folder}');const catalog=JSON.parse(await(await(await d.getFileHandle('${catalog}')).getFile()).text());const track=catalog[${trackIndex}];const f=await(await d.getFileHandle(track.filePath.split('/').pop())).getFile();return {catalog,bytes:Array.from(new Uint8Array(await f.arrayBuffer()))}})()`);
    const track=saved.catalog[trackIndex],bytes=Buffer.from(saved.bytes);
    remember(k,track,bytes);
    const seedOk=c.name==='random'?Number.isInteger(track.seed)&&track.seed>=0&&track.seed<=2147483646:track.seed===Number(c.seed);
    ok(c.name+' catalog records the prompt/settings and relative file',track.prompt===c.prompt&&track.duration===c.frames/50&&seedOk&&track.temperature===c.temperature&&track.cfgCoeff===c.cfg&&track.filePath.startsWith(folder+'/')&&saved.catalog.length===(c.name==='single'?3:c.name==='random'?4:2)&&(k==='mus'||track.category==='Combat'),JSON.stringify(track));
    if(k==='mus')ok('generation preserves the existing catalog record',JSON.stringify(saved.catalog[0])===JSON.stringify(await evalIn('existingTrack'))&&track.id===8);
    else if(c.name==='sfx')ok('two SFX variants save distinct files, seeds and original temperature offsets',saved.catalog[1].id===track.id+1&&saved.catalog[1].seed===track.seed+1&&saved.catalog[1].temperature===1.15&&saved.catalog[1].filePath!==track.filePath&&await evalIn(`project.getDirectoryHandle('SoundFX').then(d=>d.getFileHandle(${JSON.stringify(saved.catalog[1].filePath.split('/').pop())})).then(()=>true)`));
    if(c.name==='random')ok('unmocked random-seed generation uses the Integer ABI',await evalIn(`seedTypes.at(-1)==='bigint'`));
    else reference(c.name,c,bytes,await evalIn(`audioTokens[${c.name==='single'?3:index}]`));
    if(k==='sfx')await evalIn(`document.getElementById('sfx-cat').value=${JSON.stringify(track.category)};document.getElementById('sfx-cat').dispatchEvent(new Event('input'));1`);
    const playback=await evalIn(`(async()=>{const el=document.getElementById('${k}a${trackIndex}');await el.play();for(let i=0;i<40&&el.currentTime===0;i++)await new Promise(r=>setTimeout(r,50));const result={time:el.currentTime,duration:el.duration,paused:el.paused,ended:el.ended};el.pause();return result})()`);
    ok(c.name+' generated track plays from the saved file',playback.time>0&&(!playback.paused||playback.ended)&&Math.abs(playback.duration-c.frames/50)<0.01,JSON.stringify(playback));
  }
  const clamped=await evalIn(`(()=>{const start=smg_start__tb,random=random_int,old=_st['mus-variant'],prompt=document.getElementById('mus-prompt').value,temp=document.getElementById('mus-temp').value;const seen=[];smg_start__tb=()=>0n;try{for(const [temperature,jitter] of [[0.3,0],[1.8,399999]]){random_int=()=>BigInt(jitter);_st['mus-variant']=JSON.stringify({prompt:'clamp',temperature});document.getElementById('mus-regen').click();seen.push(Number(document.getElementById('mus-temp').value))}return seen}finally{smg_start__tb=start;random_int=random;_st['mus-variant']=old;document.getElementById('mus-prompt').value=prompt;document.getElementById('mus-temp').value=temp}})()`);
  ok('variant jitter clamps at the original lower and upper bounds',JSON.stringify(clamped)==='[0.3,1.8]');
  for (const k of ['mus','sfx']) {
    const before=await evalIn(`JSON.parse(_st['${k}-json'])`),old=before.at(-1),folder=k==='mus'?'Music':'SoundFX';
    const count=await evalIn(`Number(_st['${k}-generated'])`);
    await evalIn(`window.savedRandom=random_int;random_int=(lo,hi)=>Number(hi)===399999?${k==='mus'?'0n':'399999n'}:42n;document.getElementById('${k}-dur').value='0.16';document.getElementById('${k}-cfg').value='4';document.getElementById('${k}rate${old.id}-${k==='mus'?1:2}').click();1`);
    await wait(`Number(_st['audio-busy'])===0`);
    await evalIn('random_int=savedRandom;1');
    const after=await evalIn(`JSON.parse(_st['${k}-json'])`),variant=after.at(-1);
    const wantedTemp=Number(Math.max(0.3,Math.min(1.8,old.temperature+(k==='mus'?-0.2:0.199999))).toFixed(6));
    ok(k+' low rating generates one jittered variant with current duration/CFG',await evalIn(`Number(_st['${k}-generated'])`)===count+1&&after.length===before.length&&!after.some(t=>t.id===old.id)&&variant.id>old.id&&variant.prompt===old.prompt&&variant.temperature===wantedTemp&&variant.duration===0.16&&variant.cfgCoeff===4&&variant.seed===42,JSON.stringify(variant));
    const decoded=await evalIn(`(async()=>{const d=await project.getDirectoryHandle('${folder}');const old=await(await d.getFileHandle(${JSON.stringify(old.filePath.split('/').pop())})).getFile();const fresh=await(await d.getFileHandle(${JSON.stringify(variant.filePath.split('/').pop())})).getFile();const bytes=await fresh.arrayBuffer(),ctx=new AudioContext({sampleRate:32000});try{const b=await ctx.decodeAudioData(bytes.slice(0));return {oldBytes:old.size,newBytes:fresh.size,samples:b.length,rate:b.sampleRate,bytes:Array.from(new Uint8Array(bytes))}}finally{await ctx.close()}})()`);
    ok(k+' regeneration retains the old file and writes playable new audio',decoded.oldBytes>0&&decoded.newBytes>0&&decoded.samples===5120&&decoded.rate===32000,JSON.stringify({oldBytes:decoded.oldBytes,newBytes:decoded.newBytes,samples:decoded.samples,rate:decoded.rate}));
    remember(k,variant,Buffer.from(decoded.bytes));
    if(k==='mus')reference('rated music',oracle.cases.find(c=>c.name==='settings'),Buffer.from(decoded.bytes),await evalIn('audioTokens.at(-1)'));
  }
  const beforeFailure=await evalIn(`({catalog:_st['mus-json'],tokens:audioTokens.length,id:JSON.parse(_st['mus-json']).at(-1).id})`);
  await evalIn(`(()=>{const write=file_write_then;file_write_then=(path,data,cb)=>{cb('{"error":"injected catalog refusal"}');return 0n};try{document.getElementById('musrate${beforeFailure.id}-2').click()}finally{file_write_then=write}return true})()`);
  ok('failed catalog removal does not start a replacement',await evalIn(`_st['mus-json']===${JSON.stringify(beforeFailure.catalog)}&&audioTokens.length===${beforeFailure.tokens}&&Number(_st['audio-busy'])===0&&document.getElementById('mus-status').textContent.includes('injected catalog refusal')`));
  for(const k of ['mus','sfx'])for(const track of await evalIn(`JSON.parse(_st['${k}-json']).filter(t=>Number.isInteger(t.seed))`)){const bytes=await evalIn(`(async()=>{const d=await project.getDirectoryHandle('${k==='mus'?'Music':'SoundFX'}'),f=await(await d.getFileHandle(${JSON.stringify(track.filePath.split('/').pop())})).getFile();return Array.from(new Uint8Array(await f.arrayBuffer()))})()`);remember(k,track,Buffer.from(bytes));}
  pwsh('build/spark-analyze-oracle.ps1',['-Project',join(work,'analysis-project'),'-Out',join(work,'analysis.json')]);
  const analysis=JSON.parse(readFileSync(join(work,'analysis.json'),'utf8'));
  for(const {k,track} of savedMetadata.values()){const a=analysis[track.filePath.split('/').pop()];const vibe=k==='mus'?a.vibe:`${a.envelope.length>1&&a.envelope[0]>Math.fround(a.rmsDb+3)?'sharp attack':'soft onset'}, ${a.rmsDb>-10?'loud':a.rmsDb>-20?'moderate':'quiet'}, ${a.centroid>4000?'bright/crispy':a.centroid>2000?'mid-range':'deep/bassy'}`;ok(k+' catalog analysis agrees with the original analyzer',track.vibeTag===vibe&&track.bpm===Math.fround(a.bpm),JSON.stringify({file:track.filePath,actual:track.vibeTag,expected:vibe,bpm:track.bpm,expectedBpm:a.bpm}));}
  await evalIn('document.getElementById("open").click();1');
  await wait('Number(_st["project-picking"])===0&&Number(_st["audio-done"])===1');
  ok('reopening the project restores all generated tracks',await evalIn('document.querySelectorAll("#mus-list audio").length===1&&document.querySelectorAll("#sfx-list audio").length===4'));
  await evalIn('document.getElementById("musicgen-close").click();1');
  const closed=await evalIn('({ready:Number(_st["musicgen-ready"]||0),status:document.getElementById("musicgen-status").textContent,handles:_gb.filter(Boolean).length})');
  ok('Unload closes the engine and releases its handles',closed.ready===0&&closed.handles===0&&closed.status.includes('unloaded'),JSON.stringify(closed));
  await evalIn('document.getElementById("musicgen-close").click();1');
  ok('repeated Unload is safe',await evalIn('Number(_st["musicgen-ready"]||0)===0&&_gb.filter(Boolean).length===0'));
  ok('the page threw nothing',errors.length===0,errors.join(' | '));

} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    execFileSync('pwsh',['-NoProfile','-Command',`$ErrorActionPreference='Stop';function Owned { @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${join(work,'profile').replace(/'/g,"''")}*' }) };foreach($p in (Owned)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue};for($i=0;$i -lt 20;$i++){if(@(Owned).Count -eq 0){exit 0};Start-Sleep -Milliseconds 100};throw 'Owned Edge cleanup did not finish'`],{stdio:'inherit'});
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
