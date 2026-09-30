// Builds apps/diffusion/BrowserImagePage.codex into one self-contained page:
// the html plug's output with page-data holding BrowserKernels' WGSL and the
// CLIP tokenizer's vocab.json and merges.txt. Open the page in a browser with
// WebGPU (Edge or Chrome) and pick an SD1.5 .safetensors.
// Usage: node apps/diffusion/build-browser-page.mjs [--tokenizer <dir>] [--out <html>]
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const tokDir = arg('--tokenizer', 'D:\\AI\\DiffusionForge\\webui\\backend\\huggingface\\stabilityai\\stable-diffusion-xl-base-1.0\\tokenizer');
const out = resolve(arg('--out', join(repo, 'build-output', 'browser-imagegen.html')));
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

const work = mkdtempSync(join(tmpdir(), 'browser-page-'));
try {
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'apps', 'diffusion', 'BrowserImagePage.codex'), '-Out', join(work, 'page.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'page.codex'), '-Out', join(work, 'page.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const data = { bk: readFileSync(join(work, 'bk.wgsl'), 'utf8'), vocab: JSON.stringify(Array.from(readFileSync(join(tokDir, 'vocab.json')))), merges: JSON.stringify(Array.from(readFileSync(join(tokDir, 'merges.txt')))) };
  const html = readFileSync(join(work, 'page.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify(data)};</script><script>`);
  writeFileSync(out, html);
  console.log(`browser page: ${out} (${html.length} chars)`);
} finally {
  rmSync(work, { recursive: true, force: true });
}
