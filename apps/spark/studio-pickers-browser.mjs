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
const artifact = resolve(arg('--html', join(repo, 'build-output/spark-file/picker.html')));
const output = resolve(arg('--out', join(repo, 'build-output/spark-file/pickers')));
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
  const result=await evaluate(`(async()=>{
    window.__folderCalls=0;window.__legacyCalls=0;
    const checks=[];const check=(name,pass)=>{checks.push({name,pass});if(!pass)throw new Error(name)};
    const pause=()=>new Promise(r=>setTimeout(r,100));
    const previous={name:'previous'};_gdir=previous;
    state_set('done',1);state_set('audio-done',1);state_set('writable',1);state_set_text('llm-listing','previous-list');
    const preserved=()=>_gdir===previous&&Number(state_get('done'))===1&&Number(state_get('audio-done'))===1&&Number(state_get('writable'))===1&&state_get_text('llm-listing')==='previous-list';
    window.showDirectoryPicker=async()=>{__folderCalls++;throw new DOMException('Fixture cancellation','AbortError')};
    const click=HTMLInputElement.prototype.click;
    HTMLInputElement.prototype.click=function(){if(this.type==='file'){__legacyCalls++;queueMicrotask(()=>this.dispatchEvent(new Event('cancel')));return}return click.call(this)};
    document.getElementById('open').click();await pause();
    const afterCancel=Number(state_get('project-picking'));
    check('cancel resets busy and preserves project',afterCancel===0&&preserved());
    document.getElementById('open').click();await pause();
    check('retry opens picker without fallback',__folderCalls===2&&__legacyCalls===0&&Number(state_get('project-picking'))===0);
    window.showDirectoryPicker=async()=>{throw new DOMException('permission denied','NotAllowedError')};
    document.getElementById('open').click();await pause();
    check('permission refusal preserves project without fallback',preserved()&&__legacyCalls===0&&Number(state_get('project-picking'))===0&&document.getElementById('status').textContent.includes('permission denied'));
    window.showDirectoryPicker=undefined;
    document.getElementById('open').click();await pause();
    check('unsupported picker uses cancel-safe readonly fallback',__legacyCalls===1&&preserved()&&Number(state_get('project-picking'))===0);
    const models=document.getElementById('models');models.textContent='previous models';state_set_text('vocab-file','previous-vocab');state_set_text('lora-file','previous-lora');
    document.getElementById('models-pick').click();await pause();
    check('cancel preserves checkpoint list and selections',models.textContent==='previous models'&&state_get_text('vocab-file')==='previous-vocab'&&state_get_text('lora-file')==='previous-lora');
    for(let i=0;i<4;i++){document.getElementById('musicgen-file'+i).textContent='previous file '+i;document.getElementById('musicgen-pick'+i).click();await pause();check('MusicGen picker '+i+' cancellation',document.getElementById('musicgen-file'+i).textContent==='previous file '+i&&document.getElementById('musicgen-status').textContent.includes('unchanged'));}
    const engine={fixture:true},oldFiles=Array.from({length:4},(_,i)=>MusicGenFile({mgf_name:'old'+i,mgf_size:10n}));
    let closes=0,closed=0;const close=musicgen_close;
    musicgen_close=e=>{check('only selected engine closes',e===engine);closes++;return true};
    const session=SmmSession({smm_files:oldFiles,smm_engine:Just(engine),smm_loading:false,smm_ready:()=>0n,smm_closed:()=>{closed++;return 0n}});
    try{
      smm_pick(session,0n,'.json');await pause();
      check('cancel retains loaded MusicGen engine and file handles',session.smm_engine._0===engine&&session.smm_files===oldFiles&&closes===0&&closed===0);
      HTMLInputElement.prototype.click=function(){if(this.type==='file'){Object.defineProperty(this,'files',{value:[new File(['{}'],'tokenizer.json')]});queueMicrotask(()=>this.dispatchEvent(new Event('change')));return}return click.call(this)};
      smm_pick(session,0n,'.json');await pause();
      check('successful model retry replaces one file and closes old engine once',session.smm_files[0].mgf_name!=='old0'&&session.smm_files[1]===oldFiles[1]&&session.smm_engine._tag==='None'&&closes===1&&closed===1);
    }finally{musicgen_close=close;HTMLInputElement.prototype.click=click;}
    window.showDirectoryPicker=async()=>({name:'empty',async *entries(){}});
    document.getElementById('open').click();await pause();
    check('empty folder preserves project',preserved()&&Number(state_get('project-picking'))===0);
    const selected={name:'fixture',async *entries(){yield ['ArtPrompts.txt',{kind:'file',getFile:async()=>new File(['PROMPT 1 - "Coast"\\nA quiet coast.\\n'],'ArtPrompts.txt')}];}};
    window.showDirectoryPicker=async()=>selected;
    document.getElementById('open').click();
    for(let i=0;i<100&&Number(state_get('project-picking'))!==0;i++)await pause();
    check('successful retry adopts project',_gdir===selected&&Number(state_get('project-picking'))===0&&state_get_text('llm-listing').includes('fixture/ArtPrompts.txt'));
    check('selected project reaches prompt display',document.getElementById('status').textContent.includes('1 prompts'));
    HTMLInputElement.prototype.click=click;
    return {checks,protocol:location.protocol,status:document.getElementById('status').textContent};
  })()`);
  assert.equal(result.protocol,'file:');
  assert.deepEqual(errors,[],'event-handler exceptions');assert.deepEqual(external,[],'external requests');
  writeFileSync(join(output,'evidence.json'),JSON.stringify({sha256:createHash('sha256').update(html).digest('hex'),result,errors,external},null,2));console.log(JSON.stringify(result));
} finally {
  if (send && ws?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} }
  ws?.close();
  if (browser && browser.exitCode === null) { await Promise.race([new Promise(resolve => browser.once('exit', resolve)), sleep(2000)]); if (browser.exitCode === null) browser.kill(); }
  server?.close();
  if (dirname(resolve(profile)) !== resolve(tmpdir()) || !profile.startsWith(join(tmpdir(), 'spark-llm-arm-'))) throw new Error('Profile escaped temporary root');
  rmSync(profile, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}
