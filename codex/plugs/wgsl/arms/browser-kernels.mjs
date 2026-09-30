// Run codex/foreword/gpu/BrowserKernels.codex through the WGSL plug on WebGPU
// in headless Edge, each kernel against a double-precision reference within
// the tolerance codex/test/gpu-layer-kernels grades the native kernels by,
// |got - want| <= 1e-6 (1 + |want|), over inputs that are multiples of 1/64 in
// [-16, 16). A kernel launched for more threads than n must leave the tail
// untouched, and a control with a wrong formula must fail.
// Usage: node codex/plugs/wgsl/arms/browser-kernels.mjs   Exit 0 = every arm passed.
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

// In the page: run entry point `ep`. `bufs` is [binding, words, fill] per
// storage binding (words null for the output, which is filled with `fill`),
// `ub` the uniform binding and `scalars` its i32 fields; answers the output.
const runInPage = `async (code, ep, bufs, outBinding, ub, scalars, threads, wg = 64) => {
  const a = await navigator.gpu.requestAdapter(); const d = await a.requestDevice();
  const errs = []; d.pushErrorScope('validation');
  const m = d.createShaderModule({ code });
  for (const x of (await m.getCompilationInfo()).messages) if (x.type === 'error') errs.push(x.message);
  const S = GPUBufferUsage.STORAGE, D = GPUBufferUsage.COPY_DST, entries = []; let out = null, n = 0;
  for (const [bn, words, fill, len] of bufs) {
    const b = d.createBuffer({ size: len * 4, usage: S | D | GPUBufferUsage.COPY_SRC });
    d.queue.writeBuffer(b, 0, words ? new Int32Array(words) : new Int32Array(len).fill(fill));
    entries.push({ binding: bn, resource: { buffer: b } }); if (bn === outBinding) { out = b; n = len; }
  }
  const u = d.createBuffer({ size: 16 * Math.ceil(Math.max(1, scalars.length) / 4), usage: GPUBufferUsage.UNIFORM | D });
  d.queue.writeBuffer(u, 0, new Int32Array(4 * Math.ceil(Math.max(1, scalars.length) / 4)).map((_, i) => scalars[i] || 0));
  entries.push({ binding: ub, resource: { buffer: u } });
  const r = d.createBuffer({ size: n * 4, usage: GPUBufferUsage.MAP_READ | D });
  const p = d.createComputePipeline({ layout: 'auto', compute: { module: m, entryPoint: ep } });
  const bg = d.createBindGroup({ layout: p.getBindGroupLayout(0), entries });
  const e = d.createCommandEncoder(); const c = e.beginComputePass(); c.setPipeline(p); c.setBindGroup(0, bg); c.dispatchWorkgroups(Math.ceil(threads / wg)); c.end();
  e.copyBufferToBuffer(out, 0, r, 0, n * 4); d.queue.submit([e.finish()]);
  const v = await d.popErrorScope(); if (v) errs.push(v.message);
  await r.mapAsync(GPUMapMode.READ); const o = Array.from(new Int32Array(r.getMappedRange())); r.unmap(); d.destroy();
  return { errs, out: o };
}`;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'wgsl-bk-'));
  const wgslFile = join(work, 'bk.wgsl');
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex', 'plugs', 'wgsl', 'run.ps1'), '-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', wgslFile], { stdio: 'inherit' });
  const code = readFileSync(wgslFile, 'utf8');

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end('<!doctype html><title>bk</title>'); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', `http://127.0.0.1:${port}/`], { stdio: 'ignore' });
  const evalIn = await cdpOpen(dbg);
  for (let i = 0; i < 40 && !(await evalIn('!!navigator.gpu')); i++) await sleep(250);

  const N = 2048, n = 2000, FILL = -12345;
  const wordsOf = (fn) => { const f = new Float32Array(N); for (let i = 0; i < N; i++) f[i] = fn(i); return { f, w: Array.from(new Int32Array(f.buffer)) }; };
  const X = wordsOf(i => (i - 1024) / 64), Y = wordsOf(i => ((i * 37) % 2048 - 1024) / 64);
  const asF = v => new Float32Array(new Int32Array([v]).buffer)[0];
  const within = (got, want) => Math.abs(got - want) <= 1e-6 * (1 + Math.abs(want));
  const erf = (x) => { const a = Math.abs(x), t = 1 / (1 + 0.3275911 * a); const e = 1 - t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429)))) * Math.exp(-a * a); return x < 0 ? -e : e; };
  const bindingsOf = (k) => {
    const re = new RegExp(`@binding\\((\\d+)\\) var<storage, read_write> ${k}_(\\w+)_buf`, 'g'), m = {};
    for (const x of code.matchAll(re)) m[x[2]] = Number(x[1]);
    const u = new RegExp(`@binding\\((\\d+)\\) var<uniform> u_${k}\\b`).exec(code);
    return { m, ub: u ? Number(u[1]) : -1 };
  };
  const kernels = [
    { k: 'bk_silu', inputs: { xb: X }, ref: i => X.f[i] / (1 + Math.exp(-X.f[i])), control: s => s.replace('exp2(', 'exp('), controlName: 'e in place of 2 as the base' },
    { k: 'bk_gelu', inputs: { xb: X }, ref: i => 0.5 * X.f[i] * (1 + erf(X.f[i] / Math.SQRT2)), control: s => s.replace('0x1.6a09e6p-1f', '0x1.000000p+0f'), controlName: 'x in place of x / sqrt 2' },
    { k: 'bk_add', inputs: { ab: X, bb: Y }, ref: i => X.f[i] + Y.f[i], control: s => s.replace(/(bk_add_yb_buf\[gid\] = bitcast<i32>\(\()(bitcast<f32>\(bk_add_ab_buf\[gid\]\)) \+/, '$1$2 -'), controlName: 'a - b in place of a + b' },
  ];
  for (const K of kernels) {
    const { m, ub } = bindingsOf(K.k);
    const bufs = [[m.yb, null, FILL, N], ...Object.entries(K.inputs).map(([p, v]) => [m[p], v.w, 0, N])];
    const run = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, '${K.k}_main', ${JSON.stringify(bufs)}, ${m.yb}, ${ub}, [${n}], ${N})`);
    const grade = (res) => {
      let bad = 0, worst = 0;
      for (let i = 0; i < n; i++) { const want = K.ref(i), got = asF(res.out[i]); if (!within(got, want)) bad++; worst = Math.max(worst, Math.abs(got - want) / (1 + Math.abs(want))); }
      return { bad, worst, tail: res.out.slice(n).filter(v => v !== FILL).length, errs: res.errs };
    };
    const real = grade(await run(code));
    ok(`${K.k}: compiles on WebGPU`, real.errs.length === 0, real.errs.join('; ').slice(0, 200));
    ok(`${K.k}: within the native tolerance on every input`, real.bad === 0, `${real.bad} of ${n} outside; worst ${real.worst.toExponential(2)} of 1e-6`);
    ok(`${K.k}: leaves the threads past n untouched`, real.tail === 0, `${real.tail} of ${N - n} written`);
    const mutated = K.control(code);
    const wrong = grade(await run(mutated));
    ok(`${K.k}: control, ${K.controlName}, is caught`, mutated !== code && wrong.bad > 0, `${wrong.bad} of ${n} outside`);
  }

  // GroupNorm: C = 8 channels of HW = 64, 4 groups, so 4 rows of 128.
  {
    const C = 8, HW = 320, G = 4, L = (C / G) * HW, R = G, NN = C * HW, eps = 1e-5;
    const epsBits = new Int32Array(new Float32Array([eps]).buffer)[0];
    const xv = new Float32Array(NN).map((_, i) => ((i * 53) % 2048 - 1024) / 64);
    const xw = Array.from(new Int32Array(xv.buffer.slice(0, NN * 4)));
    const mean = [], rstd = [];
    for (let r = 0; r < R; r++) { let s = 0; for (let j = 0; j < L; j++) s += xv[r * L + j]; const m = s / L; let q = 0; for (let j = 0; j < L; j++) q += (xv[r * L + j] - m) ** 2; mean.push(m); rstd.push(1 / Math.sqrt(q / L + eps)); }
    const mb = bindingsOf('bk_row_mean'), rb = bindingsOf('bk_row_rstd');
    const statsRun = async (src) => {
      const a = await evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_row_mean_main', ${JSON.stringify([[mb.m.sb, null, FILL, R * 2], [mb.m.xb, xw, 0, NN]])}, ${mb.m.sb}, ${mb.ub}, [${L}, ${R}], ${R * 256}, 256)`);
      const b = await evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_row_rstd_main', ${JSON.stringify([[rb.m.sb, null, FILL, R * 2], [rb.m.xb, xw, 0, NN]])}, ${rb.m.sb}, ${rb.ub}, [${L}, ${epsBits}, ${R}], ${R * 256}, 256)`);
      return { errs: a.errs.concat(b.errs), out: a.out.map((v, i) => (i % 2 ? b.out[i] : v)) };
    };
    const statsBad = (res) => { let bad = 0; for (let r = 0; r < R; r++) { if (!within(asF(res.out[r * 2]), mean[r])) bad++; if (!within(asF(res.out[r * 2 + 1]), rstd[r])) bad++; } return bad; };
    const s1 = await statsRun(code);
    ok('bk_row_mean, bk_row_rstd: mean and 1/sqrt(var + eps) of every row within tolerance', s1.errs.length === 0 && statsBad(s1) === 0, `${statsBad(s1)} of ${R * 2} outside ${s1.errs.join('; ').slice(0, 120)}`);
    const rstdMut = code.replace('0x1.000000p+0f / sqrt(', '0x1.000000p+0f / abs(');
    const s2 = await statsRun(rstdMut);
    ok('bk_row_rstd: control, |var + eps| in place of its square root, is caught', rstdMut !== code && statsBad(s2) > 0, `${statsBad(s2)} of ${R * 2} outside`);
    const treeMut = code.replaceAll('bk_wg_level(op, t, 128)', 'bk_wg_level(op, t, 0)');
    const s3 = await statsRun(treeMut);
    ok('bk_row_mean, bk_row_rstd: control, thread 0\'s partial alone without the workgroup tree, is caught', treeMut !== code && statsBad(s3) > 0, `${statsBad(s3)} of ${R * 2} outside`);

    const gv = new Float32Array(C).map((_, i) => 0.5 + i / 8), bv = new Float32Array(C).map((_, i) => i / 16 - 0.25);
    const sv = new Float32Array(R * 2); for (let r = 0; r < R; r++) { sv[r * 2] = mean[r]; sv[r * 2 + 1] = rstd[r]; }
    const w32 = a => Array.from(new Int32Array(a.buffer));
    const ga = bindingsOf('bk_group_affine');
    const affRun = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_group_affine_main', ${JSON.stringify([[ga.m.yb, null, FILL, NN], [ga.m.xb, xw, 0, NN], [ga.m.sb, w32(sv), 0, R * 2], [ga.m.gb, w32(gv), 0, C], [ga.m.bb, w32(bv), 0, C]])}, ${ga.m.yb}, ${ga.ub}, [${C}, ${HW}, ${G}, ${NN}], ${NN})`);
    const affBad = (res) => { let bad = 0; for (let i = 0; i < NN; i++) { const ch = Math.floor(i / HW) % C, row = Math.floor(ch / (C / G)); const want = (xv[i] - sv[row * 2]) * sv[row * 2 + 1] * gv[ch] + bv[ch]; if (!within(asF(res.out[i]), want)) bad++; } return bad; };
    const a1 = await affRun(code);
    ok('bk_group_affine: every element normalized by its own group and channel', a1.errs.length === 0 && affBad(a1) === 0, `${affBad(a1)} of ${NN} outside ${a1.errs.join('; ').slice(0, 120)}`);
    const a2 = await affRun(code.replace(/bk_group_affine_gb_buf\[ch__a\]|bk_group_affine_gb_buf\[ch\]/, 'bk_group_affine_gb_buf[0]'));
    ok('bk_group_affine: control, one gamma for every channel, is caught', affBad(a2) > 0, `${affBad(a2)} of ${NN} outside`);

    // LayerNorm: 8 rows of dim 320, gamma and beta dim wide, over the same x.
    const D = 320, R2 = NN / D, lsv = new Float32Array(R2 * 2);
    for (let r = 0; r < R2; r++) { let s = 0; for (let j = 0; j < D; j++) s += xv[r * D + j]; const m = s / D; let q = 0; for (let j = 0; j < D; j++) q += (xv[r * D + j] - m) ** 2; lsv[r * 2] = m; lsv[r * 2 + 1] = 1 / Math.sqrt(q / D + eps); }
    const lg = new Float32Array(D).map((_, i) => 0.25 + i / 32), lbv = new Float32Array(D).map((_, i) => i / 64 - 0.5);
    const la = bindingsOf('bk_layer_affine');
    const lnRun = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_layer_affine_main', ${JSON.stringify([[la.m.yb, null, FILL, NN + 48], [la.m.xb, xw, 0, NN], [la.m.sb, w32(lsv), 0, R2 * 2], [la.m.gb, w32(lg), 0, D], [la.m.bb, w32(lbv), 0, D]])}, ${la.m.yb}, ${la.ub}, [${D}, ${NN}], ${NN + 48})`);
    const lnBad = (res) => { let bad = 0; for (let i = 0; i < NN; i++) { const r = Math.floor(i / D), c = i % D; const want = (xv[i] - lsv[r * 2]) * lsv[r * 2 + 1] * lg[c] + lbv[c]; if (!within(asF(res.out[i]), want)) bad++; } return bad; };
    const l1 = await lnRun(code);
    ok('bk_layer_affine: every element normalized by its own row and column', l1.errs.length === 0 && lnBad(l1) === 0, `${lnBad(l1)} of ${NN} outside ${l1.errs.join('; ').slice(0, 120)}`);
    ok('bk_layer_affine: leaves the threads past n untouched', l1.out.slice(NN).filter(v => v !== FILL).length === 0, `${l1.out.slice(NN).filter(v => v !== FILL).length} of 48 written`);
    const lnMut = code.replace(/bk_layer_affine_gb_buf\[ch\]/, 'bk_layer_affine_gb_buf[0]');
    const l2 = await lnRun(lnMut);
    ok('bk_layer_affine: control, one gamma for every column, is caught', lnMut !== code && lnBad(l2) > 0, `${lnBad(l2)} of ${NN} outside`);

    // Softmax: 8 rows of 320 scores in [-16, 16), out of place.
    const smWant = new Float64Array(NN);
    for (let r = 0; r < R2; r++) { let m = -Infinity; for (let j = 0; j < D; j++) m = Math.max(m, xv[r * D + j]); let z = 0; for (let j = 0; j < D; j++) z += Math.exp(xv[r * D + j] - m); for (let j = 0; j < D; j++) smWant[r * D + j] = Math.exp(xv[r * D + j] - m) / z; }
    const sm = bindingsOf('bk_softmax');
    const smRun = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_softmax_main', ${JSON.stringify([[sm.m.yb, null, FILL, NN + 48], [sm.m.sb, xw, 0, NN]])}, ${sm.m.yb}, ${sm.ub}, [${D}, ${NN}], ${NN + 48})`);
    const smGrade = (res) => { let bad = 0, worst = 0; for (let i = 0; i < NN; i++) { const got = asF(res.out[i]); if (!within(got, smWant[i])) bad++; worst = Math.max(worst, Math.abs(got - smWant[i]) / (1 + Math.abs(smWant[i]))); } return { bad, worst }; };
    const m1 = await smRun(code), g1 = smGrade(m1);
    ok('bk_softmax: every row within tolerance', m1.errs.length === 0 && g1.bad === 0, `${g1.bad} of ${NN} outside; worst ${g1.worst.toExponential(2)} of 1e-6 ${m1.errs.join('; ').slice(0, 120)}`);
    ok('bk_softmax: leaves the threads past n untouched', m1.out.slice(NN).filter(v => v !== FILL).length === 0, `${m1.out.slice(NN).filter(v => v !== FILL).length} of 48 written`);
    const smMut = code.replace(/(fn bk_exp_of[^}]*?)exp2\(/, '$1exp(');
    const g2 = smGrade(await smRun(smMut));
    ok('bk_softmax: control, e in place of 2 as the base, is caught', smMut !== code && g2.bad > 0, `${g2.bad} of ${NN} outside`);

    // The three-launch softmax over the same rows, a workgroup reduction per
    // row, so within tolerance rather than bk_softmax bit for bit.
    const rm = bindingsOf('bk_row_max'), rx = bindingsOf('bk_row_expsum'), sa = bindingsOf('bk_softmax_apply');
    const rowRun = async (shift) => {
      const a = await evalIn(`(${runInPage})(${JSON.stringify(code)}, 'bk_row_max_main', ${JSON.stringify([[rm.m.mb, null, FILL, R2 * 2], [rm.m.sb, xw, 0, NN]])}, ${rm.m.mb}, ${rm.ub}, [${D}, ${R2}], ${R2 * 256}, 256)`);
      const b = await evalIn(`(${runInPage})(${JSON.stringify(code)}, 'bk_row_expsum_main', ${JSON.stringify([[rx.m.mb, a.out, 0, R2 * 2], [rx.m.sb, xw, 0, NN]])}, ${rx.m.mb}, ${rx.ub}, [${D}, ${R2}], ${R2 * 256}, 256)`);
      const st = shift ? b.out.slice(2).concat(b.out.slice(0, 2)) : b.out;
      const c = await evalIn(`(${runInPage})(${JSON.stringify(code)}, 'bk_softmax_apply_main', ${JSON.stringify([[sa.m.yb, null, FILL, NN + 48], [sa.m.sb, xw, 0, NN], [sa.m.mb, st, 0, R2 * 2]])}, ${sa.m.yb}, ${sa.ub}, [${D}, ${NN}], ${NN + 48})`);
      return { errs: a.errs.concat(b.errs, c.errs), out: c.out };
    };
    const w1 = await rowRun(false), wg1 = smGrade(w1);
    const same = w1.out.slice(0, NN).filter((v, i) => v !== m1.out[i]).length;
    ok('bk_row_max, bk_row_expsum, bk_softmax_apply: every row within tolerance', w1.errs.length === 0 && wg1.bad === 0, `${same} of ${NN} differ from bk_softmax (not graded),${wg1.bad} outside ${w1.errs.join('; ').slice(0, 120)}`);
    ok('bk_softmax_apply: leaves the threads past n untouched', w1.out.slice(NN).filter(v => v !== FILL).length === 0, `${w1.out.slice(NN).filter(v => v !== FILL).length} of 48 written`);
    const wg2 = smGrade(await rowRun(true));
    ok('bk_softmax_apply: control, each row given the next row\'s max and sum, is caught', wg2.bad > 0, `${wg2.bad} of ${NN} outside`);
  }

  // Layout: one launch per kernel, n + 48 threads, the output
  // filled with FILL so the tail is visible. Copies are graded bit-exact.
  {
    const launch = (src, k, ins, scalars, n) => { const b = bindingsOf(k); return evalIn(`(${runInPage})(${JSON.stringify(src)}, '${k}_main', ${JSON.stringify([[b.m.yb, null, FILL, n + 48], ...ins.map(([p, w]) => [b.m[p], w, 0, w.length])])}, ${b.m.yb}, ${b.ub}, ${JSON.stringify(scalars)}, ${n + 48})`); };
    const tail = (res, n) => res.out.slice(n).filter(v => v !== FILL).length;
    const exactBad = (res, want) => want.reduce((bad, w, i) => bad + (res.out[i] === w ? 0 : 1), 0);

    // Concat: 2 batches, 3 channels of a then 5 of b, 16 wide.
    const BT = 2, CA = 3, CB = 5, IN = 16, cn = BT * (CA + CB) * IN;
    const aw = X.w.slice(0, BT * CA * IN), bw = Y.w.slice(0, BT * CB * IN), cWant = [];
    for (let i = 0; i < cn; i++) { const pl = Math.floor(i / IN), bt = Math.floor(pl / (CA + CB)), ch = pl % (CA + CB), off = i % IN; cWant.push(ch < CA ? aw[(bt * CA + ch) * IN + off] : bw[(bt * CB + ch - CA) * IN + off]); }
    const c1 = await launch(code, 'bk_concat', [['ab', aw], ['bb', bw]], [CA, CB, IN, cn], cn);
    ok('bk_concat: every element from its own source, bit-exact', c1.errs.length === 0 && exactBad(c1, cWant) === 0, `${exactBad(c1, cWant)} of ${cn} wrong ${c1.errs.join('; ').slice(0, 120)}`);
    ok('bk_concat: leaves the threads past n untouched', tail(c1, cn) === 0, `${tail(c1, cn)} of 48 written`);
    const cMut = code.replace('if ((ch < ca))', 'if ((ch <= ca))');
    const c2 = await launch(cMut, 'bk_concat', [['ab', aw], ['bb', bw]], [CA, CB, IN, cn], cn);
    ok('bk_concat: control, the boundary channel taken from a, is caught', cMut !== code && exactBad(c2, cWant) > 0, `${exactBad(c2, cWant)} of ${cn} wrong`);

    // Nearest upsample x2: 3 planes of 4 x 5.
    const PL = 3, UH = 4, UW = 5, un = PL * 4 * UH * UW, xu = X.w.slice(0, PL * UH * UW), uWant = [];
    for (let i = 0; i < un; i++) { const pl = Math.floor(i / (4 * UH * UW)), r = i % (4 * UH * UW), oy = Math.floor(r / (2 * UW)), ox = r % (2 * UW); uWant.push(xu[pl * UH * UW + Math.floor(oy / 2) * UW + Math.floor(ox / 2)]); }
    const u1 = await launch(code, 'bk_upsample', [['xb', xu]], [UH, UW, un], un);
    ok('bk_upsample: every element from its nearest source, bit-exact', u1.errs.length === 0 && exactBad(u1, uWant) === 0, `${exactBad(u1, uWant)} of ${un} wrong ${u1.errs.join('; ').slice(0, 120)}`);
    ok('bk_upsample: leaves the threads past n untouched', tail(u1, un) === 0, `${tail(u1, un)} of 48 written`);
    const uMut = code.replace('((oy / 2) * w)', '(oy * w)');
    const u2 = await launch(uMut, 'bk_upsample', [['xb', xu]], [UH, UW, un], un);
    ok('bk_upsample: control, the row not halved, is caught', uMut !== code && exactBad(u2, uWant) > 0, `${exactBad(u2, uWant)} of ${un} wrong`);

    // Timestep embedding, graded against Forge's own fp32 arithmetic (LDM
    // timestep_embedding: freqs = exp(-ln(P) k / half), args = t freqs, both
    // float32), within 1e-6 (1 + |want|) plus 2^-21 |arg|, a few f32 ulps of the
    // argument. The gap to f64 is printed beside it, not graded.
    const f = Math.fround, TS = [1, 50, 500, 999], TD = 64, TH = TD / 2, tn = TS.length * TD, lp = Math.log(10000);
    const tw = Array.from(new Int32Array(new Float32Array(TS).buffer)), lpBits = new Int32Array(new Float32Array([lp]).buffer)[0];
    const tArg = [], tWant = [], tF64 = [];
    for (let i = 0; i < tn; i++) { const t = TS[Math.floor(i / TD)], j = i % TD, k = j < TH ? j : j - TH; const a = f(t * f(Math.exp(f(f(-f(lp) * k) / TH)))); tArg.push(a); tWant.push(f(j < TH ? Math.cos(a) : Math.sin(a))); const a64 = t * Math.exp(-lp * k / TH); tF64.push(j < TH ? Math.cos(a64) : Math.sin(a64)); }
    const tGrade = (res) => { let bad = 0, worst = 0, gap = 0; for (let i = 0; i < tn; i++) { const got = asF(res.out[i]), tol = 1e-6 * (1 + Math.abs(tWant[i])) + Math.abs(tArg[i]) * 2 ** -21; if (Math.abs(got - tWant[i]) > tol) bad++; worst = Math.max(worst, Math.abs(got - tWant[i]) / tol); gap = Math.max(gap, Math.abs(got - tF64[i])); } return { bad, worst, gap }; };
    const t1 = await launch(code, 'bk_timestep', [['tb', tw]], [TD, lpBits, tn], tn), tg1 = tGrade(t1);
    ok('bk_timestep: every element within Forge fp32 tolerance', t1.errs.length === 0 && tg1.bad === 0, `${tg1.bad} of ${tn} outside; worst ${tg1.worst.toFixed(3)} of the tolerance; gap to f64 ${tg1.gap.toExponential(2)} ${t1.errs.join('; ').slice(0, 120)}`);
    ok('bk_timestep: leaves the threads past n untouched', tail(t1, tn) === 0, `${tail(t1, tn)} of 48 written`);
    const tMut1 = code.replace('return cos((tv', 'return sin((tv');
    const tg2 = tGrade(await launch(tMut1, 'bk_timestep', [['tb', tw]], [TD, lpBits, tn], tn));
    ok('bk_timestep: control, sin in place of cos, is caught', tMut1 !== code && tg2.bad > 0, `${tg2.bad} of ${tn} outside`);
    const tMut2 = code.replace('/ f32(f32(half))', '/ f32(f32((half + 1)))');
    const tg3 = tGrade(await launch(tMut2, 'bk_timestep', [['tb', tw]], [TD, lpBits, tn], tn));
    ok('bk_timestep: control, half + 1 in the cosine frequency, is caught', tMut2 !== code && tg3.bad > 0, `${tg3.bad} of ${tn} outside`);

    // Linear y = x w^T + b, 64 x 64 tiles, 4 x 4 outputs a thread: mm 137,
    // nn 145, kk 70 leave partial tiles on every edge. Inputs are multiples of
    // 1/64 in [-1, 1), so every product and partial sum is exact in f32.
    const LM = 137, LN = 145, LK = 70, tilesN = Math.ceil(LN / 64), tiles = Math.ceil(LM / 64) * tilesN, ln = LM * LN;
    const q = (i, s) => ((i * s) % 128 - 64) / 64;
    const lx = new Float32Array(LM * LK).map((_, i) => q(i, 37)), lw = new Float32Array(LN * LK).map((_, i) => q(i, 53)), lb = new Float32Array(LN).map((_, i) => q(i, 11));
    const yWant = []; for (let r = 0; r < LM; r++) for (let c = 0; c < LN; c++) { let s = lb[c]; for (let k = 0; k < LK; k++) s += lx[r * LK + k] * lw[c * LK + k]; yWant.push(s); }
    const f32w = a => Array.from(new Int32Array(a.buffer));
    const lnb = bindingsOf('bk_linear');
    const linRun = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_linear_main', ${JSON.stringify([[lnb.m.yb, null, FILL, ln + 48], [lnb.m.xb, f32w(lx), 0, LM * LK], [lnb.m.wb, f32w(lw), 0, LN * LK], [lnb.m.bb, f32w(lb), 0, LN]])}, ${lnb.m.yb}, ${lnb.ub}, [${LM}, ${LN}, ${LK}], ${tiles * 256}, 256)`);
    const linBad = (res) => yWant.reduce((bad, w, i) => bad + (within(asF(res.out[i]), w) ? 0 : 1), 0);
    const y1 = await linRun(code);
    ok('bk_linear: every element of x w^T + b within tolerance', y1.errs.length === 0 && linBad(y1) === 0, `${linBad(y1)} of ${ln} outside ${y1.errs.join('; ').slice(0, 160)}`);
    ok('bk_linear: leaves the elements past mm x nn untouched', tail(y1, ln) === 0, `${tail(y1, ln)} of 48 written`);
    const yMut1 = code.replaceAll('cx_shared[((((1040 + (k * 65)) + lx) * 4)) / 4]', 'cx_shared[((((1040 + (lx * 65)) + k) * 4)) / 4]');
    const y2 = await linRun(yMut1);
    ok('bk_linear: control, w tile read untransposed, is caught', yMut1 !== code && linBad(y2) > 0, `${linBad(y2)} of ${ln} outside`);
    const yMut2 = code.replace('(v + bitcast<f32>(bk_linear_bb_buf[col]))', 'v');
    const y3 = await linRun(yMut2);
    ok('bk_linear: control, bias dropped, is caught', yMut2 !== code && linBad(y3) > 0, `${linBad(y3)} of ${ln} outside`);
    const yMut3 = code.replaceAll('(col + 48), a03)', '(col + 48), a02)');
    const y4 = await linRun(yMut3);
    ok('bk_linear: control, a thread\'s fourth column stored from its third accumulator, is caught', yMut3 !== code && linBad(y4) > 0, `${linBad(y4)} of ${ln} outside`);

    // bk_linear_h: the same product with w packed as f16 (element i the low
    // half of word i / 2 when even). The weights are exact in f16, so the
    // output must equal bk_linear's bit for bit.
    const f16 = (x) => { const f = new Float32Array([x]), u = new Uint32Array(f.buffer)[0], s = (u >>> 16) & 0x8000, e = ((u >>> 23) & 0xff) - 112, m = u & 0x7fffff; if (e <= 0) return s; let h = s | (e << 10) | (m >>> 13); const rest = m & 0x1fff; if (rest > 0x1000 || (rest === 0x1000 && (h & 1))) h++; return h; };
    const packH = (a) => { const w = new Int32Array(Math.ceil(a.length / 2)); for (let i = 0; i < a.length; i++) w[i >> 1] |= f16(a[i]) << ((i & 1) * 16); return Array.from(w); };
    const lhb = bindingsOf('bk_linear_h');
    const linHRun = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_linear_h_main', ${JSON.stringify([[lhb.m.yb, null, FILL, ln + 48], [lhb.m.xb, f32w(lx), 0, LM * LK], [lhb.m.wb, packH(lw), 0, Math.ceil(LN * LK / 2)], [lhb.m.bb, f32w(lb), 0, LN]])}, ${lhb.m.yb}, ${lhb.ub}, [${LM}, ${LN}, ${LK}], ${tiles * 256}, 256)`);
    const h1 = await linHRun(code);
    ok('bk_linear_h: f16 weights give bk_linear\'s f32 output bit for bit', h1.errs.length === 0 && exactBad(h1, y1.out.slice(0, ln)) === 0, `${exactBad(h1, y1.out.slice(0, ln))} of ${ln} differ ${h1.errs.join('; ').slice(0, 160)}`);
    ok('bk_linear_h: leaves the elements past mm x nn untouched', tail(h1, ln) === 0, `${tail(h1, ln)} of 48 written`);
    const hMut = code.replaceAll('[(((r * cols) + c)) % 2]', '[((((r * cols) + c)) + 1) % 2]');
    const h2 = await linHRun(hMut);
    ok('bk_linear_h: control, the two halves of a word swapped, is caught', hMut !== code && exactBad(h2, y1.out.slice(0, ln)) > 0, `${exactBad(h2, y1.out.slice(0, ln))} of ${ln} differ`);

    // Conv2d as an implicit GEMM: cin 3, 13 x 11, cout 70, 3 x 3, stride 1 and
    // 2, pad 1 (at stride 1, two 64-tiles of cout and three of pixels, every
    // edge partial). Inputs are multiples of 1/64 in [-1, 1), exact in f32.
    const CI = 3, CH = 13, CW = 11, CO = 70, KH = 3, KW = 3, cb = bindingsOf('bk_conv2d');
    const cx = new Float32Array(CI * CH * CW).map((_, i) => q(i, 29)), cwt = new Float32Array(CO * CI * KH * KW).map((_, i) => q(i, 43)), cbias = new Float32Array(CO).map((_, i) => q(i, 7));
    const convWant = (st, pd, oh, ow) => { const o = []; for (let m = 0; m < CO; m++) for (let n = 0; n < oh * ow; n++) { let s = cbias[m]; const oy = Math.floor(n / ow), ox = n % ow; for (let ci = 0; ci < CI; ci++) for (let ky = 0; ky < KH; ky++) for (let kx = 0; kx < KW; kx++) { const iy = oy * st - pd + ky, ix = ox * st - pd + kx; if (iy >= 0 && iy < CH && ix >= 0 && ix < CW) s += cx[(ci * CH + iy) * CW + ix] * cwt[m * CI * KH * KW + (ci * KH + ky) * KW + kx]; } o.push(s); } return o; };
    const convRun = (src, st, pd, oh, ow) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_conv2d_main', ${JSON.stringify([[cb.m.yb, null, FILL, CO * oh * ow + 48], [cb.m.xb, f32w(cx), 0, cx.length], [cb.m.wb, f32w(cwt), 0, cwt.length], [cb.m.bb, f32w(cbias), 0, CO]])}, ${cb.m.yb}, ${cb.ub}, [${CI}, ${CH}, ${CW}, ${CO}, ${KH}, ${KW}, ${st}, ${pd}, ${oh}, ${ow}], ${Math.ceil(CO / 64) * Math.ceil(oh * ow / 64) * 256}, 256)`);
    const convBad = (res, want) => want.reduce((bad, w, i) => bad + (within(asF(res.out[i]), w) ? 0 : 1), 0);
    for (const [st, pd] of [[1, 1], [2, 1]]) {
      const oh = Math.floor((CH + 2 * pd - KH) / st) + 1, ow = Math.floor((CW + 2 * pd - KW) / st) + 1, want = convWant(st, pd, oh, ow), cn = CO * oh * ow;
      const r1 = await convRun(code, st, pd, oh, ow);
      ok(`bk_conv2d stride ${st} pad ${pd}: every element within tolerance`, r1.errs.length === 0 && convBad(r1, want) === 0, `${convBad(r1, want)} of ${cn} outside ${r1.errs.join('; ').slice(0, 160)}`);
      ok(`bk_conv2d stride ${st} pad ${pd}: leaves the elements past cout x oh ow untouched`, tail(r1, cn) === 0, `${tail(r1, cn)} of 48 written`);
    }
    {
      const oh = CH, ow = CW, want = convWant(1, 1, oh, ow), cn = CO * oh * ow;
      const pMut = code.replaceAll('((((n / ow) * stride) - pad) + (rest / kw))', '(((n / ow) * stride) + (rest / kw))');
      const r2 = await convRun(pMut, 1, 1, oh, ow);
      ok('bk_conv2d: control, the vertical pad dropped, is caught', pMut !== code && convBad(r2, want) > 0, `${convBad(r2, want)} of ${cn} outside`);
      const bMut = code.replace('bk_conv2d_bb_buf[m]', 'bk_conv2d_bb_buf[0]');
      const r3 = await convRun(bMut, 1, 1, oh, ow);
      ok('bk_conv2d: control, one bias for every channel, is caught', bMut !== code && convBad(r3, want) > 0, `${convBad(r3, want)} of ${cn} outside`);
      const aMut = code.replaceAll('(n + 48), a03)', '(n + 48), a02)');
      const r4 = await convRun(aMut, 1, 1, oh, ow);
      ok('bk_conv2d: control, a thread\'s fourth pixel stored from its third accumulator, is caught', aMut !== code && convBad(r4, want) > 0, `${convBad(r4, want)} of ${cn} outside`);
      const chb = bindingsOf('bk_conv2d_h');
      const convHRun = (src) => evalIn(`(${runInPage})(${JSON.stringify(src)}, 'bk_conv2d_h_main', ${JSON.stringify([[chb.m.yb, null, FILL, cn + 48], [chb.m.xb, f32w(cx), 0, cx.length], [chb.m.wb, packH(cwt), 0, Math.ceil(cwt.length / 2)], [chb.m.bb, f32w(cbias), 0, CO]])}, ${chb.m.yb}, ${chb.ub}, [${CI}, ${CH}, ${CW}, ${CO}, ${KH}, ${KW}, 1, 1, ${oh}, ${ow}], ${Math.ceil(CO / 64) * Math.ceil(oh * ow / 64) * 256}, 256)`);
      const c32 = await convRun(code, 1, 1, oh, ow), c16 = await convHRun(code);
      ok('bk_conv2d_h: f16 weights give bk_conv2d\'s f32 output bit for bit', c16.errs.length === 0 && exactBad(c16, c32.out.slice(0, cn)) === 0, `${exactBad(c16, c32.out.slice(0, cn))} of ${cn} differ ${c16.errs.join('; ').slice(0, 160)}`);
      ok('bk_conv2d_h: leaves the elements past cout x oh ow untouched', tail(c16, cn) === 0, `${tail(c16, cn)} of 48 written`);
      const chMut = code.replaceAll('[(((r * cols) + c)) % 2]', '[((((r * cols) + c)) + 1) % 2]');
      const c16m = await convHRun(chMut);
      ok('bk_conv2d_h: control, the two halves of a word swapped, is caught', chMut !== code && exactBad(c16m, c32.out.slice(0, cn)) > 0, `${exactBad(c16m, c32.out.slice(0, cn))} of ${cn} differ`);
    }

    // The SD1.5 path's remaining kernels, each bound from its cx-kernel line
    // (the bridge's contract): args by parameter name, a word array or a
    // scalar ({ f: v } for a Real, sent as its f32 bits).
    const header = (k) => { const m = new RegExp(`// cx-kernel ${k}_main wg=(\\d+)([^\\n]*)`).exec(code); return m[2].trim().split(/\s+/).map(t => { const [p, s] = t.split('='); return { p, s: s[0], i: Number(s.slice(1)) }; }); };
    const fbits = v => new Int32Array(new Float32Array([v]).buffer)[0];
    const launchK = (src, k, args, n, outName, threads, wg) => { const h = header(k), out = h.find(e => e.p === (outName || 'yb')); const bufs = [], sc = []; for (const e of h) { const a = args[e.p]; if (e.s === 'b') bufs.push(e === out ? [e.i, null, FILL, n + 48] : [e.i, a, 0, a.length]); else sc[e.i] = typeof a === 'object' ? fbits(a.f) : a; } return evalIn(`(${runInPage})(${JSON.stringify(src)}, '${k}_main', ${JSON.stringify(bufs)}, ${out.i}, ${h.filter(e => e.s === 'b').length}, ${JSON.stringify(sc)}, ${threads || n + 48}, ${wg || 64})`); };
    const Xw = X.w, Yw = Y.w, Xs = X.f.map(v => v / 16), Xsw = f32w(Float32Array.from(Xs)), Zf = Float32Array.from(X.f).reverse(), Zw = f32w(Zf);
    const HS = 2, SQ = 6, SK = 7, DD = 8;
    const more = [
      { k: 'bk_add_channel', args: { xb: Xw, cb: Yw.slice(0, 5), hw: 64, n: 320 }, n: 320, ref: i => X.f[i] + Y.f[Math.floor(i / 64)], mut: s => s.replace('cb_buf[(gid / hw)]', 'cb_buf[gid]'), what: 'the channel read per element' },
      { k: 'bk_scale', args: { xb: Xw, s: { f: 0.18215 }, n: 2000 }, n: 2000, ref: i => X.f[i] * Math.fround(0.18215), mut: s => s.replace('bk_scale_xb_buf[gid]) * s)', 'bk_scale_xb_buf[gid]) + s)'), what: 'plus in place of times' },
      { k: 'bk_lincomb3', args: { xb: Xw, yb: Yw, zb: Zw, a: { f: 0.5 }, b: { f: -1.25 }, c: { f: 2 }, n: 2000 }, n: 2000, out: 'ob', ref: i => 0.5 * X.f[i] - 1.25 * Y.f[i] + 2 * Zf[i], mut: s => s.replace('(c * bitcast<f32>(bk_lincomb3_zb_buf[gid]))', '(c * bitcast<f32>(bk_lincomb3_yb_buf[gid]))'), what: 'y in place of z' },
      { k: 'bk_geglu', args: { xb: Xsw.slice(700), inner: 64, n: 320 }, n: 320, ref: i => { const r = Math.floor(i / 64), j = i % 64, a = Xs[700 + r * 128 + j], g = Xs[700 + r * 128 + 64 + j]; return a * 0.5 * g * (1 + erf(g / Math.SQRT2)); }, mut: s => s.replace('return (a * bk_gelu_of(g));', 'return (g * bk_gelu_of(a));'), what: 'the gate and the value swapped' },
      { k: 'bk_quick_gelu', args: { xb: Xw, n: 2000 }, n: 2000, ref: i => X.f[i] / (1 + Math.exp(-1.702 * X.f[i])), mut: s => s.replace('0x1.b3b644p+0f', '0x1.000000p+0f'), what: '1 in place of 1.702' },
      { k: 'bk_transpose', args: { xb: Xw, rows: 16, cols: 20, n: 320 }, n: 320, ref: i => X.f[(i % 16) * 20 + Math.floor(i / 16)], mut: s => s.replace('* cols) + (gid / rows))', '* rows) + (gid / rows))'), what: 'rows in place of cols' },
      { k: 'bk_softmax_causal', args: { sb: Xsw, cols: SK, sq: SQ, n: HS * SQ * SK }, n: HS * SQ * SK, ref: i => { const r = Math.floor(i / SK), j = i % SK, seen = Math.min(r % SQ + 1, SK); if (j >= seen) return 0; let m = -Infinity; for (let t = 0; t < seen; t++) m = Math.max(m, Xs[r * SK + t]); let z = 0; for (let t = 0; t < seen; t++) z += Math.exp(Xs[r * SK + t] - m); return Math.exp(Xs[r * SK + j] - m) / z; }, mut: s => s.replace('if ((j >= seen))', 'if ((j > seen))'), what: 'one entry past the causal edge' },
      { k: 'bk_attn_scores', args: { qb: Xsw.slice(0, SQ * HS * DD), kb: Xsw.slice(200, 200 + SK * HS * DD), heads: HS, sq: SQ, sk: SK, d: DD, scale: { f: 1 / Math.sqrt(DD) }, n: HS * SQ * SK }, n: HS * SQ * SK, out: 'sb', ref: i => { const h = Math.floor(i / (SQ * SK)), qi = Math.floor((i % (SQ * SK)) / SK), kj = i % SK; let s = 0; for (let t = 0; t < DD; t++) s += Xs[(qi * HS + h) * DD + t] * Xs[200 + (kj * HS + h) * DD + t]; return s * Math.fround(1 / Math.sqrt(DD)); }, threads: HS * Math.ceil(SQ / 64) * Math.ceil(SK / 64) * 256, wg: 256, mut: s => s.replaceAll('((v) * scale)', '(v)'), what: 'the scale dropped' },
      { k: 'bk_attn_values', args: { pb: Xsw.slice(0, HS * SQ * SK), vb: Xsw.slice(300, 300 + SK * HS * DD), heads: HS, sq: SQ, sk: SK, d: DD, n: SQ * HS * DD }, n: SQ * HS * DD, ref: i => { const qi = Math.floor(i / (HS * DD)), h = Math.floor((i % (HS * DD)) / DD), c = i % DD; let s = 0; for (let j = 0; j < SK; j++) s += Xs[(h * SQ + qi) * SK + j] * Xs[300 + h * DD + c + j * HS * DD]; return s; }, mut: s => s.replace('(heads * d), 0, sk, 0.0)', '(heads * d), 0, (sk - 1), 0.0)'), what: 'the last key dropped' },
    ];
    for (const K of more) {
      const grade = (res) => { let bad = 0, worst = 0; for (let i = 0; i < K.n; i++) { const w = K.ref(i), g = asF(res.out[i]); if (!within(g, w)) bad++; worst = Math.max(worst, Math.abs(g - w) / (1 + Math.abs(w))); } return { bad, worst, tail: res.out.slice(K.n).filter(v => v !== FILL).length, errs: res.errs }; };
      const r1 = grade(await launchK(code, K.k, K.args, K.n, K.out, K.threads, K.wg));
      ok(`${K.k}: within tolerance on every element`, r1.errs.length === 0 && r1.bad === 0, `${r1.bad} of ${K.n} outside; worst ${r1.worst.toExponential(2)} of 1e-6 ${r1.errs.join('; ').slice(0, 120)}`);
      ok(`${K.k}: leaves the threads past n untouched`, r1.tail === 0, `${r1.tail} of 48 written`);
      const mutated = K.mut(code), r2 = grade(await launchK(mutated, K.k, K.args, K.n, K.out, K.threads, K.wg));
      ok(`${K.k}: control, ${K.what}, is caught`, mutated !== code && r2.bad > 0, `${r2.bad} of ${K.n} outside`);
    }

    // bk_attn_scores at CLIP's shape class: 2 heads, 70 queries, 77 keys,
    // d 40, so two tiles each way, partial edges, and a partial k step. Values
    // mod 127, so no two rows 16 apart are equal and a block swap shows.
    {
      const H2 = 2, Q2 = 70, K2 = 77, D2 = 40, qf = new Float32Array(Q2 * H2 * D2).map((_, i) => ((i * 37) % 127 - 64) / 256), kf = new Float32Array(K2 * H2 * D2).map((_, i) => ((i * 53) % 127 - 64) / 256), sc2 = Math.fround(1 / Math.sqrt(D2));
      const want = (i) => { const h = Math.floor(i / (Q2 * K2)), qi = Math.floor((i % (Q2 * K2)) / K2), kj = i % K2; let s = 0; for (let t = 0; t < D2; t++) s += qf[(qi * H2 + h) * D2 + t] * kf[(kj * H2 + h) * D2 + t]; return s * sc2; };
      const n2 = H2 * Q2 * K2, th = H2 * Math.ceil(Q2 / 64) * Math.ceil(K2 / 64) * 256;
      const args2 = { qb: f32w(qf), kb: f32w(kf), heads: H2, sq: Q2, sk: K2, d: D2, scale: { f: sc2 }, n: n2 };
      const bad2 = (res) => { let b = 0; for (let i = 0; i < n2; i++) if (!within(asF(res.out[i]), want(i))) b++; return b; };
      const s1 = await launchK(code, 'bk_attn_scores', args2, n2, 'sb', th, 256);
      ok(`bk_attn_scores: ${H2} heads of ${Q2} x ${K2} at d ${D2}, every score within tolerance`, s1.errs.length === 0 && bad2(s1) === 0, `${bad2(s1)} of ${n2} outside ${s1.errs.join('; ').slice(0, 120)}`);
      ok('bk_attn_scores: leaves the scores past heads x sq x sk untouched', s1.out.slice(n2).filter(v => v !== FILL).length === 0, `${s1.out.slice(n2).filter(v => v !== FILL).length} of 48 written`);
      const kMut = code.replaceAll('bk_attn_scores_kb_buf[', 'bk_attn_scores_qb_buf[');
      const s2 = await launchK(kMut, 'bk_attn_scores', args2, n2, 'sb', th, 256);
      ok('bk_attn_scores: control, k read from q\'s buffer, is caught', kMut !== code && bad2(s2) > 0, `${bad2(s2)} of ${n2} outside`);
      const aMut = code.replaceAll('scale, row, (col + 48), a03)', 'scale, row, (col + 48), a02)');
      const s3 = await launchK(aMut, 'bk_attn_scores', args2, n2, 'sb', th, 256);
      ok('bk_attn_scores: control, a thread\'s fourth key stored from its third accumulator, is caught', aMut !== code && bad2(s3) > 0, `${bad2(s3)} of ${n2} outside`);
    }

    // Speed, reported and not graded: 1024^3, buffers made once, 20 launches
    // after a warm-up, timed to onSubmittedWorkDone.
    const timeLinear = `async (code, ep, b, ub, S, wWords) => {
      const a = await navigator.gpu.requestAdapter(); const d = await a.requestDevice();
      const mk = (n) => d.createBuffer({ size: n * 4, usage: GPUBufferUsage.STORAGE });
      const e = [[b.xb, S * S], [b.wb, wWords], [b.bb, S], [b.yb, S * S]].map(([bn, n]) => ({ binding: bn, resource: { buffer: mk(n) } }));
      const u = d.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST }); d.queue.writeBuffer(u, 0, new Int32Array([S, S, S, 0]));
      e.push({ binding: ub, resource: { buffer: u } });
      const p = d.createComputePipeline({ layout: 'auto', compute: { module: d.createShaderModule({ code }), entryPoint: ep } });
      const bg = d.createBindGroup({ layout: p.getBindGroupLayout(0), entries: e });
      const run = () => { const c = d.createCommandEncoder(), q = c.beginComputePass(); q.setPipeline(p); q.setBindGroup(0, bg); q.dispatchWorkgroups((S / 64) * (S / 64)); q.end(); d.queue.submit([c.finish()]); return d.queue.onSubmittedWorkDone(); };
      await run(); const t0 = performance.now(); for (let i = 0; i < 20; i++) await run(); const s = (performance.now() - t0) / 1000; d.destroy();
      return 20 * 2 * S * S * S / s / 1e9;
    }`;
    const gf = await evalIn(`(${timeLinear})(${JSON.stringify(code)}, 'bk_linear_main', ${JSON.stringify(lnb.m)}, ${lnb.ub}, 1024, 1024 * 1024)`);
    const gh = await evalIn(`(${timeLinear})(${JSON.stringify(code)}, 'bk_linear_h_main', ${JSON.stringify(lhb.m)}, ${lhb.ub}, 1024, 512 * 1024)`);
    console.log(`  info  bk_linear: ${gf.toFixed(0)} GFLOPS f32 weights, ${gh.toFixed(0)} GFLOPS f16 weights, 1024 x 1024 x 1024, same run (not graded)`);

  }
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
