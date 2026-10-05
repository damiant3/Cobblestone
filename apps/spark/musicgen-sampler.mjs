import { readFileSync, mkdtempSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';
import { createHash } from 'node:crypto';

const [oraclePath, htmlPath] = process.argv.slice(2);
if (!htmlPath) throw new Error('Usage: node apps/spark/musicgen-sampler.mjs oracle.json arm.html');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8'));
if (!oracle.torch.startsWith('2.1.0') || oracle.device !== 'NVIDIA GeForce RTX 4060 Ti' || oracle.sm_count !== 34) throw new Error('Ungraded Torch/device sampling profile');
for (const [file, hash] of Object.entries(oracle.sha256 || {})) if (createHash('sha256').update(readFileSync(join(dirname(oraclePath), file))).digest('hex') !== hash) throw new Error('Sampler fixture hash differs');
const names = ['uniform', 'spread', 'peaked'].flatMap(kind => ['0', '1234', '4294967301'].flatMap(seed => [0, 1, 2].map(draw => `${kind}-${seed}-${draw}`)));
if (JSON.stringify(oracle.shape) !== '[4,2048]' || oracle.cases.length !== names.length || new Set(oracle.cases.map(c => c.name)).size !== names.length || names.some(n => !oracle.cases.some(c => c.name === n))) throw new Error('Sampler case inventory differs');
const data = { count: String(oracle.cases.length) };
oracle.cases.forEach((test, i) => {
  if (test.name !== `${test.kind}-${test.seed}-${test.draw}` || test.tokens.length !== 4 || test.tokens.some(t => !Number.isInteger(t) || t < 0 || t >= 2048)) throw new Error('Sampler case contract differs');
  if (test.offset_before !== test.draw * 4 || test.offset_after !== (test.draw + 1) * 4 || !oracle.sha256?.[test.probs] || !oracle.sha256?.[test.exponential]) throw new Error('Sampler state/fixture evidence missing');
  const bytes = readFileSync(join(dirname(oraclePath), test.probs));
  if (bytes.length !== 32768) throw new Error('Probability shape differs');
  const probabilities = new Float32Array(bytes.buffer, bytes.byteOffset, 8192);
  for (let row = 0; row < 4; row++) { let sum = 0; for (let k = 0; k < 2048; k++) { const p = probabilities[row * 2048 + k]; if (!Number.isFinite(p) || p < 0) throw new Error('Invalid probability'); sum += p; } if (!(sum > 0)) throw new Error('Zero probability row'); }
  data[`probs${i}`] = JSON.stringify(Array.from(new Int32Array(bytes.buffer, bytes.byteOffset, 8192)));
  data[`seed${i}`] = test.seed; data[`draw${i}`] = String(test.draw);
});
const template = readFileSync(htmlPath, 'utf8');
console.log('HTML SHA256 ' + createHash('sha256').update(template).digest('hex'));
const page = () => template.replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
const work = mkdtempSync(join(tmpdir(), 'musicgen-sampler-')), pause = ms => new Promise(r => setTimeout(r, ms));
let server, edge, socket;
try {
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page()); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`Sampler browser PID ${edge.pid}`);
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
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout')); }, 120000);
    pending.set(id, reply => { clearTimeout(timer); reply.error || reply.result?.exceptionDetails ? reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))) : resolve(reply.result); });
    socket.send(JSON.stringify({ id, method, params }));
  });
  socket.addEventListener('message', event => { const r = JSON.parse(event.data); if (pending.has(r.id)) { pending.get(r.id)(r); pending.delete(r.id); } if (r.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(r.params)); });
  await send('Runtime.enable');
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true })).result?.value;
  await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });
  let done = false;
  for (let i = 0; i < 480 && !done; i++) { if (errors.length) throw new Error(errors.join('\n')); const state = await evaluate("typeof _st !== 'undefined' ? {done:Number(_st.done||0),error:String(_st.error||'')} : {}"); if (state.error) throw new Error(state.error); done = state.done === 1; if (!done) await pause(250); }
  if (!done) throw new Error('Sampler arm timed out');
  for (const [i, test] of oracle.cases.entries()) {
    const tokens = JSON.parse(await evaluate(`String(_st.tokens${i})`));
    if (JSON.stringify(tokens) !== JSON.stringify(test.tokens)) throw new Error(`Multinomial tokens differ: ${test.name} ${JSON.stringify(tokens)} vs ${JSON.stringify(test.tokens)}`);
    const words = JSON.parse(await evaluate(`String(_st.exp${i})`));
    if (!Array.isArray(words) || words.length !== 8192 || words.some(w => !Number.isInteger(w) || w < -2147483648 || w > 4294967295)) throw new Error('Exponential word transport differs');
    const actual = new Float32Array(new Uint32Array(words).buffer), bytes = readFileSync(join(dirname(oraclePath), test.exponential));
    if (bytes.length !== 32768) throw new Error('Exponential reference shape differs');
    const expected = new Float32Array(bytes.buffer, bytes.byteOffset, 8192);
    let worst = 0;
    for (let k = 0; k < actual.length; k++) { const delta = Math.abs(actual[k] - expected[k]) / (1 + Math.abs(expected[k])); if (!Number.isFinite(delta) || actual[k] <= 0 || delta > 0.000001) throw new Error(`Exponential differs: ${test.name} index ${k}`); worst = Math.max(worst, delta); }
    console.log(JSON.stringify({ case: test.name, tokens, worst, tolerance: 0.000001 }));
  }
  data.count = '1'; data.seed0 = String(BigInt(oracle.cases[0].seed) + 1n);
  await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?wrong-seed` });
  done = false;
  for (let i = 0; i < 480 && !done; i++) { if (errors.length) throw new Error(errors.join('\n')); const state = await evaluate("typeof _st !== 'undefined' ? {done:Number(_st.done||0),error:String(_st.error||'')} : {}"); if (state.error) throw new Error(state.error); done = state.done === 1; if (!done) await pause(250); }
  if (!done) throw new Error('Wrong-seed control timed out');
  const control = JSON.parse(await evaluate('String(_st.tokens0)'));
  if (!Array.isArray(control) || control.length !== 4 || control.some(t => !Number.isInteger(t) || t < 0 || t >= 2048)) throw new Error('Malformed wrong-seed result');
  if (JSON.stringify(control) === JSON.stringify(oracle.cases[0].tokens)) throw new Error('Wrong seed was not detected');
  console.log('PASS: Torch CUDA multinomial tokens exact; every exponential draw within1e-6; wrong seed differs');
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
