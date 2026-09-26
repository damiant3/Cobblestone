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
  const unityOnly = process.argv.includes('--unity');
  const unity = ctx.targetDefaults('unity');
  ctx.targetSave(unity);
  const second = unityOnly ? Object.assign({}, unity, { id: 'unity-alternate' }) : ctx.targetDefaults('darwin');
  ctx.targetSave(second);
  assert.equal(ctx.targetProfiles().length, 2);
  assert.deepEqual(JSON.parse(JSON.stringify(ctx.targetProfiles()[0])), JSON.parse(JSON.stringify(unity)));
  assert.equal(unity.groupLimit, 4);
  assert.equal(ctx.targetValidate(Object.assign({}, unity, { groupLimit: 64 })).groupLimit, 4);
  assert.equal(ctx.targetValidate(Object.assign({}, unity, { groupLimit: 128 })).groupLimit, 4);
  for (const change of [{ profile: 'future-version' }, { kind: 'unknown' }, { version: 2 },
    { id: '../escape' }, { groupLimit: 0 }, { groupLimit: 1.5 }, { groupLimit: 129 },
    { gamePath: 'x\nstart-process bad' }, { gamePath: 'D:/game', testSavePath: 'd:/game/' }]) {
    assert.throws(() => ctx.targetValidate(Object.assign({}, unity, change)));
  }
  const migrated = ctx.targetValidate(Object.assign({}, unity, { chestRadius: 20, stationRadius: 30 }));
  assert.equal(Object.hasOwn(migrated, 'chestRadius'), false);
  assert.equal(Object.hasOwn(migrated, 'stationRadius'), false);
  const managed = await ctx.targetEmit(unity);
  const cs = new TextDecoder().decode(managed.bytes);
  assert.match(cs, /public static class PrismUnityEntry/);
  assert.doesNotMatch(cs, /_codexMain|static void Main/);
  assert.match(cs, /6000\.0\.75f1/);
  let appleWire;
  if (!unityOnly) {
    appleWire = await ctx.targetEmit(second);
    assert.equal(appleWire.extension, '.arm64-wire');
    const compiler = await ctx.moduleBytes('codex-compiler.wasm');
    const compiled = ctx.runModule(compiler, 'IR-UNI decks=125\n' + ctx.assembleUnit().text).text;
    const ir = compiled.slice(compiled.indexOf('IR-BEGIN') + 8, compiled.indexOf('IR-END')).trim();
    const baseline = ctx.runModule(await ctx.moduleBytes('arm64-stdio.wasm'), ir);
    assert.notDeepEqual(Buffer.from(baseline.bytes), Buffer.from(appleWire.bytes), 'Darwin must not emit the virt target');
  }
  const unityModule = await ctx.moduleBytes('unity-stdio.wasm');
  assert.match(ctx.runModule(unityModule, 'UNITY future\n(chapter "Bad")').text, /^REFUSED/);
  assert.match(ctx.runModule(unityModule, 'UNITY windows-x64-mono-6000.0.75f1\nnot IR').text, /^REFUSED/);
  assert.doesNotMatch(cs, /class PrismStorage/);
  assert.doesNotMatch(cs, /class PrismMeadowsSpawns/);
  assert.doesNotMatch(cs, /class PrismModIdentity/);
  assert.doesNotMatch(cs, /class PrismCircumhorizontalArc/);
  const demo = assetContext.window.__TEMPLATES.ValheimLinkedStorage;
  assert.ok(demo && demo.files['LinkedStorage.codex'] && demo.files['ValheimStorage.codex']);
  for (const [name,text] of Object.entries(demo.files)) assert.equal(text,fs.readFileSync(path.join(repo,'apps/modbuilder/mods/valheim',name),'utf8').replace(/\r\n/g,'\n'));
  const originalUnit = ctx.assembleUnit;
  ctx.assembleUnit = () => ({text:Object.values(demo.files).join('\n'),regions:[]});
  const mod = await ctx.targetEmit(unity);
  const modCs = new TextDecoder().decode(mod.bytes);
  assert.match(modCs,/class PrismStorage/);
  assert.match(modCs,/PrismStorage.Install/);
  assert.match(modCs,/class PrismMeadowsSpawns/);
  assert.match(modCs,/PrismMeadowsSpawns.Install/);
  assert.match(modCs,/class PrismModIdentity/);
  assert.match(modCs,/__PRISM_BUILD_NUMBER__/);
  assert.match(modCs,/class PrismCircumhorizontalArc/);
  assert.match(modCs,/PrismCircumhorizontalArc.Install/);
  assert.match(modCs,/__PRISM_GAME_HASH__/);
  assert.match(modCs,/__PRISM_UNITY_HASH__/);
  ctx.assembleUnit = () => ({text:demo.files['MeadowsSpawns.codex'] + '\nChapter: Meadows Entry\n\nSection: Engine Entry\n\n  effect Process where\n    unity-meadows-spawns-start : (Integer, Integer -> Integer) -> [Process] Nothing\n\nSection: Entry\n\n  opening : [Process] Nothing = unity-meadows-spawns-start meadows-neck-clearance-delay\n',regions:[]});
  const meadows = await ctx.targetEmit(unity);
  const meadowsCs = new TextDecoder().decode(meadows.bytes);
  assert.match(meadowsCs,/class PrismMeadowsSpawns/);
  assert.match(meadowsCs,/class PrismModIdentity/);
  assert.doesNotMatch(meadowsCs,/class PrismStorage/);
  ctx.assembleUnit = () => ({text:demo.files['CircumhorizontalArc.codex'] + '\nChapter: Arc Entry\n\nSection: Engine Entry\n\n  effect Process where\n    unity-circumhorizontal-arc-start : ArcPreviewSettings -> [Process] Nothing\n\nSection: Entry\n\n  opening : [Process] Nothing = unity-circumhorizontal-arc-start arc-preview-settings\n',regions:[]});
  const arc = await ctx.targetEmit(unity);
  const arcCs = new TextDecoder().decode(arc.bytes);
  assert.match(arcCs,/class PrismCircumhorizontalArc/);
  assert.doesNotMatch(arcCs,/class PrismStorage/);
  assert.doesNotMatch(arcCs,/class PrismMeadowsSpawns/);
  ctx.assembleUnit = originalUnit;
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
  for (const prefix of unityOnly ? [] : ['', 'ELF\n', 'DARWIN\n']) {
    assert.match(ctx.runModule(await ctx.moduleBytes('arm64-stdio.wasm'), prefix + 'not IR').text, /^REFUSED/);
  }
  const out = path.join(repo, 'build-output/prism-targets');
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, 'page-unity.cs'), managed.bytes);
  fs.writeFileSync(path.join(out, 'page-storage.cs'), mod.bytes);
  fs.writeFileSync(path.join(out, 'page-meadows.cs'), meadows.bytes);
  fs.writeFileSync(path.join(out, 'page-arc.cs'), arc.bytes);
  if (appleWire) fs.writeFileSync(path.join(out, 'page-darwin.wire'), appleWire.bytes);
  console.log(unityOnly ? 'PASS: Unity profiles, linked-storage emission, and version/IR refusals' : 'PASS: saved target profiles, invalid profiles, actual Unity emission, Darwin framing, and version/IR refusals');
}
main().catch(err => { console.error(err); process.exitCode = 1; });
