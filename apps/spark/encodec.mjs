import { readFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn, execFileSync } from 'node:child_process';
import http from 'node:http';

const [referenceArg, shaderArg] = process.argv.slice(2);
if (!referenceArg || !shaderArg) throw new Error('Usage: node apps/spark/encodec.mjs reference-directory EncodecKernels.wgsl');
const reference = resolve(referenceArg), shader = readFileSync(shaderArg, 'utf8');
const oracle = JSON.parse(readFileSync(join(reference, 'oracle.json'), 'utf8'));
if (oracle.books.shape.join(',') !== '4,2048,128' || oracle.cases.length !== 10) throw new Error('Unexpected reference corpus');
const cases = new Set();
for (const test of oracle.cases) {
  if (!/^(zero|spread)-(1|2|7|8|17)$/.test(test.name) || cases.has(test.name)) throw new Error('Missing or duplicate reference case');
  const frames = Number(test.name.split('-')[1]);
  if (test.codes.length !== 1 || test.codes[0].length !== 4 || test.codes[0].some(book => book.length !== frames || book.some(k => !Number.isInteger(k) || k < 0 || k >= 2048))) throw new Error('Invalid token dimensions or indices');
  cases.add(test.name);
}
const work = mkdtempSync(join(tmpdir(), 'encodec-grade-'));
const allowed = new Set([oracle.books.file, ...oracle.cases.map(c => c.latent.file)]);
if (oracle.convs) {
  const layers = ['0', '3', '4-block-1', '4-block-3', '6', '7-block-1', '7-block-3', '9', '10-block-1', '10-block-3', '12', '13-block-1', '13-block-3', '15'];
  const required = new Set(layers.flatMap(layer => [1, 3, 9].map(frames => `conv-model-${layer}-${frames}`)));
  for (const test of oracle.convs) {
    if (!required.delete(test.name) || !['conv1d', 'transpose'].includes(test.kind)) throw new Error('Unexpected or duplicate convolution case');
  }
  if (required.size) throw new Error('Missing decoder convolution cases: ' + [...required].join(', '));
  for (const test of oracle.convs) for (const key of ['v', 'g', 'bias', 'weight', 'input', 'output']) allowed.add(test[key].file);
  allowed.add(oracle.elu.input.file); allowed.add(oracle.elu.output.file);
}
if (oracle.lstm) {
  const required = new Set([1, 3, 9].flatMap(frames => ['zero', 'spread'].map(state => `lstm-${state}-${frames}`)));
  if (oracle.lstm.weights.length !== 2) throw new Error('Missing LSTM layer weights');
  for (const weights of oracle.lstm.weights) for (const value of Object.values(weights)) allowed.add(value.file);
  for (const test of oracle.lstm.cases) {
    if (!required.delete(test.name)) throw new Error('Unexpected or duplicate LSTM case');
    for (const key of ['input', 'h0', 'c0', 'gates', 'output', 'hn', 'cn']) allowed.add(test[key].file);
  }
  if (required.size) throw new Error('Missing LSTM cases');
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let edge, server, socket;
async function gradeInPage(oracle, code) {
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('No WebGPU adapter');
  const device = await adapter.requestDevice();
  const errors = [];
  device.addEventListener('uncapturederror', e => errors.push(e.error.message));
  try {
    const bindings = {};
    for (const m of code.matchAll(/@binding\((\d+)\) var<storage, read_write> ec_rvq_(\w+)_buf/g)) bindings[m[2]] = Number(m[1]);
    const uniform = Number(/@binding\((\d+)\) var<uniform> u_ec_rvq\b/.exec(code)?.[1]);
    if (!['tokens', 'books', 'out'].every(k => k in bindings) || !Number.isFinite(uniform)) throw new Error('Kernel binding schema mismatch');
    const module = device.createShaderModule({ code });
    const messages = (await module.getCompilationInfo()).messages.filter(m => m.type === 'error');
    if (messages.length) throw new Error(messages.map(m => m.message).join('\n'));
    const pipeline = await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: 'ec_rvq_main' } });
    const storage = data => {
      const buffer = device.createBuffer({ size: data.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC });
      device.queue.writeBuffer(buffer, 0, data);
      return buffer;
    };
    const books = storage(new Float32Array(await (await fetch('/' + oracle.books.file)).arrayBuffer()));
    const results = [];
    for (const test of oracle.cases) {
      const frames = test.codes[0][0].length, n = frames * 128, sentinel = 1234567;
      const token = storage(new Int32Array(test.codes[0].flat()));
      const output = storage(new Int32Array(n + 64).fill(sentinel));
      const uniforms = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
      device.queue.writeBuffer(uniforms, 0, new Int32Array([frames, 128, 2048, 0]));
      const readback = device.createBuffer({ size: (n + 64) * 4, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
      const group = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [
        { binding: bindings.tokens, resource: { buffer: token } }, { binding: bindings.books, resource: { buffer: books } },
        { binding: bindings.out, resource: { buffer: output } }, { binding: uniform, resource: { buffer: uniforms } },
      ] });
      device.pushErrorScope('validation');
      const encoder = device.createCommandEncoder(), pass = encoder.beginComputePass();
      pass.setPipeline(pipeline); pass.setBindGroup(0, group); pass.dispatchWorkgroups(Math.ceil((n + 64) / 64)); pass.end();
      encoder.copyBufferToBuffer(output, 0, readback, 0, (n + 64) * 4); device.queue.submit([encoder.finish()]);
      const error = await device.popErrorScope(); if (error) throw new Error(error.message);
      await readback.mapAsync(GPUMapMode.READ);
      const raw = readback.getMappedRange(), actual = new Float32Array(raw), words = new Int32Array(raw);
      const expected = new Float32Array(await (await fetch('/' + test.latent.file)).arrayBuffer());
      if (expected.length !== n || test.latent.shape.join(',') !== `1,128,${frames}`) throw new Error('Latent shape mismatch');
      let bad = 0, worst = 0;
      for (let i = 0; i < n; i++) { if (actual[i] !== expected[i]) bad++; worst = Math.max(worst, Math.abs(actual[i] - expected[i])); }
      const tail = words.slice(n).some(v => v !== sentinel);
      results.push({ name: test.name, bad, worst, tail });
      readback.unmap();
      for (const buffer of [token, output, uniforms, readback]) buffer.destroy();
    }
    if (errors.length) throw new Error(errors.join('\n'));
    return results;
  } finally { device.destroy(); }
}
async function gradeConvsInPage(oracle, code, wrongTrim = false) {
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('No WebGPU adapter');
  const device = await adapter.requestDevice();
  try {
    const module = device.createShaderModule({ code });
    const messages = (await module.getCompilationInfo()).messages.filter(m => m.type === 'error');
    if (messages.length) throw new Error(messages.map(m => m.message).join('\n'));
    const read = async descriptor => new Float32Array(await (await fetch('/' + descriptor.file)).arrayBuffer());
    const pipelines = new Map();
    const run = async (kernel, inputs, outputName, n, scalars) => {
      device.pushErrorScope('validation');
      if (!pipelines.has(kernel)) pipelines.set(kernel, await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: kernel + '_main' } }));
      const pipeline = pipelines.get(kernel), buffers = [], entries = [], sentinel = 1234567;
      let output;
      const regex = new RegExp(`@binding\\((\\d+)\\) var<storage, read_write> ${kernel}_(\\w+)_buf`, 'g');
      for (const binding of code.matchAll(regex)) {
        const name = binding[2], data = name === outputName ? new Int32Array(n + 64).fill(sentinel) : inputs[name];
        if (!data) throw new Error(`Missing ${kernel} buffer ${name}`);
        const buffer = device.createBuffer({ size: data.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC });
        device.queue.writeBuffer(buffer, 0, data); buffers.push(buffer);
        entries.push({ binding: Number(binding[1]), resource: { buffer } });
        if (name === outputName) output = buffer;
      }
      if (!output) throw new Error('Missing output binding');
      const uniform = Number(new RegExp(`@binding\\((\\d+)\\) var<uniform> u_${kernel}\\b`).exec(code)?.[1]);
      const values = new Int32Array(Math.ceil(scalars.length / 4) * 4); values.set(scalars);
      const uniforms = device.createBuffer({ size: values.byteLength, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
      device.queue.writeBuffer(uniforms, 0, values); buffers.push(uniforms);
      entries.push({ binding: uniform, resource: { buffer: uniforms } });
      const group = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries });
      const readback = device.createBuffer({ size: (n + 64) * 4, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST }); buffers.push(readback);
      const encoder = device.createCommandEncoder(), pass = encoder.beginComputePass();
      pass.setPipeline(pipeline); pass.setBindGroup(0, group);
      const groups = Math.ceil((n + 64) / 64), gx = Math.min(groups, 65535);
      pass.dispatchWorkgroups(gx, Math.ceil(groups / gx)); pass.end();
      encoder.copyBufferToBuffer(output, 0, readback, 0, (n + 64) * 4); device.queue.submit([encoder.finish()]);
      const error = await device.popErrorScope(); if (error) throw new Error(error.message);
      await readback.mapAsync(GPUMapMode.READ);
      const raw = readback.getMappedRange();
      const tail = new Int32Array(raw).slice(n).some(v => v !== sentinel);
      const result = new Float32Array(raw).slice(0, n); readback.unmap();
      for (const buffer of buffers) buffer.destroy();
      if (tail) throw new Error(`${kernel} overwrote the output tail`);
      return result;
    };
    const compare = (name, actual, expected, tolerance) => {
      if (actual.length !== expected.length) throw new Error(`${name}: output shape mismatch`);
      let bad = 0, worst = 0;
      for (let i = 0; i < actual.length; i++) {
        const error = Math.abs(actual[i] - expected[i]) / (1 + Math.abs(expected[i]));
        if (!Number.isFinite(error) || error > tolerance) bad++;
        worst = Math.max(worst, error);
      }
      return { name, bad, worst, tolerance };
    };
    const results = [], weights = new Map();
    for (const test of oracle.convs) {
      if (!weights.has(test.weight.file)) {
        const v = await read(test.v), g = await read(test.g), rows = g.length, len = v.length / rows;
        const scales = await run('ec_weight_scale', { vb: v, gb: g }, 'sb', rows, [len, rows]);
        const normalized = await run('ec_weight_apply', { vb: v, sb: scales }, 'wb', v.length, [len, v.length]);
        results.push(compare(test.name + '-weight', normalized, await read(test.weight), 0.000001));
        weights.set(test.weight.file, normalized);
      }
      const xb = await read(test.input), wb = weights.get(test.weight.file), bb = await read(test.bias);
      const expected = await read(test.output), transposed = test.kind === 'transpose';
      const scalars = transposed ? [test.cin, test.cout, test.frames, test.kernel, test.stride, test.left + (wrongTrim ? 1 : 0)] : [test.cin, test.cout, test.frames, test.kernel, test.left];
      const actual = await run(transposed ? 'ec_conv_transpose' : 'ec_conv1d', { xb, wb, bb }, 'yb', expected.length, scalars);
      results.push(compare(test.name, actual, expected, 0.00001));
    }
    const x = await read(oracle.elu.input), expected = await read(oracle.elu.output);
    results.push(compare('elu', await run('ec_elu', { xb: x }, 'yb', x.length, [x.length]), expected, 0.000001));
    return results;
  } finally { device.destroy(); }
}
async function gradeLstmInPage(oracle, code, eraseCell = false) {
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('No WebGPU adapter');
  const device = await adapter.requestDevice({ requiredLimits: { maxStorageBuffersPerShaderStage: 8 } });
  try {
    const module = device.createShaderModule({ code });
    const messages = (await module.getCompilationInfo()).messages.filter(m => m.type === 'error');
    if (messages.length) throw new Error(messages.map(m => m.message).join('\n'));
    const pipelines = {};
    for (const kernel of ['ec_lstm_gates', 'ec_lstm_state']) pipelines[kernel] = await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: kernel + '_main' } });
    const read = async d => new Float32Array(await (await fetch('/' + d.file)).arrayBuffer());
    const storage = (array, owned) => {
      const buffer = device.createBuffer({ size: array.byteLength, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC });
      device.queue.writeBuffer(buffer, 0, array); owned.push(buffer); return buffer;
    };
    const readback = async (buffer, n) => {
      const out = device.createBuffer({ size: n * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
      const encoder = device.createCommandEncoder(); encoder.copyBufferToBuffer(buffer, 0, out, 0, n * 4); device.queue.submit([encoder.finish()]);
      await out.mapAsync(GPUMapMode.READ); const result = new Float32Array(out.getMappedRange()).slice(); out.unmap(); out.destroy(); return result;
    };
    const padded = data => { const result = new Float32Array(data.length + 64).fill(12345); result.set(data); return result; };
    const checked = async (buffer, n) => { const result = await readback(buffer, n + 64); if (result.slice(n).some(v => v !== 12345)) throw new Error('LSTM wrote beyond its output'); return result.slice(0, n); };
    const launch = (kernel, buffers, scalars, n, owned) => {
      const fields = new RegExp(`@binding\\((\\d+)\\) var<storage, read_write> ${kernel}_(\\w+)_buf`, 'g');
      const entries = [...code.matchAll(fields)].map(m => { if (!buffers[m[2]]) throw new Error('Missing LSTM buffer ' + m[2]); return { binding: Number(m[1]), resource: { buffer: buffers[m[2]] } }; });
      const binding = Number(new RegExp(`@binding\\((\\d+)\\) var<uniform> u_${kernel}\\b`).exec(code)?.[1]);
      const data = new Int32Array(4); data.set(scalars);
      const uniform = device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST }); owned.push(uniform); device.queue.writeBuffer(uniform, 0, data);
      entries.push({ binding, resource: { buffer: uniform } });
      const pipeline = pipelines[kernel], group = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries });
      const encoder = device.createCommandEncoder(), pass = encoder.beginComputePass(); pass.setPipeline(pipeline); pass.setBindGroup(0, group); pass.dispatchWorkgroups(Math.ceil((n + 64) / 64)); pass.end(); device.queue.submit([encoder.finish()]);
    };
    const weightsOwned = [], weights = [];
    for (const layer of oracle.lstm.weights) {
      const w = {};
      for (const [key, value] of Object.entries(layer)) w[key] = storage(await read(value), weightsOwned);
      weights.push(w);
    }
    const compare = (name, actual, expected) => {
      if (actual.length !== expected.length) throw new Error('LSTM shape mismatch');
      let bad = 0, worst = 0;
      for (let i = 0; i < actual.length; i++) { const error = Math.abs(actual[i] - expected[i]) / (1 + Math.abs(expected[i])); if (!Number.isFinite(error) || error > 0.00001) bad++; worst = Math.max(worst, error); }
      return { name, bad, worst, tolerance: 0.00001 };
    };
    const results = [];
    for (const test of oracle.lstm.cases) {
      const owned = [], width = 1024, frames = test.frames;
      let xb = storage(await read(test.input), owned);
      const h0 = await read(test.h0), c0 = await read(test.c0), hn = await read(test.hn), cn = await read(test.cn);
      device.pushErrorScope('validation');
      for (let layer = 0; layer < 2; layer++) {
        const w = weights[layer];
        let hb = storage(padded(h0.slice(layer * width, (layer + 1) * width)), owned), cb = storage(padded(eraseCell ? new Float32Array(width) : c0.slice(layer * width, (layer + 1) * width)), owned);
        let nextH = storage(padded(new Float32Array(width)), owned), nextC = storage(padded(new Float32Array(width)), owned);
        const gb = storage(padded(new Float32Array(width * 4)), owned), yb = storage(padded(new Float32Array(width * frames)), owned);
        for (let time = 0; time < frames; time++) {
          launch('ec_lstm_gates', { xb, hb, wi: w.weight_ih, wh: w.weight_hh, bi: w.bias_ih, bh: w.bias_hh, gb }, [frames, time, width], width * 4, owned);
          if (layer === 0 && time === 0) results.push(compare(test.name + '-gates', await checked(gb, width * 4), await read(test.gates)));
          launch('ec_lstm_state', { gb, cb, hn: nextH, cn: nextC, yb }, [frames, time, width], width, owned);
          [hb, nextH] = [nextH, hb]; [cb, nextC] = [nextC, cb];
        }
        results.push(compare(test.name + `-h${layer}`, await checked(hb, width), hn.slice(layer * width, (layer + 1) * width)));
        results.push(compare(test.name + `-c${layer}`, await checked(cb, width), cn.slice(layer * width, (layer + 1) * width)));
        xb = yb;
      }
      results.push(compare(test.name + '-output', await checked(xb, width * frames), await read(test.output)));
      const error = await device.popErrorScope(); if (error) throw new Error(error.message);
      for (const buffer of owned) buffer.destroy();
    }
    for (const buffer of weightsOwned) buffer.destroy();
    return results;
  } finally { device.destroy(); }
}
try {
  server = http.createServer((request, response) => {
    const path = request.url.slice(1);
    if (!path) { response.end('<!doctype html><title>EnCodec kernel grade</title>'); return; }
    if (!allowed.has(path)) { response.writeHead(404); response.end(); return; }
    response.end(readFileSync(join(reference, path)));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', `http://127.0.0.1:${server.address().port}/`], { stdio: 'ignore' });
  console.log(`EnCodec browser PID ${edge.pid}; evidence ${work}`);
  let target;
  for (let i = 0; i < 120 && !target; i++) {
    try { const port = readFileSync(join(work, 'profile/DevToolsActivePort'), 'utf8').split('\n')[0]; target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
    if (!target) await delay(250);
  }
  if (!target) throw new Error('Browser startup timeout');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve); socket.addEventListener('error', reject); });
  let sequence = 0;
  const evaluate = expression => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => reject(new Error('Browser grade timeout')), 120000);
    const receive = event => { const reply = JSON.parse(event.data); if (reply.id !== id) return; clearTimeout(timer); socket.removeEventListener('message', receive); if (reply.error || reply.result?.exceptionDetails) reject(new Error(JSON.stringify(reply.error || reply.result.exceptionDetails))); else resolve(reply.result.result.value); };
    socket.addEventListener('message', receive);
    socket.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression, awaitPromise: true, returnByValue: true } }));
  });
  const run = code => evaluate(`(${gradeInPage.toString()})(${JSON.stringify(oracle)},${JSON.stringify(code)})`);
  let ready = false;
  for (let i = 0; i < 120 && !ready; i++) {
    ready = await evaluate('location.hostname === "127.0.0.1" && !!navigator.gpu');
    if (!ready) await delay(250);
  }
  if (!ready) throw new Error('Local page did not expose WebGPU');
  const good = await run(shader);
  for (const result of good) { console.log(result); if (result.bad || result.tail) throw new Error('EnCodec RVQ differs from audiocraft'); }
  const wrongOracle = structuredClone(oracle);
  wrongOracle.cases[0].codes[0][3][0] = 1;
  const control = await evaluate(`(${gradeInPage.toString()})(${JSON.stringify(wrongOracle)},${JSON.stringify(shader)})`);
  if (!control[0].bad) throw new Error('Changed fourth codebook index was not detected');
  console.log('PASS: all fixed-token RVQ outputs exactly match audiocraft; tail untouched; changed-index control fails');
  if (oracle.convs) {
    const convs = await evaluate(`(${gradeConvsInPage.toString()})(${JSON.stringify(oracle)},${JSON.stringify(shader)})`);
    for (const result of convs) { console.log(result); if (result.bad) throw new Error('EnCodec convolution differs from audiocraft'); }
    const control = await evaluate(`(${gradeConvsInPage.toString()})(${JSON.stringify(oracle)},${JSON.stringify(shader)},true)`);
    if (!control.some(r => r.bad && r.name.includes('conv'))) throw new Error('Wrong transpose trim was not detected');
    console.log('PASS: decoder weight normalization, reflection convolution, transpose trimming and ELU; wrong-trim control fails');
  }
  if (oracle.lstm) {
    const results = await evaluate(`(${gradeLstmInPage.toString()})(${JSON.stringify(oracle)},${JSON.stringify(shader)})`);
    for (const result of results) { console.log(result); if (result.bad) throw new Error('EnCodec LSTM differs from PyTorch'); }
    const control = await evaluate(`(${gradeLstmInPage.toString()})(${JSON.stringify(oracle)},${JSON.stringify(shader)},true)`);
    if (!control.some(r => r.bad && r.name.startsWith('lstm-spread'))) throw new Error('Lost initial cell state was not detected');
    console.log('PASS: both LSTM layers, gates, states and sequence outputs; lost-cell-state control fails');
  }
} finally {
  if (socket) socket.close();
  if (edge && edge.exitCode === null) execFileSync('pwsh', ['-NoProfile', '-Command', `$all=@(Get-CimInstance Win32_Process);$ids=[Collections.Generic.List[int]]::new();$ids.Add(${edge.pid});for($i=0;$i -lt $ids.Count;$i++){foreach($p in $all){if($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)){$ids.Add([int]$p.ProcessId)}}};for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}`], { stdio: 'ignore' });
  if (server) server.close();
}
