Codex Annotation System
High‑Level Design (Pure Text)

**Status (updated 2026-06-30):** Largely implemented. The H1-H12 environment
integration and the Addendum-I mutation log are shipped in apps/works/
(Annotation, AnnotationDriver/Store/Transport/Sidecar, SignedAnnotation,
Discussion, Historian, MutationLog, RepoProtocol, etc.). The
"proposal" framing in the body below is historical.

**The test-coverage paragraph that stood here was stale and is deleted**
(measured 2026-07-27). It said five annotation tests carried a `.skip` reading
"stub: test body not yet written" and that `historian-test-full` was `.failing`.
**None of the five carries a `.skip`** -- `annotation-author-test`,
`-driver-test`, `-migrate-test`, `-reader-test` and `-transport-test` each have
a `.codex` and an `.expected` and nothing suppressing them -- and
`historian-test-full` has an `.expected`, not a `.failing`. The outstanding work
this paragraph described had been done.

### What actually shipped, and where it departs from the design

Measured 2026-07-27, because the question of whether the inline form should
exist came up and neither side of it was written down.

**The inline `@kind target body` form is used sixteen times in the entire
tree**, all of them in `codex/plugs/riscv/RiscVLir.codex`. It is not peppering
anything. Every one of the sixteen names a target definition (`rv-lir-phys`,
`rv-lir-selects`, `rv-lir-emit-alu`) and states an invariant or a hazard that
costs real time to rediscover: why `s0`/`s1` are excluded from the allocatable
pool, why the frame is capped at 2032, why `LoRem`/`LoDiv` deliberately emit
nothing rather than a plausible substitute.

**`AnnotationNode` being an AST record is NOT the issue, and an earlier
revision of this section said it was.** That analysis observed that
`AnnotationNode` is declared in `AstNodes.codex`, built in `Parser.codex` and
carried through desugar and IR, and concluded the design principle was
violated by construction. It then noted that `claim` shares the record and
called removal "compiler surgery across the parser, desugarer, scoper and IR".

Both halves were beside the point, and Damian drew the distinction that
settles it: **an annotation is Codex syntax -- commentary, a communication
tool for maintainers.** `claim`, the `conversion` declaration and `punctual`'s
budget happen to be carried by a record called `AnnotationNode`, but they are
directives, not annotations. That the parser reuses one record for four
unrelated column-2 forms is an implementation detail with no bearing on where
maintainer commentary should live. The record stays regardless.

So the real question is only: **inline `@` in the source, or a sidecar in
`annotations/`?** And the free-form arm is the only thing in play. Nothing in
the compiler reads `kind == "rationale"` or `"invariant"` or `"warning"`;
`parse-annotation-line` is four lines in `Parser.codex` and everything
downstream is carry-through to the IR text emitter. Removing it is a parser
edit and a seed, not surgery.

### The sidecar side was broken, which is what actually decided this

Measured 2026-07-27. `annotations/` at the repo root holds five sidecars, six
records, one author, one date. Beyond being small, it did not work:

- `sidecar-path-for` built `codex.annotations/<quire>/<chapter>.json` while the
  directory is `annotations/`, so every lookup named a file that does not
  exist. Its only caller was a test supplying its own fixture, so the builder
  and the disk had never met.
- **Four of the five sidecars were bare JSON objects** where every reader
  expects an array. `sidecar-array-to-annotations` answers `[]` for that,
  silently, so four of five would have loaded as nothing. Only the one file
  holding two records was written as an array, which is how the mistake
  survived: the format was obvious with two entries and invisible with one.
- Nothing read a sidecar at all: no build script, no CLI (deleted at CL 1141),
  no Codex reader.
- `annotations/README.md` described a `codex.annotations/` layout and a rename
  that never happened, pointed at the wrong path for this document, and named
  two quires that do not exist.

All four are fixed, and `codex/test/apps/annotation-query-test` now puts the
format, the parser and the path together against a verbatim depot sidecar,
which is the check whose absence let all of it stand.

### Where commentary goes: the sidecar, and only the sidecar

`apps/works/AnnotationsQuery.codex` is the reader, an **optional side query**
by Damian's instruction: *"not all annotations are worth reading, and none will
be worth reading all the time."* It is not a build step and nothing invokes it.

**The inline `@` form is removed from the language** (2026-07-27, Damian's
call: *"move them out, and remove the at-symbol support entirely. I don't want
it infecting the code"*). The sixteen invariants in
`codex/plugs/riscv/RiscVLir.codex` are now
`annotations/codex/plugs/riscv/RiscVLir.json`, which is the whole of the
tree's inline annotation usage; nothing else in 2,114 source chapters used it.

What came out, in full:

| file | what went |
|---|---|
| `Syntax/Token.codex` | the `AtSign` variant |
| `Syntax/Lexer.codex` | `cc-at`, and both sites producing `AtSign` |
| `Syntax/Parser.codex` | `is-at-sign`, `parse-annotation-line`, the prose-collection guard, the top-level dispatch arm |
| `Syntax/SyntaxNodes.codex` | the `AtSign` copy arm |

`AnnotationNode` **stays**, because `claim`, the `conversion` declaration and
`punctual`'s budget are carried by it and are directives rather than
annotations.

The new behaviour was measured rather than reasoned. An `@` at column 2 is
ordinary prose and compiles. An `@` in code position falls to the general
unknown-character path, which is `ErrorToken`, and the compile fails: `@bad`
and `$bad` in the same position produce **byte-identical** diagnostics, so the
character is not special-cased any more, it is simply not in the language. The
compiler shrank 1,875 bytes, which is what says the removal reached the emitted
code rather than being a no-op.

Purpose

The Codex Annotation System is a bolt‑on semantic layer that attaches durable, queryable metadata to the compiler pipeline without altering the substrate. It exists because Codex must run everywhere: cloud clusters, embedded systems, satellites, surgical robots. Annotations must never be required for execution. They are optional, external, and disposable.

Annotations serve two audiences:

Humans, who need a place to explain, argue, warn, and document intent without touching the core language.

Agents, who need a place to leave breadcrumbs so they do not rediscover the same invariants, dependencies, or hazards.

Codex does not use comments because comments lie. They drift, rot, and contradict the code. They cannot be trusted as part of the semantic substrate. Annotations are external, typed, and anchored to AST/IR nodes instead of text.

Design Principles

Bolt‑on, not built‑in.
Annotations live in sidecar files. They are not part of the AST, IR, or binaries. The compiler and runtime do not require them. This keeps Codex minimal and deterministic while still allowing rich metadata where useful.

Semantic anchoring.
Annotations attach to AST nodes (programmer intent) and IR nodes (compiler truth). This makes them resilient across desugaring, typechecking, and lowering, unlike line‑based comments.

Deterministic identity.
Every AST and IR node has a deterministic, content‑stable identifier derived from structure and key semantic fields, not formatting. If semantics change, the ID changes; if only whitespace changes, the ID stays the same.

Typed metadata.
Annotations are structured records with kinds, payloads, scopes, authors, and timestamps. This avoids the free‑text rot of traditional comments and makes annotations queryable by agents.

Optional and removable.
Sidecar files can be ignored, stripped, or excluded from builds. The system behaves identically with or without them.

Node Identity

3.1 AST NodeId

AST nodes come from AstNodes.codex.txt. We derive:

AstNodeId = hash(
module-name,
ast-path,
node-kind,
key-fields
)

The ast-path is a deterministic structural path, for example:
defs[3].body.match.arms[1].pattern
type-defs[0].fields[2].type-expr

This survives formatting changes and reflow.

3.2 IR NodeId

IR nodes come from IRModule.codex.txt. Lowering constructs them deterministically.

IrNodeId = hash(
origin-ast-id,
ir-kind,
ir-path
)

IR nodes do not move due to formatting or syntactic changes. They are ideal anchors for agent‑level annotations about actual compiled behavior.

Annotation Model

Annotations are records attached to NodeId (AST or IR).

Annotation = {
id        : AnnotationId,
target    : NodeId,
author    : AuthorId,
scope     : Scope,
kind      : AnnotationKind,
payload   : AnnotationPayload,
timestamp : Integer,
signature : Optional Text
}

Scope values:
private, team, global, agent.

Kinds:
ReviewComment, AgentDiscovery, Invariant, RefactorNote, Doctrine, Warning, SemanticTag, Link, Custom.

Payloads:
TextPayload, KeyValuePayload, InvariantPayload, DependencyPayload, SemanticTagPayload, LinkPayload, CustomPayload.

Sidecar Storage

Annotations are stored per module in a sidecar file:

<module-name>.codex.annotations.json

Properties:
Git‑friendly, removable, optional.

**(2026-07-28: that filename is the original proposal and is NOT what
shipped. The shipped form is `annotations/<source-path>.json`, mirroring
the source tree: strip `.codex`, add `.json`. See Section B.)**

Attachment Points

6.1 AST

Expressions: ALitExpr, ANameExpr, AApplyExpr, ABinaryExpr, AUnaryExpr, AIfExpr, ALetExpr, ALambdaExpr, AMatchExpr, AListExpr, ARecordExpr, AFieldAccess, ADoExpr, AHandleExpr, AErrorExpr.

Patterns: AVarPat, ALitPat, ACtorPat, AWildPat.

Types: ANamedType, AFunType, AAppType, AEffectType.

Definitions: ADef, ATypeDef, AEffectDef, record fields, variant ctors.

Module: AModule, AImportDecl.

6.2 IR

IRExpr: IrBinary, IrIf, IrLet, IrApply, IrLambda, IrList, IrMatch, IrDo, IrHandle, IrRecord, IrFieldAccess, IrFork, IrAwait, IrError, literals, names.

IRPat: IrVarPat, IrLitPat, IrCtorPat, IrWildPat.

IRDef and IRModule.

Migration and Mapping

7.1 AST to IR (Lowering)

Lowering is defined in Lowering.codex.txt. Key mappings:

ALitExpr → IrIntLit / IrTextLit / IrBoolLit / IrCharLit
ANameExpr → IrName
ABinaryExpr → IrBinary
AUnaryExpr → IrNegate
AIfExpr → IrIf
ALetExpr → nested IrLet chain
ALambdaExpr → IrLambda
AMatchExpr → IrMatch
AListExpr → IrList
ARecordExpr → IrRecord
AFieldAccess → IrFieldAccess
ADoExpr → IrDo
AHandleExpr → IrHandle
AErrorExpr → IrError

7.2 Migration Rules

Direct mapping:
Single AST node → single IR node. Copy annotations.

Root mapping for composites:
ALetExpr → outermost IrLet
AIfExpr → IrIf
AMatchExpr → IrMatch
ADoExpr → IrDo

Pattern mapping:
APat → IRPat via lower-pattern. Attach to IRBranch.pattern.

Orphans:
If an AST node disappears or transforms, its annotations become orphaned. They remain in the sidecar file and can be surfaced for manual or agent reattachment.

IR‑only annotations:
Agents may annotate IR nodes with no AST origin (IrFork, IrAwait, etc.). These remain IR‑anchored.

Query Model

Basic queries:
get-annotations(NodeId)
get-module-annotations(ModuleName)
filter by kind
filter by author

Agent‑centric queries:
Module brief: summarize AgentDiscovery, Warning, Doctrine.
Function brief: collect annotations on a definition and its body.
Type‑aware queries: find invariants on IR nodes with specific CodexType.

Human and Agent Workflows

9.1 Human Workflows

Add ReviewComment to ADef, ABinaryExpr, AMatchExpr.
Add Doctrine to AModule or ATypeDef.
Add RefactorNote to any AST node.
Use sidecar files as the discussion layer.

9.2 Agent Workflows

During lowering: attach AgentDiscovery to IR nodes.
During analysis/optimization: attach Invariant, Warning, SemanticTag.
During review assistance: read annotations for nodes touched by a diff and generate a brief.

Why Not Comments, Why Bolt‑On, Why IR

10.1 Why not comments

Comments are tied to text, not semantics.
They drift when code changes.
They cannot be reliably queried or trusted by agents.
They pollute the substrate with human‑only noise.

Annotations are anchored to AST/IR nodes, typed, external, removable, and usable by both humans and agents.

10.2 Why bolt‑on sidecar

Codex must run in environments where storage or policy forbids extra metadata.
Sidecar files can be excluded from deployment.
Different consumers can maintain their own annotation layers without forking the code.

10.3 Why IR nodes

IR nodes represent the compiler’s resolved view of the program.
IR nodes do not move due to formatting.
Many agent‑level facts are only visible at the IR level.

AST captures intent; IR captures reality. Annotations need both.

Open Questions and Future Directions

Open questions:
NodeId format stability.
Signature and trust model.
Scope enforcement.
Orphan handling policies.
Performance and caching.

Future directions:
Deeper agent integration.
Verification and synthesis using invariants.
Profiling and optimization annotations.
IDE integration as overlays.
Multi‑layer annotation stacks for different teams or agents.

This defines the first layer: a bolt‑on, semantic, sidecar annotation system that humans and agents can share without compromising the core Codex substrate.


CRITIQUE OF THE ANNOTATION SYSTEM
(Plain Text, Codex Style)

Node Identity Stability
You assume AST paths are stable. They are not.
Reordering definitions, adding imports, or introducing new syntactic sugar will shift indices.
This means NodeId stability depends on a guarantee you have not yet formalized:
“AST structure is deterministic across all transformations that preserve semantics.”

You need a rule for how AST paths behave under reordering.
Otherwise annotations drift.

IR Node Identity
IR nodes are more stable than AST nodes, but not perfectly stable.
Optimizations will rewrite IR.
Inlining, dead‑code elimination, constant folding, lambda lifting -- all of these destroy or duplicate IR nodes.

You need a policy for what happens when IR nodes are duplicated or eliminated.
Do annotations propagate?
Do they collapse?
Do they become orphans?

Orphan Handling
You defined orphans but not what to do with them.
If 10% of annotations become orphans after a refactor, the system becomes noisy.
Agents will waste cycles trying to reattach them.
Humans will ignore them.

You need a rule:
“Orphans older than X days are archived.”
or
“Agents attempt reattachment using semantic similarity.”
or
“Orphans are visible only on demand.”

Right now, orphans accumulate forever.

Annotation Overload
Agents will annotate everything unless you stop them.
Every IR node is a potential fact.
Every lowering step is a potential breadcrumb.
Every type inference is a potential invariant.

Without constraints, the sidecar becomes a landfill.

You need a throttle:
“Agents annotate only when the fact is non‑derivable from code.”
or
“Agents annotate only when the fact contradicts or extends existing annotations.”

Otherwise you get annotation spam.

Annotation Conflicts
Two agents may disagree.
Two humans may disagree.
A human and an agent may disagree.

You have no conflict model.
Annotations are not authoritative.
But some annotations matter more than others.

You need a rule:
“Annotations do not override code; they override each other only by scope or trust.”

Right now, all annotations are equal.
That won’t survive contact with reality.

Annotation Lifetimes
Some annotations are eternal (doctrine).
Some are ephemeral (agent discoveries).
Some are contextual (warnings about a specific optimization).

You need lifetimes.
Otherwise annotations accumulate like sediment.

Annotation Provenance
You have author and timestamp, but not intent.
Why was the annotation created?
What was the agent trying to prevent?
What was the human trying to explain?

Without intent, annotations become archaeological artifacts.

Annotation Query Semantics
You defined queries but not semantics.
If a function has 200 annotations, what is the “brief”?
If a module has 10,000 annotations, how do you summarize?

You need a rule for summarization:
“Summaries collapse annotations by kind and scope.”
or
“Summaries show only non‑redundant facts.”

Otherwise the brief becomes a dump.

Annotation Security
Agents can write annotations.
Agents can read annotations.
Agents can act on annotations.

This is a capability channel.
You need to define what an agent is allowed to do with annotations.
Otherwise annotations become a covert control plane.

Annotation Evolution
Annotations are tied to AST and IR.
But Codex is evolving.
The AST schema will change.
The IR schema will change.

You need a migration story for annotations across schema evolution.
Otherwise annotations die every time you improve the language.

Sidecar File Scalability
One file per module works until modules get large.
Then the sidecar becomes a megafile.
Agents will thrash it.
Humans will hate it.

You need a sharding rule:
“Sidecar files are chunked by NodeId prefix.”
or
“Sidecar files are chunked by annotation kind.”

Annotation Garbage Collection
You have no deletion.
You have no archival.
You have no compaction.

Without GC, the annotation layer becomes a second repository.
You need a lifecycle.

Annotation Semantics vs. Doctrine
Doctrine is global.
Annotations are local.
But some annotations are doctrine in disguise.

You need a boundary:
“What belongs in doctrine vs. annotation?”

Otherwise doctrine leaks into annotations and vice versa.

Annotation Trust
Agents will trust annotations unless told otherwise.
Humans will trust annotations unless told otherwise.

You need a trust model:
“Annotations from agents are advisory.”
“Annotations from humans are authoritative only within scope.”
“Annotations with signatures are higher trust.”

Annotation Compression
Agents will generate structured payloads.
These will be verbose.
Sidecar files will balloon.

You need compression or normalization.
Otherwise the annotation layer becomes a performance problem.

SUMMARY OF GAPS

The annotation system is conceptually sound, but incomplete in these areas:

• NodeId stability
• IR rewrite behavior
• orphan lifecycle
• annotation spam control
• conflict resolution
• annotation lifetimes
• provenance intent
• summarization semantics
• agent capability boundaries
• schema evolution
• sidecar scalability
• garbage collection
• doctrine boundary
• trust model
• compression

None of these require a “spec.”
They require decisions.

And the agent can implement the system without them -- but the system will degrade over time unless these gaps are addressed.


---

# Addendum -- 2026-05-08 review (Pip)

This is a follow-up review of the annotation system as it actually
exists today against the design above. Code state surveyed:
`annotations/` (six sidecar files, ~50 annotations across five
chapters) and this document (413 lines through the original).

**(2026-07-28: this line said `codex.annotations/`, "freshly renamed from
`annotations/` in CL 1201". No such rename happened. Section B has the
correction and Section E the current measurement. The survey itself stands
as a record of what the tree looked like on 2026-05-08.)**

The original design and its self-critique remain right. This
addendum's job is to (a) record where the design is strong and
should be preserved as-is, (b) propose the rename + the lift of the
deleted operator CLI as Codex-native, and (c) make concrete
decisions on the gaps the original left as open.

## A. What's strong and should not change

The original design got several things exactly right. Calling them
out so they don't get traded away in a future revision.

1. **Sidecar storage is the right call.** Annotations live outside
   the source tree, can be ignored, stripped, or excluded from
   builds. This survives the firmest test in the project: "Codex
   must run everywhere." A Codex artifact deployed to a satellite
   does not need the annotations to execute. The decision to keep
   them out of the AST/IR and out of the binary is foundational and
   should never be reversed.
2. **Anchoring to AST and IR nodes, not text lines.** Comments rot
   because they're tied to text. Annotations anchored to structural
   identifiers survive formatting changes, reorder-within-body, and
   most refactors. The dual-anchor split (AST captures *intent*, IR
   captures *reality*) is the correct factoring; both layers need
   their own annotations.
3. **Typed payloads with declared kinds.** The kind enum
   (`invariant`, `rationale`, `warning`, `discussion`, `discovery`,
   `todo`, `doctrine`) is small enough to be useful and big enough
   to capture the categories that genuinely matter. Free-text
   "comments" are not a kind -- and that's the point.
4. **The "why not comments" framing.** This carries weight beyond
   the annotation system. Damian's broader stance on language
   design (recorded in CL 1199 on `GpuKernels.md`) is "effects are
   the contract; type-checking is the ergonomic; do not pepper the
   code with garbage." This document was already operating from
   that stance. Keep it.
5. **AST → IR migration rules.** The mapping table (`ALitExpr →
   IrIntLit`, `ABinaryExpr → IrBinary`, etc.) is the right way to
   structure the lowering carry-over. The "orphan" notion is
   correctly named even if its handling was left open.
6. **The built-in critique section.** Pre-listing 15 gaps is a
   feature, not a flaw. Don't delete the critique in some future
   "polish" pass -- it's the receipt that keeps the system honest.

## B. The rename that never happened

**This section said the tree was renamed `annotations/` ->
`codex.annotations/` in CL 1201 and described it as done. It is false, and
it was false when written** (measured 2026-07-28, and the README under
`annotations/` had already caught it on 2026-07-27). There is no
`codex.`-prefixed directory anywhere at root: every sibling is `codex/`,
`apps/`, `build/`, `docs/`, `shaders/`, `annotations/`. The
`codex.<quire>/` convention this section argued from was abandoned, and the
directory list it cited as precedent (`codex.foreword/`, `codex.kernel/`,
`codex.works/`, `codex.test/`, `codex.build/`) does not exist either.

**The cost was real and is the reason this correction is kept rather than
deleted.** `sidecar-path-for` in `apps/works/AnnotationsSidecar.codex` was
written against this section and built `codex.annotations/<quire>/<chapter>.json`
for every lookup, so the one function that knows how to find a sidecar named
a path that has never existed. Nothing caught it because its only caller was
a test supplying its own fixture: the builder and the disk had never met.
Both are fixed, and `codex/test/apps/annotation-query-test` now asserts the
built path against a verbatim depot sidecar.

**The layout, measured rather than quoted:** the root is `annotations/`, and
a sidecar mirrors its source path exactly. Strip `.codex`, add `.json`, put
it under `annotations/`. `apps/guios/GopRender.codex` is annotated by
`annotations/apps/guios/GopRender.json`. The path is derivable by
substitution and checkable by eye against the source tree, which is the
whole point of it.

A sidecar is anchored to a chapter by PATH, so **a chapter that moves
silently orphans its annotations** and nothing says so: there is no build
step, and a query for the moved chapter answers nothing rather than
answering wrongly. When you move a chapter, move its sidecar.

## C. The deleted `annotate.ps1` -- lift as Codex-native

The PowerShell CLI `codex.build/annotate.ps1` was deleted in the
CL 1141 cleanup pass alongside 17 other dead PS1 scripts. It was
an operator interface for the annotation system: list / add /
search / show-thread, all reading and writing the JSON sidecars
under what was then `annotations/`. At deletion time the system
was effectively unused, so the CLI looked like cruft.

In retrospect it was load-bearing scaffolding. Without an operator
interface, the annotation system has no front door -- humans don't
add annotations, agents don't query them, the sidecar files drift
out of date, the kind enum stops being exercised, and the design
in this document degrades into archaeology.

**Proposal**: Re-create the CLI as a first-class Codex chapter
under `codex.works/`. Sketch:

```
codex.works/AnnotationsCli.codex
  Chapter: AnnotationsCli
    cites Foreword chapter FileSystem
    cites Foreword chapter TextSearch
    cites Foreword chapter Console
    cites Encode chapter Json
    cites Foreword chapter Ed25519     -- for signing (see G below)

  Section: Operations

    annot-list    : Text -> [Console, FileSystem] Nothing
    annot-add     : AnnotationRecord -> [Console, FileSystem, AnnotationsWrite] Nothing
    annot-search  : Text -> [Console, FileSystem] Nothing
    annot-thread  : Text -> Text -> [Console, FileSystem] Nothing
    annot-orphans : [Console, FileSystem] Nothing
    annot-verify  : [Console, FileSystem] Nothing  -- check signatures + schema

  Section: Effects

    effect AnnotationsWrite where
      record-annotation : AnnotationRecord -> [AnnotationsWrite] Nothing
```

Why Codex-native, not PS1:

- It dogfoods Codex as the application substrate, the same way
  `codex.works/CodeBrowser.codex`, `codex.works/ConsoleEditor.codex`,
  `codex.works/CompilerDriver.codex` etc. do. The annotation system
  is not special enough to warrant a host-tool exception.
- The `Json.codex` foreword (`codex.foreword.encode`) handles
  read/write; `TextSearch.codex` handles search; `FileSystem.codex`
  handles the disk side. All exist. The CLI is a few hundred lines
  of glue.
- Effect typing makes write authority explicit. `[AnnotationsWrite]`
  carrying `cap-annotations-write` keeps casual readers from
  accidentally mutating the record (the existing CLI had no such
  guardrail; any PowerShell session could write).
- The CLI runs from the UEFI dev console as well as from PowerShell
  prompts on the host. `codex.build/annotate.ps1` only ran on the
  host.

## D. Decisions on the open gaps

The original critique listed 15 gaps and said "they require
decisions, not specs." Concrete decisions follow.

### D.1 NodeId stability

**Decision**: Define the AST path as a structural traversal that
ignores source position, span, and inline whitespace, but does
include `chapter-name`, `section-name`, top-level definition name,
and within-body structural indices (e.g.
`when-arms[1].pattern`).

Reordering of top-level definitions within a chapter is a *renaming
event* -- annotations migrate or orphan based on the chapter+name
key, not the chapter+index key. Reordering at the section level is
a renaming event of the same kind. Reordering inside a body is
*not* a renaming event because structural traversal goes by
production rule, not lexical order.

**Concrete rule**: `AstNodeId = hash(chapter-name, section-name?,
top-level-def-name, structural-path-without-positions)`. If any
field changes, the ID changes. If only formatting changes, the
ID is stable.

### D.2 IR rewrite behavior

**Decision**: When an IR node is *rewritten* (constant fold, lambda
lifted, inlined), the carrying annotation moves to the surviving
node's nearest ancestor of the same kind. When an IR node is
*duplicated* (loop unroll, monomorphization), the annotation
duplicates with each copy. When an IR node is *eliminated* (DCE),
the annotation becomes an orphan and is logged with the
optimization that killed it.

This makes the optimizer responsible for declaring which
annotations rode along; the annotation file records the
optimization that touched each annotation in a `transit` log.

### D.3 Orphan lifecycle

**Decision**: Orphans live in the same sidecar file as live
annotations, marked with `"status": "orphan"` and a `"orphaned-at":
<timestamp>` field. They are not surfaced by default queries
(`annot-list`, `annot-search`); they are surfaced by an explicit
`annot-orphans` query. Orphans older than 90 days are *archived*
to a `.orphans.json` companion file. Archives are never deleted
automatically.

This balances: noise (default queries don't drown in orphans),
recovery (an agent can ask for orphans and reattach), and
permanence (nothing is lost without explicit human action).

### D.4 Annotation spam control

**Decision**: Agent-authored annotations require a *non-derivability
test* -- the agent must record `"why-non-derivable"` text explaining
what the annotation says that isn't readable from the code. The
verify pass (`annot-verify`) refuses an agent-authored annotation
without this field. Human-authored annotations are exempt because
intent itself is the value.

Throttle: the `annot-verify` pass also flags any chapter exceeding
N annotations (initial: 50) and any function exceeding M (initial:
10). Above the threshold, new annotations require explicit override.

### D.5 Conflict resolution

**Decision**: Annotations are not authoritative against code.
Annotations conflict with each other only when they are the *same
kind* on the *same target*. Resolution rule, in order: (a) signed
annotations beat unsigned, (b) human authors beat agents, (c)
newer beats older. The loser is not deleted -- it gets
`"superseded-by": <id>` set, and disappears from default queries.

### D.6 Annotation lifetimes

**Decision**: Add a `"lifetime"` field, optional. Values:
`eternal`, `until-cl <N>`, `until-date <YYYY-MM-DD>`,
`while-feature <name>`. Default is `eternal`. The `annot-verify`
pass surfaces expired annotations.

### D.7 Provenance intent

**Decision**: Add an `"intent"` field, optional but recommended.
Free text, but constrained to one of: `prevent`, `explain`,
`discover`, `propose`, `warn`, `record`. The intent is the *why*
behind creating the annotation, distinct from the body which is
the *what*.

### D.8 Summarization semantics

**Decision**: A chapter's "brief" collapses by kind, then by intent.
Output structure: `{kind: invariant, intent: prevent}: 3 entries
on functions {f1, f2, f3}`. A function's "brief" lists its
annotations in chronological order with one-line summaries. The
brief is the chapter's `annot-list -Brief`; the full record is
`annot-list -Full`.

### D.9 Agent capability boundaries

**Decision**: Three new capabilities in the trust lattice (which now
exists, CL 763):

- `cap-annotations-read` -- query annotations.
- `cap-annotations-write` -- add/edit annotations attributed to
  *self*.
- `cap-annotations-write-as-other` -- write annotations attributed
  to a different identity (rare, requires explicit grant).

Agents default to `cap-annotations-read` only. `cap-annotations-write`
is granted at session start by the human agent.

### D.10 Schema evolution

**Decision**: Sidecar files carry a `"schema-version"` top-level
field. The `annot-verify` pass refuses to load files whose
schema-version is newer than the runtime knows; older versions are
auto-migrated forward. Migrations live in a `codex.works/
AnnotationsMigrate.codex` chapter, one function per version
transition.

### D.11 Sidecar scalability

**Decision**: One sidecar file per chapter is fine through ~1000
annotations per chapter. Above that threshold, the `annot-verify`
pass recommends sharding by annotation kind (`X86_64Chapter.json`,
`X86_64Chapter.invariants.json`, `X86_64Chapter.discussions.json`,
etc.). No mandatory sharding before the threshold.

### D.12 Garbage collection

**Decision**: GC happens at `annot-verify` time, not at write time.
Three actions: (a) move expired annotations to archive, (b) move
old orphans to archive, (c) prune archive entries older than two
years. Archive prune is the only action that loses data and
requires `cap-annotations-write-as-other`.

### D.13 Doctrine boundary

**Decision**: A `doctrine` annotation must be attached to a
*chapter* or *type*, never to a function or expression. Function-
or expression-level rules are `invariant` or `warning` instead.
This is enforced by `annot-verify`. Doctrine that wants to be
function-level is wrong-kinded and should be reclassified.

### D.14 Trust model

**Decision**: Annotations carry an `Optional Text` signature field
already (per the original design). Activate it. The signing key is
the author's Ed25519 identity (CL 755 -- `identity-whoami`). The
`annot-verify` pass checks signatures against the trust lattice.
Unsigned annotations are advisory; signed annotations propagate
trust scores; annotations signed by a node below the trust
threshold are not displayed by default.

This makes annotations a first-class participant in the existing
trust infrastructure rather than a parallel one.

### D.15 Compression

**Decision**: Don't pre-optimize. JSON is fine through ~10 MB per
sidecar. If a sidecar grows past that, the schema-evolution
migration path can move that chapter to a binary form. Defer.

## E. Coverage status

**Re-measured 2026-07-28. Do not quote these forward; count them again.**
21 sidecar files, 55 records. Every one is a JSON array, which is the
property worth checking and the one that has been wrong before.

| area | files | records |
|---|---:|---:|
| `codex/compiler/` (Emit, IR x2, Semantics) | 4 | 4 |
| `codex/plugs/` (riscv 16, pe 2, go 2, ocaml 2, and 9 emitters with 1 each) | 13 | 31 |
| `apps/guios/` (GopRender, GuiDisplay) | 2 | 11 |
| `codex/test/` (gop-render-clamp, gop-text-clip) | 2 | 9 |

The previous version of this section read "exactly five compiler chapters
... roughly 50 annotation records ... the other ~340 chapters in the depot
have zero", and named `codex/Emit/PeWriter` and `codex/Emit/X86_64Chapter`,
neither of which is where those chapters live. Every figure in it was stale
and the paths had rotted underneath it, which is `L-COUNT` exactly.

**Verify the array property on the RAW TEXT, not on a parsed object.**
`ConvertFrom-Json` unrolls a single-element array to a bare object, so
`$parsed -is [array]` answers False for a correct one-record file and False
for a broken bare-object file alike: the instrument cannot express the
distinction it is being asked about. Testing the first non-whitespace
character for `[` can. Measured both ways on 2026-07-28: the parsed test
reported 13 of 21 files broken and the raw test reported 0, and the raw
test is the true one.

Two ways forward:

- **Scope down**: declare the system "compiler-internal for now,"
  delete the unused directory shell, and revisit when the agent
  workflow that needs annotations actually materializes.
- **Populate**: ship `AnnotationsCli.codex`, then have agents
  annotate the chapters they touch as part of normal CL work. The
  trust + signing pieces above make this safe.

I lean toward the second. The CLI lift is the load-bearing fix;
once authoring is ergonomic, coverage follows. Without the CLI,
populating is sustained by discipline alone, and discipline is the
property the design correctly chose to *not* depend on.

## F. Phasing

CL-sized chunks for the lifts:

| CL | What |
|---|---|
| **A0** | ~~rename `annotations/` → `codex.annotations/`~~ **NEVER HAPPENED** (see Section B, corrected 2026-07-28). The README and this addendum landed; the rename did not, and the root is still `annotations/`. |
| **A1** | `codex.works/AnnotationsCli.codex` skeleton: `annot-list`, `annot-search`, `annot-thread` (read-only). No writes yet. Wires to existing 6 sidecar files. |
| **A2** | `annot-add` + `[AnnotationsWrite]` effect + `cap-annotations-write` capability. Verifier Phase 3 integration. |
| **A3** | `annot-verify` pass: schema validation, kind/target consistency, signature check, orphan listing. |
| **A4** | Schema evolution: `schema-version` field, `AnnotationsMigrate.codex` migration chapter, current files migrated to v1. |
| **A5** | Ed25519 signing: sign-on-write for human authors, verify-on-read everywhere. Trust-lattice integration. |
| **A6** | Lifecycle decisions D.6 (`lifetime` field) + D.7 (`intent` field) + D.12 (GC pass). Schema bumps to v2. |
| **A7** | Coverage: agents start annotating chapters they touch. Sets the precedent that touching `codex/Emit/X86_64Chapter.codex` without updating its annotation sidecar is at least review-worthy. |

A0 is committed. A1-A7 are proposals, not commitments -- the user
decides which (if any) to schedule.

> **Note (2026-05-08, second pass)**: Sections C and F above were
> written before I had read the founding vision documents. The
> framing of "lift `annotate.ps1` as a Codex-native CLI" turned
> out to be the wrong shape -- the work isn't a separate CLI, it's
> *editor and Environment integration*. See Section H below for
> the corrected framing. Sections A, B, D, E remain valid; the
> phasing in F is preserved as scaffolding but is reorganized in
> H.7.

## G. Open questions for Damian

1. **Scope down or populate?** Per Section E above. If "scope down,"
   stop after A0 and revisit later. If "populate," commit to A1-A3
   minimum so the system has a working operator interface.
2. **CLI living chapter** -- is `codex.works/AnnotationsCli.codex`
   the right home, or should it be a `codex.tools/` quire (which
   doesn't exist today)? The first option is consistent with
   `CodeBrowser`, `ConsoleEditor`, `CompilerDriver`; the second
   would group all developer-facing tools.
3. **Signature strictness** -- at the A5 step, should the *default*
   policy be (a) signed annotations only, (b) signed get higher
   trust but unsigned still display, or (c) unsigned display freely
   for now? Most permissive (c) lets the system get used; most
   strict (a) prevents drift but requires every contributor to
   have a signing identity.
4. **Existing annotation file content** -- six files, ~50
   annotations, none currently signed. Do we backfill signatures
   (Cam authored most of them per `author: cam`), drop them as
   advisory-only, or treat them as the implicit v0 schema?
5. **Tier with the library gap analysis** -- the `LibraryGapAnalysis.md`
   doc submitted in CL 1200 lists Tier 1 work for the UEFI app
   (form widgets, image decoders, AI inference). Where does
   `AnnotationsCli` fall in that priority? My read: A1 (read-only
   CLI) is Tier 2 (ergonomic), A2+ is Tier 3 (defer until coverage
   matters).

The original design's last line still applies: "the agent can
implement the system without these decisions -- but the system
will degrade over time unless these gaps are addressed." A0 was
the structural piece. The decisions in section D answer the
remaining gaps. The phasing in section F is the path. The
priority is yours.

---

## H. Second pass -- grounded in the founding vision

This section was added after I went back and read
`docs/Stories/Vision/NewRepository.txt` and
`docs/Stories/Vision/IntelligenceLayer.txt` end to end. Sections
above were written from the condensed `FOUNDING-VISION.md` and a
codebase tour. Reading the source documents changed the framing
substantially: most of what I was about to propose as "new
architecture" is in fact already named, defined, and partially
implemented. The right addendum is to map the founding spec onto
existing code and identify the integration gaps, not to restate
the founding spec under a different name.

### H.1 -- What the founding spec already specifies

From `NewRepository.txt`, Part Three ("The Repository") and
Part Four ("The Tooling"):

**Ch 8 -- The Codex Repository.** "The repository contains *facts*.
Every fact is: Immutable, Content-addressed, Attributed, Typed."
Fact kinds enumerated: Definitions, **Proposals**, **Verdicts**,
Dependencies, Tests, Benchmarks, **Discussions**. "There are no
pull requests. There are proposals." "There are no branches. There
are views."

**Ch 9 -- Discovery and Trust.** Every definition has six records:
proof, test, benchmark, provenance, dependency, **trust lattice**.
"You search for code by capability." "Stars" are replaced by the
proof lattice and the vouching graph.

**Ch 10 -- The Environment.** A single application with seven
roles: Reader, Writer, **Verifier**, **Explorer**, Executor,
**Narrator**, **Historian**. "The Writer provides live feedback
as you type." "Every definition in the repository can be narrated:
the Narrator explains what it does, why it does it that way, what
alternatives were considered, and what the proof record says."

**Ch 12 -- Who Owns the Code.** Nobody / everybody. Definitions are
attributed but not owned. Vulnerability disclosure flow:
"original is attributed to you, supersession is attributed to you,
disclosure is attributed to whoever found it. Nothing is hidden.
Everything is true."

**Ch 13 -- How We Change Things.** A Proposal carries: changed
definition, justification, comparison-to-superseded, tests,
optional proof, list of stakeholders. A **Verdict** is one of
Accept / Reject / Amend / Abstain. "The proposal is accepted when
all required stakeholders have issued Accept or Abstain verdicts."

From `IntelligenceLayer.txt`: the engineer of 2030 specifies
intent and constraints; the AI produces implementations;
"specifications become the primary programming surface." Argument
Tax collapses; assertions are proved cheaply.

These chapters are the authoritative reference for everything
Damian described in the message that triggered this addendum. The
"trust UI," the "annotation bubble," the "grid forwarding," the
"accept-and-publish loop," the "first-time signing key" --
every one of those is named or implied above. They are not new
ideas. They are the implementation of Part Three / Part Four.

### H.2 -- What `codex.works/` already ships

A grep across `codex.works/` shows the founding spec has begun to
land in code (CL 1018 was the milestone). Existing chapters:

| Chapter | LOC | What it covers |
|---|---:|---|
| `Annotation.codex` | 128 | Typed annotations with kinds (Rationale, Invariant, Warning, Discovery, Doctrine, Todo); content-addressed by stable target identifier |
| `AnnotationStore.codex` | 70 | Storage abstraction over the sidecar layer |
| `SignedAnnotation.codex` | 113 | Ed25519 signature on every annotation; `signer-fingerprint`, trust-lattice integration |
| `Discussion.codex` | 120 | Threaded discussions attached to annotation targets -- "discourse mechanism from the Codex repository protocol" |
| `KeyManager.codex` | 114 | Ed25519 keypair generation, keyring, fingerprints; "private keys never leave the device" |
| `RepoProtocol.codex` | 208 | Facts, proposals, verdicts, discussions; content-addressed; signed; the implementation of Part Three |
| `BuildRecord.codex`, `BuildTrace.codex`, `BuildManifest.codex` | -- | Build provenance + content-addressed build records |
| `FirstBoot.codex` | 195 | The first-boot wizard. Phase enum: PhaseWelcome, PhaseIdentity, PhaseAgentSelect, PhaseAgentSetup, PhaseUpstream, PhaseModeSelect, PhaseSaveConfig, PhaseComplete. **PhaseIdentity is where the Ed25519 keypair is generated.** |
| `ConsoleEditor.codex` | 170 | The Writer (Ch 10). Editing surface |
| `CodeBrowser.codex` | 235 | The Reader / Explorer (Ch 10). Source navigation |
| `CdxInspector.codex` | -- | Toward the Verifier role (Ch 10) -- inspect signed CDX, verify integrity |
| `DevConsole.codex`, `DevConsoleMenu.codex`, `DevConsoleBoot.codex` | -- | The Environment shell that hosts the seven roles |
| `UefiConsole.codex`, `UefiBoot.codex` | -- | Real-hardware front of the Environment |

So the foundational pieces of Part Three (`Annotation`,
`SignedAnnotation`, `Discussion`, `RepoProtocol`, `KeyManager`,
`BuildRecord`) and Part Four (`ConsoleEditor`, `CodeBrowser`,
`CdxInspector`, `DevConsole`, `FirstBoot`, `UefiConsole`) all
exist. They are not yet *integrated* into the live workflow that
Damian described. That integration is the actual work.

### H.3 -- Compiler / dev environment scope ruling

Damian's clarification (2026-05-08) about what's in scope for the
compiler product itself:

> "I want all these abilities built into the main compiler if they
> are about the source or the compiler itself, dev interface, test
> interface, annotations, editor, stuff like that. when someone
> wants to build a 3d model program or a web browser, that is
> outside."

The line is sharp:

| In the compiler product (lives in the seed) | Out (apps built *with* the compiler) |
|---|---|
| Source / lexing / parsing / IR | 3D modeling tools |
| Compiler / codegen / emitters | Web browsers |
| Reader / Writer (editor) | Games |
| Verifier / Narrator / Explorer / Historian | Office suites |
| Test interface (sweep, pingpong) | End-user applications generally |
| Annotations (read + write + query + bubble) | |
| Trust UI (publishers / scores / threshold) | |
| Identity + key management | |
| Build/test/publish loop | |
| Repository protocol client (proposal/verdict UX) | |

This is the founding-vision Environment (`Ch 10`), plus the build
loop, plus the repository client. Everything in the left column
ships in the seed CDX. Everything in the right column is a
separate program someone writes against this substrate. The
practical implication for *this* design: the lift-target for
annotation operations is the **existing** ConsoleEditor /
CodeBrowser / DevConsole -- not a separate `AnnotationsCli`
chapter. Section C's proposal of a new `codex.works/
AnnotationsCli.codex` chapter is **withdrawn**. Reasoning: a CLI
implies a separate program; the founding spec describes a *single*
unified Environment in which annotations are an inherent property
of reading and writing. The Reader surfaces annotations inline
(Ch 10's Narrator role); the Writer adds annotations as part of
authoring; the Explorer queries them; the Historian shows their
provenance over time. There is no separate operator tool.

### H.4 -- First-boot key ceremony (already partly built)

Damian's described flow:

> "in the first time you get to generate your own private key, and
> save it off to a separate usb stick or a eventually a harddrive
> somewhere, or well somewhere safe for storage. Then that becomes
> how you sign all your cdx files and annotations and stuff."

This is `FirstBoot.codex`'s `PhaseIdentity` -- the existing chapter
already has the phase enum. What's missing:

- The *external storage step*: prompting the user to insert a USB
  stick (or pick a path on a separate drive), writing the private
  key encrypted-at-rest, recording the public key in the on-device
  keyring. `KeyManager.codex` has keypair generation and
  fingerprints; it does not yet have the export-to-removable-storage
  flow.
- The *recovery* counterpart: re-mount the USB on a future device,
  validate the key against the user's identity record on the trust
  lattice.
- The *threshold* moment: telling the user that this key is now
  the root of every signature they make on this machine. Codex's
  signed-CDX + signed-annotation infrastructure (CL 751, CL 763,
  the SignedAnnotation chapter) all read from the device's
  identity table. Today the device seed comes from RDRAND; the
  external-key-on-USB story replaces or augments that.

Concrete phasing (extends Section F):

- **A8** -- `KeyManager.codex` extension: `export-keypair-to-path`
  (encrypted with a passphrase the user types), `import-keypair-from-path`,
  `verify-imported-keypair`. Effects: `[FileSystem,
  IdentityWrite]`. Uses existing `Ed25519` and `Sha512` forewords.
- **A9** -- `FirstBoot.codex` `PhaseIdentity` wiring: prompt for
  removable-storage path, run A8 export, record fingerprint to
  the on-device identity table, advance phase. Recovery on a
  fresh boot: detect existing keypair on USB, offer import
  instead of generate.

### H.5 -- The Environment (Ch 10) integration gap

This is the big one. Each of the seven Environment roles has
existing scaffolding but the inline-annotation experience Damian
described is the *integration*, not new chapters.

**Reader (CodeBrowser, ~235 LOC today).** The annotation bubble
is a Reader feature. When the Reader displays a definition, it
queries `AnnotationStore` for annotations targeting that
definition's stable id, filters by the user's trust threshold
(see H.6), renders them inline as overlays (`codex.foreword.ui/
Overlay.codex`) styled by the active Theme. The Narrator role
(Ch 10) is the same surface specialized to "explain in plain
language" -- for a definition, the Narrator collapses the
attached annotations of kind Rationale + Discovery into a
plain-language summary.

Concrete: `CodeBrowser.codex` grows a `cite` to `AnnotationStore`
and to `Overlay`. A new function
`code-browser-annotations-for-target : NodeId -> [AnnotationsRead]
List Annotation` filters and returns. Render path uses existing
`ui.Theme` + `ui.Overlay`.

**Writer (ConsoleEditor, ~170 LOC today).** Authoring annotations
inline. While editing, the user can attach an annotation to the
expression at cursor (the AST/IR NodeId is computed deterministically
per Section D.1). New input event in the editor's command map:
"add annotation to current node." On commit: signs with the
user's key (via `KeyManager`), stores via `AnnotationStore`,
publishes to the grid (H.7) if outbound is enabled.

**Verifier (CdxInspector exists, partial).** Annotation verify
pass -- already mostly designed in D.4 + D.10 + D.14. This becomes
a Verifier subroutine the Editor calls on every load.

**Explorer (CodeBrowser provides the navigation surface; trust
visualization absent).** The trust UI is the Explorer
specialization for the publisher / vouching / proof / test
records of Ch 9. New chapter `codex.works/TrustExplorer.codex`
renders the publisher list, trust scores from the existing
`codex.os.trust/TrustLattice.codex`, vouching graph, proof
coverage, test coverage. Filters and threshold sliders. This is
the "trust UI" Damian described.

**Executor (CompilerDriver exists).** Already wired to run things.

**Narrator (no chapter yet; specialization of Reader).** The
plain-language surface. Initial implementation: collapse
annotations (Rationale / Discovery / Doctrine kinds) into a
sentence per definition; later, model-assisted summary via the
agent runtime.

**Historian (no chapter yet).** The supersession chain visualizer
(Ch 12). For any definition, walks the content-addressed
ancestry, shows attribution + verdicts at each step. New chapter
`codex.works/Historian.codex`. Reads from `RepoProtocol`'s
fact log.

### H.6 -- The Trust UI surface (Ch 9 made visible)

Concrete chapter: `codex.works/TrustExplorer.codex`. Cites
`TrustLattice`, `AnnotationStore`, `RepoProtocol`,
`codex.foreword.ui/{Theme,Layout,Widget,Render,Event}`.

User-facing controls (Damian's words):

> "see publishers... control the trust you need for the messages
> to come through... block garbage, respond, resolve bugs right
> there."

Maps to four operations:

1. **Publisher view** -- list of identities the user has
   encountered, with their trust score, vouching path to the
   user's identity, count of annotations / proposals / verdicts
   they've authored, and proof/test coverage on definitions they
   own.
2. **Trust threshold control** -- a per-channel slider (the channels
   being annotation kinds: Rationale, Invariant, Warning,
   Discovery, Doctrine, Todo, plus the proposal/verdict streams).
   Annotations from publishers below threshold are filtered out
   of the Reader bubble by default but never deleted.
3. **Block list** -- explicit denylist beyond threshold. Public
   keys on this list have their annotations *hidden*, not
   removed. (Ch 12: "nothing is hidden, everything is true" -- but
   the *user's view* can filter; the repository state stays
   complete.)
4. **Inline response** -- from the bubble, the user can write a
   response (a Discussion post on the same target) or issue a
   Verdict (Accept / Reject / Amend / Abstain) on a Proposal.

The threshold isn't a single number; per Ch 9 it's multi-axis
(proof coverage × test coverage × trust score × topical match).
The slider is a UX simplification over a richer underlying query.

### H.7 -- The live workflow ("the grid")

Damian's described loop, mapped to the founding spec and
existing code:

> "someone browses along, they make an annotation, i will be
> editing along, and the message gets forwarded to me through the
> grid. i might get a notify throbber or overlay or toast,
> whatever, and i go look, say yeah, fix the bug maybe even with
> the code suggestion in the annotation. and boom i build, test,
> and publish the update on the spot, the annotator would get a
> response saying the suggestion was accepted and the new code is
> available."

In founding-spec vocabulary:

1. **Annotator** browses code in the Reader. Authors an annotation
   on a target definition. If the annotation includes a code
   change, it is a **Proposal** in Ch 13's sense -- definition +
   justification + (optional) comparison-to-superseded. The
   annotation is signed with the annotator's Ed25519 key
   (`KeyManager` + `SignedAnnotation`).
2. **Grid forwarding** -- the annotation/proposal is published to
   the trust network via `codex.os.trust/TrustTransport.codex`
   (already exists, CL 811). Subscribers downstream of the
   annotator in the trust graph receive the message; the
   `TrustService` (CL 817) routes by topic and threshold.
3. **Recipient (Damian)** is editing in the Writer. The Editor's
   notification surface (existing UI primitives:
   `ui.Animation/Throbber`, `ui.Overlay`, `ui.Sound`) raises a
   toast / overlay / throbber. The recipient opens the bubble.
4. **Review.** The Editor renders the annotation inline at the
   target NodeId. If the annotation is a Proposal carrying a code
   suggestion, the Editor shows the proposed diff against the
   current source.
5. **Verdict (Ch 13).** Recipient issues `Accept` (apply suggested
   code), `Reject`, `Amend` (edit and apply), or `Abstain`. On
   Accept: the Editor applies the diff, runs the gates
   (`pingpong-self.ps1` + `sweep.ps1` -- the test interface, in the
   compiler product per H.3), signs the resulting CDX, publishes
   the new fact via `RepoProtocol`. The original proposal is
   marked superseded by the new fact (Ch 12).
6. **Response (back to annotator).** The annotator's Editor
   subscribes to the proposal's verdict-stream. The accepting
   verdict propagates back through `TrustTransport`. Annotator's
   notification surface fires: "Proposal at hash `0x...` was
   accepted by Damian; new fact at hash `0x...` is available."
   Annotator can sync (pull the new fact via content-addressed
   lookup) or compare.

Nothing in this loop is novel. Everything is named in the founding
spec and partly implemented in `codex.works/` and
`codex.os.trust/`. The integration is what's missing.

### H.8 -- Revised phasing

Section F's A0-A7 phasing was scoped to "build a CLI." That's
withdrawn. The replacement phasing is "wire the Environment to
the existing chapters." Each phase is a CL-sized chunk integrated
into the *existing* codex.works chapters, not a new chapter.

| CL | What | Scope marker |
|---|---|---|
| **A0** | rename to `codex.annotations/` **NEVER HAPPENED**; addendum landed. Root is `annotations/` (Section B) | structural |
| **H1** | Reader inline annotations: `CodeBrowser.codex` cites `AnnotationStore`, `Overlay`, renders annotations on the displayed definition. Read-only | Reader (Ch 10) |
| **H2** | Writer annotation authoring: `ConsoleEditor.codex` adds "annotate at cursor" command; signs via `KeyManager`; stores via `AnnotationStore`. No outbound yet | Writer (Ch 10) |
| **H3** | Trust threshold filtering: read path filters by trust score from `TrustLattice`. Default threshold conservative; UI control comes in H6 | Discovery (Ch 9) |
| **H4** | First-boot key-to-USB: `KeyManager` `export-to-path`, `import-from-path`; `FirstBoot` `PhaseIdentity` extension. The "first-time signing key" Damian described | Identity (Ch 12) |
| **H5** | Verifier integration: `CdxInspector` runs annotation verify (D.4 / D.10 / D.14) on Editor load. Schema migration via `AnnotationsMigrate.codex` | Verifier (Ch 10) |
| **H6** | `TrustExplorer.codex`: publisher view, threshold slider, block list, inline response/verdict UI | Explorer (Ch 10) |
| **H7** | Outbound proposal: `RepoProtocol` exposes `publish-proposal`; signed annotation with code-suggestion payload propagates via `TrustTransport` | Repository (Ch 8) |
| **H8** | Inbound notification: `ConsoleEditor` subscribes to `TrustService` topics; raises throbber/overlay/toast on incoming proposal targeting a definition the user has open | Live grid (Ch 13) |
| **H9** | Verdict + accept-publish loop: in-Editor `Accept` runs gates, publishes superseding fact, propagates verdict back through `TrustTransport` | Verdict (Ch 13) |
| **H10** | Sync/response: annotator's editor receives verdict on subscribed proposal; offers fetch-new-fact / show-diff | Supersession (Ch 12) |
| **H11** | Narrator chapter: collapse Rationale + Discovery + Doctrine annotations into plain-language definition summary | Narrator (Ch 10) |
| **H12** | Historian chapter: walk supersession chain for any definition, render attribution + verdicts | Historian (Ch 10) |

H1-H6 stand on their own (give read + write + trust + key
ceremony + verifier hookup, no network yet). H7-H10 are the
distributed grid. H11-H12 round out Ch 10's Environment roles.

### H.9 -- What this design does NOT propose

- Does **not** propose a separate `AnnotationsCli` chapter. (Withdrawn.)
- Does **not** propose new vocabulary where the founding spec
  already names a concept. "Proposal," "Verdict," "View," "Fact,"
  "Narrator," "Historian," "supersession" -- all from Ch 8 / 9 / 10
  / 12 / 13. Use these names; do not invent new ones.
- Does **not** propose breaking the existing `annotations/`
  sidecar storage. The sidecars remain the canonical on-disk form;
  the Environment is the user surface; the grid is the network
  layer. Three layers, one model.
- Does **not** propose changing the trust lattice, the verifier,
  the identity table, or the existing `codex.os.trust/*`
  infrastructure. All of it is reused.
- Does **not** lock in a notification UX. Throbber / overlay /
  toast are all available in the UI quire; the Editor picks per
  user preference (a Theme-style decision).
- Does **not** assert what's Tier 1 in the library gap analysis.
  H1+H2 are Tier 1 because they make the existing 50 annotations
  visible to the editor user. H3-H6 are Tier 2. H7-H12 are Tier 3
  but are *the point of the system* -- without them the loop never
  closes.

### H.10 -- Revised open questions

(Replaces Section G's questions 1–5; all five were premised on
the withdrawn CLI proposal.)

1. **First-boot key storage default.** USB is one option; another
   is an encrypted file in the user's home directory; another is
   a hardware token. What's the *default* in the FirstBoot wizard,
   and what alternatives does it offer? This decision affects A8.
2. **The bubble's default trust threshold.** What's the floor
   below which an annotation is filtered out without being
   counted? Conservative default (signed-only from a vouched
   identity) means the system feels empty for new users;
   permissive default lets noise through. Per-kind thresholds
   help -- Warning bubbles up cheap, Doctrine costs more.
3. **Auto-accept proposal classes.** Damian described the live
   loop ending with "boom i build, test, and publish the update
   on the spot." Some classes of proposal (typo fix, comment
   tighten, identifier rename) might auto-accept after gates pass
   if signed by a trusted-enough author. Or the user always
   reviews. Where's the line?
4. **Notification surface defaults.** Throbber vs overlay vs toast
   vs audio cue -- these are UX preferences. Default? Per-kind
   default?
5. **Outbound default.** When a user writes an annotation, does it
   publish to the grid by default, or stay local until they
   explicitly publish? The founding spec's "everything is true,
   nothing is hidden" leans toward publish-by-default; ergonomics
   leans toward draft-by-default with a publish action.

The original design's last line *still* applies: "the agent can
implement the system without these decisions -- but the system
will degrade over time unless these gaps are addressed." That is
truer here than in Section D -- these are decisions about user
experience and live-system policy, not just data model.

---

## Addendum I -- Append-Only Mutation Log Backing Store

**Added**: 2026-05-14
**Source**: Review of LynnColeArt/Kilo-SDK (Go library for durable
multi-agent memory), cross-referenced against our annotation system.

### Motivation

The current sidecar storage (`annotations/**/*.json`) is a mutable
JSON file per chapter. An agent rewrites the whole file when adding an
annotation. This has three weaknesses:

1. **No crash safety.** A crash mid-write loses the file. There is no
   partial-write recovery.
2. **No mutation history.** Who added what, when, in what order is lost
   unless you check Perforce history. Once we replace Perforce (gap 6),
   that backstop disappears.
3. **No staleness tracking.** An annotation's timestamp says when it was
   written, but not which depot state it was written against. An agent
   cannot ask "are the annotations on X86_64Chapter still current
   relative to CL 1400?" without manual inspection.

### Proposal

Make the append-only mutation log the backing store for the existing
annotation types. The JSON sidecars become a materialized view rebuilt
from the log.

**On-disk layout:**

```
annotations/
  .log/
    segment-0001.log       -- CRC-framed mutation records
    segment-0002.log
    snapshot-latest.json   -- compacted catalog for fast startup
  codex/compiler/Emit/X86_64Chapter.json  -- materialized view (unchanged format)
  codex/plugs/pe/PeWriter.json
  ...
```

**Mutation record fields:**

- `sequence` -- monotonic log position
- `operation` -- one of: `add`, `update`, `archive`, `purge`
- `annotation` -- the full Annotation record (hash, target, kind, author,
  body, timestamp)
- `depot-cl` -- the depot changelist number at time of mutation
- `idempotency-key` -- optional dedup key for crash-safe retries
- `crc` -- CRC-32 of the frame for integrity checking

**Replay:** On startup, `AnnotationStore` replays the log from the last
snapshot forward, rebuilding the in-memory catalog. The JSON sidecars
are regenerated as a side effect (a projector in Kilo terminology).

**Staleness queries:** Any query against the store can compare the
annotation's `depot-cl` against the current depot head. If the code
under the annotation's target NodeId has changed since `depot-cl`, the
annotation is flagged stale.

**Graph edges (future):** Once the log exists, adding typed edges
between annotations (e.g., "this Invariant on Lowering.codex depends on
this Doctrine on X86_64Chapter") is a new mutation operation on the same
log. No separate storage needed.

### What stays the same

- The 6 annotation kinds (Rationale, Invariant, Warning, Discovery,
  Doctrine, Todo) -- unchanged.
- Ed25519 signatures and trust lattice -- unchanged. Kilo has no trust
  model; ours is better.
- Semantic NodeId anchoring -- unchanged.
- JSON sidecar format -- unchanged (now a derived view, not the source
  of truth).
- All H1-H12 phases -- unchanged. The log is an infrastructure change
  underneath AnnotationStore, not a user-facing change.

### What changes

- `AnnotationStore.codex` gains log-append and log-replay logic
  (~100–200 LOC).
- `AnnotationDriver.codex` writes through the store (no direct sidecar
  writes).
- A sidecar projector regenerates JSON from the log after each mutation.
- New `depot-cl` field on mutation records enables staleness queries.
- New `idempotency-key` field enables crash-safe agent retries.

### Scope estimate

~300–500 lines of Codex on top of the existing annotation
infrastructure. The append-only log is sequential disk writes -- natural
on bare metal. Linear types enforce clone-on-boundary discipline for
free. No threading needed (single-writer, single-threaded on bare
metal). No vector search, no HTTP adapters, no Go runtime.

### Phasing

This is infrastructure work that should land before H7 (outbound
proposals), since the mutation log gives proposals a proper audit trail.
It can land any time after H2 (annotation authoring), since that's when
agents start writing annotations through the driver.

### Relationship to gap 6 (repository protocol replaces Perforce)

The mutation log is a stepping stone toward source-as-facts. Once
annotations have their own append-only fact store with content
addressing, applying the same pattern to `.codex` source files is a
small incremental step. The log format, replay logic, and staleness
tracking are reusable.