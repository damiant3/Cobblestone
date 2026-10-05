// Drive the built browser image page (build-output/browser-imagegen.html) in
// headless Edge through a real models folder: every .safetensors is checked by
// its header, an SD1.5 file loads onto the GPU, and one image is generated.
// SDXL loads after SD1.5, and unload releases both active and pooled buffers.
//
// Usage: node apps/diffusion/browser-page-arm.mjs <models dir> <sd15 file> <sdxl file> <refused file> <page> <native.png>
// Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync, createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const [dir, goodName, xlName, badName, pagePath, referencePath] = process.argv.slice(2);
if (!referencePath) { console.log('usage: browser-page-arm.mjs <models dir> <sd15 file> <sdxl file> <refused file> <page> <native.png>'); process.exit(2); }
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const hashFile = async path => { const h=createHash('sha256');for await(const b of createReadStream(path))h.update(b);return h.digest('hex'); };
const evidence=JSON.parse(readFileSync(join(dirname(referencePath),'reference-evidence.json'),'utf8'));
if(evidence.nativeExit!==0||!/^png 1024x1024: [0-9]+ bytes$/.test(evidence.nativeResult)||hash(readFileSync(referencePath))!==evidence.imageSha256||await hashFile(join(dir,xlName))!==evidence.checkpointSha256||await hashFile(join(dirname(referencePath),'reference.cdx'))!==evidence.nativeCdxSha256||await hashFile(join(repo,'build/sdxl-browser-reference.codex'))!==evidence.nativeSourceSha256)throw new Error('Native reference evidence differs');
const pageData=JSON.parse(readFileSync(pagePath,'utf8').match(/window\.__DATA=(.*?);<\/script>/s)?.[1]||'{}');
for(const [key,recorded] of [['vocab',evidence.vocabSha256],['merges',evidence.mergesSha256]]){
  const bytes=pageData[key]?Buffer.from(JSON.parse(pageData[key])):readFileSync(join(dir,'tokenizers/clip',key==='vocab'?'vocab.json':'merges.txt'));
  if(hash(bytes)!==recorded)throw new Error('Tokenizer evidence differs: '+key);
}
console.log('Page SHA256 '+hash(readFileSync(pagePath)));
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

async function cdpOpen(port) {
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map();
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; const timer=setTimeout(()=>{waiting.delete(i);rej(new Error('CDP timeout: '+method));},120000);waiting.set(i,m=>{clearTimeout(timer);if(m.error)rej(new Error(JSON.stringify(m.error)));else res(m);}); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [resolve(dir)], backendNodeId: m.params.backendNodeId }).catch(e=>ok('folder selection',false,e.message));
  });
  await send('Page.enable');
  await send('DOM.enable');
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  };
  return { send, evalIn };
}

const text = (sel) => `(document.querySelector(${JSON.stringify(sel)}) || {}).textContent || ''`;
async function waitFor(evalIn, expr, seconds) {
  for (let i = 0; i < seconds * 4; i++) { try { const v = await evalIn(expr); if (v) return v; } catch {} await sleep(250); }
  return null;
}

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'browser-page-arm-'));
  const html = readFileSync(pagePath, 'utf8');
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', 'about:blank'], { stdio: 'ignore' });
  const { send, evalIn } = await cdpOpen(dbg);
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  ok('the page builds itself', !!(await waitFor(evalIn, `!!document.querySelector('#pick')`, 60)));
  await evalIn(`window.__missing=[];const launch=gpu_launch;gpu_launch=function(code,name,g,ps){for(const p of ps){if(p.handle!==undefined&&Number(p.handle)>0&&!_gb[Number(p.handle)]&&__missing.length<8)__missing.push({name,handle:String(p.handle),stack:new Error().stack});}return launch(code,name,g,ps);};`);

  await evalIn(`document.querySelector('#pick').click(), 1`);
  const checked = await waitFor(evalIn, `(${text('#status')}).startsWith('Checked ') ? ${text('#status')} : ''`, 120);
  ok('both model families are checked by their headers', /Checked 3 files: 2 supported/.test(checked || ''), checked || await evalIn(text('#status')));
  const rows = JSON.parse(await evalIn(`JSON.stringify([...document.querySelectorAll('#models > div')].map(e => e.textContent))`));
  const bad = rows.find(r => r.includes(badName)) || '', good = rows.find(r => r.includes(goodName)) || '';
  ok('unsupported data is refused', bad.includes('(unsupported checkpoint)'), bad);
  ok('the SD1.5 file is offered', good.includes('(SD1.5)'), good);
  ok('the SDXL file is offered', rows.some(r => r.includes(xlName) && r.includes('(SDXL)')));

  const idx = rows.findIndex(r => r.includes(goodName));
  await evalIn(`window.__upload=gpu_buf_upload_then;window.__uploads=0;gpu_buf_upload_then=function(...a){if(++__uploads===5){a[a.length-1](JSON.stringify({error:'injected read failure'}));return 0;}return __upload(...a);};document.querySelector('#m${idx}').click();`);
  const refused=await waitFor(evalIn,`(${text('#status')}).startsWith('Load failed:') ? ${text('#status')} : ''`,120);
  ok('partial weight upload failure is reported',/injected read failure/.test(refused||''),refused);
  ok('partial load releases active and pooled buffers',await evalIn(`_gb.filter(Boolean).length===0&&Object.values(_gpool).flat().length===0&&String(_st.busy)==='0'`));
  await evalIn(`gpu_buf_upload_then=__upload;`);
  await evalIn(`document.querySelector('#m${idx}').click(), 1`);
  const ready = await waitFor(evalIn, `(${text('#status')}).startsWith('Ready') || (${text('#status')}).startsWith('The ') || (${text('#status')}).startsWith('WebGPU') ? ${text('#status')} : ''`, 600);
  ok('the checkpoint loads onto the GPU and the tokenizer is ready', /^Ready: /.test(ready || ''), ready || await evalIn(text('#status')));

  await evalIn(`document.querySelector('#steps').value = '2', document.querySelector('#go').click(), 1`);
  const done = await waitFor(evalIn, `(${text('#status')}) === 'Done.' || (${text('#status')}).startsWith('Generation failed') || (${text('#status')}).startsWith('The ') ? ${text('#status')} : ''`, 600);
  ok('one 2-step image is generated and drawn', done === 'Done.', done || await evalIn(text('#status')));
  const stats = await evalIn(text('#stats'));
  ok('the stats name the GPU adapter', /GPU: \S/.test(stats), stats.replace(/\n/g, ' | '));
  const pix = JSON.parse(await evalIn(`(() => { const c = document.querySelector('#out'); const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data; let s = 0, v = new Set(); for (let i = 0; i < d.length; i += 4097) { s += d[i]; v.add(d[i]); } return JSON.stringify({ w: c.width, h: c.height, distinct: v.size }); })()`));
  ok('the canvas holds a 512 x 512 image that is not one flat colour', pix.w === 512 && pix.h === 512 && pix.distinct > 8, JSON.stringify(pix));
  const xi = rows.findIndex(r => r.includes(xlName));
  await evalIn(`document.querySelector('#m${xi}').click(), 1`);
  const xlReady = await waitFor(evalIn, `(${text('#status')}).startsWith('Ready:') || (${text('#status')}).startsWith('Load failed:') ? ${text('#status')} : ''`, 600);
  ok('switching loads SDXL', /Ready:.*\(SDXL\)/.test(xlReady || ''), xlReady);
  if (!/Ready:.*\(SDXL\)/.test(xlReady || '')) throw new Error('SDXL load failed');
  ok('SDXL defaults identify the actual schedule', await evalIn(`document.querySelector('#steps').value === '6' && document.querySelector('#cfg').value === '2' && document.querySelector('#settings').textContent === '1024 x 1024, Euler, Karras'`));
  await evalIn(`document.querySelector('#steps').value='1';document.querySelector('#go').click();`);
  ok('invalid step count is refused before generation', (await evalIn(text('#status'))).includes('Steps must be 2-50'));
  const prompt = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, four iron-bound wooden chests around a carpenter workbench, joined by glowing golden rune threads, inside a torch-lit longhouse';
  const negative = 'text, letters, words, watermark, signature, logo, ui, blurry, deformed, ugly, lowres, jpeg artifacts, photo, photograph, modern';
  const busy = await evalIn(`(()=>{document.querySelector('#prompt').value=${JSON.stringify(prompt)};document.querySelector('#negative').value=${JSON.stringify(negative)};document.querySelector('#seed').value='7201';document.querySelector('#steps').value='6';document.querySelector('#go').click();const active=_gb.filter(Boolean).length,path=_st.path;document.querySelector('#unload').click();document.querySelector('#m${idx}').click();return String(_st.busy)==='2'&&_st.path===path&&_gb.filter(Boolean).length===active;})()`);
  ok('busy actions preserve model identity and live handles',busy);
  const xlDone = await waitFor(evalIn, `(${text('#status')}) === 'Done.' || (${text('#status')}).startsWith('Generation failed') ? ${text('#status')} : ''`, 600);
  ok('SDXL generation survives attempted busy switch/unload', xlDone === 'Done.', xlDone);
  if(xlDone!=='Done.')throw new Error(await evalIn(`JSON.stringify({missing:__missing,console:document.querySelector('#console').textContent,gpu:_gerr})`));
  const ref = 'data:image/png;base64,' + readFileSync(referencePath).toString('base64');
  const comparison = await evalIn(`new Promise((resolve,reject)=>{const im=new Image();im.onload=()=>{const c=document.createElement('canvas');c.width=im.width;c.height=im.height;const ctx=c.getContext('2d');ctx.drawImage(im,0,0);const out=document.querySelector('#out');if(out.width!==1024||out.height!==1024||im.width!==1024||im.height!==1024){reject(new Error('dimensions differ'));return;}const a=ctx.getImageData(0,0,1024,1024).data,b=out.getContext('2d').getImageData(0,0,1024,1024).data;let sum=0,max=0,n=0;for(let i=0;i<a.length;i++){if(i%4===3)continue;const d=Math.abs(a[i]-b[i]);sum+=d;max=Math.max(max,d);n++;}resolve({mean:sum/n,max,count:n});};im.onerror=reject;im.src=${JSON.stringify(ref)};})`);
  ok('SDXL canvas meets native mean error below 4/255', comparison.count===3145728 && comparison.mean<4, JSON.stringify(comparison));
  writeFileSync(pagePath+'.sdxl.png',Buffer.from(await evalIn(`document.querySelector('#out').toDataURL('image/png').split(',')[1]`),'base64'));
  writeFileSync(pagePath+'.comparison.json',JSON.stringify({pageSha256:hash(readFileSync(pagePath)),referenceSha256:evidence.imageSha256,comparison},null,2)+'\n');
  ok('exactly two jobs finished', await evalIn(`document.querySelector('#history').children.length===2`));
  await evalIn(`document.querySelector('#m${idx}').click();`);
  const back = await waitFor(evalIn, `(${text('#status')}).startsWith('Ready:') || (${text('#status')}).startsWith('Load failed:') ? ${text('#status')} : ''`, 600);
  ok('switching back loads SD1.5', /Ready:.*\(SD1.5\)/.test(back || ''), back);
  await evalIn(`document.querySelector('#steps').value='2';document.querySelector('#prompt').value='a photo of a cat';document.querySelector('#negative').value='';document.querySelector('#seed').value='0';document.querySelector('#go').click();`);
  ok('returned SD1.5 model generates',await waitFor(evalIn, `(${text('#status')})==='Done.'`,600));
  ok('returned SD1.5 canvas and third job',await evalIn(`document.querySelector('#out').width===512&&document.querySelector('#out').height===512&&document.querySelector('#history').children.length===3`));
  await evalIn(`document.querySelector('#unload').click();`);
  ok('unload completes', await waitFor(evalIn, `(${text('#status')})==='Checkpoint unloaded.'`, 60));
  ok('unload leaves no active or pooled buffers', await evalIn(`_gb.filter(Boolean).length===0 && Object.values(_gpool).flat().length===0`));
  ok('GPU error channel remains empty',await evalIn(`_gerr===''`));
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `$profile='${join(work,'profile').replace(/'/g, "''")}';Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like ('*'+$profile+'*') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue };Start-Sleep -Milliseconds 300;if(@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like ('*'+$profile+'*') }).Count){exit 1}`]);ok('owned browser processes stopped',true); } catch(e) { ok('owned browser processes stopped',false,e.message); }
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
