import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer as portServer } from 'node:net';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const artifact = resolve(arg('--html', join(repo, 'build-output/spark-file/wizard.html')));
const output = resolve(arg('--out', join(repo, 'build-output/spark-file/wizard')));
assert.ok(output.startsWith(join(repo, 'build-output') + '\\'));
mkdirSync(output, { recursive: true });
const html = readFileSync(artifact);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const freePort = () => new Promise(resolve => { const server = portServer(); server.listen(0, '127.0.0.1', () => { const port = server.address().port; server.close(() => resolve(port)); }); });
const profile = mkdtempSync(join(tmpdir(), 'spark-wizard-arm-'));
let browser, ws, send;
const errors = [], external = [];
try {
  const debug = await freePort();
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
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 60000);
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
  await send('Browser.setDownloadBehavior', { behavior: 'deny' }); await send('Page.navigate', { url: pathToFileURL(artifact).href });
  await until('typeof _st !== "undefined" && Number(_st.ready) === 1 && typeof window.__sparkWizard === "object"');
  assert.deepEqual(errors, [], 'startup exceptions');
  const result = await evaluate(`(async()=>{
    const el=id=>document.getElementById(id),pause=(ms=100)=>new Promise(r=>setTimeout(r,ms));
    const checks=[];const check=(name,pass)=>{checks.push({name,pass});if(!pass)throw new Error(name+' | status: '+el('wizard-status').textContent+' | '+el('wizard-progress').textContent)};
    const status=()=>el('wizard-status').textContent,progress=()=>el('wizard-progress').textContent;
    const folder=(name,files)=>({name,kind:'directory',files,
      async *entries(){for(const [k,v] of files)yield [k,{kind:'file',name:k,getFile:async()=>new File([v],k)}]},
      async getFileHandle(path,options){if(!files.has(path)&&!options?.create)throw new DOMException(path,'NotFoundError');
        return {kind:'file',name:path,getFile:async()=>new File([files.get(path)??''],path),
          async createWritable(){let next='';return {async write(text){next=String(text)},async close(){files.set(path,next)},async abort(){}}}}}});
    const children=new Map();
    const parent={name:'Fixture Parent',kind:'directory',
      async getDirectoryHandle(name,options){if(!children.has(name)){if(!options?.create)throw new DOMException(name,'NotFoundError');children.set(name,folder(name,new Map()))}return children.get(name)}};
    const next=async()=>{el('wizard-next').click();await pause()};
    el('new-project').click();await pause();
    check('wizard opens at step 1 of 5',el('wizard').open&&progress().startsWith('Step 1 of 5'));
    check('defaults match the original wizard',el('wizard-name').value==='My Game'&&el('wizard-style').value==='Sci-Fi Concept Art'&&el('wizard-count').value==='10'&&el('wizard-size').value==='1344x768'&&el('wizard-steps').value==='20'&&el('wizard-cfg').value==='7'&&el('wizard-sampler').value==='DPM++ 2M SDE');
    check('create is hidden before the last step',el('wizard-create').style.display==='none');
    el('wizard-name').value='';await next();
    check('an empty project name is refused at step 1',progress().startsWith('Step 1 of 5')&&/project name/i.test(status()));
    window.showDirectoryPicker=async()=>{throw new DOMException('fixture','AbortError')};
    el('wizard-folder').click();await pause();
    check('folder picker cancellation keeps the wizard open',el('wizard').open&&/cancelled/.test(status()));
    window.showDirectoryPicker=async options=>{window.__pickerMode=options?.mode;return parent};
    el('wizard-folder').click();await pause();
    check('the folder picker asks for readwrite',window.__pickerMode==='readwrite'&&el('wizard-folder-name').textContent.includes('Fixture Parent'));
    el('wizard-name').value='Cloud Harbor';await next();
    check('step 2 is story and setting, with the assistant shown',progress().startsWith('Step 2 of 5')&&el('wizard-assistant').style.display==='');
    el('wizard-story').value='Akara watches the airships over Cloud Harbor.';await next();
    el('wizard-style').value='Custom (describe below)';el('wizard-style-notes').value='';await next();
    check('custom style without notes is refused at step 3',progress().startsWith('Step 3 of 5')&&/custom art style/i.test(status()));
    el('wizard-style').value='High Fantasy';await next();
    check('step 4 is art prompts',progress().startsWith('Step 4 of 5'));
    el('wizard-prompts').value='';await next();
    check('empty prompts advance, as the original falls back to its template',progress().startsWith('Step 5 of 5'));
    const review=el('wizard-review').textContent;
    check('review names the folder, 3 prompts and 6 files',review.includes('Fixture Parent/Cloud Harbor')&&review.includes('3 prompts')&&['universe.json','Story.md','art_directions.json','creative_pools.json','ArtPrompts.txt','spark_project.json'].every(f=>review.includes(f))&&el('wizard-create').style.display===''&&!el('wizard-create').disabled);
    check('nothing is written before Create',children.size===0);
    el('wizard-create').click();
    for(let i=0;i<200&&!/^Created /.test(el('status').textContent+status());i++)await pause(50);
    const made=children.get('Cloud Harbor');
    check('Create writes the 6 project files',made&&[...made.files.keys()].join(',')==='universe.json,Story.md,art_directions.json,creative_pools.json,ArtPrompts.txt,spark_project.json');
    const manifest=JSON.parse(made.files.get('spark_project.json'));
    check('the manifest carries the wizard settings',manifest.name==='Cloud Harbor'&&manifest.defaultSettings.width===1344&&manifest.defaultSettings.height===768&&manifest.defaultSettings.steps===20&&manifest.defaultSettings.cfgScale===7&&manifest.defaultSettings.sampler==='DPM++ 2M SDE'&&manifest.defaultSettings.scheduler==='karras');
    check('empty prompts wrote the 3 template prompts',made.files.get('ArtPrompts.txt').includes('PROMPT 03')&&made.files.get('ArtPrompts.txt').includes('high fantasy style'));
    check('the story and glossary are written',made.files.get('Story.md').startsWith('# Cloud Harbor')&&JSON.parse(made.files.get('universe.json')).glossary==='Akara watches the airships over Cloud Harbor.');
    check('the created project opens with its 3 prompts',!el('wizard').open&&_gdir===made&&Number(state_get('prompts'))===3&&el('status').textContent.includes('Created Cloud Harbor with 3 prompts'));
    check('no image generation started',Number(state_get('image-busy'))!==1&&Number(state_get('q-busy'))!==1);
    el('new-project').click();await pause();
    check('the wizard reopens at step 1',el('wizard').open&&progress().startsWith('Step 1 of 5'));
    for(let i=0;i<4;i++)await next();
    const before=[...made.files.entries()].join('|');
    el('wizard-create').click();await pause(300);
    check('an existing project folder is refused and left untouched',/already exists/.test(status())&&[...made.files.entries()].join('|')===before&&el('wizard').open);
    el('wizard-close').click();await pause();
    check('close dismisses the wizard',!el('wizard').open);
    return {checks,protocol:location.protocol,status:el('status').textContent};
  })()`);
  assert.equal(result.protocol, 'file:');
  assert.deepEqual(errors, [], 'event-handler exceptions'); assert.deepEqual(external, [], 'external requests');
  writeFileSync(join(output, 'evidence.json'), JSON.stringify({ sha256: createHash('sha256').update(html).digest('hex'), result, errors, external }, null, 2));
  console.log(JSON.stringify(result));
  console.log('PASS: ' + result.checks.length + ' wizard browser checks over file://, no external requests');
} finally {
  if (send && ws?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} }
  ws?.close();
  if (browser && browser.exitCode === null) { await Promise.race([new Promise(resolve => browser.once('exit', resolve)), sleep(2000)]); if (browser.exitCode === null) browser.kill(); }
  if (dirname(resolve(profile)) !== resolve(tmpdir()) || !profile.startsWith(join(tmpdir(), 'spark-wizard-arm-'))) throw new Error('Profile escaped temporary root');
  rmSync(profile, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}
