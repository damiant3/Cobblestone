// The game-mods page and Prism's #mod= composition, in headless
// Chrome. The catalogue and mods.html come from build-page.ps1's OWN mods block
// (run here on its own, so the code graded is the code shipped); Prism is the
// template with the deployed page's embedded modules plus that catalogue. The
// acceptance is the Unity C# a composed mod emits: exactly the chosen features.
// Usage: node apps/modbuilder/test-mods.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

class Cdp {
  constructor(ws) { this.ws = ws; this.id = 0; this.waiting = new Map(); }
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
      // A composed mod replaces the workspace behind a confirm; accept it.
      if (m.method === 'Page.javascriptDialogOpening') cdp.send('Page.handleJavaScriptDialog', { accept: true });
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

let chrome = null, server = null, profile = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'prism-mods-'));
  // build-page.ps1's mods block, run on its own.
  const bp = readFileSync(join(repo, 'codex', 'plugs', 'wasm', 'build-page.ps1'), 'utf8');
  const s = bp.indexOf('# 3h.'), e = bp.indexOf('\n', bp.indexOf('Write-Host ("[page] mods'));
  if (s < 0 || e < s) throw new Error('no mods block in build-page.ps1');
  const block = bp.slice(s, e).replace(/\$PSScriptRoot/g, "'" + join(repo, 'codex', 'plugs', 'wasm') + "'");
  const ps = `$ErrorActionPreference='Stop'; $Repo='${repo}'; $OutDir='${work}'\n` + block + `\n[IO.File]::WriteAllText((Join-Path $OutDir 'mods.json'), $modsJson)`;
  writeFileSync(join(work, 'block.ps1'), ps);
  execFileSync('pwsh', ['-NoProfile', '-File', join(work, 'block.ps1')], { stdio: 'ignore' });
  const modsJson = readFileSync(join(work, 'mods.json'), 'utf8');
  const modsHtml = readFileSync(join(work, 'mods.html'), 'utf8');
  ok("build-page's block writes mods.html with the catalogue", modsHtml.includes('window.__MODS = ') && !modsHtml.includes('<!--MODS-->'));

  const deployed = readFileSync(join(repo, 'apps', 'landing', 'web', 'compile', 'prism.html'), 'utf8');
  const at = deployed.indexOf('<script>'), end = deployed.indexOf('</script>', at);
  const embed = deployed.slice(at, end) + 'window.__MODS = ' + modsJson + ';\n</script>';
  const template = readFileSync(join(repo, 'codex', 'plugs', 'wasm', 'page', 'prism.html'), 'utf8');
  const prism = template.replace('<!--EMBED-->', () => embed);
  const port = await freePort();
  server = http.createServer((q, r) => {
    const body = q.url.startsWith('/mods.html') ? modsHtml : prism;
    r.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); r.end(body);
  });
  await new Promise(r => server.listen(port, '127.0.0.1', r));

  const cdpPort = await freePort();
  profile = mkdtempSync(join(tmpdir(), 'prism-mods-chrome-'));
  chrome = spawn(chromePath, ['--headless=new', '--no-first-run', '--no-default-browser-check', `--remote-debugging-port=${cdpPort}`, `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
  const cdp = await Cdp.open(cdpPort);
  await cdp.send('Page.enable'); await cdp.send('Runtime.enable');

  await cdp.send('Page.navigate', { url: `http://127.0.0.1:${port}/mods.html` });
  await sleep(1500);
  const cards = await cdp.eval(`Array.from(document.querySelectorAll('.game')).map(c => ({ game: c.dataset.game, features: c.querySelectorAll('input').length }))`);
  ok('the mods page lists Valheim with its three features', JSON.stringify(cards) === JSON.stringify([{ game: 'valheim', features: 3 }]), JSON.stringify(cards));
  const link = await cdp.eval(`(() => { const c = document.querySelector('[data-game="valheim"]'); for (const id of ['storage', 'meadows']) c.querySelector('input[value="' + id + '"]').click(); return { href: c.querySelector('a.go').getAttribute('href'), count: c.querySelector('.count').textContent }; })()`);
  ok('choosing the arc alone links Prism with #mod=valheim:arc', link.href === 'prism.html#mod=valheim:arc' && link.count === '1 of 3 features', link.href + ' / ' + link.count);
  await cdp.eval(`document.querySelector('[data-game="valheim"] a.go').click(); true`);
  await sleep(4000);

  const emitFor = async () => cdp.eval(`(async () => { const files = project.files.map(f => f.path); const r = await targetEmit(targetDefaults('unity')); const cs = new TextDecoder().decode(r.bytes); return { files, storage: /class PrismStorage\\b/.test(cs), meadows: /class PrismMeadowsSpawns\\b/.test(cs), arc: /class PrismCircumhorizontalArc\\b/.test(cs) }; })()`);
  const one = await emitFor();
  ok('the link composes the arc source and an entry', JSON.stringify(one.files) === JSON.stringify(['CircumhorizontalArc.codex', 'ValheimMod.codex']), one.files.join(','));
  ok('the arc-only mod emits the arc and nothing else', one.arc && !one.storage && !one.meadows, JSON.stringify(one));
  const sel1 = await cdp.eval(`JSON.stringify(project.mod)`);
  ok('the composed project carries its selection for a Targets request', sel1 === JSON.stringify({ game: 'valheim', features: ['arc'] }), sel1);

  await cdp.send('Page.navigate', { url: `http://127.0.0.1:${port}/prism.html#mod=valheim:storage,meadows` });
  await sleep(4000);
  const two = await emitFor();
  ok('storage and meadows emit those two and not the arc', two.storage && two.meadows && !two.arc, JSON.stringify(two));
  const sel2 = await cdp.eval(`JSON.stringify(project.mod)`);
  ok('a second composition replaces the selection', sel2 === JSON.stringify({ game: 'valheim', features: ['storage', 'meadows'] }), sel2);

  await cdp.send('Page.navigate', { url: `http://127.0.0.1:${port}/prism.html#mod=valheim:nosuch` });
  await sleep(3000);
  const bad = await cdp.eval(`({ status: document.getElementById('status').textContent, files: project.files.map(f => f.path) })`);
  ok('control: an unknown feature is refused and the project is unchanged', /has no feature nosuch/.test(bad.status) && JSON.stringify(bad.files) === JSON.stringify(two.files), bad.status + ' / ' + bad.files.join(','));
} catch (e) {
  ok('ran to the end', false, String(e && e.message || e));
} finally {
  try { chrome && chrome.kill(); } catch {}
  try { server && server.close(); } catch {}
  await sleep(500);
  try { profile && rmSync(profile, { recursive: true, force: true }); } catch {}
  try { work && rmSync(work, { recursive: true, force: true }); } catch {}
}
const failed = arms.filter(x => !x).length;
console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
process.exit(failed ? 1 : 0);
