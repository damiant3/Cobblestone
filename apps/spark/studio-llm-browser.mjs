import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { createServer as portServer } from 'node:net';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const artifact = resolve(arg('--html', join(repo, 'build-output/spark-llm/studio.html')));
const output = resolve(arg('--out', join(repo, 'build-output/spark-llm/browser')));
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
      if (request.request.url.startsWith(origin + '/')) send('Fetch.continueRequest', { requestId: request.requestId }).catch(() => {});
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
  await send('Page.navigate', { url: origin + '/' });
  await until('!!window.__sparkLlm');
  assert.equal(await evaluate('document.getElementById("llm-run").disabled'), true);
  assert.deepEqual(errors, []);
  await evaluate(`(() => {
    document.getElementById('llm').open=true;
    window.__llmGenerates=0;document.getElementById('go').addEventListener('click',()=>window.__llmGenerates++);
    window.__llmEngineCalls=0;
    for(const name of ['ss_generate','ss_generate__tb','ss_image_run','spark_image_run','spark_image_run__tb']){
      if(typeof window[name]!=='function')throw new Error('Missing image entry witness: '+name);
      window[name]=()=>{window.__llmEngineCalls++;throw new Error('Writing assistant invoked image generation')};
    }
    const manifest=JSON.stringify({storyFiles:['Story.md']});
    const story='The lighthouse keeper watches the icy sea. Café 日本.';
    _gfiles.llmManifest=new File([manifest],'spark_project.json');_gfiles.llmStory=new File([story],'Story.md');
    state_set_text('llm-listing',JSON.stringify([{name:'llmManifest',path:'Fixture/spark_project.json',size:manifest.length},{name:'llmStory',path:'Fixture/Story.md',size:100}]));
    document.getElementById('prompt').value='A lighthouse';
    window.__llmRequests=0;
    window.__llmReply=(text,reason='end_turn')=>[
      {type:'content_block_start',index:0,content_block:{type:'text'}},
      {type:'content_block_delta',index:0,delta:{type:'text_delta',text}},
      {type:'content_block_stop',index:0},
      {type:'message_delta',delta:{stop_reason:reason,stop_details:{category:'fixture'}}},
      {type:'message_stop'}].map(e=>'data: '+JSON.stringify(e)+'\\n\\n').join('');
    __sparkLlm.provider.setTransport(async(key,body,push)=>{window.__llmRequests++;window.__llmBody=body;const text=__llmReply('An icy lighthouse at dawn.');for(let i=0;i<text.length;i+=5)push(text.slice(i,i+5))});
    document.getElementById('llm-key').value='fixture-provider-key';document.getElementById('llm-key-save').click();
    document.getElementById('llm-run').click();return 1;
  })()`);
  await until('!__sparkLlm.busy()');
  const first = await evaluate(`({status:document.getElementById('llm-status').textContent,context:document.getElementById('llm-context').value,output:document.getElementById('llm-output').value,prompt:document.getElementById('prompt').value,apply:document.getElementById('llm-apply').disabled,keyField:document.getElementById('llm-key').value,body:JSON.stringify(__llmBody)})`);
  assert.match(first.status, /Draft ready/);
  assert.match(first.context, /lighthouse keeper/);
  assert.match(first.body, /Café 日本/);
  assert.equal(first.body.includes('fixture-provider-key'), false);
  assert.equal(first.keyField, '');
  assert.equal(first.prompt, 'A lighthouse');
  assert.equal(first.output, 'An icy lighthouse at dawn.');
  assert.equal(first.apply, false);
  await evaluate("document.getElementById('llm-apply').click()");
  assert.equal(await evaluate("document.getElementById('prompt').value"), first.output);
  assert.equal(await evaluate('__llmGenerates'), 0);
  assert.equal(await evaluate('__llmEngineCalls'), 0);
  await evaluate(`__sparkLlm.provider.setTransport(async(k,b,p)=>p(__llmReply('', 'refusal')));document.getElementById('llm-run').click()`);
  await until('!__sparkLlm.busy()');
  assert.equal(await evaluate("document.getElementById('llm-apply').disabled"), true);
  assert.match(await evaluate("document.getElementById('llm-status').textContent"), /declined/);
  await evaluate(`window.__llmStarted=false;__sparkLlm.provider.setTransport((k,b,p,s)=>new Promise((r,j)=>{__llmStarted=true;s.addEventListener('abort',()=>{const e=new Error('cancelled');e.name='AbortError';j(e)})}));document.getElementById('llm-run').click()`);
  await until('__llmStarted');
  await evaluate("document.getElementById('llm-cancel').click()");
  await until('!__sparkLlm.busy()');
  assert.match(await evaluate("document.getElementById('llm-status').textContent"), /cancelled/);
  const contextMs = await evaluate(`(() => {const q=document.getElementById('llm-context-query');const content=('A lighthouse on the icy coast. '.repeat(8)+'\\n\\n').repeat(100);q.value=JSON.stringify({query:'lighthouse',documents:[{name:'Large.md',content}]});const start=performance.now();q.dispatchEvent(new Event('input',{bubbles:true}));const result=JSON.parse(document.getElementById('llm-context-reply').value);if(!result.ok)throw new Error(result.error);return performance.now()-start})()`);
  assert.ok(contextMs < 10000, 'bounded context computation');
  const longQueryMs = await evaluate(`(() => {const q=document.getElementById('llm-context-query');const words=Array.from({length:200},(_,i)=>'word'+i);const query=(words.join(' ')+' ').repeat(10);const content=(words.slice(0,40).join(' ')+'\\n\\n').repeat(100);q.value=JSON.stringify({query,documents:[{name:'Long.md',content}]});const start=performance.now();q.dispatchEvent(new Event('input',{bubbles:true}));const result=JSON.parse(document.getElementById('llm-context-reply').value);if(!result.ok)throw new Error(result.error);return performance.now()-start})()`);
  assert.ok(longQueryMs < 10000, 'long query with bounded document workload');
  await evaluate(`__sparkLlm.provider.setTransport(async(k,b,p)=>p(__llmReply('An icy lighthouse at dawn.')));document.getElementById('llm-run').click()`);
  await until('!__sparkLlm.busy()');
  for (const [name, width, height] of [['desktop', 1280, 1000], ['narrow', 390, 900]]) {
    await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
    await evaluate("document.getElementById('llm').scrollIntoView({block:'start'})");
    await sleep(150);
    assert.equal(await evaluate("(()=>{const r=document.getElementById('llm').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth})()"), true);
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(join(output, name + '.png'), Buffer.from(shot.data, 'base64'));
  }
  assert.equal(await evaluate('__llmGenerates'), 0);
  assert.equal(await evaluate('__llmEngineCalls'), 0);
  await evaluate("document.getElementById('llm-key-clear').click()");
  assert.equal(await evaluate("localStorage.getItem('spark.llm.anthropic.key')"), null);
  assert.equal(html.includes(Buffer.from('fixture-provider-key')), false);
  assert.deepEqual(errors, []);
  assert.deepEqual(external, []);
  writeFileSync(join(output, 'evidence.json'), JSON.stringify({ status: 'pass', artifact: createHash('sha256').update(html).digest('hex'), contextMs, longQueryMs, externalRequests: external.length, billedCalls: 0, imageGenerateClicks: 0, imageEntryCalls: 0, keyExclusionScope: ['request body', 'built HTML'] }, null, 2));
  console.log('PASS: packaged Studio controls/provider/context, explicit apply, refusal/cancel, key absent from body/artifact and two captures; no external request or image entry');
} finally {
  if (send && ws?.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} }
  ws?.close();
  if (browser && browser.exitCode === null) { await Promise.race([new Promise(resolve => browser.once('exit', resolve)), sleep(2000)]); if (browser.exitCode === null) browser.kill(); }
  server?.close();
  if (dirname(resolve(profile)) !== resolve(tmpdir()) || !profile.startsWith(join(tmpdir(), 'spark-llm-arm-'))) throw new Error('Profile escaped temporary root');
  rmSync(profile, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}
