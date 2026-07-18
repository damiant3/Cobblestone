# BigLog — Systematic Review of the Last 24 Hours

**Reviewer:** blu
**Date:** 2026-07-14
**Window:** CL 7468 → CL 7825 (all of 2026-07-13 through the review start)
**Main head at review:** CL 7825 (reek copy-up, FAT16 write path; depot seed of that CL)
**Method:** All 285 changelists in the window were enumerated and separated into
substantive work (~120) versus merge-down / copy-up / seed-rebuild plumbing
(~165). The substantive CLs were grouped into ten clusters and each cluster was
reviewed against the actual `p4 describe -du` diffs and the source at every
change site — inspection first, per Rulebook rule 8, with targeted `p4`/source
verification for every load-bearing claim. **The full test battery was NOT run**
(it is Damian's tool). This is a code-and-diff review, not a battery run.

---

## How to read this

Findings are ranked by severity, not by CL order. Each finding names the CL that
introduced it, the file, and the concrete failure scenario. "Disclosed" means the
authoring CL or a BACKLOG entry already names the gap — those are recorded for
completeness but are not new. The bar for a finding is a **failure scenario**, not
a style opinion.

Severity key:
- **CRITICAL** — a public safety/security claim that the code does not keep.
- **HIGH** — a correctness or data-integrity bug reachable in normal operation.
- **MEDIUM** — a real defect in an unreached path, a missing guard, or a latent hazard.
- **LOW** — disclosed limitations, edge cases, cosmetic/verification gaps.

---

## Severity summary

| # | Sev | Finding | CL | File |
|---|-----|---------|-----|------|
| 1 | CRITICAL | Trust gate reads its trust manifest (keys + scores) from the untrusted blob it is gating | 7677/7516 | `opening.codex`, `ImportGate.codex` |
| 2 | CRITICAL | DTLS "MITM defeated" has no peer-identity binding — impersonation within the trusted CA domain is unprevented | 7810 | `DtlsEndpoint.codex` |
| 3 | HIGH | FAT16 overwrite of an existing file writes a duplicate directory entry and leaks the old cluster chain | 7822 | `Fat16.codex` |
| 4 | HIGH | FAT16 full-disk mid-write leaks the partial cluster chain, no rollback | 7822 | `Fat16.codex` |
| 5 | HIGH | FAT16 multi-cluster write is O(n²) time AND permanent heap (free scan restarts at cluster 2 over a non-freeing sector allocator) | 7822 | `Fat16.codex` |
| 6 | HIGH | `process-spawn` claims a FREE slot with no atomic — a sixth scan-and-claim TOCTOU the 4.1 sweep missed | 7791/7780 | `X86_64ProcessHelpers.codex` |
| 7 | HIGH | `process-exit` publishes its slot FREE before switching off its own stack; closing BACKLOG 4.11 (preemption) reintroduces the two-cores-one-stack fault | 7791 | `X86_64ProcessHelpers.codex` |
| 8 | HIGH | Nine foreword definitions still shadow compiler builtins with no definition-site diagnostic — the Hamt-bug class is armed, including the lexer's per-char hot path | 7628/7638 | `CCE.codex`, `StringUtils.codex`, others |
| 9 | MEDIUM | Live two-list drift: `write-file`/`write-binary-file` remain in `sorted-builtin-names` with no emitter; false load-bearing prose points at Fat16 definitions that do not exist | 7698/7721 | `X86_64Builtins.codex` |
| 10 | MEDIUM | FileSystem is type-only: `cap-filesystem-read`/`write` are defined, granted, and read by nobody at runtime (inverse stage-4 shape), not filed like `cap-gpu-memory` | 7518 | `TypeEnv.codex` |
| 11 | MEDIUM | No guard verifies the checked-in `codex-vm.exe` matches `codex-vm.c`; it drifted and shipped stale once | 7670 | `tools/codex-vm.exe`, `build/*.ps1` |
| 12 | MEDIUM | The dropped-add guard is advisory only — not wired into `build.ps1`, and exits 0 even when it detects an untracked file | 7731/7740 | `build/p4-stale-check.ps1` |
| 13 | MEDIUM | `-Decks` below 100 accepted on the mode line and under-reserves → silent `#GP`, no CDX9002 | 7563 | `BuildSettings`/`Parser.codex` |
| 14 | MEDIUM | Mapping [3GB,4GB) present/RW removes the crash-early tripwire for a wild high data pointer | 7596 | `X86_64Boot.codex` |
| 15 | MEDIUM | FAT16 read path: cluster-chain walk has no cycle/fuel cap; `list-push` snoc in read loops → O(n²) large-file read | 7763 | `Fat16.codex` |
| 16 | MEDIUM | GHASH multiply branches on key bits (not constant-time); DTLS anti-replay window is not wired into the endpoint | 7659/7692 | `AesGcm.codex`, `DtlsEndpoint.codex` |
| 17 | MEDIUM | `Audio3D` carries a leftover fixed-point `1000.0` where `1.0` is unity — the migration sweep missed it (latent, cancels under normalize) | 7517 | `Audio3D.codex` |
| 18 | MEDIUM | The GUI test harness captures on host wall-clock with no quiescence/stability guard — flaky under load, cannot be a hard gate | 7478 | `build/test-gui.ps1` |
| 19 | LOW | Network scope *allow* path is dead: `fetch`/`post`/`resolve-dns` have no handler, die at CDX2040 (disclosed → BACKLOG 1.7) | 7773 | `TypeChecker.codex` |
| 20 | LOW | `-1` deny/absent conflation across identity/network gates (disclosed, fail-closed) | 7594/7773 | `X86_64IPCHelpers.codex` |
| 21 | LOW | CDX3004 checks pagination coherence, not chapter-name uniqueness (by design) | 7560 | `Parser.codex` |
| 22 | LOW | Timer-ISR sole-process path resurrects a max-ticks ZOMBIE to RUNNING (asymmetric with the switch path) | 7780 | `X86_64Boot.codex` |
| 23 | LOW | `RankedTextSet` rename is ungated in the csharp/javascript plugs (untracked CDX, outside the smoke subset) | 7802 | plug emitters |
| 24 | LOW | `__narrow (ann-sent + ver.sent)` has a static range (2³³) exceeding its `0..2³²−1` contract (safe in practice) | 7646 | `AnnotationTransport.codex` |

---

## What is solid (verified, not assumed)

These are load-bearing claims the review confirmed against code — recorded so the
green items are as visible as the red:

- **No new stage-4 regression** (gated-builtin-typed-pure) was introduced anywhere
  in the effect/capability cluster. That cluster is itself a disciplined hunt for
  the shape and closes it in the Identity, GPU, and Network families. `device-seed`
  was a genuine live instance (cap-gated while typed pure, hidden because the test
  could not tell denial from a bad index) — now fixed (7591/7594).
- **The SMP scheduler claims are all true.** Proc 0 is created RUNNING; all five
  documented scan-and-claim sites are LOCK CMPXCHG; claim-before-publish holds on
  every switch path; the stack-switch is centralized in `__idle_dispatch` and runs
  before the scan. `smp-dispatch` pins the right thing (cell 36200 bumped only by a
  non-zero core id) and is race-safe as written.
- **The crypto landmines are handled.** `sha256`/`sha512` return words and every new
  hash call wraps them (`hkdf-words-to-bytes`); `char-code` is CCE and every wire
  label routes through `to-unicode`. The old buggy paths survive only as deliberate
  negative pins. Tag comparisons (Poly1305, GCM, DTLS Finished, Ed25519) are all
  constant-time. The DTLS MITM negative (`dtls-auth-loopback`) is on main and green.
- **The eight dropped test files (CL 7735) are all present in `//Codex/main`** today,
  and a spot-check of every other `#1 add` in the window found no further silent drop.
- **The docs reconciliation (7483/7484) walked back no real gap.** Every removed
  BACKLOG line survives under the new section scheme; the register grew, which is the
  correct direction.
- **No remaining self-agreeing `f(x)=0` stubs** in the board HAL or kernel drivers
  after the real-MMIO fix (7534). The only such bodies left are named host-side plug
  stubs and error-test fixtures.
- **`codex-vm.exe` is currently byte-identical to a reproducible rebuild of its `.c`**
  (verified this review) — finding #11 is the *absence of a guard*, not present drift.

---

## Detailed findings

### 1. CRITICAL — the trust gate takes the messenger's word for the messenger (CL 7677, 7516)

The public claim is that the in-compiler trust gate means "the messenger cannot
smuggle a definition past the gate." Against the code it does not.

The four guards (present → hash → signed-by-known-key → score ≥ floor) are real, and
the content-hash guard genuinely holds: `gate-check-integrity` recomputes
`fact-content-hash(f.content)` and compares it to the digest pinned in the source
`quotes` line. Ed25519 verify and SHA-256 are real, not stubs. On a missing quotation
the compiler fails **closed** (`ImportMissing` → `CODEGEN-HALTED`).

The defeat is in where the trust manifest comes from. 7516 promised the compiler would
"read one from disk." It does not. In the live path (`opening.codex` `build-works` →
`apply-key-line`) the manifest — fingerprints, public keys, **and their trust scores** —
is parsed out of the same `%%QUOTED-WORKS%%` blob that carries the works:

```
KEY <fingerprint> <public-key> <score>      -- score is whatever the transport writes
WORK <hash> <author> <score> <fingerprint> <sig> <kind>
```

There is no on-disk manifest, no root key, no anchor. A malicious transport delivering
a work whose digest the source pins generates its own keypair, signs the malicious
content, computes the correct hash, and emits `KEY <own-fp> <own-pub> 10000`. All four
guards pass — the key is "known" because the attacker put it in the manifest. The
signature/score layer adds **zero** protection over the digest pin against the exact
threat (an untrusted transport) it advertises.

Secondary: an absent `trusting above` defaults the floor to **0** (`Parser.codex`
`header-trust-floor … 0`), so even with a real manifest a `quotes` line with no explicit
floor is a null score check. And the whole gate is opt-in — ordinary `cites` resolution
is entirely ungated; only a source that writes `quotes` is checked at all.

**Recommend:** file that the compiler's `TrustManifest` must come from a trusted on-disk
source outside the `%%QUOTED-WORKS%%` blob, anchored to a root of trust; until then the
signature/score guards must not be described as defending against a malicious transport
(the digest pin, and only the digest pin, is what protects a build). Make an absent
`trusting above` a compile error on any chapter that quotes.

**Memory/time:** the gate is zero-cost when unused (`has-quotations` short-circuits — this
is why the self-build fixed point is unaffected). But `join-work-lines` and `resolve-admit`
both do `acc & … & "\n"` growing-accumulator concatenation → O(n²) in work-line count,
every intermediate retained until return. Harmless at a handful of small works; a real
risk before the feature is load-bearing. Rework to bounded copy.

### 2. CRITICAL — "an active man-in-the-middle is defeated" omits peer-identity binding (CL 7810)

This is the strongest crypto work in the window and most of the claim holds. The chain
walk verifies real Ed25519 signatures (not name matching — a name match still requires
the signature at `x509-anchor-sig`); validity dates are checked on every cert including
the leaf; the CA bit is required for issuers and defaults false; v1 and non-`0xFF` CA
booleans are refused; the anchor must sign the terminal cert by key, not name; and the
endpoint fails closed (chain/CV/parse failure all route to `dtls-ep-idle` with the
transcript un-advanced, no Finished emitted). The MITM trace holds: the client verifies
CertificateVerify over its own transcript, so a flipped ServerHello key_share byte
diverges the transcript and the signature fails. This is genuinely gated with negatives
(`dtls-auth-loopback.expected` asserts `client-done=False authenticated=False` for both
the MITM and downgrade cases; `x509-chain` carries one-change-per-negative).

The gap: `dtls-ep-client-cert-walk` accepts **any** chain that reaches a trusted anchor.
The leaf's subject is never checked against an expected peer identity. So a credential-less
MITM is defeated (as claimed and tested), but any holder of a valid certificate under the
trusted CA can impersonate any other peer in the same trust domain. The docs frame this as
"authenticates a fleet whose CA you run," but the flat sentence "an active man-in-the-middle
is defeated" is broader than the code delivers.

**Recommend:** state peer-identity binding as an explicit open item rather than leaving it
inside the "fleet CA" framing.

Lower-severity in the same cluster (finding #16): the GHASH GF(2¹²⁸) multiply branches on
hash-subkey bits (`AesGcm.codex`) — not constant-time, though off the unprotected handshake
path and with no attacker cache-timing channel on bare metal. The RFC 4303 anti-replay
window exists in `Dtls.codex` but is **not wired into `DtlsEndpoint`** (the endpoint uses
unprotected `dtls-plain-decode`) — acceptable for a handshake-only endpoint, but "the
anti-replay window ships" is true of the primitive, not the live path.

**Memory/time:** clean. DER decoder rejects indefinite/non-minimal/over-buffer lengths;
`x509-chain-verify` caps the chain at 8; all extension/cert-list walks are datagram-bounded.
The only multi-KB transient is the whole-handshake transcript (SHA-256 has no incremental
interface), correctly identified and released with the handshake.

### 3–5. HIGH — the FAT16 write path (CL 7822, landed hours before this review)

The defensive core is genuinely well-built and byte-verified, and several classic corruption
bugs are *handled*: both FAT copies are updated (`fat16-write-fat-copies` loops all
`vol-num-fats`; `fat16-alloc.expected` pins `copies-agree True`), every cluster is claimed
`0xFFFF` before use so the last stays end-of-chain, the free scan starts at cluster 2 and is
bounded (never allocates reserved 0/1, fails cleanly on a full disk), the 8.3 name is
upper-cased and space-padded via `to-unicode` (not CCE), and the directory entry is written
after the FAT. For a single **new** file that fits, the claim is true.

The three defects all sit in untested paths (both tests write single-cluster fresh files):

- **#3 — overwrite writes a duplicate directory entry (`Fat16.codex`).** `fat16-create-file`
  only ever searches for a *free* slot (first byte `0x00`/`0xE5`); it never checks whether the
  name already exists. An existing entry is skipped, a **second** entry with the same 8.3 name
  is written pointing at a new chain, and the old chain is **leaked** (never freed). Reads become
  order-dependent. Because `fat16-write-file` is the `write-file` entry point and `write-file`
  semantics are truncate-and-write, this is a semantic defect, not merely "callers not ported."

- **#4 — full-disk mid-write leaks the partial chain (`Fat16.codex`).** If `fat16-alloc-cluster`
  fails partway through a multi-cluster file, no directory entry is written (good — no cross-link),
  but every cluster already claimed is left marked in **both** FATs with nothing pointing at it.
  Silent cumulative free-space loss until chkdsk, on an ordinary full disk, with no rollback.

- **#5 — O(n²) time and permanent heap for multi-cluster files (`Fat16.codex`).** Every
  `fat16-alloc-cluster` restarts the free scan from cluster 2 (no resume cursor), so a K-cluster
  file costs O(K²) FAT-sector reads; and `block-read-sector` bump-allocates a fresh 512-byte
  buffer per call and never frees it, so ~O(K²/256)·512 bytes are retained until the top-level
  write returns, on top of the whole file materialized as a `List Integer`. On bare metal with no
  GC this hard-bounds the max writable file and is a real heap-blowup risk. The CL's memory note
  only defends per-sector-vs-per-cluster within one scan; it does not mention the cross-allocation
  quadratic.

Minor: no guest-side FLUSH CACHE (writes persist host-side via `syscall 11`, but an unmount/
power-loss window exists on the "real USB stick" framing); a 0-byte file wastes one cluster;
`512` is hardcoded in the slot-encoding path while everything else uses `vol-bytes-per-sector`.

**Recommend:** BACKLOG 7.15 correctly stays OPEN. Add #3 (overwrite) and #5 (O(n²)) as named
sub-items before any app is pointed at this on a disk holding the only copy of something.

### 6–7. HIGH — two SMP races the 4.1 sweep did not close (CL 7791, 7780)

CL 7780's log says it fixed "every scan-and-claim in the scheduler" and enumerates five. There
is a sixth, and an exit-ordering bug:

- **#6 — `process-spawn` claims a FREE slot with no atomic (`X86_64ProcessHelpers.codex`).** The
  free-slot find loop reads `[slot].state`, compares to `proc-state-free`, and does not mark the
  slot taken until the *end* of the carve/init sequence. Two cores each running a process that
  calls `process-spawn` concurrently both see the same FREE slot, both `__spawn_pool_carve` the
  same slot-indexed region, and one clobbers the other. Latent only because `smp-dispatch` spawns
  all six children serially on the BSP before waiting — nothing in the design forbids an
  AP-resident process from spawning. Fix: `LOCK CMPXCHG FREE→<reserving state>`.

- **#7 — `process-exit` publishes FREE before leaving its own stack (`X86_64ProcessHelpers.codex`).**
  It stores `proc-state-free` into its slot, then runs the entire 16-slot wake loop *still standing
  on the exiting process's stack*, and only switches RSP inside `__idle_dispatch` at the very end.
  The instant the slot is FREE it is claimable by `process-spawn` (#6), which carves the same
  slot-indexed stack region and resumes a process there while the exiting core is still on it — the
  exact two-cores-one-stack fault 4.1 was created to kill, re-entered through the spawn-reuse door.
  The wake loop does no stack writes, so it survives *today* only because APs receive no interrupts.

That last point is the real hazard and it is written down nowhere: **the current
no-two-cores-one-stack proof depends on AP non-interruptibility.** BACKLOG 4.11's stated content
(per-core LAPIC timer or scheduler IPI) is exactly what lets an interrupt fire in the publish→
RSP-switch window and push a fault frame through a stack another core has resumed on
(`!EXC=08 RSP=0`). So 4.11 is not merely "we spin instead of sleep" — closing it invalidates the
proof and reintroduces #7 as a regression. This precondition should be recorded in `SMP.md`/BACKLOG
as a hard blocker on 4.11.

Also #22 (LOW): the timer-ISR *no-other-candidate* path does a plain store of `proc-state-running`
over the current entry, so a process that just hit `max-ticks` and was marked ZOMBIE gets resurrected
to RUNNING when it is the only runnable process — asymmetric with the switch path, which correctly
uses CMPXCHG RUNNING→READY. Edge case, probably pre-existing, sharpened by the SMP work.

Register discipline is clean (starve-check clobbers only dead RSI/RCX; RAX comparand and R10 heap
respected in all three CMPXCHG loops), and every table scan is bounded at 16. The one uncapped spin
is `__idle_dispatch`'s idle loop — the documented "idle cores spin" (4.11), a throughput cost, not
a blow-up.

### 8. HIGH — builtin shadowing is fixed by rename, not by a diagnostic (CL 7628, 7638)

The Hamt bug (a library `list-insert-at` shadowing the compiler builtin, redirecting the hottest
insert path to an O(n) body and walking the allocator to the 3 GB stack top) was fixed by renaming
Hamt's function. The *mechanism* is untouched: `X86_64Compound.codex` `if user-arity < 0 & is-builtin
(st.builtin-names) (flat.func-name) then emit-builtin` — a user definition of a builtin name silently
wins for the whole unit, with **no diagnostic at the definition site** (the only shadow diagnostic in
the tree is cite-vs-cite `cdx-duplicate-cite`). BACKLOG 2.15 confirms this is open.

An adversarial sweep of the foreword found **nine live definitions that still shadow a builtin**:
`is-digit` (`CCE.codex`, `Json.codex`), `is-letter`, `is-whitespace` (`CCE.codex`), `text-contains`
(`StringUtils.codex`, `TextSearch.codex`), `text-replace`, `text-starts-with` (`StringUtils.codex`),
`text-split` (`TextSearch.codex`), `text-to-integer` (`Parse.codex`), `race` (`Concurrent.codex`).
All are currently dormant (none of those chapters is cited into the compiler's unit), but
`is-letter`/`is-digit`/`is-whitespace` are the lexer's per-character hot path over ~1.9 MB of source —
the same shape as the Hamt bug, one `cites` line from detonation. The correct fix is a diagnostic
(even a warning) when a top-level definition matches `builtin-names`; it does not exist.

**Memory/time:** the fix itself is neutral (a rename and a cite-not-copy). The finding is that the
quadratic-blowup class can recur.

### 9. MEDIUM — two-list drift and false prose in the builtin tables (CL 7698, 7721)

At main head, `sorted-builtin-names` (146 names) still contains `write-file` and `write-binary-file`,
but `x86-builtin-emitters` (144) has no entry for either — the two lists drifted the moment those
were pulled from the emitter without being pulled from the name list. Currently unreachable (both are
gone from `NameResolver.builtin-names` and `TypeEnv`, so a bare call fails at CDX3002), and thanks to
CL 7599 an unknown builtin is now a hard **CDX2042** rather than a silent miscompile — so severity is
"orphaned entries that could shadow/misroute," not "silent True." They should be deleted from
`sorted-builtin-names`.

Separately, `X86_64Builtins.codex` prose claims `write-file`/`write-binary-file` "both names now resolve
to definitions that keep their promise, next to file-exists in Foreword chapter Fat16." Fat16 defines
`fat16-write-file`/`fat16-write-binary-file` (prefixed, deliberately, to avoid colliding with the
FileSystem effect op). There is **no** bare `write-file`/`write-binary-file` in Fat16. The prose
contradicts the chapter it points at — exactly the "the doc says it shipped" claim the rules say to
make louder, not trust.

For the record, the `file-exists` saga resolved correctly: 7698 replaced the always-True stub
(`li rd, 1`) with an always-False body (wrong guard: `block-sector-count`, which reads 0 on a working
disk), and 7763 finally made it correct (guard the parsed volume's geometry, `fat16-vol-is-usable`,
with `fat16-zero-volume` protecting the division). Fix-then-completion, not a regression pair. No other
constant-return stub bodies survive the sweep.

### 10. MEDIUM — FileSystem is type-only (CL 7518)

The nine FileSystem builtins now carry honest `FileSystem.Read/Write` effect rows, and the real bug
(`KeyManager.export-to-path`, a private-key writer typed pure) is fixed. But the rows are enforced
**only** in the type checker: `cap-filesystem-read` (bit 6) and `cap-filesystem-write` (bit 7) are
defined and manifest-granted but **read by nobody** at runtime — `emit-read-serial-cce-builtin` calls
the serial read unconditionally, `emit-write-file-builtin` is a refusal no-op. This is the inverse
stage-4 shape (type-enforced, runtime-ungated, two granted cap bits unread), defensible on bare metal
(no writable FS; `read-file` is serial transport wearing a filesystem name) but not filed the way
`cap-gpu-memory` is (BACKLOG 1.6). Recommend a mirror entry.

Related: the `scope-allow` test added in 7509 asserts `file-exists "/config/app.toml"` compiles and
prints "in scope" — a compile-time positive whose *runtime* assertion was vacuous while `file-exists`
was a stub. Now that 7763 made `file-exists` real, the runtime half may be meaningful; worth a look.

### 11. MEDIUM — nothing verifies codex-vm.exe against its source (CL 7670)

`tools/codex-vm.exe` is a checked-in ~6000-line C hypervisor built with `/Brepro` (reproducible). This
review rebuilt the checked-in `.c` and confirmed the depot `.exe` is **currently byte-identical**
(SHA256 `016EEE65…E032D`). But no script rebuilds-and-compares it: every `build/*.ps1` reference is a
bare `Test-Path`+invoke, and the gate's only `Get-FileHash` calls hash the pingpong text stages, not the
VM. CL 7670 proves it drifts silently — a binary integrate submits *resolved* content, so a merge shipped
main's pre-merge `.exe` against a merged `.c`, and only a human noticing caught it. Every agent's hardware/
BVT test result is measured against this opaque blob. Because the build is `/Brepro`, a one-line
rebuild-to-temp-and-compare-SHA guard would make the entire 7670 class impossible.

**Recommend:** file a codex-vm.exe freshness gate. This is the largest standing trust gap for a project
whose thesis is "if we didn't build it, we don't trust it."

### 12. MEDIUM — the dropped-add guard is advisory, not a barrier (CL 7731, 7740)

`p4 unshelve` silently drops a `p4 add` (it prints "Can't clobber writable file" and does not open the
file, so the edits submit and the new files vanish) — this cost eight test files, restored in 7735, and
even cost `p4-stale-check.ps1` its own add once. The script now detects the case (`p4 status` →
"reconcile to add"), but two weaknesses remain: (a) it is **not wired into `build.ps1`** — grep finds it
referenced only by itself and two docs, so it is a script an agent must remember to run; and (b) even when
run, the untracked block does **not** set `$bad`, so it `exit 0`s on detection. The guard for the exact
eight-file loss is neither automatic nor fail-closed. The next dropped add will be as silent as the last.

### 13. MEDIUM — the deck knob under-reserves silently (CL 7563)

`-Decks` (phase deck floor scale) is guarded by `[ValidateRange(1,10000)]` only in the PowerShell wrapper;
the real interface is the mode-line `decks=N` token, and `deck-scale-of` clamps only `v < 1` to 100 —
`decks=1..4` are accepted and under-reserve. Under-reservation does **not** raise CDX9002 (the post-copy
floor check never runs); the parse keep-deck copy writes past the floor into scratch it is still reading
and dies in `#GP` (`!EXC=0d`) with no diagnostic. Disclosed and filed as BACKLOG 2.16 (confirmed still
open); the knob makes the sharp edge reachable on purpose.

### 14. MEDIUM — the device gigabyte removes a tripwire (CL 7596)

`emit-fill-device-pd` maps [3GB,4GB) identity, present/RW/NX, to recover the three above-3GB board
batteries. The NX bit means a wild *jump/return* above 3 GB still faults, but a wild *data* pointer there
now silently reads/writes the device aperture (or, with `-board-mmio`, committed host RAM) instead of
raising a clean, unserved `#PF`. No legitimate code depended on the fault; the residual is the loss of a
crash-early tripwire, matching real-hardware behavior. Disclosed in the CL; worth a line in the security
posture doc.

### 15. MEDIUM — FAT16 read path hazards (CL 7763)

Pre-existing, now on more paths: the cluster-chain walk (`fat16-next-cluster`) terminates only on
`fat16-is-end`, so a corrupted FAT with a cluster cycle loops until OOM (no fuel/cycle guard). And the
directory-listing and byte-read loops use `list-push` (append-to-tail snoc); if `List` is a persistent
cons spine, that is O(n) per push → O(entries²) listing and O(filesize²) file read, every element retained
until the top-level call returns. Acceptable for directory listings; genuinely quadratic for large-file
reads. Worth a cons-then-reverse or capacity-buffer follow-up before this is load-bearing.

### 16–18, 22–24

- **#16** GHASH not constant-time; DTLS anti-replay window unreached — see finding 2.
- **#17** `Audio3D.codex` `al-forward = vec3-new 0.0 0.0 1000.0` / `al-up = … 1000.0` — a Real vector holding
  `1000.0` where `1.0` is unity, the bp-identity/t3-identity bug shape (7558) that the "swept the foreword"
  claim missed. Latent: the only consumer normalizes, so the magnitude cancels; but any future `vec3-dot`
  against these without normalizing is 1000× off, and it contradicts the migration's own unit-Real rule.
  Correct to `1.0`.
- **#18** `build/test-gui.ps1` captures the frame purely on host wall-clock (`-screenshot-delay $shotAt`) with
  `Tolerance=0` exact compare, no quiescence detection, no capture-twice-and-compare. This is the reported
  flakiness (menu-select-all/menu-file failing once in four under parallel load) and matches the CL's own
  "a test is only as stable as the app's own determinism." Fine as a diagnostic; not gate-grade. The only
  guard against `-Accept` recording a buggy frame is a printed warning and a documented human step — not
  enforced.
- **#22** timer-ISR sole-process zombie resurrection — see finding 7.
- **#23** `RankedTextSet` rename (7802) is genuinely closed for the compiler↔foreword collision (zero
  top-level overlap verified; the 44-name collision is gone), but the csharp/javascript plug emitters carry
  the rename untracked and outside the plug-smoke subset — unverified by any gate until those plugs rebuild.
- **#24** `AnnotationTransport.codex` `sent = __narrow (ann-sent + ver.sent)` — both operands are
  `0..2³²−1`, so the sum's abstract bound is 2³³, genuinely exceeding the `0..2³²−1` target. Cannot trap in
  practice (each is bounded by a list length nowhere near 2³²), but it is the one `__narrow` in the
  apps/works campaign whose static range does not fit its contract.

### 19–21 — disclosed limitations

- **#19** Network scope *allow* path is dead: `scoped-op-effect` maps `fetch`/`post`/`resolve-dns` to
  Network effects, but they have no handler and die at CDX2040, so only network-scope *refusals* are
  provable today. Disclosed → BACKLOG 1.7. "Network scope is enforced" is a refusal-only, no-legal-caller
  guarantee for now.
- **#20** The `-1` deny/absent conflation across the identity and network gates is fail-closed and
  intentional, but a denied syscall is permanently indistinguishable from empty data.
- **#21** CDX3004 enforces pagination *coherence* (a repeated chapter name must carry distinct-N, M==k
  markers) but not chapter-name *uniqueness* — two unrelated chapters that both wear valid page numbers
  still merge under one slug. By design (repeated names are the intentional multi-file idiom), but it is a
  heuristic that raises the cost of an accident, not a proof of chapter identity.

---

## Recommended BACKLOG actions (net-new from this review)

1. **Trust manifest must be anchored** (finding 1) — the compiler's `TrustManifest` cannot come from the
   `%%QUOTED-WORKS%%` blob; needs a trusted on-disk source and a root of trust. Until then, do not describe
   the signature/score guards as defending against a malicious transport. Make absent `trusting above` a
   compile error on any chapter that quotes.
2. **DTLS peer-identity binding** (finding 2) — file as an explicit open item; "MITM defeated" currently
   means *unauthenticated* MITM only.
3. **FAT16 overwrite + O(n²)** (findings 3, 5) — add as named sub-items under the still-open 7.15.
4. **SMP: `process-spawn` free-slot claim must be atomic** (finding 6), and **record AP-non-interruptibility
   as a hard precondition that BACKLOG 4.11 must not break** (finding 7) — or fix `process-exit` to leave the
   stack before publishing FREE.
5. **Builtin-shadow diagnostic** (finding 8) — a warning when a top-level definition matches `builtin-names`;
   nine live foreword defs are armed, including the lexer hot path.
6. **codex-vm.exe freshness gate** (finding 11) — reproducible rebuild-and-compare, wired into the gate.
7. **Wire `p4-stale-check.ps1` into `build.ps1` and make its untracked block fail-closed** (finding 12).
8. **Delete `write-file`/`write-binary-file` from `sorted-builtin-names` and correct the false prose in
   `X86_64Builtins.codex`** (finding 9).
9. **File the FileSystem unread-cap-bits gap** mirroring `cap-gpu-memory`/1.6 (finding 10).

---

*Reviewer's note: this is inspection-and-diff review across ~120 substantive changelists, not a battery
run. Where a finding names a race or a corruption path, it is a reasoned failure scenario traced through the
code, and — for the two CRITICAL and the FAT16 HIGH items especially — warrants a targeted reproduction
before it is treated as closed. Every green item in "What is solid" was verified against source, not assumed.*
