import assert from 'node:assert/strict';
import { createWizardSupport } from './studio-wizard.mjs';

const support=createWizardSupport();
const draft={name:'Cloud Harbor',story:'Akara watches the harbor.',glossary:'Akara, Cloud Harbor',style:'High Fantasy',styleNotes:'',prompts:'',size:'1344x768',steps:'20',cfg:'7',sampler:'DPM++ 2M',providerKey:'fixture-secret-key'};
draft.prompts=support.starterPrompts(draft);
const output=support.files(draft);
assert.deepEqual(Object.keys(output),['universe.json','Story.md','art_directions.json','creative_pools.json','ArtPrompts.txt','spark_project.json']);
assert.equal(Object.values(output).some(text=>text.includes(draft.providerKey)),false);
const manifest=JSON.parse(output['spark_project.json']);
assert.deepEqual(manifest.defaultSettings,{width:1344,height:768,steps:20,cfgScale:7,sampler:'DPM++ 2M',scheduler:'karras'});
assert.deepEqual(manifest.storyFiles,['Story.md']);
assert.deepEqual(JSON.parse(output['universe.json']).properNouns,['Akara','Cloud Harbor']);
assert.equal(support.glossary('The Akara watches Cloud Harbor. Akara returns.'),'Akara, Cloud Harbor');
assert.equal(support.folderName('../A/B?'),'.._A_B_');
for(const name of ['', '.', '..', 'x'.repeat(129)])assert.throws(()=>support.folderName(name),/project name/);
for(const [key,value] of [['size','9999x9999'],['steps','0'],['steps','2.5'],['cfg','NaN'],['cfg','31'],['sampler','unknown'],['story','x'.repeat(16385)]])assert.throws(()=>support.files({...draft,[key]:value}));
for(const prompts of ['','  \n'])assert.equal(support.files({...draft,prompts})['ArtPrompts.txt'],support.starterPrompts(draft));
assert.equal(support.files({...draft,prompts:'PROMPT 01 - "Own"\nx'})['ArtPrompts.txt'],'PROMPT 01 - "Own"\nx');

function fixture({existing=false,foreign=false,fail=''}={}) {
  const data=new Map(existing||foreign?[['keep.txt','untouched']]:[]),writes=[];
  let opened=0,aborted=0;
  const directory={name:'Cloud Harbor',async *entries(){yield*data.entries()},async getFileHandle(name){return {async createWritable(){let next;return {async write(text){next=text;if(fail===name)throw Error('fixture write failure')},async close(){data.set(name,next);writes.push(name)},async abort(){aborted++}}}}}};
  const parent={async getDirectoryHandle(name,options){opened++;assert.equal(name,'Cloud Harbor');if(!options?.create&&!existing)throw new DOMException('absent','NotFoundError');return directory}};
  return {parent,data,writes,opened:()=>opened,aborted:()=>aborted};
}
const valid=(text,count)=>{assert.equal(text,draft.prompts);assert.equal(count,0);return {ok:true,count:3}};
const success=fixture();
const created=await support.createProject(success.parent,draft,valid);
assert.equal(created.count,3);assert.deepEqual(created.paths,Object.keys(output));
assert.deepEqual([...success.data],Object.entries(output));assert.equal(success.writes.at(-1),'spark_project.json');
const existing=fixture({existing:true});
await assert.rejects(support.createProject(existing.parent,draft,valid),/already exists/);
assert.deepEqual([...existing.data],[['keep.txt','untouched']]);assert.deepEqual(existing.writes,[]);
const race=fixture({foreign:true});
await assert.rejects(support.createProject(race.parent,draft,valid),/not empty/);
assert.deepEqual([...race.data],[['keep.txt','untouched']]);
const invalid=fixture();
await assert.rejects(support.createProject(invalid.parent,draft,()=>({ok:false,error:'fixture invalid prompts'})),/fixture invalid prompts/);
assert.equal(invalid.opened(),0);
const failed=fixture({fail:'art_directions.json'});
await assert.rejects(support.createProject(failed.parent,draft,valid),/creation incomplete.*2 files written.*fixture write failure/);
assert.equal(failed.aborted(),1);assert.equal(failed.data.has('spark_project.json'),false);
console.log('PASS: wizard file schema, key exclusion, bounded fields, existing-folder refusal, validation before I/O, partial-write refusal and manifest-last commit; in-memory handles');
