import {readFileSync} from 'node:fs';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));

export function qwenPageSource(){
  const parts=['qwen-gguf.mjs','qwen-tokenizer.mjs','qwen-forward.mjs'].map(name=>{
    const source=readFileSync(join(here,name),'utf8');
    if(/^import /m.test(source.replace(/^import \{[^}]*\} from '\.\/qwen-[a-z-]+\.mjs';\r?$/gm,'')))throw new Error(name+' imports a module the page bundle cannot carry');
    return source.replace(/^import \{[^}]*\} from '\.\/qwen-[a-z-]+\.mjs';\r?$/gm,'').replace(/^export /gm,'');
  });
  return '(()=>{\n'+parts.join('\n')+'\nreturn {createLocalQwenProvider,qwenRuntime};\n})()';
}
