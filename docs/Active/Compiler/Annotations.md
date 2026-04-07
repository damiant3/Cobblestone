Codex Annotation System
High‑Level Design (Pure Text)

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
Inlining, dead‑code elimination, constant folding, lambda lifting — all of these destroy or duplicate IR nodes.

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

And the agent can implement the system without them — but the system will degrade over time unless these gaps are addressed.