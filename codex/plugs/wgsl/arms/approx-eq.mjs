// Run ApproxEqArm's WGSL on WebGPU in headless Edge. ae-flags answers bit 0 for x ~0 z
// and bit 1 for x ~ z, which must agree with the x86-64 backend's definition: the
// float's ordinal (sign and magnitude mapped to two's complement), equal for ~0 and at
// most 4 apart for ~. The pairs include +0 and -0, floats 1 to 5 ordinals apart, a NaN
// against its own bits (== says false, ~0 says true) and opposite-sign extremes whose
// i32 difference would wrap. The control widens the tolerance to 5 and must be caught.
// Usage: node codex/plugs/wgsl/arms/approx-eq.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

async function cdpOpen(port) {
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map();
  ws.addEventListener('message', (ev) => { const m = JSON.parse(ev.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } });
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  return async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  };
}

const runInPage = `async (code, xs, zs) => {
  const a = await navigator.gpu.requestAdapter(); const d = await a.requestDevice();
  const errs = [];
  const m = d.createShaderModule({ code });
  for (const x of (await m.getCompilationInfo()).messages) if (x.type === 'error') errs.push(x.message);
  if (errs.length) { d.destroy(); return { errs, out: [] }; }
  d.pushErrorScope('validation');
  const n = xs.length, S = GPUBufferUsage.STORAGE, D = GPUBufferUsage.COPY_DST;
  const x = d.createBuffer({ size: n * 4, usage: S | D }); d.queue.writeBuffer(x, 0, new Int32Array(xs));
  const z = d.createBuffer({ size: n * 4, usage: S | D }); d.queue.writeBuffer(z, 0, new Int32Array(zs));
  const y = d.createBuffer({ size: n * 4, usage: S | GPUBufferUsage.COPY_SRC });
  const r = d.createBuffer({ size: n * 4, usage: GPUBufferUsage.MAP_READ | D });
  const p = d.createComputePipeline({ layout: 'auto', compute: { module: m, entryPoint: 'ae_flags_main' } });
  const man = /cx-kernel ae_flags_main (.*)/.exec(code)[1].split(' ').slice(1).map(s => s.split('='));
  const buf = { xb: x, zb: z, yb: y };
  const bg = d.createBindGroup({ layout: p.getBindGroupLayout(0), entries: man.map(([k, v]) => ({ binding: Number(v.slice(1)), resource: { buffer: buf[k] } })) });
  const e = d.createCommandEncoder(); const c = e.beginComputePass(); c.setPipeline(p); c.setBindGroup(0, bg); c.dispatchWorkgroups(Math.ceil(n / 64)); c.end();
  e.copyBufferToBuffer(y, 0, r, 0, n * 4); d.queue.submit([e.finish()]);
  const v = await d.popErrorScope(); if (v) errs.push(v.message);
  await r.mapAsync(GPUMapMode.READ); const out = Array.from(new Int32Array(r.getMappedRange())); r.unmap(); d.destroy();
  return { errs, out };
}`;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'wgsl-approx-'));
  const wgslFile = join(work, 'approx.wgsl');
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex', 'plugs', 'wgsl', 'run.ps1'), '-Src', join(repo, 'codex', 'plugs', 'wgsl', 'arms', 'ApproxEqArm.codex'), '-Out', wgslFile], { stdio: 'inherit' });
  const code = readFileSync(wgslFile, 'utf8');
  ok('~ and ~0 lower to ordinal comparisons, not a refusal and not ==', !/CODEX_REFUSED/.test(code) && /bitcast<i32>/.test(code) && /<= 4u/.test(code));

  const bits = f => new Int32Array(new Float32Array([f]).buffer)[0];
  const ord = b => b >= 0 ? b : -(b & 0x7fffffff);
  const xs = [], zs = [];
  const pair = (a, b) => { xs.push(a); zs.push(b); };
  pair(bits(0), bits(-0));
  for (const base of [1, -1, 3.25, -1e-20, 1e30]) for (let k = 0; k <= 5; k++) { const b = bits(base); pair(b, b >= 0 ? b + k : b - k); pair(b >= 0 ? b + k : b - k, b); }
  pair(0x7fc00001, 0x7fc00001);
  pair(bits(3.4e38), bits(-3.4e38));
  pair(bits(-3.4e38), bits(3.4e38));
  pair(bits(1.5), bits(1.5));
  const want = xs.map((x, i) => { const d = Math.abs(ord(x) - ord(zs[i])); return (d === 0 ? 1 : 0) + (d <= 4 ? 2 : 0); });

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end('<!doctype html><title>approx</title>'); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', `http://127.0.0.1:${port}/`], { stdio: 'ignore' });
  const evaluate = await cdpOpen(dbg);
  for (let i = 0; i < 40 && !(await evaluate('!!navigator.gpu')); i++) await sleep(250);
  const run = (src) => evaluate(`(${runInPage})(${JSON.stringify(src)}, ${JSON.stringify(xs)}, ${JSON.stringify(zs)})`);

  const real = await run(code);
  ok('the module compiles on WebGPU', real.errs.length === 0, real.errs.join('; ').slice(0, 200));
  const bad = real.out.map((v, i) => [v, want[i], i]).filter(([v, w]) => v !== w);
  ok(`every pair answers ~0 and ~ as the ordinal definition (${xs.length} pairs)`, real.errs.length === 0 && bad.length === 0, bad.slice(0, 4).map(([v, w, i]) => `pair ${i} got ${v} want ${w}`).join('; '));
  ok('+0 ~0 -0 and a NaN ~0 its own bits hold (== would refuse the NaN)', real.out[0] === 3 && real.out[xs.length - 4] === 3);
  ok('opposite-sign extremes are not approximately equal (no i32 wrap)', real.out[xs.length - 3] === 0 && real.out[xs.length - 2] === 0);

  const wide = await run(code.replaceAll('<= 4u', '<= 5u'));
  ok('control: a 5-ULP tolerance is caught', wide.errs.length === 0 && wide.out.some((v, i) => v !== want[i]));
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  await sleep(1000);
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`], { stdio: 'ignore' }); } catch {}
    try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(a => !a).length;
console.log(failed === 0 ? `\nPASS: ${arms.length} arms` : `\nFAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
