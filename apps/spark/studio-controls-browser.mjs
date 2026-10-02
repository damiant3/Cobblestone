import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { createServer as portServer } from 'node:net';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const artifact = resolve(arg('--html', join(repo, 'build-output/spark-file/controls.html')));
const output = resolve(arg('--out', join(repo, 'build-output/spark-file/controls')));
assert.ok(output.startsWith(join(repo, 'build-output') + '\\'));
mkdirSync(output, { recursive: true });
const html = readFileSync(artifact);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const freePort = () => new Promise(resolve => { const server = portServer(); server.listen(0, '127.0.0.1', () => { const port = server.address().port; server.close(() => resolve(port)); }); });
const profile = mkdtempSync(join(tmpdir(), 'spark-llm-arm-'));
let browser, server, ws, send;
const errors = [], external = [];
try {
  const port = await freePort(), debug = await freePort();
  const origin = `http://127.0.0.1:${port}`;
  server = createServer((request, response) => { response.writeHead(200, { 'Content-Type': 'text/html' }); response.end(html); });
  await new Promise(resolve => server.listen(port, '127.0.0.1', resolve));
  browser = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', `--remote-debugging-port=${debug}`, `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore', windowsHide: true });
  console.log('Owned browser PID=' + browser.pid + ' profile=' + profile);
  let target;
  for (let i = 0; i < 80 && !target; i++) {
    try { target = (await (await fetch(`http://127.0.0.1:${debug}/json/list`)).json()).find(t => t.type === 'page' && t.webSocketDebuggerUrl); } catch {}
    if (!target) await sleep(100);
  }
  assert.ok(target, 'owned browser target');
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.addEventListener('open', resolve); ws.addEventListener('error', reject); });
  let sequence = 0;
  const pending = new Map();
  send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 30000);
    pending.set(id, { resolve, reject, timer }); ws.send(JSON.stringify({ id, method, params }));
  });
  ws.addEventListener('message', message => {
    const value = JSON.parse(message.data);
    if (pending.has(value.id)) {
      const task = pending.get(value.id); pending.delete(value.id); clearTimeout(task.timer);
      if (value.error) task.reject(new Error(JSON.stringify(value.error))); else task.resolve(value.result);
    } else if (value.method === 'Runtime.exceptionThrown') errors.push(value.params.exceptionDetails?.exception?.description || value.params.exceptionDetails?.text);
    else if (value.method === 'Fetch.requestPaused') {
      const request = value.params;
      if (request.request.url.startsWith('file:') || request.request.url.startsWith('data:') || request.request.url.startsWith('blob:')) send('Fetch.continueRequest', { requestId: request.requestId }).catch(() => {});
      else { external.push(request.request.url.slice(0, 300)); send('Fetch.failRequest', { requestId: request.requestId, errorReason: 'BlockedByClient' }).catch(() => {}); }
    }
  });
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
    return result.result.value;
  };
  const until = async expression => { for (let i = 0; i < 200; i++) { if (await evaluate(expression)) return; await sleep(50); } throw new Error('Browser condition timed out: ' + expression); };
  await send('Page.enable'); await send('Runtime.enable');
  await send('Fetch.enable', { patterns: [{ urlPattern: '*', requestStage: 'Request' }] });
  await send('Browser.setDownloadBehavior',{behavior:'deny'});await send('Page.navigate', { url: pathToFileURL(artifact).href });
  await until('typeof _st !== "undefined" && Number(_st.ready) === 1');
  assert.deepEqual(errors, [], 'startup exceptions');
  for(const type of ['mousePressed','mouseReleased'])await send('Input.dispatchMouseEvent',{type,x:20,y:20,button:'left',clickCount:1});
  const result=await evaluate(`(async()=>{
    const checks=[],clicked=new Set(),fields=new Set();
    const el=id=>{const e=document.getElementById(id);if(!e)throw Error('Missing control '+id);return e};
    const check=(name,pass)=>{checks.push({name,pass});if(!pass)throw Error(name)};
    const pause=()=>new Promise(r=>setTimeout(r,80));
    const click=async id=>{const e=el(id);check('enabled '+id,!e.disabled);e.click();clicked.add(id);await pause()};
    const set=(id,value)=>{el(id).value=value;el(id).dispatchEvent(new Event('input',{bubbles:true}));fields.add(id)};
    const wait=async test=>{for(let i=0;i<150;i++){if(test())return;await pause()}throw Error('control completion timed out')};
    const log=()=>el('log').textContent;
    window.showDirectoryPicker=async()=>{throw new DOMException('cancelled','AbortError')};
    const oldClick=HTMLInputElement.prototype.click;
    HTMLInputElement.prototype.click=function(){if(this.type==='file'){queueMicrotask(()=>this.dispatchEvent(new Event('cancel')));return}return oldClick.call(this)};
    await click('open');check('Open gives cancellation outcome',el('status').textContent.includes('unchanged'));
    await click('models-pick');check('model chooser gives cancellation outcome',log().includes('unchanged'));
    await click('go');check('cold Generate gives loading instructions',log().includes('Choose a models folder'));
    await click('cancel');check('idle queue cancellation is explained',log().includes('No image queue'));
    await click('draft');check('Draft gives visible selected quality',log().includes('12 steps')&&el('steps-in').value==='12');
    await click('good');check('Good applies quality',el('steps-in').value==='25'&&Number(state_get('steps'))===25);
    const seed=el('seed').value;await click('reroll');check('reroll produces a valid seed',Number(el('seed').value)>0);
    await click('adv-toggle');check('Advanced opens',getComputedStyle(el('adv')).display!=='none');
    await click('adv-toggle');check('Advanced closes',getComputedStyle(el('adv')).display==='none');await click('adv-toggle');
    await click('lora-none');check('No LoRA is visible',el('lora-selected').textContent.includes('none'));
    await click('lora-inject');check('missing trigger words explained',log().includes('trigger words'));
    for(const id of ['lora-weight','prompt-augment','prompt','negative','seed','runs','steps-in','cfg']){const value=el(id).value;set(id,value);check('editable '+id,el(id).value===value)}
    set('image-size','768x512');check('size reaches request state',Number(state_get('image-width'))===768);set('image-size','512x512');
    set('image-sampler','Euler');check('sampler reaches request state',state_get_text('image-sampler')==='Euler');
    let requests=[];const fetch0=window.fetch;
    window.fetch=async(url)=>{requests.push(String(url));return {text:async()=>JSON.stringify({items:[]})}};
    for(const bad of ['', '   ', 'x'.repeat(121), 'bad'+String.fromCharCode(1)+'input']){
      set('lora-q',bad);const n=requests.length;await click('lora-rated');await click('lora-most');check('invalid LoRA query sends no request: '+JSON.stringify(bad)+' '+JSON.stringify(requests),requests.length===n&&el('lora-results').textContent.length>0);
    }
    set('lora-q','coast & light');await click('lora-rated');await click('lora-most');check('valid search preserves escaped query and both sorts',requests.length===2&&requests.every(x=>x.includes('query=coast%20%26%20light'))&&requests[0].includes('Highest%20Rated')&&requests[1].includes('Most%20Downloaded'));
    window.fetch=async()=>({text:async()=>JSON.stringify({items:[{id:123,name:'Coast style',modelVersions:[{id:456,baseModel:'SDXL 1.0',trainedWords:['coast-style']}]}]})});await click('lora-rated');check('populated search gives model link and trigger words',el('lora-results').textContent.includes('coast-style')&&el('lora-results').querySelector('a')?.href==='https://civitai.com/models/123?modelVersionId=456');
    window.fetch=async()=>{throw Error('fixture offline')};await click('lora-rated');check('network refusal visible and retry released',el('lora-results').textContent.includes('fixture offline')&&Number(state_get('lr-busy'))===0);window.fetch=fetch0;
    for(let i=0;i<4;i++)await click('musicgen-pick'+i);
    await click('musicgen-load');check('missing MusicGen files explained',el('musicgen-status').textContent.includes('Select all four'));
    await click('musicgen-close');check('Unload has visible outcome',el('musicgen-status').textContent.includes('unloaded'));
    for(const k of ['mus','sfx']){
      await click(k+'-go');check(k+' Generate refuses missing prerequisites',el(k+'-status').textContent.includes('refused'));
      await click(k+'-cancel');check(k+' idle cancel explained',el(k+'-status').textContent.includes('No audio'));
    }
    for(const id of ['mus-mood','mus-genre','mus-family','mus-inst','mus-composer','mus-artist','mus-tempo','mus-key','mus-scale','sfx-cat','sfx-preset']){const choice=[...el(id).options].find(o=>o.value);check('choices '+id,!!choice);set(id,choice.value)}
    check('music tags build prompt',el('mus-prompt').value.length>0);check('sound preset fills prompt',el('sfx-prompt').value.length>0);
    for(const id of ['mus-prompt','mus-dur','mus-temp','mus-cfg','mus-seed','sfx-prompt','sfx-dur','sfx-temp','sfx-cfg','sfx-seed','sfx-batch'])set(id,el(id).value);
    el('llm').open=true;set('llm-key','fixture-key');await click('llm-key-save');check('provider key accepted and cleared from field',el('llm-key').value==='');
    set('llm-provider','anthropic');set('llm-action','expand');set('llm-instruction','Use plain language.');el('llm-use-context').click();fields.add('llm-use-context');
    const reply=text=>[{type:'content_block_start',index:0,content_block:{type:'text'}},{type:'content_block_delta',index:0,delta:{type:'text_delta',text}},{type:'content_block_stop',index:0},{type:'message_delta',delta:{stop_reason:'end_turn'}},{type:'message_stop'}].map(e=>'data: '+JSON.stringify(e)+'\\n\\n').join('');
    __sparkLlm.provider.setTransport(async(key,body,push)=>push(reply('A coast at dawn.')));
    await click('llm-run');await wait(()=>!__sparkLlm.busy());check('LLM draft remains preview',el('llm-output').value==='A coast at dawn.');
    await click('llm-apply');check('Apply changes prompt',el('prompt').value==='A coast at dawn.');
    __sparkLlm.provider.setTransport(async(key,body,push,signal)=>new Promise((resolve,reject)=>signal.addEventListener('abort',()=>reject(new DOMException('cancelled','AbortError')),{once:true})));
    await click('llm-run');await click('llm-cancel');await wait(()=>!__sparkLlm.busy());await click('llm-key-clear');
    const directory=name=>{const children=new Map();return {name,kind:'directory',async *entries(){yield*children.entries()},
      async getDirectoryHandle(n,o={}){if(!children.has(n)){if(!o.create)throw new DOMException(n,'NotFoundError');children.set(n,directory(n))}const d=children.get(n);if(d.kind!=='directory')throw new DOMException(n,'TypeMismatchError');return d},
      async getFileHandle(n,o={}){if(!children.has(n)){if(!o.create)throw new DOMException(n,'NotFoundError');let data=new File([],n);children.set(n,{name:n,kind:'file',getFile:async()=>data,createWritable:async()=>{let next=data;return {write:async value=>{next=new File([value],n)},close:async()=>{data=next},abort:async()=>{}}}})}const f=children.get(n);if(f.kind!=='file')throw new DOMException(n,'TypeMismatchError');return f},
      async removeEntry(n){if(!children.delete(n))throw new DOMException(n,'NotFoundError')}
    }};
    const project=directory('Controls');
    const put=async(path,data)=>{const parts=path.split('/');let d=project;for(const p of parts.slice(0,-1))d=await d.getDirectoryHandle(p,{create:true});const w=await(await d.getFileHandle(parts.at(-1),{create:true})).createWritable();await w.write(data);await w.close()};
    await put('ArtPrompts.txt','PROMPT 1 - "Coast"\\nA quiet coast.\\n');
    const canvas=document.createElement('canvas');canvas.width=32;canvas.height=32;canvas.getContext('2d').fillRect(0,0,32,32);await put('Concept/still.png',await new Promise(r=>canvas.toBlob(r)));
    await put('Concept/catalog.json',JSON.stringify([{id:'image1',promptNumber:1,title:'Coast',filePath:'Concept/still.png',promptText:'A quiet coast',seed:7,rating:0,settingsTag:'fixture'}]));
    const wav=new Uint8Array(16044),view=new DataView(wav.buffer),ascii=(at,text)=>{for(let i=0;i<text.length;i++)wav[at+i]=text.charCodeAt(i)};
    ascii(0,'RIFF');view.setUint32(4,wav.length-8,true);ascii(8,'WAVEfmt ');view.setUint32(16,16,true);view.setUint16(20,1,true);view.setUint16(22,1,true);view.setUint32(24,8000,true);view.setUint32(28,16000,true);view.setUint16(32,2,true);view.setUint16(34,16,true);ascii(36,'data');view.setUint32(40,16000,true);
    for(const [dir,file] of [['Music','.music_catalog.json'],['SoundFX','.sfx_catalog.json']]){await put(dir+'/fixture.wav',wav);await put(dir+'/'+file,JSON.stringify(Array.from({length:5},(_,i)=>({id:i+1,title:'Fixture '+i,prompt:'A bell',filePath:dir+'/fixture.wav',duration:1,temperature:0.8,cfgCoeff:4,rating:0,category:'UI',deleted:false}))));}
    window.showDirectoryPicker=async()=>project;await click('open');await wait(()=>Number(state_get('done'))===1&&!!document.getElementById('scene-add'));
    await click('use1');check('project prompt applies',el('prompt').value.includes('quiet coast'));
    const musicOpen=musicgen_open,musicClose=musicgen_close,musicRun=musicgen_generate,musicRunTb=musicgen_generate__tb;let musicRequest;
    musicgen_open=(files,shaders,ready,refuse)=>ready({fixture:true});musicgen_close=()=>true;
    musicgen_generate=musicgen_generate__tb=(engine,prompt,settings,progress,ready,refuse)=>{musicRequest={prompt,settings};return refuse('fixture audio refusal')};
    try{
      HTMLInputElement.prototype.click=function(){if(this.type==='file'){Object.defineProperty(this,'files',{value:[new File(['fixture'],'model.bin')]});queueMicrotask(()=>this.dispatchEvent(new Event('change')));return}return oldClick.call(this)};
      for(let i=0;i<4;i++)await click('musicgen-pick'+i);await click('musicgen-load');check('model load binds audio session',Number(state_get('musicgen-ready'))===1);
      for(const k of ['mus','sfx']){
        set(k+'-prompt','A bell');set(k+'-dur','1.5');set(k+'-temp','0.8');set(k+'-cfg','4');set(k+'-seed','23');if(k==='sfx')set('sfx-batch','2');
        await click(k+'-go');check(k+' edited settings reach engine',musicRequest.prompt==='A bell'&&Number(musicRequest.settings.mg_frames)===75&&Number(musicRequest.settings.mg_seed)===23&&Number(musicRequest.settings.mg_guidance)===4&&Math.abs(Number(musicRequest.settings.mg_temperature)-(k==='sfx'?0.65:0.8))<1e-6&&el(k+'-status').textContent==='fixture audio refusal');
      }
      await click('musicgen-close');check('loaded audio unload clears readiness',Number(state_get('musicgen-ready'))===0);
    }finally{musicgen_open=musicOpen;musicgen_close=musicClose;musicgen_generate=musicRun;musicgen_generate__tb=musicRunTb;HTMLInputElement.prototype.click=oldClick;}
    const read=async(dir,name)=>JSON.parse(await(await(await(await project.getDirectoryHandle(dir)).getFileHandle(name)).getFile()).text());
    await click('t0');check('thumbnail opens decoded lightbox',getComputedStyle(el('lb')).display==='flex'&&el('lb-img').naturalWidth===32);
    for(const key of ['ArrowRight','ArrowLeft','5','s']){document.body.dispatchEvent(new KeyboardEvent('keydown',{key,bubbles:true}));await pause()}
    const imageSaved=(await read('Concept','catalog.json'))[0];check('gallery rating and save persist',imageSaved.rating===5&&imageSaved.saved===true);
    await click('lb');check('lightbox click closes',getComputedStyle(el('lb')).display==='none');
    set('sfx-cat','All');
    for(const [k,dir,file] of [['mus','Music','.music_catalog.json'],['sfx','SoundFX','.sfx_catalog.json']]){
      const audio=el(k+'a0');audio.muted=true;await audio.play();check(k+' media plays',!audio.paused);audio.pause();audio.currentTime=0.25;check(k+' media seek',Math.abs(audio.currentTime-0.25)<0.01);
      await click(k+'an0');await wait(()=>!!document.getElementById(k+'view0pos'));
      set(k+'view0pos','500');check(k+' waveform seek updates audio',Math.abs(audio.currentTime-0.5)<0.02);
      for(let id=1;id<=5;id++){const rating=6-id;await click(k+'rate'+id+'-'+rating);await wait(()=>Number(state_get('audio-busy'))===0)}
      const tracks=await read(dir,file);check(k+' ratings and low-rating regeneration refusal',tracks.find(t=>t.id===1)?.rating===5&&!tracks.some(t=>t.id===4||t.id===5)&&el(k+'-status').textContent.includes('Load MusicGen'));
      await click(k+'del3');await wait(()=>Number(state_get('audio-busy'))===0);check(k+' delete persists',!(await read(dir,file)).some(t=>t.id===3));
    }
    await click('video-play');check('empty video playback explained',el('video-status').textContent.includes('Add a still'));
    set('video-image','Concept/still.png');set('video-duration','24');set('video-fade','0');set('video-prompt','A quiet coast');set('video-soundtrack','');
    await click('video-add');await wait(()=>el('video-status').textContent.includes('Composition ready'));
    await click('video-up0');check('first clip boundary explained',el('video-status').textContent.includes('already first'));
    await click('video-play');await click('video-pause');check('Pause visible',el('video-status').textContent==='Paused.');
    set('video-seek','12');check('seek changes frame',Number(state_get('video-frame'))===12);
    await click('video-stop');check('Stop resets frame',Number(state_get('video-frame'))===0);
    await click('video-save');check('composition saved',el('video-status').textContent==='Composition saved.');
    const videoSaved=JSON.parse(await(await(await(await project.getDirectoryHandle('Video')).getFileHandle('timeline.json')).getFile()).text());
    check('video fields persist',videoSaved.prompt==='A quiet coast'&&videoSaved.clips[0].frames===24&&videoSaved.clips[0].fade===0&&videoSaved.clips[0].file==='Concept/still.png');
    await click('video-generate');check('video engine absence explained',el('video-status').textContent.includes('no video model'));
    await click('video-remove0');check('Remove removes clip',el('video-clips').children.length===0);
    set('scene-shape','cube');await click('scene-add');await wait(()=>!!document.getElementById('scene-select0'));await click('scene-select0');
    set('scene-x','1');set('scene-y','0');set('scene-z','0');set('scene-texture','');await click('scene-apply');
    for(const [id,v] of [['scene-yaw','20'],['scene-pitch','10'],['scene-distance','8']])set(id,v);
    await click('scene-save');check('scene saved',el('scene-status').textContent==='Scene saved.');
    const sceneSaved=JSON.parse(await(await(await(await project.getDirectoryHandle('Scene')).getFileHandle('scene.json')).getFile()).text());
    check('camera and object fields persist',sceneSaved.camera.yaw===20&&sceneSaved.camera.pitch===10&&sceneSaved.camera.distance===8&&sceneSaved.objects[0].x===1&&sceneSaved.objects[0].shape==='cube');
    await click('scene-generate');check('3D engine absence explained',el('scene-status').textContent.includes('no 3D model'));
    await click('scene-remove0');check('Remove removes mesh',el('scene-objects').children.length===0);
    const run=spark_image_run,runTb=spark_image_run__tb,close=spark_image_close;let engineCalls=0,request;
    spark_image_run=spark_image_run__tb=(...args)=>{engineCalls++;request=args;return args.at(-1)(0n,'fixture engine refusal')};spark_image_close=()=>true;
    try{
      ss_ready({}, {}, 'fixture checkpoint','sd15 checkpoint');
      state_set_text('project-settings','{"steps":33}');await click('image-model-defaults');check('model defaults clear project settings',state_get_text('project-settings')===''&&state_get_text('project-settings-error')==='');
      set('prompt','Coast');set('negative','fog');set('prompt-augment','blue sky');set('seed','37');set('steps-in','11');set('cfg','6.5');set('image-size','768x512');set('runs','2');
      await click('go');check('loaded Generate reaches engine and reports refusal',engineCalls===1&&log().includes('fixture engine refusal'));
      check('edited image fields reach engine request',request[2]==='Coast, blue sky'&&request[3]==='fog'&&Number(request[4])===37&&Number(request[5])===11&&Number(request[6])===6.5&&Number(request[7])===768&&Number(request[8])===512&&request[9]==='Euler');
      await click('go-all');await wait(()=>Number(state_get('q-busy'))===0);check('Generate all dispatches chosen run count',engineCalls===3);
      await click('image-unload');check('Unload restores actionable Generate',Number(state_get('loaded'))===0&&!!el('go'));
      await click('go');check('unloaded Generate explains prerequisite',log().includes('Choose a models folder'));
      await click('image-model-defaults');check('model defaults need a checkpoint',log().includes('Load a checkpoint before choosing model defaults'));
    }finally{spark_image_run=run;spark_image_run__tb=runTb;spark_image_close=close;}
    await click('new-project');check('New project opens the wizard',el('wizard').open);
    await click('wizard-close');check('wizard closes',!el('wizard').open);
    const shown=e=>getComputedStyle(e).display!=='none'&&!e.closest('dialog:not([open])');
    const missing=[...document.querySelectorAll('button')].filter(e=>!e.disabled&&shown(e)&&!clicked.has(e.id)).map(e=>e.id||e.textContent);
    check('every enabled button in opened fixture clicked'+(missing.length?': '+missing.join(', '):''),missing.length===0);
    const untouched=[...document.querySelectorAll('input,select,textarea')].filter(e=>!e.disabled&&!e.readOnly&&shown(e)&&e.id&&!fields.has(e.id)).map(e=>e.id);
    check('every editable field exercised',untouched.length===0);
    HTMLInputElement.prototype.click=oldClick;
    return {checks,clicked:[...clicked],fields:[...fields],missing,untouched,protocol:location.protocol,engine:'inference refusal stubs; provider transport canned',filesystem:'in-memory File System Access fixture; native dialog permission unproved'};
  })()`);
  assert.equal(result.protocol,'file:');assert.deepEqual(errors,[]);assert.deepEqual(external,[]);
  for(const [name,width] of [['desktop',1280],['narrow',390]]){
    await send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
    await evaluate('window.scrollTo(0,0)');await sleep(100);
    const shot=await send('Page.captureScreenshot',{format:'png'});writeFileSync(join(output,name+'.png'),Buffer.from(shot.data,'base64'));
  }
  writeFileSync(join(output,'evidence.json'),JSON.stringify({sha256:createHash('sha256').update(html).digest('hex'),result,errors,external},null,2));console.log(JSON.stringify(result));
} finally {
  if (send && ws?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} }
  ws?.close();
  if (browser && browser.exitCode === null) { await Promise.race([new Promise(resolve => browser.once('exit', resolve)), sleep(2000)]); if (browser.exitCode === null) browser.kill(); }
  server?.close();
  if (dirname(resolve(profile)) !== resolve(tmpdir()) || !profile.startsWith(join(tmpdir(), 'spark-llm-arm-'))) throw new Error('Profile escaped temporary root');
  rmSync(profile, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}
