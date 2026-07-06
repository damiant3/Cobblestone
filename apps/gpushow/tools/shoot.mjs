// Serve apps/gpushow over localhost, run a WebGPU page in headless Chrome,
// wait for a target animation frame, and screenshot the canvas to a PNG.
// Node built-ins only (http, fetch, global WebSocket). CDP-driven.
//
// Usage: node shoot.mjs <page-path> <out.png> [chromePath]
//   e.g. node shoot.mjs /web/fireworks.html shot.png

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, mkdtemp, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, extname, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const pagePath = process.argv[2] || '/web/fireworks.html';
const outPng = process.argv[3] || 'shot.png';
const chromePath = process.argv[4] || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const appRoot = normalize(join(fileURLToPath(import.meta.url), '..', '..')); // apps/gpushow
const PORT = 9317, CDP = 9318;
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

const MIME = { '.html':'text/html', '.wgsl':'text/plain', '.mjs':'text/javascript',
  '.js':'text/javascript', '.css':'text/css', '.json':'application/json', '.png':'image/png' };

const server = createServer(async (req, res) => {
  try {
    const rel = decodeURIComponent(req.url.split('?')[0]);
    const path = normalize(join(appRoot, rel));
    if (!path.startsWith(appRoot)) { res.writeHead(403).end(); return; }
    const body = await readFile(path);
    res.writeHead(200, { 'content-type': MIME[extname(path)] || 'application/octet-stream' });
    res.end(body);
  } catch { res.writeHead(404).end('not found'); }
});
await new Promise(r => server.listen(PORT, '127.0.0.1', r));

// --- minimal CDP client ---
async function cdpWs() {
  for (let i = 0; i < 80; i++) {
    try {
      const t = await (await fetch(`http://127.0.0.1:${CDP}/json`)).json();
      const p = t.find(x => x.type === 'page');
      if (p?.webSocketDebuggerUrl) return p.webSocketDebuggerUrl;
    } catch {}
    await sleep(250);
  }
  throw new Error('CDP never came up');
}
function connect(url) {
  const ws = new WebSocket(url);
  let id = 0; const pending = new Map();
  const ready = new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  });
  const send = (method, params={}) => new Promise((res) => { const i = ++id; pending.set(i, res); ws.send(JSON.stringify({ id:i, method, params })); });
  return { ready, send, close: () => ws.close() };
}
const evalJs = async (cdp, expr) => {
  const r = await cdp.send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  return r.result?.result?.value;
};

const userDir = await mkdtemp(join(tmpdir(), 'gpushow-shoot-'));
const chrome = spawn(chromePath, [
  '--headless=new', `--remote-debugging-port=${CDP}`, '--remote-allow-origins=*',
  '--enable-unsafe-webgpu', '--enable-unsafe-swiftshader', '--enable-features=Vulkan,WebGPU',
  '--no-first-run', '--no-default-browser-check', '--window-size=1060,860',
  '--hide-scrollbars', `--user-data-dir=${userDir}`,
  `http://127.0.0.1:${PORT}${pagePath}`,
], { stdio: 'ignore' });

let exit = 1;
try {
  const cdp = connect(await cdpWs());
  await cdp.ready;
  await cdp.send('Page.enable');
  // wait for readiness + a good burst frame
  let ready = false, err = null;
  for (let i = 0; i < 200; i++) {
    const st = await evalJs(cdp, 'JSON.stringify({r:window.__ready===true, e:window.__error||null, f:window.__frame||0})');
    const s = st ? JSON.parse(st) : {};
    if (s.e) { err = s.e; break; }
    if (s.r && s.f >= 90 && s.f <= 170) { ready = true; break; }
    await sleep(40);
  }
  if (err) { console.error('page error:', err); }
  else {
    if (!ready) console.error('warning: frame window not hit; capturing anyway');
    const shot = await cdp.send('Page.captureScreenshot', { format: 'png' });
    const data = shot.result?.data;
    if (data) {
      await writeFile(outPng, Buffer.from(data, 'base64'));
      const frame = await evalJs(cdp, 'window.__frame||0');
      console.log(`captured ${outPng} at frame ${frame}`);
      exit = 0;
    } else console.error('no screenshot data');
  }
  cdp.close();
} catch (e) { console.error('shoot failed:', e.message || e); }
finally { chrome.kill(); server.close(); }
process.exit(exit);
