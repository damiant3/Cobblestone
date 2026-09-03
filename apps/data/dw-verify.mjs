// Grade the Codex DB wasm module against relational algebra, not against
// itself.
//
//   node apps/data/dw-verify.mjs [path/to/data.wasm]
//
// The plans this module exposes are a filter, a projection, a sort, a limit
// and a group-by with aggregates. Each row below asserts a property the
// ALGEBRA must have, so a re-recorded expectation cannot make a broken engine
// pass. Where possible the assertion is an INVARIANT against a relationship
// rather than a captured number: "sum divided by count equals the reported
// average" cannot rot the way "avg-salary is 129000" can, and it holds for
// every group without naming any of them.

import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const WASM = process.argv[2]
  ? resolve(process.argv[2])
  : join(HERE, '..', 'landing', 'web', 'data', 'data.wasm');

let mod;
try {
  mod = new WebAssembly.Module(await readFile(WASM));
} catch (e) {
  console.log(`REFUSE: no usable module at ${WASM} (${e.message})`);
  console.log('  build it with: pwsh apps/data/build-wasm.ps1');
  process.exit(2);
}

// WASI program: the plan name arrives on fd_read, the table leaves on
// fd_write, both UTF-8, _start runs once. A fresh instance per query is the
// contract, so every query sees a freshly seeded catalog.
function run(plan) {
  const input = new TextEncoder().encode(plan + '\n');
  let mem = null, pos = 0;
  const chunks = [];
  const imports = { wasi_snapshot_preview1: {
    fd_write(fd, iovs, n, outp) {
      const v = new DataView(mem.buffer); let t = 0;
      for (let i = 0; i < n; i++) {
        const p = v.getUint32(iovs + i * 8, true), l = v.getUint32(iovs + i * 8 + 4, true);
        chunks.push(new Uint8Array(mem.buffer.slice(p, p + l))); t += l;
      }
      v.setUint32(outp, t, true); return 0;
    },
    fd_read(fd, iovs, n, outp) {
      const v = new DataView(mem.buffer); let t = 0;
      for (let i = 0; i < n; i++) {
        const p = v.getUint32(iovs + i * 8, true), l = v.getUint32(iovs + i * 8 + 4, true);
        const dst = new Uint8Array(mem.buffer, p, l);
        let k = 0; while (k < l && pos < input.length) dst[k++] = input[pos++];
        t += k; if (k < l) break;
      }
      v.setUint32(outp, t, true); return 0;
    }
  }};
  const inst = new WebAssembly.Instance(mod, imports);
  mem = inst.exports.memory;
  inst.exports._start();
  let total = 0; for (const c of chunks) total += c.length;
  const all = new Uint8Array(total); let off = 0;
  for (const c of chunks) { all.set(c, off); off += c.length; }
  return new TextDecoder().decode(all).trim();
}

// "<n> rows" then one row per line, columns separated by " | ".
function table(plan) {
  const out = run(plan);
  const lines = out.split('\n').map(s => s.trim()).filter(Boolean);
  const m = /^(\d+) rows$/.exec(lines[0] || '');
  if (!m) return { count: null, rows: [], raw: out };
  return {
    count: Number(m[1]),
    rows: lines.slice(1).map(l => l.split('|').map(c => c.trim())),
    raw: out,
  };
}

const results = [];
function check(name, ok, detail) {
  results.push(ok);
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
}

// -- the scan and the sort -------------------------------------------------
const all = table('all');
check('all: the header count matches the rows delivered',
      all.count === all.rows.length, `header ${all.count}, rows ${all.rows.length}`);
const salaries = all.rows.map(r => Number(r[3]));
const sorted = salaries.every((v, i) => i === 0 || salaries[i - 1] >= v);
check('all: RelSort SortDesc leaves salary non-increasing', sorted, salaries.join(' '));

// -- the filter and the projection ----------------------------------------
const eng = table('engineering');
check('engineering: RelProject narrows to two columns',
      eng.rows.every(r => r.length === 2), `widths ${[...new Set(eng.rows.map(r => r.length))].join(',')}`);
check('engineering: RelFilter selects fewer rows than the scan',
      eng.count > 0 && eng.count < all.count, `${eng.count} of ${all.count}`);

// -- the limit -------------------------------------------------------------
const top = table('top');
check('top: RelLimit 3 delivers exactly three rows', top.count === 3, `${top.count}`);
// One property per row. This asserted the ORDERING but also carried a hidden
// `length === 3`, so the RelLimit sabotage reddened it with the detail reading
// "142000, max 142000" -- a FAIL line whose own evidence says PASS, because the
// clause that failed was not the one being printed. The count is the row
// above; this row is about which rows the limit keeps, and it is left green by
// a change to how MANY it keeps, which is correct.
check('top: the limit takes the HIGHEST rows, not the first scanned',
      top.rows.length > 0 && Number(top.rows[0][2]) === Math.max(...salaries),
      `top salary ${top.rows[0] && top.rows[0][2]}, max ${Math.max(...salaries)}`);

// -- the group-by and its aggregates ---------------------------------------
// department | headcount | total-salary | avg-salary
const dep = table('departments');
check('departments: RelGroup collapses the scan to one row per group',
      dep.count > 0 && dep.count < all.count, `${dep.count} groups from ${all.count} rows`);
const avgOk = dep.rows.every(r => Math.trunc(Number(r[2]) / Number(r[1])) === Number(r[3]));
check('departments: AggSum / AggCount == AggAvg for EVERY group', avgOk,
      dep.rows.map(r => `${r[0]} ${r[2]}/${r[1]}=${r[3]}`).join('  '));
const headcounts = dep.rows.reduce((a, r) => a + Number(r[1]), 0);
check('departments: the headcounts partition the table',
      headcounts === all.count, `${headcounts} vs ${all.count}`);
const grand = dep.rows.reduce((a, r) => a + Number(r[2]), 0);
const scanTotal = salaries.reduce((a, v) => a + v, 0);
check('departments: the group totals sum to the scan total',
      grand === scanTotal, `${grand} vs ${scanTotal}`);

// -- a predicate with a comparison other than equality ---------------------
const recent = table('recent');
check('recent: PredColCmp CmpGe admits only hire-year >= 2024',
      recent.rows.every(r => Number(r[4]) >= 2024),
      recent.rows.map(r => r[4]).join(' '));

// -- refusal ---------------------------------------------------------------
const bad = run('nonsense');
check('an unknown plan is refused and the known ones named',
      bad.startsWith('no such query') && bad.includes('departments'), bad.split('\n')[0]);

// -- CONTROL ---------------------------------------------------------------
// It must be able to go red. "all is not empty" would be true of a module that
// answered one constant forever; that the ANSWER TRACKS THE PLAN would not be.
check('CONTROL: the answer tracks the plan',
      all.raw !== dep.raw && eng.raw !== top.raw,
      `all/departments differ, engineering/top differ`);

// The paired sabotage belongs against the SOURCE, not here, because this
// grader loads a built module and cannot rebuild one. RUN 2026-09-02: with
// dw-top's `RelLimit sorted 3` changed to 5 and the module rebuilt, this
// corpus scores 12 of 13 against 13 of 13 for the real module, and the ONE row
// that moves is `RelLimit 3 delivers exactly three rows`. That is the shape to
// want: the sabotage reddens the row that names the property it broke and
// nothing else, so the corpus is discriminating rather than merely sensitive.
//
// It took two attempts to be worth reading. The first version of the ordering
// row also carried a hidden `length === 3`, so it went red too, printing
// "top salary 142000, max 142000" beside a FAIL -- evidence that says PASS,
// because the clause that failed was not the one printed. One property per
// row is what makes a red line diagnostic instead of merely alarming.

const failed = results.filter(r => !r).length;
console.log(`\n${results.length - failed} of ${results.length} checks passed.`);
process.exit(failed ? 1 : 0);
