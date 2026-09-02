// Stage-1 arm: load the SHIPPED page's script (the artifact, not a copy) in a
// stub DOM, then drive the workspace machinery and one real compile.
const fs = require('fs');
const vm = require('vm');
const path = require('path');

// The repo is derived from where this file SITS, not written down. A fleet
// tool with a fixed workspace path silently measures another agent's tree, or
// as here refuses to run at all outside the one it was written in (L-SHARED):
// this arm named Cobblestone-red and could not be run by anyone else.
const repo = path.resolve(__dirname, '..', '..', '..');
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
const absentModules = [];
const sandbox = {
  console, TextEncoder, TextDecoder, WebAssembly, performance, URL,
  setTimeout, clearTimeout,
  // Blob was a do-nothing stub, which was enough while nothing read one back.
  // libraryImage() pipes a real Blob through DecompressionStream, so the bed
  // hands over Node's own globals: a stub here would report the library dark
  // and the arm would pass by measuring the bed.
  Blob, Response, DecompressionStream, ReadableStream,
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
  fetch(u) {
    // A module the page asks for and the embed does not carry lands here. Say
    // WHICH one and where it comes from: the bare "no network in the arm" is
    // true and useless, and it is what a missing module looked like.
    const name = String(u).split('/').pop();
    if (absentModules.includes(name)) {
      return Promise.reject(new Error(
        'module ' + name + ' is not in build-output/page; run codex/plugs/wasm/build-page.ps1'));
    }
    return Promise.reject(new Error('no network in the arm (asked for ' + u + ')'));
  },
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
// A module an arm reaches and this list omits does NOT fall back to the embed:
// it reaches `fetch`, which the sandbox rejects, and the arm dies with "no
// network in the arm" instead of with a finding about the module.
//
// A module in the list but ABSENT from build-output used to throw here, before
// arm 1, so a workspace that had never run build-page.ps1 refused the whole
// suite with a readFileSync stack instead of a finding. Absence is recorded and
// reported by the arm that reaches for it.
for (const m of ['codex-compiler.wasm', 'pe-bytes.wasm', 'evidence-stdio.wasm', 'javascript-stdio.wasm', 'elf-bytes.wasm', 'riscv-stdio.wasm', 'arm64-stdio.wasm']) {
  const p = path.join(repo, 'codex\\plugs\\wasm\\build-output\\page', m);
  if (fs.existsSync(p)) sandbox.window.__EMBED[m] = fs.readFileSync(p).toString('base64');
  else absentModules.push(m);
}
if (absentModules.length) {
  console.log('NOTE: not in build-output/page, the arms that need them will say so: ' + absentModules.join(', '));
}
sandbox.__compilerSrcText = compilerSrc.toString('utf8');
// The DISK arm's image is a build asset this file does not lay down, and it is
// read from inside generated code. Absent, that surfaces as an ENOENT stack
// naming drive.js and a line number in a string, which says nothing about what
// to do; the command that fixes it was a comment two hundred lines away. The
// library arms above already refuse by name when their artifacts are missing,
// so this one does too rather than being the odd asset that throws.
const diskImgPath = path.join(repo, 'codex\\plugs\\wasm\\build-output\\page\\disk-arm.img');
if (!fs.existsSync(diskImgPath)) {
  console.log('REFUSE: the DISK arm needs ' + diskImgPath + ', which no build lays down. Build it with:');
  console.log('  build/build-img.ps1 -PeInput build/boot/blockladder.efi \\');
  console.log('    -Out codex/plugs/wasm/build-output/page/disk-arm.img \\');
  console.log('    -Source codex/plugs/wasm/disk-arm-src.codex');
  process.exit(2);
}
// The library rides the embed exactly as build-page.ps1 puts it there: the
// gzipped volume plus the names-only manifest. Both are build artifacts; if
// they are absent the library arms say so rather than passing quietly.
const libGzPath = path.join(repo, 'codex\\plugs\\wasm\\build-output\\page\\library.img.gz');
const libJsonPath = path.join(repo, 'codex\\plugs\\wasm\\build-output\\page\\library.json');
sandbox.__haveLibrary = fs.existsSync(libGzPath) && fs.existsSync(libJsonPath);
if (sandbox.__haveLibrary) {
  sandbox.window.__EMBED['library.img.gz'] = fs.readFileSync(libGzPath).toString('base64');
  sandbox.window.__LIBRARY = JSON.parse(fs.readFileSync(libJsonPath, 'utf8'));
}
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
  const bytes = __fs.readFileSync(${JSON.stringify(path.join(repo, 'codex\\plugs\\wasm\\build-output\\page\\codex-compiler.wasm'))});
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

  // Arm 12b: the ELF targets, driven through the PAGE'S OWN elfWire rather
  // than a second copy of the framing. page-bytes-test.ps1 builds that wire
  // again in PowerShell and grades the module; that proves the plug and says
  // nothing about the page, and two implementations of one contract is how
  // they drift apart. This one calls the shipped function.
  //
  // The two modes are told apart by class, machine and entry, not by "an ELF
  // came back": a builder wired to the wrong mode still answers a valid ELF,
  // so the magic number passes for both and proves nothing.
  const elfCdxRes = runModule(modBuf, 'CDX\\n' + diskSrc);
  const elfCdx = cdxPayload(elfCdxRes.bytes);
  let elfNote = '';
  if (!elfCdx) elfNote = 'FAIL: no CDX to build an ELF from';
  else {
    const elfMod = await moduleBytes('elf-bytes.wasm');
    const shapes = [];
    for (const mode of [0, 1]) {
      const w = elfWire(elfCdx, mode);
      if (w.err) { elfNote = 'FAIL: elfWire refused a good CDX: ' + w.err; break; }
      const out = runModule(elfMod, w.wire);
      const b = out.bytes;
      if (!b || b.length < 64) { elfNote = 'FAIL: mode ' + mode + ' answered ' + (b ? b.length : 0) + ' bytes'; break; }
      if (!(b[0] === 0x7F && b[1] === 0x45 && b[2] === 0x4C && b[3] === 0x46)) {
        elfNote = 'FAIL: mode ' + mode + ' did not answer an ELF: ' +
                  new TextDecoder().decode(b.subarray(0, 80)); break;
      }
      const dv = new DataView(b.buffer, b.byteOffset, b.byteLength);
      const cls = b[4], machine = dv.getUint16(18, true);
      const entry = mode === 0 ? dv.getUint32(24, true) : Number(dv.getBigUint64(24, true));
      const wantCls = mode === 0 ? 1 : 2;
      const wantMachine = mode === 0 ? 3 : 0x3E;
      const wantEntry = mode === 0 ? (1048576 + 32) : (4194304 + 176 + 32);
      if (cls !== wantCls || machine !== wantMachine || entry !== wantEntry) {
        elfNote = 'FAIL: mode ' + mode + ' answered class ' + cls + ' machine 0x' + machine.toString(16) +
                  ' entry 0x' + entry.toString(16) + ', wanted class ' + wantCls +
                  ' machine 0x' + wantMachine.toString(16) + ' entry 0x' + wantEntry.toString(16); break;
      }
      shapes.push((mode === 0 ? 'kernel ELF32' : 'usermode ELF64') + ' entry 0x' + entry.toString(16) +
                  ' (' + b.length.toLocaleString() + ' bytes)');
    }
    if (!elfNote) {
      // The control: a CDX whose header claims a section past the end of the
      // file must be REFUSED before anything is sliced, because that shape
      // otherwise builds clean and dies later (build/cdx-to-pe.ps1 records the
      // cost). Overstate the text length and require a refusal.
      const bad = elfCdx.slice();
      new DataView(bad.buffer, bad.byteOffset, bad.byteLength).setBigInt64(176, BigInt(bad.length * 4), true);
      const guard = elfWire(bad, 0);
      if (!guard.err) elfNote = 'FAIL: a CDX overstating its text section was not refused';
      else elfNote = shapes.join('; ') + '; overstated-section control refused';
    }
  }
  if (elfNote.startsWith('FAIL')) return elfNote;

  // Arm 12c: the board kernel. This target shares nothing with the ELF arms
  // above -- no CDX, no elf plug -- because the riscv backend writes its own
  // ELF from IR. Graded on class, MACHINE and load address, since an ELF that
  // came back claiming x86-64 is exactly the mislabelling the machine field
  // was missing to prevent.
  const rvMod = await moduleBytes('riscv-stdio.wasm');
  const rvIr = runModule(modBuf, 'IR-UNI decks=12\\n' + diskSrc);
  const rvLines = rvIr.text.split('\\n');
  const rb = rvLines.indexOf('IR-BEGIN'), re2 = rvLines.indexOf('IR-END');
  let boardNote = '';
  if (rb < 0 || re2 < 0) boardNote = 'FAIL: no IR to build a board kernel from';
  else {
    const irText = rvLines.slice(rb + 1, re2).join('\\n');
    const kOut = runModule(rvMod, 'ELF\\n' + irText);
    const kb = kOut.bytes;
    if (!kb || kb.length < 64) boardNote = 'FAIL: the board module answered ' + (kb ? kb.length : 0) + ' bytes';
    else if (!(kb[0] === 0x7F && kb[1] === 0x45 && kb[2] === 0x4C && kb[3] === 0x46)) {
      boardNote = 'FAIL: not an ELF: ' + new TextDecoder().decode(kb.subarray(0, 80));
    } else {
      const dv2 = new DataView(kb.buffer, kb.byteOffset, kb.byteLength);
      const cls2 = kb[4], mach2 = dv2.getUint16(18, true);
      const entry2 = Number(dv2.getBigUint64(24, true));
      // The entry is checked for ALIGNMENT and for landing inside the text
      // segment, not merely for being above the load address. That weaker
      // check passed a kernel whose entry was 0x80002D83 -- an ODD address,
      // which no RISC-V core will fetch -- because rv-record-func stores an
      // instruction INDEX and it was being used as a byte offset. From
      // outside, a bad entry and a working kernel differ only in that one
      // of them prints.
      const phOff = Number(dv2.getBigUint64(32, true));
      const textVaddr = Number(dv2.getBigUint64(phOff + 16, true));
      const textSize = Number(dv2.getBigUint64(phOff + 32, true));
      if (cls2 !== 2 || mach2 !== 243) {
        boardNote = 'FAIL: class ' + cls2 + ' machine ' + mach2 + ', wanted ELF64 EM_RISCV 243';
      } else if (entry2 % 4 !== 0) {
        boardNote = 'FAIL: entry 0x' + entry2.toString(16) + ' is not 4-byte aligned, so no RISC-V core can fetch it';
      } else if (entry2 < textVaddr || entry2 >= textVaddr + textSize) {
        boardNote = 'FAIL: entry 0x' + entry2.toString(16) + ' is outside the text segment 0x' +
                    textVaddr.toString(16) + '..0x' + (textVaddr + textSize).toString(16);
      } else if (textVaddr !== 0x80000000) {
        // QEMU with -bios none enters at the RAM base regardless of the ELF
        // entry, so the first instruction has to BE there.
        boardNote = 'FAIL: text maps at 0x' + textVaddr.toString(16) + ', not the 0x80000000 RAM base';
      } else {
        // The control: the DEFAULT mode must still answer a wire. Without it a
        // module that ignored the mode line and always built an ELF would pass
        // the arm above, and the wire every other consumer reads would be gone.
        const wb = runModule(rvMod, irText).bytes;
        if (wb.length > 4 && wb[0] === 0x7F && wb[1] === 0x45 && wb[2] === 0x4C && wb[3] === 0x46) {
          boardNote = 'FAIL: the default mode answered an ELF, so the mode line is selecting nothing';
        } else {
          boardNote = 'RISC-V ELF64 machine 243 entry 0x' + entry2.toString(16) +
                      ' (' + kb.length.toLocaleString() + ' bytes); wire control still a wire (' +
                      wb.length.toLocaleString() + ' bytes)';
          // Arm 12d: the same grading for arm64, off the SAME IR. The two
          // backends write their own ELFs and share no code, so a second
          // architecture is a second arm and not a parameter. EM_AARCH64 is 183
          // and the load address is the 0x40000000 RAM base
          // qemu-system-aarch64 -machine virt maps, not RISC-V's 0x80000000: an
          // arm64 kernel built at the RISC-V base is the same silent hang the
          // comment above describes, one architecture over.
          //
          // A failure here REPLACES boardNote rather than appending to it,
          // because the gate below tests startsWith('FAIL') and an appended
          // failure would read as a pass.
          const a64Mod = await moduleBytes('arm64-stdio.wasm');
          const aOut = runModule(a64Mod, 'ELF\\n' + irText);
          const ab = aOut.bytes;
          let a64Note = '';
          if (!ab || ab.length < 64) {
            a64Note = 'FAIL arm64: the board module answered ' + (ab ? ab.length : 0) + ' bytes';
          } else if (!(ab[0] === 0x7F && ab[1] === 0x45 && ab[2] === 0x4C && ab[3] === 0x46)) {
            a64Note = 'FAIL arm64: not an ELF: ' + new TextDecoder().decode(ab.subarray(0, 80));
          } else {
            const dv3 = new DataView(ab.buffer, ab.byteOffset, ab.byteLength);
            const cls3 = ab[4], mach3 = dv3.getUint16(18, true);
            const entry3 = Number(dv3.getBigUint64(24, true));
            const phOff3 = Number(dv3.getBigUint64(32, true));
            const textVaddr3 = Number(dv3.getBigUint64(phOff3 + 16, true));
            const textSize3 = Number(dv3.getBigUint64(phOff3 + 32, true));
            if (cls3 !== 2 || mach3 !== 183) {
              a64Note = 'FAIL arm64: class ' + cls3 + ' machine ' + mach3 + ', wanted ELF64 EM_AARCH64 183';
            } else if (entry3 % 4 !== 0) {
              a64Note = 'FAIL arm64: entry 0x' + entry3.toString(16) + ' is not 4-byte aligned, so no AArch64 core can fetch it';
            } else if (entry3 < textVaddr3 || entry3 >= textVaddr3 + textSize3) {
              a64Note = 'FAIL arm64: entry 0x' + entry3.toString(16) + ' is outside the text segment 0x' +
                        textVaddr3.toString(16) + '..0x' + (textVaddr3 + textSize3).toString(16);
            } else if (textVaddr3 !== 0x40000000) {
              a64Note = 'FAIL arm64: text maps at 0x' + textVaddr3.toString(16) + ', not the 0x40000000 RAM base';
            } else {
              const wb64 = runModule(a64Mod, irText).bytes;
              if (wb64.length > 4 && wb64[0] === 0x7F && wb64[1] === 0x45 && wb64[2] === 0x4C && wb64[3] === 0x46) {
                a64Note = 'FAIL arm64: the default mode answered an ELF, so the mode line is selecting nothing';
              } else {
                a64Note = 'AArch64 ELF64 machine 183 entry 0x' + entry3.toString(16) +
                          ' (' + ab.length.toLocaleString() + ' bytes); wire control still a wire (' +
                          wb64.length.toLocaleString() + ' bytes)';
              }
            }
          }
          boardNote = a64Note.startsWith('FAIL') ? a64Note : (boardNote + '; ' + a64Note);
        }
      }
    }
  }
  if (boardNote.startsWith('FAIL')) return boardNote;

  // Arms 13-17: the library on board. The volume is the whole shipped tree,
  // gzipped into the embed; RESOLVE mounts it in linear memory and hands back
  // prefix+source. Every arm here has a control that REMOVES the library,
  // because a compile that would have succeeded anyway proves nothing about a
  // resolver.
  let libNote = 'library arms SKIPPED (no built library.img.gz)';
  if (__haveLibrary) {
    // 13. The volume decodes out of the embed.
    const img = await libraryImage();
    if (!img) return 'FAIL: the library volume did not load: ' + libraryWhy;
    if (img.length < 1000000) return 'FAIL: the library volume is implausibly small, ' + img.length;

    // 14. A cite into the library resolves, and the SAME source without the
    // library refuses. The control is the load-bearing half: without it a
    // green compile says nothing about where Maybe came from.
    const userSrc = 'Chapter: UsesMaybe\\n\\n  cites Foreword chapter Maybe\\n\\nSection: Main\\n\\n' +
      '  pick : Maybe Integer -> Integer\\n  pick (m) = from-maybe m 0\\n';
    project.files = [{ path: 'uses.codex', text: userSrc }];
    const uRes = await resolveUnit(assembleUnit());
    if (!uRes.used) return 'FAIL: the library was not used: ' + (uRes.why || 'no reason given');
    if (uRes.missing.length) return 'FAIL: unexpected CITE-MISSING: ' + uRes.missing.join(', ');
    if (uRes.text.indexOf('Chapter: Foreword--Maybe') < 0)
      return 'FAIL: the resolved unit does not carry the renamed chapter; head: ' + uRes.text.slice(0, 200);
    if (!uRes.text.endsWith(assembleUnit().text))
      return 'FAIL: the resolved unit does not end with the source, so positions cannot be mapped';
    if (!(uRes.shift > 0)) return 'FAIL: a resolved unit shifted 0 lines';
    const rOk = runModule(modBuf, 'IR-UNI decks=12\\n' + uRes.text);
    if (rOk.text.indexOf('IR-BEGIN') < 0)
      return 'FAIL: the resolved unit did not compile; diag: ' +
             (rOk.text.split('\\n').find(l => l.indexOf('CDX') >= 0) || rOk.text.slice(0, 200));
    const rCtl = runModule(modBuf, 'IR-UNI decks=12\\n' + assembleUnit().text);
    if (rCtl.text.indexOf('CDX3007') < 0)
      return 'FAIL: the UNRESOLVED control compiled or refused for another reason, so the library proves nothing; diag: ' +
             (rCtl.text.split('\\n').find(l => l.indexOf('CDX') >= 0) || rCtl.text.slice(0, 200));

    // 15. The shift is what makes a diagnostic land in the user's file rather
    // than N lines into a prepended library chapter. Sabotage the user's own
    // source and require the mapped position to name uses.codex.
    project.files = [{ path: 'uses.codex', text: userSrc.replace('from-maybe m 0', 'no-such-fn m 0') }];
    const uBad = await resolveUnit(assembleUnit());
    lastRegions = uBad.regions;
    const badDiag = runModule(modBuf, 'IR-UNI decks=12\\n' + uBad.text).text
      .split('\\n').find(l => l.indexOf('CDX3002') >= 0);
    if (!badDiag) return 'FAIL: the sabotaged library user produced no CDX3002';
    const badHome = mapDiagLine(badDiag);
    if (badHome.indexOf('uses.codex:8:') !== 0)
      return 'FAIL: a diagnostic under a resolved prefix mapped to [' + badHome + '], wanted uses.codex:8:...';
    // The same diagnostic WITHOUT the shift applied must NOT land there, or
    // the arm above would pass whether the shift were right or absent.
    lastRegions = assembleUnit().regions;
    if (mapDiagLine(badDiag).indexOf('uses.codex:8:') === 0)
      return 'FAIL: the unshifted control mapped home too, so arm 15 cannot see the shift';

    // 16. A cite the volume does not carry is named by the resolver and still
    // refused by the compiler: CITE-MISSING says the medium was asked, CDX3007
    // says the name is not in the unit, and both are wanted.
    project.files = [{ path: 'ghost.codex', text: 'Chapter: Ghost\\n\\n  cites Foreword chapter NoSuchChapterHere\\n' }];
    const gRes = await resolveUnit(assembleUnit());
    if (gRes.missing.indexOf('Foreword NoSuchChapterHere') < 0)
      return 'FAIL: a missing chapter produced no CITE-MISSING; got [' + gRes.missing.join(', ') + ']';
    const gOut = runModule(modBuf, 'IR-UNI decks=12\\n' + gRes.text).text;
    if (gOut.indexOf('CDX3007') < 0)
      return 'FAIL: a missing cite did not answer CDX3007; diag: ' + (gOut.split('\\n').find(l => l.indexOf('CDX') >= 0) || gOut.slice(0, 200));

    // 17. The toolbox reads one chapter off the volume and hands it back under
    // its OWN header, which is what makes Add to project a real source file.
    await viewChapter('Foreword', 'Maybe');
    if (!libShown || !libShown.text) return 'FAIL: the toolbox read no text for Foreword/Maybe';
    if (libShown.text.indexOf('Chapter: Maybe') !== 0)
      return 'FAIL: the toolbox chapter does not open with its own header: ' + libShown.text.slice(0, 60);
    if (libShown.text.indexOf('Foreword--') >= 0)
      return 'FAIL: the toolbox chapter kept the resolver rename';
    if (libShown.text.indexOf('maybe-bind') < 0)
      return 'FAIL: the toolbox chapter looks truncated; it lacks maybe-bind';
    if (LIBRARY.quires.reduce((n, q) => n + q.chapters.length, 0) < 500)
      return 'FAIL: the manifest carries implausibly few chapters';
    libNote = 'library ' + (img.length / 1048576).toFixed(1) + ' MB volume, ' +
      LIBRARY.quires.length + ' quires / ' +
      LIBRARY.quires.reduce((n, q) => n + q.chapters.length, 0) + ' chapters; cite resolved (+' +
      uRes.shift + ' lines) where the no-library control answered CDX3007; toolbox read Maybe (' +
      libShown.text.length + ' chars)';
  }

  return 'ALL ARMS OK; real diag mapped: ' + home.slice(0, 60) +
         '; self IR ' + r3.text.length.toLocaleString() + ' chars in ' + r3.ms.toFixed(0) + ' ms, ' + r3.mb + ' MB' +
         '; binary evidence claims the CDX, text control does not' +
         '; DISK round trip in linear memory: OUT.CDX off the mutated image byte-identical to stdin (' + outCdx.length + ' bytes), no-disk control refused' +
         '; ELF ' + elfNote +
         '; board ' + boardNote +
         '; ' + libNote;
})()
`;
vm.runInContext(drive, sandbox, { filename: 'drive.js' }).then(
  (res) => { console.log(res); process.exit(res.startsWith('ALL ARMS OK') ? 0 : 1); },
  (err) => { console.log('FAIL: ' + (err && err.stack || err)); process.exit(1); }
);
