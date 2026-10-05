import { readFileSync, writeFileSync, mkdtempSync, createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const [oraclePath, htmlPath, codePath, basePath] = process.argv.slice(2);
if (!basePath) throw new Error('Usage: node apps/spark/musicgen-lm.mjs oracle.json arm.html lm.wgsl base.wgsl');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8'));
const names = ['drums-1', 'drums-3', 'drums-7', 'empty-1', 'empty-3', 'empty-7'];
if (oracle.cases.length !== names.length || new Set(oracle.cases.map(c => c.name)).size !== names.length || names.some(n => !oracle.cases.some(c => c.name === n))) throw new Error('Missing LM cases');
const { dim, num_layers: layers } = oracle.config;
if (!((dim === 1024 && layers === 24) || (dim === 1536 && layers === 48)) || oracle.config.num_heads !== dim / 64) throw new Error('Unexpected LM dimensions');
function shape(tensor, expected) {
  if (JSON.stringify(tensor.shape) !== JSON.stringify(expected)) throw new Error('Reference tensor shape differs');
  if (readFileSync(join(dirname(oraclePath), tensor.file)).byteLength !== expected.reduce((a, b) => a * b, 4)) throw new Error('Reference tensor byte length differs');
}
for (const test of oracle.cases) {
  const steps = Number(test.name.split('-')[1]);
  if (test.steps !== steps || test.codes.length !== 4 || test.codes.some((book, k) => book.length !== steps || book.some((id, t) => id !== (t === 0 ? 2048 : (t * 137 + k * 503) % 2048)))) throw new Error('Fixed LM code contract differs');
  if (test.blocks.length !== layers || test.condition.shape.length !== 3 || test.condition.shape[1] < 1) throw new Error('Reference inventory differs');
  shape(test.condition, [1, test.condition.shape[1], dim]);
  for (const block of test.blocks) shape(block, [1, steps, dim]);
  shape(test.logits, [1, 4, steps, 2048]);
  if (!test.precision) throw new Error('LM precision reference is required');
  const singleBytes = readFileSync(join(dirname(oraclePath), test.logits.file));
  const wideBytes = readFileSync(join(dirname(oraclePath), test.precision.file));
  if (wideBytes.byteLength !== singleBytes.byteLength * 2) throw new Error('Wide reference shape differs');
  const single = new Float32Array(singleBytes.buffer, singleBytes.byteOffset, singleBytes.byteLength / 4);
  const wide = new Float64Array(wideBytes.buffer, wideBytes.byteOffset, wideBytes.byteLength / 8);
  let deviation = 0;
  for (let k = 0; k < single.length; k++) {
    const delta = Math.abs(wide[k] - single[k]) / (1 + Math.abs(single[k]));
    if (!Number.isFinite(delta)) throw new Error('Nonfinite reference logits');
    deviation = Math.max(deviation, delta);
  }
  if (!Number.isFinite(test.precision.max_normalized) || Math.abs(deviation - test.precision.max_normalized) > 1e-12) throw new Error('Reference precision measurement differs');
  test.bound = Math.max(0.0001, 2 * deviation);
}
const hash = createHash('sha256'); for await (const bytes of createReadStream(oracle.checkpoint)) hash.update(bytes);
if (hash.digest('hex') !== oracle.sha256) throw new Error('Checkpoint changed');
const html = readFileSync(htmlPath, 'utf8'), code = readFileSync(codePath, 'utf8'), base = readFileSync(basePath, 'utf8');
console.log('Artifact SHA256: ' + JSON.stringify(Object.fromEntries(Object.entries({ html, code, base }).map(([k, v]) => [k, createHash('sha256').update(v).digest('hex')]))));
const work = mkdtempSync(join(tmpdir(), 'musicgen-lm-')), pause = ms => new Promise(r => setTimeout(r, ms));
let shader = code, cases = oracle.cases, server, edge, socket;
const words = buffer => Array.from(new Int32Array(buffer.buffer, buffer.byteOffset, buffer.byteLength / 4));
function page() {
  const data = { code: shader, base, count: String(cases.length) };
  cases.forEach((test, i) => { data[`codes${i}`] = JSON.stringify(test.codes.flat()); data[`rows${i}`] = String(test.steps); data[`condition${i}`] = JSON.stringify(words(readFileSync(join(dirname(oraclePath), test.condition.file)))); });
  return html.replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
}
try {
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page()); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--enable-unsafe-webgpu', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`LM browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try { const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]; target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
    if (!target) await pause(250);
  }
  if (!target) throw new Error('Browser startup timed out');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { socket.addEventListener('open', r); socket.addEventListener('error', j); });
  const pending = new Map(), errors = []; let sequence = 0;
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout ${method}`)); }, 60000);
    pending.set(id, reply => { clearTimeout(timer); reply.error || reply.result?.exceptionDetails ? reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))) : resolve(reply.result); });
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
  const wait = async expression => {
    for (let i = 0; i < 1200; i++) {
      if (errors.length) throw new Error(errors.join('\n'));
      const s = await evaluate(`({ok:(${expression}),error:typeof _st !== 'undefined' ? String(_st.error||'') : ''})`);
      if (s?.error) throw new Error(s.error); if (s?.ok) return; await pause(250);
    }
    throw new Error('LM page timed out');
  };
  const run = async label => {
    const started = performance.now();
    await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?run=${label}` });
    await wait("typeof _st !== 'undefined' && Number(_st.need) === 0");
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    await wait("typeof _st !== 'undefined' && Number(_st.done) === 1");
    console.log(JSON.stringify({ run: label, wallMs: performance.now() - started }));
    if (await evaluate('Number(_st.layers)') !== oracle.config.num_layers) throw new Error('Layer count differs');
    const results = [];
    for (const [i, test] of cases.entries()) {
      const logitsBytes = readFileSync(join(dirname(oraclePath), test.logits.file));
      const logits = new Float32Array(logitsBytes.buffer, logitsBytes.byteOffset, logitsBytes.byteLength / 4);
      const wideBytes = readFileSync(join(dirname(oraclePath), test.precision.file));
      const wide = new Float64Array(wideBytes.buffer, wideBytes.byteOffset, wideBytes.byteLength / 8);
      for (let part = 0; part < oracle.config.num_layers + 4; part++) {
        const isHead = part >= oracle.config.num_layers;
        const partName = isHead ? `head${part - oracle.config.num_layers}` : `block${part}`;
        const returned = JSON.parse(await evaluate(`String(_st[${JSON.stringify(`c${i}-v${part}`)}])`));
        if (!Array.isArray(returned) || returned.some(v => !Number.isInteger(v) || v < -2147483648 || v > 2147483647)) throw new Error('Malformed GPU word array');
        if (isHead) {
          const production = JSON.parse(await evaluate(`String(_st[${JSON.stringify(`p${i}-h${part - oracle.config.num_layers}`)}])`));
          if (!Array.isArray(production) || production.length !== returned.length || production.some((v, k) => v !== returned[k])) throw new Error('Production lifetime path differs');
        }
        const actual = new Float32Array(new Int32Array(returned).buffer);
        const bytes = isHead ? null : readFileSync(join(dirname(oraclePath), test.blocks[part].file));
        const expected = isHead ? logits.subarray((part - oracle.config.num_layers) * test.steps * 2048, (part - oracle.config.num_layers + 1) * test.steps * 2048) : new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
        if (actual.length !== expected.length || actual.length !== test.steps * (isHead ? 2048 : oracle.config.dim)) throw new Error('LM shape differs');
        let bad = 0, worst = 0, wideWorst = 0, fixedBad = 0;
        const tolerance = isHead ? test.bound : 0.0001;
        for (let k = 0; k < actual.length; k++) {
          const scale = 1 + Math.abs(expected[k]);
          const error = Math.abs(actual[k] - expected[k]) / scale;
          const wideError = isHead ? Math.abs(actual[k] - wide[(part - layers) * actual.length + k]) / scale : 0;
          if (!Number.isFinite(error) || !Number.isFinite(wideError) || Math.max(error, wideError) > tolerance) bad++;
          if (!Number.isFinite(error) || error > 0.0001) fixedBad++;
          worst = Math.max(worst, error); wideWorst = Math.max(wideWorst, wideError);
        }
        const result = { run: label, case: test.name, part: partName, bad, worst, wideWorst, fixedBad, referenceDeviation: isHead ? test.precision.max_normalized : 0, tolerance }; results.push(result); console.log(JSON.stringify(result));
        if (bad || isHead) writeFileSync(join(work, `${label}-${test.name}-${partName}.f32`), Buffer.from(actual.buffer));
      }
    }
    return results;
  };
  const real = await run('real');
  if (real.some(r => r.bad)) throw new Error('MusicGen LM differs from audiocraft');
  shader = code.replace('(col > row)', '(col < row)');
  if (shader === code) throw new Error('Causal control did not change shader');
  cases = [oracle.cases.find(c => c.name === 'drums-3')];
  const control = await run('wrong-causal');
  if (!control.some(r => r.part.startsWith('head') && r.bad)) throw new Error('Wrong causal direction was not detected');
  console.log('PASS: all MusicGen blocks and every head logit; wrong-causal control fails');
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
