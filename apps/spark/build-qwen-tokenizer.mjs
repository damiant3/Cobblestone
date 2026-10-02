import {execFileSync} from 'node:child_process';
import {mkdirSync,readFileSync} from 'node:fs';
import {dirname,join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const admit=()=>{const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));if(m.FreePhysicalMemory<=1572864)throw new Error('Insufficient RAM for tokenizer build');console.log('Tokenizer build memory '+JSON.stringify(m));};
const run=(file,args)=>execFileSync('pwsh',['-NoProfile','-File',join(repo,file),...args],{cwd:repo,stdio:'inherit',windowsHide:true});
export function buildQwenTokenizer(path){
  const out=resolve(path);if(!out.endsWith('.wasm'))throw new Error('Tokenizer output must end in .wasm');
  mkdirSync(dirname(out),{recursive:true});const stem=out.slice(0,-5);
  admit();run('codex/plugs/wasm/build.ps1',[]);
  run('build/bundle-app.ps1',['-Src',join(repo,'apps/spark/QwenBpeWasm.codex'),'-Out',stem+'.codex']);
  admit();run('codex/plugs/wasm/run.ps1',['-Src',stem+'.codex','-Out',stem+'.wat','-Kernel',join(repo,'seed/Codex.cdx')]);
  execFileSync('wat2wasm',['--enable-tail-call',stem+'.wat','-o',out],{cwd:repo,stdio:'inherit',windowsHide:true});
  return readFileSync(out);
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  if(!process.argv[2])throw new Error('Usage: build-qwen-tokenizer.mjs output.wasm');
  console.log('Tokenizer module bytes '+buildQwenTokenizer(process.argv[2]).length);
}
