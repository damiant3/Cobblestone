import {readFileSync,mkdtempSync,rmSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import vm from 'node:vm';
const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../../../..');
const pageAt=process.argv.indexOf('--page');
const supplied=pageAt<0?null:process.argv[pageAt+1];
if(pageAt>=0&&!supplied)throw Error('Usage: json-controls.mjs [--page shipped.html]');
const work=supplied?null:mkdtempSync(join(tmpdir(),'json-controls-'));
const load=path=>{
  const bytes=readFileSync(path),html=bytes.toString('utf8').replaceAll('\r\n','\n');
  console.log('Page SHA256 '+createHash('sha256').update(bytes).digest('hex'));
  const blocks=[...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)],source=blocks.at(-1)?.[1];
  const marker=source?.lastIndexOf('// Entry\ntry { opening(); } catch(e) { console.error(e); }');
  if(marker===undefined||marker<0)throw Error('Generated entry boundary missing');
  const context=vm.createContext({console,window:{},TextEncoder,TextDecoder,setTimeout,clearTimeout,setInterval,clearInterval,performance});
  vm.runInContext(source.slice(0,marker),context);return context;
};
const build=name=>{
  const source=join(work,name+'.codex'),page=join(work,name+'.html');
  const run=(script,args)=>execFileSync('pwsh',['-NoProfile','-File',join(repo,script),...args],{stdio:'inherit',windowsHide:true});
  const memory=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));
  if(memory.FreePhysicalMemory<=1572864)throw Error('RAM admission refused');
  run('build/bundle-app.ps1',['-Src',join(repo,'codex/plugs/html/arms',name+'.codex'),'-Out',source]);
  run('codex/plugs/html/run.ps1',['-Src',source,'-Out',page,'-Compiler',join(repo,'seed/Codex.cdx')]);
  return load(page);
};
try{
  const context=supplied?load(resolve(supplied)):build('JsonControlArm');
  const values=['','plain text','x'.repeat(65536)+'\t',...Array.from({length:32},(_,i)=>String.fromCodePoint(i)),'quote"slash\\','é Ж 日本','😀','\ud800','\udfff','line\u2028paragraph\u2029','prefix'+String.fromCodePoint(...Array.from({length:32},(_,i)=>i))+'suffix'];
  let failed=0;
  for(const text of values){
    let encoded;try{encoded=context.json_quote(text);if(JSON.parse(encoded)!==text)throw Error('Text changed');if(/[\x00-\x1f]/.test(encoded))throw Error('Raw control in JSON');
      if(!supplied){if(JSON.parse(context.json_quote__tb(text))!==text)throw Error('Trampoline quote changed value');if(JSON.parse(context.json_control_text(text))!==text)throw Error('JsonStr emitter changed value');const parsed=JSON.parse(context.json_control_object(text,text));if(Object.keys(parsed).length!==1||parsed[text]!==text)throw Error('Object key/value changed');}
      console.log('PASS '+(text.length>160?'length '+text.length:JSON.stringify(text)));
    }catch(error){failed++;console.log('FAIL '+JSON.stringify({input:text,encoded,error:String(error)}));}
  }
  for(const point of [9,13,31])if(context.char_code_at(String.fromCodePoint(point),0n)!==-1n)throw Error('CCE unmapped control semantics changed');
  if(!supplied){const custom=build('CustomJsonQuoteArm');if(custom.json_quote('\t')!=='custom:\t')throw Error('Unrelated json-quote definition was replaced');console.log('PASS unrelated same-name function and CCE controls unchanged');}
  if(failed)throw Error(failed+' JSON string cases failed');
  console.log('PASS HTML JSON Unicode-control round trips');
}finally{
  if(work){if(dirname(work)!==resolve(tmpdir())||!work.startsWith(join(tmpdir(),'json-controls-')))throw Error('Temporary path escaped root');rmSync(work,{recursive:true,force:true});}
}