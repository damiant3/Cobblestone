import assert from 'node:assert/strict';
import vm from 'node:vm';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { anthropicProviderSource } from './llm-provider.mjs';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
let network = 0;
const context = vm.createContext({ AbortController, TextDecoder, fetch() { network++; throw new Error('Live network forbidden in this arm'); } });
const create = vm.runInContext(anthropicProviderSource(repo), context);
const values = new Map([['prism.claude.key', 'unrelated-sentinel']]);
const storage = { getItem: k => values.get(k) || null, setItem: (k, v) => values.set(k, String(v)), removeItem: k => values.delete(k) };
const provider = create(storage, 'spark.llm.anthropic.key');
const user = [{ role: 'user', content: 'A lighthouse in winter' }];
const event = value => 'data: ' + JSON.stringify(value) + '\n\n';
const turn = (text, reason = 'end_turn', details) => [
  { type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } },
  { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text } },
  { type: 'content_block_stop', index: 0 },
  { type: 'message_delta', delta: { stop_reason: reason, stop_details: details } },
  { type: 'message_stop' }
].map(event).join('');
await assert.rejects(provider.stream(user), /Enter your Anthropic key/);
assert.equal(network, 0);
provider.setKey(' test-only-key ');
assert.equal(values.get('spark.llm.anthropic.key'), 'test-only-key');
assert.equal(values.get('prism.claude.key'), 'unrelated-sentinel');
let captured;
provider.setTransport(async (key, body, push) => {
  captured = { key, body };
  const text = turn('A snowy lighthouse 日本.');
  for (let i = 0; i < text.length; i += 7) push(text.slice(i, i + 7));
});
const result = await provider.stream(user, { effort: 'high', system: 'Creative director' });
assert.equal(result.text, 'A snowy lighthouse 日本.');
assert.equal(captured.key, 'test-only-key');
assert.equal(captured.body.model, 'claude-opus-5');
assert.equal(captured.body.max_tokens, 64000);
assert.equal(captured.body.thinking.type, 'adaptive');
assert.equal(captured.body.thinking.display, 'summarized');
assert.equal(captured.body.thinking.budget_tokens, undefined);
assert.equal(captured.body.output_config.effort, 'high');
assert.equal(captured.body.system, 'Creative director');
assert.equal(JSON.stringify(captured.body).includes('test-only-key'), false);
const toolEvents = [
  { type: 'content_block_start', index: 0, content_block: { type: 'thinking' } },
  { type: 'content_block_delta', index: 0, delta: { type: 'thinking_delta', thinking: 'Summary' } },
  { type: 'content_block_delta', index: 0, delta: { type: 'signature_delta', signature: 'signed-test' } },
  { type: 'content_block_stop', index: 0 },
  { type: 'content_block_start', index: 1, content_block: { type: 'tool_use', id: 't1', name: 'read_file' } },
  { type: 'content_block_delta', index: 1, delta: { type: 'input_json_delta', partial_json: '{"path":"Story.md"}' } },
  { type: 'content_block_stop', index: 1 },
  { type: 'message_delta', delta: { stop_reason: 'tool_use' } },
  { type: 'message_stop' }
].map(event).join('');
provider.setTransport(async (key, body, push) => { for (const ch of toolEvents) push(ch); });
const tools = await provider.stream(user);
assert.equal(tools.content[0].signature, 'signed-test');
assert.equal(tools.tools[0].input.path, 'Story.md');
assert.equal(tools.stopReason, 'tool_use');
let requests = 0;
context.fetch = async (url, options) => {
  requests++;
  assert.equal(url, 'https://api.anthropic.com/v1/messages');
  assert.equal(options.method, 'POST');
  assert.equal(options.headers['anthropic-version'], '2023-06-01');
  assert.equal(options.headers['anthropic-dangerous-direct-browser-access'], 'true');
  assert.equal(options.headers['x-api-key'], 'test-only-key');
  const bytes = new TextEncoder().encode(turn('日本'));
  let index = 0;
  return { ok: true, body: { getReader: () => ({ read: async () => index < bytes.length ? { value: bytes.slice(index, ++index), done: false } : { done: true } }) } };
};
provider.setTransport(null);
assert.equal((await provider.stream(user)).text, '日本');
assert.equal(requests, 1);
await assert.rejects(provider.stream([{ role: 'assistant', content: 'prefill' }]), /prefill/);
provider.setTransport(async (key, body, push) => push(turn('', 'refusal', { category: 'policy' })));
assert.equal((await provider.stream(user)).refusal.category, 'policy');
provider.setTransport(async (key, body, push) => push(event({ type: 'error', error: { message: 'test service error' } })));
await assert.rejects(provider.stream(user), /test service error/);
provider.setTransport(async (key, body, push) => push(event({ type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: 'partial' } })));
await assert.rejects(provider.stream(user), /Incomplete provider stream/);
provider.setTransport(async (key, body, push) => push(event({ type: 'message_delta', delta: { stop_reason: 'end_turn' } })));
await assert.rejects(provider.stream(user), /Incomplete provider stream/);
provider.setTransport(async (key, body, push) => push('x'.repeat(1048577)));
await assert.rejects(provider.stream(user), /response exceeds/);
const abort = new AbortController();
let released = false;
provider.setTransport(async (key, body, push, signal) => {
  signal.addEventListener('abort', () => { released = true; });
  abort.abort();
  push(turn('late result'));
});
await assert.rejects(provider.stream(user, { signal: abort.signal }), { name: 'AbortError' });
assert.equal(released, true);
const withinChunk = new AbortController();
let callbacks = 0;
provider.setTransport(async (key, body, push) => push(turn('must not finish')));
await assert.rejects(provider.stream(user, { signal: withinChunk.signal }, () => {
  callbacks++;
  withinChunk.abort();
}), { name: 'AbortError' });
assert.equal(callbacks, 1);
provider.clearKey();
assert.equal(provider.hasKey(), false);
assert.equal(values.get('prism.claude.key'), 'unrelated-sentinel');
assert.equal(network, 0);
console.log('PASS: canonical Prism provider, key isolation, request shape, split SSE, refusal, truncation, bounds and cancellation; no network');
