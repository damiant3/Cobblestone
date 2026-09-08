// Assemble the gpushow demo pages from one skeleton plus per-page data.
//
// Why this exists. The 39 demo pages were 39 hand-written files that share a
// head, a style block, a nav and a tail, and diverge in the middle. Measured
// 2026-09-07: 4,043 lines across them, 56% of which is the SAME 53 lines
// repeated. Anything that has to change in the shared half changes in 39
// places, which is the shape the live-compile work would otherwise take.
//
// What this is NOT. It does not generate the demos. The body of each page is
// its own WebGPU code and is kept VERBATIM in page-data.json; the skeleton is
// what is shared. So this factors the repeated half and leaves the authored
// half alone, rather than pretending 39 different demos are one template.
//
// The structure was measured, not assumed:
//   1..2        fixed
//   3           <title>            per page
//   4..16       <style>            two lines per page (5 pages differ)
//   17          <nav>              per page
//   18..22      fixed
//   23..N-7     the demo           per page, verbatim
//   N-6..N      fixed tail
//
// Usage:
//   node build-pages.mjs --extract   rebuild page-data.json from the pages
//   node build-pages.mjs             write the pages from page-data.json
//   node build-pages.mjs --check     regenerate and compare BYTES, write nothing
//
// --check is the equivalence arm and is the same code path as the build, so
// the arm cannot drift from the generator it grades.

import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const webDir = join(here, '..', 'web');
const dataFile = join(here, 'page-data.json');

const EOL = '\r\n';
const HEAD_FIXED_END = 22;   // last line of the fixed run that starts at 18
const TAIL_FIXED = 6;        // trailing lines identical in every page

const pageFiles = () =>
  readdirSync(webDir).filter(f => f.endsWith('.html') && f !== 'index.html').sort();

// Splits on EITHER line ending and the render always joins with CRLF, so what
// this writes is CRLF whatever the inputs are. Splitting on CRLF alone is not
// equivalent: live-block.html is LF, so it came back as ONE line carrying
// embedded LFs, and every generated page ended up with 201 bare LF inside a
// CRLF file. That is P-EOL, and it makes a whole file read as changed.
//
// Every input also ends with a newline, so splitting leaves a trailing empty
// element. Carried into the tail it becomes a second terminator, which is
// exactly +2 bytes per page and put byte-equivalence at 0 of 39 on the first
// run of this script.
function readLines(path) {
  const lines = readFileSync(path, 'utf8').split(/\r?\n/);
  if (lines.length && lines[lines.length - 1] === '') lines.pop();
  return lines;
}

function splitPage(lines) {
  return {
    title: lines[2],
    style: lines.slice(3, 16),
    nav: lines[16],
    body: lines.slice(HEAD_FIXED_END, lines.length - TAIL_FIXED),
  };
}

function extract() {
  const files = pageFiles();
  const ref = readLines(join(webDir, files[0]));
  const skeleton = {
    head: ref.slice(0, 2),
    styleShell: ref.slice(3, 16),
    headFixed: ref.slice(17, HEAD_FIXED_END),
    tail: ref.slice(ref.length - TAIL_FIXED),
  };
  const pages = {};
  for (const f of files) {
    const lines = readLines(join(webDir, f));
    const p = splitPage(lines);
    // The style block is shared except where a demo sets its own ground and
    // canvas. Store only the lines that differ from the shell, by index, so a
    // change to the shared style is one edit and the five exceptions survive.
    const styleOverrides = {};
    p.style.forEach((l, i) => { if (l !== skeleton.styleShell[i]) styleOverrides[i] = l; });
    // The live block needs the kernel it compiles and the entry point it must
    // find in the lowered WGSL. Both are read off the page rather than kept by
    // hand. FIVE pages fetch two kernels (bloom, deferred, radial, shadowmap,
    // ssao); the first is taken and the count recorded, so which kernel a page
    // compiles live is visible in the data rather than implied by ordering.
    const text = p.body.join('\n');
    const kernels = [...new Set([...text.matchAll(/kernels\/([A-Za-z0-9_]+)\.wgsl/g)].map(m => m[1]))];
    if (!kernels.length) throw new Error(`${f}: no kernel fetch found`);
    // The entry point comes from the SHIPPED .wgsl, which is the artifact the
    // block compares against, not from the page's `entryPoint:` strings. Those
    // are the render pipeline's `vs`/`fs` on six pages, and fireworks names a
    // compute entry that no naming rule predicts (fw_burst_spark_main). Every
    // one of the 42 kernels declares exactly one leading `fn *_main`.
    const wgsl = readFileSync(join(here, '..', 'kernels', kernels[0] + '.wgsl'), 'utf8');
    const em = /fn\s+([A-Za-z0-9_]*_main)\b/.exec(wgsl);
    if (!em) throw new Error(`${f}: no entry point in ${kernels[0]}.wgsl`);
    pages[f] = {
      title: p.title, nav: p.nav, styleOverrides,
      live: { kernel: kernels[0], entry: em[1], kernelsOnPage: kernels.length },
      body: p.body,
    };
  }
  writeFileSync(dataFile, JSON.stringify({ skeleton, pages }, null, 1) + '\n');
  const overriding = Object.values(pages).filter(p => Object.keys(p.styleOverrides).length).length;
  console.log(`extracted ${files.length} pages, ${overriding} with a style override`);
}

// The live-compile block goes in ONCE, here, and lands on all 39. It is kept
// in live-block.html rather than inline so it can be read and reviewed as the
// HTML it is. It follows the demo's own </script>, because it carries its own
// module script and cannot sit inside the demo's.
const liveBlock = (page) => {
  if (!page.live) return [];
  return readLines(join(here, 'live-block.html'))
    .map(l => l.replace(/\{\{KERNEL\}\}/g, page.live.kernel).replace(/\{\{ENTRY\}\}/g, page.live.entry));
};

function render(skeleton, page) {
  const style = skeleton.styleShell.slice();
  for (const [i, l] of Object.entries(page.styleOverrides)) style[Number(i)] = l;
  return [
    ...skeleton.head,
    page.title,
    ...style,
    page.nav,
    ...skeleton.headFixed,
    ...page.body,
    ...skeleton.tail,
    ...liveBlock(page),
  ].join(EOL) + EOL;
}

function build(checkOnly) {
  const { skeleton, pages } = JSON.parse(readFileSync(dataFile, 'utf8'));
  const files = pageFiles();
  const known = Object.keys(pages);
  const missing = files.filter(f => !known.includes(f));
  const extra = known.filter(f => !files.includes(f));
  let same = 0;
  const differ = [];
  for (const f of files) {
    if (!pages[f]) continue;
    const out = render(skeleton, pages[f]);
    if (checkOnly) {
      const cur = readFileSync(join(webDir, f), 'utf8');
      if (out === cur) same++;
      else {
        const a = cur.split(/\r?\n/), b = out.split(/\r?\n/);
        let at = -1;
        for (let i = 0; i < Math.max(a.length, b.length); i++) if (a[i] !== b[i]) { at = i + 1; break; }
        differ.push(`${f} (first difference at line ${at}, ${cur.length} bytes shipped vs ${out.length} generated)`);
      }
    } else {
      writeFileSync(join(webDir, f), out);
      same++;
    }
  }
  if (missing.length) console.log(`NOT IN page-data.json: ${missing.join(', ')}`);
  if (extra.length) console.log(`in page-data.json with no page: ${extra.join(', ')}`);
  if (checkOnly) {
    for (const d of differ) console.log(`  DIFFERS  ${d}`);
    console.log(`\nbyte-equivalence: ${same} of ${files.length} pages regenerate identically`);
    if (differ.length || missing.length || extra.length) process.exit(1);
  } else {
    console.log(`wrote ${same} pages`);
  }
}

// The live block fetches the library chapters a kernel CITES, so they have to
// be reachable over HTTP beside the kernels. They live in codex/foreword/gpu
// and are staged here rather than tracked twice: tools/serve.mjs serves
// apps/gpushow as its root and apps/landing/build.ps1 copies kernels/ into the
// site, so staging into kernels/ is the one place that serves both. The set is
// READ from the kernels' own cites; a list here would drift the first time a
// kernel gained a chapter, and it would drift silently.
function stageChapters() {
  const kernelDir = join(here, '..', 'kernels');
  const gpuDir = join(here, '..', '..', '..', 'codex', 'foreword', 'gpu');
  const cited = new Set();
  for (const f of readdirSync(kernelDir).filter(f => f.endsWith('.codex'))) {
    const t = readFileSync(join(kernelDir, f), 'utf8');
    for (const m of t.matchAll(/^\s*cites\s+Gpu\s+chapter\s+([A-Za-z0-9_]+)\s*$/gm)) cited.add(m[1]);
  }
  const staged = [];
  for (const name of [...cited].sort()) {
    const from = join(gpuDir, name + '.codex');
    if (!existsSync(from)) throw new Error(`cited chapter has no source: ${from}`);
    writeFileSync(join(kernelDir, name + '.codex'), readFileSync(from));
    staged.push(name);
  }
  return staged;
}

const arg = process.argv[2] || '';
if (arg === '--extract') extract();
else {
  if (arg !== '--check') {
    const staged = stageChapters();
    console.log(`staged ${staged.length} cited chapter(s): ${staged.join(', ')}`);
  }
  build(arg === '--check');
}
