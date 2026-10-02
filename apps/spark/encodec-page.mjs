import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const [referenceArg, htmlArg, shaderArg] = process.argv.slice(2);
if (!referenceArg || !htmlArg || !shaderArg) throw new Error('Usage: node apps/spark/encodec-page.mjs reference-directory EncodecArm.html EncodecKernels.wgsl');
const reference = resolve(referenceArg), oracle = JSON.parse(readFileSync(join(reference, 'oracle.json'), 'utf8'));
if (oracle.device !== 'cuda' || oracle.cases.length !== 10 || oracle.sample_rate !== 32000 || oracle.frame_rate !== 50) throw new Error('CUDA 32 kHz EnCodec f16/f32 reference required');
const required = new Set([1, 2, 7, 8, 17].flatMap(frames => ['zero', 'spread'].map(p => `${p}-${frames}`)));
for (const test of oracle.cases) {
  if (!required.delete(test.name) || !(test.f16_f32_max_abs > 0) || !Number.isFinite(test.f16_f32_max_abs)) throw new Error('Invalid audio reference case');
  const [pattern, textFrames] = test.name.split('-'), frames = Number(textFrames);
  if (test.codes.length !== 1 || test.codes[0].length !== 4 || test.audio.shape.join(',') !== `1,1,${frames * 640}`) throw new Error('Audio reference shape differs from its case');
  for (const [book, tokens] of test.codes[0].entries()) {
    if (tokens.length !== frames || tokens.some((token, frame) => token !== (pattern === 'zero' ? 0 : (book * 503 + frame * 197 + 2047) % 2048))) throw new Error('Audio reference token pattern differs from its case');
  }
}
if (required.size) throw new Error('Missing audio reference case');
const html = readFileSync(htmlArg, 'utf8'), shader = readFileSync(shaderArg, 'utf8');
const work = mkdtempSync(join(tmpdir(), 'encodec-page-'));
console.log(`Evidence: ${work}`);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let edge, server, socket, currentCode = shader, currentCases = oracle.cases;
function page() {
  const data = { code: currentCode, count: String(currentCases.length) };
  for (const [i, test] of currentCases.entries()) { data[`frames${i}`] = String(test.codes[0][0].length); data[`tokens${i}`] = JSON.stringify(test.codes[0].flat()); }
  return html.replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
}
try {
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page()); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--enable-unsafe-webgpu', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`Full EnCodec browser PID ${edge.pid}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try { const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]; target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
    if (!target) await delay(250);
  }
  if (!target) throw new Error('Browser startup timed out');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve); socket.addEventListener('error', reject); });
  const pending = new Map(), errors = []; let sequence = 0;
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30000);
    pending.set(id, reply => { clearTimeout(timer); if (reply.error || reply.result?.exceptionDetails) reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))); else resolve(reply.result); });
    socket.send(JSON.stringify({ id, method, params }));
  });
  socket.addEventListener('message', event => {
    const reply = JSON.parse(event.data);
    if (pending.has(reply.id)) { pending.get(reply.id)(reply); pending.delete(reply.id); }
    if (reply.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(reply.params));
    if (reply.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [oracle.checkpoint], backendNodeId: reply.params.backendNodeId }).catch(e => errors.push(String(e)));
  });
  for (const domain of ['Page', 'DOM', 'Runtime']) await send(`${domain}.enable`);
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true })).result?.value;
  const until = async expression => {
    for (let i = 0; i < 1200; i++) { if (errors.length) throw new Error(errors.join('\n')); if (await evaluate(expression)) return; await delay(250); }
    throw new Error('Page timed out: ' + expression);
  };
  const run = async label => {
    await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?run=${label}` });
    await until('typeof _st !== "undefined" && Number(_st.armed) === 1 && Number(_st.done || 0) === 0');
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    await until('typeof _st !== "undefined" && Number(_st.done) === 1');
    const error = await evaluate('String(_st.error || "")'); if (error) throw new Error(error);
    const results = [];
    for (const [i, test] of currentCases.entries()) {
      const words = JSON.parse(await evaluate(`String(_st.audio${i})`));
      if (!Array.isArray(words)) throw new Error('No waveform returned');
      const actual = new Float32Array(new Int32Array(words).buffer);
      const bytes = readFileSync(join(reference, test.audio.file));
      const expected = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
      const frames = test.codes[0][0].length;
      if (actual.length !== expected.length || actual.length !== frames * 640) throw new Error('Waveform length mismatch');
      let bad = 0, worst = 0;
      for (let k = 0; k < actual.length; k++) { const error = Math.abs(actual[k] - expected[k]); if (!Number.isFinite(error) || error > test.f16_f32_max_abs) bad++; worst = Math.max(worst, error); }
      const result = { name: test.name, samples: actual.length, bad, worst, tolerance: test.f16_f32_max_abs };
      writeFileSync(join(work, `${label}-${test.name}.f32`), Buffer.from(actual.buffer));
      results.push(result); console.log(JSON.stringify(result));
    }
    return results;
  };
  const results = await run('real');
  if (results.some(r => r.bad)) throw new Error('Assembled EnCodec waveform differs beyond the reference precision deviation');
  currentCases = [structuredClone(oracle.cases[0])]; currentCases[0].codes[0][0][0] = -1;
  let negativeRefused = false;
  try { await run('negative-token'); } catch (error) { if (String(error).includes('EnCodec decode failed:')) negativeRefused = true; else throw error; }
  if (!negativeRefused) throw new Error('Negative codebook token was not refused');
  currentCode = shader.replace(/(ec_add_yb_buf\[gid\] = bitcast<i32>\(\(bitcast<f32>\(ec_add_ab_buf\[gid\]\)) \+/, '$1 -');
  if (currentCode === shader) throw new Error('Residual-sign control did not alter the shader');
  currentCases = [oracle.cases.find(c => c.name === 'spread-8')];
  const control = await run('wrong-residual');
  if (!control.some(r => r.bad)) throw new Error('Residual-sign control was not detected');
  console.log('PASS: real-checkpoint browser EnCodec, every audio sample within its measured f16/f32 reference deviation; residual-sign control fails');
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
