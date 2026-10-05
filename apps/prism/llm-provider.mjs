import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export function anthropicProviderSource(repo) {
  const page = readFileSync(join(repo, 'codex/plugs/wasm/page/prism.html'), 'utf8');
  const start = 'const CLAUDE_URL =';
  const stop = '// ---- Stage 4b:';
  const a = page.indexOf(start), b = page.indexOf(stop);
  if (a < 0 || b <= a || a !== page.lastIndexOf(start) || b !== page.lastIndexOf(stop)) {
    throw new Error('Prism provider boundaries changed; shared provider packaging refused');
  }
  const canonical = page.slice(a, b);
  return `(function createAnthropicProvider(storage, storageKey) {
    if (!storageKey) throw new Error('Provider storage namespace required');
    const window = {};
    const localStorage = {
      getItem() { return storage.getItem(storageKey); },
      setItem(k, v) { storage.setItem(storageKey, v); }
    };
    ${canonical}
    const api = window.__claude;
    let service = fetchTransport;
    const cancelled = () => { const e = new Error('Request cancelled'); e.name = 'AbortError'; return e; };
    api.setTransport(async (key, body, onChunk, signal) => {
      let received = 0;
      return service(key, body, chunk => {
        if (signal.aborted) throw cancelled();
        received += chunk.length;
        if (received > 1048576) throw new Error('Provider response exceeds the text limit');
        onChunk(chunk);
      }, signal);
    });
    return {
      id: 'anthropic',
      hasKey() { return !!claudeKey(); },
      setKey(key) {
        storage.setItem(storageKey, String(key).trim());
        if (claudeKey() !== String(key).trim()) throw new Error('Browser could not retain the provider key');
      },
      clearKey() { storage.removeItem(storageKey); if (claudeKey()) throw new Error('Browser could not remove the provider key'); },
      setTransport(next) { service = next || fetchTransport; },
      async stream(messages, options = {}, onDelta) {
        if (!Array.isArray(messages) || !messages.length) throw new Error('At least one message is required');
        if (messages[messages.length - 1].role === 'assistant') throw new Error('Assistant prefill is not supported');
        if (messages.length > 64 || JSON.stringify(messages).length > 262144) throw new Error('Provider request exceeds the message limit');
        if (!claudeKey()) throw new Error('Enter your Anthropic key in this browser first');
        const controller = new AbortController();
        const cancel = () => controller.abort();
        if (options.signal?.aborted) throw cancelled();
        options.signal?.addEventListener('abort', cancel, { once: true });
        try {
          let complete = false;
          const result = await api.claudeStream(messages, { ...options, signal: controller.signal }, (event, state) => {
            if (controller.signal.aborted) throw cancelled();
            if (event.type === 'message_stop') complete = true;
            if (onDelta) onDelta(event, state);
          });
          if (controller.signal.aborted) throw cancelled();
          if (result.stopReason === 'error') throw new Error(result.refusal?.message || 'Provider stream failed');
          if (!result.stopReason || !complete) throw new Error('Incomplete provider stream: missing stop reason or message_stop');
          return result;
        } finally {
          options.signal?.removeEventListener('abort', cancel);
          controller.abort();
        }
      }
    };
  })`;
}
