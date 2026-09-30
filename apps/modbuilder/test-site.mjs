import { spawn } from 'node:child_process';
import http from 'node:http';
import {createConnection} from 'node:net';
import { readFileSync, writeFileSync, mkdtempSync, rmSync, mkdirSync, copyFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const repo = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const fileMode = process.argv.includes('--file');
const root = resolve(process.argv.slice(2).find(arg => arg !== '--file') || join(repo, 'apps/modbuilder/web'));
const out = join(repo, 'build-output/modbuilder-site'); mkdirSync(out, { recursive: true });
const wait = ms => new Promise(r => setTimeout(r, ms));
const requests = [], errors = [], externalRequests = [];
let failTrial = false, helperOffline = false, edge, ws, server, bridge, profile, standalone, nativeProcess;
function assert(pass, name) { if (!pass) throw new Error(name); console.log('PASS ' + name); }
const listen = server => new Promise(r => server.listen(0, '127.0.0.1', () => r(server.address().port)));
try {
  server = http.createServer((q, r) => {
    const name = new URL(q.url, 'http://local').pathname.slice(1) || 'index.html';
    if (!['index.html','workspace.html','modbuilder.html','prism.html'].includes(name)) { r.writeHead(404); r.end(); return; }
    const source = name === 'prism.html' ? join(repo, 'codex/plugs/wasm/page/prism.html') : join(root, name);
    r.setHeader('Content-Type', 'text/html; charset=utf-8'); r.end(readFileSync(source));
  });
  const port = fileMode ? null : await listen(server);
  if (fileMode) {
    standalone = mkdtempSync(join(tmpdir(), 'modbuilder-downloaded-'));
    copyFileSync(join(root, 'workspace.html'), join(standalone, 'workspace.html'));
  }
  bridge = http.createServer((q, r) => {
    r.setHeader('Access-Control-Allow-Origin', '*'); r.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Bridge-Token');
    if (q.method === 'OPTIONS') { r.end(); return; }
    let body = ''; q.on('data', b => body += b); q.on('end', () => {
      r.setHeader('Content-Type', 'application/json');
      if (q.url === '/health') { if (helperOffline) { q.socket.destroy(); return; } r.end(JSON.stringify({ok:true,tokenOk:q.headers['x-bridge-token']==='test'})); return; }
      const data = JSON.parse(body); requests.push(data);
      const result = {ok: !(failTrial && data.operation === 'test'), operation:data.operation,artifact:'C:/test/winhttp.dll',buildNumber:'site-test',backup:'C:/test/backup',err:'trial refused'};
      r.end(JSON.stringify(result));
    });
  });
  const bridgePort = await listen(bridge);
  const debug = http.createServer(); const debugPort = await listen(debug); await new Promise(r => debug.close(r));
  profile = mkdtempSync(join(tmpdir(), 'modbuilder-site-'));
  edge = spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', ['--headless=new','--no-first-run','--no-default-browser-check',`--remote-debugging-port=${debugPort}`,`--user-data-dir=${profile}`,'about:blank'], {stdio:'ignore',windowsHide:true});
  let target;
  for (let i=0;i<80&&!target;i++) { try { target=(await(await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json()).find(t=>t.type==='page'); } catch {} if (!target) await wait(250); }
  if (!target) throw new Error('Edge debug target unavailable');
  ws = new WebSocket(target.webSocketDebuggerUrl); await new Promise(r=>ws.addEventListener('open',r));
  let next=0; const pending=new Map();
  ws.addEventListener('message', e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)?.(m);pending.delete(m.id);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails);if(m.method==='Network.requestWillBeSent' && /^https?:/.test(m.params.request.url)){const url=new URL(m.params.request.url);if(!['127.0.0.1','localhost'].includes(url.hostname))externalRequests.push(url.href);}});
  const send=(method,params={})=>new Promise(r=>{const id=++next;pending.set(id,r);ws.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const m=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(m.error||m.result?.exceptionDetails)throw new Error(JSON.stringify(m));return m.result.result.value;};
  const until=async expression=>{for(let i=0;i<240;i++){if(await evaluate(expression))return;await wait(250);}throw new Error('Timed out: '+expression);};
  await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable');
  await send('Page.addScriptToEvaluateOnNewDocument',{source:'window.confirm=()=>true;'});
  const open=async path=>{
    const [name, hash] = path.split('#');
    const local = name === 'prism.html' ? join(repo,'codex/plugs/wasm/page/prism.html') : join(name === 'workspace.html' && standalone ? standalone : root,name);
    await send('Page.navigate',{url:fileMode ? pathToFileURL(local).href + (hash ? '#'+hash : '') : `http://127.0.0.1:${port}/${path}`});await wait(750);
  };
  await open('index.html');
  assert(await evaluate(`document.querySelector('h1').textContent.includes('Your world') && Array.from(document.links).some(a=>a.getAttribute('href')==='workspace.html')`),'home links to the project workspace');
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
  const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(out,'home.png'),Buffer.from(shot.result.data,'base64'));
  await open('workspace.html#preset=hud');
  await until(`typeof project!=='undefined' && project.files.length>0 && document.getElementById('mb-install')!==null`);
  if (fileMode) assert(await evaluate(`location.protocol==='file:'`),'downloaded workspace runs directly from disk without companion files');
  assert(await evaluate(`activePath==='ValheimMod.codex' && !location.hash.includes('preset=')`),'preset link initializes once after workspace boot');
  assert(await evaluate(`PAGE_PROFILE.storage==='modbuilder-valheim' && lens.plug==='unity' && project.files.some(f=>f.path==='HudLayout.codex')`),'Valheim profile starts with a real feature project');
  assert(await evaluate(`document.title==='Cobblestone ModBuilder' && document.querySelector('.header h1').textContent==='Cobblestone ModBuilder' && document.querySelector('.mb-game-logo').alt==='Valheim' && document.querySelector('.mb-game-logo').complete && !document.querySelector('.mb-features') && !document.querySelector('.nav') && document.getElementById('lib-panel').hidden`),'page has one brand, embedded Valheim banner and no top feature link or library panel');
  assert(await evaluate(`Object.keys(EMBED).sort().join(',')==='PrismRuntime.dll,cil-stdio.wasm,codex-compiler.wasm,csharp-stdio.wasm,library.img.gz,pe-bytes.wasm,unity-stdio.wasm'`),'compiler, native PE, managed DLL plug, adapter and library are embedded');
  await until(`document.getElementById('mb-forge')?.contentWindow.mbw !== undefined`);
  assert(await evaluate(`(()=>{const d=document.getElementById('mb-forge').contentDocument;return !d.getElementById('mbw-download') && d.getElementById('mbw-source') && d.getElementById('mbw-uninstall') && !d.getElementById('mbw-connection').open;})()`),'source compilation replaces the prebuilt helper download');
  assert(await evaluate('!window.__NATIVE_HELPER.pe && !window.__NATIVE_HELPER.map && !ModBuilderNative.download'),'page carries helper source with no precompiled executable');
  assert(await evaluate("document.getElementById('mb-setup-card').open && !document.getElementById('features').open && document.getElementById('mb-helper-save').disabled"),'setup card starts with source and requires compilation before saving');
  await until(`document.getElementById('mb-connection-status').textContent.includes('Helper not connected')`);
  assert(await evaluate(`!document.getElementById('mb-check-helper')&&!document.getElementById('mb-helper-mode')&&document.getElementById('mb-uninstaller-save').disabled`),'initial absence is detected automatically without a check button or program dropdown');
  await evaluate(`document.getElementById('mb-build-helper').click()`);
  assert(await evaluate(`document.getElementById('mb-native-source').open&&document.getElementById('mb-helper-tab').getAttribute('aria-selected')==='true'&&!!document.querySelector('#mb-helper-source-panel .k-kw')&&document.querySelector('.mb-native-toolbar').firstElementChild.id==='mb-helper-source-zip'&&document.querySelectorAll('.mb-native-build-row').length===2`),'Build the helper opens highlighted tabs with the ZIP action above separate compile/save rows');
  await evaluate(`document.getElementById('mb-uninstaller-tab').click()`);
  assert(await evaluate(`document.getElementById('mb-helper-source-panel').hidden&&!document.getElementById('mb-uninstaller-source-panel').hidden&&document.getElementById('mb-uninstaller-code').value.includes('let uninstall = True')&&!!document.querySelector('#mb-uninstaller-source-panel .k-kw')`),'uninstaller tab exposes its own editable highlighted entry point');
  await evaluate(`document.getElementById('mb-uninstaller-tab').dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}))`);
  assert(await evaluate(`document.activeElement.id==='mb-helper-tab'&&!document.getElementById('mb-helper-source-panel').hidden`),'source tabs support keyboard switching');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  assert(await evaluate(`document.documentElement.scrollWidth<=400`),'helper code tabs and both compile/save rows fit a narrow screen');
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
  if (fileMode) {
    await evaluate(`(()=>{const specialize=ModBuilderWinHost.specialize;ModBuilderWinHost.specialize=(bytes,map,console,options)=>specialize(bytes,map,console,{...options,port:18789});})()`);
    await evaluate("document.getElementById('mb-helper-build').onclick()");
    assert(await evaluate("!document.getElementById('mb-helper-save').disabled && document.getElementById('mb-setup-card').open"),'helper compiles in-page but setup waits for discovery and pairing');
    const native = await evaluate(`(async()=>{const picker=window.showSaveFilePicker,blob=URL.createObjectURL;let blobCalls=0,release,completed=false,helper,uninstaller;const save=document.getElementById('mb-helper-save'),status=document.getElementById('mb-helper-build-status');URL.createObjectURL=(...args)=>{blobCalls++;return blob(...args);};try{window.showSaveFilePicker=async options=>({createWritable:async()=>({write:async data=>{helper=new Uint8Array(data);},close:()=>new Promise(r=>{release=r;}),abort:async()=>{}})});const saving=save.onclick().then(()=>{completed=true;});await new Promise(r=>setTimeout(r,0));const waits=!completed&&save.disabled;release();await saving;const saved=status.textContent.startsWith('Saved.');window.showSaveFilePicker=async()=>{throw new DOMException('Cancelled','AbortError');};await save.onclick();const cancelled=status.textContent.startsWith('Save cancelled.');window.showSaveFilePicker=async()=>({createWritable:async()=>({write:async()=>{},close:async()=>{throw new Error('Security check refused file');},abort:async()=>{}})});await save.onclick();const refused=status.textContent.includes('Security check refused');window.showSaveFilePicker=undefined;await save.onclick();const unsupported=status.textContent.includes('Save As support');const noBlob=blobCalls===0;URL.createObjectURL=blob;document.getElementById('mb-uninstaller-tab').click();const uninstallSave=document.getElementById('mb-uninstaller-save');const independent=!save.disabled&&uninstallSave.disabled;await document.getElementById('mb-uninstaller-build').onclick();window.showSaveFilePicker=async options=>({createWritable:async()=>({write:async data=>{uninstaller=new Uint8Array(data);},close:async()=>{},abort:async()=>{}})});await uninstallSave.onclick();const editor=document.getElementById('mb-helper-code'),source=editor.value;editor.value=source+'\\n';editor.dispatchEvent(new Event('input'));const editInvalidates=save.disabled&&!uninstallSave.disabled;editor.value=source;editor.dispatchEvent(new Event('input'));return {waits,saved,cancelled,refused,unsupported,noBlob,independent,editInvalidates,magic:helper[0]===77&&helper[1]===90,different:uninstaller.some((v,i)=>v!==helper[i]),helper:btoa(Array.from(helper,c=>String.fromCharCode(c)).join('')),uninstaller:btoa(Array.from(uninstaller,c=>String.fromCharCode(c)).join(''))};}finally{window.showSaveFilePicker=picker;URL.createObjectURL=blob;}})()`);
    assert(native.waits&&native.saved&&native.cancelled&&native.refused&&native.unsupported&&native.noBlob,'Save As waits for close and reports cancellation/security failures without blob fallback (picker stand-in)');
    assert(native.independent&&native.editInvalidates&&native.magic&&native.different,'helper and uninstaller compile from source; separate builds survive tab changes and edits invalidate only their own output');
    writeFileSync(join(out,'ModBuilder-browser.exe'),Buffer.from(native.helper,'base64'));
    writeFileSync(join(out,'Uninstall-ModBuilder-browser.exe'),Buffer.from(native.uninstaller,'base64'));
    assert(await evaluate(`(async()=>{const picker=window.showSaveFilePicker,editor=document.getElementById('mb-uninstaller-code'),source=editor.value;let opened=false;try{window.showSaveFilePicker=async()=>{editor.value+='\\n';editor.dispatchEvent(new Event('input'));return {createWritable:async()=>{opened=true;throw new Error('Stale writer');}};};await document.getElementById('mb-uninstaller-save').onclick();return !opened&&document.getElementById('mb-uninstaller-save').disabled&&document.getElementById('mb-uninstaller-build-status').textContent.includes('source changed');}finally{window.showSaveFilePicker=picker;editor.value=source;editor.dispatchEvent(new Event('input'));}})()`),'editing a program during Save As prevents stale EXE writes');
    assert(await evaluate(`(()=>{const zip=makeZip,click=HTMLAnchorElement.prototype.click;let files;try{makeZip=entries=>{files=entries;return zip(entries);};HTMLAnchorElement.prototype.click=()=>{};document.getElementById('mb-helper-source-zip').click();return files.some(f=>f.path==='Edited-Helper.codex'&&f.text===document.getElementById('mb-helper-code').value)&&files.some(f=>f.path==='Edited-Uninstaller.codex'&&f.text===document.getElementById('mb-uninstaller-code').value)&&Object.keys(window.__NATIVE_HELPER.sources).every(path=>files.some(f=>f.path===path&&f.text===window.__NATIVE_HELPER.sources[path]));}finally{makeZip=zip;HTMLAnchorElement.prototype.click=click;}})()`),'source ZIP preserves original files and both independently edited programs');
  }
  await evaluate(`document.getElementById('mb-install').click()`);
  await until(`!document.getElementById('mb-install').disabled`);
  assert(requests.length===0 && await evaluate(`document.getElementById('mb-message').textContent.includes('Helper not connected')`),'unconnected build returns to the helper step before forging');
  const setupShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(out,'setup-wizard.png'),Buffer.from(setupShot.result.data,'base64'));
  await evaluate(`localStorage.setItem('prism.endpoints',JSON.stringify([{name:'existing Prism endpoint',url:'http://127.0.0.1:${bridgePort}',auto:true,token:'test'}]))`);
  await evaluate(`document.getElementById('go').click()`); await until(`!goBtn.disabled`);
  assert(await evaluate(`statusEl.textContent==='ok' && outText().includes('PrismHudLayout.Install')`),'Compile emits the Valheim HUD through the Unity plug');
  writeFileSync(join(out,'hud.cs'),await evaluate(`outText()`));
  assert(await evaluate(`goBtn.textContent==='Generate C#'&&document.getElementById('saveout').textContent==='Save C# source'`),'source export action labels survive compilation');
  const genericHud=await evaluate(`(async()=>{const result=await runW(await moduleBytes('csharp-stdio.wasm'),outIrEl.textContent);return result.text;})()`);
  writeFileSync(join(out,'hud-generic.cs'),genericHud);
  assert(!genericHud.includes('public static class PrismUnityEntry')&&!genericHud.includes('PrismHudLayout.Install'),'generic C# lacks the Valheim entry and adapter installation supplied by the Unity plug');
  assert(await evaluate(`!/_Cce|_Buf|_State|_CxText|_ICodexVariant/.test(outText())`),'health and food output omits unused language runtimes');
  assert(requests.length===0,'browser compile does not inherit automatic Prism endpoint uploads');
  await evaluate(`viewChapter('Foreword','CCE')`);
  assert(await evaluate(`libShown.text && libShown.text.startsWith('Chapter: CCE')`),'library browsing reads the embedded volume');
  await evaluate(`addFile('notes.txt','persistent Valheim notes');persistFile('notes.txt')`);
  await send('Page.reload'); await wait(900); await until(`typeof project!=='undefined' && project.files.some(f=>f.path==='notes.txt')`);
  assert(await evaluate(`fileByPath('notes.txt').text==='persistent Valheim notes' && fileByPath('HudLayout.codex') && fileByPath('ValheimMod.codex')`),'project files survive reload');
  const presets = await evaluate(`window.__VALHEIM.features.map(f=>({id:f.id,marker:f.marker}))`);
  for (const preset of presets) {
    await evaluate(`window.confirm=()=>true;templatesEl.value=${JSON.stringify(preset.id)};templatesEl.dispatchEvent(new Event('change'));goBtn.click()`);
    await until(`!goBtn.disabled`);
    assert(await evaluate(`statusEl.textContent==='ok' && outText().includes(${JSON.stringify(preset.marker+'.Install')})`),'Valheim preset compiles: '+preset.id);
    writeFileSync(join(out,'mod-'+preset.id+'.cs'),await evaluate(`outText()`));
  }
  const controls = [
    { name:'text', source:'Chapter: TextControl\n\n  opening : Text = "hello"\n', helper:'_Cce' },
    { name:'unicode', source:'Chapter: UnicodeControl\n\n  opening : Text = "h\u00e9llo"\n', helper:'_Cce' },
    { name:'heap', source:'Chapter: HeapControl\n\n  opening : Integer = __heap-save\n', helper:'_Buf' },
    { name:'state', source:'Chapter: StateControl\n\n  opening : Integer = run-state 7 get-state\n', helper:'_State' }
  ];
  for (const control of controls) {
    await evaluate(`startProject({title:'Runtime control',main:'Control.codex',files:{'Control.codex':${JSON.stringify(control.source)}}});chooseUnity();goBtn.click()`);
    await until(`!goBtn.disabled`);
    const result=await evaluate(`({status:statusEl.textContent,code:outText(),log:outLogEl.textContent,output:outEl.textContent})`);
    assert(result.status==='ok' && result.code?.includes('class '+control.helper),'required runtime survives: '+control.name+' '+(result.status==='ok'?'':JSON.stringify(result).slice(-1800)));
    writeFileSync(join(out,'control-'+control.name+'.cs'),result.code);
    if (control.name==='unicode' && process.env.MODBUILDER_BASELINE_UNITY) {
      const baseline=readFileSync(process.env.MODBUILDER_BASELINE_UNITY).toString('base64');
      const old=await evaluate(`(async()=>{const result=await runW(b64ToBytes(${JSON.stringify(baseline)}),plugInput('unity',outIrEl.textContent));return result.text;})()`);
      writeFileSync(join(out,'control-unicode-before.cs'),old);
      const opening=code=>code.split('\n').find(line=>line.includes('public static string opening()'));
      assert(opening(old)===opening(result.code),'previous and current Unity modules emit the same text literal');
    }
  }
  await evaluate(`window.confirm=()=>true; startProject(window.__TEMPLATES.hud); openFile('HudLayout.codex'); srcEl.value=srcEl.value.replace('then 110 else 62','then 117 else 62'); srcEl.dispatchEvent(new Event('input'))`);
  await until(`document.getElementById('mb-forge').contentWindow.mba_start !== undefined`);
  if (fileMode) {
    await evaluate(`document.getElementById('features').open=true`);
    assert(await evaluate(`document.getElementById('features').open`),'single-file navigation opens the feature card');
  }
  await evaluate(`(()=>{const w=document.getElementById('mb-forge').contentWindow; const d=w.document;for(const [id,value] of Object.entries({'p-id':'site-test','p-gamePath':'D:/Original','p-testSavePath':'D:/TestSaves','p-installPath':'D:/Play','p-groupLimit':'4','b-url':'http://127.0.0.1:${bridgePort}','b-token':'test'})){d.getElementById(id).value=value;} w.mba_bridge_edit('');})()`);
  await evaluate(`document.getElementById('mb-forge').contentWindow.document.getElementById('p-save').click();persistFile(activePath)`);
  await send('Page.reload'); await wait(900);
  await until(`document.getElementById('mb-forge')?.contentWindow.mba_start !== undefined`);
  assert(await evaluate(`(()=>{const d=document.getElementById('mb-forge').contentDocument;return d.getElementById('p-id').value==='site-test' && d.getElementById('b-token').value==='test';})()`),'game profile and bridge settings survive reload');
  await evaluate(`document.getElementById('mb-forge').contentDocument.getElementById('mbw-check').click()`);
  await until(`!document.getElementById('mb-forge').contentDocument.getElementById('mbw-game').hidden`);
  await evaluate(`document.getElementById('mb-forge').contentDocument.getElementById('mbw-next').click()`);
  assert(await evaluate(`!document.getElementById('mb-forge').contentDocument.getElementById('mbw-build').hidden`),'wizard advances from a connected helper through game review to build');
  const handedProfile=await evaluate(`JSON.parse(document.getElementById('mb-forge').contentWindow.mba_profile_json(0n))`);
  handedProfile.id='launcher-test';
  await open('workspace.html#bridge=test&bridgeUrl='+encodeURIComponent('http://127.0.0.1:'+bridgePort)+'&profile='+encodeURIComponent(JSON.stringify(handedProfile)));
  await until(`document.getElementById('mb-forge')?.contentWindow.mbw !== undefined`);
  const handoff=await evaluate(`(()=>{const d=document.getElementById('mb-forge').contentDocument;return {id:d.getElementById('p-id').value,token:d.getElementById('b-token').value==='test',cleared:!location.hash.includes('bridge='),message:document.getElementById('mb-message').textContent};})()`);
  assert(handoff.id==='launcher-test' && handoff.token && handoff.cleared,'launcher connects the workspace profile and clears its token from the address: '+JSON.stringify(handoff));
  await until(`document.getElementById('mb-connection-status').textContent.includes('older developer bridge')`);
  assert(await evaluate(`document.getElementById('mb-setup-card').querySelector('.mb-step-result').textContent==='Native helper not connected'`),'legacy bridge is identified instead of waiting forever for native discovery');
  helperOffline=true;
  await evaluate(`document.getElementById('mb-install').click()`);
  await until(`!document.getElementById('mb-install').disabled`);
  assert(requests.length===0 && await evaluate(`document.getElementById('mb-message').textContent.includes('No helper connection was found')&&document.getElementById('mb-message').textContent.includes('local-network access')`),'unreachable helper is diagnosed before package requests');
  helperOffline=false;
  await evaluate(`document.getElementById('mb-forge').contentWindow.local_storage_set('prism.bridge.token','wrong');document.getElementById('mb-install').click()`);
  await until(`!document.getElementById('mb-install').disabled`);
  assert(requests.length===0 && await evaluate(`document.getElementById('mb-message').textContent.includes('did not accept')`),'stale helper token stops before package requests');
  await evaluate(`document.getElementById('mb-forge').contentWindow.local_storage_set('prism.bridge.token','test')`);
  await evaluate(`document.getElementById('mb-install').click()`);
  await until(`!document.getElementById('mb-install').disabled && !document.getElementById('mb-forge').contentWindow.mba_busy(0n)`);
  assert(requests.map(r=>r.operation).join(',')==='package,test,install','project handoff builds, tests, then installs');
  assert(requests[0].code.includes('PrismHudLayout.Install')&&requests[0].code.includes('117')&&requests[0].features.join(',')==='hud','installer receives the edited project output and matching features');
  requests.length=0; failTrial=true;
  await evaluate(`document.getElementById('mb-install').click()`);
  await until(`!document.getElementById('mb-install').disabled && !document.getElementById('mb-forge').contentWindow.mba_busy(0n)`);
  assert(requests.map(r=>r.operation).join(',')==='package,test','failed trial blocks installation');
  requests.length=0;
  await evaluate(`srcEl.value=${JSON.stringify('Chapter: Broken\n  opening = no-such-symbol')};document.getElementById('mb-install').click()`);
  await until(`!document.getElementById('mb-install').disabled`);
  assert(requests.length===0&&await evaluate(`document.getElementById('mb-message').textContent.includes('CDX')`),'compiler refusal sends no package to the bridge');
  await evaluate(`window.confirm=()=>true;startProject({title:'C# check',main:'hello.codex',files:{'hello.codex':'Chapter: Hello\\n\\n  opening : [Console] Nothing = act\\n    print-line-uni "Valheim workspace"\\n  end\\n'}});document.querySelector('[data-plug="csharp"]').click();goBtn.click()`);
  await until(`!goBtn.disabled`);
  const csharp = await evaluate(`({status:statusEl.textContent,output:outText(),source:srcEl.value,log:outLogEl.textContent})`);
  assert(csharp.status==='ok' && /class Codex_Program\b/.test(csharp.output) && /static .*opening\(/.test(csharp.output),'C# plug compiles an ordinary Codex project');
  await evaluate(`window.confirm=()=>true;templatesEl.value='hud';templatesEl.dispatchEvent(new Event('change'));document.querySelector('.mb-setup').open=false`);
  const workspace=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});writeFileSync(join(out,'workspace.png'),Buffer.from(workspace.result.data,'base64'));
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  assert(await evaluate(`document.documentElement.scrollWidth<=400`),'workspace fits a narrow screen');
  await open('index.html');assert(await evaluate(`document.documentElement.scrollWidth<=400`),'home fits a narrow screen');
  await open('modbuilder.html#mod=valheim:hud');
  await until(`window.mbw !== undefined`);
  assert(await evaluate(`!document.getElementById('mbw-connect').hidden && document.getElementById('mbw').textContent.includes('Connect helper')`),'standalone feature picker opens with the setup wizard');
  requests.length=0;
  await evaluate(`local_storage_set('prism.bridge.token','');document.getElementById('a-forge').click()`);
  await until(`!mba_busy(0n)`);
  assert(requests.length===0 && await evaluate(`document.getElementById('mbw-helper-status').textContent.includes('Helper not connected')`),'standalone forge also stops at helper setup');
  if(fileMode){
    await new Promise((resolve,reject)=>{const probe=createConnection({host:'127.0.0.1',port:18789});probe.once('connect',()=>{probe.destroy();reject(new Error('Port 18789 is occupied; native browser test refuses to replace an existing helper'));});probe.once('error',e=>e.code==='ECONNREFUSED'?resolve():reject(e));});
    const local=join(out,'native-local');mkdirSync(local,{recursive:true});
    const exe=join(out,'ModBuilder-browser.exe');
    nativeProcess=spawn(exe,['--test'],{env:{...process.env,LOCALAPPDATA:local},windowsHide:true,stdio:['ignore','pipe','pipe']});
    const ready=await new Promise((resolve,reject)=>{let s='';const timer=setTimeout(()=>reject(new Error('Native helper startup timeout')),10000);nativeProcess.stdout.on('data',b=>{s+=b.toString();if(s.endsWith('\n')){clearTimeout(timer);try{resolve(JSON.parse(s));}catch(e){reject(e);}}});nativeProcess.on('error',reject);nativeProcess.on('exit',code=>{clearTimeout(timer);reject(new Error('Native helper exited '+code));});});
    await open('workspace.html#bridge='+ready.token+'&bridgeUrl=http%3A%2F%2F127.0.0.1%3A18789&profile='+encodeURIComponent(JSON.stringify(ready.inventory.profile)));
    await until(`document.getElementById('mb-forge')?.contentDocument.getElementById('mbw-game')?.hidden===false`);
    assert(await evaluate(`(()=>{const d=document.getElementById('mb-forge').contentDocument;return !d.getElementById('mbw-tools')&&d.getElementById('p-gamePath').readOnly&&d.getElementById('p-gamePath').value===${JSON.stringify(ready.inventory.gamePath)}&&!d.getElementById('status').textContent.includes('The profile was refused')&&!location.hash.includes('bridge=');})()`),'actual native helper pairs, displays authenticated discovery and avoids legacy SDK-profile validation');
    assert(await evaluate(`!document.getElementById('mb-setup-card').open && document.getElementById('mb-setup-card').classList.contains('done') && document.getElementById('features').open && !document.getElementById('mb-create-project').disabled`),'successful discovery collapses setup and opens feature selection');
    await evaluate(`window.returnChecks=0;const previousCheck=forgeFrame.contentWindow.mbw.check;forgeFrame.contentWindow.mbw.check=async()=>{try{return await previousCheck();}finally{window.returnChecks++;}};window.dispatchEvent(new Event('focus'))`);
    await until(`window.returnChecks>0`);
    assert(await evaluate(`document.getElementById('mb-setup-card').classList.contains('done')&&!document.getElementById('mb-create-project').disabled`),'returning to the page automatically verifies the real helper without a check button');
    for(const selected of [['rows'],['tools'],['rows','tools']]){
      await evaluate(`window.confirm=()=>true;document.querySelectorAll('#mb-feature-options input').forEach(b=>b.checked=${JSON.stringify(selected)}.includes(b.value));document.getElementById('mb-create-project').click()`);
      assert(await evaluate(`(()=>{const text=fileByPath('ValheimMod.codex').text;return text.includes('unity-plant-rows-start')===${selected.includes('rows')}&&text.includes('unity-tool-kit-start')===${selected.includes('tools')};})()`),'independent feature entry points: '+selected.join('+'));
      await evaluate(`document.getElementById('mb-compile-project').onclick()`);
      assert(await evaluate(`!document.getElementById('mb-save-binary').disabled&&document.getElementById('mb-project-card').classList.contains('done')`),'combined compiler builds '+selected.join('+'));
    }
    await evaluate(`window.confirm=()=>true;document.querySelectorAll('#mb-feature-options input').forEach(b=>b.checked=true);document.querySelector('#mb-feature-options input[value=tools]').dispatchEvent(new Event('change'));document.getElementById('mb-create-project').click()`);
    assert(await evaluate(`document.querySelector('#mb-feature-options input[value=rows]').checked && project.files.filter(f=>f.path==='PlantRows.codex').length===1 && fileByPath('ValheimMod.codex').text.includes('unity-tool-kit-start') && fileByPath('ValheimMod.codex').text.includes('unity-plant-rows-start') && !document.getElementById('features').open && document.getElementById('mb-project-card').open && !document.getElementById('mb-compile-project').disabled`),'shovel and planting rows remain independently selected and compose without duplicate source');
    await evaluate(`document.getElementById('mb-compile-project').click()`);await until(`!document.getElementById('mb-compile-project').disabled`);
    assert(await evaluate(`outText().includes('PrismStorage.Install') && outText().includes('PrismToolKit.Install') && outText().includes('PrismHudLayout.Install') && document.getElementById('mb-project-card').classList.contains('done') && !document.getElementById('mb-project-card').open && document.getElementById('mb-deploy-card').open && !document.getElementById('mb-save-binary').disabled && !document.getElementById('mb-deploy-card').classList.contains('done')`),'integrated project compiles and builds its DLL in one action, then opens deployment');
    await evaluate(`document.querySelectorAll('#mb-feature-options input').forEach(b=>b.checked=b.value==='hud');document.getElementById('mb-create-project').click()`);
    assert(await evaluate(`!document.getElementById('mb-project-card').classList.contains('done') && document.getElementById('mb-save-binary').disabled`),'replacing a compiled project clears all prior success state');
    await evaluate(`goBtn.click()`);await until(`!goBtn.disabled`);
    assert(await evaluate(`!document.getElementById('mb-project-card').classList.contains('done') && document.getElementById('mb-save-binary').disabled`),'editor source preview does not claim a built DLL');
    await evaluate(`deleteFile('HudLayout.codex')`);await wait(100);
    assert(await evaluate(`!document.getElementById('mb-project-card').classList.contains('done')`),'deleting a source file invalidates the compilation receipt');
    await evaluate(`document.getElementById('mb-create-project').click();srcEl.value='Chapter: Broken\\n  opening = no-such-symbol';srcEl.dispatchEvent(new Event('input'));document.getElementById('mb-compile-project').click()`);await until(`!document.getElementById('mb-compile-project').disabled`);
    assert(await evaluate(`document.getElementById('mb-project-card').querySelector('.mb-step-result').textContent==='Compilation failed' && !document.getElementById('mb-project-card').classList.contains('done')`),'failed compilation never retains a previous success receipt');
    await evaluate(`document.getElementById('mb-create-project').click()`);
    await evaluate(`srcEl.dispatchEvent(new Event('input'))`);
    assert(await evaluate(`!document.getElementById('mb-project-card').classList.contains('done') && document.getElementById('mb-save-binary').disabled`),'source edits invalidate completed project compilation');
    await evaluate(`document.getElementById('mb-install').click()`);await until(`!document.getElementById('mb-install').disabled`);
    assert(await evaluate(`document.getElementById('mb-message').textContent.includes('Build mod DLL and Deploy mod')`),'native helper routes the older forge to the managed build and deployment cards');
    await evaluate(`(async()=>{const w=document.getElementById('mb-forge').contentWindow;w.nativeFetch=w.fetch;w.fetch=async(...args)=>{const response=await w.nativeFetch(...args);const body=await response.json();if(body.inventory){body.inventory.found=false;body.inventory.gamePath='';body.inventory.profile.gamePath='';}return new w.Response(JSON.stringify(body));};await w.mbw.check();})()`);
    assert(await evaluate(`(()=>{const d=document.getElementById('mb-forge').contentDocument;return d.querySelector('#mbw-game h3').textContent.includes('not found')&&d.getElementById('mbw-game-status').textContent.includes('Steam');})()`),'missing game is reported immediately with recovery instructions');
    assert(await evaluate(`!document.getElementById('mb-setup-card').classList.contains('done') && document.getElementById('mb-create-project').disabled`),'missing game does not complete the setup card');
    await evaluate(`(async()=>{const w=document.getElementById('mb-forge').contentWindow;w.fetch=w.nativeFetch;await w.mbw.check();})()`);
    const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});writeFileSync(join(out,'native-connected.png'),Buffer.from(shot.result.data,'base64'));
    await new Promise((resolve,reject)=>{const u=spawn(join(out,'Uninstall-ModBuilder-browser.exe'),['--test'],{env:{...process.env,LOCALAPPDATA:local},windowsHide:true,stdio:'ignore'});u.on('error',reject);u.on('exit',code=>code===0?resolve():reject(new Error('Native uninstall exit '+code)));});
    for(let i=0;i<30&&nativeProcess.exitCode===null;i++)await wait(100);
    assert(nativeProcess.exitCode===0,'browser-built GUI executable uninstalls and stops the paired helper');
    nativeProcess=null;
  }
  await open('prism.html');await until(`typeof project!=='undefined' && project.files.length>0`);
  assert(await evaluate(`!PAGE_PROFILE.storage && project.files[0].path==='greeting.codex' && !fileByPath('notes.txt') && plugInput('csharp','ir')==='ir'`),'ordinary Prism keeps its default project, storage and plug protocol');
  assert(errors.length===0,'no uncaught browser exceptions');
  assert(externalRequests.length===0,'no external website or font requests: '+externalRequests.join(', '));
} finally {
  nativeProcess?.kill();ws?.close();edge?.kill();server?.close();bridge?.close();await wait(800);
  if(profile)try{rmSync(profile,{recursive:true,force:true});}catch{}
  if(standalone)rmSync(standalone,{recursive:true,force:true});
}
