import { readFileSync, writeFileSync, readdirSync, mkdirSync, existsSync, chmodSync, realpathSync } from 'node:fs';
import { resolve, dirname, relative, join, extname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const here = dirname(fileURLToPath(import.meta.url));
const arg = name => { const i = process.argv.indexOf(name); return i < 0 ? null : process.argv[i + 1]; };
const web = resolve(arg('--web') || join(here, 'web'));
const realWeb = realpathSync(web);
const only = arg('--only')?.split(',');
const output = join(web, 'embedded');
const posix = path => path.split(sep).join('/');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function filesUnder(path) {
  if (!existsSync(path)) return [];
  return readdirSync(path, { withFileTypes: true }).flatMap(e => e.isDirectory() ? filesUnder(join(path, e.name)) : [join(path, e.name)]);
}
function under(root, path) {
  const resolved = resolve(root, path);
  if (!resolved.startsWith(root + sep)) throw new Error('Asset escapes bundle: ' + path);
  if (existsSync(resolved) && !realpathSync(resolved).startsWith(realWeb + sep)) throw new Error('Asset link escapes bundle: ' + path);
  return resolved;
}
const htmlUnder = path => filesUnder(join(web, path)).filter(p => extname(p) === '.html').sort();
const group = (name, pages, assets, shared = []) => ({ name, pages, assets, shared });
const groups = [
  group('games', htmlUnder('games'), filesUnder(join(web, 'games')).filter(p => extname(p) === '.wasm')),
  ...['c64', 'data', 'mathbook', 'safari', 'spark', 'starmap'].map(name => group(name, htmlUnder(name),
    filesUnder(join(web, name)).filter(p => ['.wasm', '.dat'].includes(extname(p))))),
  group('fishtank', htmlUnder('fishtank'), filesUnder(join(web, 'fishtank')).filter(p => ['.wasm', '.png'].includes(extname(p)))),
  group('gpushow', htmlUnder('gpushow').filter(p => /\bfetch\s*\(/.test(readFileSync(p, 'utf8'))), filesUnder(join(web, 'gpushow/kernels')).filter(p => ['.wgsl', '.codex'].includes(extname(p))), ['compiler']),
  group('fireworks', htmlUnder('fireworks'), filesUnder(join(web, 'fireworks')).filter(p => ['.wasm', '.wgsl', '.codex'].includes(extname(p)))),
  group('globe', htmlUnder('globe'), filesUnder(join(web, 'globe')).filter(p => ['.raw', '.wgsl', '.codex'].includes(extname(p)))),
  group('experimental', htmlUnder('experimental'), [join(web, 'experimental/DeviceEffect.codex'), join(web, 'gpushow/kernels/PlasmaKernel.codex'), join(web, 'gpushow/kernels/PlasmaKernel.wgsl')], ['compiler'])
].filter(g => g.pages.length && (!only || only.includes(g.name)));
if (only && only.some(name => !groups.some(g => g.name === name))) throw new Error('Requested group has no pages or is unknown');
const packs = new Map(groups.map(g => [g.name, g.assets]));
if (groups.some(g => g.shared.includes('compiler'))) packs.set('compiler', ['codex-compiler.wasm', 'wgsl-stdio.wasm', 'wasm-stdio.wasm'].map(name => join(web, 'compile', name)));
const mime = path => ({ '.wasm': 'application/wasm', '.png': 'image/png', '.json': 'application/json', '.wgsl': 'text/plain;charset=utf-8', '.codex': 'text/plain;charset=utf-8' }[extname(path)] || 'application/octet-stream');
const manifest = { complete: !only, groups: [], assets: [], pages: [], documents: [] };
const writes = [];
for (const [name, paths] of packs) {
  if (!paths.length) throw new Error('No packaged assets for ' + name);
  const data = Object.create(null);
  for (const path of [...new Set(paths)].sort()) {
    const checked = under(web, relative(web, path));
    if (!existsSync(checked)) throw new Error('Missing packaged asset: ' + posix(relative(web, checked)));
    const bytes = readFileSync(checked), key = posix(relative(web, checked));
    data[key] = [mime(path), bytes.toString('base64')];
    manifest.assets.push({ pack: name, path: key, bytes: bytes.length, sha256: hash(bytes) });
  }
  let script = 'Object.assign(window.__CB_ASSETS=window.__CB_ASSETS||Object.create(null),' + JSON.stringify(data) + ');\n';
  if (name === 'games') {
    const imports = {};
    for (const module of ['arcade.js', 'rules.js']) {
      const bytes = readFileSync(under(web, 'games/' + module));
      imports['./' + module] = 'data:text/javascript;charset=utf-8;base64,' + bytes.toString('base64');
      manifest.assets.push({ pack: name, kind: 'module', path: 'games/' + module, bytes: bytes.length, sha256: hash(bytes) });
    }
    script += 'window.__CB_IMPORTS=' + JSON.stringify(imports) + ';\nwindow.cbInstallImports();\n';
  }
  writes.push([join(output, name + '.js'), script]);
  manifest.groups.push({ name, assets: paths.length, encodedBytes: Buffer.byteLength(script) });
}
for (const g of groups) {
  for (const page of g.pages) {
    const root = posix(relative(dirname(page), web)) || '.';
    const prefix = root + '/embedded/';
    const tags = '<!--CB-FILE-ASSETS--><script>window.__CB_ASSET_ROOT=new URL(' + JSON.stringify(root + '/') + ',location.href).href;</script>' +
      '<script src="' + prefix + 'loader.js"></script>' + [...g.shared, g.name].map(name => '<script src="' + prefix + name + '.js"></script>').join('') + '<!--/CB-FILE-ASSETS-->';
    let html = readFileSync(page, 'utf8').replace(/<!--CB-FILE-ASSETS-->[\s\S]*?<!--\/CB-FILE-ASSETS-->/g, '');
    if (/<head(?:\s[^>]*)?>/i.test(html)) html = html.replace(/<head(?:\s[^>]*)?>/i, head => head + tags);
    else if (/<!doctype[^>]*>/i.test(html)) html = html.replace(/<!doctype[^>]*>/i, type => type + tags);
    else html = tags + html;
    if (g.name === 'fishtank') {
      const before = "img.src = 'assets/' + name + '.png';";
      const after = "img.src = window.cbAssetUrl('assets/' + name + '.png');";
      if (!html.includes(before) && !html.includes(after)) throw new Error('Fishtank image loader changed; update the package adapter');
      html = html.replace(before, after);
    }
    writes.push([page, html]);
    manifest.pages.push({ path: posix(relative(web, page)), packs: [...g.shared, g.name] });
  }
}
mkdirSync(output, { recursive: true });
writes.push([join(output, 'loader.js'), readFileSync(join(here, 'embedded-assets.js'), 'utf8')]);
const rewritten = new Map(writes);
manifest.documents = htmlUnder('').map(path => ({ path: posix(relative(web, path)), sha256: hash(rewritten.has(path) ? Buffer.from(rewritten.get(path)) : readFileSync(path)) }));
writes.push([join(output, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n']);
for (const [path, content] of writes) {
  under(web, relative(web, path));
  if (existsSync(path) && readFileSync(path, 'utf8') === content) continue;
  if (existsSync(path)) chmodSync(path, 0o666);
  writeFileSync(path, content);
}
console.log(JSON.stringify({ pages: manifest.pages.length, packs: packs.size, assets: manifest.assets.length, manifest: join(output, 'manifest.json') }));
