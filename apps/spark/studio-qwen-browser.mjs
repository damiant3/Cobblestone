// Grades the local Qwen3 provider through the built Studio page: the Writing
// assistant's provider select offers it, the first Expand asks for the GGUF
// through the provider's own file input, the draft streams into the editor and
// applies only on the explicit button, and a second Expand reuses the loaded
// model (no second file request) and drafts the same greedy text. Headless
// Edge shows no file dialog, so the input's click is recorded instead of
// opening one and the file is set on that same input over CDP, which fires its
// change event.
// Usage: node apps/spark/studio-qwen-browser.mjs studio.html [model.gguf]
import {readFileSync,existsSync} from 'node:fs';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {withGpuPage} from './qwen-gpu.mjs';

const here=dirname(fileURLToPath(import.meta.url));
const [htmlArg,modelArg]=process.argv.slice(2);
if(!htmlArg)throw new Error('Usage: node apps/spark/studio-qwen-browser.mjs studio.html [model.gguf]');
const reference=JSON.parse(readFileSync(join(here,'tests/qwen3-generation-reference.json'),'utf8'));
const model=modelArg??'D:/AI/OllamaModels/blobs/sha256-'+reference.modelSha256;
if(!existsSync(model))throw new Error('Qwen3 GGUF reference model is missing: '+model);
const html=readFileSync(htmlArg);
const route=(q,r)=>{if(q.url==='/'){r.writeHead(200,{'Content-Type':'text/html'});r.end(html);return true;}return false;};
const pause=ms=>new Promise(r=>setTimeout(r,ms));
await withGpuPage('',new Map(),async(run,evaluate,send)=>{
  const until=async(expression,ms)=>{for(let t=0;t<ms;t+=250){if(await evaluate(expression))return;await pause(250);}throw new Error('Timed out: '+expression);};
  await until('!!window.__sparkLlm',20000);
  const option=await evaluate(`(()=>{const o=document.querySelector('#llm-provider option[value="local"]');return o?{disabled:o.disabled,label:o.textContent,reason:document.getElementById('llm-local-reason').textContent}:null})()`);
  console.log(JSON.stringify({option}));
  if(!option||option.disabled)throw new Error('The local provider is not selectable: '+JSON.stringify(option));
  await evaluate(`(()=>{
    window.__requests=0;const click=HTMLInputElement.prototype.click;
    HTMLInputElement.prototype.click=function(){if(this.type!=='file')return click.call(this);window.__requests++;window.__fileInput=this;};
    window.__engineCalls=0;
    for(const name of ['ss_generate','ss_generate__tb','ss_image_run','spark_image_run','spark_image_run__tb'])if(typeof window[name]==='function')window[name]=()=>{window.__engineCalls++;throw new Error('Writing assistant invoked image generation')};
    window.__outputs=new Set();const out=document.getElementById('llm-output');setInterval(()=>{if(out.value)__outputs.add(out.value)},20);
    const sel=document.getElementById('llm-provider');sel.value='local';sel.dispatchEvent(new Event('change',{bubbles:true}));
    document.getElementById('llm-action').value='expand';document.getElementById('prompt').value='A lighthouse on an icy coast';return 0})()`);
  const state=()=>evaluate(`({status:document.getElementById('llm-status').textContent,output:document.getElementById('llm-output').value,apply:document.getElementById('llm-apply').disabled,prompt:document.getElementById('prompt').value,streamed:__outputs.size,requests:__requests,model:__sparkLlm.provider.model,engineCalls:__engineCalls})`);
  const draft=async supply=>{
    await evaluate("window.__outputs.clear();document.getElementById('llm-run').click();0");
    if(supply){
      await until('!!window.__fileInput',30000);
      const input=await send('Runtime.evaluate',{expression:'window.__fileInput'});
      await send('DOM.enable');await send('DOM.setFileInputFiles',{objectId:input.result.objectId,files:[model]});
    }
    await until('!__sparkLlm.busy()',600000);
    return state();
  };
  let started=performance.now();
  const first=await draft(true);
  console.log(JSON.stringify({run:1,ms:Math.round(performance.now()-started),...first}));
  if(!/Draft ready/.test(first.status))throw new Error('First local draft failed: '+first.status);
  if(first.requests!==1||!first.model)throw new Error('The model was not requested once through the file input');
  if(!first.output.trim()||first.apply)throw new Error('No applicable draft');
  if(first.streamed<2)throw new Error('The draft did not stream: '+first.streamed+' distinct editor states');
  if(first.prompt!=='A lighthouse on an icy coast')throw new Error('The prompt changed before apply');
  await evaluate("document.getElementById('llm-apply').click();0");
  const applied=await evaluate("document.getElementById('prompt').value");
  if(applied!==first.output)throw new Error('Apply did not place the draft in the prompt');
  await evaluate("document.getElementById('prompt').value='A lighthouse on an icy coast';0");
  started=performance.now();
  const second=await draft(false);
  console.log(JSON.stringify({run:2,ms:Math.round(performance.now()-started),status:second.status,requests:second.requests,same:second.output===first.output}));
  if(second.requests!==1)throw new Error('The second draft requested the model again');
  if(second.output!==first.output)throw new Error('Greedy drafts differ between runs on one loaded model');
  if(second.engineCalls!==0)throw new Error('The writing assistant invoked image generation');
  console.log('PASS Studio local provider: selectable, one GGUF request through the page, streamed draft ('+first.streamed+' editor states, '+first.output.length+' chars) applied only on click, second draft reuses the model and is identical, no image entry');
},route);
