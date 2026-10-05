import { readFileSync, writeFileSync, mkdtempSync, createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const [oraclePath, htmlPath, codePath, basePath] = process.argv.slice(2);
if (!basePath) throw new Error('Usage: node apps/spark/musicgen-t5-encoder.mjs oracle.json arm.html t5.wgsl base.wgsl');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8')), encoder = oracle.encoder;
const names = ['drums', 'melody', 'empty', 'whitespace', 'spaces', 'punctuation', 'latin', 'fullwidth', 'unicode', 'special', 'explicit-eos', 'special-adjacent', 'combining', 'long'];
if (!encoder || encoder.cases.length !== names.length || new Set(encoder.cases.map(c => c.name)).size !== names.length || names.some(n => !encoder.cases.some(c => c.name === n))) throw new Error('Missing T5 encoder cases');
const html = readFileSync(htmlPath, 'utf8'), code = readFileSync(codePath, 'utf8'), base = readFileSync(basePath, 'utf8');
async function digest(path) { const hash = createHash('sha256'); for await (const bytes of createReadStream(path)) hash.update(bytes); return hash.digest('hex'); }
for (const [path, hash] of [[oracle.tokenizer, oracle.sha256['tokenizer.json']], [encoder.weights, encoder.sha256.weights], [encoder.checkpoint, encoder.sha256.checkpoint]]) {
  if (await digest(path) !== hash) throw new Error('Reference input changed: ' + path);
}
console.log('Artifact SHA256: ' + JSON.stringify({ html: createHash('sha256').update(html).digest('hex'), t5: createHash('sha256').update(code).digest('hex'), base: createHash('sha256').update(base).digest('hex') }));
const files = [oracle.tokenizer, encoder.weights, encoder.checkpoint], work = mkdtempSync(join(tmpdir(), 'musicgen-t5-encoder-'));
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let currentCode = code, currentCases = encoder.cases, pick = 0, server, edge, socket;
function page() {
  const data = { code: currentCode, base, count: String(currentCases.length) };
  currentCases.forEach((test, i) => { data[`prompt${i}`] = test.prompt; });
  return html.replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
}
try {
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page()); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--enable-unsafe-webgpu', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`T5 browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try { const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]; target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
    if (!target) await pause(250);
  }
  if (!target) throw new Error('Browser startup timed out');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve); socket.addEventListener('error', reject); });
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
    if (reply.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [files[pick]], backendNodeId: reply.params.backendNodeId }).catch(e => errors.push(String(e)));
  });
  for (const domain of ['Page', 'DOM', 'Runtime']) await send(`${domain}.enable`);
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true })).result?.value;
  const waitFor = async expression => {
    for (let i = 0; i < 1200; i++) {
      if (errors.length) throw new Error(errors.join('\n'));
      const state = await evaluate(`({ok:(${expression}),error:typeof _st !== 'undefined' ? String(_st.error || '') : ''})`);
      if (state?.error) throw new Error(state.error); if (state?.ok) return; await pause(250);
    }
    throw new Error('T5 page timed out: ' + expression);
  };
  const run = async label => {
    const started = performance.now();
    await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?run=${label}` });
    for (pick = 0; pick < 3; pick++) {
      await waitFor(`typeof _st !== 'undefined' && Number(_st.need) === ${pick} && !Number(_st.done || 0)`);
      for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
      await waitFor(`typeof _st !== 'undefined' && Number(_st.need) > ${pick}`);
      console.log(`${label}: input ${pick + 1} loaded`);
    }
    await waitFor("typeof _st !== 'undefined' && Number(_st.done) === 1");
    const memory = await evaluate("(()=>{const active=_gb.filter(Boolean),pooled=Object.values(_gpool).flat(),all=[...new Set(active.concat(pooled))];return {activeBytes:active.reduce((s,b)=>s+b.size,0),pooledBytes:pooled.reduce((s,b)=>s+b.size,0),storageBytes:all.reduce((s,b)=>s+b.size,0)}})()");
    console.log(JSON.stringify({ run: label, wallMs: performance.now() - started, gpuStorage: memory }));
    const buckets = JSON.parse(await evaluate('String(_st.buckets)'));
    if (label === 'real' && JSON.stringify(buckets) !== JSON.stringify(encoder.buckets)) throw new Error('T5 relative-position buckets differ');
    const results = [];
    for (const [i, test] of currentCases.entries()) {
      if (await evaluate(`String(_st.ids${i})`) !== test.ids.join(',') || await evaluate(`String(_st.mask${i})`) !== test.mask.join(',')) throw new Error('T5 tokens or mask differ: ' + test.name);
      let inspected;
      for (let part = 0; part < 14; part++) {
        const name = part < 12 ? `block${part}` : part === 12 ? 'hidden' : 'condition';
        const descriptor = part < 12 ? test.blocks[part] : part === 12 ? test.hidden : test.output;
        const words = JSON.parse(await evaluate(`String(_st[${JSON.stringify(`c${i}-v${part}`)}])`));
        if (!Array.isArray(words)) throw new Error('No T5 tensor returned');
        if (part === 13) inspected = words;
        const actual = new Float32Array(new Int32Array(words).buffer), bytes = readFileSync(join(dirname(oraclePath), descriptor.file));
        const expected = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
        if (actual.length !== expected.length || actual.length !== test.ids.length * (part === 13 ? encoder.output_dim : 768)) throw new Error('T5 tensor shape differs');
        const scales = new Float64Array(test.ids.length);
        if (part < 12) for (let row = 0; row < scales.length; row++) { let sum = 0; for (let col = 0; col < 768; col++) sum += expected[row * 768 + col] ** 2; scales[row] = Math.max(1, Math.sqrt(sum / 768 + 0.000001)); }
        let bad = 0, worst = 0, elementWorst = 0;
        for (let k = 0; k < actual.length; k++) {
          const delta = Math.abs(actual[k] - expected[k]);
          const element = delta / (1 + Math.abs(expected[k]));
          const error = part < 12 ? delta / scales[Math.floor(k / 768)] : element;
          if (!Number.isFinite(error) || error > 0.0001) bad++;
          worst = Math.max(worst, error); elementWorst = Math.max(elementWorst, element);
        }
        const result = { run: label, case: test.name, part: name, metric: part < 12 ? 'row-rms' : 'element-relative', bad, worst, elementWorst, tolerance: 0.0001 };
        results.push(result); console.log(JSON.stringify(result));
        if (bad || part === 13) writeFileSync(join(work, `${label}-${test.name}-${name}.f32`), Buffer.from(actual.buffer));
      }
      const production = JSON.parse(await evaluate(`String(_st.production${i})`));
      if (!Array.isArray(production) || production.length !== inspected.length || production.some((v, k) => v !== inspected[k])) throw new Error('T5 non-inspection lifetime path differs: ' + test.name);
    }
    return results;
  };
  const results = await run('real');
  if (results.some(r => r.bad)) throw new Error('T5 encoder/conditioner differs from audiocraft');
  currentCode = code.replace('(rel > 0)', '(rel < 0)');
  if (currentCode === code) throw new Error('Relative-bias control did not change the shader');
  currentCases = [encoder.cases.find(c => c.name === 'drums')];
  const control = await run('wrong-bias');
  if (!control.some(r => r.bad && r.part === 'condition')) throw new Error('Wrong relative-position direction was not detected');
  console.log('PASS: tokens, masks, all T5 blocks, hidden states and MusicGen projection; relative-bias control fails');
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
