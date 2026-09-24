// PRISM-7 stage 2f: the compile page's Bench, driven in headless Chrome against
// a real build-bridge.ps1 with three saved toolchains: python (run only), zig
// (build then run) and a RIGGED ruby lane whose run prints 42 whatever the
// program, which the bench must mark as differing. The page under test is the TEMPLATE
// (codex/plugs/wasm/page/prism.html) with the deployed page's embedded modules
// spliced in at <!--EMBED-->, so a template edit is graded before any redeploy.
// Usage: node apps/prism/test-bench.mjs   Exit 0 = every arm passed.
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
const BRIDGE_PORT = 8792;

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
  root = mkdtempSync(join(tmpdir(), 'prism-bench-root-'));
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
  profile = mkdtempSync(join(tmpdir(), 'prism-bench-chrome-'));
  chrome = spawn(chromePath, ['--headless=new', '--no-first-run', '--no-default-browser-check',
    `--remote-debugging-port=${cdpPort}`, `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
  const cdp = await Cdp.open(cdpPort);
  await cdp.send('Page.enable'); await cdp.send('Runtime.enable');
  await cdp.send('Page.navigate', { url: `http://127.0.0.1:${httpPort}/prism.html` });
  await sleep(3000);
  const cfgs = {
    python: { name: 'python', build: '', run: 'python {file}' },
    zig: { name: 'zig', build: 'zig build-exe {file} -O ReleaseFast --name {stem}', run: './{stem}' },
    ruby: { name: 'rigged', build: '', run: 'Write-Output 42' }
  };
  await cdp.eval(`localStorage.setItem('prism.configs', ${JSON.stringify(JSON.stringify(cfgs))});
    localStorage.setItem('prism.bridge.url', 'http://127.0.0.1:${BRIDGE_PORT}');
    localStorage.setItem('prism.bridge.token', ${JSON.stringify(token)}); location.reload(); true`);
  await sleep(3000);
  await cdp.eval(`document.getElementById('bench').click(); document.getElementById('bench-go').click(); true`);
  const done = await cdp.eval(`(async () => { for (let i = 0; i < 1800; i++) { const t = document.getElementById('cfg-status').textContent; if (t === 'done' || t === 'stopped') return t; await new Promise(r => setTimeout(r, 500)); } return 'TIMEOUT'; })()`);
  ok('the bench ran to the end', done === 'done', done);
  const rows = await cdp.eval(`Array.from(document.querySelectorAll('#bench-table tr')).map(tr => Array.from(tr.children).map(td => ({ t: td.textContent, c: td.className, o: td.title })))`);
  const headRow = rows[0].map(c => c.t), body = rows.slice(1);
  console.log('  ' + headRow.join(' | '));
  for (const r of body) console.log('  ' + r.map(c => c.t).join(' | '));
  const col = (name) => headRow.indexOf(name);
  const want = (await cdp.eval(`examples.filter(e => e.cat === 'Bench').length`));
  ok('one row per Bench example', body.length === want && want > 0, `${body.length} of ${want}`);
  ok('lanes are the tab plus the three saved toolchains', JSON.stringify(headRow) === JSON.stringify(['program', 'in tab (javascript)', 'python', 'ruby', 'zig']), headRow.join(','));
  const timed = (c) => /^[0-9.,]+ ms$/.test(c.t) && c.c === 'ok';
  ok('every example runs in the tab', body.every(r => timed(r[1])), body.filter(r => !timed(r[1])).map(r => r[0].t + ': ' + r[1].t).join('; '));
  ok('python agrees with the tab on every example', body.every(r => timed(r[col('python')])), body.filter(r => !timed(r[col('python')])).map(r => r[0].t + ': ' + r[col('python')].t).join('; '));
  ok('zig builds and agrees with the tab on every example', body.every(r => timed(r[col('zig')])), body.filter(r => !timed(r[col('zig')])).map(r => r[0].t + ': ' + r[col('zig')].t).join('; '));
  ok('control: the rigged lane is marked as differing on every example', body.every(r => /output differs/.test(r[col('ruby')].t) && r[col('ruby')].c === 'err'));
  ok('the tab lane carries real output', body.every(r => r[1].o.length > 0 && r[1].o !== '42'));

  // The Run button shares the in-tab runner with the bench.
  await cdp.eval(`document.getElementById('cfg-close').click(); document.querySelector('[data-tab="scripts"]').click(); document.querySelector('[data-plug="javascript"]').click(); document.getElementById('go').click(); true`);
  const ran = await cdp.eval(`(async () => { for (let i = 0; i < 240; i++) { const b = document.getElementById('run'); if (b && !b.disabled && document.getElementById('status').textContent === 'ok') { b.click(); return document.getElementById('runout').textContent; } await new Promise(r => setTimeout(r, 500)); } return 'TIMEOUT'; })()`);
  ok('the Run button still runs the javascript lens in the tab', /ran in [0-9.,]+ ms \(in this tab\)/.test(ran) && !/printed nothing/.test(ran), ran.trim().split('\n')[0]);

  // Stage 6a: the WebAssembly lens offers Save .wasm, assembled in this tab.
  await cdp.eval(`document.querySelector('[data-plug="wasm"]').click(); document.getElementById('go').click(); true`);
  const wv = await cdp.eval(`(async () => { for (let i = 0; i < 240; i++) { if (document.getElementById('status').textContent === 'ok' && outText()) { const b = watAssemble(outText()); return { shown: document.getElementById('savewasm').style.display !== 'none', bytes: b.length, valid: WebAssembly.validate(b) }; } await new Promise(r => setTimeout(r, 500)); } return null; })()`);
  ok('the WebAssembly lens offers Save .wasm and the tab assembles a valid module', !!(wv && wv.shown && wv.valid), wv ? `${wv.bytes} bytes, valid ${wv.valid}` : 'no output');
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