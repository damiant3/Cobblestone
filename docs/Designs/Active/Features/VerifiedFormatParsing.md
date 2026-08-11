# Verified Format Parsing -- descriptions that generate parsers

**Status:** DESIGN. Not built. Stage 0 is worth doing on its own and
needs none of the rest.

**Author:** AgentGrid session, 2026-08-02, at Damian's request, after
reading EverParse (Project Everest / Microsoft Research).

**Ruling 2026-08-05 (Damian): stage 0 is APPROVED as schedulable background work.** It needs no design approval and is not seed-affecting; any lane with slack may take it. Stages 1-4 remain proposal.

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
