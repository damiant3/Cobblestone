# Foreword Core -- open capabilities

Quire-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.


## The shared mixer

`Foreword chapter Random` supplies `mix-bits` and `rand-in-range` (CL 10493),
and its prose carries the whole account: why an unfolded multiply-add has no
usable low bits, why that is invisible to a balance check, and -- the part
that matters most -- **why it is only a defect where the consumer reads low
bits.** A remainder by a power of two or a bit-and is degenerate; a remainder
by a large non-power-of-two is often fine. Steering was recorded here as
broken on the strength of its expression, measured, and found fine.

**Read that prose before migrating anything, and measure the consumer rather
than grading the mixer.** `codex/test/mix-bits.codex` is the worked example
and keeps the old mixer as a live negative control.

**Do not touch `ElasticHash`, `FunnelHash`, `Lz4`, `Steering`, `ImageTensor`
or `bloom-hash-text`.** The first two already run a full Murmur-style
finalizer, Lz4 takes the HIGH bits, Steering's modulus saves it, and
ImageTensor's mask is its LCG modulus while the output comes from a division
that reads the high bits. `bloom-hash-text` ends on a fold and measures 7 and
2 false positives per 500 at 1024 and 1021 bits, which is healthy; it is
pinned by `codex/test/bloom-spread` alongside the integer path. All measured.
They are named so nobody re-audits them.

**Grading a mixer by reading it was wrong again, and this time in the other
direction.** CORE-3 was recorded here as "duplication rather than defect,"
because both `chr-hash-key` and `chr-hash-pair` ended on a fold. Running the
consumer showed a ring that sent 993 of 1000 keys to one node: the two hashes
were on different SCALES, so every key hashed past every vnode and wrapped to
the first entry. The fold was never the question. Of six chapters graded from
their expressions, two were falsely accused and one was falsely cleared.

The rule that came out of that, and which outlives the rows it was learned
from: **a grade read off an expression is a HYPOTHESIS until its consumer is
run.** Four chapters were graded broken from their expressions and two of
them (Steering, ImageTensor) measured fine. Migrating a chapter CHANGES ITS
OUTPUT, so each is its own changelist with its own re-recorded expectations.

## Open

**CORE-10. SECURITY: every published release verifies an unsigned image one
time in four.** `ed25519-verify` (`Ed25519.codex`, since CL 1532, 2026-05-16)
never refused a small-order A or R. The all-zero key and signature decode to
one point of order 4 with S = 0, and the equation then holds whenever h is 3
mod 4, so an unsigned CDX passes for one content hash in four. Every public
release through Update 66 carries it. Head refuses A and R whose [8]P is the
identity; `codex/test/apps/ed25519-small-order` grades all four h classes and a
non-canonical S, and the depot verifier accepts its h = 3 case.

Callers, all exposed until the fix ships: boot and seed self-verify
(`apps/works/SeedVerify`, `GopWake`, `codex/os/verify/WakeCeremony` via
`GopBoot`, `build/test-self-verify.ps1` behind the seed path); image load and
inspection (`codex/os/verify/CdxBinary` `cdx-verify-signature` and so
`CdxVerifier` and `VerifyReport`, `apps/works/CdxChain`, `CdxInspector`,
`AgentBundle`); the repository and fact store (`codex/foreword/core/ImportGate`
and so `FactDisk`, `apps/works/RepoProtocol`, `RepoProtocolPersist`,
`FactArchive`); keys and products (`apps/works/KeyManager`, `GopWizard`,
`codex/product/ProductAuthorization`, `ProductNotary`,
`apps/services/Revocation`); peers and transport (`codex/os/trust/Handshake`,
`apps/fileshare/PeerDiscovery`, `codex/foreword/encode/TlsCert`, `X509Chain`);
the browser (`apps/browser/ContentAddress`, `PageFetcher`, `TrustManager`).

OTA does not reach the verifier at all: `OtaUpdate.codex` `gate-b-run-all`,
named "full 5-phase verification", checks the magic and a 136-byte minimum and
ignores its `trusted-pubkey`. Open: the advisory for releases through Update 66
(Damian's call, via root), and Gate B performing the signature check its name
claims. Owner: red.

**CORE-9. We can READ a certificate and cannot WRITE one, so we cannot stand
in for ourselves: no self-signed certificate, and no CA of our own.** Ruled
2026-09-08 by Damian: self-signing is where this goes, and the stand-in is
needed now.

**The CA policy is DEFERRED by Damian 2026-09-30** (naming, revocation, key
location): the current state is tolerable. The three questions and root's
recommendations are in `docs/PM/Active/DamianDecisions.md` 5.1.

**Measured 2026-09-08.** `Asn1.codex` is a pure reader: `asn1-read`,
`asn1-children`, `asn1-content`, `asn1-oid-is`, `asn1-bit-string`,
`asn1-small-int`. Every `x509-*` name in `X509.codex` and `X509Chain.codex`
parses or verifies, `x509-signed-by` included. There is **no `asn1-encode`, no
DER writer and no `x509-build` anywhere in the tree**, searched across `codex`
and `apps` outside `build-output`. The tag constants exist already
(`asn1-sequence`, `asn1-oid`, `asn1-utc-time` and the rest); what is absent is
the write side.

So the certificates we hold come from OUTSIDE: `build/mint-tls-fixtures.ps1`
shells out to `openssl.exe` under Git for Windows. That is acceptable for
checked-in test fixtures and is not a story for a running server, and it sits
against the founding rule that what we did not build we do not trust.

**Three steps; the first two are DONE and the third is half built.**

1. **A DER encoder. LANDED** (red 2026-09-08), `codex/foreword/encode/Asn1Write.codex`:
   length in short and both long forms, SEQUENCE, SET and the context tags,
   INTEGER by value and by caller-supplied content, BOOLEAN, NULL, OCTET
   STRING, BIT STRING, OID and the two time types. Graded by
   `codex/test/asn1-der-write.codex`, 16 arms, from two directions that share
   no code: a round trip through our own parser, and a byte comparison against
   the certificate RFC 8410 section 10.2 publishes, whose encodings the IETF
   produced. `openssl` was not needed for the byte oracle because that
   published certificate carries all three length forms itself (5, 223 and
   300), which is a third-party encoding of the exact question. **NOT
   seed-affecting, proven by the binary:** the compiler built with the chapter
   present matches the depot seed at every offset outside 40..135, because
   nothing in the compiler's cite closure cites it (Rulebook rule 7). In the
   BVT beside `x509-parse`.
2. **A self-signed server certificate. LANDED** (red 2026-09-23),
   `x509-dev-cert-ed25519` in `codex/foreword/encode/X509Write.codex`: Ed25519
   from a 32-byte private key, subject O=`Codex DEV-ONLY self-signed` equal to
   the issuer, basicConstraints CA:FALSE critical, SAN dNSName and IPv4, times
   chosen UTCTime or GeneralizedTime by RFC 5280's year rule, and every bad
   input refused as None. `x509-dev-cert-p256` is the same certificate over a
   P-256 key (id-ecPublicKey prime256v1, ecdsa-with-SHA256, RFC 5480 and 5758),
   because no browser offers Ed25519. Graded by `codex/test/x509-dev-cert.codex`,
   both keys, 28 lines (2026-09-29): our parser, the pinned-anchor verdicts (ok, wrong identity, not yet valid,
   expired, tampered, other key) and a TLS 1.3 handshake authenticating against
   the minted leaf, with a wrong-pin control. Graded from outside by
   `codex/plugs/elf/hosted-https-arm.ps1` (`-Key ed25519` or `-Key p256`):
   OpenSSL 3 reads the certificate as that key type and `openssl verify
   -partial_chain -check_ss_sig` answers OK (a flipped signature byte answers
   error 7), then the hosted server answers it over TLS 1.3; under `-Key p256`
   the client offers only ecdsa_secp256r1_sha256. The key comes from the
   caller; `codex/test/hosted-https.codex` and `hosted-https-p256.codex` draw
   it from `hardware-random` at start. In the BVT beside `x509-parse`.
3. **A CA of our own.** **Issuance LANDED** (val 2026-09-28), both in
   `X509Write.codex`: `x509-ca-cert-ed25519` mints a self-signed root
   (O=`Codex DEV-ONLY CA`, basicConstraints CA:TRUE pathlen 0 and keyUsage
   keyCertSign, both critical, subjectKeyIdentifier by RFC 7093 method 1), and
   `x509-issue-ed25519` issues a leaf under it (O=`Codex DEV-ONLY issued`,
   issuer copied as raw DER from the parsed CA subject, CA:FALSE, SAN, an
   authorityKeyIdentifier equal to the CA's SKI), refusing a CA certificate
   that does not parse, is not a CA, or carries a key other than the signing
   key's, or a leaf whose notAfter is later than the CA's. Nested validity is
   stricter than RFC 5280 and web chains break it, so the general walk
   (`x509-verify-peer`, which TLS and DTLS call) does not check it;
   `x509-verify-peer-nested` adds it for a chain we issued or pin, answering
   `ChainOutlivesIssuer` for any certificate whose notAfter is later than its
   issuer certificate's. An anchor carries no validity, so a leaf sent
   without its CA is not checked against the CA's window. Graded by
   `codex/test/x509-ca-issue.codex` (in the BVT): the leaf
   walks to the CA anchor with and without the CA sent; a same-name anchor
   with the wrong key, a rogue-issued leaf, a tampered leaf and a non-CA
   issuer each refuse; a TLS 1.3 handshake authenticates the issued leaf; a
   sabotage of the two issuance guards moves exactly their two refusals, and sabotage of each notAfter guard moves exactly its own line.
   Graded once by hand from outside: OpenSSL 3.2.4 `verify -x509_strict
   -check_ss_sig -purpose sslserver -verify_hostname localhost` answers OK,
   a flipped leaf signature byte error 7, `evil.test` error 62.
   **Still open, each a policy ruling for Damian (val's CurrentPlan row):** a
   naming policy beyond the DEV-ONLY markers, revocation (no CRL or OCSP
   writer, and `X509Chain` checks none), and where a CA private key lives.
   **A self-signed root does not make a browser
   trust us**, so a public endpoint still needs a CA-issued chain whatever we
   build here.

**Not in scope and worth saying:** the seed signing key is NOT the server key.
The two attest different things and have opposite exposure profiles, a signing
key being used rarely in one place and a server key sitting in a
network-facing process on every host. A server key is generated per
deployment.

**CORE-11. PBKDF2 is too slow on codex-vm for the current iteration guidance.**
`pbkdf-hash` costs about 23 us per iteration on codex-vm (val 2026-10-07: four
hashes at 20000 iterations and two at 100000, timed against a 1-iteration
control). 600000 iterations, the current guidance for PBKDF2-HMAC-SHA256,
would take about 14 s per verify. Every caller (`apps/market/MarketAuth`,
`apps/uoaix/GameAccounts`) verifies on a single request loop, and both therefore
stay at 20000. Each iteration runs `pb-prf`: two list-based `sha256` calls
over a fresh concatenation and a word-to-byte conversion. A word-level PRF
over the precomputed ipad and opad states is the open fix.

**CORE-12. SHA-1, SHA-256 and SHA-512 alias a non-octet element instead of
refusing it.** `bytes-to-words` (`Sha256.codex`) and its siblings in
`Sha512.codex` and `Sha1.codex` OR each shifted element into the block word
unmasked, so `sha256 [0, 0, 0, 256]` equals `sha256 [0, 0, 1, 0]` and
`[0, 0, 0, -1]` equals `[255, 255, 255, 255]` (val probe, seed 279DF926).
Text callers cannot reach it (`text-to-bytes` yields CCE bytes); a caller
passing a raw Integer list is unaudited. Open: a census of raw-list callers,
and a refusal of non-octet input the way `pbkdf-hash` refuses it.

## CCE has no Dingbats block

U+2700..U+27BF (Dingbats) is in neither CCE tier, so a character from it
reaches Codex text as `?`. Measured 2026-09-30 by `codex/test/apps/spark-audio`:
the WPF Spark's `sfx_config.json` labels its 15 Magic presets with U+2728
(sparkles), and every other character in that file and in `music_config.json`
(Latin-1 letters, U+2013, U+266D, U+266F, emoji) survives. The oracle
`build/spark-audio-oracle.ps1` applies the same substitution by name, so the
test stays green on the rest and turns red the day the block is added.
