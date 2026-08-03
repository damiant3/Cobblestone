# TCO Reset Must Compact

Status: **unlanded plan** (2026-04-24). Written ahead of implementation so the
investigation survives a reboot.

## The bug

`emit-reset-block` in `Codex.Codex/Emit/X86_64Compound.codex:263-269` emits a
tail-call heap reclamation that rewinds `r10` to `heap-mark-local` (the value
of `r10` at function prologue, stored per-function at
`Codex.Codex/Emit/X86_64.codex:183`).

The reset is gated by `emit-list-checks` (X86_64Compound.codex:229-256). Three
per-live-list runtime checks; all must pass for reset to fire:

1. `temp_ptr == param_ptr` -- pointer unchanged since tail-arg eval.
2. `new_ptr < heap-mark` -- list was allocated before the function entered.
3. `*(new_ptr) == snapshot_length` -- length word unchanged since the
   `snapshot-list-counts` taken at the top of the current tail-call.

**All three can pass while `r10`'s rewind target is inside a live list's
payload.** The check for "still safe to reclaim" is not the check emitted.

## Concrete failure in BS3 stage1.elf (the acceptance-blocker)

`sort-bindings-loop` is an accumulator loop over `acc : List TypeBinding`.
`heap-mark` captures `r10` at `sort-bindings-loop` entry. At that moment `acc`
is `[]` with cap=0; `heap-mark = acc + 8` (just past the empty list header).

Iterations 1..1025: a mix of path-1 in-place and path-2 grow-in-place inserts.
Path-2 doublings bring `cap` from 0 → 2048 and correctly advance `r10` to
`0xd680cf0` (end of the 16 KB payload). Each insert changes the length word,
so check 3 fails and no reset fires. `r10` tracks correctly.

Iterations 1025..1705: all path-1 (in-place) inserts, length grows 1025 → 1705.
Check 3 still fails every iteration. `r10` stays at `0xd680cf0`.

**Iteration 1706: the first dedup.** Dedup returns `acc` unchanged:

- Check 1: `new == old` ✓
- Check 2: `acc < heap-mark` ✓ (acc is 8 bytes below mark)
- Check 3: `*(acc) == snapshot` ✓ (length word wasn't touched this iteration)

Reset fires. `r10 ← heap-mark = 0xd67ccf0`, which is *inside* acc's 16 KB
payload (specifically, at slot 0). `acc`'s pointer and cap word are untouched,
but the heap tip is now pinned there.

Iterations 1706..4588: every dedup reinforces the wrong `r10`. Path-1 inserts
continue to work (they only update the length word and memcpy within the
payload). When length finally reaches cap at i=4588, the next insert needs to
grow. Path 2 fails (`acc + (cap+1)*8 ≠ r10`). Path 3 runs.

Path-3 alloc writes:
- `*(r10) = new_cap` → stores `0x1000` (= 4096) into acc's slot 0.
- `r10 += 8` → r10 now points at acc's slot 1.
- `rax = r10`; `*(rax) = new_len` → stores `0x801` (= 2049) into acc's slot 1.

The path-3 pre-copy loop then reads old slots 0..pos-1 and writes them to new
slots 0..pos-1. Because **old slot N and new slot N-2 alias at the same
physical address** (new list starts 16 bytes past old list), the alternating
`0x1000 / 0x801` pattern cascades forward through the entire copy:

```
old[0] = 0x1000 (just clobbered)   new[0] := 0x1000
old[1] = 0x801  (just clobbered)   new[1] := 0x801
old[2] = new[0] = 0x1000           new[2] := 0x1000
old[3] = new[1] = 0x801            new[3] := 0x801
...
```

Result: 1680 of 4828 slots in the final sorted binding list are `tb=0x1000 /
tb=0x801` alternating garbage, clustered at the front (the slots that were
copied most times through successive path-3 reallocs). `lookup-type-bsearch`
fails on 598 names.

Evidence artifacts (raw pointers only, safe to read):
- `build-output/bare-metal/acc-moves-dump.txt` -- acc/cap/len/r10 per
  iteration; shows the r10 step from `0xd680cf0` to `0xd67ccf0` at i=1706.
- `build-output/bare-metal/first-garbage-dump.txt` -- first iteration where
  corruption appears in the list (i=4589).
- `build-output/bare-metal/probe-*.sh` -- the probe scripts.

## Why the check is unsound

Check 3's snapshot is taken at the current tail-call's setup -- **not** at
function entry. Growth from prior iterations is invisible. The check answers
"did acc grow during THIS iteration?" but the question that matters for
soundness is "does acc's current payload fit within `[list_base, heap-mark)`?"

Check 2 (`acc < heap-mark`) is not sufficient either -- it constrains only the
header, not the end of the payload. Path-2 grow-in-place extends the payload
past heap-mark while leaving the pointer below it.

There is no combination of per-iteration local checks that correctly detects
"acc has cumulatively grown past heap-mark since function entry." Any sound
fix has to either disable the optimization whenever a live list could have
grown, or actively move the live data before reclaiming.

## The plan: compact live lists, then reset

At tail-call reset, instead of `mov r10, heap-mark`:

1. For each tracked live param (xs, acc, …), compute its payload size
   (`(cap+1) * 8`, or `(len+1) * 8` if we decide to compact to used size).
2. Copy live payloads contiguously starting at `heap-mark`.
3. Patch each param's pointer to the new location.
4. Set `r10 = heap-mark + sum(compacted sizes)`.

The result is the post-reset invariant the current code assumes but doesn't
enforce: every live list's payload lies within `[heap-mark, r10)`, no
aliasing with reclaimable space.

### Cost

- One memcpy per live param per iteration. For sort-bindings-loop's acc that's
  ~16 KB copied per reset. Across ~3000 dedup resets in this one call that's
  ~48 MB of memory traffic -- non-trivial but still bandwidth, not latency.
- A bounds check on each live param (payload size) before emitting the copy.
- Code size: the reset block grows; each tracked param adds a memcpy loop.

### Cost opt-outs

- Skip compaction entirely for a param when its pointer ≥ heap-mark (it was
  allocated inside the iteration and will be reclaimed anyway; no live data
  below mark to save). This is the common case for non-accumulator recursion.
- Skip compaction when the payload already ends at `<= heap-mark` -- no growth
  happened, current reset is sound.
- For same-pointer lists (the accumulator case), compact to `len`, not `cap`;
  capacity is recoverable on next grow.

Both short-circuits are cheap runtime checks. The full memcpy only runs when
the list has actually grown past mark and is still live, which is rare outside
of accumulator loops but exactly the case where the current code is wrong.

### Alternative we're rejecting

"Disable reset when any tracked param's pointer is below mark AND its cap
might have grown." This would be correct but would disable the optimization
for *every* accumulator pattern -- defeating the reason the mechanism exists.
Compaction preserves the optimization for the pattern it was built for.

## Open design questions

- **What's `heap-mark` once we compact?** If we compact into `[mark,
  mark+size)`, we must advance `mark` itself on subsequent resets to avoid
  re-copying the same stable data repeatedly. Or: update mark to the new
  `r10` after compaction. Needs a clear invariant.
- **Cap vs len compaction.** Compacting to `len` loses headroom; next insert
  immediately triggers path-2/3. Probably worth measuring both under
  pingpong's heap hwm.
- **Multiple live params.** Order matters -- copy in address order to avoid
  overwriting sources. Need a pass to sort live param pointers before
  emitting the copies.
- **Instrumentation.** Before landing, add a `#[debug]`-gated counter of
  "resets that required compaction" vs "resets that short-circuited" so we
  can see the ratio on real programs. If compaction is rare, the overhead is
  near zero.

## Rollout

1. Land the unsound-reset repro as a failing selfhost bare-metal test (BS3
   green bar exercises this; no new sample needed if we keep BS3 as the
   gate).
2. Implement the two short-circuits (skip when safe) without compaction.
   Re-run: the BS3 bug disappears *if and only if* resets that formerly fired
   on accumulators now get short-circuited -- verify via instrumentation.
   This is a correctness-only CL.
3. Add the compaction path for resets that don't short-circuit. Verify
   pingpong heap hwm doesn't regress; measure BS3 memory traffic.
4. Sweep the rest of the compiler for accumulator-style TCO patterns that
   might have been silently wrong (lookup-type-loop, sort-text-list,
   fold-list, etc.). The bug existed for all of them; BS3 just happened to
   exercise it with a list big enough and a dedup pattern dense enough to
   manifest.

## Why this matters beyond BS3

Every accumulator loop in the selfhost is potentially affected. The only
reason BS2 (pingpong) is green is that its accumulator usage doesn't create
the exact "grow past mark, then dedup" sequence. Any future change to a
selfhost loop could trip this. The fix is a correctness baseline, not just a
BS3 unblocker.
