// Run F32StorageArm's WGSL on WebGPU in headless Edge and check the numbers.
// fs-copy must hand back the same 32 bits for every input; fs-axpy must equal
// f32 arithmetic (x * 2.5 + 1) within one ulp. The control runs the same
// module with bitcast<f32> replaced by the f32() conversion, which is the
// defect the lowering exists to avoid, and requires the arm to fail it.
// Usage: node codex/plugs/wgsl/arms/f32-storage.mjs   Exit 0 = every arm passed.
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
  return {
    send,
    eval: async (expression) => {
      const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
      return r.result?.result?.value;
    },
  };
}

// In the page: run entry point `ep` of `code` over the i32 words `xs`, binding 0
// the output and binding 1 the input (the emitter's discovery order), and
// answer the output words.
const runInPage = `async (code, ep, xs) => {
  const a = await navigator.gpu.requestAdapter(); const d = await a.requestDevice();
  const errs = []; d.pushErrorScope('validation');
  const m = d.createShaderModule({ code });
  const info = await m.getCompilationInfo(); for (const x of info.messages) if (x.type === 'error') errs.push(x.message);
  const n = xs.length, S = GPUBufferUsage.STORAGE;
  const x = d.createBuffer({ size: n * 4, usage: S | GPUBufferUsage.COPY_DST });
  d.queue.writeBuffer(x, 0, new Int32Array(xs));
  const y = d.createBuffer({ size: n * 4, usage: S | GPUBufferUsage.COPY_SRC });
  const r = d.createBuffer({ size: n * 4, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
  const p = d.createComputePipeline({ layout: 'auto', compute: { module: m, entryPoint: ep } });
  const bg = d.createBindGroup({ layout: p.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: y } }, { binding: 1, resource: { buffer: x } }] });
  const e = d.createCommandEncoder(); const c = e.beginComputePass(); c.setPipeline(p); c.setBindGroup(0, bg); c.dispatchWorkgroups(Math.ceil(n / 64)); c.end();
  e.copyBufferToBuffer(y, 0, r, 0, n * 4); d.queue.submit([e.finish()]);
  const v = await d.popErrorScope(); if (v) errs.push(v.message);
  await r.mapAsync(GPUMapMode.READ); const out = Array.from(new Int32Array(r.getMappedRange())); r.unmap(); d.destroy();
  return { errs, out };
}`;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'wgsl-f32-'));
  const wgslFile = join(work, 'f32arm.wgsl');
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex', 'plugs', 'wgsl', 'run.ps1'), '-Src', join(repo, 'codex', 'plugs', 'wgsl', 'arms', 'F32StorageArm.codex'), '-Out', wgslFile], { stdio: 'inherit' });
  const code = readFileSync(wgslFile, 'utf8');
  ok('the loads and stores lower through bitcast', /bitcast<f32>\(fs_copy_xb_buf\[gid\]\)/.test(code) && /= bitcast<i32>\(/.test(code));

  // Inputs: every sign, fractions, large and small normals, and signed zero,
  // as f32 bit patterns. No NaN (a copy may quiet it) and no subnormal (a GPU
  // may flush it).
  const f = new Float32Array(4096), w = new Int32Array(f.buffer);
  let s = 12345;
  const rnd = () => (s = (s * 1103515245 + 12345) % 2147483648) / 2147483648;
  for (let i = 0; i < f.length; i++) f[i] = (rnd() - 0.5) * Math.pow(2, Math.floor(rnd() * 60) - 30);
  f[0] = 0; f[1] = -0; f[2] = 1; f[3] = -1; f[4] = 0.1; f[5] = 3.4e38; f[6] = -1.17549435e-38;
  const xs = Array.from(w);
  const expectAxpy = xs.map((_, i) => Math.fround(Math.fround(f[i] * 2.5) + 1));
  const expectConst = xs.map((_, i) => Math.fround(Math.fround(f[i] * -0.5) + 3));

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end('<!doctype html><title>f32</title>'); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', `http://127.0.0.1:${port}/`], { stdio: 'ignore' });
  const cdp = await cdpOpen(dbg);
  for (let i = 0; i < 40 && !(await cdp.eval('!!navigator.gpu')); i++) await sleep(250);
  const run = (src, ep) => cdp.eval(`(${runInPage})(${JSON.stringify(src)}, ${JSON.stringify(ep)}, ${JSON.stringify(xs)})`);
  const ulps = (got, want) => { const a = new Int32Array([got])[0], b = new Int32Array(new Float32Array([want]).buffer)[0]; return Math.abs(a - b); };
  const grade = async (src) => {
    const copy = await run(src, 'fs_copy_main'), axpy = await run(src, 'fs_axpy_main'), copyA = await run(src, 'fs_copy_approx_main'), constA = await run(src, 'fs_const_approx_main');
    const errs = copy.errs.concat(axpy.errs, copyA.errs, constA.errs);
    const copyBad = copy.out.filter((v, i) => v !== xs[i]).length;
    const axpyBad = axpy.out.filter((v, i) => ulps(v, expectAxpy[i]) > 1).length;
    const copyABad = copyA.out.filter((v, i) => v !== xs[i]).length;
    const constABad = constA.out.filter((v, i) => ulps(v, expectConst[i]) > 1).length;
    return { errs, copyBad, axpyBad, copyABad, constABad };
  };

  const real = await grade(code);
  ok('the module compiles on WebGPU', real.errs.length === 0, real.errs.join('; ').slice(0, 200));
  ok('fs-copy returns every input bit for bit', real.copyBad === 0, `${real.copyBad} of ${xs.length} differ`);
  ok('fs-axpy equals f32 arithmetic within one ulp', real.axpyBad === 0, `${real.axpyBad} of ${xs.length} differ`);
  ok('fs-copy-approx (Real approximate) returns every input bit for bit', real.copyABad === 0, `${real.copyABad} of ${xs.length} differ`);
  ok('fs-const-approx (bits-to-real-approx, real-approx-from-int) equals f32 arithmetic within one ulp', real.constABad === 0, `${real.constABad} of ${xs.length} differ`);

  const conv = await grade(code.replaceAll('bitcast<f32>(', 'f32(').replaceAll('bitcast<i32>(', 'i32('));
  ok('control: conversion in place of bitcast is caught', conv.errs.length === 0 && conv.copyBad > 0 && conv.axpyBad > 0 && conv.copyABad > 0 && conv.constABad > 0, `copy ${conv.copyBad}, axpy ${conv.axpyBad}, copy-approx ${conv.copyABad}, const-approx ${conv.constABad} of ${xs.length} differ`);
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  await sleep(1000);
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(a => !a).length;
console.log(failed === 0 ? `\nPASS: ${arms.length} arms` : `\nFAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
