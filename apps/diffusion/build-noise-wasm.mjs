import {execFileSync} from 'node:child_process';
import {mkdirSync,readFileSync} from 'node:fs';
import {dirname,join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const memory=()=>{const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));if(m.FreePhysicalMemory<=1572864)throw new Error('Insufficient RAM for noise module build');console.log('Noise build memory '+JSON.stringify(m));};
const pwsh=(file,args)=>execFileSync('pwsh',['-NoProfile','-File',join(repo,file),...args],{cwd:repo,stdio:'inherit',windowsHide:true});

export function buildNoiseWasm(path){
  const out=resolve(path);if(!out.endsWith('.wasm'))throw new Error('Noise output must end in .wasm');
  mkdirSync(dirname(out),{recursive:true});
  const stem=out.slice(0,-5);
  memory();pwsh('codex/plugs/wasm/build.ps1',[]);
  pwsh('build/bundle-app.ps1',['-Src',join(repo,'apps/diffusion/BrowserNoiseWasm.codex'),'-Out',stem+'.codex']);
  memory();pwsh('codex/plugs/wasm/run.ps1',['-Src',stem+'.codex','-Out',stem+'.wat','-Kernel',join(repo,'seed/Codex.cdx')]);
  execFileSync('wat2wasm',['--enable-tail-call',stem+'.wat','-o',out],{cwd:repo,stdio:'inherit',windowsHide:true});
  return readFileSync(out);
}

if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  if(!process.argv[2])throw new Error('Usage: node apps/diffusion/build-noise-wasm.mjs output.wasm');
  const bytes=buildNoiseWasm(process.argv[2]);console.log('Noise module bytes '+bytes.length);
}
