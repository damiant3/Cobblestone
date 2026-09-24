// PRISM-7 stage 2d: the compile page's endpoints, driven in headless Chrome
// against a real build-bridge.ps1. The page under test is the TEMPLATE
// (codex/plugs/wasm/page/prism.html) with the deployed page's embedded modules
// spliced in at <!--EMBED-->, so a template edit is graded before any redeploy.
// Usage: node apps/prism/test-endpoints.mjs   Exit 0 = every arm passed.
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, readdirSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const BRIDGE_PORT = 8790, DEAD_PORT = 8799;

const freePort = () => new Promise((resolve) => {
  const s = createServer();
  s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => resolve(p)); });
});
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

class Cdp {
  constructor(ws) { this.ws = ws; this.id = 0; this.waiting = new Map(); }
  static async open(port) {
    let url = null;
    for (let i = 0; i < 60 && !url; i++) {
      try {
        const r = await fetch(`http://127.0.0.1:${port}/json/list`);
        const t = (await r.json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl);
        if (t) { url = t.webSocketDebuggerUrl; break; }
      } catch {}
      await sleep(250);
    }
    if (!url) throw new Error('no CDP target');
    const ws = new WebSocket(url);
    await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
    const cdp = new Cdp(ws);
    ws.addEventListener('message', (ev) => {
      const m = JSON.parse(ev.data);
      if (m.id && cdp.waiting.has(m.id)) { cdp.waiting.get(m.id)(m); cdp.waiting.delete(m.id); }
    });
    return cdp;
  }
  send(method, params = {}) {
    const id = ++this.id;
    return new Promise((res) => { this.waiting.set(id, res); this.ws.send(JSON.stringify({ id, method, params })); });
  }
  async eval(expression) {
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  }
}

let chrome = null, server = null, bridge = null, profile = null, root = null;
try {
  root = mkdtempSync(join(tmpdir(), 'prism-ep-root-'));
  bridge = spawn('pwsh', ['-NoProfile', '-File', join(repo, 'apps', 'prism', 'build-bridge.ps1'), '-Port', String(BRIDGE_PORT), '-Root', root], { stdio: ['ignore', 'pipe', 'pipe'] });
  let bout = '';
  bridge.stdout.on('data', d => { bout += d.toString(); });
  let token = null;
  for (let i = 0; i < 80 && !token; i++) { const m = /token\s+(\S+)/.exec(bout); if (m) token = m[1]; else await sleep(250); }
  if (!token) throw new Error('bridge printed no token: ' + bout);

  const deployed = readFileSync(join(repo, 'apps', 'landing', 'web', 'compile', 'prism.html'), 'utf8');
  const embedAt = deployed.indexOf('<script>');
  const embed = deployed.slice(embedAt, deployed.indexOf('</script>', embedAt) + 9);
  const template = readFileSync(join(repo, 'codex', 'plugs', 'wasm', 'page', 'prism.html'), 'utf8');
  if (!embed.includes('window.__EMBED') || template.split('<!--EMBED-->').length !== 2) throw new Error('cannot splice the page');
  const html = template.replace('<!--EMBED-->', () => embed);
  const httpPort = await freePort();
  server = http.createServer((q, s) => { s.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); s.end(html); });
  await new Promise(r => server.listen(httpPort, '127.0.0.1', r));

  const cdpPort = await freePort();
  profile = mkdtempSync(join(tmpdir(), 'prism-ep-chrome-'));
  chrome = spawn(chromePath, ['--headless=new', '--no-first-run', '--no-default-browser-check',
    `--remote-debugging-port=${cdpPort}`, `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
  const cdp = await Cdp.open(cdpPort);
  await cdp.send('Page.enable'); await cdp.send('Runtime.enable');
  const url = `http://127.0.0.1:${httpPort}/prism.html`;
  await cdp.send('Page.navigate', { url });
  await sleep(3000);

  const eps = (autoAll) => JSON.stringify([
    { name: 'box1', url: `http://127.0.0.1:${BRIDGE_PORT}`, token, cmd: 'Get-FileHash {file} | ForEach-Object Hash | Set-Content hash-{stem}.txt', auto: autoAll },
    { name: 'badtok', url: `http://127.0.0.1:${BRIDGE_PORT}`, token: 'wrong', cmd: 'echo never', auto: autoAll },
    { name: 'dead', url: `http://127.0.0.1:${DEAD_PORT}`, token: 'x', cmd: '', auto: autoAll },
    { name: 'off', url: `http://127.0.0.1:${BRIDGE_PORT}`, token, cmd: 'Set-Content off-ran.txt yes', auto: false }]);
  await cdp.eval(`localStorage.setItem('prism.endpoints', ${JSON.stringify(eps(true))}); location.reload(); true`);
  await sleep(3000);

  const waitEp = `(async () => { for (let i = 0; i < 480; i++) { const t = document.getElementById('ep-status').textContent; if (t && t.indexOf('endpoints') < 0 && t.indexOf('box1') >= 0) return t; await new Promise(r => setTimeout(r, 500)); } return 'TIMEOUT: ' + document.getElementById('ep-status').textContent + ' / ' + document.getElementById('status').textContent; })()`;

  // ARM 1: a binary (CDX) build is written byte-exact and box1's command runs on it.
  await cdp.eval(`document.getElementById('ep-status').textContent = ''; document.querySelector('[data-tab="binary"]').click(); document.querySelector('[data-bin="cdx"]').click(); document.getElementById('go').click(); true`);
  const s1 = await cdp.eval(waitEp);
  console.log('  status after binary build: ' + s1);
  const bin = await cdp.eval(`(async () => emittedBin ? { name: emittedBin.name, len: emittedBin.bytes.length, sha: Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', emittedBin.bytes)), b => b.toString(16).padStart(2, '0')).join('') } : null)()`);
  ok('the page built a CDX', !!bin, bin ? `${bin.name}, ${bin.len} bytes` : 'no emittedBin');
  if (bin) {
    const onDisk = join(root, bin.name);
    const sha = existsSync(onDisk) ? createHash('sha256').update(readFileSync(onDisk)).digest('hex') : 'missing';
    ok('box1 received the CDX byte-exact', sha === bin.sha, sha === bin.sha ? sha.slice(0, 16) : `disk ${sha.slice(0, 16)} page ${bin.sha.slice(0, 16)}`);
    const hf = join(root, 'hash-' + bin.name.replace(/\.[^.]*$/, '') + '.txt');
    const h = existsSync(hf) ? readFileSync(hf, 'utf8').trim().toLowerCase() : 'missing';
    ok("box1's command ran on the artifact there", h === bin.sha, h.slice(0, 16));
  }
  ok('box1 reports built', /box1: built in \d+ ms/.test(s1));
  ok('a wrong token is reported, not hidden', /badtok: write refused: refused the token/.test(s1));
  ok('an absent endpoint is reported, not hidden', /dead: did not answer/.test(s1));
  ok('an endpoint set to off is not contacted', !/off:/.test(s1) && !existsSync(join(root, 'off-ran.txt')));
  ok('the build itself stays ok', (await cdp.eval(`document.getElementById('status').textContent`)) === 'ok');

  // ARM 2: a text lens build travels as text.
  await cdp.eval(`document.getElementById('ep-status').textContent = ''; document.querySelector('[data-tab="scripts"]').click(); document.querySelector('[data-plug="python"]').click(); document.getElementById('go').click(); true`);
  const s2 = await cdp.eval(waitEp);
  console.log('  status after python build: ' + s2);
  const txt = await cdp.eval(`({ name: outName(), text: lastRun.body.join('\\n') })`);
  const tf = join(root, txt.name);
  const disk = existsSync(tf) ? readFileSync(tf, 'utf8') : null;
  ok('box1 received the emitted python as text', disk !== null && disk === txt.text, `${txt.name}, ${txt.text.length} chars`);

  // CONTROL: every endpoint off, a build writes nothing anywhere.
  for (const f of readdirSync(root)) rmSync(join(root, f), { force: true, recursive: true });
  await cdp.eval(`localStorage.setItem('prism.endpoints', ${JSON.stringify(eps(false))}); true`);
  await cdp.eval(`document.getElementById('ep-status').textContent = 'x'; document.getElementById('go').click(); true`);
  await cdp.eval(`(async () => { for (let i = 0; i < 240; i++) { if (document.getElementById('status').textContent === 'ok' && document.getElementById('ep-status').textContent === '') return; await new Promise(r => setTimeout(r, 500)); } })()`);
  const left = readdirSync(root);
  ok('control: all endpoints off writes nothing', left.length === 0 && (await cdp.eval(`document.getElementById('ep-status').textContent`)) === '', left.join(','));
} catch (e) {
  ok('ran to the end', false, String(e && e.message || e));
} finally {
  try { chrome && chrome.kill(); } catch {}
  try { server && server.close(); } catch {}
  try { bridge && bridge.kill(); } catch {}
  await sleep(500);
  try { profile && rmSync(profile, { recursive: true, force: true }); } catch {}
  try { root && rmSync(root, { recursive: true, force: true }); } catch {}
}
const failed = arms.filter(x => !x).length;
console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
process.exit(failed ? 1 : 0);
