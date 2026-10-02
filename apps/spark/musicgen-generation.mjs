import { readFileSync, writeFileSync, mkdtempSync, createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const [oraclePath, htmlPath, codePath, basePath] = process.argv.slice(2);
if (!basePath) throw new Error('Usage: node apps/spark/musicgen-generation.mjs oracle.json arm.html lm.wgsl base.wgsl');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8'));
if (!oracle.torch.startsWith('2.1.0') || oracle.device !== 'NVIDIA GeForce RTX 4060 Ti') throw new Error('Ungraded generation sampling profile');
for (const [file, hash] of Object.entries(oracle.fixture_sha256 || {})) if (createHash('sha256').update(readFileSync(join(dirname(oraclePath), file))).digest('hex') !== hash) throw new Error('Generation fixture hash differs');
const names = ['greedy-8', 'greedy-17', 'greedy-50', 'greedy-129', 'sampled-8', 'sampled-17', 'sampled-50', 'sampled-129'];
if (oracle.dtype !== 'float32' || oracle.tf32 !== false || oracle.cfg !== 3 || oracle.temperature !== 1 || oracle.top_k !== 250 || JSON.stringify(oracle.delays) !== '[0,1,2,3]') throw new Error('Generation reference contract differs');
if (oracle.cases.length !== names.length || new Set(oracle.cases.map(c => c.name)).size !== names.length || names.some(n => !oracle.cases.some(c => c.name === n))) throw new Error('Generation inventory differs');
for (const test of oracle.cases) {
  if (test.name !== `${test.sampling ? 'sampled' : 'greedy'}-${test.frames}` || test.seed !== '1234' || test.steps !== test.frames + 3 || test.offset_before !== 0 || test.offset_after !== (test.sampling ? test.steps * 4 : 0)) throw new Error('Generation case/state differs');
  if (test.tokens.length !== 4 || test.tokens.some(row => row.length !== test.frames || row.some(v => !Number.isInteger(v) || v < 0 || v >= 2048))) throw new Error('Reference codebooks differ');
  if (readFileSync(join(dirname(oraclePath), test.logits)).length !== test.steps * 8192 * 4) throw new Error('Guided reference shape differs');
  if (!oracle.fixture_sha256?.[test.logits]) throw new Error('Guided fixture hash missing');
}
const hash = createHash('sha256'); for await (const bytes of createReadStream(oracle.checkpoint)) hash.update(bytes);
if (hash.digest('hex') !== oracle.sha256) throw new Error('Checkpoint hash differs');
const condBytes = readFileSync(join(dirname(oraclePath), oracle.condition.file));
if (oracle.condition.shape.length !== 3 || oracle.condition.shape[0] !== 1 || oracle.condition.shape[1] < 1 || oracle.condition.shape[2] !== oracle.dim || ![1024, 1536].includes(oracle.dim) || condBytes.length !== oracle.condition.shape[1] * oracle.dim * 4) throw new Error('Condition shape differs');
if (!oracle.fixture_sha256?.[oracle.condition.file] || Array.from(new Float32Array(condBytes.buffer, condBytes.byteOffset, condBytes.length / 4)).some(v => !Number.isFinite(v))) throw new Error('Invalid conditioning fixture');
const condition = JSON.stringify(Array.from(new Int32Array(condBytes.buffer, condBytes.byteOffset, condBytes.length / 4)));
const html = readFileSync(htmlPath, 'utf8'), code = readFileSync(codePath, 'utf8'), base = readFileSync(basePath, 'utf8');
console.log('Artifact SHA256 ' + JSON.stringify(Object.fromEntries(Object.entries({ html, code, base }).map(([k, v]) => [k, createHash('sha256').update(v).digest('hex')]))));
const work = mkdtempSync(join(tmpdir(), 'musicgen-generation-')), pause = ms => new Promise(r => setTimeout(r, ms));
let cases = oracle.cases, wrongSeed = false, server, edge, socket;
function page() {
  const data = { code, base, condition, count: String(cases.length) };
  cases.forEach((test, i) => { data[`frames${i}`] = String(test.frames); data[`seed${i}`] = wrongSeed ? '1235' : test.seed; data[`sampling${i}`] = test.sampling ? '1' : '0'; });
  return html.replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
}
try {
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page()); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--enable-unsafe-webgpu', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`Generation browser PID ${edge.pid}; evidence ${work}`);
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
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout ${method}`)); }, 120000);
    pending.set(id, reply => { clearTimeout(timer); reply.error || reply.result?.exceptionDetails ? reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))) : resolve(reply.result); });
    socket.send(JSON.stringify({ id, method, params }));
  });
  socket.addEventListener('message', event => {
    const r = JSON.parse(event.data); if (pending.has(r.id)) { pending.get(r.id)(r); pending.delete(r.id); }
    if (r.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(r.params));
    if (r.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [oracle.checkpoint], backendNodeId: r.params.backendNodeId }).catch(e => errors.push(String(e)));
  });
  for (const domain of ['Page', 'DOM', 'Runtime']) await send(`${domain}.enable`);
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true })).result?.value;
  const wait = async expression => {
    let last = '';
    for (let i = 0; i < 2400; i++) {
      if (errors.length) throw new Error(errors.join('\n'));
      const s = await evaluate(`({ok:(${expression}),error:typeof _st !== 'undefined' ? String(_st.error||'') : '',progress:typeof _st !== 'undefined' ? String(_st.case||0)+':'+String(_st.progress||0) : ''})`);
      if (s.error) throw new Error(s.error); if (s.ok) return;
      if (i % 40 === 0 && s.progress !== last) { console.log('progress ' + s.progress); last = s.progress; }
      await pause(250);
    }
    throw new Error('Generation timed out');
  };
  const run = async label => {
    const started = performance.now();
    await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?${label}` });
    await wait("typeof _st !== 'undefined' && Number(_st.need) === 0");
    if (label === 'cancel') await evaluate("(()=>{const seen=ga_seen;ga_seen=(...args)=>{seen(...args);return -1n};return true})()");
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    await wait("typeof _st !== 'undefined' && Number(_st.done) === 1");
    const memory = await evaluate("(()=>{const a=_gb.filter(Boolean),p=Object.values(_gpool).flat();return {activeBytes:a.reduce((s,b)=>s+b.size,0),retainedStorageBytes:[...new Set(a.concat(p))].reduce((s,b)=>s+b.size,0)}})()");
    console.log(JSON.stringify({ run: label, wallMs: performance.now() - started, memory }));
    let differences = 0;
    for (const [i, test] of cases.entries()) {
      const counters = await evaluate(`({steps:Number(_st.steps${i}),offset:Number(_st.offset${i}),order:Number(_st.order${i}||0)})`);
      if (counters.steps !== test.steps || counters.offset !== test.offset_after || counters.order !== 0) throw new Error('Browser step/RNG counters differ');
      const actual = JSON.parse(await evaluate(`String(_st.tokens${i})`)), expected = test.tokens.flat();
      if (!Array.isArray(actual) || actual.length !== expected.length || actual.some(v => !Number.isInteger(v) || v < 0 || v >= 2048)) throw new Error('Malformed generated codebooks');
      writeFileSync(join(work, `${label}-${test.name}-tokens.json`), JSON.stringify(actual));
      let first = null;
      for (let step = 1; step <= test.steps && !first; step++) for (let book = 0; book < 4 && !first; book++) {
        const frame = step - 1 - book;
        if (frame >= 0 && frame < test.frames) { const index = book * test.frames + frame; if (actual[index] !== expected[index]) first = { step, book, frame, actual: actual[index], expected: expected[index] }; }
      }
      if (first) {
        differences++;
        const words = JSON.parse(await evaluate(`String(_st[${JSON.stringify(`g${i}-s${first.step - 1}`)}])`));
        if (!Array.isArray(words) || words.length !== 8192) throw new Error('Missing divergence logits');
        writeFileSync(join(work, `${label}-${test.name}-divergence.f32`), Buffer.from(new Int32Array(words).buffer));
        console.log(JSON.stringify({ run: label, case: test.name, first }));
      } else console.log(JSON.stringify({ run: label, case: test.name, exact: true, frames: test.frames }));
    }
    return differences;
  };
  if (!process.argv.includes('--cancel-only')) {
    if (await run('real')) throw new Error('Generated tokens differ from audiocraft');
    wrongSeed = true; cases = [oracle.cases.find(c => c.name === 'sampled-8')];
    if (!(await run('wrong-seed'))) throw new Error('Wrong generation seed was not detected');
    console.log('PASS: complete delayed greedy and sampled codebooks match audiocraft; wrong seed differs');
  }
  wrongSeed = false; cases = [oracle.cases.find(c => c.name === 'sampled-8')];
  let cancelled = false;
  try { await run('cancel'); } catch (error) { if (error.message !== 'MusicGen generation cancelled') throw error; cancelled = true; }
  const closed = await evaluate('({active:_gb.filter(Boolean).length,done:Number(_st.done),steps:Number(_st.steps0),offset:Number(_st.offset0)})');
  if (!cancelled || closed.active !== 0 || closed.done !== 1 || closed.steps !== 1 || closed.offset !== 4) throw new Error('Cancellation cleanup/counters differ');
  console.log('PASS: cancellation after one sampled step and caller cleanup leave no live GPU handles');
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
