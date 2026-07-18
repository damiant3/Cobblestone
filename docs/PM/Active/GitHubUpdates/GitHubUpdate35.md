# GitHub Update 35 -- 2026-07-17

Covers main CLs 7356-8915 (Update 34 covered through 7355) -- the largest
cycle the project has had, and the one where several things the founding
document asked for stopped being aspirations. The exact upper bound moves
until the push; the numbers below are measured at push-prep time.

The headline is the oldest ask in the repository. The rest is a compiler
that grew a middle end and then let it emit real code, a machine that
learned to make a sound and then to hear one, a device that can update its
own firmware and roll it back, two security holes we found in our own work
and closed, and a polish round that made two compression formats actually
compress and cleared a layer of deep rot out of the host VM.

## The headline: a program was compiled out of the fact store

The founding prompt proposes deleting GitHub and replacing it with a
content-addressed store of signed facts. That sentence is now literally
true, end to end, with no Perforce anywhere in the chain.

`build/test-compile-from-store.ps1` boots a **blank disk**, stores a Codex
program into it as an Ed25519-signed content-addressed fact, revises it,
rebuilds the work index by replaying the log off the disk, checks out the
tree, compiles that checked-out source, runs it, and prints:

    I was compiled out of the fact store.

The first stored edition is wrong on purpose, so a checkout that follows
the log instead of the tree fails loudly rather than passing by accident.

What that took, in the order it bit:

- **A SourceDefinition used to be a pointer.** It carried a hash and a
  *Perforce path where the bytes could be found*. That is an index over
  somebody else's store, not a store. The fact now carries the bytes,
  signed, admitted through the same `gate-import` the compiler's quotation
  gate uses.
- **Replay truncated the store at the first multi-sector fact.** Every
  fact behind it was on the disk, intact and verifiable, and invisible.
- **Compaction kept one fact per KIND.** It would have destroyed every
  source definition but one, every secret but one, every revocation but
  one. The rule that came out of it: *a compactor that cannot prove two
  facts are the same must keep both.*
- **An index of works, not of records:** `wi-current` (path to hash -- the
  tree) beside `wi-works` (hash to work -- the complete history).
  Superseding is not deleting; every edition ever stored stays addressable
  by its hash, forever.
- **Removal, authenticated.** A signed tombstone the verified index checks
  before honouring. An unsigned or forged removal is refused. Then
  **replay-resistant**: the index resolves each path by signed timestamp
  rather than log position, so a replayed old tombstone cannot retire a
  file that was re-added, and a replayed old definition cannot resurrect
  one that was retired.
- **Replication.** Two stores that overlap but differ reconcile to the
  union with no conflict resolution -- by content hash, order-independent
  -- and the reconciliation rides `TrustTransport` as real wire messages.
- **A snapshot** of the materialised index (a kind-40 fact) so a boot
  replays only the tail, with a full-replay fallback whenever the snapshot
  cannot be trusted. A snapshot is a cache; it is never the truth.

Pinned by `repo-source-fact`, `repo-checkout`, `checkout-emit`,
`repo-tombstone`, `repo-tombstone-signed`, `repo-tombstone-replay`,
`repo-index-snapshot`, `fact-sync-test`, `fact-sync-wire-test`,
`disk-facts-compact`, `colophon-dogfood` -- all in the standing gate.

**The honest ceiling, in the words of the CL that set it:** this is a small
content-addressed store with a tree and a history, and it is not yet
something you would trust a repository to. *Dogfood it; do not move
anything into it that you would miss.* The compiler still reads its quoted
works from the offered blob rather than the store by default, the cutover
is unowned, and compaction reclaims almost nothing.

## Multi-core is real

Before this cycle no application processor had ever executed a guest
instruction. An AP ended its bring-up at `hlt; jmp hlt`, and codex-vm was
faking the ready count and rewriting the boot processor's RIP past its own
wait loop. The tests passed.

Now: six children spawned at `-smp 4` execute across four cores. Each AP
arms its own LAPIC timer, so a process on an application processor is
preempted exactly as one on the boot processor is. Idle cores `hlt` and
wake on an IPI instead of spinning. A process can be pinned to a core. A
faulting AP produces a real `!EXC` dump on its own IST stack instead of a
silent triple fault. AP boot then ported to RISC-V, and to ARM64 via PSCI
`CPU_ON`.

The discipline that made it trustworthy: **an SMP test must assert that a
different core ran the work, not that the work finished** -- one core can
finish the work. `smp-dispatch` reads a cell only a core whose id is not
zero ever writes. `smp-preempt` reads a cell only a non-zero core's timer
ever bumps. Both fail single-core.

Two races were found with a **yield-injection mutant** rather than by luck:
a wake race that put one process on two cores, and a channel lost-wakeup
deadlock. The mutant hangs the unfixed code and completes with the fix --
which is the only way either would ever have been seen.

## The compiler grew a middle end -- and the selector now reaches the binary

The compiler had no IR-level optimisation layer. It now has one: an
occurrence analyser, a GHC-style simplifier (constant folding, SCCP,
dead-let elimination, occurs-once inlining, copy propagation), an
`ir-check` validator, and an ablation harness that measures what each pass
is worth. Best measured result: **-17,040 compiler bytes** from one
simplifier slice.

Beneath it, a flat LIR built in seven increments -- representation,
blocks and branches, match lowering, constructor patterns, backward
liveness, an allocation verifier, and a **Wimmer linear-scan register
allocator**.

**And as of this cycle it emits real code** (BACKLOG 3.8). The earlier
note in this file -- that the allocator "moved not one emitted byte" --
is retired. The x86 selector now compiles a whitelisted class of
functions end to end: non-punctual, at most six plain parameters, a
non-bounded return type, a lowerable body. Everything outside that class
falls back to the tree emitter, so the language is fully covered whether
or not a given function is one the selector handles.

It was allowed to emit only behind three verifiers that **fail the build
rather than ship a wrong answer** -- CDX9006 (the prologue parallel-move
check), CDX9007 (structural), CDX9008 (allocation) -- and behind a new
`lir-selector-smoke` BVT test that pins five miscompile shapes by their
*answers*, not their instruction counts. The bar it cleared: `build.ps1`
green, the 494-test battery byte-for-byte identical to the tree-emitter
baseline, a 319-seed independent-oracle fuzz clean, and the benches
instruction-neutral.

The campaign's own lesson is worth keeping: a byte-identical fixed point
proves **determinism, not correctness** -- a compiler that miscompiles
the same way twice still reaches one. The gate that actually caught a
silent miscompile mid-campaign (a parallel move in the entry-move
sequence that reported a red channel as yellow, invisible to every
count-based bench) was a `.expected` answer, not the fixed point. The
same workstream also found and closed a **shadowed-let miscompile** with
its own validator (below).

Where that leaves the numbers, measured on the shipping seed over the
nine-bench suite (`bench/compare.ps1`, function-body x86-64 instruction
counts -- not just the four primordial benches):

| Bench | Codex | MSVC /O2 | vs /O2 |
|---|--:|--:|--|
| fib | 22 | 20 | +2 |
| fact | 13 | 15 | **-2** |
| gcd | 10 | 14 | **-4 (-29%)** |
| sum | 7 | 23 | **-16 (-70%)** |
| ack | 23 | -- | |
| tak | 37 | -- | |
| collatz | 13 | -- | |
| locals | 18 | -- | |
| regright | 14 | -- | |

The compiler beats MSVC /O2 on three of the four benches that carry an
in-tree C reference -- `sum` by 70 percent -- and loses `fib` by two. The
honest attribution: the selector is instruction-neutral against the
*current* tree emitter, so these are the whole cycle's codegen campaign,
and the selector -- now load-bearing rather than dead -- is the seat for
the next increment (widening the whitelisted class and beating the tree on
allocation), not yet the source of the gains above.

## The machine's first sound, and then its first hearing

`Kernel chapter HdaAudio` plays PCM to the speakers over Intel HDA: the
machine's first sound. The next day, microphone input -- a `waveIn`
capture ring and an HDA input stream DMA'ing host-microphone samples into
a guest buffer, with `listen` and `is-quiet` as performable `[Microphone]`
operations.

Around them, effects that had been declarations became things a program
can actually do: `Display` now paints the GOP framebuffer (`draw-text`,
`draw-rect`, `clear`, `set-pixel`, full-colour palette), `Identity`
answers `current-user` and `authenticate`, and the keyboard reads real
keys. A Codex program can write a file over `[FileSystem.Write]` alone --
and, by the end of the window, create one in a directory, and create the
directory.

Still declared and still unimplemented: `Camera`, `Location`, `Sensors`,
and the host half of `Gpu`. A declared effect nobody can perform is a
promise the compiler does not keep, and it is tracked as one.

## A device can update its own firmware, and roll back

A new first-class `Flash` capability and a linear `FlashBank` (open ->
write-page* -> seal) that the type system enforces exactly-once. An LwM2M
Object 5 download over CoAP Block2 stages straight into the bank. Gate A
hashes **the bank** -- what is verified is what landed, not what the client
believes it sent. A boot selector counts and *persists* its attempt before
hashing, because an image that hangs never reaches an after.

Pinned by `hal-flash-linear` plus four adversarial probes (laundering the
MMIO through a pure signature, leaking the bank, double-sealing, using
after seal -- each its own `.failing` diagnostic), and by
`ota-lwm2m-loopback`: 254 of 254 bytes staged, one flipped byte refused at
Gate A, still booting the good slot.

The socket underneath is not built -- CoAP is UDP and codex-vm serves only
port 53 -- so the flow is proven over a loopback, not a network.

## Two security holes, both ours, both closed

**The quotation gate's trust manifest was built from the attacker's blob.**
The gate's four guards -- present, hash-honest, signed by a known key, at
or above the chapter's trust floor -- were real and pinned by tests, and
they checked against a trust manifest read out of *the same
`%%QUOTED-WORKS%%` blob the transport supplies.* A malicious transport
minted its own Ed25519 keypair, signed the forged content, computed the
honest digest, emitted its own key at score 10000 beside it, and passed all
four. Trusted keys are now pinned in the source (`trusting <fp> <pub>
<score>`); an unpinned signer is CDX3023.

The path this took is worth recording: one agent closed the entry, a second
agent's 24-hour adversarial review of the first agent's work found the
hole, and the first agent **retracted its own close in writing** -- *"a
correction I owe"* -- before the third fix landed. The review that found it
also produced 24 other findings.

**DTLS authenticated the CA, not the peer.** A valid certificate for the
wrong peer, under the same trusted CA, was an authenticated
man-in-the-middle. An expected-peer binding closes it; an active MITM who
swaps the key_share is now defeated (`dtls-auth-loopback`). It is opt-in
and no production caller wires it yet, which is stated rather than
glossed.

## codex/os is fully effect-enforced

`quire-effect-exempt` turned effect checking *off* for whole quires, so a
driver could touch ports and MMIO while typed pure and its callers declared
nothing. Closed over a migration: a chapter-level `grounds Device.Port,
Device.Mmio` declaration -- scoped per effect, so a chapter that grounds
one and performs another is rejected -- plus de-exempting each OS quire one
changelist at a time. **All ten are now enforced.** The exemption list holds
only the compiler's own quires and the five plug backends.

The migration surfaced what a blanket exemption hides: chapters that had
never been type-checked at all, one of which was silently dropping 12 of 17
match arms.

## Things that were lying, and are not now

A theme across four agents this cycle, worth its own section because the
pattern repeats:

- **Nine filesystem builtins were registered as pure.** The first thing
  honest rows caught: `KeyManager.export-to-path` writes a private key to
  disk and was typed pure.
- **The board HAL's MMIO primitives were stubs.** `mmio-read-32 (addr) = 0`
  -- so all 429 call sites across nine boards read zero and discarded their
  writes, *and the tests passed, because a stub always agrees with itself.*
- **A capability diagnostic that fired nowhere.** CDX4002 existed, read
  correctly, and never fired. A diagnostic that exists and cannot fire is
  worse than a missing one.
- **An unknown builtin compiled to the constant `1`.**
- **`file-exists` returned True for every path** -- because the IDE
  task-file registers returned 0xFF, the guest saw a no-drive signature,
  and the sector count came back zero. A green test asserted the lie.
- **Every `uefi-*` helper read the system-table pointer from GPA 0x8000 --
  which is the PML4.** It pulled a function pointer out of the page tables
  and called it. It could never have worked, and a bisect against four
  older seeds proved it never had.
- **The type environment did not have lexical scope.** `__record-set` is an
  in-place field store that returns the same record, so binding a local
  wrote into the *caller's* environment. The visible symptom was the
  opposite of a laundering hole: the checker **rejected valid programs**
  that the emitter compiled correctly. The name resolver leaked identically
  and was masking it.

## The polish round before the push

The tail of the cycle was cleanup, and two pieces of it were capabilities,
not cosmetics:

- **Four compression formats that did not really compress now do -- and
  prove it against real decoders.** `Zstd` and `Brotli` had been
  pass-through framing that returned *more* bytes than they were handed
  while wearing a standard name, and nothing caught it because no test
  asserted the output was smaller. That is closed, and then some:
  - **`Deflate` never inflates, and ships a data-derived Huffman block.**
    It gained a dynamic-Huffman block (BTYPE=02, verified against .NET's
    `DeflateStream`), and a `deflate-compress-best` that keeps whichever of
    stored / fixed / dynamic is smallest -- so incompressible input falls
    back to a stored block instead of the fixed code's one-byte-per-eight
    growth. `Gzip` uses it and interoperates both ways with zlib, pinned by
    `build/gzip-interop-test.ps1` (python zlib decodes our Gzip and raw
    Deflate, compressible and stored alike).
  - **`Brotli` compresses general data** through a compressed meta-block
    whose payload is a real Deflate block (LZ77 plus Huffman), taken only
    when it beats raw. Still an internal byte-aligned format, NOT RFC 7932
    -- stated, not glossed.
  - **`Zstd` was never actually a valid zstd stream, and now is.** A
    python-`zstandard` oracle -- the only thing in the loop that could tell
    -- proved every frame we had ever emitted was rejected by a real
    decoder: the two-byte `Frame_Content_Size` carries a +256 offset the
    encoder ignored, and a round-trip through our own decoder could never
    have seen it. Fixed to a four-byte FCS, then given a real Huffman-coded
    literals block (direct weights, a single backward-read stream, zero
    sequences). Both are validated the only honest way, by
    `build/zstd-interop-test.ps1` handing our output to real zstd.

  The through-line is this cycle's theme pointed at ourselves: a round-trip
  cannot tell a compressor from a pipe, and self-consistency is not
  validity. The instruments that settled it are independent decoders -- zlib
  and real zstd -- now wired into the tree as `build/gzip-interop-test.ps1`
  and `build/zstd-interop-test.ps1`. The honest ceiling: Zstd's compressed
  block is scoped to small alphabets and short literal regions, and
  full-generality entropy coding (FSE weights, LZ sequences) is the open
  remainder (BACKLOG 5.13).
- **A layer of deep rot came out of the host VM.** codex-vm's audit
  shipped crash, hang, and use-after-free fixes and closed two
  lying-oracle bugs in its UEFI and HDA models; the vga-terminal-demo
  (4.14) and the xHCI command ring (4.15) were fixed, and the deeper finds
  -- dead xHCI rings, NAT sequence/ack handling, a GpuBridge COM3 stub, a
  timer stub -- were filed rather than rushed into the shared binary
  (4.16-4.18).

The rest was hygiene that pays every session: the Perforce filetype of 216
mistyped source files standardized (closing the CCE-in-`.expected` and
em-dash-flips-the-filetype class at its root), a full re-vet of the
backlog against source, the first pass at un-skipping tests carrying a
stale "not yet written" reason that in fact tested nothing of their name,
and the WASM plug taught to emit unit-family constructors as identity so
its output finally passes `wat2wasm`.

## By the numbers

Measured at push-prep time, not carried forward:

| | |
|---|---|
| Changelists to main | **368** in 9 days (blu, val, fester, reek, plus direct-to-main docs CLs) |
| Compiler | 62 chapters, 43,490 lines of Codex |
| `codex/os` | 149 chapters, all ten quires effect-enforced |
| Foreword | 416 chapters |
| Plugs | 53 |
| Apps | 66 |
| Tests | 351 root + 145 expected-failure |
| Backlog | 91,413 -> 79,936 chars, 74 -> 71 open entries |

Seed at push-prep time: `seed/Codex.cdx`, 2,551,486 bytes (~2.43 MB),
SHA-256 `C0B74DBE413B3B25BB5CF47E30F32EC1471092F3A72F36F55D890453CD9A4FB6`,
content hash prefix `C0B74DBE`. A hard fixed point of itself on bare
metal: it compiles itself, and the output of that self-compile compiled by
itself is byte-identical. This seed was rebuilt from source to carry the
LIR selector -- the changelist that landed the selector *source* did not
carry a seed, so the rebuild was a push prerequisite -- and it is larger
than the pre-selector seed, the selector code accounting for the growth.

The bootable image `seed/Codex.img` was rebuilt this cycle (CL 8920) and
carries the current seed; it is a separate distribution artifact
(`build/build-boot-img.ps1`), not part of a seed rebuild, so it is checked
and refreshed explicitly at push time.

## What's next

The store is the thing to push on: the cutover is unowned, and the
compiler still reads its quoted works from the offered blob rather than
from the store. Under it sits a seam that has to be decided rather than
discovered -- every hash and every signature in the trust system is over
**CCE bytes**, so no outside peer can verify our content addressing without
reimplementing our character encoding. It is internally consistent and it
is not interoperable, and it must not be fixed by flipping `text-to-bytes`,
which would change every hash, every signature and every pinned quotation
at once.

For the middle end, the selector is live and the codegen is strong: the
shipping compiler beats MSVC /O2 on three of the four primordial benches
(`sum` by 70 percent -- see the table above). What has not landed is the
selector beating the *tree emitter*: it is instruction-neutral against it
today. Widening the whitelisted class and winning on allocation -- the
reason to have a linear-scan allocator at all -- is the next increment.

And the batteries: a green gate is not the absence of work. Dozens of
written tests are still skipped behind a stale reason that says they were
never written (the first pass at un-skipping them landed this cycle), and
the full battery is not run on an agent's initiative.
