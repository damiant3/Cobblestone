# GitHub Update 72

Covers main 48739 through 49089. Structured concurrency is wired from source:
`par` and `race` lower over fork, await and await-any, session channels are
typed from source, tasks take several waiters and refuse a stale handle, and a
race loser is freed or cancelled. The hosted plugs are censused against
x86-64 over `codex/test/ops`: Python reaches 63 of 71, Perl 63, Zig 64 and
JavaScript 43, each with its oracle at 59 values. The UOAIX keepers stop
replanning on a busy route job, and a law save that wiped the civic section is
fixed. The TCP checksum and two web bugs reported on GitHub are fixed, and two
pull requests from an outside contributor land. The release seed is `9E200018`
(signed and self-verified); full digests are recorded in `TechnicalDetails.md`.
Numbers in parentheses are main changelists.

## Compiler

- **`par` and `race` (COMPILER-133, COMPILER-135)**: `par` runs over fork and await in ListUtils and `race` over await-any on x86-64; a race loser blocked in `await` is freed, and one blocked in a race of its own is cancelled; a closure stored in a list may not capture a linear value (CDX2067) (val 48828, red 48839, batched in 48854; red 48903, 48935; val 48979).
- **Session channels (COMPILER-134)**: `s-*` session channels are built from source, and session type names and `Task` are written in signatures (red 48870, 48916).
- **Channels and tasks (COMPILER-136, COMPILER-138)**: a closed channel's slot is reused through generation-tagged handles, and `chan-kern-close`'s capability refusal no longer pops a register it never pushed; tasks take several waiters, and `await-any` refuses a stale task handle (red 48964; val 48958, 48968, 48993).
- **COMPILER-139**: a tail self-call past its definition's arity is a call, not a jump, so polymorphic recursion no longer crashes the compile (red 49030).
- **Codex printer**: a `fork` or `await` argument prints in parentheses, so the printed compiler compiles again; `par`'s `list-push deeper (await t)` had printed as `list-push deeper await (t)`, found by the release gate's text round trip (red 49080).
- **CostModel registry**: three batches of builtin allocation rows measured, `unknown` 57 to 35; `chan-kern-create`'s second-channel back edge and the `race`, `await-any` and `s-*` rows measured (val 48895, 48910, 48927).
- **FileSystem scope**: a scope admits a path only at a path component boundary (val 48950).

## Kernel and network

- **TCP checksum (GitHub issue 162)**: `ip-sum` folded each element as its low byte, which stalled a connection carrying a checksum byte over 255; test `tcp-checksum-wire` (val 48881).
- **HTTP body (GitHub issue 162)**: `http-encode` sends its body as UTF-8 and counts Content-Length in those bytes (val 48773).
- **WebMux (GitHub issue 163)**: frames a send drain reads for other connections are stashed and replayed before the next NIC read (val 48774).

## Plugs

- **Operation censuses**: each hosted plug runs the 71 `codex/test/ops` chapters against x86-64's output. Python reaches 63 of 71 (Real numbers, shifts, integer power, a byte heap, vectors, nested patterns, names that parse, bounded record fields), Perl 63 (Real numbers, a byte heap, nested patterns, curried function values, quote-like names, vector loads, UTF-8 literals), Zig 64 (Real approximations and modes, vector loads, `poke-qword`, closures of 7 to 12 values, text patterns) and JavaScript 43 (Real numbers) (red 48974 to 49064).
- **`par` on the plugs**: `plug-oracle-test` takes `-Subject` and a `par` subject; `par` runs on the Python, JavaScript and C# plugs (red 48884, 48922).
- **Perl callees**: `check-plug-callees` has a Perl row (red 49037).
- **Plug drivers**: the spirv, wasm, evidence and zig run drivers compile with the seed by default (val 48931).

## UOAIX

- **Round time (UOAIX-267)**: Britain's shopkeepers back off when the route job is busy and plan a walled-in KEEPER report once per position and aim; graded and accepted (red 48791, 48863, 48876, 48897).
- **Law saves (UOAIX-286)**: a guard who finds a murdered player's body makes the murder known with no reported witness; the law checkpoint refused that state, so every save after it wrote an empty civic section. A cried case is valid, and the composite save and load write and read the law state whatever its invariants say (red 49072).
- **Building (UOAIX-285)**: nothing is built within 3 tiles of a town entrance (red 48940).

## UOAIX Assist

- **Way home**: the Assist travels home by the house's town (tailor, recall reagents, bank, walk home), and weaves bolts before shirts while no tailor is ready (blu 48739).

## Apps

- **Codex Chat (GitHub PR 170)**: a chat server, store, bare-metal shell and live page (val 48817).
- **Pages**: ModBuilder (site and forge), GPU, IoT, Realtime and News build their HTML-string labels as elements (val 48786).

## Tools and build

- **GPU subjects run one at a time**: `test.ps1` and `bvt.ps1` run `-gpu-files` subjects in their serial slot, because several subjects reading different checkpoints off one disk at once slow each other past the wall budget (red 48820, 48823).
- **Cross smoke**: `check-cross-smoke` retries a cut-off capture rather than grading it as a wrong answer (red 48800).

## Release proofs

- **Full gate** (`build/build.ps1`, every phase): the compiler is a hard fixed point in one pass and byte-identical to the shipped seed `9E200018` (content `19AD8C0D`). Two reds were fixed on the way. First, the text round trip refused the printed compiler until the printer parenthesized `await` arguments (red 49080). Second, the wasm run met six subjects new this cycle: `tail-self-over-apply` is fixed (red 49086), and five are named as known plug reds rather than blocking, because the wasm plug runs `fork` at once and `await` as the identity and has no `race`: `await-twice`, `race-first`, `race-blocked-loser`, `race-nested-loser` and `waiters-mixed` (plugs 2.118). `apps/lz77-hostile` timed out once at 16 ways and passed alone.
- **IR fidelity**: 9 cases, 0 unexpected, after three cases and one ablation were repointed to the lambda's new lifted name (red 49088).
- **Battery** (`-Tier all`): 2332 tests, 2267 pass, 65 skip, 0 fail; the GPU-model subjects that went red at Update 71 now run one at a time and pass.
- **App sweep**: 562 units against the baseline, 559 clean and 3 known.
- **Poison build** (0xCD fill, `-Tier all`): 2267 pass, 65 skip, 0 fail.
- **Images**: `seed/Codex.img` rebuilt from the release seed and its own source (SHA-256 `81382408`); `seed/Codex.map` is the gate's map; `build/boot/diag.img` (SHA-256 `07982CDAFEA3EB5B7F98678634580813FDACADFC87956C4649289FDD03226A80`) rehearsed on all 60 arms on Codex VM and QEMU/OVMF, and the shipping check passes.
- **Box** (`docs/Agents/box-release-2026-10-26-u72.csv`, 15:27 to 17:32): free-memory floor 16.2 GiB at 17:07, in the poison battery; peak 18 guests at 16:36, in the battery, at 78 MB working set per guest.
- **DDC** (Roslyn arm): both arms 3,859,863 bytes, the seed's length, and 0 bytes differ outside the signature region.

## Outside contributions

Two pull requests from Steve Howell on GitHub are ingested: PR 167, FileSystem
`append-file`, `list-dir` and `make-dir` with `fat16-append-file` (val 48810);
PR 170, the Codex Chat server, store, bare-metal shell and live page (val
48817). Issues 162 and 163 are fixed (val 48773, 48774, 48881).
