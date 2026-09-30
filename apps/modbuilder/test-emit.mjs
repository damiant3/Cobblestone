// The ModBuilder page's own emission, driven through its player flow in headless
// Edge: manual build, the feature checkboxes, Create project, Compile and build.
// Every feature alone must install itself and no other feature (a class it
// cites, as tools cites PlantRows, is emitted and not installed); all features
// together must install all of them; the Unity module must refuse a foreign
// profile and text that is not IR. The emitted libraries land in
// build-output/prism-targets as mod-<feature>.cs and mod-all.cs, the
// -UnitySource inputs of test-world.ps1 and test-plants.ps1.
// Usage: node apps/modbuilder/test-emit.mjs [workspace.html]   Exit 0 = every arm passed.
import { spawn } from 'node:child_process';
import http from 'node:http';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const repo = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const page = resolve(process.argv[2] || join(repo, 'apps/modbuilder/web/workspace.html'));
const out = join(repo, 'build-output/prism-targets'); mkdirSync(out, { recursive: true });
const wait = ms => new Promise(r => setTimeout(r, ms));
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
let edge, ws, profile;
try {
  const probe = http.createServer(); await new Promise(r => probe.listen(0, '127.0.0.1', r));
  const debugPort = probe.address().port; await new Promise(r => probe.close(r));
  profile = mkdtempSync(join(tmpdir(), 'modbuilder-emit-'));
  edge = spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', ['--headless=new', '--no-first-run', '--no-default-browser-check', `--remote-debugging-port=${debugPort}`, `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore', windowsHide: true });
  let target;
  for (let i = 0; i < 80 && !target; i++) { try { target = (await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json()).find(t => t.type === 'page'); } catch {} if (!target) await wait(250); }
  if (!target) throw new Error('Edge debug target unavailable');
  ws = new WebSocket(target.webSocketDebuggerUrl); await new Promise(r => ws.addEventListener('open', r));
  let next = 0; const pending = new Map();
  ws.addEventListener('message', e => { const m = JSON.parse(e.data); if (m.id) { pending.get(m.id)?.(m); pending.delete(m.id); } });
  const send = (method, params = {}) => new Promise(r => { const id = ++next; pending.set(id, r); ws.send(JSON.stringify({ id, method, params })); });
  const evaluate = async expression => { const m = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); if (m.error || m.result?.exceptionDetails) throw new Error(JSON.stringify(m).slice(0, 2000)); return m.result.result.value; };
  const until = async (expression, tries = 600) => { for (let i = 0; i < tries; i++) { if (await evaluate(expression)) return; await wait(250); } throw new Error('Timed out: ' + expression); };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Page.addScriptToEvaluateOnNewDocument', { source: 'window.confirm=()=>true;' });
  await send('Page.navigate', { url: pathToFileURL(page).href });
  await until(`typeof project!=='undefined' && !!document.getElementById('mb-build-without-helper') && !!window.__VALHEIM`);
  await evaluate(`document.getElementById('mb-build-without-helper').click()`);

  const unity = `await moduleBytes('unity-stdio.wasm')`;
  ok('the Unity module refuses a foreign profile', await evaluate(`(async()=>/^REFUSED/.test((await runW(${unity},'UNITY future\\n(chapter "Bad")')).text))()`));
  ok('the Unity module refuses text that is not IR', await evaluate(`(async()=>/^REFUSED/.test((await runW(${unity},plugInput('unity','not IR'))).text))()`));

  const features = await evaluate(`window.__VALHEIM.features.map(f=>({id:f.id,marker:f.marker}))`);
  const markers = features.map(f => f.marker);
  const emit = async ids => {
    await evaluate(`(()=>{for(const box of document.querySelectorAll('#mb-feature-options input'))box.checked=${JSON.stringify(ids)}.includes(box.value);document.getElementById('mb-create-project').click();document.getElementById('mb-compile-project').click();})()`);
    await until(`!document.getElementById('mb-compile-project').disabled`);
    return await evaluate(`({code:outText(),status:document.getElementById('mb-project-status').textContent})`);
  };
  for (const f of features) {
    const { code, status } = await emit([f.id]);
    const others = markers.filter(m => m !== f.marker && new RegExp(m + '\\.Install\\b').test(code));
    ok(`${f.id} alone installs ${f.marker} and no other feature`, new RegExp('class ' + f.marker + '\\b').test(code) && new RegExp(f.marker + '\\.Install').test(code) && /class PrismModIdentity\b/.test(code) && others.length === 0, others.join(',') || (code ? '' : status));
    writeFileSync(join(out, `mod-${f.id}.cs`), code || '');
  }
  const all = await emit(features.map(f => f.id));
  const missing = markers.filter(m => !new RegExp(m + '\\.Install').test(all.code || ''));
  ok(`every feature together installs all ${markers.length}`, missing.length === 0, missing.join(',') || (all.code ? '' : all.status));
  writeFileSync(join(out, 'mod-all.cs'), all.code || '');

  await evaluate(`startProject({title:'Unicode control',main:'Control.codex',files:{'Control.codex':'Chapter: UnicodeControl\\n\\n  opening : Text = "h\\u00e9llo"\\n'}});chooseUnity();goBtn.click()`);
  await until(`!goBtn.disabled`);
  const uni = await evaluate(`outText()`) || '';
  const line = uni.split('\n').find(l => l.includes('public static string opening()')) || '';
  // A Text literal is CCE in the emitted C# and _Cce.ToUnicode converts it at
  // the boundary, so "héllo" is the CCE units 20 97 23 23 16 (é is tier-0 CCE 97).
  ok('a non-ASCII literal reaches C# as its CCE units (MB-14)', line.includes('"\\u0014\\u0061\\u0017\\u0017\\u0010"'), line.trim().slice(0, 160));
} catch (e) {
  ok('ran to the end', false, String(e && e.stack || e));
} finally {
  try { ws?.close(); } catch {}
  try { edge?.kill(); } catch {}
  await wait(500);
  try { if (profile) rmSync(profile, { recursive: true, force: true }); } catch {}
  const failed = arms.filter(x => !x).length;
  console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
  process.exitCode = failed ? 1 : 0;
}
