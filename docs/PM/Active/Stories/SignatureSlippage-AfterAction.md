# Signature slippage: after-action report

The report the SignatureSlippage campaign owes (`docs/Designs/Active/Security/SignatureSlippage.md`, part E), in the
manner of the Rogers Commission on Challenger: what happened, when, why each check that touched the code missed it,
the organizational causes, the census of the shape across the tree, and the LESSONS row with the runner that refuses
the shape. Every fact below cites the depot, a published standard, or a run named with its kernel. A naive reader
barred from this report (R-NAIVE, 2026-10-07) reached causes 1, 2, 4 and 5 from the depot alone, and supplied the
design's line, the 25776 reject rows and the off-depot evidence, each read at source before it was added.

## The defect

`ed25519-verify` (`codex/foreword/core/Ed25519.codex`) accepts a small-order public key A with a small-order R, so an
unsigned CDX (zero key, zero signature) passes whenever the challenge hash h has h mod 4 = 3 (the campaign doc, part A).

## The causes, in order of weight

1. **The specification was the ceiling of the validation pass.** 25777 implemented RFC 8032's invalid-input list
   verbatim, and that list is written for interoperation; the RFC never mentions small order, and its preferred
   equation accepts the zero key for every message. 25790 did the same for RSA the same day and set no modulus floor.
   The project's own design said the same: `docs/Designs/Done/OS/CryptoPrimitives.md:52-54` defines correctness as
   matching the published vectors byte for byte, and `:259` specifies the cofactored check. (L-SPECCEILING.)
2. **The right suite was run, passed in full, and could not express the defect.** All 151 Wycheproof Ed25519 vectors
   carry 0 small-order keys and print 0 disagree against the pre-fix verifier; the suite's pass was read as the
   verifier's soundness (L-GAP). The run's evidence never entered the depot: 25776 cites
   `red/build-output/crypto-audit-20260917/`, which is in no depot path and no longer on disk, and only tc151 landed in a
   test, so the claim could not be re-run until red's arm carried the whole suite.
3. **The one audit of record framed the threat as timing, with the attacker already on the machine.** For a verifier
   the attacker supplies the key, the signature and the message; no document ever stated that threat model.
4. **No test fed the degenerate input**, so every test agreed (L-ORACLE, L-DEGENERATE). The 18 `reject` rows 25776 added
   to `ed25519-sign-test` each damage the honest `pk1` or `sig1`, so the equation refuses them with or without the new
   checks. The one test that did feed the zero key and signature, `cdx-chain` (expected "signature invalid" for an
   unsigned subject), passed because that subject's hash was not 3 mod 4, and it went red only when an unrelated
   compiler change moved the hash into the accepting class.
5. **Nothing pinned the signer.** The gates that call the verifier trusted the key in the artifact's own header, so
   even a sound verifier proved only "signed by some key" (part B, main 38219).
6. **Outside the crypto core the shape is the normal state of a trust decision**: the census below holds 17 rows read
   at the line (2026-10-07), most of them scaffolds that answer yes and were never closed, and nothing in the tree tells a scaffold that
   answers from a check that works (L-FALSIF, L-BAILVALUE).

## Timeline (depot, `p4 filelog -i //Codex/main/codex/foreword/core/Ed25519.codex`)

The file lived at `foreword/Ed25519.codex` (changes 541 to 617), `codex.foreword/Ed25519.codex` (702 to 1532) and
`codex/foreword/core/Ed25519.codex` (1630 on).

| change | date | lane | what the change says it did |
|---|---|---|---|
| 541 | 2026-04-30 | nib | adds SHA-256, SHA-512 and Ed25519 ("RFC 8032, GF(2^255-19)") |
| 557 | 2026-04-30 | nib | compile-error fixes across the foreword |
| 590, 591, 593 | 2026-05-01 | nib | arithmetic shift; 10-limb ref10 field arithmetic; a wrong base point T coordinate fixed |
| 702 | 2026-05-03 | cam | rename to `codex.foreword/` |
| 722 | 2026-05-03 | nib | Ed25519 sign/verify, CDX signing, CdxVerifier phases 1-3 |
| 770, 771, 772 | 2026-05-03 | nib | in-place field elements; `ge-from-bytes` copies its input before mutating byte 31 |
| 1013 | 2026-05-06 | nib | IrNegate folding across the compiler |
| 1064, 1095, 1098 | 2026-05-06 | cam | CPL prose rewrites, crypto batch included |
| 1139 | 2026-05-07 | nib | callers migrated to `bit-shru` |
| 1349 | 2026-05-12 | cam | "Ed25519 constant-time: fix 7 timing leaks, correct stale KNOWN-CONDITIONS claims" |
| 1532, 1630 | 2026-05-16, 05-18 | main | branch copy; repository restructure to `codex/foreword/core/` |
| 3767, 8452, 11522 | 2026-06-11 to 07-28 | fester, val, blu | whitespace, filetype, em-dash removal |
| 13568 | 2026-08-06 | blu | annotation extraction (prose blocks kept, annotated or deleted) |
| 25777 | 2026-09-17 | red | "proven Ed25519 verification input validation": rejects malformed lengths and octets, non-canonical S and invalid point encodings; "RFC8032 positives and 151 Wycheproof cases pass"; signed seed 6628BFC24E926940 |
| 25806 | 2026-09-17 | red | signing key binding, byte contracts and callers |
| 32290 | 2026-09-30 | blu | code-layout whitespace |

Public exposure (campaign doc): the public mirror from Update 30 (2026-07-01) through the Update 67 preview.

Reading of the table, to be tested in the sections below: the file had one validation pass of record (25777), which
added every input check except the one that failed, and one side-channel audit (1349). Every other touch was
mechanical (renames, whitespace, prose, filetype).

## Why the validation pass missed it: the suite it ran cannot express the defect (verified)

25777's description reports "RFC8032 positives and 151 Wycheproof cases pass". The published suite
(`C2SP/wycheproof` `testvectors_v1/ed25519_test.json`, SHA-256 `752D2EA7...84975536`, the file red's arm cites)
holds exactly 151 tests in 78 groups, so the count is the whole suite. The tree kept one of them: 25777's
`codex/test/apps/ed25519-sign-test.codex#4` line 52 carries tc151 alone, and red's `ed25519-degenerate` (main 38182)
now carries all 151. The whole suite passing proves nothing about this defect, because the suite cannot express it:

- 52 distinct public keys across the 78 groups; 0 of them is one of the 8 small-order encodings.
- 11 vectors carry a small-order R (tc10 to tc19, "special values for r and s", and tc60, "R==0"), every one under a
  prime-order key, and an equation-only verifier refuses those: the forgery needs a small-order A as well as R.
- The file's ten flags (`Valid`, `Ktv`, `InvalidKtv`, `InvalidSignature`, `InvalidEncoding`, `SignatureMalleability`,
  `SignatureWithGarbage`, `TruncatedSignature`, `CompressedSignature`, `TinkOverflow`) name no key-validation class.

Measured, not argued (fester 2026-10-07, kernel `279DF926`): `ed25519-degenerate` compiled over
`Ed25519.codex@38144` (the last pre-fix revision) and over head both print `wycheproof: 151 vectors, 88 valid, 63
invalid, 0 disagree`, while the same runs' 8 small-order A rows read `accepted` before and `refused` after, so the arm
can see the defect and the suite's line cannot. Running the standard adversarial suite in full would have caught nothing. L-GAP, verbatim: the
suite's silence was read as agreement without asking what the suite could express.

## Why 25777 stopped where it did: the RFC permits the input (verified)

RFC 8032 section 5.1.7 (Verify), quoted from `rfc-editor.org/rfc/rfc8032.txt`:

> 1. ... Decode the first half as a point R, and the second half as an integer S, in the range 0 <= s < L. Decode the
> public key A as point A'. If any of the decodings fail (including S being out of range), the signature is invalid.
> ...
> 3. Check the group equation [8][S]B = [8]R + [8][k]A'. It's sufficient, but not required, to instead check
> [S]B = R + [k]A'.

The RFC's whole text contains no "small order", "low order" or "torsion"; section 8.8 discusses the cofactor only as
agreement between implementations ("not strictly necessary for security"), and section 8.4 names the S < L check as the
malleability defence. 25777 implemented exactly that list: the octet and length checks, `ed-scalar-below-order`, and
`ed-decoded-point-valid` with the comment "RFC 8032 section 5.1.3: canonical y, a valid square root, and no sign bit for
x = 0". The verifier then ran the permitted cofactorless form, `ed-verify-equation`: `[S]B` against `R + [k]A` by
encoding (`Ed25519.codex@38144`). With A = R = an order-4 point and S = 0 that equation holds exactly when k is 3 mod 4,
which is the 1-in-4 class. The RFC's preferred cofactored form is worse: `[8]R` and `[8][k]A` are both the identity for
every small-order A and R, so a strictly conformant verifier accepts the zero key and zero signature for every message.

So 25777 was not careless. It was faithful to a specification whose invalid-input list was written for interoperation
(which encodings two implementations must both reject), and a trust decision needs a wider domain than interoperation
does. The specification was treated as the ceiling of the validation pass.

## The same shape, twice more in the campaign (verified)

- **RSA (part C, reek; fix main 38192).** `Rsa.codex`'s validation pass was 25790 (red, 2026-09-17, the same day as
  25777): "Reject S>=N, exponent1 and malformed ranges/octet inputs before arithmetic". Those are the checks PKCS#1
  itself states. A modulus floor is policy outside the RSA specification, and none was added, so a 512-bit key verified
  until 38192 set `rsa-min-bits` at 2048. The pass was again bounded by the specification it implemented.
- **MarketAuth (part D, val; fixed main 38210 with `pbkdf-verify`).** `verify-password` (`apps/market/MarketAuth.codex:16`) is
  `hash-password password salt == stored-hash`: a correct equation, a text compare that stops at the first differing
  character, and one HMAC keyed by the store's merchant id. It was written by fester in 3544 (2026-06-08), seven weeks
  before the right primitive existed (`Pbkdf.codex`, val 10772, 2026-07-27). `GameAccounts` and `VaultCrypto` adopted
  `pbkdf-verify`; nothing migrated MarketAuth until this campaign, and no register row asked for it.

Reek's full Wycheproof runs (main 38206) show the same ceiling from the other side: every case of the ECDSA P-256,
RSA-2048 and X25519 suites agrees with its verdict at head, and the RSA suite is 2048-bit by its own name, so it could
not have carried the 512-bit key 38192 now refuses.

In all three the check is right as an equation and silent about the conditions that make the equation a security
decision: the domain of its inputs, the strength of its key, the way it compares.

## The one audit of record framed the risk as timing (verified)

1349 (cam, 2026-05-12, "Ed25519 constant-time: fix 7 timing leaks, correct stale KNOWN-CONDITIONS claims") removed this
entry from `docs/Test/KNOWN-CONDITIONS.md` (#5 to #6):

> ### Ed25519 is not constant-time -- KNOWN LIMITATION
> ... Fine for the trust lattice use case where the attacker would already need bare-metal access. Not suitable for
> network-facing signing where timing side channels are exploitable.

The only security statement the project ever recorded about this file was about side channels in signing, with a
threat model in which the attacker already holds the machine. The audit retired that statement and touched no input
domain. For a verifier the attacker is whoever supplies the key, the signature and the message, and a CDX's key and
signature arrive in the file being verified.
## The census of the shape across the tree (2026-10-07)

Taken by behaviour, not by name (L-CENSUS): a keyword grep for hashes, tokens and tags compared with `==` returned codec
tags, settings cells and media types, and a census by `-> Boolean` name returned 567 functions in 286 files. Three
read-only readers took the regions `codex/foreword` + `codex/os` + `codex/product`, `apps/` less games, and
`apps/uoaix` + `apps/games` + `apps/works`, each listing every trust decision it examined and found clean, and
classing the rest as S1 (answers yes on a degenerate input: empty, zero, absent, threshold 0, an all-of over nothing),
S2 (a secret or tag compared by early-exit `==` or over a prefix) or S3 (a bail that answers yes). Every row below was
then read at the cited line by fester; a reader's claim not read there is not in this table.

| reach | file:line | shape | what answers yes |
|---|---|---|---|
| network | `apps/games/codexmagic/MagicServer.codex:1394`, `:1411`, `:1480` | S1, S2 | every session token is `"s" & show next-nonce`; register and login never advance `next-nonce` (only `:715`, `:726`, `:865`, `:1747` do), the first account is admin (`:1387`, `:1393`), and lookup is Text `==`: on a fresh server the admin's token is `s1000`. `acct.password /= pw` (`:1410`) on a plain-text password |
| network | `apps/games/codexmagic/ClanServer.codex:869` | S1 | `handle-authorize` reads `token` and never checks it before approving the clan |
| network | `apps/games/codexmagic/Auth.codex:60` | S1 | `verify-signature` never reads `pubkey`; the expected value is a hash of the public nonce and account id |
| network | `apps/games/codexmagic/TransactionValidator.codex:31`, `:48` | S1 | Signature FIXED (val): Ed25519 over a domain-tagged encoding of every field, verified against the sender's key registered once in `ChainCore` (`register-account-key`, a second key refused); an unregistered sender is refused. On the old code a reward signed with key 0 by anyone validated (control on the depot validator: "accepted"). `codex/test/edge-mesh-mint` grades the unregistered, tampered-amount, all-zero-key and re-registration arms. Nonce FIXED (val): a transaction's nonce must exceed the sender's last, mined or pending; mining records it on the sender, and a balance change no longer moves it. On the old check a mined reward replayed and a lower nonce were both accepted; `edge-mesh-mint` grades both replays, the next nonce and a stale one |
| a loaded binary | `codex/os/net/HttpFetch.codex:296`, `:283-284` | S1 | an empty network grant admits every URL; an empty port on either side admits every port |
| a loaded binary | `codex/foreword/core/Fat16.codex:1818-1819` | S1, S2 | an empty file grant admits every path; a grant admits by bare text prefix, so `/apps/a` admits `/apps/ab` |
| outsider file | `apps/works/AgentBundle.codex:153` | S1 | the key is read from the manifest being verified, and nothing compares it with a pinned key: any self-signed bundle passes (part B's caller list holds this file) |
| local keyboard | `apps/services/accounts/ManagedAccounts.codex:267` | S1 | `verify-pin` answers True for an empty PIN hash; the guardian account is created with `acc-pin-hash = []` (`:187`, reloaded empty by `ServicesPersist.codex:221`) and nothing sets one, so `ParentalUI.codex:247` opens the guardian panel to anyone |
| local keyboard | `apps/secrets/Vault.codex:55`, `SecretsApp.codex:109` | S1 | `vault-unlock` derives a key from whatever was typed and the app sets "Unlocked" without a check; `verify-master-password` (`VaultCrypto.codex:89`) is called only by a test |
| handshake config | `codex/foreword/core/ProofOfWork.codex:27` | S1 | difficulty 0 or below passes every hash |
| peer | `codex/os/net/RaftConsensus.codex:161-163` | S1 | every granted vote response is counted, with no voter identity, so one peer can make a quorum |
| none today | `codex/os/verify/VerifyCache.codex:54`, `:82` | S1 | the cache key is the content hash the binary claims in its own header (`cdx-read-content-hash`), so a forged header returns a cached acceptance; only a test calls it |
| none today | `codex/foreword/core/OtaUpdate.codex:324-330` | S1 | `gate-b-run-all` answers `GateBPass` for any 136 bytes starting with the magic and never reads `trusted-pubkey` |
| none today | `codex/os/net/Accounts.codex:64`, `:109` | S2 | session token and password hash compared by Text `==`; nothing calls `auth-serve` |
| none today | `apps/browser/PageFetcher.codex:264` | S1 | no publisher header answers True |
| none today | `apps/data/TwoPhaseCommit.codex:124`, `codex/os/trust/PolicyEngine.codex:62` | S1 | an all-of over an empty vote or condition list answers True |
| account owner | `apps/uoaix/GameSession.codex:245` | S1, S2 | FIXED (val, 38332): 0x83 now verifies the bound account's password through `ga-verify-either` (PBKDF2, full-length compare; an empty password is refused), and creation no longer stores the character password; records saved earlier keep their plaintext bytes until the slot is rewritten. `proofs/GameDeleteProof` reads 6 of 6 FAIL on the old code |

Reported by the readers and not yet read at the line by fester (the owning lane verifies before acting): the
responder marked authenticated in `codex/os/trust/TrustTransport.codex:143`; `ExternalAuthBridge.codex:66` and
`Jwt.codex:104-116` (no signature or claim check; empty expected issuer passes); `FactSync.codex:213` storing peer
facts under the peer's hash; `Revocation.codex:241` trusting the signer's own `signed-at`; `BrowserPersist.codex:126`
mapping an unknown tier to `TierSystem`; `MeetingManager.codex:53`, `DbAdmin.codex:70`, `Security.codex:119`,
`modbuilder/native/Helper.codex:256`, `Lorawan.codex:178`, `LeaseManager.codex:73`.

What the census says about the slippage: the crypto core was the best-defended code in the tree and still carried the
shape twice (Ed25519, RSA). Outside it the shape is the normal state of a trust decision. Most of the rows are
scaffolds that answer yes (a hash of public fields standing in for a signature, an unread token, an unused verifier)
and were never closed, because nothing distinguishes a scaffold that answers from a check that works (L-FALSIF).

## The runner (2026-10-07)

`build/checks/degenerate-arms.ps1`, registry `build/checks/degenerate-arms.json`. Discovery is by structure: every
function in a `codex/foreword` chapter that is a crypto root (the 18 roots in the registry, taken from the tree's
chapter list) or cites one, whose result is Boolean or Maybe and which another chapter calls. Each must have a row: an
`arm` (a test that names the function and whose `.expected` carries every listed class line), a `waive` with its reason,
or an `open` naming the campaign part that owns the gap. Exit 1 on an unregistered function, a stale row, an arm that
never names its function, or a missing line; `open` rows print. At head: 63 trust decisions in 37 chapters, 10 armed
(98 class lines), 31 waived, 22 open, exit 0. Ablations, each exit 1: a class line the arm does not print
(`h = 4 mod 4`), the `rsa-verify-pkcs1-sha384` row removed (a new verifier with no row), a stale row and a Pbkdf arm
pointed at a test that never calls it. Limits: a trust decision outside `codex/foreword`, or answering another type, is
not discovered (every census row above is outside it). The release gate `build.ps1` runs it after the sidecar check and fails on its exit 1 (main, from 2026-10-07).

## The verifier's input checks by revision (verified)

A search of `ed25519-verify`'s chapter at three revisions for any input-domain check (small, order, torsion,
cofactor, identity, canonical) finds:

- 722 (2026-05-03, verify written; `codex.foreword/Ed25519.codex`, 1094 lines): none.
- 1349 (2026-05-12, the constant-time audit; 1194 lines): none. The audit fixed seven timing leaks and touched
  no input domain.
- 25777 (2026-09-17, the validation pass; `codex/foreword/core/Ed25519.codex`, 629 lines): S below the group order
  (`ed-scalar-below-order`) and the RFC 8032 section 5.1.3 point decoding (canonical y, a valid square root); no check
  that A or R is outside the small-order subgroup.

So for 137 days (722 to 25777) the verifier checked only its equation; the one pass that added domain checks added the
decoding and S checks and none for small order.

## What a forgery bought

The campaign doc's part B table (blu) is the account, one row per caller. The defect forges only under a key the forger
chooses, so it bought exactly what a fresh honest key buys wherever a caller verifies under a key the signed object
supplies, and nothing where the key is pinned. The finding behind it is worse than the defect: the seed gate, the
desk's seed check and the chain loader verified a CDX under the key in its own header, so "signed and self-verified"
never meant "signed by the depot's signer" (part B, fixed main 38219 by pinning `seed/signer.pub`).

## Open

- The census's unverified list, and per-finding rows for the verified table, are blu's (root, 2026-10-07).
