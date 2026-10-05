import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const [oraclePath, nativePath, htmlPath] = process.argv.slice(2);
if (!oraclePath || !nativePath || !htmlPath) throw new Error('Usage: node build/torchzip-checkpoints.mjs oracle.json native.cdx page.html');
const oracle = JSON.parse(readFileSync(oraclePath, 'utf8'));
if (oracle.checkpoints.length !== 4) throw new Error('Expected all four MusicGen checkpoints');
const subjects = new Set();
for (const checkpoint of oracle.checkpoints) {
  const model = checkpoint.path.match(/models--facebook--musicgen-(small|medium)[\\/]/)?.[1];
  const file = basename(checkpoint.path);
  const key = `${model}/${file}`;
  if (!model || !['state_dict.bin', 'compression_state_dict.bin'].includes(file) || subjects.has(key)) throw new Error(`Unexpected or duplicate checkpoint: ${checkpoint.path}`);
  if (!checkpoint.tensors.length) throw new Error(`Empty tensor table: ${checkpoint.path}`);
  subjects.add(key);
}
const work = mkdtempSync(join(tmpdir(), 'torchzip-grade-'));
console.log(`Evidence: ${work}`);
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
function stopTree(pid) {
  execFileSync('pwsh', ['-NoProfile', '-Command', `$all = @(Get-CimInstance Win32_Process); $ids = [Collections.Generic.List[int]]::new(); $ids.Add(${pid}); for ($i=0; $i -lt $ids.Count; $i++) { foreach ($child in $all) { if ($child.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$child.ProcessId)) { $ids.Add([int]$child.ProcessId) } } }; for ($i=$ids.Count-1; $i -ge 0; $i--) { Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue }`], { stdio: 'ignore' });
}
function nativeRun(args) {
  return new Promise((resolve, reject) => {
    const child = spawn('pwsh', args, { cwd: repo, stdio: 'inherit' });
    console.log(`Native driver PID: ${child.pid}`);
    const timer = setTimeout(() => { stopTree(child.pid); reject(new Error('Native grader timed out')); }, 330000);
    child.on('error', e => { clearTimeout(timer); reject(e); });
    child.on('exit', code => { clearTimeout(timer); code === 0 ? resolve() : reject(new Error(`Native driver exit ${code}`)); });
  });
}
function memory() {
  const free = Number(execFileSync('pwsh', ['-NoProfile', '-Command', '(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory'], { encoding: 'utf8' }).trim());
  if (!(free > 1572864)) throw new Error(`Free RAM ${free} KiB is below guest admission`);
  console.log(`Free RAM: ${free} KiB; one guest`);
}
function grade(checkpoint, actual, platform) {
  const dtype = { float32: 0, float16: 1 };
  const expected = checkpoint.tensors.map(t => {
    if (!(t.dtype in dtype)) throw new Error(`Unsupported oracle dtype ${t.dtype}`);
    return `${t.name}|${dtype[t.dtype]}|${t.shape.join(',')}|${t.start}|${t.end}`;
  }).concat('END').join('\n') + '\n';
  if (actual !== expected) {
    const got = actual.split('\n'), want = expected.split('\n');
    const index = want.findIndex((line, i) => line !== got[i]);
    throw new Error(`${platform}: ${checkpoint.path}: line ${index + 1}: got ${got[index]}, expected ${want[index]}; lengths ${actual.length}/${expected.length}`);
  }
  console.log(`PASS ${platform}: ${checkpoint.path}: ${checkpoint.tensors.length} tensors, every field exact`);
}
for (const [i, checkpoint] of oracle.checkpoints.entries()) {
  const input = join(work, `${i}.stdin`), args = join(work, `${i}.vmargs`), output = join(work, `${i}.native`);
  writeFileSync(input, `${basename(checkpoint.path)}\n${checkpoint.bytes}\n`);
  if (/\s/.test(dirname(checkpoint.path))) throw new Error('VM sidecar paths cannot contain whitespace');
  writeFileSync(args, `-gpu-files ${dirname(checkpoint.path)}\n`);
  memory();
  await nativeRun(['-NoProfile', '-File', join(repo, 'build/test-run.ps1'), '-Kernel', resolve(nativePath), '-OutFile', output, '-StdinFile', input, '-VmArgsFile', args]);
  grade(checkpoint, readFileSync(output, 'utf8'), 'native');
}

let edge, server, ws;
try {
  const html = readFileSync(htmlPath);
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', 'about:blank'], { stdio: 'ignore' });
  console.log(`Browser PID: ${edge.pid}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try {
      const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0];
      target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page');
    } catch {}
    if (!target) await pause(250);
  }
  if (!target) throw new Error('Browser did not expose a page');
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.addEventListener('open', resolve); ws.addEventListener('error', reject); });
  let seq = 0, current;
  const pending = new Map(), errors = [];
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++seq;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30000);
    pending.set(id, m => { clearTimeout(timer); m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result); });
    ws.send(JSON.stringify({ id, method, params }));
  });
  ws.addEventListener('message', event => {
    const m = JSON.parse(event.data);
    if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
    if (m.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(m.params));
    if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [current.path], backendNodeId: m.params.backendNodeId }).catch(e => errors.push(String(e)));
  });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true })).result?.value;
  const waitFor = async expression => {
    for (let i = 0; i < 480; i++) { if (errors.length) throw new Error(errors.join('\n')); if (await evaluate(expression)) return; await pause(250); }
    throw new Error(`Page timed out: ${expression}`);
  };
  for (const name of ['Page', 'DOM', 'Runtime']) await send(`${name}.enable`);
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  for (const [i, checkpoint] of oracle.checkpoints.entries()) {
    current = checkpoint;
    await send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/?case=${i}` });
    await waitFor('typeof _st !== "undefined" && Number(_st.armed) === 1 && Number(_st.done || 0) === 0');
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    await waitFor('typeof _st !== "undefined" && Number(_st.done) === 1');
    const actual = await evaluate('String(_st.lines)');
    writeFileSync(join(work, `${i}.page`), actual);
    grade(checkpoint, actual, 'page');
  }
  console.log('PASS: all four checkpoints, native and page');
} finally {
  if (ws) ws.close();
  if (edge && edge.exitCode === null) stopTree(edge.pid);
  if (server) server.close();
}
