// Builds apps/spark/SparkStudioPage.codex into one self-contained page: the
// html plug's output with page-data holding BrowserKernels' WGSL and, unless
// --no-tokenizer, the CLIP tokenizer's vocab.json and merges.txt (as
// apps/diffusion/build-browser-page.mjs does for the engine's own page).
// Usage: node apps/spark/build-studio-page.mjs [--tokenizer <dir> | --no-tokenizer] [--out <html>]
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildNoiseWasm } from '../diffusion/build-noise-wasm.mjs';
import { anthropicProviderSource } from '../prism/llm-provider.mjs';
import { installStudioLlm } from './studio-llm-panel.mjs';
import { createWizardSupport, installStudioWizard } from './studio-wizard.mjs';
import { qwenPageSource } from './qwen-page-bundle.mjs';
import { buildQwenTokenizer } from './build-qwen-tokenizer.mjs';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const tokDir = arg('--tokenizer', 'D:\\AI\\DiffusionForge\\webui\\backend\\huggingface\\stabilityai\\stable-diffusion-xl-base-1.0\\tokenizer');
const out = resolve(arg('--out', join(repo, 'build-output', 'sparkstudio.html')));
const pwsh = (script, args) => {
  const free = Number(execFileSync('pwsh', ['-NoProfile', '-Command', '(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory'], { encoding: 'utf8', windowsHide: true }).trim());
  if (!(free > 1572864)) throw new Error('Studio build refused: insufficient free RAM');
  return execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args, ...(script === 'codex/plugs/html/run.ps1' ? ['-Compiler', join(repo, 'seed/Codex.cdx')] : [])], { stdio: 'inherit', windowsHide: true });
};

const work = mkdtempSync(join(tmpdir(), 'spark-studio-page-'));
try {
  pwsh('codex/plugs/html/build.ps1', []);
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'apps', 'spark', 'SparkStudioPage.codex'), '-Out', join(work, 'page.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'page.codex'), '-Out', join(work, 'page.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const data = { bk: readFileSync(join(work, 'bk.wgsl'), 'utf8') };
  data['sdxl-noise'] = buildNoiseWasm(join(work, 'noise.wasm')).toString('base64');
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'apps', 'diffusion', 'BrowserLoraKernels.codex'), '-Out', join(work, 'lora.wgsl')]);
  data.lora = readFileSync(join(work, 'lora.wgsl'), 'utf8');
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'apps', 'diffusion', 'BrowserClipKernels.codex'), '-Out', join(work, 'clip-xl.wgsl')]);
  data['clip-xl'] = readFileSync(join(work, 'clip-xl.wgsl'), 'utf8');
  for (const [key, chapter] of [['music-text','MusicT5Kernels'],['music-model','MusicLMKernels'],['music-audio','EncodecKernels']]) {
    const shader = join(work, key + '.wgsl');
    pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'apps', 'spark', chapter + '.codex'), '-Out', shader]);
    data[key] = readFileSync(shader, 'utf8');
  }
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'apps', 'spark', 'QwenKernels.codex'), '-Out', join(work, 'qwen.wgsl')]);
  data.qwen = readFileSync(join(work, 'qwen.wgsl'), 'utf8');
  data['qwen-bpe'] = buildQwenTokenizer(join(work, 'qwen-bpe.wasm')).toString('base64');
  if (!process.argv.includes('--no-tokenizer')) for (const [key, file] of [['vocab', 'vocab.json'], ['merges', 'merges.txt']]) {
    const bytes = readFileSync(join(tokDir, file)), text = bytes.toString('utf8');
    if (!Buffer.from(text, 'utf8').equals(bytes)) throw new Error(file + ' is not plain UTF-8 and cannot ride the page as text');
    data[key + '-text'] = text;
  }
  const provider = anthropicProviderSource(repo);
  const assistant = `window.__sparkWizardSupport=(${createWizardSupport.toString()})();
  window.__sparkPromptsCheck=(text,count)=>{const q=document.getElementById('wizard-prompts-query'),r=document.getElementById('wizard-prompts-reply');r.value='';q.value=JSON.stringify({text,count});q.dispatchEvent(new Event('input',{bubbles:true}));return JSON.parse(r.value)};
  window.__sparkLlm=(${installStudioLlm.toString()})(document,{
    anthropic:(${provider})({getItem:k=>window.localStorage.getItem(k),setItem:(k,v)=>window.localStorage.setItem(k,v),removeItem:k=>window.localStorage.removeItem(k)},'spark.llm.anthropic.key'),
    local:__QWEN.createLocalQwenProvider({kernels:window.__DATA.qwen,tokenizerWasm:Uint8Array.from(atob(window.__DATA['qwen-bpe']),c=>c.charCodeAt(0)),pickFile:()=>new Promise(resolve=>{const input=document.createElement('input');input.type='file';input.addEventListener('change',()=>resolve(input.files[0]||null));input.addEventListener('cancel',()=>resolve(null));input.click()})})
  },{
    setBusy:value=>{state_set('llm-busy',value?1:0);window.__sparkWizard?.refresh()},
    busy:()=>['audio-busy','image-busy','q-busy','project-picking'].some(key=>Number(state_get(key))===1),
    project:()=>state_get_text('llm-listing'),
    listing:()=>JSON.parse(state_get_text('llm-listing')||'[]'),file:name=>_gfiles[name],
    context:request=>{const q=document.getElementById('llm-context-query'),r=document.getElementById('llm-context-reply');r.value='';q.value=JSON.stringify(request);q.dispatchEvent(new Event('input',{bubbles:true}));return JSON.parse(r.value)},
    prompts:window.__sparkPromptsCheck,glossary:window.__sparkWizardSupport.glossary
  });
  window.__sparkWizard=(${installStudioWizard.toString()})(document,window.__sparkLlm,window.__sparkWizardSupport,{
    busy:()=>['audio-busy','image-busy','q-busy','project-picking'].some(key=>Number(state_get(key))===1),
    setBusy:value=>state_set('project-picking',value?1:0),prompts:window.__sparkPromptsCheck,
    chooseFolder:()=>{if(typeof window.showDirectoryPicker!=='function')throw new Error('This browser cannot choose a writable folder.');return window.showDirectoryPicker({mode:'readwrite'})},
    adopt:async(directory,paths,count)=>{
      const rows=[];
      for(const path of paths){const file=await(await directory.getFileHandle(path)).getFile();window.__sparkFileSerial=(window.__sparkFileSerial||0)+1;const name='spark-wizard-'+window.__sparkFileSerial;_gfiles[name]=file;rows.push({name,path:directory.name+'/'+path,size:file.size})}
      rows.sort((a,b)=>a.path<b.path?-1:1);_gdir=directory;state_set('done',0);state_set('audio-done',0);state_set('project-picking',1);ss_picked(JSON.stringify(rows));
      await new Promise((resolve,reject)=>{let tries=0;const poll=()=>{if(Number(state_get('done'))===1&&Number(state_get('project-picking'))===0){if(Number(state_get('prompts'))!==count)reject(new Error('Opened project prompt count differs from validated files'));else resolve()}else if(++tries>200)reject(new Error('Timed out opening the created project'));else setTimeout(poll,50)};poll()});
    }
  });`;
  const unpack = "for(const k of ['vocab','merges']){const t=window.__DATA[k+'-text'];if(typeof t==='string'){window.__DATA[k]=JSON.stringify(Array.from(new TextEncoder().encode(t)));delete window.__DATA[k+'-text']}}";
  const html = readFileSync(join(work, 'page.html'), 'utf8').replace('<script>', () => `<script>window.__DATA=${JSON.stringify(data).replace(/<\//g, '<\\/')};${unpack}</script><script>`)
    .replace('</body>', `<script>window.__QWEN=${qwenPageSource().replace(/<\/script/gi, '<\\/script')};</script><script>${assistant.replace(/<\/script/gi, '<\\/script')}</script></body>`);
  writeFileSync(out, html);
  console.log(`spark studio page: ${out} (${html.length} chars)`);
} finally {
  if (dirname(resolve(work)) !== resolve(tmpdir()) || !work.startsWith(join(tmpdir(), 'spark-studio-page-'))) throw new Error('Temporary page path escaped its root');
  rmSync(work, { recursive: true, force: true });
}
