// Stage 2 of docs/Designs/Active/Apps/InBrowserDiffusion.md: the browser GPU
// bridge (apps/webapp/WebGpu.codex) driven by a compiled Codex page,
// codex/plugs/html/arms/GpuBridgeArm.codex, in headless Edge. A buffer round
// trip must come back word for word; ranges of a picked file (answered through
// CDP's file-chooser interception) are uploaded and bk_linear from
// BrowserKernels is launched through the bridge, once over f32 weights and once
// over the same weights uploaded as f16; both must equal the exact product.
// bk_lincomb3 takes f32 scalars from gpu-arg-f32 and must be exact; a control
// passes one scalar as gpu-arg-u32. A control swaps two bindings in the
// module's cx-kernel line.
// Usage: node codex/plugs/html/arms/gpu-bridge.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

async function cdpOpen(port, bin) {
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map();
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Page.fileChooserOpened') send('DOM.setFileInputFiles', { files: [bin], backendNodeId: m.params.backendNodeId });
  });
  await send('Page.enable');
  await send('DOM.enable');
  await send('Page.setInterceptFileChooserDialog', { enabled: true });
  const evalIn = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  };
  return { send, evalIn };
}

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'gpu-bridge-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'GpuBridgeArm.codex'), '-Out', join(work, 'gba.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'gba.codex'), '-Out', join(work, 'gba.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const html = readFileSync(join(work, 'gba.html'), 'utf8'), wgsl = readFileSync(join(work, 'bk.wgsl'), 'utf8');

  // x (37 x 70), w (45 x 70), b (45): multiples of 1/64 in [-1, 1), exact in
  // f32 and in f16, so every product and sum is exact.
  const M = 37, N = 45, K = 70, q = (i, s) => ((i * s) % 128 - 64) / 64;
  const x = new Float32Array(M * K).map((_, i) => q(i, 37)), w = new Float32Array(N * K).map((_, i) => q(i, 53)), b = new Float32Array(N).map((_, i) => q(i, 11));
  const f16 = (v) => { const u = new Uint32Array(new Float32Array([v]).buffer)[0], s = (u >>> 16) & 0x8000, e = ((u >>> 23) & 0xff) - 112, m = u & 0x7fffff; if (e <= 0) return s; let h = s | (e << 10) | (m >>> 13); const r = m & 0x1fff; if (r > 0x1000 || (r === 0x1000 && (h & 1))) h++; return h; };
  const wh = new Uint16Array(N * K).map((_, i) => f16(w[i]));
  const bin = join(work, 'in.bin');
  writeFileSync(bin, Buffer.concat([Buffer.from(x.buffer), Buffer.from(w.buffer), Buffer.from(b.buffer), Buffer.from(wh.buffer)]));
  const yWant = []; for (let r = 0; r < M; r++) for (let c = 0; c < N; c++) { let s = b[c]; for (let k = 0; k < K; k++) s += x[r * K + k] * w[c * K + k]; yWant.push(s); }
  const words = []; for (let i = 0; i < 256; i++) words.push(((i * 40503) % 65536 - 32768) * 65535);

  const swapped = wgsl.replace(/(\/\/ cx-kernel bk_linear_main [^\n]*)/, (line) => { const xb = / xb=(b\d+)/.exec(line)[1], wb = / wb=(b\d+)/.exec(line)[1]; return line.replace(` xb=${xb}`, ` xb=${wb}`).replace(` wb=${wb}`, ` wb=${xb}`); });
  const page = (code) => html.replace('<script>', `<script>window.__DATA=${JSON.stringify({ bk: code })};</script><script>`);
  const port = await freePort();
  server = http.createServer((qq, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(qq.url.startsWith('/control') ? page(swapped) : page(wgsl)); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', 'about:blank'], { stdio: 'ignore' });
  const { send, evalIn } = await cdpOpen(dbg, bin);

  const runPage = async (path) => {
    await send('Page.navigate', { url: `http://127.0.0.1:${port}${path}` });
    for (let i = 0; i < 120; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.armed) === 1')) break; } catch {} }
    for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: 5, y: 5, button: 'left', clickCount: 1 });
    for (let i = 0; i < 240; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && _st.done === 1n || (typeof _st !== "undefined" && _st.done === 1)')) break; } catch {} }
    return evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  };
  const asF = v => new Float32Array(new Int32Array([v]).buffer)[0];
  const yBad = (text) => { let a; try { a = JSON.parse(text); } catch { return M * N; } if (!Array.isArray(a)) return M * N; return yWant.reduce((bad, v, i) => bad + (asF(a[i]) === v ? 0 : 1), 0); };

  const st = await runPage('/');
  let open = {}; try { open = JSON.parse(st.open || '{}'); } catch {}
  ok('the page opens a WebGPU device through gpu-open-then', open.ok === true, st.open);
  let rt = null; try { rt = JSON.parse(st.rt || 'null'); } catch {}
  ok('a buffer round trip returns 256 words as written', Array.isArray(rt) && rt.length === 256 && rt.every((v, i) => v === words[i]), Array.isArray(rt) ? `${rt.filter((v, i) => v !== words[i]).length} of 256 differ` : String(st.rt).slice(0, 160));
  for (const k of ['up-x', 'up-w', 'up-b', 'up-wh']) { let u = {}; try { u = JSON.parse(st[k] || '{}'); } catch {} ok(`upload ${k.slice(3)} from the picked file answers ok`, u.ok === true, st[k]); }
  ok('both launches are queued and no error is reported', st.launch === 0 && st.launch2 === 0 && st.err === '', `launch ${st.launch}, ${st.launch2}; error '${st.err}'`);
  ok('bk_linear through the bridge equals x w^T + b exactly', yBad(st.y) === 0, `${yBad(st.y)} of ${M * N} differ`);
  ok('the same launch over f16-uploaded weights equals it exactly', yBad(st.y2) === 0, `${yBad(st.y2)} of ${M * N} differ`);
  const lWant = Array.from(x, (v, i) => 0.5 * v - 2 * w[i] + 0.25 * w[i]);
  const lBad = (text) => { let a; try { a = JSON.parse(text); } catch { return M * K; } if (!Array.isArray(a)) return M * K; return lWant.reduce((bad, v, i) => bad + (asF(a[i]) === v ? 0 : 1), 0); };
  ok('bk_lincomb3 with f32 scalars from gpu-arg-f32 equals 0.5 x - 2 w + 0.25 w exactly', st.launch3 === 0 && lBad(st.l) === 0, `launch ${st.launch3}; ${lBad(st.l)} of ${M * K} differ`);
  ok('control, a passed as gpu-arg-u32 1, is caught', st.launch4 === 0 && lBad(st.l2) > 0, `launch ${st.launch4}; ${lBad(st.l2)} of ${M * K} differ`);

  // The upload's f32-to-f16 conversion (_gf2h, over f32 bits): every f16
  // value round-trips exactly, and between neighbours the f32 midpoint
  // rounds to the even one and one f32 ulp either side to the nearer, both
  // signs, 65520 to infinity. Control: the same function with its rounding
  // removed must fail.
  const f2hTest = `(conv) => { const F = new Float32Array(1), U = new Uint32Array(F.buffer), bits = (x) => { F[0] = x; return U[0]; }; let bad = 0, n = 0;
    for (let h = 0; h < 0x7c00; h++) for (const s of [0, 0x8000]) { n++; if (conv(bits(_gh2f(h | s))) !== (h | s)) bad++; }
    for (let h = 0; h < 0x7bff; h++) for (const s of [0, 0x8000]) { const m = bits((_gh2f(h | s) + _gh2f((h + 1) | s)) / 2), even = (h & 1) ? h + 1 : h; n += 3; if (conv(m) !== (even | s)) bad++; if (conv(m - 1) !== (h | s)) bad++; if (conv(m + 1) !== ((h + 1) | s)) bad++; }
    n += 2; if (conv(bits(65520)) !== 0x7c00) bad++; if (conv(bits(65520) - 1) !== 0x7bff) bad++;
    return JSON.stringify({ bad, n }); }`;
  const f2h = JSON.parse(await evalIn(`(${f2hTest})(_gf2h)`));
  ok('the upload converts f32 to f16 rounding to nearest even, subnormals and overflow included', f2h.bad === 0, `${f2h.bad} of ${f2h.n} wrong`);
  const f2hCut = JSON.parse(await evalIn(`(${f2hTest})(eval('(' + _gf2h.toString().replaceAll('h++', 'h') + ')'))`));
  ok('control, the conversion with its rounding removed, is caught', f2hCut.bad > 0, `${f2hCut.bad} of ${f2hCut.n} wrong`);

  const ct = await runPage('/control');
  ok('control, xb and wb swapped in the cx-kernel line, is caught', swapped !== wgsl && yBad(ct.y) > 0, `${yBad(ct.y)} of ${M * N} differ`);
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
