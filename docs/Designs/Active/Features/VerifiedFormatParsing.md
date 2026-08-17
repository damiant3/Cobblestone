# Verified Format Parsing -- descriptions that generate parsers

**Status:** DESIGN. Not built. Stage 0 is worth doing on its own and
needs none of the rest.

**Author:** AgentGrid session, 2026-08-02, at Damian's request, after
reading EverParse (Project Everest / Microsoft Research).

**Ruling 2026-08-05 (Damian): stage 0 is APPROVED as schedulable background work.** It needs no design approval and is not seed-affecting; any lane with slack may take it. Stages 1-4 remain proposal.

**2026-08-15: this design owns Track D of `docs/PM/CurrentPlan.md` ("bytes we
did not produce"), and stage 0 now has a CENSUS, section 10: 141 parsers, 43
with a negative arm, 52 KAT-only, 19 with no test on the parse. Section 10.1
is the work queue, in priority order. Take from it; do not fix the nearest
parser.**

---

## 1. The exposure

`codex/foreword/encode/` holds **75 format modules**, every one of them
hand-written, and the evidence behind them is byte-exact known-answer
tests against an independent reference encoder.

A known-answer test is strong evidence about **well-formed input**. It is
not evidence about anything else, and malformed input is the entire
threat model of a parser. Nothing in the tree currently feeds these
modules a hostile byte string.

The fuzz corpus does not cover this. Its 44 adversarial inputs (binary
garbage, huge identifiers, deep nesting, unclosed syntax, 100 KB lines,
recursive types, keyword abuse) target the **compiler's source parsing**.
The wire-format surface has no adversarial corpus at all.

The exposure is not evenly spread. Sorted by what a bad parse buys an
attacker:

| Class | Modules | What a bad parse yields |
|---|---|---|
| Trust decisions | `Asn1`, `X509`, `X509Chain`, `TlsCert`, `TrustAnchors`, `Jwt` | a forged identity accepted |
| Loading authority | `codex/os/verify/CdxBinary`, `CdxVerifier`, `VerifiedLoader` | code executed, capabilities granted |
| Session establishment | `Dtls`, `DtlsHandshake`, `DtlsHello`, `DtlsMessage`, `WebSocket` | a hijacked or downgraded channel |
| Device and industrial | `Modbus`, `Dnp3`, `Bacnet`, `Knx`, `J1939`, `Goose`, ... | actuation from an unauthenticated frame |
| Media and documents | `Png`, `Jpeg`, `Mp4`, `TrueType`, `Gltf`, ... | memory and CPU from untrusted files |

ASN.1 and X.509 are the most CVE-productive parsing surface in the
industry's history. We have hand-written implementations of both, and
they feed `TrustAnchors`.

## 2. Prior art: EverParse, and why we cannot use it

EverParse (fstar-lang.org, project-everest) generates verified zero-copy
parsers from declarative format descriptions:

- **LowParse** -- "a verified library of parsing and formatting
  combinators programmed and verified in F* and Pulse".
- **3D** -- descriptions "in a style resembling type definitions in the C
  programming language, but with data dependencies"; emits validators for
  low-level C.
- **QuackyDucky** -- RFC-style descriptions; used for TLS and QUIC.
- **EverCBOR / EverCDDL** -- CBOR and CDDL, emitting C and Rust.

Deployed in Windows Hyper-V, reportedly handling every network packet
through Azure, plus TLS, QUIC and COSE signing.

**Honest sourcing:** the docs site and repo README describe "provably
secure, zero-copy parsers" but do NOT enumerate the proven properties.
The named paper is *Provably Correct Non-Malleable Parsing*. The fuller
list usually attributed to it -- memory safety, arithmetic safety,
double-fetch freedom, round-trip correctness -- comes from the papers and
was not verified against primary sources here. Read the PLDI paper before
relying on any specific claim.

We cannot adopt it: it is F* emitting C and Rust, and "if we didn't build
it, we don't trust it". This document is about rebuilding the idea, and
only the part of the idea we actually need.

## 3. What our type system already covers, and what it does not

Much of what motivates 3D is C-specific and does not apply here. A
`.codex` parser has no raw pointers, its integers carry declared ranges
with an overflow mode, resources are linear, and effects are declared.
The buffer overflow that 3D exists to prevent is not reachable the same
way -- **conditional on the compiler being right about its own bounds
checks**, which is the subject of `IndependentRechecker.md` and not
re-argued here.

What the type system does NOT give, at any strength:

- **Non-malleability.** That two distinct byte strings cannot parse to
  the same value, and that a value has exactly one encoding. This is a
  property of the format-and-parser pair, not of a type.
- **Parser/serializer agreement.** Two hand-written functions that are
  supposed to be inverses, and nothing checks that they are.
- **Termination on hostile input.** A length field that makes a loop run
  to four billion is a type-correct program.
- **Single source of truth for the layout.** Two copies of a constant
  cannot be type-checked against each other.

That last one has already cost us a near-miss, and it is written down in
`CdxBinary.codex` itself: the capability direction constants were a
second copy of the foreword's, matched "by inspection and by nothing
else", and because `cap-dir-bits` answers readwrite on its last branch, a
drifted triple "would not have raised anything -- the loader would have
expanded a read-only entry into read AND write and granted an authority
the binary never asked for". A parser derived from one description cannot
drift from itself.

## 4. The shape

A **format description** is the artifact an author writes and maintains.
Parser and serializer are derived from it, so they cannot disagree, and
the constants exist once.

A description needs to express what real formats do:

- fixed fields with declared widths, which our bounded integers already
  spell precisely
- **dependencies**: a length field that governs a later array, a tag that
  selects a variant, a count that governs repetition
- constraints: a magic value, a version range, a reserved field that must
  be zero
- endianness, alignment, and padding that must be checked rather than
  skipped

Derivation should ride the machinery that already exists -- the typed IR
and the plug pipeline -- rather than introducing a second one. A format
description is a chapter that lowers to IR; the parser and serializer are
emitted from it the way a plug emits a target.

Properties the generator should be able to establish or check, in
increasing order of difficulty:

1. **Total.** Every input either parses or is rejected. No path diverges;
   fuel-capped, with exhaustion counted as rejection.
2. **In-bounds.** No read past the buffer, and no allocation sized by an
   unvalidated attacker-controlled length.
3. **Round-trip.** For every value the parser accepts, serializing it
   reproduces the accepted bytes.
4. **Unique encoding** where the format demands it, which is the
   non-malleability property of section 6.

`punctual` is the vocabulary we already have for bounded execution (no
heap, no recursion, bounded instruction count) and is the natural
discipline for a validator pass, though not for a full deserializer.

## 5. Stages

| Stage | Deliverable | Depends on new machinery |
|---|---|---|
| 0 | **An adversarial corpus for the parsers that exist today.** Mutate the KAT vectors (truncate, extend, flip length fields, nest deeply, zero the magic, maximise every count) and require: no crash, no hang, no accept of a mutant that should be rejected | no |
| 1 | One format, described and generated. Differential-test generated against hand-written across KAT vectors and the stage 0 corpus | yes |
| 2 | The trust-decision set: `Asn1`, `X509`, `X509Chain`, `TlsCert`, `TrustAnchors`, `Jwt` | yes |
| 3 | The CDX header and loader, where unique encoding matters most | yes |
| 4 | The remaining formats, opportunistically, as each is next touched | yes |

**Stage 0 is the one to do first and it is not conditional on any of
this.** It needs no generator, no description language and no design
approval: it finds real bugs in code that ships today, and it is the
baseline any generated parser must beat. If this document achieves
nothing else, it should achieve stage 0.

## 6. Non-malleability, stated precisely

For a **signed** artifact the dangerous property is not "does it parse"
but "does it parse uniquely". If two distinct byte strings `a` and `b`
parse to the same value `v`, then a signature computed over `v`, or a
decision made about `v`, transfers from `a` to `b`. The attacker never
touches the crypto; they re-encode.

This matters here more than in most systems, because the CDX is both the
signed artifact and the thing that carries capability grants, and because
the trust lattice makes decisions about parsed identities. The content
hash deliberately excluding the signature region is already an
acknowledgement of the same family of concern.

For every format we sign or make a trust decision about, the description
should state whether it requires unique encoding, and the generated
parser should reject a non-canonical encoding rather than normalising it.
`EverCBOR` supporting "deterministic encoding" is the same requirement
under a different name.

## 7. Memory and time (rule 8)

- A generated parser is single-pass over the input, with no allocation
  proportional to a length field that has not yet been validated against
  the bytes actually remaining. **The classic failure is trusting a
  declared length before checking it.** On bare metal with no GC, that is
  not a slow parse, it is a dead machine.
- Every loop bound comes from bytes already consumed, never from a field
  read ahead.
- Fuel caps on any recursive structure (nested ASN.1, nested CBOR), with
  exhaustion answering "reject", never "accept".
- The generator runs at build time and has no runtime cost.

## 8. Risks and honest scope

- **The generator becomes a trusted component.** It is a single point of
  failure across 75 formats, which is precisely the correlation argument
  from `IndependentRechecker.md`. The mitigation is the same: keep the
  hand-written parser, differential-test against it, and do not delete it
  until the generated one passes both the KAT vectors and the corpus.
- **A description language is a language**, with a grammar, error
  messages and a learning cost. Stage 1 exists to find out whether
  describing one real format is actually shorter and clearer than the
  hand-written module. If it is not, stop at stage 0 and keep the corpus.
- **Scope:** stages 1 to 4 are a campaign, not a task. Nothing here
  blocks or competes with seed work, and none of it is seed-affecting
  until a generated parser enters the foreword.

## 9. Cross-references

- `docs/Designs/Active/Tools/IndependentRechecker.md` -- the same
  correlation argument, applied to the type checker
- `docs/Designs/Done/OS/Verifier.md` -- the verifier's own design; read it
  before touching the loader
- `codex/os/verify/CdxBinary.codex` -- the CDX header layout and the
  capability-direction near-miss quoted in section 3
- `codex/foreword/encode/` -- the 75 modules in question
- `docs/ExaminersAssay.md` -- test and probe conventions the stage 0
  corpus must follow
- EverParse: fstar-lang.org, project-everest.github.io/everparse, and the
  paper *Provably Correct Non-Malleable Parsing*

## 10. Stage 0 census -- every parser of foreign bytes, and whether it has a negative arm (2026-08-15, red)

**Why this section exists.** Track D in `docs/PM/CurrentPlan.md` names the
class this design is about, and the first thing it needed was not another
fix but a table: which parsers exist, where each one enters, and whether
anything in `codex/test/` feeds it bytes it should refuse. Without that, the
next agent with slack fixes the nearest parser rather than the most
dangerous unguarded one, which is exactly how the last two days went.

**Method (L-COUNT: re-measure, do not carry these numbers forward).** Every
module under `codex/foreword/encode/`, plus the parsers in `codex/os/net`,
`codex/os/verify`, `codex/os/trust`, `codex/os/kernel` and `apps/works`,
was read for its chapter name and its decode entry points; then every test
under `codex/test/` (including `codex/test/apps/`) citing that chapter was
read for whether it feeds MALFORMED, TRUNCATED or HOSTILE input and expects
a refusal or clamp. Three states:

- **NEGATIVE-ARM**: at least one such test exists, named in the evidence
  column.
- **KAT-ONLY**: the only tests are known-answer or round-trip on well-formed
  input. Strong evidence about the encoder; none about the parser.
- **NO-TEST**: no test calls the decode entry point at all.
- **N/A**: the module is encode-only; there is no decoder to guard.
- **UNSURE**: the classification could not be settled without reading more
  than the census budget allowed, and the reason is stated.

**The result inverts the assumption this design's own section 1 invited.**
The trust-decision row, ranked first by what a bad parse buys an attacker,
is the BEST covered: `Asn1` has six negative arms (indefinite length,
non-minimal length, overrun, high tag, leading zero, unused bits), `X509`
has a truncated real certificate, `X509Chain` six tamper arms, `TlsCert`
two, `Jwt` two. The loading authority (`CdxVerifier`, `VerifiedLoader`,
`WakeCeremony`) has flipped-byte and bad-magic arms. Where the tree is
naked is one row down and sideways: **52 KAT-ONLY and 19 NO-TEST parsers**,
concentrated in wire framing (25 KAT-only), device descriptors, and the
session layer's own transport.

### 10.1 The gaps, in the order the next agent should take them

Ranked by the same rule as section 1: what a bad parse buys, then how many
bytes reach it unauthenticated. This list, not the full table, is the work
queue.

**Reachability, measured 2026-08-15 after the first ranking, and it
reorders the top.** **Scope:** the first sweep covered `codex/foreword/encode/`, `codex/os/{net,verify,trust,kernel}` and `apps/works` and missed the rest of `codex/foreword/`, which fester's item-12 claim exposed on 2026-08-15. **The second sweep (red, 2026-08-16, Damian's direction) covered every remaining `foreword/` directory: ai, compress, core, engine, game, gpu, math, punctual, shell, signal, sim, ui (357 files).** Its rows are in 10.2 under "second sweep", its counts in the second table, and its findings are ranked into this list as items 14-19 (measured, not appended: the top of the list moved). Still outside every sweep: `apps/` other than `works` and `guios`, and `codex/os/` other than the four dirs named; nothing is known to hide there but nothing has looked. A parser nothing calls is untested (L-UNCALLED) but it
is also not exposure, and stage 0 is about code that ships. Grepping every
non-test chapter for callers of the entry points below: `cbor-decode`,
`msgpack-decode`, `pb-decode-field`, `ws-decode-frame`,
`discovery-process-beacon`, `oauth-parse-tokens`,
`imap-parse-response-kind`, `modbus-parse-registers`, `qoi-decode`,
`bmp-decode`, `csv-parse`, `ini-parse`, `uri-parse`, `toml-parse` and
`yaml-parse` have **NO production caller** outside their own chapter. What
IS reached from a wire or a volume: `transport-process-frame` /
`recv-buf-i32` (`NetIO`, `Arm64NetIO`, `TrustTransport`, and every plug
through `plug-source`, so it is the most-trafficked parser in the tree),
`hs-receive-*` and `decode-agent-msg` (through `TrustTransport`),
`http-parse-response` (`HttpFetch`, the browser's `PageFetcher`),
`ttf-parse` (`TrueTypeFont`, `FontExtract`: a font off a stick), and
`base64-decode` (`Jwt`). Take those first; the uncalled ones are a latent
list, worth a negative arm only when a caller appears, or as the cheap
corpus exercise stage 0 describes.

| # | parser | state | why it ranks here |
|---|---|---|---|
| 1 | `codex/os/net/TcpTransport.codex` `recv-buf-i32`, `transport-process-frame` | **NEGATIVE-ARM (val, 2026-08-15)** | three defects, all fed from the wire: `transport-feed-raw` wrote past the 32 MB `recv-base` allocation with no `recv-cap` check (`__buf-write-bytes` has no capacity argument); `msg-len = 0` gave `__buf-read-bytes` a read length of **-1**; a `msg-len` above `recv-cap - 4` stalled the connection permanently. All three refuse now. `codex/test/apps/tcp-transport-guard`, four arms with one positive control, each ablation run and moving exactly one row; account in `ExaminersAssay.md` "The Transport Length Guards" |
| 2 | `codex/os/trust/Handshake.codex` `hs-receive-hello`, `hs-receive-prove` (via `TrustTransport`) | **NEGATIVE-ARM (val, 2026-08-15)**; and the "reached" in this row was WRONG, see below | the trust lattice's own handshake. The defect was not a bound: `hs-receive-prove` never took the signature as a parameter, ignored `expected-nonce`, and returned `HsCompleted` unconditionally with the claimed key's trust score, while `trust-complete-as-responder` discarded the prove body and set `authenticated = True` on every path. Proof-of-work needs no private key, so any peer could claim any identity. Now refuses a non-32-byte key, a non-64-byte signature, and a signature `ed25519-verify` rejects over the challenge nonce; both length checks also stop a remote guest kill, since `ge-from-bytes` reads index 31 and `ed-list-drop` computes a negative slice length. `codex/test/apps/handshake-prove-guard`, six arms with two positive controls, all four guards ablated; account in `ExaminersAssay.md` "The Handshake Prove Guards". **Reachability correction: `trust-respond-hello`, `trust-complete-as-responder`, `trust-initiate` and `trust-complete-as-initiator` have NO caller anywhere in the tree, tests included, so this row is LATENT like `WebSocket` and `PeerDiscovery`, not reached. Fixed anyway rather than demoted: an uncalled parser with a bad bound is a bad bound, an uncalled handshake that authenticates nobody is a trap for whoever wires it up.** |
| 3 | `codex/os/trust/TrustTransport.codex` `decode-agent-msg`, `decode-hello-body`, `decode-capability` | **NEGATIVE-ARM (val, 2026-08-15)**; the "reached" here is CORRECT, unlike row 2 | genuinely live: `trust-recv` is called from `TrustNode`'s `node-recv-loop` and what it decodes reaches `eval-policy`. Eight sites read a tag or flag byte with a bare `list-at bs off` past a peer-controlled offset; `decode-agent-msg tag-propose []` died `!EXC=06`. All eight now use `frame-byte-at`. With the fault gone a truncated body still decoded to an empty-fielded message that policy was asked about, so `decode-agent-msg-checked` answers a `valid` flag (a round trip against our own encoder, so it cannot drift from the decoder and it also buys canonical-encoding refusal) and `trust-recv` reports `has-message = False`. `agent-tag-known` is separate and needed: the final `else` decoded ANY unrecognised tag as a `WorkReply`. `decode-hello-body-checked` closes item 2's named residual. `codex/test/apps/agent-msg-truncated`, twelve arms with three positive controls, four ablations; account in `ExaminersAssay.md` "The Agent Message Guards" |
| 4 | `codex/os/net/HttpClient.codex` `http-parse-response` (via `HttpFetch`, `PageFetcher`) | **NEGATIVE-ARM (red, 2026-08-15)** | crash-safe by construction; the two wire-supplied numbers it believed (a wrapping status accumulator, a wrapping or signed Content-Length) now refuse. `codex/test/apps/http-response-guard`, account in `ExaminersAssay.md` "The HTTP Response Guard" |
| 5 | the font off the stick: `apps/guios/FontLoad.codex` `fb-parse-ttf` via `GopFont` `gfont-load` (NOT `encode/TrueType` `ttf-parse`, which is reached only by `FontExtract`, red under FONTAI-1, and by the uncalled `TrueTypeFont`) | **NEGATIVE-ARM (red, 2026-08-15)** | a `peek-byte` parser: every 32-bit directory offset, loca entry and cmap subtable offset was a raw address; ablated, an empty buffer parsed to 8295 glyphs and an 8-byte runt killed the guest. `fb-ttf-plausible` gates `gfont-load`; `codex/test/apps/ttf-plausible-guard`; account in `ExaminersAssay.md` "The TrueType Plausibility Guard". Residual named there: glyph-body counts still unbounded by `next-off`. `ttf-parse` in `encode/` stays KAT-ONLY, latent |
| 6 | `codex/foreword/encode/WebSocket.codex` `ws-read-length` | KAT-ONLY, **uncalled** | a 64-bit length field read ahead of the bytes (section 7's classic failure); latent until a caller exists |
| 7 | `codex/os/trust/PeerDiscovery.codex`; `Cbor`, `MessagePack`, `Protobuf`, `Bencode`; `ImapClient`, `OAuthClient` | KAT-ONLY / NO-TEST, **uncalled** | latent: `cbor-decode-at` trusts every length and count and treats `ai` 27 and 31 as four-byte arguments, which is what a corpus would find; nothing reaches it today |
| 8 | `codex/os/kernel/Usb.codex` descriptor parsers, `UsbHid.codex`, `VirtioBlk.codex` | DONE for the USB half, 2026-08-15 (reek) | `UsbHid.codex` hid-scan-loop was a real over-run and is clamped; the three `Usb.codex` parsers were already guarded and now have the arm that says so (`codex/test/usb-desc-guard.codex`). `VirtioBlk` is left for the census owner: not USB, not a descriptor, and its device-written ring index is a different shape |
| 8b | `codex/os/kernel/VirtioBlk.codex`: `peek-16 d0 14` is a used-ring index the DEVICE writes and the driver follows with no bound | NO-TEST, unowned | placed here by reek's reading (not USB, not a descriptor, so outside lane 3); the ARM64/virtio path has no bed that can feed it a hostile ring today (`CrossLaneFilesystem.md`), so it waits for one or for the Renode bed |
| 9 | `apps/works/AgentBundle.codex` `gguf-parse-header`, digest and manifest-size refusals | KAT-ONLY | a model file off a volume; its own refusal paths have no test |
| 10 | `codex/foreword/encode/Modbus.codex`, `Coap` consumers `Lwm2mClient`, `Lwm2mFirmware` | KAT-ONLY | industrial: actuation from a frame; `Coap` itself is guarded, its consumers are not |
| 11 | `codex/foreword/encode/TrueType.codex`, `Qoi.codex`, `Bmp.codex`, `VideoCodec.codex`, `GopFont` `gfont-load` | KAT-ONLY / NO-TEST | media off untrusted files; memory and CPU rather than authority |
| 12 | `codex/foreword/core/Fat16.codex`: `fat16-parse-bpb`, `fat16-next-cluster`, `fat16-read-dir-entry`, `fat16-find-in-root` (REACHED: the compiler's own DISK path, `opening.codex` :978, :1849, :1950-1970, is A5 compiling from the stick; also `AgentBundle`, `SinkLadderProbe`) | KAT-ONLY (`fat16-*` tests are round trips on volumes we wrote); `opening.codex` already hand-probes BPB bytes 11 and 13 because `fat16-parse-bpb` divides by them | the FOREWORD FAT reader, one layer under the `GopFat16` guards and WORKS-29: geometry decides where reads land, and a cluster number off the volume is followed. Cited by the compiler unit, so it is inside the seed's reachable set. `codex/os/kernel/FatReader.codex` (`fat-parse-bpb`, `fat-parse-dir-entry`) has NO production caller and is latent, and it is a trap for whoever wires it up (fester, 2026-08-15): its cluster walk has neither a low-side nor a high-side bound and `fat-parse-bpb` has no geometry check at all, worse than `GopFat16` was; `DriveManager` UNSURE as before. Item 12 itself is fester's (announced 2026-08-15, seed-affecting) |
| 13 | `Csv`, `Ini`, `Toml`, `Yaml`, `Uri`, `Markdown`, `Smtp`, `Sntp`, `Syslog`, `Tftp`, `Icmp`, `Hex`, `Base64`, `Uuid` | KAT-ONLY / NO-TEST | text and small binary formats; low authority, high count; `Ini` and `Csv` have no malformed case at all |
| 14 | `codex/foreword/core/Gpt.codex` `gpt-read`, `gpt-read-entries-step` (REACHED: `os/kernel/DiskFacts.codex:501`, `core/Fat16.codex:1258`, so it is on the boot-volume path the compiler itself walks and inside the seed's reachable set) | **NEGATIVE-ARM (red, 2026-08-16)**: `gpt-header-geom-ok` is `GopFat16`'s `gpt-array-geom-ok` transcribed into the foreword; `codex/test/apps/gpt-core-{read,size-256,size-guard,count-guard}`, positive control, accepted-not-refused arm, two refusals both moved by ablation; fixtures from `build/mint-gpt-core-fixtures.ps1`; account in `ExaminersAssay.md` "The Foreword GPT Geometry Guard". Was: KAT-ONLY | the FOREWORD GPT reader, one layer under the guarded Works one. `:196-197` refuse entry-size 0 and > 512 and the prose above them says that is the whole hazard; it is not: `:154-156` admits any entry-size 1..127, and for those `off-in-sector` runs to `(512/es - 1) * es` and `gpt-parse-entry` peeks 128 bytes past it, off the end of the 512-byte sector buffer (a raw buffer, so adjacent heap, no fault). The u32 partition count at `:201` is followed until the first zero type GUID with no ceiling, and the entry start-lba is a u64 off the disk with no bound. Not seed-neutral: `Fat16` cites `Gpt` |
| 15 | `codex/foreword/core/FactDisk.codex` `store-read-bundle`, `fd-entry-content` and `core/SourceDefWire.codex` `sdw-decode` (REACHED: `compiler/opening.codex:2101`, the compiler's `store` mode; `RepoProtocolPersist.codex:159`) | **DONE 2026-08-16 (reek)**, and both halves were real. Was: KAT-ONLY (`factdisk-read` on a well-formed sidecar) / NEGATIVE-ARM on shape only for `sdw-decode`, with NO arm feeding an over-long length. Now `source-def-wire-guard` (eight arms, three of them the well-formed shapes a guard must not refuse; the unguarded code dies on arm 4) and `factdisk-hostile-head` (ablated: `OUT OF MEMORY` at a 4,000,000,512-byte request; guarded: the store is refused whole and the caller's own bundle comes back intact, with `factdisk-read` untouched as the other half of the control). Head bounded by `block-sector-count` rather than a constant, since a constant cannot know the medium's size. Fixtures minted from the spec, `build/mint-factlog-fixture.ps1 -Hostile`. **Three lanes built this item independently within about thirty minutes (reek 15549, val 15548, blu 15544); the refusal posture is val's and the `emit-substring-bounds` citation is blu's and val's.** STILL OPEN and val's to land: a per-record cap (`fd-max-content-len`), since the head bound does not stop one record on a large medium from allocating most of it | a disk-supplied record length sizes an allocation: `FactDisk.codex:94` `alloc-bytes (nsec * 512)` with `nsec` from the entry's u32 `clen`, bounded only by `:141 sector + nsec > end-sec`, and `end-sec` is the superblock's u64 log head (`:60`, `FactLog:85`) with no upper bound; `:154` walks every sector up to it. `SourceDefWire.codex:164` `substring line (p9+1) clen` with `clen` from the record and never checked against what remains of the line (`substring` traps out of range, so this is a guest kill on one hostile record). **Both confirmed and fixed 2026-08-16; red's reading of this row was right in every particular, including `end-sec` being the only bound. The ceiling taken is `block-sector-count`.** That residual -- a large medium still permitting one record to allocate most of it -- is CLOSED (val, 2026-08-16): `fd-max-content-len` caps one entry at 4 MB independently of the medium, against a widest legitimate value of one chapter (735,952 bytes measured). **It ships with NO ARM and the account says why**: reaching it needs a store image over 4 MB, because on anything smaller the medium ceiling refuses the entry first. Account in `ExaminersAssay.md` "The Fact Store Length Guards" |
| 16 | `codex/foreword/ai/Gguf.codex` `gguf-tensor-info-offset`, `gguf-md-scan`, `gguf-parse-tensor-info` (REACHED: `apps/works/AgentBundle.codex:297-317` from `DevConsole.codex:313` via `verify-bundled-agent`, a model file off the boot volume) | NEGATIVE-ARM since val 2026-08-16 (`gguf-hostile`, sixteen rows, eleven ablations); was NEGATIVE-ARM on the 4-byte header only (`gguf-test` `test-invalid-header`), with the metadata walk, KV arrays, string lengths, `ndim` and dims carrying no hostile arm | the parse runs behind a manifest signature (`AgentBundle.codex:240`) and a model digest, but the public key is read out of the SAME manifest (`:236`), so the gate proves the stick's author signed it and nothing about who that is. `:151-155` loops a u64 `count` off the data advancing by `8 + len` with `len` a u64 off the data and no `list-length` guard or cap (`gguf-skip-metadata:168` has the guard, `gguf-md-scan:185-198` does not); `:213-215` `ndim` off the data sizes the shape and `dtype-off`; the caller passes `ti-off` to `gguf-parse-tensor-info` unchecked. `list-at` traps out of range, so the shape is guest kill or a long walk, not a silent over-read. **DONE (val, 2026-08-16), and the row understated it in one place and overstated it in another.** Understated: `gguf-parse-header` refused a file under 20 bytes and then read bytes 16..23 of a 24-byte header, so the guard itself trapped on a 20-byte file, and `gguf-md-scan` had no offset check at all while `gguf-skip-metadata` had one, so the SAME file that `gguf-tensor-info-offset` correctly refuses killed the guest through `gguf-metadata-text`. Overstated: the string-array walk needs no fuel cap, because every element it accepts consumes at least its own eight-byte length word out of a finite file. `gguf-fits` is now asked before every read at a file-supplied offset and each caller refuses on the channel it already had; `gguf-parse-tensor-info` gains an additive `gti-valid`. Arm `codex/test/apps/gguf-hostile`: sixteen rows, three positive controls, ELEVEN guards ablated separately, each killing the guest at exactly its own row. Positive control that matters: `build/gguf-foreign-test.ps1` still parses four real llama.cpp models up to 3,184 MB. NOT seed-affecting (only `AgentBundle` cites the chapter). **RESIDUE CLOSED 2026-08-16: the dequant path was outside that pass and was still a guest kill** (found by reek, verified against main's bytes: 32 elements from a one-block buffer answers 32 values, 64 dies `!EXC=06`). Neither dequant loop takes a length off the file, so the metadata guards never reached them; the count arrives from the caller and the caller reads it off the tensor shape. Both loops now stop on the first block that does not fit, which is a CLAMP rather than a refusal because the count decides how many values come back and not where a read lands. `AgentBundle.ab-parse-model` also now checks `gti-valid`, which the first pass shipped with no caller asking for it. Arm is twenty rows and thirteen guards, each isolated by its own row. Account in `ExaminersAssay.md` "The GGUF Bounds Guards" |
| 17 | `codex/foreword/core/Fat32.codex` `fat32-parse-bpb32`, `fat32-resolve-path`, `fat32-read-bytes` (REACHED: `os/kernel/DriveManager.codex:270-271`, `apps/guios/FontLoad.codex:610`) | KAT-ONLY (`install-to-drive` reads a volume `fat32-format` made) | the same class as item 12 one format over: `:44` divides by BPB byte 13 with no zero check; `:206` the dir entry's `de-size` is the read length bounded only by chain end; `:135`/`:146` the FAT chain walks have no cycle guard, so a volume whose chain loops walks forever |
| 18 | `codex/foreword/core/OtaBoot.codex` `boot-load` (**LATENT, correction 2026-08-16 reek: the `FirstBoot.codex:223` cite is a SUBSTRING of `first-boot-load-mode`, not a call. `boot-load` has no production caller anywhere; only `codex/test/apps/ota-boot-rollback` and OtaBoot's own `:244`/`:251`**); `core/Aes256.codex` `aes256-pkcs7-unpad` (REACHED, confirmed: `os/kernel/IdentityManager.codex:110` and `:162`, `GopWizard.codex:495`); `core/KeyboardLayout.codex` `kbl-lookup` (REACHED: `os/kernel/Keyboard.codex:67`, `:85`, `:86`) | NEGATIVE-ARM (`ota-boot-rollback` corrupt bank, no flag; nothing on the length) / NO-TEST / KAT-ONLY. **Reachability audited 2026-08-16 (reek): no live guest kill in this row, see the notes** | small and reached: `OtaBoot.codex:204-206` a 32-bit length word off flash is `sha256-buf slot-addr 0 len` with only `len <= 0` refused; `Aes256.codex:150` the pad byte 1..16 is not checked against the buffer length (a negative take answers `[]`, benign by inspection); `KeyboardLayout.codex:30` bounds `sc` above only, `peek-byte tbl (sc*2)` on a negative scancode reads below the table (no caller produces one today, the same shape reek recorded for `Usb.codex`). **Audited 2026-08-16 (reek), and each of the three is thinner than the row reads. `kbl-lookup`'s missing low bound is closed by a TYPE, not by luck: `kb-process-scancode` declares `Integer between 0 and 255`, so the compiler refuses a negative scancode at the only caller, and `kbl-write` takes literal constants only. `aes256-pkcs7-unpad`'s `pad-val > len` case is benign as the row says, and benign one step further than inspection reached: it answers `[]`, and BOTH callers then hand that to `ed25519-public-key`, which is total on a short list because `sha512` absorbs any length, so neither can be killed by it. `OtaBoot`'s 32-bit length off flash is the one real defect in the row (`sha256-buf` over up to 4 GB, only `len <= 0` refused, and no slot-size constant exists to bound it against) and it is LATENT with no caller.** Nothing here is a live guest kill; the value is hardening latent code, which is why the `compress/` half of the claim went first. **CLOSED 2026-08-16 (reek) with ONE named residue: `OtaBoot`'s 32-bit flash length.** `kbl-lookup` and `aes256-pkcs7-unpad` need no change for the reasons above. `boot-verify-candidate:204` is a real unbounded `sha256-buf` and is deliberately NOT fixed here: there is no slot-size constant in the chapter to bound it against, the only honest bound is one the CALLER supplies, and the function has no production caller to supply it, so inventing a signature for an uncalled function is design work for whoever wires OTA up rather than a guard. It qualifies for a guard under red's ruling (`ota-boot-rollback` runs it) and is the one piece of items 18-19 left open on purpose |
| 19 | **CALLER CENSUS FOR THE WHOLE ROW, measured 2026-08-16 (reek), under red's extended ruling: any caller, production OR a harness that RUNS it, earns a guard; no caller gets a row and no guard.** The trap on this leg is that EVERY one of the thirteen has citers and most of them run nothing: `codex/test/apps/foreword-all-compile.codex` cites 417 chapters and its whole body is one `print-line-uni`, and MOST of `codex/test/forewords/*.codex` is the same cite-smoke shape (`foreword-pbkdf` is 157 bytes and prints `"Foreword/Pbkdf OK"`). **CORRECTION, reek 2026-08-16: this row first said EVERY one of them was cite-smoke, and that is false.** Measured: 316 files, **299 under 400 bytes and 16 over 1,000**, and the large ones are real harnesses. `foreword-safetensors.codex` is 5,824 bytes, builds SafeTensors files byte by byte, calls `st-parse-file`, `st-load-by-name` and `st-find-tensor`, and pins two defects of its own. `foreword-ranked-text-set-surface` (6,692), `ai-exp-approximations` (6,612) and `foreword-source-def-wire` (5,625) are the same. **So `codex/test/forewords/` cannot be dismissed as a directory: check the file.** The generalisation was cheap to make and would have exempted a chapter that has a real runner. Counting those as callers would qualify all thirteen and mean nothing. Excluding them: **QUALIFY (10)** -- `Tls` (prod `DtlsHello`, `DtlsMessage`, `TlsCert`, `TlsEndpoint`, `os/net/DtlsEndpoint`; run by `tls-test:36`/`:41`), `Pbkdf` (prod `apps/secrets/VaultCrypto:77`; `pbkdf-verify` run by `crypto-vectors:81`/`:84`), `SafeTensors` (14 `ai/` chapters plus `apps/assetforge`), `GpuProxy` (prod `ai/Conv2d`, `os/kernel/GpuBridge`; six real arms), `GlyphRasterizer` (prod `ui/FontAtlas`, `ui/TrueTypeFont`, `apps/guios/FontLoad`), `TrueTypeFont` (run by `truetype-bridge-test`, `ttf-parse`/`ttf-load-font`), `ChaCha20Poly1305` (run by `chacha20poly1305`), `Schedule` and `Pattern` (both run by `final-batch-test`, `sched-matches` / `pat-match`), `Decimal` (run by `lib/decimal-test`). **NO CALLER, row and no guard, L-UNCALLED (3): `LoraLoader`, `PngMetadata`, `PromptParser`** -- cite-smoke only. **THE `compress/` LEG IS CLOSED. THE REST OF THIS ROW IS NOT.** Caught by blu 2026-08-16 against reek's own "row 19 closed", which was closed on a SUBSET (L-CAPABILITY). **`core/Tls` DONE, reek 15699** (five-byte header read with no length check; `tls-rec-valid` added so a truncated record is not returned as a well-formed empty one; account in `ExaminersAssay.md`). **`core/Pbkdf` DONE, reek 2026-08-16**: `pbkdf-verify:159` walked both hashes to the STORED length, so a longer stored hash indexed the rehash past its end (`!EXC=06`) and a SHORTER one silently compared a prefix and agreed -- a stored record truncated to eight bytes verified True against the right password, a forgery cost of 2^64 down from 2^256, and that half never faults so no crash arm would have found it. Second guard: `pbkdf-final-block` sliced at `(0 - 1) * 32` when `pb-block-count` was zero or less. Seven-row arm, both guards ablated separately, row 7 (a legitimate 16-byte tag) discriminating against a constant-32 check. `pb-time-cost`/`pb-block-count` left UNBOUNDED on purpose: they are a cost, not a read, and clamping either changes the key derived for `apps/secrets`, which already runs at 100000 -- a stored-data break that belongs to that format's owner. **`core/ChaCha20Poly1305` DONE, reek 2026-08-16**, and it is the leg's most interesting one because ChaCha20 does NOT fail on a wrong-sized key or nonce, it REINTERPRETS the state: `chacha-init-state:38` concatenates constants, key words, counter, nonce words, and `chacha-words-from-bytes:88` takes `list-length / 4`, so the size decides which word is which. Measured before the guard: a 16-byte nonce drops its fourth word and yields the keystream of its own 12-byte prefix (two nonces, one keystream, which is the Poly1305 nonce reuse the chapter's opening says destroys it); a 64-byte key pushes the nonce out of the state entirely, so two DIFFERENT nonces give byte-identical ciphertext; an 11-byte nonce leaves a 15-word state and `chacha-qr state 3 7 11 15` kills the guest. **Only the third crashes; the two silent ones are worse.** `cp-params-ok` refuses at both entry points, `cp-valid` added so a refusal is distinguishable from a short message. Nine-row arm, both guards ablated separately; rows 1 and 2 are the instrument control (two legitimate nonces MUST differ, or rows 6 and 7 agreeing would prove nothing). `poly-tags-equal:221` was checked, not assumed, and already had the length equality `pbkdf-verify` lacked. RFC 8439 section 2.8.2 vector byte-identical after the change. **`core/Decimal` DONE, reek 2026-08-16**, and it is the first fault in this campaign that is not `!EXC=06`: 10^18 is the largest power of ten a signed 64-bit integer holds, 10^19 wraps negative, and **10^64 is exactly zero**, so a scale of 64 makes `dec-pow10` a ZERO DIVISOR for `dec-to-text`, `dec-whole-part`, `dec-frac-part`, `dec-div`, `dec-round` and `dec-truncate`. `dec-from-text:147` set the scale to `text-length frac-str` unbounded, so 64 fraction digits was the whole exploit, and the guest died `!EXC=00`. **Three producers, not one**: `dec-mul` ADDS the scales so two scale-32 values reach 64 with no text involved, and `dec-new` takes a scale from its caller, so the saturation sits in `dec-pow10` where every consumer passes and the two input-driven producers clamp as well (guarding only the parser would have been L-CAPABILITY one level down). Eight-row arm, three ablations each moving only its own rows. **The parser-clamp ablation is the one worth reading: with `dec-pow10` safe it does not fault at all, and `1.1234...` parses to `-8.000...`** -- a fault-hunting pass would have fixed the divisor, watched the crash stop, and shipped the wrong number. Row 8 (exactly eighteen digits, must survive unclamped) and row 3 (ordinary multiplication, must not move) are the discriminators. **Measured and deliberately NOT changed: `dec-find-dot:156` compares against 65, which reads as ASCII `A` and looks like a typo for `.`; in CCE the full stop IS 65 and `A` is 41** (R-CCE). `decimal-test` never calls `dec-from-text`, so the parser had no runner at all (L-GAP). **`core/Schedule` DONE, reek 2026-08-16, and it is the only chapter in this campaign with NO memory-safety defect.** Eighteen malformed inputs were run before a line was written (empty, fragments, bare `between`, lone `:`, twenty-digit interval, spaces only) and every one answered `None` or returned a `Sched` with no fault: its index checks are complete, which is a result worth recording rather than a non-event. **What it did instead was ACCEPT**, in four shapes, the same no-crash-wrong-answer class as `Pbkdf`'s truncated hash: `weekly on wendsday at 9am` scheduled for SUNDAY (`sched-parse-day-name`'s final `else 0` made every unrecognised word Sunday); `every 5 minutes between 9am` silently DROPPED the window and returned an unrestricted schedule, because `n >= 6` guarded the whole clause instead of its arguments; `every 5 minutes between 9am and` defaulted its stop to hour 24 and printed it as `between 9am and 12pm` while running to midnight; `daily at 99999:99999` parsed. Three guards (day-name sentinel, `between` completeness, hour/minute/day ranges), three ablations moving rows 7-8, 9-10 and 11-14 respectively. **Rows 1-6 and 15 are the controls and carry the whole weight**: a guard answering `None` to everything passes all eight hostile rows, and `12:00am` plus `monthly on 31` are the boundary pair that catches an off-by-one range check. `final-batch-test` is byte-identical and only ever passes well-formed strings, which is why none of the four was visible to it. **`core/Pattern` DONE, reek 2026-08-16**, two defects and **the only heap exhaustion in this campaign reached from ordinary text rather than a crafted binary**. `pat-match-many:181` recursed on whatever position its inner pattern left it at without requiring the position to MOVE, so a zero-width match repeats forever accumulating on a GC-less heap: `many (lit "")` reaches `OUT OF MEMORY` in about seven seconds, and **`pattern "many optional"` -- two ordinary English words -- does the same**, because `optional` at the end of the token list falls to `pat-parse-optional`'s `i >= n` case which answers `PatLiteral ""`. Second: `pat-match-repeat:199` answered `mr-consumed = pos`, the ABSOLUTE position, where every other matcher answers a relative count; at position 0 they coincide, which is why nothing caught it, and everywhere else the caller overshoots, so `and-then (lit "ab") (and-then (exactly 2 digit) (lit "cd"))` did NOT match `"ab12cd"`. Nine-row arm, two ablations (rows 8 / rows 5-6). **Row 4 is the discriminator and the reason the fix is a new accumulator rather than a subtraction**: the position-0 case was already right and a fix that moved it would trade one wrong answer for another. `final-batch-test` is byte-identical and exercises `pat-match` only at position 0 with patterns that consume, which is exactly the blind spot both defects lived in (L-GAP). **`ui/GlyphRasterizer` and `ui/TrueTypeFont` DONE, reek 2026-08-16.** The census row said "font bbox and `upem` (a zero divisor) size a `w*h` non-tail-recursive allocation" and both halves were right. **`unitsPerEm` is a raw 16-bit head-table field and `gr-render-glyph:203` divides every outline coordinate by it, so TWO BYTES of a font file decide whether the chapter runs.** Measured on `truetype-bridge-test`'s own font with only offsets 246-247 rewritten: `upem = 0` gives `!EXC=00` (divide error) on the first glyph; `upem = 1` gives **`!EXC=08`, a DOUBLE FAULT**, because with no divisor to bring coordinates back the width came out 11,201 px and `w * h` sized the buffer. Third fault class in this campaign. `gr-upem-ok` refuses a non-positive upem with a 1x1 blank; `gr-clamp-dim` bounds each dimension at 4x ppem, floored 16, capped 1024 (the cap is set by the AA path, which supersamples 4x4). Seven rows, two ablations (row 3 / row 4), rows 1-2 and 7 the controls: 1-2 are the same font at its real 1024 still rendering 96 glyphs at 9x11, and 7 is a legitimate upem of 2048 at 5x6, which is what says the clamp does not touch ordinary scaling. `TrueTypeFont` is a thin adapter over this and needed no guard of its own. All three truetype tests byte-identical.

**`encode/TrueType` DONE, reek 2026-08-16** (found while guarding `GlyphRasterizer`, recorded here as unowned, then assigned to this lane by red). `ttf-u8:10`, `ttf-u16:14`, `ttf-u32:25` and `ttf-read-tag:45` all read with bare `list-at`, and every field of every table is read at a fixed offset from a table offset the FILE supplied, so a file that stops early is read past its end. `ttf-parse []` died `!EXC=06`. **The lesson is where it sat relative to the rest of this row: the `ui/` `upem` guards are DOWNSTREAM of `ttf-parse`, so a truncated font never reached them** -- guarding a consumer does nothing for an input its producer cannot survive. `ttf-byte-at` answers zero outside the buffer, chosen because the downstream guard already catches it (`unitsPerEm 0` -> `gr-upem-ok` refuses -> blank glyphs); `TtfFont` has no validity field, so zeros plus a downstream refusal is the honest arrangement. `ttf-get-hmetric:273` also fixed: it answered `list-at hmetrics -1` for an out-of-range index on an empty list, which a truncated `hmtx` produces. Ten-row arm over eight truncation points; ablated, row 2 dies with `RSI=4` on a zero-length buffer and **row 1, the intact font, does not move**, which is what says the guard did not start answering zero everywhere.

**`ai/SafeTensors` DONE, reek 2026-08-16**, and it qualified for a guard only because of the correction above: its runner is `codex/test/forewords/foreword-safetensors.codex`, one of the 16 real harnesses in a directory this row had written off. **The header parser was already defended and the loaders were not**: `st-parse-file` answers `valid=False` for a short file and a garbage header and the harness covered both, but nothing covered a WELL-FORMED header describing a tensor that is not there. `st-read-u32:48` reads four bytes with four bare `list-at`, `st-read-f16-loop:209` and `st-read-bf16-loop:239` the same; the offset is `st-data-offset + stm-offset-start` and the count is `st-element-count`, the PRODUCT of the declared shape, all of it out of the file's JSON. Measured: `"shape":[1000000]` in front of 20 bytes of payload dies `!EXC=06`, `R13=0xf4240` against `RSI=0x5e`. `st-can-read` is written SUBTRACTIVELY (`count <= (length - off) / width`) so a shape whose product overflows cannot pass by wrapping, which row 8 (`[4294967296, 4294967296]`) is there to hold. Three call sites, three ablations, each killing only its own row. Row 9 is the discriminator: a tensor ending exactly at the last byte must still load.

**`ai/GpuProxy` DONE, reek 2026-08-16.** Only the parsing half belongs in this census: the buffer it reads is what a device wrote. `gpu-read-u32:115` reads four bytes with four bare `list-at`; `gpu-parse-result:194` calls it at offsets 0, 32 and 36 and so needs forty bytes and checked for none; `gpu-is-complete` and `gpu-is-error` need four and checked for none; `gpu-f32-bytes-to-tensor:153` reads `rows * cols` elements with no comparison to the buffer. Measured: `gpu-parse-result []` dies `!EXC=06`, and **an empty result buffer is not exotic, it is what a device that answered nothing returns.** Nine rows, three ablations killing only rows 3, 6 and 7; ablation C reads `R13=0x2710` (10,000 elements) against `RSI=0x10` (16 bytes). Rows 5 and 9 are the discriminators (exactly forty bytes must parse; a tensor ending exactly at the last byte must load). `gpu-status-error` was already in the chapter's vocabulary so a truncated result reports it rather than inventing a channel.

**THE THREE L-UNCALLED ROWS RE-CHECKED, reek 2026-08-16, against the correction above rather than the generalisation that produced them.** The verdict HOLDS for all three and now rests on measurement: `LoraLoader`, `PngMetadata` and `PromptParser` each have exactly two citers, `codex/test/apps/foreword-all-compile.codex` (13,742 bytes, 417 cites, entire body one `print-line-uni "foreword-all-compile: 417 chapters OK"`) and a per-chapter file of 160, 163 and 166 bytes respectively that prints a string and calls nothing. Neither runs a line of them. **No guard, L-UNCALLED, and the reason is now a count rather than an assumption.**

**ROW 19 IS CLOSED, whole.** Ten chapters guarded (`Tls`, `Pbkdf`, `ChaCha20Poly1305`, `Decimal`, `Schedule`, `Pattern`, `TrueTypeFont`+`GlyphRasterizer`, `SafeTensors`, `GpuProxy`), three carrying L-UNCALLED rows and no guard. Every one measured absent from the compiler unit against a `Foreword--Fat16` control, and every CL states the `Sut.cdx` hash against the depot seed. **Nothing leaves this row unowned: `encode/TrueType` was the one loose end and it landed too, recorded above.** Two of those measured 2026-08-16 (blu, re-verified by reek): `tls-decode-record` has NO production caller, only `codex/test/tls-test.codex:36`/`:41`; `pbkdf-verify` has no production caller either, only `codex/test/crypto-vectors.codex:81`/`:84` -- **note both DO have harnesses that run them, so under red's ruling both QUALIFY for a guard rather than being exempt; the ruling was written for `compress/` and extending it to this leg is an inference somebody should make deliberately.** `Pbkdf`'s chapter is separately REACHED in production through `apps/secrets/VaultCrypto.codex:77`, though not by the hostile path. **RULED by red 2026-08-16: guard a `compress/` chapter only if it has ANY caller, production or a harness that RUNS it; a chapter with no caller at all gets a row saying so under L-UNCALLED and no guard.** Callers measured, not taken from this row: `Lz4` reached (`FactArchive:177`) **DONE, reek 15677**; `Lz77` called throughout `Deflate.codex` and run by `lz77-test` **DONE, reek 15687**; `Deflate` called by `Brotli.codex:2408` and run by `deflate-dynamic-test` **DONE, reek 2026-08-16** (negative `deflate-copy` source refused at all three call sites; `br-exhausted` moved the exhaustion test INSIDE `deflate-dyn-loop`, where `deflate-blocks:788` could not see it, closing an `OUT OF MEMORY` on a truncated dynamic block); `Brotli` run by four harnesses plus `build/brotli-interop-test.ps1` and `brotli-read-test.ps1` **CLOSED with NO guard added, reek 2026-08-16**: the census's "best defended by inspection" held, but a termination arm (`brotli-hostile`) proved a NEGATIVE worth keeping -- deleting the `brotli-valid` gate and widening the `dist < 1` refusal each moved ZERO rows, because hostile headers and truncated streams never decode far enough to reach a distance command. **`brotli-valid` is not load-bearing and its `w <= 24` half is unreachable by construction** (`brotli-read-wbits` answers only 16, 17, 9..15, 18..24). Reaching the real guards needs a crafted bitstream past the Huffman tables, which is a FUZZ-CORPUS job and is the honest residue of this row; **`Rle` has NO caller and gets NO guard (L-UNCALLED)** -- `compress-rle` cites it and only prints a string, `foreword-all-compile` is compile-only, and the `rle-encode`/`rle-decompress` hits elsewhere belong to `apps/data/ColumnStore` and `encode/VideoCodec`, which define their own. Was: LATENT, second sweep: `compress/` entire (`Deflate`, `Lz4`, `Lz77`, `Rle`, `Brotli`); `ai/SafeTensors`, `LoraLoader`, `PngMetadata`, `GpuProxy`, `PromptParser`; `ui/TrueTypeFont` and `GlyphRasterizer`; `core/Tls` `tls-decode-record`, `Pbkdf`, `ChaCha20Poly1305`, `Schedule`, `Pattern`, `Decimal` | KAT-ONLY / NO-TEST | the compressors are the classic shape and every one carries it: `Lz4.codex:130` copies a stream-supplied literal count with no bound against `len`, `:133` reads `after-lit + 1` unchecked, `:160` sends `list-at acc (list-length acc)` on offset 0; `Deflate.codex:827`/`:900` `deflate-copy` from `list-length acc - dist` which can be negative, and `:887-892` with `br-bit:727` answering 0 past the end means a truncated dynamic block loops and allocates without bound; `Lz77.codex:207-218` the same negative source; `Rle.codex:28-35` a stream integer sizes an allocation with no cap; `Brotli` is the best-defended by inspection (`:2370-2371` bounded by the meta-block, `:2405` `dist < 1` refused, `:2409` zero progress refused) but its only hostile arm is the empty stream through `brotli-valid`. **None is reached today**: `lz4-decompress`'s one production caller is `FactArchive.codex:177`, behind a hash check against a signed base, and `FactArchive` itself is cited by nothing outside `codex/test`; the other four have no non-test caller at all. `SafeTensors.codex:174-176`/`:284-287` JSON offsets and shape products drive reads with no bound, reached only by `ModelRegistry`/`LoraLoader`, themselves uncalled. `GlyphRasterizer.codex:200-207` font bbox and `upem` (a zero divisor) size a `w*h` non-tail-recursive allocation, reached only through the uncalled `TrueTypeFont` |

| 16b | ~~`Gguf.codex` dequant~~ CLOSED (val 15603, from reek's measurement) | DONE 2026-08-16 | `gguf-dq8-loop:303` and `gguf-dq4-loop:325` both refuse a block that does not fit before reading its scale. Row 16 carries the account. reek CL 15583 was the parallel implementation and is deleted |
| 20 | **A CLASS, not a file: a bounds guard that ADDS can be overflowed.** `71` sites compare `a + b` against a `list-length` or `text-length`; **34 have a non-constant second operand**. Found by blu in reek's own item-15 `sdw-decode` bound one day after it landed | MEASURED, unowned, NOT swept | `text-to-integer` of a 19-digit field answers i64 max and `46 + i64max` wraps negative, so the sum is under any length and the guard admits what it exists to refuse; the guest died `!EXC=06` against main 15576. The fix is to SUBTRACT (`clen > text-length - (p9 + 1)`), which cannot wrap. **Refusing a negative length is not enough and is the trap**: the value was bounded, the arithmetic was not. `sdw-decode` fixed by blu (15614); `repo-has` made subtractive by reek with an arm, no live path there since every call site passes a constant. The other 33 are per-site judgement in owned files (`Tls`, `Dtls*`, `Asn1`, `TlsCert`, `TlsEndpoint` are the net leg's; `CCE`, `Pattern`, `TextScan` foreword) and most are probably safe. Account: `ExaminersAssay.md` "A Bounds Guard That ADDS Can Be Overflowed" |

**The order to take them now, across both sweeps (2026-08-16):** 3
(`TrustTransport` decode, val) and 16 (`Gguf`, val) are DONE. 12 (`Fat16`) and
17 (`Fat32`) are fester's, 14 (`Gpt`) is red's, 15 (`FactDisk` +
`SourceDefWire`) is reek's, all in flight. Then 8b (`VirtioBlk`, waits for a
bed), 9 and 10, then the latent rows 6, 7, 11, 13 as corpus work. **18 and 19
are reek's, claimed 2026-08-16** (red's routing): item 18's three files are
reached, item 19 is scoped to `compress/` first. 14
and 15 are on the compiler's own boot and store paths and both are inside the
seed's reachable set (`Fat16` cites `Gpt`; `opening.codex` cites `FactDisk`);
whoever takes either takes the token for the gate. **16 was NOT seed-affecting
and needed no token: only `AgentBundle` cites `Gguf`, measured rather than
assumed from the row.**

**How to take one.** The pattern is settled and five worked examples are in
`docs/ExaminersAssay.md` ("The UDP Frame Guard", "The CDX Input Guard", "The
GPT Integrity Guard", "The FAT Geometry Guard", "The Config-Descriptor
Clamp"). Mutate the KAT vector: truncate at every field boundary, extend,
flip each length field to zero and to maximum, zero the magic, nest to the
fuel cap. Require no crash, no hang, and no accept of a mutant that should
be refused. Keep the well-formed vector as the positive control and a
well-formed-but-refused-for-another-reason arm as the discriminator (the
UDP guard's `other-port-frame`). Clamp where the length decides a slice,
refuse where it decides WHERE a read lands. Predict every expected value
before the arm runs, and ablate the guard to prove the arm moves.

### 10.2 The full table

141 rows. Encode-only modules are listed so nobody re-audits them.

| class | module (path) | chapter | decode entry points | test state | evidence (test name or "none found") |
|---|---|---|---|---|---|
| trust decisions | codex/foreword/encode/Asn1.codex | Asn1 | asn1-read, asn1-read-len, asn1-read-tag | NEGATIVE-ARM | codex/test/asn1-der.codex: n-indefinite, n-nonminimal, n-overrun, n-high-tag, n-leading-zero, n-bit-unused |
| trust decisions | codex/foreword/encode/Jwt.codex | Jwt | jwt-decode, jwt-assemble, jwt-assemble-payload | NEGATIVE-ARM | codex/test/apps/jwt-decode-test.codex: two-segments, bad-payload -> JwtMalformed |
| trust decisions | codex/foreword/encode/TlsCert.codex | TlsCert | tls-cert-parse | NEGATIVE-ARM | codex/test/tls-cv-schemes.codex: r-bad-len (truncated cert), r-empty |
| trust decisions | codex/foreword/encode/TrustAnchors.codex | TrustAnchors | ENCODE-ONLY in this module (ta-collect/ta-anchors reuse x509-parse on embedded root DER) | NEGATIVE-ARM (via consumer) | codex/test/web-chain.codex chain-verify negatives; codex/test/tls-fetch-loopback.codex, tls-noauth-loopback.codex |
| trust decisions | codex/foreword/encode/X509.codex | X509 | x509-parse | NEGATIVE-ARM | codex/test/x509-parse.codex: r-trunc, truncated real certificate |
| trust decisions | codex/foreword/encode/X509Chain.codex | X509Chain | x509-chain-verify, x509-time-norm, x509-utc-time | NEGATIVE-ARM | codex/test/x509-chain.codex: r-tampered, r-wrong-key, r-wrong-name, r-early, r-late, r-nbad |
| trust decisions | codex/os/trust/ExternalAuthBridge.codex | ExternalAuthBridge | bridge-authenticate, bridge-identity-of | NEGATIVE-ARM | codex/test/apps/external-auth-bridge-test.codex: junk-token, missing-sub-claim refused |
| trust decisions | codex/os/trust/PeerDiscovery.codex | PeerDiscovery | discovery-process-beacon, beacon-to-peer-address | KAT-ONLY | codex/test/apps/peer-discovery-test.codex, well-formed values only |
| loading authority | codex/os/verify/CdxBinary.codex | CdxBinary | cdx-verify-magic, cdx-verify-content-hash, decode-effects, decode-proof-section | NEGATIVE-ARM | codex/test/apps/cdx-binary-test.codex: cdx-verify-magic on [0,0,0,0] |
| loading authority | codex/os/verify/CdxVerifier.codex | CdxVerifier | verify-integrity, decode-capabilities, verify-proofs | NEGATIVE-ARM | codex/test/apps/verifier-tampered.codex: flipped byte, bad magic |
| loading authority | codex/os/verify/VerifiedLoader.codex | VerifiedLoader | evaluate-load, decode-capabilities (via CdxVerifier) | NEGATIVE-ARM | codex/test/apps/verified-loader-test.codex: 3-byte binary, load denied |
| loading authority | codex/os/verify/VerifyCache.codex | VerifyCache | verify-cdx-cached, cdx-content-hash-hex | KAT-ONLY | codex/test/apps/verifier-cache-test.codex, well-formed signed binaries only |
| loading authority | codex/os/verify/VerifyReport.codex | VerifyReport | verify-cdx-report, read-threshold | NEGATIVE-ARM | codex/test/apps/verifier-report-test.codex: 224-byte all-zero bad-bin rejected |
| loading authority | codex/os/verify/WakeCeremony.codex | WakeCeremony | wake-verify, wake-ceremony | NEGATIVE-ARM | codex/test/apps/wake-ceremony-test.codex: tampered-bin flipped byte |
| session establishment | codex/foreword/encode/Dtls.codex | Dtls | dtls-plain-decode, dtls-plain-decode-body, dtls-open | NEGATIVE-ARM | codex/test/dtls-record.codex: test-tamper, test-short |
| session establishment | codex/foreword/encode/DtlsHandshake.codex | DtlsHandshake | ENCODE-ONLY, no decoder (flight-sequencing state machine) | UNSURE | codex/test/dtls-handshake.codex: test-verdict-bad, test-verdict-other-addr are state-machine verdicts, not raw-byte refusals |
| session establishment | codex/foreword/encode/DtlsHello.codex | DtlsHello | dtls-sh-key-share, dtls-hrr-cookie, dtls-ext-scan | NEGATIVE-ARM | codex/test/dtls-hello.codex: test-liar, chopped ServerHello |
| session establishment | codex/foreword/encode/DtlsMessage.codex | DtlsMessage | dtls-msg-parse, dtls-msg-parse-body, dtls-reasm-add | NEGATIVE-ARM | codex/test/dtls-message.codex: test-short, test-liar; also codex/test/dtls-fragment.codex |
| session establishment | codex/foreword/encode/TlsEndpoint.codex | TlsEndpoint | tep-dec, tep-parse-chain, tep-parse-one | NEGATIVE-ARM | codex/test/apps/tls-noauth-loopback.codex: test-tampered; tls-fetch-loopback.codex: test-wrong-host |
| session establishment | codex/foreword/encode/WebSocket.codex | WebSocket | ws-decode-frame, ws-read-length | KAT-ONLY | codex/test/apps/websocket-vector-test.codex, RFC 6455 vector, well-formed only |
| session establishment | codex/os/trust/Handshake.codex | Handshake | hs-receive-hello, hs-receive-prove | NEGATIVE-ARM | codex/test/apps/handshake-prove-guard.codex: wrong signature, empty signature, short key, short key at hello, plus a real RFC 8032 vector-1 signature and a well-formed hello as controls |
| session establishment | codex/os/trust/TrustTransport.codex | TrustTransport | decode-agent-msg, decode-hello-body, decode-capability | NEGATIVE-ARM | codex/test/apps/agent-msg-truncated.codex: empty bodies for five tags, a cut body, a well-formed body under an unrecognised tag, two truncated hellos, plus three positive controls |
| session establishment | codex/os/net/DtlsEndpoint.codex | DtlsEndpoint | dtls-msg-parse (via dtls-ep-recv), dtls-ep-parse-chain | NEGATIVE-ARM | codex/test/apps/dtls-fragmented-hello.codex: runt 8-byte ClientHello |
| device+industrial | codex/foreword/encode/Bacnet.codex | Bacnet | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/BleAtt.codex | BleAtt | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Canopen.codex | Canopen | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Coap.codex | Coap | coap-parse, coap-scan-options, coap-find-option | NEGATIVE-ARM | codex/test/coap-parse.codex: check-truncated, check-bad-version, check-long-token, check-runaway-option, check-reserved-nibble |
| device+industrial | codex/foreword/encode/CoapEndpoint.codex | CoapEndpoint | coap-ep-recv (delegates to Coap's coap-parse) | NEGATIVE-ARM | codex/test/apps/coap-loopback.codex: token-mismatch dropped (byte-level negatives live in coap-parse.codex) |
| device+industrial | codex/foreword/encode/Dnp3.codex | Dnp3 | ENCODE-ONLY, no decoder | N/A | codex/test/dnp3-encode.codex tests CRC/build only |
| device+industrial | codex/foreword/encode/Enip.codex | Enip | ENCODE-ONLY, no decoder | N/A | codex/test/enip-encode.codex: check-send-rr-data vs reference bytes |
| device+industrial | codex/foreword/encode/Fins.codex | Fins | ENCODE-ONLY, no decoder | N/A | codex/test/fins-encode.codex: check-read-dm vs reference bytes |
| device+industrial | codex/foreword/encode/Goose.codex | Goose | ENCODE-ONLY, no decoder | N/A | codex/test/goose-encode.codex: check-pdu, check-header vs reference bytes |
| device+industrial | codex/foreword/encode/Hart.codex | Hart | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Iec104.codex | Iec104 | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Ieee802154.codex | Ieee802154 | ENCODE-ONLY, no decoder (frame builders and FCS only) | N/A | none found |
| device+industrial | codex/foreword/encode/J1939.codex | J1939 | field extractors decompose a wire CAN id; no function named decode/parse/read | N/A (naming artifact, see 10.3) | none found |
| device+industrial | codex/foreword/encode/Knx.codex | Knx | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Lorawan.codex | Lorawan | lw-decrypt-join-accept, lw-ja-verify work on wire bytes; no function named decode/parse/read | N/A (naming artifact, see 10.3) | none found |
| device+industrial | codex/foreword/encode/Lwm2m.codex | Lwm2m | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Mbus.codex | Mbus | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Melsec.codex | Melsec | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Modbus.codex | Modbus | modbus-parse-registers, modbus-parse-coils | KAT-ONLY | codex/test/modbus-encode.codex: check-parse-registers/coils, well-formed PDUs only |
| device+industrial | codex/foreword/encode/MqttSn.codex | MqttSn | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/OpcUa.codex | OpcUa | ENCODE-ONLY, no decoder (builds HELLO/ACK/UACP only) | N/A | codex/test/opcua-encode.codex tests encoder only |
| device+industrial | codex/foreword/encode/S7comm.codex | S7comm | ENCODE-ONLY, no decoder (s7-read-item builds requests) | N/A | none found |
| device+industrial | codex/foreword/encode/Sixlowpan.codex | Sixlowpan | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Sparkplug.codex | Sparkplug | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | codex/foreword/encode/Zigbee.codex | Zigbee | ENCODE-ONLY, no decoder | N/A | none found |
| device+industrial | apps/works/GopXhci.codex | GopXhci | xhci-speed-resolve, xhci-psi-lookup, xhci-find-protocol | NEGATIVE-ARM | codex/test/apps/xhci-speed-psi.codex: undefined PSIV, uncovered port, PSIC zero, missing extended-cap pointer |
| media+documents | codex/foreword/encode/Avi.codex | Avi | ENCODE-ONLY, no decoder | N/A | none found |
| media+documents | codex/foreword/encode/Bmp.codex | Bmp | bmp-decode, bmp-decode-rows, bmp-decode-row | KAT-ONLY | codex/test/media-codec-test.codex: test-bmp, well-formed round trip only |
| media+documents | codex/foreword/encode/Flac.codex | Flac | ENCODE-ONLY, no decoder | N/A | codex/test/av-codec-test.codex: test-flac, size/magic check on well-formed sample |
| media+documents | codex/foreword/encode/FontGen.codex | FontGen | ENCODE-ONLY, no decoder | N/A | compile-only smoke |
| media+documents | codex/foreword/encode/Gif.codex | Gif | ENCODE-ONLY, no decoder | N/A | codex/test/image-codec-test.codex: test-gif, well-formed sample |
| media+documents | codex/foreword/encode/Gltf.codex | Gltf | ENCODE-ONLY, no decoder | N/A | codex/test/forewords/encode-gltf-bounds.codex, well-formed mesh only |
| media+documents | codex/foreword/encode/Jpeg.codex | Jpeg | ENCODE-ONLY, no decoder | N/A | none found |
| media+documents | codex/foreword/encode/Midi.codex | Midi | ENCODE-ONLY, no decoder | N/A | none found |
| media+documents | codex/foreword/encode/Mp3.codex | Mp3 | ENCODE-ONLY, no decoder | N/A | none found |
| media+documents | codex/foreword/encode/Mp4.codex | Mp4 | ENCODE-ONLY, no decoder | N/A | none found |
| media+documents | codex/foreword/encode/Ogg.codex | Ogg | ENCODE-ONLY, no decoder | N/A | none found |
| media+documents | codex/foreword/encode/Png.codex | Png | ENCODE-ONLY, no decoder | N/A | codex/test/image-codec-test.codex exercises png-encode only |
| media+documents | codex/foreword/encode/Qoi.codex | Qoi | qoi-decode, qoi-decode-pixels, qoi-read-be32 | KAT-ONLY | codex/test/media-codec-test.codex: test-qoi, self-encoded well-formed image |
| media+documents | codex/foreword/encode/Tiff.codex | Tiff | ENCODE-ONLY, no decoder | N/A | codex/test/image-codec-test.codex exercises tiff-encode only |
| media+documents | codex/foreword/encode/TrueType.codex | TrueType | ttf-parse, ttf-read-dir, ttf-read-glyph | KAT-ONLY | codex/test/truetype-bridge-test.codex: one hand-built well-formed embedded font |
| media+documents | codex/foreword/encode/TrueTypeWriter.codex | TrueTypeWriter | ENCODE-ONLY, no decoder | N/A | compile-only smoke |
| media+documents | codex/foreword/encode/VideoCodec.codex | VideoCodec | video-decode-frame | NO-TEST | no test calls video-decode-frame |
| media+documents | codex/foreword/encode/Wav.codex | Wav | ENCODE-ONLY, no decoder | N/A | codex/test/media-codec-test.codex exercises wav-encode-mono only |
| media+documents | apps/works/GopFont.codex, apps/guios/FontLoad.codex | GopFont, FontLoad | gfont-load, fb-ttf-plausible, fb-parse-ttf, fb-glyph-for-char | NEGATIVE-ARM | codex/test/apps/ttf-plausible-guard.codex: runt/trunc/many-tables/head-far/glyphs-huge/hmetrics-zero/cmap-sub-far refused, loca-flag-short accepted, glyph-past-end clamped (2026-08-15) |
| wire framing+net | codex/foreword/encode/Base64.codex | Base64 | base64-decode, b64-decode-char, b64-decode-loop | KAT-ONLY | codex/test/apps/base64-test.codex: test-decode-hello, well-formed only |
| wire framing+net | codex/foreword/encode/Bencode.codex | Bencode | ben-decode, ben-decode-at, ben-decode-int | KAT-ONLY | codex/test/apps/wave2-test.codex: test-bencode, round trip only |
| wire framing+net | codex/foreword/encode/Cbor.codex | Cbor | cbor-decode, cbor-decode-at, cbor-read-arg | KAT-ONLY | codex/test/lib/cbor-test.codex, well-formed round trips only |
| wire framing+net | codex/foreword/encode/Crc32.codex | Crc32 | checksum only, no decoder | N/A | codex/test/apps/crc32-test.codex tests checksum correctness only |
| wire framing+net | codex/foreword/encode/Csv.codex | Csv | csv-parse, csv-parse-row, csv-parse-quoted | KAT-ONLY | codex/test/apps/csv-rfc4180-test.codex, well-formed cases only, no unterminated-quote or truncation case |
| wire framing+net | codex/foreword/encode/GrayCode.codex | GrayCode | gray-decode (in-memory Integer, not foreign bytes; see 10.3) | KAT-ONLY | codex/test/apps/graycode-test.codex: test-roundtrip |
| wire framing+net | codex/foreword/encode/Hex.codex | Hex | hex-decode, hex-decode-loop, hex-char-value | KAT-ONLY | codex/test/apps/hex-test.codex: test-decode, test-roundtrip |
| wire framing+net | codex/foreword/encode/Ini.codex | Ini | ini-parse, ini-parse-lines, ini-parse-line | NO-TEST | codex/test/forewords/encode-ini.codex cites Ini but never calls ini-parse |
| wire framing+net | codex/foreword/encode/Json.codex | Json | json-parse, json-parse-value, json-parse-object | NEGATIVE-ARM | codex/test/forewords/encode-json-numbers.codex: "1.", "1e", "1e+", ".5", "1e99" all refused |
| wire framing+net | codex/foreword/encode/Markdown.codex | Markdown | md-parse, md-parse-lines, md-parse-line | KAT-ONLY | codex/test/smtp-md-test.codex, well-formed markdown only |
| wire framing+net | codex/foreword/encode/MessagePack.codex | MessagePack | msgpack-decode, mp-decode-at, mp-decode-fixstr | KAT-ONLY | codex/test/lib/msgpack-test.codex, well-formed round trips only |
| wire framing+net | codex/foreword/encode/Mqtt.codex | Mqtt | mqtt-parse, mqtt-decode-vbi | KAT-ONLY | codex/test/apps/mqtt-loopback.codex, well-formed frames; negatives are protocol-state checks, not byte refusals |
| wire framing+net | codex/foreword/encode/MqttEndpoint.codex | MqttEndpoint | mqtt-ep-recv drives mqtt-parse | KAT-ONLY | codex/test/apps/mqtt-loopback.codex, as Mqtt |
| wire framing+net | codex/foreword/encode/Protobuf.codex | Protobuf | pb-decode-varint, pb-decode-field, pb-read-u32 | KAT-ONLY | codex/test/apps/wave3-test.codex: test-protobuf, self-encoded well-formed bytes |
| wire framing+net | codex/foreword/encode/Smtp.codex | Smtp | smtp-parse-response, smtp-parse-code | KAT-ONLY | codex/test/smtp-md-test.codex: test-smtp-response, well-formed "250 OK" |
| wire framing+net | codex/foreword/encode/Sntp.codex | Sntp | sntp-read-u32-be | KAT-ONLY | codex/test/sntp-encode.codex: check-roundtrip, self-built packet |
| wire framing+net | codex/foreword/encode/Toml.codex | Toml | toml-parse, toml-parse-lines, toml-parse-kv | KAT-ONLY | codex/test/lib/toml-test.codex, well-formed only |
| wire framing+net | codex/foreword/encode/Uri.codex | Uri | uri-parse, uri-parse-relative, uri-parse-authority | KAT-ONLY | codex/test/apps/uri-test.codex, well-formed URLs only |
| wire framing+net | codex/foreword/encode/Uuid.codex | Uuid | uuid-from-bytes | KAT-ONLY | codex/test/apps/uuid-test.codex: test-from-bytes, fixed 16 bytes |
| wire framing+net | codex/foreword/encode/Yaml.codex | Yaml | yaml-parse, yaml-parse-block, yaml-parse-scalar | KAT-ONLY | codex/test/lib/yaml-test.codex, well-formed only |
| wire framing+net | codex/os/net/Ethernet.codex | Ethernet | arp-parse, eth-ethertype, eth-payload | NEGATIVE-ARM | codex/test/tcp-checksum-refuse.codex: bad-ip-frame; codex/test/udp-frame-guard.codex: cut-frame, bad-hdr-frame, bad-cksum-frame |
| wire framing+net | codex/os/net/NetworkStack.codex | NetworkStack | net-process-frame, net-process-ip, net-process-arp | NEGATIVE-ARM | codex/test/tcp-checksum-refuse.codex, codex/test/udp-frame-guard.codex, codex/test/arp-cache-bound.codex |
| wire framing+net | codex/os/net/Tcp.codex | Tcp | tcp-parse | NEGATIVE-ARM | codex/test/tcp-checksum-refuse.codex: checksum bit flip |
| wire framing+net | codex/os/net/Udp.codex | Udp | udp-parse, udp-parse-ip-payload | NEGATIVE-ARM | codex/test/udp-frame-guard.codex: bad-hdr-frame, bad-cksum-frame |
| wire framing+net | codex/os/net/UdpIO.codex | UdpIO | udp-io-mine (via ip-payload/eth-payload) | NEGATIVE-ARM | codex/test/udp-frame-guard.codex: cut and corrupted frames |
| wire framing+net | codex/os/net/MessageFraming.codex | MessageFraming | frame-decode-length, frame-decode-tag, frame-decode-body/text/bytes | NEGATIVE-ARM | codex/test/frame-short-buffer.codex: 4-byte truncated buffer; refusal channel `valid` at main 15375 |
| wire framing+net | codex/os/net/TcpTransport.codex | TcpTransport | transport-try-recv, recv-buf-i32, transport-process-frame | NEGATIVE-ARM | codex/test/apps/tcp-transport-guard.codex: over-cap feed, msg-len 0, msg-len past recv-cap, plus a well-formed positive control |
| wire framing+net | codex/os/net/WebServer.codex | WebServer | wb-request-total, wb-headers-end, wb-content-length | NEGATIVE-ARM | codex/test/web-mux-reassembly.codex: get-partial, truncated request across TCP segments |
| wire framing+net | codex/os/net/DnsResolver.codex | DnsResolver | dns-parse-response, dns-parse-answers | NEGATIVE-ARM | codex/test/apps/dns-wire-test.codex: [1,2,3] -> valid=False; codex/test/dns-answer-count.codex |
| wire framing+net | codex/os/net/Dhcp.codex | Dhcp | dhcp-parse-response, dhcp-parse-options | NEGATIVE-ARM | codex/test/apps/dhcp-test.codex: [1,2,3,4] -> valid=False |
| wire framing+net | codex/os/net/DhcpIO.codex | DhcpIO | delegates to dhcp-parse-response | KAT-ONLY | codex/test/dhcp-acquire.codex and siblings, live-NIC well-formed replies only |
| wire framing+net | codex/os/net/Ntp.codex | Ntp | ntp-parse-response | NEGATIVE-ARM | codex/test/apps/ntp-test.codex: test-invalid, 3-byte short buffer |
| wire framing+net | codex/os/net/Tftp.codex | Tftp | tftp-parse, tftp-decode-string | KAT-ONLY | codex/test/apps/tftp-test.codex, self-built well-formed packets |
| wire framing+net | codex/os/net/Syslog.codex | Syslog | syslog-parse, syslog-parse-digits, syslog-decode-bytes | KAT-ONLY | codex/test/apps/syslog-test.codex: test-parse, self-built message |
| wire framing+net | codex/os/net/Icmp.codex | Icmp | icmp-parse | KAT-ONLY | codex/test/apps/icmp-test.codex, self-built well-formed packets; no production caller (CurrentPlan Track B) |
| wire framing+net | codex/os/net/HttpClient.codex | HttpClient | http-parse-response, read-line-from-bytes, read-body-from-bytes | NEGATIVE-ARM | codex/test/apps/http-response-guard.codex: big-status, neg-status, big-cl, neg-cl refused; runt/empty/one-cr; high-body accepted (2026-08-15) |
| wire framing+net | codex/os/net/HttpFetch.codex | HttpFetch | http-parse-response, dns-parse-response, udp-parse on fetched bytes | NO-TEST | citing tests are effect-scope only |
| wire framing+net | codex/os/net/ImapClient.codex | ImapClient | imap-parse-response-kind, imap-parse-exists, imap-parse-header-line | NO-TEST | codex/test/imap-wire.codex covers command encoding only |
| wire framing+net | codex/os/net/OAuthClient.codex | OAuthClient | oauth-parse-tokens | NO-TEST | codex/test/oauth-island.codex never calls oauth-parse-tokens |
| wire framing+net | apps/works/RepoProtocol.codex | RepoProtocol | decode-annotation, decode-verdict, decode-proposal-payload | NEGATIVE-ARM | codex/test/apps/repo-frame-truncated.codex: cut frames at exact boundary, past-end verdict-kind, negative-index byte-at |
| wire framing+net | apps/works/RepoProtocolPersist.codex | RepoProtocolPersist | source-def-from-text, tombstone-from-text, proposal-from-persist-text | NEGATIVE-ARM | codex/test/apps/repo-tombstone-replay.codex: replayed and stale-timestamp records must not override newer ones |
| wire framing+net | apps/works/Http.codex | Http | http-parse-request, http-find-blank-line, http-find-byte | NEGATIVE-ARM | codex/test/apps/http-test.codex: test-invalid, [0,0,0] -> valid=False |
| wire framing+net | apps/works/WebServer.codex | WebServer | web-server-handle (calls http-parse-request) | KAT-ONLY | codex/test/apps/web-server-test.codex, well-formed GET requests only |
| wire framing+net | apps/works/AgentBundle.codex | AgentBundle | ab-parse-model, ab-window-step, gguf-parse-header (via wrapper) | KAT-ONLY | codex/test/apps/bundled-agent.codex, bundled-agent-heap.codex, valid bundled model only; no digest-mismatch or manifest-size refusal test |
| storage | codex/os/kernel/DiskFacts.codex | DiskFacts | unpack-superblock, unpack-u32-le, sysdb-unpack-identity | NEGATIVE-ARM | codex/test/apps/disk-facts-mbr-guard.codex, disk-facts-gpt-guard.codex: foreign MBR/GPT present, write refused |
| storage | codex/os/kernel/FatReader.codex | FatReader | fat-parse-bpb, fat-parse-dir-entry | KAT-ONLY | codex/test/fat32-parse.codex, spec-offset fixtures, all well-formed |
| storage | codex/os/kernel/DriveManager.codex | DriveManager | dm-read-identify-word, dm-check-codexfs, dm-read-oem, gpt-read | UNSURE | codex/test/apps/install-to-drive.codex reports NO GPT/NO ESP when absent; no truncated or corrupted-buffer test found |
| storage | apps/works/GopFat16.codex | GopFat16 | gpt-esp-start, gfat-parse-bpb, gfat-mount-at | NEGATIVE-ARM | codex/test/apps/gpt-hdr-crc-guard.codex, gpt-array-crc-guard.codex, gpt-array-geom-guard.codex, gop-fat16-geom-guard.codex; the cluster walk is WORKS-29 (open) |
| storage | apps/works/GopBoot.codex | GopBoot | gpt-sig-match, gpt-sig-byte | NO-TEST | signature check feeds a diagnostic status line only |
| device descriptors | codex/os/kernel/Usb.codex | Usb | usb-parse-device-desc, usb-parse-endpoint, usb-parse-interface | NEGATIVE-ARM | codex/test/usb-desc-guard.codex (reek 2026-08-15): all three already guarded their upper bound and degraded to zeroed records, and nothing asserted it; the arm pins the degrade. The one hole left is a NEGATIVE offset, which passes every `offset + n > len` test and then indexes backwards -- no caller can currently produce one, so it is recorded rather than guarded |
| device descriptors | codex/os/kernel/UsbHid.codex | UsbHid | hid-scan-interfaces, hid-scan-loop, hid-classify-device | NEGATIVE-ARM | **A real defect, found and fixed 2026-08-15 (reek).** hid-scan-loop bounded its offset against the callers total and never against list-length desc, and that total is a device wTotalLength: a 25-byte config claiming 200 died !EXC=06 with 25 in R12 and 200 in R13. Clamped at hid-scan-interfaces; arm in codex/test/usb-desc-guard.codex, honest and lying totals answering the same interface count |
| device descriptors | codex/os/kernel/UsbAudio.codex | UsbAudio | usb-audio-detect, usb-interleave, uaf-pcm-bytes | KAT-ONLY | codex/test/apps/hda-codec-test.codex, codex/test/usb-test.codex, fixed well-formed records |
| device descriptors | codex/os/kernel/Pci.codex | Pci | pci-scan-loop, pci-read-config, pci-parse-bar | KAT-ONLY | codex/test/apps/pci-scan-test.codex, synthetic well-formed records |
| device descriptors | codex/os/kernel/Arm64Pci.codex | Arm64Pci | arm64-pci-read-dev, ecam-read-32, arm64-pci-scan-loop | NEGATIVE-ARM | codex/test/arm64-send-refusal.codex: all-zero dead PCI/virtio device, send path must refuse |
| device descriptors | codex/os/kernel/VirtioPci.codex | VirtioPci | virtio-walk-caps, virtio-find-cap, virtio-cap-bar-offset | NEGATIVE-ARM | codex/test/arm64-send-refusal.codex, same dead-device fixture |
| device descriptors | codex/os/kernel/Ne2k.codex | Ne2k | ne2k-read-from-buf, ne2k-stage-frame | KAT-ONLY | codex/test/apps/nic-recv.codex, one well-formed ARP frame |
| device descriptors | codex/os/kernel/E1000e.codex | E1000e | e1000-read-bytes, e1000-take-frame, e1000-phy-read | NEGATIVE-ARM | codex/test/e1000-phy-absent.codex: PHY read on absent device returns error value |
| device descriptors | codex/os/kernel/VirtioNet.codex | VirtioNet | virtio-net-read-frame-bytes, virtio-net-poll-frame, virtio-net-read-mac | NEGATIVE-ARM | codex/test/arm64-send-refusal.codex, same dead-device fixture |
| device descriptors | codex/os/kernel/VirtioBlk.codex | VirtioBlk | virtio-blk-read-sector, virtio-blk-read-buf, virtio-blk-read-sectors | NO-TEST | Looked at and NOT taken 2026-08-15 (reek): it is neither USB nor a descriptor, so it sits outside the lane-3 wording, and it wants placing by the census owner rather than grabbing. What is there to place: the ring index at peek-16 d0 14 is written by the DEVICE and followed as an index without a bound, which is the class one structure over from a descriptor length |
| device descriptors | codex/os/kernel/HdaAudio.codex | HdaAudio | hda-capture, hda-peak, hda-peak-loop | KAT-ONLY | codex/test/apps/hda-audio.codex, hda-tone.codex, well-formed tone/capture |
| device descriptors | codex/os/kernel/VmIde.codex | VmIde | vm-ide-handle-out, vm-ide-get-lba, vm-ide-start-read | NO-TEST | codex/test/apps/ide-pio-read.codex exercises the Works-layer GopDisk chapter, not this one |
| device descriptors | codex/os/kernel/VmSerial.codex | VmSerial | vm-serial-handle-out, vm-serial-handle-in, vm-serial-get-output | KAT-ONLY | codex/test/apps/vmx-serial-test.codex, one well-formed guest program |
| device descriptors | codex/os/kernel/Keyboard.codex | Keyboard | kb-process-scancode, kb-process-printable, kb-poll | KAT-ONLY | codex/test/apps/keyboard-test.codex: test-press-a, test-shift-a, test-release |
| device descriptors | codex/os/kernel/Mouse.codex | Mouse | mouse-read-packet, mouse-poll, mouse-clamp | NO-TEST | codex/test/mouse-decode.codex tests apps/works/GopUsbMouse, not this chapter |
| device descriptors | codex/os/kernel/GpuBridge.codex | GpuBridge | gpu-get-tensor, gpu-bridge-matmul, gpu-header | NEGATIVE-ARM | codex/test/gpu-doorbell.codex: mis-shaped 200x200 tensor refused |
| device descriptors | apps/works/GopUsb.codex | GopUsb | usb-inspect, usb-has-boot-hid, usb-hid-walk | NEGATIVE-ARM | codex/test/apps/usb-cfg-total-guard.codex: honest/liar/poison/big/runt/zerolen wTotalLength arms |
| device descriptors | apps/works/GopUsbMsc.codex | GopUsbMsc | msc-find-interface, msc-find-bulk-off, msc-read-capacity | NEGATIVE-ARM | codex/test/apps/usb-cfg-total-guard.codex, poisoned config descriptor |
| device descriptors | apps/works/GopUsbKbd.codex | GopUsbKbd | kbd-find-interface, kbd-find-int-in, kbd-open-port | COVERED BY THE CLAMP | Resolved from UNSURE 2026-08-15 (reek). These walk a RAW BUFFER with peek-byte, not a List, so an over-run reads adjacent heap instead of faulting and no arm of their own can catch it from the inside; what bounds them is the total handed in. Both call sites now pass usb-cfg-total (GopUsb.codex and GopUsbKbd.codex), and codex/test/apps/usb-cfg-total-guard.codex poison arm is the evidence: unclamped, a walk of this shape reports an interface planted past the fetch. The five usb-kbd-* tests do exercise them, on well-formed descriptors only, which is what UNSURE was recording |
| device descriptors | apps/works/GopUsbMouse.codex | GopUsbMouse | mouse-find-interface, mouse-consume, mouse-fold | KAT-ONLY | codex/test/mouse-decode.codex, hand-derived well-formed values only |
| device descriptors | apps/works/GopUsbCam.codex | GopUsbCam | cam-find-stream-iface, cam-find-isoch-in, cam-open | KAT-ONLY | codex/test/apps/usb-cam-frame.codex, emulated UVC camera, well-formed only |
| other | codex/os/trust/FactSync.codex | FactSync | sync-decode-hashes, sync-decode-facts, sync-decode-fact | KAT-ONLY | codex/test/apps/fact-sync-wire-test.codex, well-formed payloads only |
| other | codex/os/net/Lwm2mClient.codex | Lwm2mClient | lwm2m-client-recv (via coap-parse) | KAT-ONLY | codex/test/apps/lwm2m-client.codex, well-formed CoAP datagrams only |
| other | codex/os/net/Lwm2mFirmware.codex | Lwm2mFirmware | fw-feed-response (via coap-parse) | KAT-ONLY | codex/test/apps/ota-lwm2m-loopback.codex, well-formed block transfer only |

**Second sweep (red, 2026-08-16): the rest of `codex/foreword/`.** 357
files read; 43 chapters carry a decoder and get a row; the 314 that do not
are named per directory at the end so nobody re-audits them. Reachability
column: a production caller outside the chapter and outside `codex/test`,
or UNCALLED. `list-at`, `char-at` and `substring` trap out of range on this
runtime, so an unbounded index in these rows is a guest kill, not a silent
over-read; `peek-byte` on a raw buffer does not trap, so those rows are
silent adjacent-heap reads (`Gpt`, `KeyboardLayout`).

| class | module (path) | chapter | decode entry points | test state | evidence; reachability; hazard seen (file:line) |
|---|---|---|---|---|---|
| storage | codex/foreword/core/Gpt.codex | Gpt | gpt-read, gpt-read-after-hdr, gpt-parse-entry | KAT-ONLY | install-to-drive.codex reads back our own gpt-write-table; REACHED DiskFacts.codex:501, Fat16.codex:1258; entry-size 1..127 admitted (:154-156, :196-197) and gpt-parse-entry peeks past the 512-byte sector, count and start-lba unbounded (:201) |
| storage | codex/foreword/core/Fat32.codex | Fat32 | fat32-parse-bpb32, fat32-resolve-path, fat32-read-bytes | KAT-ONLY | install-to-drive.codex on our own fat32-format volume; REACHED DriveManager.codex:270, FontLoad.codex:610; :44 divides by BPB byte 13 unchecked, :206 de-size is the read length, :135/:146 chain walks have no cycle guard |
| storage | codex/foreword/core/Fat16.codex | Fat16 | fat16-parse-bpb, fat16-read-dir-entry, fat16-read-bytes, fat16-next-cluster | KAT-ONLY | fat16-write/subdir/mkdir/dirgrow, fat-sink-big: write-then-read on our own volumes; the geom-guard refusal lives in Works GopFat16 above it; REACHED opening.codex:978/1560/1958, GopFat16.codex:213/331; **fester's, item 12**. `fat16-read-text` keeping CR (CCE 255) is an ASSERTED CONTROL, `codex/test/fat16-source-cr.expected` pins `41 58 255 1 50 48` against `fat16-read-source` dropping byte 13; not a finding (blu, 2026-08-16) |
| storage | codex/foreword/core/FactDisk.codex | FactDisk | store-read-bundle, fd-fold, fd-entry-content | KAT-ONLY | factdisk-read.codex well-formed sidecar; REACHED opening.codex:2101; :94 alloc-bytes sized by the entry's clen, bounded only by the superblock's u64 log head (:60, :141) |
| storage | codex/foreword/core/FactLog.codex | FactLog | fl-entry-content-len, fl-sb-log-head, fl-magic-ok | NEGATIVE-ARM (magic only) | factlog-layout.codex test-magic-bad; REACHED FactDisk.codex:133, DiskFacts.codex:293, AppPersist.codex:111; fixed-offset readers, no length hazard |
| storage | codex/foreword/core/SourceDefWire.codex | SourceDefWire | sdw-decode, sdw-hex-decode | NEGATIVE-ARM (shape only) | foreword-source-def-wire.codex: eight pipes, ninth empty, not the format; REACHED FactDisk.codex:104, RepoProtocolPersist.codex:159; :164 substring by a record-supplied clen never checked against the line |
| storage | codex/foreword/core/OtaBoot.codex | OtaBoot | boot-load, boot-verify-candidate, boot-run | NEGATIVE-ARM | ota-boot-rollback.codex run-corrupt, run-no-flag; boot-load REACHED FirstBoot.codex:223, the other two UNCALLED; :204-206 a 32-bit flash length word drives sha256-buf with only len <= 0 refused |
| storage | codex/foreword/core/OtaUpdate.codex | OtaUpdate | gate-a-check-magic, gate-a-verify-block, gate-b-run-all | NEGATIVE-ARM | ota-gate-real.codex truncated/empty/ELF/PE magic; ota-gate-block.codex short/empty digest; only ota-step REACHED (Lwm2mFirmware.codex:11); length-checked |
| session establishment | codex/foreword/core/Tls.codex | Tls | tls-decode-record, tls13-record-decrypt, tls13-strip-inner | NEGATIVE-ARM (decrypt) / KAT-ONLY (decode-record) | tls13-record.codex tamper, wrong-seq; tls-test.codex decode-record on our own encoder output; decrypt REACHED TlsEndpoint.codex:109, tls-decode-record UNCALLED; :98-101 indexes bytes 0..4 with no length check |
| trust decisions | codex/foreword/core/Ed25519.codex | Ed25519 | ed25519-verify, ge-from-bytes | NEGATIVE-ARM (value-tampered only) | edvector.codex tampered sig, ed25519-sign-test wrong msg/key; no short-key or short-sig arm; REACHED ImportGate.codex:53, TlsCert.codex:150, Handshake.codex:50; :503-504 list-at bs 31 with no length check on key or R (val's item-2 finding, the callers now guard it) |
| trust decisions | codex/foreword/core/EcdsaP256.codex | EcdsaP256 | ecdsa-p256-verify, ecdsa-p384-verify | NEGATIVE-ARM | ecdsa-p256.codex bad-r, zero-s, bad-pub, short-sig; ecdsa-p384.codex short key; REACHED X509Chain.codex:201-231, TlsCert.codex:168 |
| trust decisions | codex/foreword/core/Rsa.codex | Rsa | rsa-verify-pkcs1-sha256, rsa-verify-pss-sha256 | NEGATIVE-ARM | rsa-verify.codex bad, short; rsa-pss.codex bad-first/last, short, salt0/48, topbit, sep; REACHED X509Chain.codex:181-187, TlsCert.codex:190 |
| trust decisions | codex/foreword/core/AesGcm.codex | AesGcm | aesgcm-decrypt | NEGATIVE-ARM | aesgcm-test.codex tampered ct, aesgcm-vector-test wrong aad; REACHED Tls.codex:508, Dtls.codex:217, VaultCrypto.codex:128 |
| trust decisions | codex/foreword/core/ChaCha20Poly1305.codex, Poly1305.codex | ChaCha20Poly1305, Poly1305 | chacha20poly1305-decrypt, poly1305-verify | NEGATIVE-ARM | chacha20poly1305.codex bad ct, bad aad; poly1305.codex verify-bad; both UNCALLED |
| trust decisions | codex/foreword/core/Pbkdf.codex | Pbkdf | pbkdf-verify | GUARDED (reek 2026-08-16) | this row's ":159 compare length is the stored hash's" was RIGHT and was the whole finding: longer stored hash killed the guest, shorter one verified a prefix. `pbkdf-stored-guard`, 7 rows, 2 guards ablated. "stored params drive the cost unbounded" still stands and is deliberately not clamped |
| trust decisions | codex/foreword/core/Aes256.codex | Aes256 | aes256-pkcs7-unpad, aes256-cbc-decrypt | NO-TEST | none found (crypto-vectors covers block encrypt/decrypt only); REACHED IdentityManager.codex:109-110, GopWizard.codex:494-495; :150 pad byte 1..16 not checked against len (negative take answers [], benign) |
| wire framing+net | codex/foreword/core/CCE.codex | CCE | utf8-bytes-to-text, utf8-code-point-at, cce-decode-at | NEGATIVE-ARM (utf8) / KAT-ONLY (cce-decode-at) | utf8-cce-test.codex utf8-truncated-dropped, utf8-stray-cont-dropped; REACHED Validation.codex:129, plug-source.codex:3418; :167-176 cce-decode-at reads offset+1..+3 with no check against the text length |
| wire framing+net | codex/foreword/core/Parse.codex | Parse | parse-decimal, parse-hex, text-to-integer | KAT-ONLY | parse-test.codex, neg-int-parse.codex; REACHED opening.codex:1153, PromptParser.codex:113 |
| wire framing+net | codex/foreword/core/Schedule.codex, Pattern.codex, Decimal.codex | Schedule, Pattern, Decimal | schedule, pattern, dec-from-text (user text) | KAT-ONLY | final-batch-test.codex, decimal-test.codex, well-formed only; all UNCALLED |
| device descriptors | codex/foreword/core/KeyboardLayout.codex | KeyboardLayout | kbl-lookup | KAT-ONLY | keyboard-layout-test.codex (unknown NAME arm only); REACHED Keyboard.codex:67; :30 upper bound only, peek-byte tbl (sc*2) on a negative sc |
| device descriptors | codex/foreword/core/Board.codex | Board | mmio-read-32, flash-wait-ready | KAT-ONLY | timer-registers.codex, hpet-interrupt.codex read live counters; mmio-read-32 REACHED Esp32C6Board.codex:39; fuel-capped :277 |
| device descriptors | codex/foreword/engine/Input.codex | Input | input-set-key, input-key-down (device scancode as index) | NO-TEST | none found; UNCALLED outside chapter; bounded 0..255 (:54-55, :92-93) |
| device descriptors | codex/foreword/gpu/DeviceBuffer.codex | DeviceBuffer | db-read-cursor (cursor word read from device memory), device-buffer-alloc | KAT-ONLY | device-buffer-alloc.codex fresh cursor only; UNCALLED outside tests; floor clamp :48, ceiling :68 |
| device descriptors | codex/foreword/ui/InputSource.codex | InputSource | raw-input-poll, ri-build, ri-collect-events | NEGATIVE-ARM | input-metal.codex arm D (mailbox 9999,9000 clamped to the panel), floating-bus 0x00/0xFF as no mouse; REACHED apps/circuits/opening.codex:148, AppRunner.codex:44 |
| device descriptors | codex/foreword/ui/KeyInput.codex | KeyInput | poll-key, poll-key-decode, efi-key-decode | KAT-ONLY | keys-collision/mods/numpad/shift/sidecar well-formed timelines; no garbage-scancode arm; REACHED widely (vision, IdentityManager.codex:362, GopBrowser.codex:123); no bounds hazard, see 10.3 for a contract note |
| media+documents | codex/foreword/ai/Gguf.codex | Gguf | gguf-parse-header, gguf-tensor-info-offset, gguf-md-scan, gguf-parse-tensor-info | NEGATIVE-ARM (header only) | gguf-test.codex test-invalid-header (4 bytes); the rest KAT (test-valid-header, test-string, dequant arms; build/gguf-foreign-test.ps1 real llama.cpp prefixes); REACHED AgentBundle.codex:297-317 from DevConsole.codex:313; :151-155 u64 count times u64 stride with no list-length guard, :185-198 md-scan unguarded, :213-215 ndim sizes the shape |
| media+documents | codex/foreword/ai/SafeTensors.codex | SafeTensors | st-parse-file, st-find-tensor, st-load-tensor | NEGATIVE-ARM (header only) | foreword-safetensors.codex short file, garbage header; loads and shapes KAT; reached only by ModelRegistry.codex:103 and LoraLoader.codex:53, both UNCALLED; :174-176/:201-203/:230-233 JSON offsets and :284-287 shape products drive reads unbounded, offset-end never compared |
| media+documents | codex/foreword/ai/LoraLoader.codex, ModelRegistry.codex, PngMetadata.codex, PromptParser.codex | LoraLoader, ModelRegistry, PngMetadata, PromptParser | lora-load; model-entry-from-file; png-read-metadata; parse-prompt | NO-TEST | compile-only cites; all UNCALLED; PngMetadata :116-125 chunk-len off the data sets the extract count with no list-length guard, LoraLoader :70-72 shape dims size tensors unchecked against data |
| media+documents | codex/foreword/ai/GpuProxy.codex | GpuProxy | gpu-parse-result, gpu-f32-bytes-to-tensor | KAT-ONLY | gpu-proxy-test.codex self-built buffers; UNCALLED outside test; :194-198 fixed reads no length check, :152-155 rows*cols device-reported drives the loop |
| media+documents | codex/foreword/ui/TrueTypeFont.codex, GlyphRasterizer.codex | TrueTypeFont, GlyphRasterizer | ttf-load-font (delegates to encode ttf-parse); gr-render-glyph, gr-render-glyph-aa | KAT-ONLY | truetype-bridge-test.codex, truetype-render-test.codex, one embedded valid font; UNCALLED (FontExtract calls encode ttf-parse directly); GlyphRasterizer :200-207 bbox and upem off the font size w*h (non-tail recursion, :114-117), upem zero divides |
| media+documents | codex/foreword/ui/Markdown.codex | Markdown | ui-md-parse, md-parse-inline | KAT-ONLY | markdown-ordered.codex; REACHED CallPage, RiverPage, Notebook, WorkflowHtml, DetailPane; indices bounded by n, md-at:137 quadratic per line (cost only) |
| compression | codex/foreword/compress/Deflate.codex | Deflate | deflate-decompress, deflate-blocks, deflate-dynamic-block | KAT-ONLY | deflate-dynamic-test.codex six round trips; UNCALLED (Brotli reuses BitReader/dh-decode-sym/deflate-copy, nothing calls deflate-decompress); :827/:900 negative copy source, :800 stored LEN unchecked, :887-892 with :727 loops on a truncated dynamic block |
| compression | codex/foreword/compress/Lz4.codex | Lz4 | lz4-decompress, lz4-decompress-loop, lz4-read-extra-length | KAT-ONLY | lz4-test.codex four round trips; repo-archive.codex "open tampered" is caught by FactArchive's hash at :171 before :177 decodes; production caller FactArchive.codex:177 only, and FactArchive is cited by nothing outside codex/test; :130 literal count unbounded against len, :133 after-lit+1 unchecked, :158-160 offset 0 indexes past acc |
| compression | codex/foreword/compress/Lz77.codex | Lz77 | lz77-decompress | KAT-ONLY | lz77-test.codex round trips and []; UNCALLED; :207-218 negative source, mlen unbounded |
| compression | codex/foreword/compress/Rle.codex | Rle | rle-decode | NO-TEST | none found (rle-encode in db-full-test is Data/ColumnStore's); UNCALLED; :28-35 count sizes an allocation with no cap |
| compression | codex/foreword/compress/Brotli.codex | Brotli | brotli-decompress, brotli-blocks, brotli-cmd-loop, brotli-valid | NEGATIVE-ARM (empty stream only) | brotli-test.codex brotli-invalid (brotli-valid []); b01-b24, brotli-ctx2-test, brotli-dict-test, build/brotli-read-test.ps1 all KAT; UNCALLED; :2370-2371 bounded by need, :2405 dist < 1 refused, :2409 zero progress refused; :1901 mlen up to 2^24 from a tiny header sizes the output, :2493-2495 fill-lens delta unbounded against n |
| compression | codex/foreword/compress/Huffman.codex, BrotliDict.codex, BrotliDictIndex.codex | (3) | ENCODE-ONLY / embedded constant | N/A | huffman-test.codex encoder side only |
| N/A, second sweep | codex/foreword/core (104): Aes, Arm64Encoder, Audio, BigInt, BitSet, BloomFilter, BPlusTree, Camera, Capability, ChaCha20, Channel, CircularBuffer, Cmac, Collate, ComplianceBuild, ComplianceEvidence, Concurrent, ConsistentHash, Console, CountMinSketch, CryptoBig, DateTime, Deque, DiffieHellman, Display, EditDistance, Either, ElasticBloom, ElasticHash, EventBus, FactStore, FileSystem (delegates to Fat16), Format, Fuel, FunnelHash, Graph, Hamt, History, Hkdf, Hmac, Identity, ImportGate (hands the sig to Ed25519, parses nothing), InductiveList, IntervalTree, Iterate, KvStore, Linear, List, ListUtils, LoadTest, Locale, Location, LocationStub, Logger, LruCache, MathLib, Maybe, MemoryMap, Microphone, Network, NumberTheory, Pair, Path, Pipeline, PriorityQueue, Probability, ProofOfWork, Queue, Random, RankedTextSet, RateLimiter, Regex, Result, RingBuffer, RiscV32CEncoder, RiscVEncoder, Rope, Scheduler, SensorData, Sensors, SensorsStub, SerialLine, SessionTypes, Set, Sha1, Sha256, Sha512, Sort, State, Statistics, StringBuilder, StringUtils, TabComplete, TextScan, TextSearch, TextWrap, Thumb2Encoder, Time, TimingWheel, Trie, Tuple, Unicode, UnionFind, Units | | no parser of foreign bytes | N/A | |
| N/A, second sweep | codex/foreword/ai (36): Activation, Attention, ClipInterrogator, ControlNet, Conv2d, DecisionTree, DiffusionPipeline, DiffusionScheduler, Embedding, FaceRestore, FluxPipeline, GeneticAlgorithm, HiresFix, ImageTensor, ImageTo3d, Inpainting, KNearestNeighbor, KvCache, Loss, NeuralNet, Normalization, Optimizer, Reservoir, Sampler, Sampling, SparseLattice, Tensor, TextEncoder, TextEncoderXL, Tokenizer, Transformer, UNet, UNetXL, Upscaler, VaeDecoder, VaeTiling (the model consumers take shapes from config and tensors from SafeTensors, no independent parse) | | no parser of foreign bytes | N/A | |
| N/A, second sweep | codex/foreword/ui (45): Accessibility, Animation, AppRunner, Binding, BoxModel, Canvas, Charts, Clipboard, CommandPalette, Cursor, DataTable, DetailPane, Dialog, Drag, Dropdown, Editor, Event, FilterableList, Focus, Font, FontAtlas, GpuRender, Icon, Layout, Orchestrator, Overlay, PixelBuf, Render, RichText, Scroll, SearchBar, Selection, SettingsPanel, Sound, StatusBadge, Surface, TextField, TextOverflow, Theme, Touch, TreeView, Validation, Vector, Widget, Window | | no parser of foreign bytes | N/A | |
| N/A, second sweep | codex/foreword/engine (42, all but Input): AbilitySystem, AnimBlend, AssetTable, Audio3D, AudioBus, Biome, ClothSim, Collision3D, Culling, Cutscene, DamageSystem, DebugDraw, EdgeMesh, FacialAnim, Fog, FractalPlant, GameLoop, GameplayTags, GpuScene, HairSim, HelmBridge, LOD, Material, Mesh, Musculature, NavMesh, ParticleRenderer, PhysicsJoint, PostProcess, Renderer3D (peek-32 on its own buffers), Scene3D, Signal, Skinning, SkinShader, SoftBody, SplinePath, Terrain, Texture, TimeOfDay, Water, WorldGen, WorldHUD; game (26): AStar, Bresenham, CardDeck, CellularAutomata, Color, DiamondSquare, Easing, ECS, FloodFill, GameCamera, HexMap, Inventory, Klondike, Netcode (typed records, no wire parse; its pending list grows without bound on a never-matching frame, :61/:102, cost not parse), Octree, Pathfinding, Quadtree, Rasterizer, Raytracer, SaveSlot, Scene2D, Sprite, StateMachine, TileMap, Tween, Voronoi | | no parser of foreign bytes | N/A | |
| N/A, second sweep | codex/foreword/gpu (10, all but DeviceBuffer): Atomic, Barrier, DeviceEffect, DeviceMath, DisjointSlice, GpuEffect, LaunchConfig, Shared, Thread, Warp; math (14): Bezier, Complex, Cordic, Geodesic, Geometry, Interval, LinearAlgebra, Matrix3, Matrix4, Numeric, Optimize, Quaternion, Spline, VecArray; punctual (8): BitOps, ColorOps, Endian, FastMath, IntOps, Kinematic, Saturate, Trig; shell (5): BashEmit, KshEmit, PowerShellEmit, ShellBuild, ShellTypes (emitters, nothing reads shell text); signal (14): AudioAnalysis, AudioEffect, Convolution, Envelope, FFT, Filter, MusicTheory, Noise, Oscillator, Perlin, Pitch, Resample, Synth, Wavelet; sim (7): Collision, Constraint, Kinematics, ParticleSystem, Physics, SpatialHash, Steering | | no parser of foreign bytes | N/A | |

### Counts per class per test state (2026-08-15)

| class | NEGATIVE-ARM | KAT-ONLY | NO-TEST | UNSURE | N/A (no decoder) | total |
|---|---|---|---|---|---|---|
| trust decisions | 7 | 1 | 0 | 0 | 0 | 8 |
| loading authority | 5 | 1 | 0 | 0 | 0 | 6 |
| session establishment | 5 | 2 | 1 | 1 | 0 | 9 |
| device+industrial | 3 | 4 | 4 | 0 | 15 | 26 |
| media+documents | 0 | 6 | 4 | 0 | 9 | 19 |
| wire framing+net | 14 | 25 | 5 | 0 | 0 | 44 |
| storage | 2 | 1 | 1 | 1 | 0 | 5 |
| device descriptors | 7 | 9 | 4 | 1 | 0 | 21 |
| other | 0 | 3 | 0 | 0 | 0 | 3 |
| **total** | **43** | **52** | **19** | **3** | **24** | **141** |

### Counts for the second sweep, the rest of `codex/foreword/` (2026-08-16)

Kept as its own table rather than folded in (L-COUNT: the first table was
measured on 08-15 and has been corrected row by row since; a merged total
would be a number nobody measured).

| directory | files | decoder chapters | NEGATIVE-ARM | KAT-ONLY | NO-TEST | no parser |
|---|---|---|---|---|---|---|
| core | 128 | 24 | 13 | 10 | 1 | 104 |
| ai | 43 | 7 | 2 | 1 | 4 | 36 |
| ui | 50 | 5 | 1 | 4 | 0 | 45 |
| compress | 8 | 5 | 1 | 3 | 1 | 3 |
| engine, game, gpu, math, punctual, shell, signal, sim | 128 | 2 | 0 | 1 | 1 | 126 |
| **total** | **357** | **43** | **17** | **19** | **7** | **314** |

Of the 43, **21 are reached from production code**: `Gpt`, `Fat32`, `Fat16`,
`FactDisk`, `FactLog`, `SourceDefWire`, `OtaBoot`, `Aes256`, `KeyboardLayout`,
`Board`, `CCE`, `Parse`, `Gguf`, `InputSource`, `KeyInput`, `Markdown`, the
`Tls` decrypt side, and the crypto verifiers (`Ed25519`, `EcdsaP256`, `Rsa`,
`AesGcm`); the whole of
`compress/` and most of `ai/` are latent. Of the reached-and-not-guarded,
10.1 items 14-18 are the ones with a data-supplied length, offset, count or
divisor behind them.

### 10.3 What the census could not settle, and how it can be wrong

- **The naming rule under-counts.** Entry points were found by name
  (`decode`, `parse`, `read`, `from-bytes`, `unpack`). `J1939` decomposes a
  wire CAN id and `Lorawan` decrypts and verifies a join-accept off the
  wire; neither is named that way and both are marked N/A. Anyone taking
  the device+industrial row should read those two bodies first.
- **A test that CITES a chapter is not a test OF it** (L-NAMED). Several
  KAT-ONLY and NO-TEST verdicts rest on reading test bodies briefly; the
  UNSURE rows say where that budget ran out (`GopUsbKbd`, `DriveManager`,
  `DtlsHandshake`).
- **`GrayCode` decodes an integer we produced**, not foreign bytes; it is
  in the table under the literal rule and is not work.
- **`TrustAnchors` parses embedded DER**, not attacker-supplied bytes at
  the call site; its NEGATIVE-ARM is by consumer.
- **`PolicyProse` (human-authored policy text) is out of scope** and was
  left out; if untrusted text is ever in scope it needs a row.
- **`Gguf` has its own row since the second sweep** (10.1 item 16); the
  GGUF read direction was measured against llama.cpp files (WORKS-2,
  deferred), which is a KAT, not a negative arm.
- **What "unbounded" means differs by primitive, and the second sweep's rows
  say which.** `list-at`, `char-at` and `substring` trap out of range on
  this runtime (`docs/PM/Active/Stories/TheImageThatWasTwoDaysOld.md`, CLs
  11098/11112/11179), so a hostile length there is a guest kill or, where a
  count is followed first, a long walk. `peek-byte`/`peek-16`/`peek-32` on a
  raw buffer do not trap, so `Gpt.codex:154-156` and
  `KeyboardLayout.codex:30` are silent reads of adjacent heap. Refuse-vs-clamp
  follows blu's split either way; the difference is what the ablation looks
  like.
- **The second sweep read three subagents' tables and verified their top
  rows against the source** (`Lz4.codex:130/:133/:160` and
  `FactArchive.codex:171-177`, `Gpt.codex:154-201`, `FactDisk.codex:91-155`,
  `Gguf.codex:141-218` and `AgentBundle.codex:221-320`, `KeyInput.codex:184-258`);
  the rows below the top of each directory rest on the readers' file:line
  claims and were not independently re-read. Grep the entry point across
  `codex/test/**` before believing a NO-TEST there, as for the first sweep.
- **One contract note outside the census's subject, recorded here so it is
  not lost:** `KeyInput.codex:191` returns the firmware's `UnicodeChar` raw
  from `efi-key-decode` while the PS/2 path converts through `from-unicode`
  at `:258`, and CCE is not identity on ASCII. Read, not run: whether
  `uefi-read-key-ex` ever answers on the bare-metal path after
  ExitBootServices was not measured. It is a boundary question, not a
  bound, and belongs to whoever next touches `KeyInput`.
- **Still outside every sweep:** `apps/` other than `works` and `guios`, and
  `codex/os/` other than `net`, `verify`, `trust`, `kernel`. Nothing points
  there; nothing has looked.
- **What would falsify a row:** a negative arm the census missed. Grep the
  entry point name across `codex/test/**` before believing a NO-TEST, and
  read the test body for a truncated or corrupted input before believing a
  KAT-ONLY.
