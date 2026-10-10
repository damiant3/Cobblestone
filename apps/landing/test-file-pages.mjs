import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync, mkdirSync, mkdtempSync, existsSync } from 'node:fs';
import { resolve, join, dirname, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';

const here = dirname(fileURLToPath(import.meta.url));
const arg = name => { const i = process.argv.indexOf(name); return i < 0 ? null : process.argv[i + 1]; };
const web = resolve(arg('--web') || join(here, 'web'));
const out = resolve(arg('--out') || join(here, '../../build-output/landing-file-pages'));
const selected = arg('--only')?.split(',');
const edge = arg('--browser') || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const sleep = ms => new Promise(r => setTimeout(r, ms));
const walk = dir => readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? walk(join(dir, e.name)) : [join(dir, e.name)]);
const pages = walk(web).filter(p => p.endsWith('.html') && (!selected || selected.includes(relative(web, p).replaceAll('\\', '/')))).sort();
if (!pages.length) throw new Error('No selected HTML pages');
const manifestPath = join(web, 'embedded/manifest.json');
if (!existsSync(manifestPath)) throw new Error('Missing asset manifest; assemble and pack the site before grading');
if (existsSync(manifestPath)) {
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  if (!selected && !manifest.complete) throw new Error('Whole-site proof requires a complete pack');
  const documents = new Map((manifest.documents || []).map(entry => [entry.path, entry.sha256]));
  for (const page of pages) {
    const name = relative(web, page).replaceAll('\\', '/');
    if (documents.get(name) !== hash(readFileSync(page))) throw new Error('Missing or stale page manifest: ' + name);
  }
  if (!selected && documents.size !== pages.length) throw new Error('Page inventory differs from complete manifest');
  const packs = new Map();
  for (const entry of manifest.assets) {
    if (!packs.has(entry.pack)) {
      const lines = readFileSync(join(web, 'embedded', entry.pack + '.js'), 'utf8').split('\n');
      const prefix = 'Object.assign(window.__CB_ASSETS=window.__CB_ASSETS||Object.create(null),';
      if (!lines[0].startsWith(prefix) || !lines[0].endsWith(');')) throw new Error('Unknown asset pack format');
      packs.set(entry.pack, { assets: JSON.parse(lines[0].slice(prefix.length, -2)), imports: lines[1]?.startsWith('window.__CB_IMPORTS=') ? JSON.parse(lines[1].slice('window.__CB_IMPORTS='.length, -1)) : {} });
    }
    const pack = packs.get(entry.pack);
    const encoded = entry.kind === 'module' ? pack.imports['./' + entry.path.split('/').pop()].split(',')[1] : pack.assets[entry.path][1];
    if (hash(readFileSync(join(web, entry.path))) !== entry.sha256 || hash(Buffer.from(encoded, 'base64')) !== entry.sha256) throw new Error('Stale or incorrect packaged bytes: ' + entry.path);
  }
}
mkdirSync(out, { recursive: true });
const profile = mkdtempSync(join(out, 'profile-'));
const browser = spawn(edge, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--enable-unsafe-webgpu', '--disable-gpu-sandbox', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
console.log('browser PID=' + browser.pid + ' profile=' + profile);
let ws, nextId = 0;
const pending = new Map(), results = [];
const requests = new Map();
let current = null;
const watchdog = setTimeout(() => { console.error('File-page grader timed out'); browser.kill(); process.exitCode = 1; }, 900000);
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++nextId;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 90000);
    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ id, method, params }));
  });
}
async function evaluate(expression) {
  const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
  return result.result?.value;
}
const probe = `(() => {
  window.__fileProbe={errors:[],localFailures:[],requests:[],wasm:0,shaders:0};
  const p=window.__fileProbe;
  addEventListener('error',e=>p.errors.push(String(e.message)));
  addEventListener('unhandledrejection',e=>p.errors.push(String(e.reason&&e.reason.stack||e.reason)));
  const wrap=fn=>function(input,init){const url=new URL(typeof input==='string'||input instanceof URL?input:input.url,document.baseURI).href;p.requests.push(url);return Promise.resolve(fn.call(this,input,init)).catch(e=>{if(url.startsWith('file:'))p.localFailures.push(url+': '+e.message);throw e;});};
  let active=wrap(window.fetch.bind(window));
  Object.defineProperty(window,'fetch',{configurable:true,get:()=>active,set:fn=>{active=wrap(fn);}});
  for(const name of ['instantiate','instantiateStreaming','compile']){const original=WebAssembly[name];if(original)WebAssembly[name]=function(...args){return original.apply(this,args).then(value=>{p.wasm++;const x=value.instance?.exports||value.exports;if(x?.c64_screen)window.__fileC64=x;return value;});};}
  for(const name of ['Module','Instance']){const original=WebAssembly[name];WebAssembly[name]=new Proxy(original,{construct(target,args){const value=Reflect.construct(target,args);p.wasm++;return value;}});}
  if(navigator.gpu){const request=navigator.gpu.requestAdapter.bind(navigator.gpu);navigator.gpu.requestAdapter=async(...args)=>{const adapter=await request(...args);if(adapter){const deviceRequest=adapter.requestDevice.bind(adapter);adapter.requestDevice=async(...params)=>{const device=await deviceRequest(...params);device.addEventListener('uncapturederror',e=>p.errors.push(e.error.message));const create=device.createShaderModule.bind(device);device.createShaderModule=function(desc){const module=create(desc);module.getCompilationInfo().then(info=>{p.shaders++;for(const message of info.messages)if(message.type==='error')p.errors.push(message.message);});return module;};return device;};}return adapter;};}
})();`;
try {
  const portFile = join(profile, 'DevToolsActivePort');
  for (let i = 0; i < 120 && !existsSync(portFile); i++) await sleep(100);
  if (!existsSync(portFile)) throw new Error('Browser did not expose CDP');
  const port = Number(readFileSync(portFile, 'utf8').split(/\r?\n/)[0]);
  const targets = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  const target = targets.find(t => t.type === 'page');
  if (!target) throw new Error('No browser page');
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.addEventListener('open', r, { once: true }); ws.addEventListener('error', j, { once: true }); });
  ws.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const call = pending.get(message.id); pending.delete(message.id); clearTimeout(call.timer);
      if (message.error) call.reject(new Error(message.error.message)); else call.resolve(message.result || {});
    } else if (message.method === 'Fetch.requestPaused') {
      if (current) current.blockedExternal.push(message.params.request.url);
      send('Fetch.failRequest', { requestId: message.params.requestId, errorReason: 'Aborted' }).catch(() => {});
    } else if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error' && current) {
      current.consoleErrors.push(message.params.args.map(a => a.value ?? a.description ?? '').join(' '));
    } else if (message.method === 'Network.requestWillBeSent' && current) {
      requests.set(message.params.requestId, { url: message.params.request.url, page: current });
    } else if (message.method === 'Network.loadingFailed') {
      const request = requests.get(message.params.requestId);
      if (request && request.url.startsWith('file:') && message.params.type !== 'Image') request.page.networkFailures.push({ url: request.url, error: message.params.errorText, type: message.params.type });
    }
  });
  await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable');
  await send('Page.addScriptToEvaluateOnNewDocument', { source: probe });
  await send('Fetch.enable', { patterns: [{ urlPattern: 'http://*', requestStage: 'Request' }, { urlPattern: 'https://*', requestStage: 'Request' }] });
  for (const page of pages) {
    const name = relative(web, page).replaceAll('\\', '/');
    current = { page: name, url: pathToFileURL(page).href, actions: [], blockedExternal: [], consoleErrors: [], networkFailures: [] };
    try {
      await send('Page.navigate', { url: current.url });
      for (let i = 0; i < 100; i++) { if (await evaluate('location.href.split("#")[0]===' + JSON.stringify(current.url) + '&&document.readyState==="complete"&&!!window.__fileProbe')) break; await sleep(100); }
      await sleep(1300);
      const localLinks = await evaluate('[...document.querySelectorAll("a[href]")].map(a=>a.href).filter(h=>h.startsWith("file:"))');
      for (const href of localLinks) {
        const target = new URL(href); target.hash = ''; target.search = '';
        if (!existsSync(fileURLToPath(target))) throw new Error('Missing local link target: ' + href);
      }
      current.localLinks = localLinks;
      if (name === 'landing.html') {
        for (const width of [1280, 390]) {
          await send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
          await evaluate('document.getElementById("td0")?.scrollIntoView({block:"center"})');
          await sleep(200);
          const card = await evaluate('(()=>{const c=document.getElementById("td0");if(!c)return null;const r=c.getBoundingClientRect();return {text:c.innerText,links:[...c.querySelectorAll("a")].map(a=>a.getAttribute("href")),left:r.left,right:r.right,overflow:document.documentElement.scrollWidth>innerWidth,cards:document.querySelectorAll("#today-grid>.card").length};})()');
          if (!card?.text.includes('Valheim') || !card.links.includes('modbuilder/index.html') || !card.links.includes('compile/prism.html') || card.cards !== 6 || card.left < 0 || card.right > width || card.overflow) throw new Error('Grouped tool cards are missing or do not fit at ' + width + ': ' + JSON.stringify(card));
          const uoaix = await evaluate('(()=>{const c=document.getElementById("td-uo");return c&&{text:c.innerText,href:c.querySelector("a")?.getAttribute("href"),art:getComputedStyle(c).backgroundImage};})()');
          if (uoaix?.href !== 'uoaix.html' || !uoaix.text.includes('no public play') || !uoaix.text.includes('a new kind of UO') || !uoaix.text.includes('playwrights') || !uoaix.art.includes('uoaix-isometric-village.jpg')) throw new Error('UOAIX card/link/status/art missing');
          await evaluate('document.getElementById("td-uo").scrollIntoView({block:"center"})');
          await sleep(200);
          const uoaixCardShot = await send('Page.captureScreenshot', {format:'png'});
          writeFileSync(join(out, 'uoaix-card-' + width + '.png'), Buffer.from(uoaixCardShot.data, 'base64'));
          await evaluate('document.getElementById("td0").scrollIntoView({block:"center"})');
          current.actions.push({ card: 'Valheim', width, ...card });
          const shot = await send('Page.captureScreenshot', { format: 'png' });
          writeFileSync(join(out, 'valheim-' + width + '.png'), Buffer.from(shot.data, 'base64'));
          const desktopImage = await evaluate('new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve({width:i.naturalWidth,height:i.naturalHeight});i.onerror=()=>reject(new Error("Desktop screenshot did not load"));i.src=document.getElementById("td2shot").href;})');
          if (!desktopImage.width || !desktopImage.height) throw new Error('Desktop screenshot is empty');
          await evaluate('document.getElementById("td2").scrollIntoView({block:"center"})');
          await sleep(200);
          const desktopShot = await send('Page.captureScreenshot', { format: 'png' });
          writeFileSync(join(out, 'desktop-' + width + '.png'), Buffer.from(desktopShot.data, 'base64'));
          current.actions.push({ screenshot: 'desktop-clock-menu.png', viewport: width, ...desktopImage });
        }
        await send('Emulation.clearDeviceMetricsOverride');
      }
      if (name === 'uoaix.html') {
        for (const width of [1280, 390]) {
          await send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
          await evaluate('scrollTo(0,0)');
          await sleep(200);
          const content = await evaluate('({text:document.body.innerText,overflow:document.documentElement.scrollWidth>innerWidth,anchors:[...document.querySelectorAll("a[href^=\\"#\\"]")].every(a=>!!document.getElementById(a.getAttribute("href").slice(1)))})');
          for (const word of ['A new kind of UO', 'closed world', 'customer support', 'Game masters', 'Artists', 'Playwrights', 'Players', 'Public play is not available', 'Jaegermeister', 'GreyWorld', 'Wolfpack', 'prepared by Damian', 'KEEPER PLANNED']) if (!content.text.includes(word)) throw new Error('UOAIX content missing: ' + word);
          if (content.overflow || !content.anchors) throw new Error('UOAIX layout or anchor failure at ' + width);
          const art = await evaluate('Promise.all(["uoaix-hero-castle-town.jpg","uoaix-forge.jpg","uoaix-isometric-village.jpg","uoaix-keeper.jpg"].map(name=>new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve({name,width:i.naturalWidth});i.onerror=()=>reject(new Error("UOAIX art did not load: "+name));i.src="uoaix-art/"+name;})))');
          if (art.some(image => !image.width)) throw new Error('UOAIX illustration is empty');
          const applied = await evaluate('[getComputedStyle(document.getElementById("ux-art")).backgroundImage,...["ux-sim","ux-minds","ux-keeper"].map(id=>getComputedStyle(document.getElementById(id),"::before").backgroundImage)]');
          for (let i=0;i<art.length;i++) if (!applied[i].includes(art[i].name)) throw new Error('UOAIX themed image is not applied: '+art[i].name);
          current.actions.push({width,art,anchors:content.anchors,overflow:content.overflow});
          const shot = await send('Page.captureScreenshot', {format:'png'});
          writeFileSync(join(out, 'uoaix-' + width + '.png'), Buffer.from(shot.data, 'base64'));
          if (width === 1280) {
            const layout = await send('Page.getLayoutMetrics');
            const full = await send('Page.captureScreenshot', {format:'png',captureBeyondViewport:true,clip:{x:0,y:0,width,height:layout.cssContentSize.height,scale:1}});
            writeFileSync(join(out, 'uoaix-full.png'), Buffer.from(full.data, 'base64'));
          }
        }
        await send('Emulation.clearDeviceMetricsOverride');
        await evaluate('document.getElementById("ux-nav-join").click()');
        if (!await evaluate('location.hash==="#join"')) throw new Error('UOAIX join navigation failed');
        await evaluate('document.getElementById("ux-nav-history").click()');
        if (!await evaluate('location.hash==="#lineage"')) throw new Error('UOAIX lineage navigation failed');
        await evaluate('document.getElementById("ux-home").click()');
        for (let i = 0; i < 100; i++) { if (await evaluate('location.pathname.endsWith("/landing.html")&&!!document.getElementById("td-uo-a")')) break; await sleep(50); }
        if (!await evaluate('!!document.getElementById("td-uo-a")')) throw new Error('UOAIX home link failed');
        await evaluate('document.getElementById("td-uo-a").click()');
        for (let i = 0; i < 100; i++) { if (await evaluate('location.pathname.endsWith("/uoaix.html")&&!!document.getElementById("ux-title")')) break; await sleep(50); }
        if (!await evaluate('!!document.getElementById("ux-title")')) throw new Error('Landing UOAIX card navigation failed');
        current.actions.push({navigation:'lineage, home, landing card',pass:true});
      }
      if (name === 'games/index.html') {
        const cats = await evaluate('[...document.querySelectorAll("[data-cat]")].map(e=>e.dataset.cat)');
        if (!cats.length) throw new Error('Arcade controls did not initialize');
        for (const cat of cats) {
          await evaluate('document.querySelector(' + JSON.stringify('[data-cat="' + cat + '"]') + ').click()');
          const games = await evaluate('[...document.querySelectorAll(".pick[data-id]")].map(e=>e.dataset.id)');
          for (const game of games) {
            await evaluate('document.querySelector(' + JSON.stringify('[data-id="' + game + '"]') + ').click()');
            for (let i = 0; i < 100; i++) { if (await evaluate('document.getElementById("status").textContent!=="Loading the engine…"')) break; await sleep(50); }
            const result = await evaluate('({status:document.getElementById("status").textContent,failed:!!document.querySelector("#status .fail"),children:document.getElementById("stage").children.length})');
            if (result.failed || !result.children || result.status === 'Loading the engine…') throw new Error(game + ': ' + result.status);
            current.actions.push({ game, ...result });
          }
        }
      }
      if (name === 'mathbook/index.html') {
        const answer = await evaluate('document.querySelector("#tape .a")?.textContent||document.querySelector(".a")?.textContent||""');
        if (!answer.includes('5/6')) throw new Error('Notebook did not compute 1/2 + 1/3: ' + answer);
        current.actions.push({ expression: '1/2 + 1/3', answer });
      }
      if (name === 'data/index.html') {
        const value = await evaluate('({count:document.getElementById("count").textContent,result:document.getElementById("result").textContent,error:document.getElementById("err")?.textContent})');
        if (value.count !== '10 rows' || !value.result.includes('Grace') || !value.result.includes('Heidi')) throw new Error('Database did not produce its salary query: ' + value.error);
        current.actions.push(value);
      }
      if (name === 'c64/index.html') {
        const screen = '(()=>{const x=window.__fileC64;if(!x)return "";let s="";for(let i=0;i<1000;i++){const c=x.c64_screen(i)&127;s+=c===0?"@":c>=1&&c<=26?String.fromCharCode(64+c):c>=32&&c<=63?String.fromCharCode(c):" ";if(i%40===39)s+="\\n";}return s;})()';
        let boot = '';
        for (let i = 0; i < 100; i++) {
          boot = await evaluate(screen);
          if (boot.includes('READY.')) break;
          await sleep(300);
        }
        if (!boot.includes('OPEN ROMS GENERIC BUILD') || !boot.includes('READY.')) throw new Error('C64 did not reach its real ROM boot screen: ' + boot);
        for (const [key, code] of [['p','KeyP'],['r','KeyR'],['i','KeyI'],['n','KeyN'],['t','KeyT'],[' ','Space'],['2','Digit2'],['Enter','Enter']]) {
          await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code });
          await send('Input.dispatchKeyEvent', { type: 'keyUp', key, code });
          await sleep(100);
        }
        let typed = '';
        for (let i = 0; i < 50; i++) {
          typed = await evaluate(screen);
          if (/PRINT 2\s+(2|\?NOT IMPLEMENTED ERROR)\s+READY\./.test(typed)) break;
          await sleep(100);
        }
        if (!/PRINT 2\s+(2|\?NOT IMPLEMENTED ERROR)\s+READY\./.test(typed)) throw new Error('C64 keyboard input did not reach the ROM: ' + typed);
        const state = await evaluate('document.getElementById("stat").textContent');
        if (!state.includes('fps') || !state.includes('PC $')) throw new Error('C64 did not advance frames: ' + state);
        current.actions.push({ state, screen: typed, basicPrint: typed.includes('?NOT IMPLEMENTED ERROR') ? 'UNSUPPORTED: known BASIC compatibility gap' : 'computed' });
      }
      if (name === 'starmap/index.html') {
        const state = await evaluate('({total:document.getElementById("n-total").textContent,drawn:document.getElementById("n-drawn").textContent,error:document.getElementById("err").textContent})');
        if (!Number(state.total.replaceAll(',', '')) || !Number(state.drawn.replaceAll(',', '')) || state.error) throw new Error('Star Map did not draw its catalogue: ' + state.error);
        current.actions.push(state);
      }
      if (name === 'experimental/index.html') {
        for (const kind of ['wasm', 'wgsl']) {
          await evaluate('document.getElementById(' + JSON.stringify('go-' + kind) + ').click()');
          for (let i = 0; i < 180; i++) {
            if (await evaluate('!document.getElementById(' + JSON.stringify('go-' + kind) + ').disabled')) break;
            await sleep(500);
          }
          const result = await evaluate('({done:!document.getElementById(' + JSON.stringify('go-' + kind) + ').disabled,ok:document.getElementById(' + JSON.stringify('v-' + kind) + ').classList.contains("ok"),text:document.getElementById(' + JSON.stringify('v-' + kind) + ').textContent})');
          if (!result.done || !result.ok) throw new Error(kind + ' compile: ' + result.text);
          current.actions.push({ compile: kind, ...result });
        }
      }
      if (name === 'gpushow/web/cube.html') {
        await evaluate('document.getElementById("lv-go").click()');
        for (let i = 0; i < 180; i++) {
          if (await evaluate('!!window.__live')) break;
          await sleep(500);
        }
        const result = await evaluate('window.__live');
        if (!result?.ok || !result.same) throw new Error('Cube live compile did not reproduce its shader: ' + JSON.stringify(result));
        current.actions.push({ compile: 'CubeKernel', same: result.same, decks: result.decks });
      }
      if (name === 'gpushow/web/index.html') {
        const kernels = await evaluate('[...document.querySelectorAll("button[data-kernel]")].map(b=>b.dataset.kernel)');
        if (!kernels.length) throw new Error('GPU source controls did not initialize');
        for (const kernel of kernels) {
          await evaluate('document.querySelector(' + JSON.stringify('button[data-kernel="' + kernel + '"]') + ').click()');
          for (let i = 0; i < 40; i++) {
            if (await evaluate('!document.getElementById("code").textContent.includes("loading...")')) break;
            await sleep(50);
          }
          const source = await evaluate('document.getElementById("code").textContent');
          if (!source.includes('Chapter:') || source.startsWith('failed to load')) throw new Error('GPU source did not load: ' + kernel);
          current.actions.push({ source: kernel });
          await evaluate('document.getElementById("close").click()');
        }
      }
      await sleep(300);
      current.probe = await evaluate('window.__fileProbe');
      current.text = await evaluate('document.body.innerText.slice(0,1800)');
      const wasmPage = /^(games|c64|data|mathbook|safari|spark|starmap|fishtank)\//.test(name);
      if (wasmPage && !current.probe.wasm) throw new Error('No WASM module ran or compiled');
      const gpuPage = (/^gpushow\/web\/.+\.html$/.test(name) && !name.endsWith('/index.html')) || /^(globe|fireworks|fishtank)\//.test(name);
      if (gpuPage && !current.probe.shaders) throw new Error('No GPU shader compiled');
      if (current.probe.localFailures.length || current.probe.errors.length || current.consoleErrors.length || current.networkFailures.length) throw new Error('Page or local-asset errors');
      current.pass = true;
    } catch (error) {
      current.pass = false; current.error = String(error.stack || error);
      try { current.probe = await evaluate('window.__fileProbe'); current.text = await evaluate('document.body.innerText.slice(0,1800)'); } catch {}
    }
    try {
      const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      writeFileSync(join(out, name.replaceAll('/', '_') + '.png'), Buffer.from(shot.data, 'base64'));
    } catch (error) { current.screenshotError = error.message; }
    results.push(current); console.log((current.pass ? 'PASS ' : 'FAIL ') + name + (current.error ? ': ' + current.error.split('\n')[0] : ''));
    writeFileSync(join(out, 'results.json'), JSON.stringify(results, null, 2));
  }
  process.exitCode = results.every(r => r.pass) ? 0 : 1;
  await send('Browser.close');
} finally {
  clearTimeout(watchdog);
  for (const call of pending.values()) { clearTimeout(call.timer); call.reject(new Error('Browser closed')); }
  ws?.close();
  if (browser.exitCode === null) browser.kill();
}
