// Validate a WGSL file against Chrome's real WGSL compiler, headless.
// Launches installed Chrome with WebGPU (SwiftShader fallback), connects over
// CDP using only node built-ins (fetch + WebSocket, node >= 22), calls
// device.createShaderModule + getCompilationInfo, and reports diagnostics.
//
// Usage: node validate.mjs <file.wgsl> [chrome.exe]
// Exit 0 = zero WGSL errors, 1 = errors/failure.

import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const wgslPath = process.argv[2];
if (!wgslPath) { console.error('usage: node validate.mjs <file.wgsl> [chrome.exe]'); process.exit(2); }
const chromePath = process.argv[3]
  || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const code = readFileSync(wgslPath, 'utf8');
const PORT = 9223;

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

async function cdpTarget() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${PORT}/json`);
      const targets = await r.json();
      const page = targets.find(t => t.type === 'page');
      if (page && page.webSocketDebuggerUrl) return page.webSocketDebuggerUrl;
    } catch {}
    await sleep(250);
  }
  throw new Error('CDP endpoint never came up');
}

function evalInPage(wsUrl, expression) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl);
    ws.addEventListener('open', () => {
      ws.send(JSON.stringify({
        id: 1, method: 'Runtime.evaluate',
        params: { expression, awaitPromise: true, returnByValue: true },
      }));
    });
    ws.addEventListener('message', (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.id === 1) {
        ws.close();
        if (msg.result && msg.result.result) resolve(msg.result.result.value);
        else reject(new Error(JSON.stringify(msg)));
      }
    });
    ws.addEventListener('error', (e) => reject(e));
  });
}

const expression = `(async () => {
  try {
    if (!navigator.gpu) return { ok:false, error:'no navigator.gpu (WebGPU unavailable)', diag:{ href: location.href, secure: isSecureContext, ua: navigator.userAgent, gpuType: typeof navigator.gpu } };
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) return { ok:false, error:'no GPU adapter' };
    const device = await adapter.requestDevice();
    const code = ${JSON.stringify(code)};
    const mod = device.createShaderModule({ code });
    const info = await mod.getCompilationInfo();
    const messages = info.messages.map(m => ({ type:m.type, message:m.message, line:m.lineNum, col:m.linePos }));
    const errors = messages.filter(m => m.type === 'error');
    return { ok: errors.length === 0, adapter: adapter.info ? (adapter.info.vendor+' '+adapter.info.architecture) : 'unknown', messages };
  } catch (e) { return { ok:false, error: String(e) }; }
})()`;

const userDir = mkdtempSync(join(tmpdir(), 'gpushow-chrome-'));
// WebGPU is only exposed in a secure context. about:blank is not one; a
// file:// URL is. Write a blank page and load it so navigator.gpu appears.
const blankHtml = join(userDir, 'blank.html');
writeFileSync(blankHtml, '<!doctype html><meta charset=utf-8><title>gpushow validate</title>');
const startUrl = 'file:///' + blankHtml.replace(/\\/g, '/');
const headful = !!process.env.HEADFUL;
const flags = [
  `--remote-debugging-port=${PORT}`,
  '--remote-allow-origins=*',
  '--enable-unsafe-webgpu',
  '--enable-unsafe-swiftshader',
  '--enable-features=Vulkan,WebGPU',
  '--no-first-run', '--no-default-browser-check',
  `--user-data-dir=${userDir}`,
  startUrl,
];
if (!headful) flags.unshift('--headless=new');
const chrome = spawn(chromePath, flags, { stdio: 'ignore' });

let exitCode = 1;
try {
  const wsUrl = await cdpTarget();
  const result = await evalInPage(wsUrl, expression);
  console.log('adapter:', result.adapter ?? '(n/a)');
  if (result.error) console.log('error:', result.error);
  if (result.diag) console.log('diag:', JSON.stringify(result.diag));
  for (const m of (result.messages ?? [])) {
    console.log(`  [${m.type}] ${m.line}:${m.col} ${m.message}`);
  }
  if (result.ok) { console.log('WGSL VALID: 0 errors'); exitCode = 0; }
  else console.log('WGSL INVALID');
} catch (e) {
  console.error('validate failed:', e.message || e);
} finally {
  chrome.kill();
}
process.exit(exitCode);
