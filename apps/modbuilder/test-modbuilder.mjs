import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

class Cdp {
  constructor(ws) { this.ws = ws; this.id = 0; this.waiting = new Map(); this.errors = []; }
  static async open(port) {
    let url = null;
    for (let i = 0; i < 60 && !url; i++) {
      try { const t = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
      if (!url) await sleep(250);
    }
    if (!url) throw new Error('no CDP target');
    const ws = new WebSocket(url);
    await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
    const cdp = new Cdp(ws);
    ws.addEventListener('message', (ev) => {
      const m = JSON.parse(ev.data);
      if (m.id && cdp.waiting.has(m.id)) { cdp.waiting.get(m.id)(m); cdp.waiting.delete(m.id); }
      if (m.method === 'Runtime.exceptionThrown') cdp.errors.push(JSON.stringify(m.params.exceptionDetails).slice(0, 300));
    });
    return cdp;
  }
  send(method, params = {}) { const id = ++this.id; return new Promise((res) => { this.waiting.set(id, res); this.ws.send(JSON.stringify({ id, method, params })); }); }
  async eval(expression) {
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  }
}

let edge = null, server = null, profile = null, work = null, bridge = null;
try {
  work = mkdtempSync(join(tmpdir(), 'modbuilder-'));
  let compiler = process.argv[2];
  if (!compiler) {
    const m = /"codex-compiler\.wasm"\s*:\s*"([A-Za-z0-9+/=]+)"/.exec(readFileSync(join(repo, 'apps', 'landing', 'web', 'compile', 'prism.html'), 'utf8'));
    if (!m) throw new Error('no compiler module in the deployed Prism page');
    compiler = join(work, 'codex-compiler.wasm');
    writeFileSync(compiler, Buffer.from(m[1], 'base64'));
  }
  const page = join(work, 'modbuilder.html');
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'apps', 'modbuilder', 'build-page.ps1'), '-CompilerWasm', compiler, '-OutFile', page], { stdio: 'inherit' });
  const mb = readFileSync(page, 'utf8');
  const featureCount = JSON.parse(/window\.__DATA = (\{[\s\S]*?\});<\/script>/.exec(mb)[1]).mods[0].features.length;

  const requests = [];
  let failTest = false;
  const bridgePort = await freePort();
  bridge = http.createServer((q, r) => {
    const cors = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Content-Type, X-Bridge-Token', 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS' };
    if (q.method === 'OPTIONS') { r.writeHead(204, cors); r.end(); return; }
    let body = ''; q.on('data', c => body += c); q.on('end', () => {
      if (q.url === '/health') { r.writeHead(200, Object.assign({ 'Content-Type': 'application/json' }, cors)); r.end(JSON.stringify({ ok: true, tokenOk: q.headers['x-bridge-token'] === 'tok' })); return; }
      const j = JSON.parse(body || '{}'); requests.push({ token: q.headers['x-bridge-token'], body: j });
      const op = j.operation;
      const res = op === 'package' ? { ok: true, operation: op, artifact: 'C:/fake/winhttp.dll', buildNumber: '7' }
        : op === 'test' ? (failTest ? { ok: false, operation: op, err: 'probe FAILED in the sealed copy' } : { ok: true, operation: op })
        : op === 'install' ? { ok: true, operation: op, installed: true, buildNumber: '7', installPath: 'D:/Play', backup: 'D:/Play/Support/Before-7' } : { ok: true, operation: op };
      r.writeHead(200, Object.assign({ 'Content-Type': 'application/json' }, cors)); r.end(JSON.stringify(res));
    });
  });
  await new Promise(r => bridge.listen(bridgePort, '127.0.0.1', r));

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); r.end(mb); });
  await new Promise(r => server.listen(port, '127.0.0.1', r));

  const cdpPort = await freePort();
  profile = mkdtempSync(join(tmpdir(), 'modbuilder-edge-'));
  edge = spawn(edgePath, ['--headless=new', '--no-first-run', '--no-default-browser-check', `--remote-debugging-port=${cdpPort}`, `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
  const cdp = await Cdp.open(cdpPort);
  await cdp.send('Page.enable'); await cdp.send('Runtime.enable');

  const open = async (hash) => { await cdp.send('Page.navigate', { url: `http://127.0.0.1:${port}/modbuilder.html${hash || ''}` }); await sleep(2500); };
  const view = () => cdp.eval(`({ on: Array.from(document.querySelectorAll('.card.on')).map(c => c.id.slice(5)), cards: document.querySelectorAll('.card').length, count: document.getElementById('count').textContent, status: document.getElementById('status').textContent })`);
  await open('');
  let v = await view();
  ok('the page shows every catalogue feature as a card, all chosen', v.cards === featureCount && v.on.length === featureCount && v.count === `${featureCount} of ${featureCount} powers chosen`, JSON.stringify(v));

  await cdp.eval(`document.getElementById('card-rows').click()`);
  v = await view();
  ok('clicking a card toggles it off', v.on.length === featureCount - 1 && !v.on.includes('rows'), JSON.stringify(v.on));

  await open('#mod=valheim:rows'); v = await view();
  ok('#mod=valheim:rows selects planting in rows alone', JSON.stringify(v.on) === '["rows"]' && v.count === `1 of ${featureCount} powers chosen`, JSON.stringify(v));

  const code = await cdp.eval(`(async () => { state_set_text('code', ''); state_set_text('job', 'probe'); state_set('busy', 0n); mba_emit(0n); for (let i = 0; i < 600 && state_get_text('code') === '' && !/refused|no IR|no library/.test(document.getElementById('status').textContent); i++) await new Promise(r => setTimeout(r, 100)); return state_get_text('code') || document.getElementById('status').textContent; })()`);
  ok('the rows mod emits PrismPlantRows and no storage, in the browser', /class PrismPlantRows\b/.test(code) && /PrismPlantRows\.Install/.test(code) && !/class PrismStorage\b/.test(code), code.slice(0, 120));

  await open('#mod=valheim:nosuch'); v = await view();
  ok('control: an unknown feature is refused and the selection is unchanged', /has no feature nosuch/.test(v.status) && JSON.stringify(v.on) === '["rows"]', JSON.stringify(v));

  const setup = `localStorage.setItem('prism.bridge.url', 'http://127.0.0.1:${bridgePort}'); localStorage.setItem('prism.bridge.token', 'tok');`;
  const forge = `(async () => { document.getElementById('p-gamePath').value = 'D:/Game'; document.getElementById('p-testSavePath').value = 'D:/Saves'; document.getElementById('p-installPath').value = 'D:/Play';
    document.getElementById('a-forge').click(); for (let i = 0; i < 900 && state_get('busy') == 1n; i++) await new Promise(r => setTimeout(r, 100));
    const pip = (s) => document.getElementById('pip-' + s).className;
    return { status: document.getElementById('status').textContent, pips: [pip('package'), pip('test'), pip('install')], victory: document.getElementById('victory').className, title: document.getElementById('victory-title').textContent }; })()`;
  await cdp.eval(setup); await open('#mod=valheim:rows');
  const installBefore = await cdp.eval(`(() => { document.getElementById('a-install').click(); return { cls: document.getElementById('a-install').className, status: document.getElementById('status').textContent }; })()`);
  ok('Install waits for a build: it is marked off and refuses', /off/.test(installBefore.cls) && /Build the extension first/.test(installBefore.status) && requests.length === 0, JSON.stringify(installBefore));

  const done = await cdp.eval(forge);
  const pkg = requests.find(x => x.body.operation === 'package'), tst = requests.find(x => x.body.operation === 'test'), ins = requests.find(x => x.body.operation === 'install');
  ok('Forge & Install sends package with the token, the profile, the emitted C# and the features', pkg && pkg.token === 'tok' && pkg.body.profile.gamePath === 'D:/Game' && pkg.body.profile.installPath === 'D:/Play' && /class PrismPlantRows\b/.test(pkg.body.code) && JSON.stringify(pkg.body.features) === '["rows"]', JSON.stringify(pkg && pkg.body.features));
  ok('then test and install with the built artifact, in that order', tst && ins && tst.body.artifact === 'C:/fake/winhttp.dll' && ins.body.artifact === 'C:/fake/winhttp.dll' && requests.indexOf(tst) < requests.indexOf(ins), requests.map(x => x.body.operation).join(','));
  ok('the three runes finish lit and the victory card names the build', JSON.stringify(done.pips) === '["pip done","pip done","pip done"]' && /show/.test(done.victory) && /Build 7 is installed/.test(done.title), JSON.stringify(done));

  failTest = true; requests.length = 0;
  await open('#mod=valheim:rows');
  const failed = await cdp.eval(forge);
  ok('control: a failing trial stops the forge before any install', failed.pips[1] === 'pip fail' && /FAILED/.test(failed.status) && !requests.some(x => x.body.operation === 'install') && !/show/.test(failed.victory), JSON.stringify(failed));

  const handed = { version: 1, id: 'handed', kind: 'unity', profile: 'windows-x64-mono-6000.0.75f1', gamePath: 'D:/Game', testSavePath: 'D:/Saves', installPath: 'D:/Play', groupLimit: 3, workbenchLinks: false };
  await open('#bridge=handtok&profile=' + encodeURIComponent(JSON.stringify(handed)));
  const took = await cdp.eval(`({ tok: localStorage.getItem('prism.bridge.token'), id: document.getElementById('p-id').value, install: document.getElementById('p-installPath').value, limit: document.getElementById('p-groupLimit').value, links: document.getElementById('p-links').textContent, hash: location.hash })`);
  ok('the launcher fragment hands over the token and the profile, then leaves the address', took.tok === 'handtok' && took.id === 'handed' && took.install === 'D:/Play' && took.limit === '3' && took.links === 'Off' && took.hash === '', JSON.stringify(took));
  await open('#bridge=handtok2&profile=' + encodeURIComponent(JSON.stringify(Object.assign({}, handed, { id: 'same', installPath: 'D:/Game' }))));
  const refused = await cdp.eval(`({ status: document.getElementById('status').textContent, id: document.getElementById('p-id').value })`);
  ok('control: a handed profile installing into the original game is refused', /never the original game/.test(refused.status) && refused.id !== 'same', JSON.stringify(refused));
  ok('no uncaught page errors', cdp.errors.length === 0, cdp.errors.join(' | '));
} catch (e) {
  ok('ran to the end', false, String(e && e.message || e));
} finally {
  try { edge && edge.kill(); } catch {}
  try { server && server.close(); } catch {}
  try { bridge && bridge.close(); } catch {}
  await sleep(500);
  try { profile && rmSync(profile, { recursive: true, force: true }); } catch {}
  try { work && rmSync(work, { recursive: true, force: true }); } catch {}
}
const failed = arms.filter(x => !x).length;
console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
process.exit(failed ? 1 : 0);
