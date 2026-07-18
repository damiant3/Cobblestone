# Build Verification Test — minimal confidence gate.
#
# The compiler self-compile (fixed point) already proves: pattern matching,
# variants, records, let/if/when, function application, closures, text ops,
# list ops, bounded integers, recursion, TCO, binary ops, field access,
# act blocks, prose, cites, chapters/sections/pages.
#
# This BVT covers ONLY what the fixed point does NOT exercise:
#   - type classes, effects/handlers, linear types, mutable state
#   - try/retry, for-loops, concurrency (fork/await)
#   - a handful of error-rejection tests (diagnostic path)
#   - one library smoke (hamt — the backing store for Set/KvStore)
#   - codegen stress (noise-test): arithmetic-heavy pure leaves whose
#     register/temp pressure differs from the compiler's own shape, so
#     the self-compile cannot catch a miscompile there
#
# ~16 tests, runs in under 30 seconds after a fresh build.
#
# Usage:
#   build/bvt.ps1                 # uses seed from build-output
#   build/bvt.ps1 -CodexCdx X    # uses specified CDX
[CmdletBinding()]
param(
    [string]$CodexCdx,
    [int]$Jobs = 8
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$BvtTests = @(
    # --- Language features the compiler does NOT use ---
    'codex\test\typeclass-smoke.codex'         # type classes (Show, Eq, Ord)
    'codex\test\typeclass-poly.codex'          # a class with a SUPERCLASS: the instance dict must carry __super-<Super>, which was a null pointer in every instance until the desugarer populated it
    'codex\test\handler-smoke.codex'           # effect handlers
    'codex\test\handler-dotted-discharge.codex' # a family handler (with Store) discharges its dotted members (Store.Read, Store.Write) via the sub-effect lattice; probe declares NO row, so a discharge failure surfaces as CDX2031/CDX2033, not a wrong answer
    'codex\test\cap-gpu-family.codex'          # a bare [Gpu] row is a FAMILY grant and must survive the manifest: it compiled clean and was then DENIED at the window guard (write: -1) because manifest-cap-id did not know the name "Gpu" (BACKLOG 1.13). Pins the row -> manifest -> process-cap-word path end to end
    'codex\test\linear-smoke.codex'            # linear types (consume, freeze)
    'codex\test\mutable-smoke.codex'           # mutable records, field mutation
    'codex\test\try-smoke.codex'               # try/retry/fallback
    'codex\test\with-timeout-test.codex'       # timeout construct
    'codex\test\fork-nested.codex'             # concurrency (fork/await)
    'codex\test\unit-smoke.codex'              # unit type

    # --- Library correctness (not exercised by self-compile) ---
    'codex\test\hamt-test.codex'               # HAMT — backs Set and KvStore
    'codex\test\sort-test.codex'               # sort with custom comparators
    'codex\test\crypto-test.codex'             # AES, ChaCha20

    # --- Codegen stress (caught regressions the fixed point misses) ---
    'codex\test\lir-selector-smoke.codex'      # the LIR selector's correctness shapes, one boot: a shadowed-global call that must go indirect not direct (answered -1 for 7), a join-into-join coalesce that must refuse or the structural verifier halts a valid program (CDX9007), a compound-accumulator tail call the tree emitter miscompiles and the selector gets right (9 for 8), the 5-param col-hue parallel-move clash (red as yellow), and the result-is-a-use coalesce (RAX garbage for the result). Every one is a wrong ANSWER the fixed point cannot see; pinned by .expected
    'codex\test\noise-test.codex'              # arithmetic-heavy pure leaves (Noise/Perlin gradient math); caught the Tier 2 non-TCO leaf temp-pool miscompile (CL 6387 widening)
    'codex\test\tco-nested-if.codex'           # TCO if-chain with a nested-if arm body; guards the jmp-end elision miscompile that silently skipped the arm (CL 6430)
    'codex\test\real-negate.codex'             # unary minus on a Real: the compiler does no float math, so the fixed point cannot see this. -(f x) where f returns Real was typed Integer, which broke foreword math Matrix4 and with it the whole 3D stack (engine quire, globe, spark)

    # --- Proof system (normalizer soundness) ---
    'codex\test\normalize-eq.codex'            # Stage 3 defeq normalizer: delta/iota reduce flip On -> Off, id-bit On -> On (proofs check by Refl)
    'codex\test\induction-parse.codex'         # Stage 4b/5: induction on Nat CHECKS add n Zero === n (Zero->Refl, Succ->cong ih), erased CDX4020
    'codex\test\induction-list.codex'          # Stage 5: binary-ctor induction CHECKS append xs MyNil === xs (MyCons->cong ih, ?f := MyCons h via N-ary peel)
    'codex\test\induction-assoc.codex'         # Stage 5b: multi-var induction via nested for-all -- append-assoc (induct on named `as`, bs/cs opaque)
    'codex\test\reverse-reverse.codex'         # Stage 5b FLAGSHIP: reverse (reverse xs) === xs via append-nil/append-assoc/reverse-append (applicable lemmas + app-cong + capture-avoiding normalizer)
    'codex\test\induction-param.codex'         # Stage 5c: induction over a PARAMETRIC type Lst a (checker resolves applied ConstructedTy to its SumTy)

    # --- Error rejection (diagnostic path) ---
    'codex\test\errors\normalize-false.codex'  # Stage 3 soundness tripwire: flip On === On is FALSE (flip On reduces to Off), must reject CDX2001
    'codex\test\errors\induction-unsound.codex' # Stage 4b/5 soundness tripwire: Succ case proved by Refl (not cong ih) is FALSE, must reject CDX2001
    'codex\test\errors\induction-list-unsound.codex' # Stage 5 tripwire: N-ary peel injectivity -- MyCons case by Refl is FALSE, must reject CDX2001
    'codex\test\errors\reverse-reverse-unsound.codex' # Stage 5b tripwire: reverse-reverse MyCons step by cong ih alone (no reverse-append lemma) must reject CDX2001
    'codex\test\errors\type-mismatch.codex'    # type error detection
    'codex\test\errors\unknown-name.codex'     # undefined name detection
    'codex\test\errors\non-exhaustive-match.codex'  # exhaustiveness checker
    'codex\test\errors\keyword-as-pattern-var.codex' # keyword rejection
    'codex\test\errors\linear-errors.codex'    # linear type violations
    'codex\test\errors\record-field-silence.codex'  # a record literal names every field, and no other: CDX2006 / CDX2005
    'codex\test\errors\effect-op-unhandled.codex'   # a nullary effect op with no handler read zero and said nothing: CDX2034
    'codex\test\errors\scope-let-arm-escape.codex'  # the adversarial half of 2.22: a read of a name bound only inside an if-arm's let must be rejected AT THE READ, by the RESOLVER (CDX3002). It answered CDX2040 from the emitter ("unresolved call" to a name nobody wrote) while both the resolver and the type env aliased their caller's scope, then CDX2002 from the checker once the type env was fixed. The program is rejected either way, so only the CODE says whether the scope discipline is intact

    # --- Regressions ---
    'codex\test\console-readline-cite.codex'   # citing Console must not disable read-line (the handler slot outranked the builtin)
    'codex\test\scope-let-arm-global.codex'    # the type environment has lexical scope (BACKLOG 2.22): an if-arm's `let inner : Integer` must not still be bound after the arm, where it shadowed the global `inner : Text` and made the CHECKER reject a valid program with CDX2001 -- one the emitter compiled correctly. The fixed point cannot see this: the compiler self-compiles either way, because the leak only ever ADDED bindings and the compiler's own source never reads one out of scope
    'codex\test\match-arms-per-line.codex'     # a `when` keeps every `is` arm however they are packed across lines (BACKLOG 2.25). The parser pinned `ln` to the line the match STARTED on, so the second arm of the second line matched neither the line nor the column test and SILENTLY ENDED THE MATCH -- dropping every later line including the `is otherwise`, with no diagnostic. Deliberately an Integer match: exhaustiveness was the only thing that ever caught this and it cannot fire on an open type, which is why the one site anybody found was a closed variant and the rest went unnoticed
    'codex\test\scope-handler-clause.codex'    # a handler clause's `resume` binder does not escape the clause body (BACKLOG 2.23, closed). The quietest of the scope leaks: it crashed nothing and printed a plausible wrong answer -- the read found the clause's recycled slot, which happened to hold the previously-built Text, so `after: GLOBAL` printed as `after: counter: 42`. `tick` must STILL be bound after its clause (the handled expression reads the handler slot through the op-name, added after the body on purpose), so this also pins the restore ORDERING: get it wrong and handler-smoke's multi-op and nested cases go with it
    'codex\test\scope-try-region.codex'        # a `trying` body's bindings do not escape into `falling back to`, `on failure`, or past the `end` (BACKLOG 2.23). This one MISCOMPILED rather than rejecting, because the checker was already right and the resolver and emitter were not: the fallback's `label` typed as the global Text and loaded the body's Integer slot, so a wild pointer reached text concatenation. The fixed point cannot see this either -- `trying` appears in no compiler source, so the self-compile never emits a try at all and this test is the only thing between the try regions and silence
    'codex\test\field-cache-text-lit.codex'    # a field read twice in one call gives the same answer both times, even with a text literal between the reads (BACKLOG 2.28). `emit-text-lit` writes RAX with a hardcoded mov-ri64 before alloc-temp runs, and alloc-temp only evicts the register it hands out -- so the field cache went on claiming `b.items` was live in RAX after the literal had overwritten it, and `list-length [7, 8, 9]` returned 9: the character count of "read-text". A wrong number, no crash, no diagnostic. THE FIXED POINT CANNOT SEE THIS, and green self-compiles are exactly what it produced for months: the compiler's own instance of the shape (`find-effect-op-addr (st.effect-op-addrs) "read-text" 0 (list-length (st.effect-op-addrs))`) walked off the end of a two-element list and found no match, which is the answer it wanted anyway. It only ever surfaced as a crash when a program's heap layout put a non-canonical pointer after the list. The literal must stay LONGER than the list or the walk stays in bounds and this passes while broken

    'codex\test\apps\trust-vouch-depth.codex'  # the vouch-walk memo is keyed on the agent AND THE DEPTH (BACKLOG 6.4). An agent's score is not a property of the agent: the walk is capped at depth 5, so the same agent scores 8000 arriving at depth 1 and 0 arriving at depth 5, and both are right. The deep arrival is walked FIRST here, so a memo keyed on the bare agent name caches 0 and the shallow arrival reads it back -- x scores 0 instead of 6400. Measured: with the key cut to the bare agent, EVERY other trust test still passes, trust-lattice-test included, and only this one fails. A wrong trust score admits or refuses a quotation with no diagnostic anywhere

    # --- The repository protocol (BACKLOG 6.1) --- these carry a .disk sidecar
    'codex\test\apps\repo-source-fact.codex'   # Codex stores its own SOURCE: a real chapter, pipes and all, written to a block device as a content-addressed Ed25519-signed fact, booted back, and admitted by the same import gate the quotation gate uses. A forged body is ImportCorrupt
    'codex\test\apps\disk-facts-multi-load.codex'  # a fact bigger than one sector must not truncate the store, and one corrupt fact must not take every fact behind it
    'codex\test\apps\repo-checkout.codex'       # A CHECKOUT: 3 facts, 2 works. The checkout takes the REVISED edition of a path and leaves the superseded one behind (a checkout is a tree, not a log), comes back byte-identical, and the superseded edition is still in the store forever, addressable by its hash
    'codex\test\apps\repo-tombstone.codex'      # REMOVAL: a path is stored, retired with a tombstone, and stored again; the checked-out tree follows the last word for the path each time (store->tomb->restore), while every edition stays in wi-works addressable by its hash
    'codex\test\apps\repo-index-snapshot.codex' # PERSIST THE INDEX: the whole materialized WorkIndex is snapshotted as a kind-40 fact, then a def is added after it; a fast rebuild (load snapshot + replay only the tail) checks out identically to a full log replay, the snapshot alone checks out the tree as of its moment, and a superseded edition is still addressable by hash -- the history survives the snapshot, not just the tree
    'codex\test\apps\repo-tombstone-signed.codex' # AUTHENTICATED REMOVAL: a signed kind-39 tombstone the verified index checks before honoring. The owner's signed tombstone retires a.codex; a forger the manifest does not hold cannot retire b.codex (the verified index refuses it, b stays); the trusting index still drops both by path. An unsigned removal was a hole any sector-writer could use
    'codex\test\apps\repo-tombstone-replay.codex' # REPLAY DEFEATED: every assertion is validly signed by the owner -- the attack is copying an OLD signature forward to override a newer one. The verified index resolves each path by the signed timestamp, so a replayed t2 tombstone cannot retire a file re-added at t3, a replayed t1 definition cannot resurrect a file retired at t2, and a genuinely newer t9 tombstone still retires
    'codex\test\apps\fact-sync-test.codex'      # REPLICATION: two content-addressed stores that overlap but differ reconcile to the union with no conflict resolution -- the shared fact is not duplicated, each side asks for exactly the fact it lacks, and merging in either direction lands on the same store
    'codex\test\apps\fact-sync-wire-test.codex' # THE WIRE VERB: the reconciliation carried as AgentMessages over TrustTransport. A offers its hashes, the offer is encoded to a tagged frame body and decoded back exactly as it would cross TCP, B answers with the one fact A lacked, that reply crosses the same wire, and A absorbs it to the union
    'codex\test\apps\checkout-emit.codex'       # stage one of build/test-compile-from-store.ps1: stores a REAL PROGRAM, revises it, checks the tree back out and prints it. Its .expected IS the checked-out source, so it pins the checkout BYTE FOR BYTE. The first edition is wrong on purpose
    'codex\test\apps\colophon-dogfood.codex'    # THE DOGFOOD: a two-chapter quire goes into the store, one chapter is edited and stored again, and the test prints an honest account -- what works (bytes, hashes, signatures, the gate, both editions retained forever) and what does not (ask for a chapter by name and the store hands back the last file written, whichever it was). A dogfood test that only prints its successes is a colophon
    'codex\test\apps\disk-facts-compact.codex'  # compaction kept ONE FACT PER KIND -- it would have destroyed every source definition but one, and every secret entry, FileShare manifest and revocation record but one. A compactor that cannot prove two facts are the same must keep both
)

$OutRoot    = 'test-output'
$ResultsDir = Join-Path $OutRoot '_results'
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
if (Test-Path $ResultsDir) { Remove-Item -Recurse -Force $ResultsDir }
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

$TestLog = Join-Path $OutRoot 'bvt.log'
Set-Content -Path $TestLog -Value '' -Encoding UTF8
$env:CODEX_SWEEP_LOG = (Resolve-Path $TestLog).Path

if ($CodexCdx) {
    $stage0 = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    Copy-Item -Force $CodexCdx $stage0
}

$stage0 = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path $stage0)) {
    Write-Host "ERROR: No kernel at $stage0 — run build/build.ps1 first." -ForegroundColor Red
    exit 1
}

$vmExe = Join-Path (Resolve-Path .).Path 'tools\codex-vm.exe'
if (-not (Test-Path $vmExe)) {
    Write-Host "ERROR: codex-vm.exe not found at $vmExe" -ForegroundColor Red
    exit 1
}

$missing = @()
foreach ($t in $BvtTests) {
    if (-not (Test-Path $t)) { $missing += $t }
}
if ($missing.Count -gt 0) {
    Write-Host "ERROR: Missing BVT test files:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "BVT: $($BvtTests.Count) tests, $Jobs parallel slots"
Write-Host ""

# Phase 1: compile all tests
Write-Host "--- Phase 1: compile ---"
$compileScript = Join-Path (Resolve-Path .).Path 'build\compile.ps1'
$compileFails  = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$compilePass   = [System.Collections.Concurrent.ConcurrentBag[string]]::new()

$BvtTests | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $t          = $_
    $base       = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $outDir     = Join-Path $using:OutRoot $base
    $cdxOut     = Join-Path $outDir "$base.cdx"
    $logOut     = Join-Path $outDir "$base.log"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $failFile = $t -replace '\.codex$', '.failing'
    $expectFail = Test-Path $failFile

    $r = & pwsh -NoProfile -File $using:compileScript -Src $t -Out $cdxOut -Log $logOut 2>&1
    $ok = $LASTEXITCODE -eq 0

    if ($expectFail) {
        if ($ok) {
            ($using:compileFails).Add("$base (expected compile FAIL but got PASS)")
            Write-Host "  FAIL  $base (expected compile failure)" -ForegroundColor Red
        } else {
            $codes = (Get-Content $failFile -ErrorAction SilentlyContinue) -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' }
            $logText = if (Test-Path $logOut) { Get-Content $logOut -Raw -ErrorAction SilentlyContinue } else { '' }
            $allFound = $true
            foreach ($code in $codes) {
                if ($logText -notmatch "CDX$code") { $allFound = $false }
            }
            if ($allFound) {
                ($using:compilePass).Add($base)
                Write-Host "  PASS  $base (expected error)" -ForegroundColor Green
            } else {
                ($using:compileFails).Add("$base (missing expected CDX codes: $($codes -join ','))")
                Write-Host "  FAIL  $base (wrong error codes)" -ForegroundColor Red
            }
        }
    } else {
        if ($ok) {
            ($using:compilePass).Add($base)
            Write-Host "  PASS  $base" -ForegroundColor Green
        } else {
            ($using:compileFails).Add("$base (compile failed)")
            Write-Host "  FAIL  $base (compile)" -ForegroundColor Red
        }
    }
}

# Phase 2: run tests that have .expected files
Write-Host ""
Write-Host "--- Phase 2: run ---"
$runFails = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$runPass  = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$runSkip  = 0

$runnableTests = $BvtTests | Where-Object {
    $exp = $_ -replace '\.codex$', '.expected'
    Test-Path $exp
}

$runnableTests | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $t    = $_
    $base = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $outDir  = Join-Path $using:OutRoot $base
    $cdxOut  = Join-Path $outDir "$base.cdx"
    $runOut  = Join-Path $outDir "$base.out"
    $expFile = $t -replace '\.codex$', '.expected'

    if (-not (Test-Path $cdxOut)) {
        ($using:runFails).Add("$base (no CDX to run)")
        Write-Host "  SKIP  $base (no CDX)" -ForegroundColor Yellow
        return
    }

    # A test with a .disk sidecar needs the block device handed to it. Without
    # this the BVT could not run one at all, so every disk test lived in the
    # battery only -- which is to say it never ran at the gate. test-run.ps1
    # copies the sidecar to a writable temp image, so the depot copy is safe.
    $runArgs = @('-Kernel', $cdxOut, '-OutFile', $runOut)
    $diskFile = $t -replace '\.codex$', '.disk'
    if (Test-Path -PathType Leaf $diskFile) { $runArgs += @('-DiskFile', $diskFile) }

    & pwsh -NoProfile -File (Join-Path $using:PWD 'build\test-run.ps1') @runArgs 2>$null
    if (-not (Test-Path $runOut)) {
        ($using:runFails).Add("$base (no output)")
        Write-Host "  FAIL  $base (no output)" -ForegroundColor Red
        return
    }

    $actual   = (Get-Content $runOut -Raw -ErrorAction SilentlyContinue) -replace "`r", ""
    $expected = (Get-Content $expFile -Raw -ErrorAction SilentlyContinue) -replace "`r", ""
    $actual   = $actual.TrimEnd("`n")
    $expected = $expected.TrimEnd("`n")

    if ($actual -eq $expected) {
        ($using:runPass).Add($base)
        Write-Host "  PASS  $base" -ForegroundColor Green
    } else {
        ($using:runFails).Add("$base (output mismatch)")
        Write-Host "  FAIL  $base (output mismatch)" -ForegroundColor Red
        Write-Host "    expected: $($expected.Substring(0, [Math]::Min(80, $expected.Length)))" -ForegroundColor DarkGray
        Write-Host "    actual:   $($actual.Substring(0, [Math]::Min(80, $actual.Length)))" -ForegroundColor DarkGray
    }
}

$sw.Stop()
$totalPass = $compilePass.Count + $runPass.Count
$totalFail = $compileFails.Count + $runFails.Count
Write-Host ""
Write-Host "--- BVT Results ---"
Write-Host "  Compile: $($compilePass.Count) pass, $($compileFails.Count) fail"
Write-Host "  Runtime: $($runPass.Count) pass, $($runFails.Count) fail"
Write-Host "  Total:   $totalPass pass, $totalFail fail"
Write-Host "  Time:    $([math]::Round($sw.Elapsed.TotalSeconds, 1))s"

if ($totalFail -gt 0) {
    Write-Host ""
    Write-Host "FAILURES:" -ForegroundColor Red
    $compileFails | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    $runFails | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "BVT PASSED" -ForegroundColor Green
exit 0
