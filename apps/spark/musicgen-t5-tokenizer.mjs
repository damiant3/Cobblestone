import { readFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';
import { createHash } from 'node:crypto';

const [oraclePath, htmlPath] = process.argv.slice(2);
if (!oraclePath || !htmlPath) throw new Error('Usage: node apps/spark/musicgen-t5-tokenizer.mjs oracle.json MusicT5TokenizerArm.html');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8')), html = readFileSync(htmlPath, 'utf8');
if (!oracle.cases.length || new Set(oracle.cases.map(c => c.name)).size !== oracle.cases.length) throw new Error('Empty or duplicate token cases');
const names = ['drums', 'melody', 'empty', 'whitespace', 'spaces', 'punctuation', 'latin', 'fullwidth', 'unicode', 'special', 'explicit-eos', 'special-adjacent', 'combining'];
if (oracle.cases.length !== names.length || names.some(name => !oracle.cases.some(c => c.name === name))) throw new Error('Missing tokenizer coverage cases');
if (createHash('sha256').update(readFileSync(oracle.tokenizer)).digest('hex') !== oracle.sha256['tokenizer.json']) throw new Error('Tokenizer file changed after oracle generation');
const data = { count: String(oracle.cases.length) };
for (const [i, test] of oracle.cases.entries()) data[`prompt${i}`] = test.prompt;
const page = html.replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
const work = mkdtempSync(join(tmpdir(), 'musicgen-tokenizer-'));
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let edge, server, socket;
try {
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(page); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`Tokenizer browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try { const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]; target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
    if (!target) await delay(250);
  }
  if (!target) throw new Error('Browser startup timeout');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve); socket.addEventListener('error', reject); });
  let sequence = 0; const waiting = new Map(), errors = [];
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { waiting.delete(id); reject(new Error(`CDP timeout ${method}`)); }, 30000);
    waiting.set(id, reply => { clearTimeout(timer); if (reply.error || reply.result?.exceptionDetails) reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))); else resolve(reply.result); });
    socket.send(JSON.stringify({ id, method, params }));
  });
  socket.addEventListener('message', event => {
    const reply = JSON.parse(event.data);
    if (waiting.has(reply.id)) { waiting.get(reply.id)(reply); waiting.delete(reply.id); }
    if (reply.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(reply.params));
    if (reply.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [oracle.tokenizer], backendNodeId: reply.params.backendNodeId }).catch(e => errors.push(String(e)));
  });
  for (const domain of ['Page', 'DOM', 'Runtime']) await send(`${domain}.enable`);
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true })).result?.value;
  const until = async expression => { for (let i = 0; i < 1200; i++) { if (errors.length) throw new Error(errors.join('\n')); if (await evaluate(expression)) return; await delay(250); } throw new Error('Tokenizer page timeout'); };
  await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/` });
  await until('typeof _st !== "undefined" && Number(_st.armed) === 1');
  for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
  await until('typeof _st !== "undefined" && Number(_st.done) === 1');
  const error = await evaluate('String(_st.error || "")'); if (error) throw new Error(error);
  let failures = 0;
  for (const [i, test] of oracle.cases.entries()) {
    const ids = await evaluate(`String(_st.ids${i})`), mask = await evaluate(`Number(_st.mask${i})`);
    const pass = ids === test.ids.join(',') && test.mask.length === test.ids.length && test.mask.every(v => v === mask);
    console.log(`${pass ? 'PASS' : 'FAIL'} ${test.name}: ${ids}; mask ${mask}`);
    if (!pass) { console.log(`  expected ${test.ids.join(',')}; mask ${test.mask.join(',')}`); failures++; }
  }
  if (failures) throw new Error(`${failures} tokenizer cases differ from audiocraft's T5Tokenizer`);
  console.log('PASS: every supplied token ID and conditioning mask matches');
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
