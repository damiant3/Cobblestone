// Spark Studio step 5: the page's reading of each music and sound track (the
// html plug's audio-analyze-then) against the WPF original's MusicAnalyzer,
// unmodified, over the original project's own WAVs
// (build/spark-analyze-oracle.ps1). The BPM must be the original's float;
// the peak, RMS and envelope levels within one float step, since MathF.Log10
// and a correctly rounded log10 can differ in the last place.
// Usage: node apps/spark/audio-analyze.mjs [--project <dir>] [--float32]   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
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
const F = Math.fround;
const ulpClose = (a, b) => { if (a === b) return true; const x = F(b), step = Math.max(Math.abs(x) * 1.2e-7, 1e-30); return Math.abs(a - x) <= step; };

let edge = null, server = null, work = null;
const watchdog = setTimeout(() => { console.log('  FAIL  the arm finished within 20 minutes'); console.log('FAIL: watchdog'); try { edge && edge.kill(); } catch {} process.exit(1); }, 20 * 60000);
try {
  work = mkdtempSync(join(tmpdir(), 'spark-analyze-'));
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'build', 'spark-analyze-oracle.ps1'), '-Project', project, '-Out', join(work, 'oracle.json')], { stdio: 'inherit' });
  const oracle = JSON.parse(readFileSync(join(work, 'oracle.json'), 'utf8'));
  execFileSync('node', [join(repo, 'apps', 'spark', 'build-studio-page.mjs'), '--out', join(work, 'page.html')], { stdio: 'inherit' });
  const html = readFileSync(join(work, 'page.html'), 'utf8');
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const tg = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (tg) url = tg.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map(); const errors = [];
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; const tm = setTimeout(() => { waiting.delete(i); rej(new Error(`${method} unanswered after 90 s`)); }, 90000); waiting.set(i, (m) => { clearTimeout(tm); res(m); }); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [project], backendNodeId: m.params.backendNodeId });
    else if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  });
  await send('Page.enable'); await send('DOM.enable'); await send('Runtime.enable'); await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true }); if (r.error || r.result?.exceptionDetails) throw new Error(JSON.stringify(r.error || r.result.exceptionDetails)); return r.result?.result?.value; };
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await evalIn('typeof _st !== "undefined" && Number(_st.ready) === 1')) break; }
  await evalIn('window.showDirectoryPicker = undefined, 1');
  await evalIn('document.getElementById("open").click(), 1');
  for (let i = 0; i < 480; i++) { await sleep(250); if (await evalIn('Number(_st["audio-done"]) === 1')) break; }

  const rows = [];
  for (const [k, dir, file] of [['mus', 'Music', '.music_catalog.json'], ['sfx', 'SoundFX', '.sfx_catalog.json']]) {
    JSON.parse(readFileSync(join(project, dir, file), 'utf8')).filter(t => !t.deleted).forEach((t, i) => rows.push({ k, i, file: (t.filePath || '').split(/[\\/]/).pop() }));
  }
  if (process.argv.includes('--float32')) {
    const converted = await evalIn(`(async()=>{let count=0;for(const row of ${JSON.stringify(rows)}){const name=_st['afile:'+row.file],file=_gfiles[name],v=new DataView(await file.arrayBuffer());let off=12,channels=0,rate=0,start=0,length=0;while(off+8<=v.byteLength){const tag=String.fromCharCode(...new Uint8Array(v.buffer,off,4)),n=v.getUint32(off+4,true);if(tag==='fmt '){if(v.getUint16(off+8,true)!==1||v.getUint16(off+22,true)!==16)throw Error('Fixture is not PCM16');channels=v.getUint16(off+10,true);rate=v.getUint32(off+12,true)}if(tag==='data'){start=off+8;length=n;break}off+=8+n+(n&1)}if(!channels||!rate||!length)throw Error('Missing PCM fixture');const samples=length/2,b=new ArrayBuffer(58+samples*4),w=new DataView(b),tag=(o,s)=>[...s].forEach((c,i)=>w.setUint8(o+i,c.charCodeAt(0)));tag(0,'RIFF');w.setUint32(4,b.byteLength-8,true);tag(8,'WAVE');tag(12,'fmt ');w.setUint32(16,18,true);w.setUint16(20,3,true);w.setUint16(22,channels,true);w.setUint32(24,rate,true);w.setUint32(28,rate*channels*4,true);w.setUint16(32,channels*4,true);w.setUint16(34,32,true);tag(38,'fact');w.setUint32(42,4,true);w.setUint32(46,samples/channels,true);tag(50,'data');w.setUint32(54,samples*4,true);for(let i=0;i<samples;i++)w.setFloat32(58+i*4,v.getInt16(start+i*2,true)/32768,true);_gfiles[name]=new File([b],row.file,{type:'audio/wav'});count++}return count})()`);
    ok('every analysis input uses MusicGen-style IEEE-f32 WAVE with unchanged samples',converted===rows.length&&converted>0,`${converted} float32 files`);
  }
  for (const r of rows) await evalIn(`(() => { const e = document.getElementById("${r.k}an${r.i}"); if (e) e.click(); return 1; })()`);
  for (let t = 0; t < 600; t++) { await sleep(500); if (await evalIn(`${JSON.stringify(rows.map(r => r.k + 'an' + r.i))}.every(n => typeof _st[n] === "string")`)) break; }
  const bad = [];
  let compared = 0, spectrumBands = 0;
  for (const r of rows) {
    const o = oracle[r.file];
    const p = JSON.parse(await evalIn(`_st["${r.k}an${r.i}"] || "{}"`));
    if (!o) { bad.push(`${r.file}: no oracle`); continue; }
    compared++;
    const envOk = Array.isArray(p.envelope) && p.envelope.length === o.envelope.length && p.envelope.every((x, j) => ulpClose(x, o.envelope[j]));
    const why = [];
    if (p.bpm !== F(o.bpm)) why.push(`bpm ${p.bpm} vs ${o.bpm}`);
    if (!ulpClose(p.peakDb, o.peakDb)) why.push(`peak ${p.peakDb} vs ${o.peakDb}`);
    if (!ulpClose(p.rmsDb, o.rmsDb)) why.push(`rms ${p.rmsDb} vs ${o.rmsDb}`);
    if (p.sampleRate !== o.sampleRate || p.duration !== o.duration) why.push(`rate/duration ${p.sampleRate}/${p.duration} vs ${o.sampleRate}/${o.duration}`);
    if (!envOk) why.push(`envelope ${p.envelope?.length} vs ${o.envelope.length}`);
    if (!ulpClose(p.centroid, o.centroid)) why.push(`centroid ${p.centroid} vs ${o.centroid}`);
    if (!Array.isArray(p.waveform) || p.waveform.length !== o.waveform.length || p.waveform.some((x, j) => x !== F(o.waveform[j]))) why.push(`waveform ${p.waveform?.length} vs ${o.waveform.length}`);
    // The FFT sums the original's DFT in another order, so a band may differ
    // in its last float steps; 1e-5 relative is a few dozen of them, and a
    // wrong bin, band or window moves a band by far more.
    const specBad = !Array.isArray(p.spectrogram) || p.spectrogram.length !== o.spectrogram.length ? 1 : p.spectrogram.reduce((n, fr, f) => n + fr.filter((x, b) => Math.abs(x - o.spectrogram[f][b]) > 1e-5 * Math.max(Math.abs(o.spectrogram[f][b]), 1e-3)).length, 0);
    if (specBad) why.push(`spectrogram ${p.spectrogram?.length} frames vs ${o.spectrogram.length}, ${specBad} bands apart`);
    spectrumBands += (p.spectrogram || []).length * 64;
    if (why.length) bad.push(`${r.file}: ${why.join(', ')}`);
  }
  ok('every track\'s BPM, levels and envelope are the original\'s MusicAnalyzer\'s', compared === rows.length && compared > 0 && bad.length === 0, bad.length ? bad.slice(0, 4).join('; ') : `${compared} tracks, ${spectrumBands} spectrogram bands`);
  // The printed BPM is the number in the original's vibe tag, a tie to even
  // included (track 006 is 62.5 and prints 62).
  const printBad = [];
  for (const r of rows) {
    const shown = await evalIn(`document.getElementById("${r.k}an${r.i}").textContent`);
    const want = (oracle[r.file]?.vibe.match(/\((\d+) BPM\)/) || [])[1];
    const got = (shown.match(/~(\d+) BPM/) || [])[1];
    if (!want || got !== want) printBad.push(`${r.file}: page "${shown}", original ${want}`);
  }
  const tie = rows.some(r => oracle[r.file]?.bpm === 62.5);
  ok('the page prints every BPM as the original\'s vibe tag does, a tie included', printBad.length === 0 && tie, printBad.length ? printBad.slice(0, 3).join('; ') : `${rows.length} printed; a 62.5 among them ${tie}`);
  const vibeBad = [];
  for (const r of rows) {
    const shown = await evalIn(`document.getElementById("${r.k}an${r.i}").textContent`);
    if (!shown.startsWith('  ' + oracle[r.file]?.vibe + ';')) vibeBad.push(`${r.file}: page "${shown}", original "${oracle[r.file]?.vibe}"`);
  }
  const kinds = new Set(rows.map(r => (oracle[r.file]?.vibe || '').split(', ').pop()));
  ok('every track\'s vibe tag is the original\'s', vibeBad.length === 0 && kinds.size >= 3, vibeBad.length ? vibeBad.slice(0, 3).join('; ') : `${rows.length} tags over ${kinds.size} brightness bands`);
  const viewBad = [];
  for (const r of rows) {
    const id = `${r.k}view${r.i}`, o = oracle[r.file];
    const path = await evalIn(`document.querySelector('#${id}wave path')?.getAttribute('d')`);
    const points = [...(path || '').matchAll(/L(\d+) (\d+)/g)].map(m => m.slice(1).map(Number));
    const wf = o.waveform;
    if (points.length !== wf.length * 2 || points.some(([x, y], j) => {
      const i = j < wf.length ? j : wf.length * 2 - 1 - j;
      const sign = j < wf.length ? -1 : 1;
      return x !== Math.floor(i * 1000 / wf.length) || Math.abs(y - (50 + sign * Math.trunc(Math.min(1, Math.max(0, wf[i])) * 45))) > 1;
    })) viewBad.push(`${r.file}: waveform geometry differs`);
    for (const pos of [0, 500, 1000]) {
      const drawn = await evalIn(`(() => { const s = document.getElementById('${id}pos'); if (!s) return null; s.value = '${pos}'; s.dispatchEvent(new Event('input', {bubbles:true})); const svg = document.querySelector('#${id}spec svg'); return { frame: Number(svg?.dataset.frame), ns: svg?.namespaceURI, bars: [...(svg?.querySelectorAll('rect') || [])].map(e => ['x','y','width','height'].map(k => Number(e.getAttribute(k)))) }; })()`);
      const frame = Math.min(Math.floor(pos * o.spectrogram.length / 1000), o.spectrogram.length - 1);
      const bands = o.spectrogram[frame], peak = Math.max(1, ...bands);
      if (!drawn || drawn.frame !== frame || drawn.ns !== 'http://www.w3.org/2000/svg' || drawn.bars.length !== bands.length || drawn.bars.some(([x,y,w,h], b) => {
        const left = Math.floor(b * 1000 / bands.length), height = Math.trunc(Math.max(0, bands[b]) * 90 / peak);
        return x !== left + 1 || w !== Math.floor((b+1) * 1000 / bands.length) - left - 2 || y + h !== 100 || Math.abs(h - height) > 1;
      })) viewBad.push(`${r.file}: spectrum at ${pos} differs`);
    }
  }
  ok('Music and SFX waveform geometry and beginning, middle, end spectra follow the original analysis', viewBad.length === 0 && rows.some(r => r.k === 'mus') && rows.some(r => r.k === 'sfx'), viewBad.slice(0, 4).join('; ') || `${rows.length} waveforms and three spectra per track`);
  const playbackBad = [];
  for (const k of ['mus', 'sfx']) {
    const r = rows.find(r => r.k === k), id = `${k}view${r.i}`, audio = `${k}a${r.i}`;
    const seek = await evalIn(`(() => { const s=document.getElementById('${id}pos'), a=document.getElementById('${audio}'); s.value='250'; s.dispatchEvent(new Event('input',{bubbles:true})); window.__wave=document.querySelector('#${id}wave path'); return Math.abs(a.currentTime-a.duration/4)<0.01; })()`);
    if (!seek) playbackBad.push(`${k}: slider did not seek the player`);
    await evalIn(`(() => { const a=document.getElementById('${audio}'); a.muted=true; a.playbackRate=4; a.currentTime=0; return a.play().then(()=>1); })()`);
    await sleep(600);
    const played = await evalIn(`(() => { const a=document.getElementById('${audio}'); a.pause(); const p=Math.round(a.currentTime/a.duration*1000); a.dispatchEvent(new Event('timeupdate')); return {time:a.currentTime,p,head:Number(document.getElementById('${id}head').getAttribute('x1')),slider:Number(document.getElementById('${id}pos').value),frame:Number(document.querySelector('#${id}spec svg').dataset.frame),same:window.__wave===document.querySelector('#${id}wave path')}; })()`);
    const frame = Math.min(Math.floor(played.p * oracle[r.file].spectrogram.length / 1000), oracle[r.file].spectrogram.length - 1);
    if (!(played.time > 0 && played.head === played.p && played.slider === played.p && played.frame === frame && played.same)) playbackBad.push(`${k}: playback reading ${JSON.stringify(played)}`);
  }
  ok('muted Music and SFX playback moves the playhead and spectrum; the slider seeks without rebuilding the waveform', playbackBad.length === 0, playbackBad.join('; '));
  const binding = await evalIn(`(() => { const a=document.createElement('audio'); document.body.append(a); let old=0,latest=[]; audio_on_position(a,()=>old++); audio_on_position(a,p=>latest.push(Number(p))); a.dispatchEvent(new Event('timeupdate')); a.remove(); a.dispatchEvent(new Event('timeupdate')); audio_seek(a,500n); return old===1 && latest.length===2 && latest.every(p=>p===0) && audio_on_position(null,()=>0)===null && audio_seek(null,0n)===null; })()`);
  ok('rebinding replaces the old callback; detached, missing and unloaded media are safe', binding);
  const first = rows[0], viewId = `${first.k}view${first.i}`;
  await evalIn(`document.getElementById('${viewId}').scrollIntoView(), 1`);
  const shotPath = arg('--screenshot', '');
  if (shotPath) {
    const shot = await send('Page.captureScreenshot', {format:'png'});
    writeFileSync(resolve(shotPath), Buffer.from(shot.result.data, 'base64'));
  }
  const fixture = async data => evalIn(`ss_audio_analyzed('${first.k}', BigInt(${first.i}), ${JSON.stringify(JSON.stringify(data))}), 1`);
  const silent = {ok:true,waveform:[0,0,0,0],spectrogram:[Array(64).fill(0)],bpm:0,peakDb:-120,rmsDb:-120,centroid:0};
  await fixture(silent);
  ok('silence stays on the center line with zero spectrum height', await evalIn(`(() => { const p = document.querySelector('#${viewId}wave path')?.getAttribute('d') || ''; const ys = [...p.matchAll(/L[0-9]+ ([0-9]+)/g)].map(m => Number(m[1])); const bars = [...document.querySelectorAll('#${viewId}spec rect')]; return ys.length === 8 && ys.every(y => y === 50) && bars.length === 64 && bars.every(e => e.getAttribute('height') === '0'); })()`));
  await fixture({...silent, waveform:[]});
  ok('empty analysis has a visible empty state and no old drawing', await evalIn(`document.getElementById('${viewId}').textContent === 'No waveform samples.' && !document.querySelector('#${viewId} svg')`));
  await fixture(silent);
  await fixture({ok:false,error:'fixture refusal'});
  ok('failed analysis clears the previous drawing and reports the refusal', await evalIn(`!document.querySelector('#${viewId} svg') && document.getElementById('${first.k}an${first.i}').textContent.includes('fixture refusal')`));
  ok('the page threw nothing', errors.length === 0, errors.slice(0, 2).map(e => String(e).split('\n')[0]).join(' | '));
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
clearTimeout(watchdog);
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
