const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const repo = path.resolve(__dirname, '../..');
const page = fs.readFileSync(path.join(repo, 'apps/landing/web/compile/prism.html'), 'utf8');
const script = page.slice(page.lastIndexOf('<script>') + 8, page.lastIndexOf('</script>'));
const template = fs.readFileSync(path.join(repo, 'codex/plugs/wasm/page/prism.html'), 'utf8');
assert.equal(script.replace(/\r\n/g, '\n'), template.slice(template.lastIndexOf('<script>') + 8, template.lastIndexOf('</script>')).replace(/\r\n/g, '\n'));
const assets = page.match(/<script>\s*window\.__EMBED = \{[\s\S]*?<\/script>/);
assert.ok(assets, 'Missing embedded page assets');
const assetContext = { window: {} };
vm.runInNewContext(assets[0].slice(8, -9), assetContext, { timeout: 5000 });
new vm.Script(script);
function slice(from, to) {
  const start = script.indexOf(from), end = script.indexOf(to, start);
  assert.ok(start >= 0 && end > start, 'Missing page section: ' + from);
  return script.slice(start, end);
}
const storage = new Map();
const elements = {};
const element = () => ({style:{},handlers:{},value:'',addEventListener(k,f){this.handlers[k]=f;},appendChild(){}});
const ctx = {
  TextEncoder, TextDecoder, Uint8Array, DataView, WebAssembly, performance, console,
  document: { getElementById(id) { return elements[id] || (elements[id]=element()); }, createElement: element },
  localStorage: { getItem: k => storage.get(k), setItem: (k,v) => storage.set(k,v) },
  activePath: null, DECKS: [125],
  assembleUnit: () => ({ text: 'Chapter: TargetProbe\n\n  opening : Integer = 7\n', regions: [] }),
  resolveUnit: async x => Object.assign(x, { missing: [] }),
  moduleBytes: async name => {
    assert.ok(assetContext.window.__EMBED[name], 'Missing embedded module: ' + name);
    return Buffer.from(assetContext.window.__EMBED[name], 'base64');
  }
};
vm.createContext(ctx);
vm.runInContext(slice('function mountDisk(', '// The compile runs OFF'), ctx);
ctx.runW = async (bytes, input) => ctx.runModule(bytes, input);
vm.runInContext(slice('const TARGET_TYPES =', 'function cfgSay('), ctx);
async function main() {
  const darwin = ctx.targetDefaults('darwin');
  ctx.targetSave(darwin);
  ctx.targetSave(Object.assign({}, darwin, { id: 'darwin-alternate' }));
  assert.equal(ctx.targetProfiles().length, 2);
  assert.deepEqual(JSON.parse(JSON.stringify(ctx.targetProfiles()[0])), JSON.parse(JSON.stringify(darwin)));
  for (const change of [{ profile: 'future-version' }, { kind: 'unknown' }, { kind: 'unity' }, { version: 2 },
    { id: '../escape' }, { macToolchain: 'x\nstart-process bad' }]) {
    assert.throws(() => ctx.targetValidate(Object.assign({}, darwin, change)));
  }
  const appleWire = await ctx.targetEmit(darwin);
  assert.equal(appleWire.extension, '.arm64-wire');
  const compiler = await ctx.moduleBytes('codex-compiler.wasm');
  const compiled = ctx.runModule(compiler, 'IR-UNI decks=125\n' + ctx.assembleUnit().text).text;
  const ir = compiled.slice(compiled.indexOf('IR-BEGIN') + 8, compiled.indexOf('IR-END')).trim();
  const baseline = ctx.runModule(await ctx.moduleBytes('arm64-stdio.wasm'), ir);
  assert.notDeepEqual(Buffer.from(baseline.bytes), Buffer.from(appleWire.bytes), 'Darwin must not emit the virt target');
  assert.equal(assetContext.window.__EMBED['unity-stdio.wasm'], undefined, 'Prism no longer ships the Unity module');
  assert.equal(assetContext.window.__MODS, undefined, 'Prism no longer carries the mod catalogue');
  const deleted=[];
  const firstDir={removeEntry:async p=>deleted.push('first/'+p)};
  const secondDir={removeEntry:async p=>deleted.push('second/'+p)};
  ctx.opfsProjectDir=async()=>null;
  ctx.dirForPath=async(dir,leaf)=>({dir,leaf});
  ctx.fsaDir=firstDir;
  vm.runInContext(slice('async function persistDelete(', 'async function loadPersisted('),ctx);
  const pending=ctx.persistDelete('owned.codex');ctx.fsaDir=secondDir;await pending;
  assert.deepEqual(deleted,['first/owned.codex']);
  deleted.length=0;ctx.fsaDir=null;
  const detached=ctx.persistDelete('old.codex');ctx.fsaDir=secondDir;await detached;
  assert.deepEqual(deleted,[]);
  for (const prefix of ['', 'ELF\n', 'DARWIN\n']) {
    assert.match(ctx.runModule(await ctx.moduleBytes('arm64-stdio.wasm'), prefix + 'not IR').text, /^REFUSED/);
  }
  const out = path.join(repo, 'build-output/prism-targets');
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, 'page-darwin.wire'), appleWire.bytes);
  console.log('PASS: saved Darwin target profiles, invalid profiles, Darwin framing, IR refusals, and no Unity module or mod catalogue in Prism');
}
main().catch(err => { console.error(err); process.exitCode = 1; });
