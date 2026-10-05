// Spark Studio step 1's first acceptance line (apps/spark/SparkStudio.md,
// section 9): the WPF original's own project opens in the compiled page
// apps/spark/SparkStudioPage.codex with every prompt and catalog record
// listed and every file found through section 6's path rule. Headless Edge
// answers the folder chooser with the project folder; the counts the page
// reports are checked against counts this script takes from the files.
// Usage: node apps/spark/studio-page.mjs [--project <dir>]   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const project = resolve(arg('--project', 'D:\\Projects\\Spark\\Spark'));
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

// What the page must report, from the files. The prompt count is the
// original's PromptParser regex; the path rule takes a record's path from its
// "Concept" component on, relative to the project folder.
const pj = existsSync(join(project, 'spark_project.json')) ? JSON.parse(readFileSync(join(project, 'spark_project.json'), 'utf8')) : {};
const promptsText = readFileSync(join(project, pj.promptsFile || 'ArtPrompts.txt'), 'utf8');
const wantPrompts = [...promptsText.matchAll(/PROMPT\s+(\d+)\s*\p{Pd}\s*"([^"]+)"/gu)].length;
const catalog = JSON.parse(readFileSync(join(project, pj.outputDir || 'Concept', 'catalog.json'), 'utf8'));
const rel = (p) => { const s = p.replace(/\\/g, '/'); const m = s.match(/(^|\/)(Concept\/.*)$/); return m ? m[2] : null; };
const wantFound = catalog.filter(r => { const q = rel(r.filePath || ''); return q && existsSync(join(project, q)); }).length;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'spark-studio-page-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'apps', 'spark', 'SparkStudioPage.codex'), '-Out', join(work, 'page.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'page.codex'), '-Out', join(work, 'page.html')]);
  const html = readFileSync(join(work, 'page.html'), 'utf8');

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });

  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map();
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') { chooser++; send('DOM.setFileInputFiles', { files: [project], backendNodeId: m.params.backendNodeId }); }
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  let chooser = 0; const errors = [];
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };

  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; } catch {} }
  await evalIn('window.showDirectoryPicker = undefined, 1');
  await send('Runtime.evaluate', { expression: 'document.getElementById("open").click(), 1', awaitPromise: true, returnByValue: true, userGesture: true });
  for (let i = 0; i < 480; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  const status = await evalIn('document.getElementById("status").textContent');
  const cards = await evalIn('document.querySelectorAll("#prompts > div").length');

  ok('the page threw nothing', errors.length === 0, errors.slice(0, 3).map(e => String(e).split('\n')[0]).join(' | '));
  ok('the page finished reading the project', st.done === 1, `chooser opened ${chooser}; ${String(status).slice(0, 200)}`);
  ok('every prompt is listed', st.prompts === wantPrompts && cards === wantPrompts, `page ${st.prompts} prompts, ${cards} cards; the file holds ${wantPrompts}`);
  ok('every catalog record is read', st.records === catalog.length, `page ${st.records}; catalog.json holds ${catalog.length}`);
  ok('every file the path rule reaches is found', st.found === wantFound && wantFound > 0, `page ${st.found}; on disk ${wantFound}`);

  // Thumbnails and the lightbox: every found, undeleted file is shown as an
  // image that decoded; a click opens it large, the arrow keys step and wrap,
  // Escape closes.
  const wantShown = catalog.filter(r => !r.deletedUtc && !r.legacyDeleted && (() => { const q = rel(r.filePath || ''); return q && existsSync(join(project, q)); })()).length;
  await sleep(1500);
  const thumbs = await evalIn('JSON.stringify([...document.querySelectorAll("img[id^=t]")].map(i => i.complete && i.naturalWidth > 0))').then(JSON.parse);
  ok('every shown file is a thumbnail that decoded', thumbs.length === wantShown && thumbs.every(Boolean) && st.shown === wantShown, `${thumbs.filter(Boolean).length} of ${thumbs.length} decoded; page shows ${st.shown}; want ${wantShown}`);
  await send('Runtime.evaluate', { expression: 'document.getElementById("t0").click(), 1', userGesture: true });
  await sleep(800);
  const open = await evalIn('JSON.stringify({d: getComputedStyle(document.getElementById("lb")).display, w: document.getElementById("lb-img").naturalWidth, c: document.getElementById("lb-cap").textContent})').then(JSON.parse);
  ok('a thumbnail click opens the lightbox on a decoded image', open.d === 'flex' && open.w > 0 && open.c.startsWith(`1 of ${wantShown}`), JSON.stringify(open));
  const order = [...promptsText.matchAll(/PROMPT\s+(\d+)\s*\p{Pd}\s*"([^"]+)"/gu)].map(m => Number(m[1]));
  const shown = (r) => !r.deletedUtc && !r.legacyDeleted && (() => { const q = rel(r.filePath || ''); return q && existsSync(join(project, q)); })();
  const first = order.map(n => catalog.find(r => r.promptNumber === n && shown(r))).find(Boolean);
  const detail = await evalIn('document.getElementById("lb-detail").textContent');
  const head = (first?.promptText || '').slice(0, 40);
  ok('the lightbox shows what made the image', !!first && detail.includes(`seed ${first.seed}`) && detail.includes(`preset ${first.refinePreset}`) && detail.includes(head), `${String(detail).split('\n').slice(0, 2).join(' / ')}; want seed ${first?.seed}, "${head}"`);
  const key = async (k) => { for (const type of ['keyDown', 'keyUp']) await send('Input.dispatchKeyEvent', { type, key: k, code: /^\d$/.test(k) ? 'Digit' + k : k, text: /^\d$/.test(k) && type === 'keyDown' ? k : undefined, windowsVirtualKeyCode: k === 'ArrowRight' ? 39 : k === 'ArrowLeft' ? 37 : /^\d$/.test(k) ? 48 + Number(k) : 27 }); await sleep(300); return evalIn('document.getElementById("lb-cap").textContent'); };
  const right = await key('ArrowRight');
  const left2 = (await key('ArrowLeft'), await key('ArrowLeft'));
  ok('the arrow keys step and wrap', right.startsWith(`2 of ${wantShown}`) && left2.startsWith(`${wantShown} of ${wantShown}`), `${right} / ${left2}`);
  await key('Escape');
  ok('Escape closes the lightbox', await evalIn('getComputedStyle(document.getElementById("lb")).display') === 'none');

  // Music and sound effects: every track the original lists (not deleted) is
  // shown, found in the project's Music or SoundFX folder, and plays for the
  // length its WAV header gives.
  for (let i = 0; i < 120; i++) { if (await evalIn('Number(_st["audio-done"])') === 1) break; await sleep(250); }
  const wavSeconds = (p) => { const b = readFileSync(p); let o = 12, rate = 0, align = 0, data = 0; while (o + 8 <= b.length) { const id = b.toString('ascii', o, o + 4), n = b.readUInt32LE(o + 4); if (id === 'fmt ') { rate = b.readUInt32LE(o + 12); align = b.readUInt16LE(o + 20); } if (id === 'data') { data = n; break; } o += 8 + n + (n & 1); } return data / (rate * align); };
  for (const [k, dir, file] of [['mus', 'Music', '.music_catalog.json'], ['sfx', 'SoundFX', '.sfx_catalog.json']]) {
    const tracks = JSON.parse(readFileSync(join(project, dir, file), 'utf8')).filter(t => !t.deleted);
    const want = tracks.map(t => { const f = join(project, dir, (t.filePath || '').split(/[\\/]/).pop()); return existsSync(f) ? wavSeconds(f) : null; });
    await evalIn(`Promise.all([...document.querySelectorAll('#${k}-list audio')].map(a => a.readyState >= 1 ? 1 : new Promise(r => { a.addEventListener('loadedmetadata', r, { once: true }); a.addEventListener('error', r, { once: true }); setTimeout(r, 5000); }))).then(() => 1)`);
    const got = await evalIn(`[...document.querySelectorAll('#${k}-list > div')].map(d => { const a = d.querySelector('audio'); return a ? a.duration : null; })`) || [];
    const bad = want.map((w, i) => (w === null) === (got[i] === null) && (w === null || Math.abs(w - got[i]) < 0.01) ? '' : `${i}: page ${got[i]}, WAV ${w}`).filter(Boolean);
    ok(`the ${dir} tab lists every track and plays it for its WAV's length`, tracks.length > 0 && got.length === tracks.length && want.some(w => w !== null) && bad.length === 0, `${got.length} of ${tracks.length} shown, ${got.filter(x => x !== null).length} playable${bad.length ? '; ' + bad.slice(0, 3).join('; ') : ''}`);
  }
  const mcfg = JSON.parse(readFileSync(join(project, 'music_config.json'), 'utf8'));
  const scfg = JSON.parse(readFileSync(join(project, 'sfx_config.json'), 'utf8'));
  const pick = (id, v) => evalIn(`(() => { const s = document.getElementById(${JSON.stringify(id)}); s.value = ${JSON.stringify(v)}; s.dispatchEvent(new Event('input')); return 1; })()`);
  await pick('mus-mood', mcfg.moodPresets[0]); await pick('mus-genre', mcfg.genres[3]); await pick('mus-family', Object.keys(mcfg.instruments)[1]); await pick('mus-inst', mcfg.instruments[Object.keys(mcfg.instruments)[1]][2]); await pick('mus-tempo', mcfg.tempoMarkings[6].label);
  const built = await evalIn('document.getElementById("mus-prompt").value');
  const wantBuilt = [mcfg.moodPresets[0], mcfg.genres[3], mcfg.instruments[Object.keys(mcfg.instruments)[1]][2], mcfg.tempoMarkings[6].label].join(', ');
  ok('a mood preset and picked tags build the music prompt as InjectTag does', built === wantBuilt, JSON.stringify(built));
  const preset = scfg.presets.find(p => p.category === 'Combat' && scfg.categoryDefaults.Combat);
  await pick('sfx-preset', preset.label);
  const applied = await evalIn('({ p: document.getElementById("sfx-prompt").value, c: document.getElementById("sfx-cat").value, d: document.getElementById("sfx-dur").value, t: document.getElementById("sfx-temp").value, g: document.getElementById("sfx-cfg").value })');
  const def = scfg.categoryDefaults.Combat;
  ok('an SFX preset sets its prompt, its category and that category\'s defaults', applied.p === preset.prompt && applied.c === 'Combat' && Number(applied.d) === def.duration && Number(applied.t) === def.temperature && Number(applied.g) === def.cfg, JSON.stringify(applied));
  const sfxAll = JSON.parse(readFileSync(join(project, 'SoundFX', '.sfx_catalog.json'), 'utf8')).filter(t => !t.deleted);
  await pick('sfx-cat', 'UI');
  const uiShown = await evalIn('document.querySelectorAll("#sfx-list > div").length');
  await pick('sfx-cat', 'Combat');
  const combatShown = await evalIn('document.querySelectorAll("#sfx-list > div").length');
  ok('the category filter shows only that category\'s sounds', uiShown === sfxAll.filter(t => t.category === 'UI').length && combatShown === sfxAll.filter(t => t.category === 'Combat').length && uiShown + combatShown <= sfxAll.length, `UI ${uiShown}, Combat ${combatShown} of ${sfxAll.length}`);
  await evalIn('document.getElementById("mus-go").click(), 1');
  const musNext = Math.max(0, ...JSON.parse(readFileSync(join(project, 'Music', '.music_catalog.json'), 'utf8')).filter(t => !t.deleted).map(t => t.id)) + 1;
  const refusal = await evalIn('document.getElementById("mus-status").textContent');
  ok('Generate refuses without a writable project and loaded model', /^refused: (Open a writable project|Load MusicGen model files first)/.test(refusal), refusal);
  // Writing back: a project in an origin-private folder stands in for the
  // directory picker a headless browser cannot answer; the page's own
  // file-write-then rewrites its catalog.json when a rating key is pressed.
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; } catch {} }
  const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';
  const recent = new Date(Date.now() - 3600000).toISOString();
  const cat0 = [{ id: 'r0', promptNumber: 1, title: 'Apple', filePath: 'C:\\x\\Proj\\Concept\\s\\prompt_01_old.png', settingsTag: 's', deletedUtc: '2026-01-01T00:00:00.0000000Z' }, { id: 'r1', promptNumber: 1, title: 'Apple', series: '', filePath: 'C:\\x\\Proj\\Concept\\s\\prompt_01_apple.png', settingsTag: 's', promptText: 'a red apple, anime cinematic', style: 'wide', seed: 7, refinePreset: 'none', rating: 0, seen: false, saved: false }, { id: 'r2', promptNumber: 1, title: 'Apple', filePath: 'C:\\x\\Proj\\Concept\\s\\prompt_01_apple.png', settingsTag: 's', deletedUtc: recent }];
  const mus0 = [1, 2, 3, 4].map(id => ({ id, title: `t${id}`, prompt: `p${id}`, filePath: `C:\\x\\Proj\\Music\\track_00${id}_t${id}.wav`, duration: 10, temperature: 1.2, cfgCoeff: 3, createdUtc: '2026-01-02T03:04:05Z', vibeTag: '', bpm: 90, rating: 0, saved: false, deleted: id === 4 }));
  await evalIn(`(async () => { const root = await navigator.storage.getDirectory(); try { await root.removeEntry('Proj', { recursive: true }); } catch {} const d = await root.getDirectoryHandle('Proj', { create: true });
    const put = async (h, name, data) => { const w = await (await h.getFileHandle(name, { create: true })).createWritable(); await w.write(data); await w.close(); };
    await put(d, 'ArtPrompts.txt', 'PROMPT 01 \\u2014 "Apple"\\n\\na red apple\\n');
    const c = await d.getDirectoryHandle('Concept', { create: true }); await put(c, 'catalog.json', ${JSON.stringify(JSON.stringify(cat0))}); await put(c, 'preferences.json', '{"weights":{"anime":1},"totalPositive":0,"totalNegative":0}');
    const s = await c.getDirectoryHandle('s', { create: true }); await put(s, 'prompt_01_apple.png', Uint8Array.from(atob('${png}'), ch => ch.charCodeAt(0))); await put(s, 'prompt_01_old.png', 'old');
    const mu = await d.getDirectoryHandle('Music', { create: true }); await put(mu, '.music_catalog.json', ${JSON.stringify(JSON.stringify(mus0))});
    window.showDirectoryPicker = async () => d; return 1; })()`);
  await send('Runtime.evaluate', { expression: 'document.getElementById("open").click(), 1', awaitPromise: true, returnByValue: true, userGesture: true });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('Number(_st.done) === 1')) break; }
  const writable = await evalIn('Number(_st.writable)');
  const purgedCat = JSON.parse(await evalIn(`(async () => { const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('Proj'); const c = await d.getDirectoryHandle('Concept'); return await (await (await c.getFileHandle('catalog.json')).getFile()).text(); })()`));
  const oldGone = await evalIn(`(async () => { const s = await (await (await (await navigator.storage.getDirectory()).getDirectoryHandle('Proj')).getDirectoryHandle('Concept')).getDirectoryHandle('s'); try { await s.getFileHandle('prompt_01_old.png'); return false; } catch { return true; } })()`);
  ok('opening purges a record deleted over 24 hours ago and its file, and keeps a recent one', purgedCat.map(r => r.id).join(',') === 'r1,r2' && oldGone === true && await evalIn('Number(_st.purged)') === 1, `ids ${purgedCat.map(r => r.id).join(',')}; old file gone ${oldGone}`);
  await send('Runtime.evaluate', { expression: 'document.getElementById("t0").click(), 1', userGesture: true });
  await sleep(500);
  await key('4');
  for (let i = 0; i < 40; i++) { await sleep(250); if (await evalIn('_st.rated !== undefined')) break; }
  const back = await evalIn(`(async () => { const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('Proj'); const c = await d.getDirectoryHandle('Concept'); return await (await (await c.getFileHandle('catalog.json')).getFile()).text(); })()`);
  let rec = null; try { rec = JSON.parse(back).find(r => r.id === 'r1'); } catch {}
  ok('the folder opens writable and a rating key rewrites catalog.json', writable === 1 && rec && rec.rating === 4 && rec.id === 'r1' && rec.seed === 7 && rec.filePath === cat0[1].filePath && JSON.parse(back).length === 2, `writable ${writable}; ${String(back).slice(0, 160)}`);
  // The rating flow: 4 then 2 on the same image backs the 4 out before the 2
  // goes in, and preferences.json and the record's signals follow.
  const readProj = (name) => evalIn(`(async () => { const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('Proj'); const c = await d.getDirectoryHandle('Concept'); return await (await (await c.getFileHandle('${name}')).getFile()).text(); })()`);
  const p4 = JSON.parse(await readProj('preferences.json'));
  const r4 = JSON.parse(await readProj('catalog.json')).find(r => r.id === 'r1');
  ok('rating 4 records a positive 1.5 and the record keeps its signals', p4.weights.anime === 2.5 && p4.weights.cinematic === 1.5 && p4.weights.wide === 1.5 && p4.totalPositive === 1 && JSON.stringify(r4.positiveSignals) === '["anime","cinematic","wide"]', JSON.stringify(p4) + ' ' + JSON.stringify(r4.positiveSignals));
  await evalIn('_st.rated = undefined, 1');
  await key('2');
  for (let i = 0; i < 40; i++) { await sleep(250); if (await evalIn('_st.rated !== undefined')) break; }
  const p2 = JSON.parse(await readProj('preferences.json'));
  const r2 = JSON.parse(await readProj('catalog.json')).find(r => r.id === 'r1');
  ok('re-rating 2 backs the 4 out, then records a negative 0.5', p2.weights.anime === 0.5 && p2.weights.cinematic === -0.5 && p2.weights.wide === -0.5 && p2.totalPositive === 1 && p2.totalNegative === 2 && r2.rating === 2 && JSON.stringify(r2.negativeSignals) === '["anime","cinematic","wide"]', JSON.stringify(p2) + ' rating ' + r2.rating);
  for (let i = 0; i < 40; i++) { await sleep(250); if (await evalIn('Number(_st.deleted) >= 1')) break; }
  const rd = JSON.parse(await readProj('catalog.json')).find(r => r.id === 'r1');
  const age = Date.now() - Date.parse(rd.deletedUtc || '');
  const dim = await evalIn('getComputedStyle(document.getElementById("t0")).opacity');
  ok('a rating of 2 soft-deletes: deletedUtc is now, the thumbnail dims', age >= 0 && age < 60000 && Number(dim) < 0.5, `deletedUtc ${rd.deletedUtc}; opacity ${dim}`);
  await key('s');
  for (let i = 0; i < 40; i++) { await sleep(250); if (await evalIn('_st.saved !== undefined')) break; }
  const ps = JSON.parse(await readProj('preferences.json'));
  const rs = JSON.parse(await readProj('catalog.json')).find(r => r.id === 'r1');
  ok('Save marks the record saved and records a positive 1.0', rs.saved === true && ps.totalPositive === 2 && ps.weights.anime === 1.5 && ps.weights.wide === 0.5, JSON.stringify(ps) + ' saved ' + rs.saved);
  // A track's rating and deletion rewrite Music/.music_catalog.json as the
  // original's SaveCatalog does: only the tracks the tab lists are written.
  const readMus = async () => JSON.parse(await evalIn(`(async () => { const d = await (await navigator.storage.getDirectory()).getDirectoryHandle('Proj'); const m = await d.getDirectoryHandle('Music'); return await (await (await m.getFileHandle('.music_catalog.json')).getFile()).text(); })()`));
  const musClick = async (id) => { const w = await evalIn('Number(_st["mus-writes"] || 0)'); await evalIn(`document.getElementById(${JSON.stringify(id)}).click(), 1`); for (let i = 0; i < 40; i++) { await sleep(250); if (await evalIn('Number(_st["mus-writes"] || 0)') > w) break; } return readMus(); };
  const m1 = await musClick('musrate3-4');
  const m2 = await musClick('musrate2-2');
  const status2 = await evalIn('document.getElementById("mus-status").textContent');
  const m3 = await musClick('musdel1');
  ok('rating a track 4 writes its rating; the deleted track leaves the file', m1.map(t => `${t.id}:${t.rating}`).join(',') === '1:0,2:0,3:4' && m1[2].prompt === 'p3' && m1[2].temperature === 1.2, JSON.stringify(m1.map(t => [t.id, t.rating])));
  ok('rating a track 2 removes it and explains the unloaded variant engine', m2.map(t => t.id).join(',') === '1,3' && /Load MusicGen model files first; the rated track was removed/.test(status2), `${m2.map(t => t.id).join(',')}; ${status2}`);
  ok('deleting a track removes it from the file and the list', m3.map(t => t.id).join(',') === '3' && await evalIn('document.querySelectorAll("#mus-list > div").length') === 1, m3.map(t => t.id).join(','));} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
