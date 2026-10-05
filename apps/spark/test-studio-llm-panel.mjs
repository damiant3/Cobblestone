import assert from 'node:assert/strict';
import vm from 'node:vm';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { anthropicProviderSource } from '../prism/llm-provider.mjs';
import { installStudioLlm } from './studio-llm-panel.mjs';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const nodes = new Map();
const get = id => {
  if (!nodes.has(id)) nodes.set(id, { value: '', textContent: '', disabled: false, checked: false, handlers: {}, addEventListener(k, fn) { this.handlers[k] = fn; }, dispatchEvent(e) { this.handlers[e.type]?.(e); } });
  return nodes.get(id);
};
get('llm-provider').value = 'anthropic';
get('llm-action').value = 'expand';
get('llm-use-context').checked = true;
get('prompt').value = 'A lighthouse';
get('log').textContent = 'Engine refused: model has not been loaded';
let nativeBusy = false, llmBusy = false, project = 'project-a', generates = 0;
get('go').addEventListener('click', () => { generates++; });
const files = {
  manifest: { text: async () => JSON.stringify({ storyFiles: ['Story.md'] }) },
  story: { text: async () => 'The lighthouse stands in an icy sea.' }
};
const listing = [{ name: 'manifest', path: 'Project/spark_project.json', size: 60 }, { name: 'story', path: 'Project/Story.md', size: 100 }];
let contextCalled = 0;
let promptValidation = true, expectedPromptCount = 0;
const storage = new Map();
const create = vm.runInNewContext(anthropicProviderSource(repo), { AbortController, TextDecoder, fetch() { throw new Error('Live network forbidden'); } });
const provider = create({ getItem: k => storage.get(k) || null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) }, 'spark.llm.anthropic.key');
const providers = { anthropic: provider, local: { id: 'local', unavailableReason: 'local GGUF generation is not available in this build.' } };
const ui = installStudioLlm({ getElementById: get }, providers, {
  busy: () => nativeBusy, setBusy: v => { llmBusy = v; }, project: () => project,
  listing: () => listing, file: name => files[name],
  context(request) { contextCalled++; assert.equal(request.documents[0].name, 'Story.md'); return { ok: true, context: request.documents[0].content }; },
  prompts(text,count) { expectedPromptCount=count; return {ok:promptValidation,count,error:promptValidation?'':'fixture prompt refusal'}; },
  glossary:()=> 'Akara'
});
const event = value => 'data: ' + JSON.stringify(value) + '\n\n';
const response = (text, reason = 'end_turn') => [
  { type: 'content_block_start', index: 0, content_block: { type: 'text' } },
  { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text } },
  { type: 'content_block_stop', index: 0 },
  { type: 'message_delta', delta: { stop_reason: reason, stop_details: { category: 'test refusal' } } },
  { type: 'message_stop' }
].map(event).join('');
let requests = 0, body;
const success = async (key, request, push) => { requests++; body = request; for (const chunk of response('An icy lighthouse at dawn.').match(/.{1,11}/gs)) push(chunk); };
provider.setTransport(success);
assert.equal(get('llm-run').disabled, true);
await ui.run();
assert.equal(requests, 0);
get('llm-key').value = 'fixture-key';
get('llm-key-save').handlers.click();
assert.equal(get('llm-key').value, '');
assert.equal(get('llm-run').disabled, false);
await ui.run();
assert.equal(contextCalled, 1);
assert.match(body.messages[0].content, /icy sea/);
assert.equal(JSON.stringify(body).includes('fixture-key'), false);
assert.equal(get('prompt').value, 'A lighthouse');
assert.equal(get('llm-output').value, 'An icy lighthouse at dawn.');
ui.apply();
assert.equal(get('prompt').value, 'An icy lighthouse at dawn.');
assert.equal(generates, 0);
await ui.run();
get('prompt').value = 'User edited this';
ui.apply();
assert.equal(get('prompt').value, 'User edited this');
assert.match(get('llm-status').textContent, /changed/);
for (const reason of ['refusal', 'max_tokens', 'tool_use']) {
  provider.setTransport(async (key, body, push) => push(response('partial', reason)));
  await ui.run();
  assert.equal(get('llm-apply').disabled, true);
  assert.match(get('llm-status').textContent, /Refused:/);
}
let started;
const ready = new Promise(resolve => { started = resolve; });
provider.setTransport((key, body, push, signal) => new Promise((resolve, reject) => {
  signal.addEventListener('abort', () => { const error = new Error('cancelled'); error.name = 'AbortError'; reject(error); });
  started();
}));
const pending = ui.run();
await ready;
assert.equal(get('open').disabled, true);
ui.cancel();
await pending;
assert.equal(llmBusy, false);
assert.equal(get('open').disabled, false);
assert.match(get('llm-status').textContent, /cancelled/);
provider.setTransport(async (key, body, push) => { project = 'project-b'; push(response('stale')); });
await ui.run();
assert.equal(get('llm-apply').disabled, true);
assert.match(get('llm-status').textContent, /Project changed/);
provider.setTransport(success);
get('llm-use-context').checked = false;
get('llm-action').value = 'refusal';
await ui.run();
assert.equal(get('llm-context').value, '');
assert.equal(get('llm-apply').disabled, true);
assert.match(body.messages[0].content, /Engine refused/);
let alternateCalls = 0;
providers.fixture = { id: 'fixture', async stream() { alternateCalls++; return { text: 'Another provider explanation', thinking: '', tools: [], stopReason: 'end_turn' }; } };
get('llm-provider').value = 'fixture';
await ui.run();
assert.equal(alternateCalls, 1);
assert.equal(get('llm-output').value, 'Another provider explanation');
get('llm-provider').value = 'local';
const before = requests;
await ui.run();
assert.equal(requests, before);
assert.match(get('llm-status').textContent, /local GGUF/);
get('llm-provider').value = 'anthropic';
nativeBusy = true;
await ui.run();
assert.equal(requests, before);
nativeBusy = false;
get('wizard').open=true;
get('wizard-name').value='Cloud Harbor';get('wizard-story').value='Akara lives in a city above the sea.';
get('wizard-count').value='2';get('wizard-style').value='High Fantasy';get('wizard-style-notes').value='';
get('llm-action').value='synopsis';
provider.setTransport(async(key,body,push)=>push(response('Akara guards Cloud Harbor.')));
await ui.run();assert.equal(get('wizard-story').value,'Akara lives in a city above the sea.');ui.apply();
assert.equal(get('wizard-story').value,'Akara guards Cloud Harbor.');assert.equal(get('wizard-glossary').value,'Akara');
get('llm-action').value='prompts';get('wizard-prompts').value='Original';
await ui.run();assert.equal(expectedPromptCount,2);assert.equal(get('wizard-prompts').value,'Original');
get('wizard-count').value='3';ui.apply();assert.equal(get('wizard-prompts').value,'Original');assert.match(get('llm-status').textContent,/wizard inputs changed/i);
get('wizard-count').value='2';promptValidation=false;await ui.run();assert.equal(get('llm-apply').disabled,true);assert.match(get('llm-status').textContent,/fixture prompt refusal/);
promptValidation=true;provider.setTransport(async(key,body,push)=>{get('wizard-style').value='Cyberpunk';push(response('stale'))});
await ui.run();assert.equal(get('llm-apply').disabled,true);assert.match(get('llm-status').textContent,/Wizard inputs changed/);
provider.setTransport(async(key,body,push)=>push(response('Two accepted prompts')));await ui.run();ui.apply();assert.equal(get('wizard-prompts').value,'Two accepted prompts');
get('wizard').open=false;await ui.run();assert.match(get('llm-status').textContent,/Open New project/);
get('llm-key-clear').handlers.click();
assert.equal(storage.has('spark.llm.anthropic.key'), false);
assert.equal(generates, 0);
console.log('PASS: explicit draft/apply, context routing, key handling, no generation, refusal/truncation/tools, cancellation and stale-state guards; no network');
