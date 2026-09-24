// PRISM-13: the structural grade for an Apple Silicon executable, written
// before the writer it will grade. It reads the Mach-O header, the load
// commands and segments, and RECOMPUTES the ad-hoc code signature's SHA-256
// page hashes, which is what the kernel checks before it runs an arm64 binary.
// It proves structure and signature only; running on a Mac is the only proof
// that the program works.
//   node apps/prism/test-macho.mjs <file>   grade one file
//   node apps/prism/test-macho.mjs          self-test: a zig-linked, signed
//     aarch64-macos binary must pass, and a tampered and a truncated copy fail.
//   node apps/prism/test-macho.mjs --writer [out]   grade codex/plugs/macho on
//     a DARWIN wire it compiles itself; --wire <darwin.wire> [out] on one in hand
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Mach-O constants (mach-o/loader.h) and code-signing blob magics
// (cs_blobs.h). The signature is big-endian; everything else is little-endian.
const MH_MAGIC_64 = 0xfeedfacf, CPU_TYPE_ARM64 = 0x0100000c, MH_EXECUTE = 2, MH_PIE = 0x200000;
const LC = { SEGMENT_64: 0x19, SYMTAB: 0x2, DYSYMTAB: 0xb, LOAD_DYLIB: 0xc, LOAD_DYLINKER: 0xe, CODE_SIGNATURE: 0x1d, MAIN: 0x80000028, BUILD_VERSION: 0x32 };
const CSMAGIC_EMBEDDED_SIGNATURE = 0xfade0cc0, CSMAGIC_CODEDIRECTORY = 0xfade0c02, CS_ADHOC = 0x2, CS_HASHTYPE_SHA256 = 2;

export function gradeMacho(buf) {
  const errs = [];
  const need = (c, m) => { if (!c) errs.push(m); return c; };
  if (!need(buf.length >= 32, 'shorter than a Mach-O header')) return errs;
  const u32 = (o) => buf.readUInt32LE(o), u64 = (o) => Number(buf.readBigUInt64LE(o));
  need(u32(0) === MH_MAGIC_64, 'not a 64-bit Mach-O');
  need(u32(4) === CPU_TYPE_ARM64, 'cputype is not ARM64');
  need(u32(12) === MH_EXECUTE, 'filetype is not MH_EXECUTE');
  need((u32(24) & MH_PIE) !== 0, 'not position-independent (MH_PIE)');
  const ncmds = u32(16), sizeofcmds = u32(20);
  if (!need(32 + sizeofcmds <= buf.length, 'load commands run past the file')) return errs;
  const segs = [], seen = {}, cstr = (o, end) => { let e = o; while (e < end && buf[e] !== 0) e++; return buf.toString('latin1', o, e); };
  let off = 32, main = null, sig = null, text = null;
  for (let i = 0; i < ncmds; i++) {
    const cmd = u32(off), size = u32(off + 4);
    if (!need(size >= 8 && off + size <= 32 + sizeofcmds, `load command ${i} has a bad size`)) return errs;
    seen[cmd] = (seen[cmd] || 0) + 1;
    if (cmd === LC.SEGMENT_64) {
      const s = { name: cstr(off + 8, off + 24), vmaddr: u64(off + 24), vmsize: u64(off + 32), fileoff: u64(off + 40), filesize: u64(off + 48), nsects: u32(off + 64) };
      for (let k = 0; k < s.nsects; k++) {
        const so = off + 72 + k * 80;
        if (cstr(so, so + 16) === '__text') text = { addr: u64(so + 32), size: u64(so + 40), offset: u32(so + 48) };
      }
      segs.push(s);
    } else if (cmd === LC.LOAD_DYLINKER) need(cstr(off + u32(off + 8), off + size) === '/usr/lib/dyld', 'the dynamic linker is not /usr/lib/dyld');
    else if (cmd === LC.LOAD_DYLIB) seen.libSystem = seen.libSystem || cstr(off + u32(off + 8), off + size) === '/usr/lib/libSystem.B.dylib';
    else if (cmd === LC.MAIN) main = { entryoff: u64(off + 8) };
    else if (cmd === LC.BUILD_VERSION) need(u32(off + 8) === 1, 'the build platform is not macOS');
    else if (cmd === LC.CODE_SIGNATURE) sig = { dataoff: u32(off + 8), datasize: u32(off + 12) };
    off += size;
  }
  for (const [n, c] of Object.entries({ LC_MAIN: LC.MAIN, LC_LOAD_DYLINKER: LC.LOAD_DYLINKER, LC_BUILD_VERSION: LC.BUILD_VERSION, LC_SYMTAB: LC.SYMTAB, LC_DYSYMTAB: LC.DYSYMTAB, LC_CODE_SIGNATURE: LC.CODE_SIGNATURE }))
    need(seen[c] === 1, `${n} appears ${seen[c] || 0} times, not once`);
  need(seen.libSystem, 'no LC_LOAD_DYLIB of /usr/lib/libSystem.B.dylib');
  const seg = (n) => segs.find(s => s.name === n);
  const pz = seg('__PAGEZERO'), tx = seg('__TEXT'), le = seg('__LINKEDIT');
  need(pz && pz.vmaddr === 0 && pz.vmsize >= 0x100000000 && pz.filesize === 0, '__PAGEZERO is not a 4 GiB unmapped guard at 0');
  need(tx && tx.fileoff === 0, '__TEXT does not start the file');
  need(le, 'no __LINKEDIT segment');
  for (const s of segs) {
    need(s.fileoff + s.filesize <= buf.length, `${s.name} runs past the file`);
    if (s.vmsize) need(s.vmaddr % 0x4000 === 0, `${s.name} is not 16 KiB aligned`);
  }
  need(text && tx && text.addr >= tx.vmaddr && text.addr + text.size <= tx.vmaddr + tx.vmsize, 'no __text section inside __TEXT');
  need(main && text && main.entryoff >= text.offset && main.entryoff < text.offset + text.size, 'LC_MAIN does not point into __text');
  if (!sig) return errs;
  need(sig.dataoff + sig.datasize <= buf.length && le && sig.dataoff >= le.fileoff, 'the code signature is not inside __LINKEDIT');
  const b32 = (o) => buf.readUInt32BE(o);
  if (!need(b32(sig.dataoff) === CSMAGIC_EMBEDDED_SIGNATURE, 'the signature is not an embedded-signature SuperBlob')) return errs;
  let cd = -1;
  for (let k = 0; k < b32(sig.dataoff + 8); k++) {
    const bo = sig.dataoff + b32(sig.dataoff + 12 + k * 8 + 4);
    if (b32(bo) === CSMAGIC_CODEDIRECTORY && b32(sig.dataoff + 12 + k * 8) === 0) cd = bo;
  }
  if (!need(cd >= 0, 'no CodeDirectory in the signature')) return errs;
  const flags = b32(cd + 12), hashOffset = b32(cd + 16), nCodeSlots = b32(cd + 28), codeLimit = b32(cd + 32);
  const hashSize = buf[cd + 36], hashType = buf[cd + 37], pageSize = 1 << buf[cd + 39];
  need((flags & CS_ADHOC) !== 0, 'the signature is not ad-hoc');
  need(hashType === CS_HASHTYPE_SHA256 && hashSize === 32, 'the page hashes are not SHA-256');
  need(codeLimit === sig.dataoff, 'the code limit is not the start of the signature');
  need(nCodeSlots === Math.ceil(codeLimit / pageSize), 'the code slot count does not cover the code limit');
  let bad = 0;
  for (let p = 0; p < nCodeSlots; p++) {
    const page = buf.subarray(p * pageSize, Math.min((p + 1) * pageSize, codeLimit));
    const want = buf.subarray(cd + hashOffset + p * 32, cd + hashOffset + p * 32 + 32);
    if (!createHash('sha256').update(page).digest().equals(want)) bad++;
  }
  need(bad === 0, `${bad} of ${nCodeSlots} page hashes do not match`);
  return errs;
}

// The arm64 plug addresses its string table with ADR, relative to rodata placed
// at align-8 of the code length, and each entry is an 8-byte length then the
// bytes, 8-aligned. So every ADR from __text into __const must land on an
// 8-aligned entry whose length fits. A writer that drops the align-8 gap moves
// every target 4 bytes when the code length is 4 mod 8, and fails here.
export function gradeRodataRefs(buf) {
  const u32 = (o) => buf.readUInt32LE(o), u64 = (o) => Number(buf.readBigUInt64LE(o));
  const cstr = (o) => { let e = o; while (e < o + 16 && buf[e] !== 0) e++; return buf.toString('latin1', o, e); };
  let off = 32, text = null, cnst = null;
  for (let i = 0; i < u32(16); i++) {
    if (u32(off) === LC.SEGMENT_64) for (let k = 0; k < u32(off + 64); k++) {
      const so = off + 72 + k * 80, s = { addr: u64(so + 32), size: u64(so + 40), offset: u32(so + 48) };
      if (cstr(so) === '__text') text = s; else if (cstr(so) === '__const') cnst = s;
    }
    off += u32(off + 4);
  }
  if (!text) return { refs: 0, bad: ['no __text'] };
  const bad = []; let refs = 0;
  for (let p = 0; p + 4 <= text.size; p += 4) {
    const w = u32(text.offset + p);
    if ((w & 0x9f000000) !== 0x10000000) continue;
    let imm = (((w >>> 5) & 0x7ffff) << 2) | ((w >>> 29) & 3);
    if (imm & 0x100000) imm -= 0x200000;
    const target = text.addr + p + imm;
    if (!cnst || target < cnst.addr || target >= cnst.addr + cnst.size) continue;
    refs++;
    const rel = target - cnst.addr, len = u64(cnst.offset + rel);
    if (rel % 8 !== 0 || rel + 8 + len > cnst.size) bad.push(`ADR at __text+${p} lands at __const+${rel} (length word ${len})`);
  }
  return { refs, bad };
}

function runWriter(wasm, payload) {
  return execFileSync('wasmtime', ['-W', 'max-wasm-stack=16777216', wasm], { input: payload, maxBuffer: 64 << 20 });
}

// Two strings, then a growing prefix of EXTRAS. The ADR arm only bites on a
// code length of 4 mod 8, and the runtime's size moves with every codegen
// change, so the subjects are tried in order until one lands there. The
// extras differ in shape because statements of one shape tend to add the
// same multiple of 8 bytes each.
const EXTRAS = ['print-line-uni (show (6 * 7))', 'print-line-uni (show (6 - 7))', 'print-line-uni "third"',
  'print-line-uni (show (6 * 7 + 1))', 'print-line-uni (show (100 / 7))', 'print-line-uni (show (6 * 7 * 8 - 1))'];
const subject = (extra) => 'Chapter: MachoSubject\n  cites Foreword chapter Console\n\n We say:\n\nSection: Body\n\n  opening : [Console] Nothing = act\n    print-line-uni "first string"\n    print-line-uni "second string"\n' +
  EXTRAS.slice(0, extra + 1).map(s => `    ${s}\n`).join('') + '  end\n';

function darwinWire(repo) {
  const work = mkdtempSync(join(tmpdir(), 'prism-macho-wire-'));
  try {
    let wire = null;
    for (let extra = 0; extra < EXTRAS.length; extra++) {
      const src = join(work, `s${extra}.codex`), log = join(work, `s${extra}.log`);
      fs.writeFileSync(src, subject(extra));
      execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'build', 'compile.ps1'), '-Src', src, '-Out', join(work, `s${extra}.out`), '-Log', log,
        '-IrUni', '-Passes', 'text-plug', '-Kernel', join(repo, 'seed', 'Codex.cdx')], { stdio: 'ignore' });
      const text = fs.readFileSync(log, 'utf8'), a = text.indexOf('IR-BEGIN'), b = text.indexOf('IR-END');
      if (a < 0 || b < a) throw new Error(`the subject produced no IR-UNI; see ${log}`);
      const ir = 'DARWIN\n' + text.slice(a + 9, b).trim();
      wire = runWriter(join(repo, 'codex', 'plugs', 'arm64', 'build-output', 'arm64-stdio.wasm'), Buffer.from(ir, 'utf8'));
      if (wire.length >= 12 && wire.readUInt32LE(0) % 8 === 4) break;
    }
    return wire;
  } finally { rmSync(work, { recursive: true, force: true }); }
}

if (process.argv[1] && process.argv[1].endsWith('test-macho.mjs') && ['--wire', '--writer'].includes(process.argv[2])) {
  // node apps/prism/test-macho.mjs --writer [out]: compile a subject through the
  // seed and arm64-stdio.wasm to a DARWIN wire, then grade the writer on it.
  // node apps/prism/test-macho.mjs --wire <darwin.wire> [out]: the same arms on
  // a wire in hand. Both build through codex/plugs/macho's module and grade the
  // result and its refusals.
  const repo = fileURLToPath(new URL('../../', import.meta.url));
  const wasm = join(repo, 'codex', 'plugs', 'macho', 'build-output', 'macho-bytes.wasm');
  const own = process.argv[2] === '--writer';
  const wire = own ? darwinWire(repo) : fs.readFileSync(process.argv[3]);
  const outPath = own ? process.argv[3] : process.argv[4];
  const arms = [];
  const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
  const exe = runWriter(wasm, Buffer.concat([Buffer.from([0]), wire]));
  if (outPath) fs.writeFileSync(outPath, exe);
  const g = gradeMacho(exe);
  const codeLen = wire.readUInt32LE(0);
  ok('the writer\'s output is a signed arm64 Mach-O executable', g.length === 0, g.join('; ') || `${exe.length} bytes, code ${codeLen} bytes (${codeLen % 8 === 4 ? 'exercises' : 'does not exercise'} the align-8 gap)`);
  if (own) ok('the subject reaches the align-8 gap (code length 4 mod 8)', codeLen % 8 === 4, `code ${codeLen} bytes`);
  const r = gradeRodataRefs(exe);
  ok('every ADR into __const lands on a string entry', r.refs > 0 && r.bad.length === 0, r.bad.slice(0, 3).join('; ') || `${r.refs} references`);
  const again = runWriter(wasm, Buffer.concat([Buffer.from([0]), wire]));
  ok('the writer is deterministic', again.equals(exe));
  const t = Buffer.from(exe); t[t.length >> 1] ^= 1;
  ok('control: one flipped byte fails the grade', gradeMacho(t).length > 0);
  const m = runWriter(wasm, Buffer.concat([Buffer.from([9]), wire])).toString('latin1');
  ok('an unknown mode is refused by name', /^REFUSED unknown mode 9/.test(m), m.trim());
  const s = runWriter(wasm, Buffer.from([0, 1, 2])).toString('latin1');
  ok('a short payload is refused', /^REFUSED short payload/.test(s), s.trim());
  const cut = runWriter(wasm, Buffer.concat([Buffer.from([0]), wire.subarray(0, 12 + codeLen)])).toString('latin1');
  ok('a payload shorter than its header claims is refused', /^REFUSED payload/.test(cut), cut.trim());
  const failed = arms.filter(x => !x).length;
  console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
  process.exit(failed ? 1 : 0);
}

if (process.argv[1] && process.argv[1].endsWith('test-macho.mjs') && !['--wire', '--writer'].includes(process.argv[2])) {
  if (process.argv[2]) {
    const errs = gradeMacho(fs.readFileSync(process.argv[2]));
    console.log(errs.length ? errs.map(e => '  FAIL  ' + e).join('\n') : '  ok    a signed arm64 Mach-O executable');
    process.exit(errs.length ? 1 : 0);
  }
  const arms = [];
  const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
  const work = mkdtempSync(join(tmpdir(), 'prism-macho-'));
  try {
    fs.writeFileSync(join(work, 'm.c'), 'int main(void){return 0;}\n');
    execFileSync('zig', ['cc', '-target', 'aarch64-macos', '-o', join(work, 'm'), join(work, 'm.c')], { stdio: 'ignore' });
    const good = fs.readFileSync(join(work, 'm'));
    const g = gradeMacho(good);
    ok('the zig-linked, signed aarch64-macos binary passes', g.length === 0, g.join('; ') || `${good.length} bytes`);
    const tampered = Buffer.from(good);
    tampered[Math.floor(good.length / 3)] ^= 0x01;
    const t = gradeMacho(tampered);
    ok('control: one flipped byte fails the page hashes', t.some(e => /page hashes do not match/.test(e)), t.join('; '));
    const u = gradeMacho(good.subarray(0, good.length - 64));
    ok('control: a truncated file fails', u.length > 0, u[0]);
  } catch (e) {
    ok('ran to the end', false, String(e && e.message || e));
  } finally { rmSync(work, { recursive: true, force: true }); }
  const failed = arms.filter(x => !x).length;
  console.log(failed ? `${failed} arm(s) FAILED` : `all ${arms.length} arms passed`);
  process.exit(failed ? 1 : 0);
}
