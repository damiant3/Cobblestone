// Stage-1 arm: load the SHIPPED page's script (the artifact, not a copy) in a
// stub DOM, then drive the workspace machinery and one real compile.
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const repo = 'D:\\Projects\\Cobblestone-red';
const html = fs.readFileSync(path.join(repo, 'codex\\plugs\\wasm\\page\\prism.html'), 'utf8');
const a = html.lastIndexOf('<script>');
const b = html.lastIndexOf('</script>');
if (a < 0 || b < a) { console.log('FAIL: no script block'); process.exit(1); }
const code = html.slice(a + 8, b);

function el() {
  const e = {
    style: {}, dataset: {}, children: [], handlers: {},
    classList: { add() {}, remove() {}, contains() { return false; } },
    addEventListener(t, fn) { this.handlers[t] = fn; },
    appendChild(c) { this.children.push(c); }, remove() {},
    setAttribute() {}, querySelector() { return null; }, closest() { return null; },
    click() {}, innerHTML: '', textContent: '', value: '', disabled: false,
    scrollTop: 0, scrollLeft: 0, files: [], title: ''
  };
  return e;
}
const byId = {};
const sandbox = {
  console, TextEncoder, TextDecoder, WebAssembly, performance, URL,
  setTimeout, clearTimeout, Blob: class { constructor() {} },
  document: {
    getElementById(id) { return byId[id] || (byId[id] = el()); },
    querySelectorAll() { return []; },
    createElement() { return el(); },
    body: el()
  },
  location: { protocol: 'https:' },
  navigator: {},
  window: {},
  prompt() { return null; }, confirm() { return false; }, alert() {},
  atob(s) { return Buffer.from(s, 'base64').toString('binary'); },
  fetch() { return Promise.reject(new Error('no network in the arm')); },
  crypto: require('node:crypto').webcrypto,
  Worker: undefined
};
sandbox.self = sandbox.window;
sandbox.__fs = fs;
sandbox.Buffer = Buffer;
// The compiler's own source, embedded the way build-page.ps1 embeds it, so
// the preset arms exercise the EMBED path the shipped page takes. The
// modules the driven Compile paths reach ride the same embed.
const compilerSrc = fs.readFileSync(path.join(repo, 'build\\output\\Codex.codex'));
sandbox.window.__EMBED = { 'Codex.codex': compilerSrc.toString('base64') };
for (const m of ['codex-compiler.wasm', 'pe-bytes.wasm', 'evidence-stdio.wasm', 'javascript-stdio.wasm']) {
  sandbox.window.__EMBED[m] =
    fs.readFileSync(path.join(repo, 'codex\\plugs\\wasm\\build-output\\page', m)).toString('base64');
}
sandbox.__compilerSrcText = compilerSrc.toString('utf8');
vm.createContext(sandbox);
// The page encodes module input with TextEncoder (runW) and one function
// later asks `instanceof Uint8Array` (runModule). The host encoder answers a
// HOST-realm array, the contextified script tests its OWN realm's intrinsic,
// and the already-encoded input is re-encoded as text: the mode line arrives
// as digit soup and every driven compile refuses. A browser is one realm, so
// the bed must answer in the page's realm too.
const CtxU8 = vm.runInContext('Uint8Array', sandbox);
sandbox.TextEncoder = class { encode(s) { return new CtxU8(Buffer.from(String(s), 'utf8')); } };
vm.runInContext(code, sandbox, { filename: 'prism-script.js' });
console.log('script loaded: OK');

const drive = `
(async () => {
  // Arm 1: unit assembly and the region table. Three files, one non-unit.
  project.files = [
    { path: 'a.codex', text: 'Chapter: A\\n\\nSection: One\\n\\n  f : Integer -> Integer\\n  f (x) = x\\n' },
    { path: 'notes.md', text: 'not in the unit' },
    { path: 'b.codex', text: 'Chapter: B\\n\\nSection: Two\\n\\n  g : Integer -> Integer\\n  g (x) = f x\\n' }
  ];
  const u = assembleUnit();
  const aLines = 6, expectB = aLines + 1;
  if (u.regions.length !== 2) return 'FAIL: regions ' + u.regions.length;
  if (u.regions[0].path !== 'a.codex' || u.regions[0].start !== 1) return 'FAIL: region a ' + JSON.stringify(u.regions[0]);
  if (u.regions[1].path !== 'b.codex' || u.regions[1].start !== expectB) return 'FAIL: region b ' + JSON.stringify(u.regions[1]);
  if (u.text.indexOf('not in the unit') >= 0) return 'FAIL: non-unit file leaked into the unit';

  // Arm 2: a diagnostic in unit coordinates maps home; line 1 of b is unit line expectB.
  lastRegions = u.regions;
  const mapped = mapDiagLine(expectB + ':3: error CDX3002: Undefined name: q');
  if (mapped !== 'b.codex:1:3: error CDX3002: Undefined name: q') return 'FAIL: mapDiagLine gave ' + mapped;
  const control = mapDiagLine('error CDX3002: no position here');
  if (control !== 'error CDX3002: no position here') return 'FAIL: positionless line was rewritten: ' + control;

  // Arm 3: the zip. Written here, unzipped by PowerShell outside as the referee.
  const zip = makeZip(project.files);
  __fs.writeFileSync(${JSON.stringify(require('os').tmpdir() + '\\prism-arm.zip')}, zip);

  // Arm 4: one REAL compile of the assembled two-chapter unit through the
  // actual compiler module, on the sync fallback path (no Worker here).
  const bytes = __fs.readFileSync(${JSON.stringify('D:\\Projects\\Cobblestone-red\\codex\\plugs\\wasm\\build-output\\page\\codex-compiler.wasm')});
  const r = runModule(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
                      'IR-UNI decks=12\\n' + u.text);
  if (r.text.indexOf('IR-BEGIN') < 0) return 'FAIL: no IR from the two-file unit; head: ' + r.text.slice(0, 300);
  if (r.text.indexOf('"g"') < 0 && r.text.indexOf('(def "g"') < 0) return 'FAIL: chapter B did not reach the IR';

  // Arm 5 (negative): an error in file b reports in b's OWN coordinates after
  // the map, proving the remap points at the right file for a REAL diagnostic.
  project.files[2].text = 'Chapter: B\\n\\nSection: Two\\n\\n  g : Integer -> Integer\\n  g (x) = missing-name x\\n';
  const u2 = assembleUnit();
  lastRegions = u2.regions;
  const r2 = runModule(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
                       'IR-UNI decks=12\\n' + u2.text);
  const diag = r2.text.split('\\n').find(l => l.indexOf('CDX3002') >= 0);
  if (!diag) return 'FAIL: the sabotage produced no CDX3002';
  const home = mapDiagLine(diag);
  if (home.indexOf('b.codex:6:') !== 0) return 'FAIL: real diagnostic mapped to [' + home + '], wanted b.codex:6:...';

  // Arm 6: paint() bounds the DOM at PAINT_MAX lines and says what it kept back.
  const paintEl = document.getElementById('paint-probe');
  paint(paintEl, Array.from({ length: PAINT_MAX + 1000 }, (_, i) => 'line ' + i));
  if (paintEl.children.length !== PAINT_MAX + 1) return 'FAIL: paint kept ' + paintEl.children.length + ' children';
  if (paintEl.children[PAINT_MAX].textContent.indexOf('more lines not painted') < 0)
    return 'FAIL: paint truncation notice reads: ' + paintEl.children[PAINT_MAX].textContent;
  const paintCtl = document.getElementById('paint-probe-2');
  paint(paintCtl, ['one', 'two']);
  if (paintCtl.children.length !== 2) return 'FAIL: small paint gained a notice';

  // Arm 7: past HL_PLAIN the highlighter renders plain text (the pre is the
  // only visible copy, so it must still carry the text), and under it the
  // span path still runs. innerHTML and textContent are separate stub fields,
  // so which path ran is directly observable.
  const hl = document.getElementById('hl'), srcTa = document.getElementById('src');
  srcTa.value = 'x'.repeat(HL_PLAIN + 1);
  hl.innerHTML = ''; hl.textContent = '';
  renderHighlight();
  if (!hl.textContent.endsWith('\\n') || hl.textContent.length !== HL_PLAIN + 2)
    return 'FAIL: big file did not take the plain path';
  if (hl.innerHTML !== '') return 'FAIL: big file still built spans';
  srcTa.value = 'Chapter: Small\\n';
  hl.textContent = '';
  renderHighlight();
  if (hl.innerHTML.indexOf('k-head') < 0) return 'FAIL: small file lost its highlighting';

  // Arm 8: the compiler preset, driven through the page's own change handler,
  // lands the REAL source in the tree, equal to build/output/Codex.codex, and
  // takes the project to itself: the source carries its own opening, so a
  // shared unit would refuse on the duplicate entry point. The refusing
  // confirm is the control: the tree must come through untouched.
  await loadExamples();
  const exEl = document.getElementById('examples');
  if (!exEl.children.some(c => c.value === 'compiler'))
    return 'FAIL: no compiler option in the preset menu';
  const before = project.files.length;
  confirm = () => false;
  exEl.value = 'compiler';
  await exEl.handlers['change']();
  if (project.files.length !== before || project.files.some(f => f.path === 'Codex.codex'))
    return 'FAIL: a refused confirm still changed the project';
  confirm = () => true;
  exEl.value = 'compiler';
  await exEl.handlers['change']();
  if (project.files.length !== 1 || project.files[0].path !== 'Codex.codex')
    return 'FAIL: the preset did not take the project to itself: ' + project.files.map(f => f.path).join(', ');
  const cf = project.files[0];
  if (cf.text !== __compilerSrcText)
    return 'FAIL: preset text differs from the source on disk (' + cf.text.length + ' vs ' + __compilerSrcText.length + ' chars)';

  // Arm 9: the whole compiler source compiles through the actual module at
  // decks=125, the rung the big-unit ladder jumps to. This is the load-bearing
  // claim behind the preset and it is measured, not predicted (L-GREEN). It is
  // the slow arm and rides last.
  const u3 = assembleUnit();
  if (u3.text.length <= BIG_UNIT) return 'FAIL: the unit with the compiler aboard is not big (' + u3.text.length + ')';
  const r3 = runModule(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
                       'IR-UNI decks=125\\n' + u3.text);
  if (r3.text.indexOf('IR-BEGIN') < 0)
    return 'FAIL: the compiler source produced no IR; head: ' + r3.text.slice(0, 300);

  // Arm 10: the Binary tab builds the compliance evidence package, from the
  // CDX header itself (before this CL the toggle sat on that tab and never
  // fired: the binary path returned early). Driven through the page's own
  // Compile handler. The text-lens run after it is the CONTROL: it has no
  // CDX and must read binary.present=n, so the binary arm's y is the header
  // being read rather than a default.
  project.files = [{ path: 'evd.codex', text: 'Chapter: EvdArm\\n\\nSection: Main\\n\\n  opening : [Console] Nothing = act\\n   print-line-uni "evd"\\n  end\\n' }];
  openFile('evd.codex');
  tab = 'binary'; binTarget = 'pe';
  document.getElementById('eu-toggle').checked = true;
  await document.getElementById('go').handlers['click']();
  if (!emittedBin || emittedBin.name !== 'BOOTX64.EFI')
    return 'FAIL: binary arm produced no artifact; status: ' + document.getElementById('status').innerHTML;
  if (!euOut || !euOut.cdxe) return 'FAIL: binary build produced no evidence package';
  if (euOut.cdxe.indexOf('binary.present=y') < 0)
    return 'FAIL: evidence does not read the CDX; cdxe head: ' + euOut.cdxe.slice(0, 200);
  if (euOut.cdxe.indexOf('no CDX header given') >= 0)
    return 'FAIL: evidence still reports no CDX header on the binary path';
  tab = 'scripts'; lens = { plug: 'javascript' };
  await document.getElementById('go').handlers['click']();
  if (!euOut || !euOut.cdxe) return 'FAIL: text control produced no evidence';
  if (euOut.cdxe.indexOf('binary.present=n') < 0)
    return 'FAIL: text control also claims a CDX, so the binary arm proves nothing';

  // Arm 11: the CDX target hands over the compiled payload itself, named for
  // the active file, and it is a real CDX (the magic is the check the
  // evidence plug applies to the same bytes).
  tab = 'binary'; binTarget = 'cdx';
  await document.getElementById('go').handlers['click']();
  if (!emittedBin || emittedBin.name !== 'evd.cdx')
    return 'FAIL: cdx target named ' + (emittedBin && emittedBin.name);
  const cb = emittedBin.bytes;
  if (!(cb[0] === 0x43 && cb[1] === 0x44 && cb[2] === 0x58 && cb[3] === 0x31))
    return 'FAIL: cdx target does not start CDX1';

  // Arm 12: DISK mode end to end on the LINEAR-MEMORY disk (the Device.Block
  // grounding this CL adds). disk_reserve is called before _start, the image
  // bytes go in, the volume mounts off block-read-sector, SOURCE.SRC compiles,
  // and the CDX payload is byte-identical to a stdin compile of the same
  // source. The CONTROL is the same DISK input with no disk reserved: it must
  // answer no CDX, proving the disk is load-bearing rather than decorative.
  // The image is a build artifact, laid down from the repo test asset:
  //   build/build-img.ps1 -PeInput build/boot/blockladder.efi
  //     -Out codex/plugs/wasm/build-output/page/disk-arm.img
  //     -Source codex/plugs/wasm/disk-arm-src.codex
  const diskImg = __fs.readFileSync(${JSON.stringify(path.join(repo, 'codex\\plugs\\wasm\\build-output\\page\\disk-arm.img'))});
  const diskSrc = __fs.readFileSync(${JSON.stringify(path.join(repo, 'codex\\plugs\\wasm\\disk-arm-src.codex'))}, 'utf8');
  function runWithDisk(modBytes, inputStr, img) {
    const input = new TextEncoder().encode(inputStr + '\\0');
    let mem = null, pos = 0, total = 0;
    const chunks = [];
    const imports = { wasi_snapshot_preview1: {
      fd_write(fd, iovs, n, outp) {
        const v = new DataView(mem.buffer); let t = 0;
        for (let i = 0; i < n; i++) {
          const p = v.getUint32(iovs + i * 8, true), l = v.getUint32(iovs + i * 8 + 4, true);
          chunks.push(new Uint8Array(mem.buffer.slice(p, p + l))); total += l; t += l;
        }
        v.setUint32(outp, t, true); return 0;
      },
      fd_read(fd, iovs, n, outp) {
        const v = new DataView(mem.buffer); let t = 0;
        for (let i = 0; i < n; i++) {
          const p = v.getUint32(iovs + i * 8, true), l = v.getUint32(iovs + i * 8 + 4, true);
          const dst = new Uint8Array(mem.buffer, p, l);
          let k = 0;
          while (k < l && pos < input.length) { dst[k++] = input[pos++]; }
          t += k; if (k < l) break;
        }
        v.setUint32(outp, t, true); return 0;
      }
    }};
    const inst = new WebAssembly.Instance(new WebAssembly.Module(modBytes), imports);
    mem = inst.exports.memory;
    let base = 0;
    if (img) {
      base = inst.exports.disk_reserve(img.length);
      new Uint8Array(mem.buffer).set(img, base);
    }
    inst.exports._start();
    const all = new Uint8Array(total);
    let off = 0; for (const c of chunks) { all.set(c, off); off += c.length; }
    const imgAfter = img ? new Uint8Array(mem.buffer.slice(base, base + img.length)) : null;
    return { bytes: all, img: imgAfter };
  }
  // A minimal FAT16 reader over the arm's own image: the independent referee
  // that digs OUT.CDX back out of the mutated bytes. Test-side only; the
  // product never parses FAT in JS.
  function fat16Extract(img, name83) {
    const v = new DataView(img.buffer, img.byteOffset, img.byteLength);
    const part = 2048 * 512;
    const bps = v.getUint16(part + 11, true), spc = img[part + 13];
    const reserved = v.getUint16(part + 14, true), nfats = img[part + 16];
    const rootEntries = v.getUint16(part + 17, true), fatSize = v.getUint16(part + 22, true);
    const fatStart = part + reserved * bps;
    const rootStart = fatStart + nfats * fatSize * bps;
    const dataStart = rootStart + rootEntries * 32;
    for (let e = 0; e < rootEntries; e++) {
      const off = rootStart + e * 32;
      if (img[off] === 0) break;
      const nm = new TextDecoder('latin1').decode(img.subarray(off, off + 11));
      if (nm !== name83) continue;
      let cluster = v.getUint16(off + 26, true);
      const size = v.getUint32(off + 28, true);
      const out = new Uint8Array(size);
      let got = 0;
      while (cluster >= 2 && cluster < 0xFFF8 && got < size) {
        const cs = dataStart + (cluster - 2) * spc * bps;
        const n = Math.min(spc * bps, size - got);
        out.set(img.subarray(cs, cs + n), got); got += n;
        cluster = v.getUint16(fatStart + cluster * 2, true);
      }
      return got === size ? out : null;
    }
    return null;
  }
  const modBuf = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
  if (typeof (new WebAssembly.Instance(new WebAssembly.Module(modBuf), { wasi_snapshot_preview1: { fd_write() { return 0; }, fd_read() { return 0; } } }).exports.disk_reserve) !== 'function')
    return 'FAIL: the module exports no disk_reserve';
  const diskRun = runWithDisk(modBuf, 'DISK\\nSOURCE.SRC\\n', diskImg);
  const diskText = new TextDecoder().decode(diskRun.bytes);
  const verdict = diskText.match(/DISK-OUT: OK OUT\\.CDX (\\d+)/);
  if (!verdict) return 'FAIL: no green DISK-OUT verdict; tail: ' + diskText.slice(-300);
  const outCdx = fat16Extract(diskRun.img, 'OUT     CDX');
  if (!outCdx) return 'FAIL: OUT.CDX not found on the mutated image';
  const stdinOut = runModule(modBuf, 'CDX\\n' + diskSrc);
  const stdinCdx = cdxPayload(stdinOut.bytes);
  if (!stdinCdx) return 'FAIL: the stdin control compile produced no CDX';
  if (outCdx.length !== stdinCdx.length) return 'FAIL: disk and stdin CDX differ in size, ' + outCdx.length + ' vs ' + stdinCdx.length;
  for (let i = 0; i < outCdx.length; i++) if (outCdx[i] !== stdinCdx[i]) return 'FAIL: disk and stdin CDX differ at byte ' + i;
  // The control may TRAP rather than print: zeroed sectors mount a volume
  // whose bytes-per-sector is 0 and the fat16 arithmetic divides by it. A
  // trap answers the control's question the same way a refusal does -- the
  // disk's absence changed the outcome -- so both shapes pass, and only a
  // green verdict fails it. (The page never feeds DISK mode without a disk;
  // RESOLVE guards sector-count 0 before mounting, by design.)
  let noDiskText = '';
  try {
    noDiskText = new TextDecoder().decode(runWithDisk(modBuf, 'DISK\\nSOURCE.SRC\\n', null).bytes);
  } catch (e) {
    noDiskText = '(trapped: ' + String(e && e.message || e) + ')';
  }
  if (noDiskText.indexOf('DISK-OUT: OK') >= 0)
    return 'FAIL: DISK mode with NO disk still went green, so the disk proves nothing';

  return 'ALL ARMS OK; real diag mapped: ' + home.slice(0, 60) +
         '; self IR ' + r3.text.length.toLocaleString() + ' chars in ' + r3.ms.toFixed(0) + ' ms, ' + r3.mb + ' MB' +
         '; binary evidence claims the CDX, text control does not' +
         '; DISK round trip in linear memory: OUT.CDX off the mutated image byte-identical to stdin (' + outCdx.length + ' bytes), no-disk control refused';
})()
`;
vm.runInContext(drive, sandbox, { filename: 'drive.js' }).then(
  (res) => { console.log(res); process.exit(res.startsWith('ALL ARMS OK') ? 0 : 1); },
  (err) => { console.log('FAIL: ' + (err && err.stack || err)); process.exit(1); }
);
