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

**CORE-9. We can READ a certificate and cannot WRITE one, so we cannot stand
in for ourselves: no self-signed certificate, and no CA of our own.** Ruled
2026-09-08 by Damian: self-signing is where this goes, and the stand-in is
needed now.

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

**Three steps; the first two are DONE.**

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
   input refused as None. Graded by `codex/test/x509-dev-cert.codex`, 18 lines:
   our parser, the pinned-anchor verdicts (ok, wrong identity, not yet valid,
   expired, tampered, other key) and a TLS 1.3 handshake authenticating against
   the minted leaf, with a wrong-pin control. Graded once by hand from outside:
   OpenSSL 3 (Git for Windows) prints every field as intended and `openssl
   verify -partial_chain -check_ss_sig` answers OK, a flipped signature byte
   answers error 7 and `-verify_hostname evil.test` error 62. The key comes
   from the caller; `codex/test/hosted-https.codex` draws it from
   `hardware-random` at start. In the BVT beside `x509-parse`.
3. **A CA of our own**, which is the larger commitment and not implied by the
   first two: issuance, a naming policy, validity windows, and revocation.
   **A self-signed certificate does not make a browser trust us**, so a public
   endpoint still needs a CA-issued chain whatever we build here.

**Not in scope and worth saying:** the seed signing key is NOT the server key.
The two attest different things and have opposite exposure profiles, a signing
key being used rarely in one place and a server key sitting in a
network-facing process on every host. A server key is generated per
deployment.

**CORE-8. CCE cannot represent tab, carriage return, backspace or formfeed,
and every caller that asks for one is silently handed the character y-diaeresis
instead.** `from-unicode` answers -1 for Unicode 8, 9, 12 and 13, which is
correct and deliberate: CCE tier 0 carries LF at 1 and SPACE at 2 and nothing
else in that region (`to-unicode 1` is 10, `to-unicode 2` is 32). The defect is
what happens next. `char-encode` and `char-to-text` both accept the -1 without
complaint and emit a single unit 255, and `cce-encode-length (-1)` answers 1
rather than refusing, so the sentinel flows all the way into a Text.

**MEASURED end to end through the public parser, seed CA7B018E:**

| input | units out |
|---|---|
| `"a\\rb"` | 15 **255** 32 |
| `"a\\tb"` | 15 **255** 32 |
| `"a\\bb"` | 15 **255** 32 |
| `"a\\nb"` | 15 1 32 (correct) |
| `"ab"` | 15 32 |

**THE VALUE IS NOT GARBAGE, WHICH IS WHY NOTHING CAUGHT IT.** `to-unicode 255`
is 255, so 255 is a perfectly legal CCE character and renders as a letter. A
reader sees a plausible character rather than a corruption, and three distinct
escapes collapse onto one indistinguishable value -- the same lossiness shape
COMPILER-23 defect B has, one layer up.

`\\n` is right only because `Json.codex:450` uses a literal `"\\n"` instead of
routing through `from-unicode`. The four broken arms are `Json.codex:448, 449,
451, 452`, and `read-unicode-escape` at 461 has the same hole for any `\\uXXXX`
CCE does not map.

**THE DECISION IS NOT MINE AND IS NOT A PATCH.** RFC 8259 requires a conforming
parser to accept `\\b \\f \\r \\t`, and CCE deliberately has no code point for
any of them, so the three available answers are: refuse the escape
(`string-fail`, honest but non-conforming), substitute something chosen and
documented rather than accidental, or give CCE those code points. Whichever is
taken, the encoders must stop accepting -1 silently: that is the part with no
argument on either side.

Found 2026-08-25 while migrating the encode-meaning callers to `char-encode`
(main 19636). It is PRE-EXISTING and the migration neither caused nor fixed it;
`char-to-text (-1)` produced the same 255. It surfaced because Damian pushed
back on "nothing depended on the truncation" -- a dependency on a sentinel
being silently encoded is exactly the dependency that should fail hard.

**RULED AND HALF FIXED, main 19662. This row read as an open undecided question
until 2026-08-26 and the decision had been taken and shipped the day before.**
Damian's ruling: *"we do not conform to a standard that codifies stupidity; do
what is best for us on input and output and preserve the original intent as far
as it can be preserved."* So none of the three answers this row offered was
taken whole. Split by intent against machinery: **tab** means horizontal
whitespace, which CCE can express, so it becomes a space; **backspace,
formfeed and carriage return** are teletype and line-printer machinery with no
modern meaning and are DROPPED, and mapping CR to a newline was rejected
because it doubles every CRLF, which is the common case and the worse trade;
an **unmappable `\uXXXX` is CONTENT rather than machinery and is REFUSED**,
because silently losing what an author wrote is the failure being removed and
CCE has no replacement character to substitute.

**WHAT IS STILL OPEN IS THE HALF THE ROW ITSELF CALLED UNARGUABLE: THE
ENCODERS STILL TAKE -1.** Re-measured 2026-09-08 against seed
`23AA66C50A56E628`: `cce-encode-length (-1)` still answers 1, because its
first test is `if cp < 128 then 1` and -1 satisfies it (`CCE.codex:128`). The
JSON arms were repaired at main 19662; the floor underneath them was not. Any
OTHER caller handing a -1 to an encoder still gets unit 255, and the callers
are not hypothetical: `Gguf.codex:87`, `PngMetadata.codex:133` and `:152`,
`SafeTensors.codex:174`, `Fat16.codex:1128` and `:1254` all spell
`char-encode (code-to-char (from-unicode ...))` over bytes read from FOREIGN
files, with nothing between the -1 and the encoder.

**A SECOND INSTANCE, found 2026-09-08 in a different subsystem, which is the
argument for fixing the floor rather than each caller.** Prism's in-tab
template compile refused with CDX3007 and nothing naming the reason. Cause:
the tree is CRLF, `from-unicode 13` answers -1, so a RESOLVE frame did not end
with the unit text and `resolveUnit` reported the library unused. Fixed at the
emit point (`codex/plugs/wasm/build-page.ps1`) and on the page's other four
routes (PRISM-8, main 23198 and following), because the page normalised
nowhere. That repair is correct where it sits and does NOT close this row: it
is the same -1 reaching a different consumer, and the next consumer will pay
again. Making the encoders REFUSE -1 is what stops the class.

**WHAT IS STILL OPEN IS THE RESIDUE, AND IT IS THE HALF THIS ROW CALLED "the
part with no argument on either side": the encoders still accept -1 silently
everywhere OUTSIDE Json.** `char-encode`, `char-to-text` and
`cce-encode-length (-1)` are unchanged, so any non-JSON caller that builds a
Char from a Unicode code point can still put unit 255 into a Text with no
diagnostic.

**AND THE OBVIOUS REPAIR IS RULED OUT, WHICH IS WHY THIS IS NOT A ONE-LINE
FIX.** Refusing -1 inside `char-encode` traps `HttpClient` on any response body
containing a carriage return, which is most of them, so **the policy belongs at
the CALL SITES rather than in the encoder**.

**The census at head (reek, seed `CC7DD4559232C558`, 2026-09-24).**
`from-unicode` answers -1 for exactly 31 values in 0..255: 1-9, 11-31 and 127.
NUL, LF and all of 128..255 map. So a site hands -1 to the encoder only by
letting a control byte or DEL through, or by passing a value above 255. The 37
source sites spelling `code-to-char (from-unicode ...)` (generated copies under
`build-output` and `apps/landing/web` excluded):

| class | sites |
|---|---|
| safe by construction | `Fat16.codex:1420`, `Uri.codex:241`, `Base64.codex:56`, `GopFiles.codex:103`, `GopFont.codex:65`, `ParentalUI.codex:280`, two test constants (233); `compiler/opening.codex:1708` (unpacks phase labels, all thirteen literal uppercase ASCII passed to `phase-measure`); `Fat16.codex:1254` (`fat16-lfn-take` refuses the name unless `fat16-lfn-units-decodable` maps every unit) |
| desk bytes, CLOSED: DEL shown as a control | `GopConsole.codex:49`, `GopEdit.codex:123`, `GopFiles.codex:233`, `GopReview.codex:82` |
| foreign bytes, CLOSED: each goes through `cce-foreign-byte-text` (`CCE.codex`), tab to space and every other unmapped byte dropped, pinned by `codex/test/cce-foreign-byte` | `Gguf.codex:87`, `PngMetadata.codex:133` and `:152`, `SafeTensors.codex:174`, `Fat16.codex:1128`, `Base64.codex:139`, `HttpClient.codex:81` and `:92`, `ExplorerStore.codex:55`, `AgentBundle.codex:45`, `GopEdit.codex:532`, `GopEdit.codex:692`, `Accounts.codex:176`, `SpirvBinary.codex:566`, `codex/test/apps/usb-bot.codex:39`; the percent-decoders `ExplorerServer.codex:61` and `IdeaEngine.codex:31` likewise, pinned by `codex/test/apps/core8-content-sites` |
| JSON escapes, CLOSED: `AccpJson.codex` `aj-decode` takes `Json.codex`'s ruling, `\t` a space, `\b \f \r` dropped, an unmappable `\u` refusing the whole string (`""`, its failure value), pinned by `codex/test/apps/core8-content-sites` | `AccpJson.codex:194` |
| key handlers, CLOSED: an `EvKeyDown` key is what `poll-key` answers, a character as its CCE code and Enter, Backspace, Escape and the rest as named keys at `key-named-base` (R-CCE); each handler tests `key-enter`, `key-escape`, `key-backspace` and `key-is-char` (`KeyInput.codex`) and encodes the code directly, pinned by `codex/test/apps/key-contract` | `Diagram.codex:319`, `helm/opening.codex:157`, `SecretsApp.codex:100`, `Browser.codex:839`, `collab/opening.codex:223`. Before, Enter arrived as `key-enter`, never equalled 13 and inserted unit 255, and a lowercase letter was dropped; the Secrets lock screen commits a passphrase only on Enter, so no vault was ever keyed from the keyboard. `efi-key-decode` answered raw Unicode until main 27603; `poll-key` is CCE on both paths |
| InputSource, CLOSED: key-down goes through `poll-key-decode` (the same modifier latch as `poll-key`) and key-up through `apply-mods` and `key-emit`, so a key reports one CCE identity on both edges, pinned by `codex/test/input-metal` (scan code 35 reads CCE 20, `h`) | `InputSource.codex` `ri-key-events` |

A compile-time backstop in the declared domain is possible: COMPILER-28 is
fixed and CDX2054 makes a range on a non-integer base a refusal instead of
decoration. Owner: blu, 2026-08-26.
