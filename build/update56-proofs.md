# Update 56 release proofs -- measured by blu, 2026-09-08

Every number here was read off the run that produced it. Nothing is carried
forward from a previous cycle (L-COUNT).

## Heads, and why there is more than one

The gate proved main 23978 in every phase except the app-class sweep. The
sweep's one regression (apps/works/GopBoot.codex CDX3002) was fixed by val
and the sweep alone was rerun at 24007, per Damian's Update 55 lesson that a
landing restarts only what it can invalidate. The PUSH HEAD is main 24062.

## The four proofs a release cannot skip

| proof | result | detail |
|---|---|---|
| battery | 1788 total, 1739 pass, 0 fail, 49 skip | `-Tier all -Jobs 4`; compile 485s, run 205s; oracle-scalar 2013/2013, oracle-vector 130/130, oracle-cce 1485/1516 with 31 in documented gaps and 0 unexplained |
| app sweep | 294 units, 291 clean, 3 known-dirty, 0 REGRESSIONS | 4.6 min at the fix head, kernel named as the release seed |
| poison build | 1788 total, 1739 pass, 0 fail, 49 skip | 0xCD-fill seed BC28B4D1ADC90502, 3,217,571 bytes; delta against the real battery newly red 0, fixed 0 |
| DDC | WITNESS HOLDS | codex arm 3,217,563 bytes and c# arm 3,217,563 bytes, each differing 96 bytes inside the signature region and 0 outside; Roslyn took the emitted compiler with 0 errors |

The 96 is the WIDTH of the signature region at offsets 40..135, not an
expected count. Two unrelated signatures agree at a given byte about one time
in 256, so 95 is equally ordinary; quoting 96 as a target is a count carried
forward.

## The seed, and the check that failed the last two releases

`build/output/Sut.cdx` (built by the full gate from the release source),
`seed/Codex.cdx` and `build-output/bare-metal/Codex.cdx` are byte-identical.
So the battery proved the exact compiler being published, and Update 54's and
Update 55's finding (a seed on main that was not the fixed point of its own
source) does NOT recur this cycle.

| field | value |
|---|---|
| bytes | 3,217,563 |
| content hash prefix | 5A46BBCBA3C510DF |
| SHA-256 | D9CF240465C3D0BC40BA854834D0A890C13757230C191E99DD54163C851DB08B |
| MD5 | 04F57DD25FE588488E94C183EFE33B42 |

`check-doc-counts` exits 0 on all 61 claims; 20 had drifted and are corrected
in `TechnicalDetails.md` at main 24027.

## ir-fidelity

`-Grade`: 8 cases, unexpected 0, 24 compiles in 13.2s, no `>>>` row. Every
case named the release seed as its kernel.

## Two findings this release paid for, both landed

**The push reconcile hid every exact-path ignore rule.** `check-ignore
--stdin` fed CRLF returns only paths matched by a DIRECTORY rule and silently
drops every EXACT-PATH one, so the survivor list carried all ten third-party
specifications, the exact files the 2026-09-07 rule withholds. Measured both
directions on the same three paths. Recipe corrected in `PublicPush.md`;
ignored set went 417 to 430 and survivors 323 to 310.

**The sitting configs were withheld by one sentence.** Seven
`build/boot/diag-sitting*.cfg`, each carrying a real LAN address, survived
`check-ignore` because nothing but prose kept them off the mirror.
`build/boot/diag-sitting*.cfg` is now in `.gitignore`, falsified both ways.

## Two rows opened, neither a blocker

**The poison build has no positive control** (`ExaminersAssay.md`, "What the
standing gate does not cover"). A green poison battery and an inert `-Poison`
flag are the same colour. What closes it is one chapter that reads an
uninitialized field, green on the release seed and faulting with
`CR2=0xCDCD...` on the poison seed; no test chapter mentions poison, 0xCD or
CDCDCD at head.

**The diag image hash is not reproducible across workspaces**
(`DiagnosticStick.md`). `DIAG.RCP` bakes the absolute cfg path, so the same
source and seed give 2D8FA5AE in one workspace and A7AFF2FC in another. The
release publishes that hash and the skill calls the image reproducible from
its source and seed, which it is not unless a stranger clones to the same
directory name.

## The diagnostic stick

Rebuilt against the release seed, because the shipped image recorded
`kernel EFE7A6AC9103A884`, two seeds back. The rehearsal record and the final
hash are appended by blu when the 50-arm run lands; do not quote a hash for
it from this file until that line is here.

## The pre-push rerun

Scoped by the CITE GRAPH, not by changed files: every `.codex` changed after
24007, the Chapter: name of each non-test one, then every `codex/test`
chapter citing one of those, unioned with the changed test chapters. 51 arms,
listed in `build/update56-rerun-arms.txt`. Scoping by changed chapters alone
would have run 6.