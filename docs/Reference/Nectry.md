# Nectry -- Chlipala's company, and what it means for Codex

**Subject**: Nectry, https://nectry.com/
**Reviewed**: 2026-10-05, from the public site, Chlipala's home page and
third-party listings. Nothing here was obtained from Nectry directly.
**Companion**: `docs/Reference/Chlipala-StructureAndGuarantees.md` (his
research record and the collaboration angles). This note covers only the
company.

---

## 1. What Nectry is

| fact | value | source |
|---|---|---|
| Founded | 2018 | CB Insights |
| Based | Saratoga, California | CB Insights |
| CEO and CTO | Daniel Winograd-Cort (Yale PhD in programming languages; Target, Luminous) | nectry.com/team |
| Chief Scientist | Adam Chlipala (MIT professor); "Developed the core product and the Ur/Web line of research NectryCore is built on" | nectry.com/team |
| Chief Product Officer | Diou Shi (LogRocket, Catalant) | nectry.com/team |
| Stage | beta; "pricing still being finalized" | nectry.com/faq |
| Funding, customers | not published | none found |

Chlipala's own page calls him "Founder/CEO". Nectry's team page names
Winograd-Cort as CEO and Chlipala as Chief Scientist. Use the team page's
titles when writing to either man.

**The product changed shape.** The MIT ILP and CB Insights descriptions
present a **no-code builder for enterprise applications**: a component
architecture in richly typed functional programming (Ur/Web plus UPO, his
library of composable application components), with a large-language-model
front end, so that "programming [is] like chatting with a person building
your app". The site in October 2026 sells something else: **a control layer
for AI agents**, headlined "Stop trusting your AI. Start controlling it."
The application-builder lineage survives as the technology underneath.

## 2. The product as shipped: Nectry MCP

A gateway that sits between an agent (Claude, ChatGPT, Cursor or a custom
agent) and business tools (Google Workspace, GitHub, HubSpot, Salesforce,
Slack, any REST or MCP API), exposed to the agent as one MCP server.

1. **Connect**: authorize services from an integration catalog.
2. **Configure**: enable operations one by one and attach rules to each,
   "rules, not blanket access".
3. **Review**: an administrator inspects edits, including edits drafted by
   **Mozart**, an assistant that writes configuration from plain language
   ("Only let Gmail send to @nectry.com addresses").
4. **Save & Deploy**: publish the reviewed tool set to MCP clients.

Every read, write and change is logged (timestamp, application, user,
action). The FAQ states the enforcement model in one sentence: Nectry
"expresses deterministic constraints in the tools the agent uses and blocks
operations that fall outside those constraints before execution."
Hallucinations are not prevented; their unauthorized actions are.

Hosting: Nectry Cloud, where customer data passes through Nectry's servers
(not used for training), or a containerized self-hosted deployment for
enterprise customers.

## 3. The technology claim: NectryCore

The technology page makes the stronger claim, and it is the part that
matters to Codex.

- **NectryCore** is a domain-specific language layered on Ur/Web, and it is
  **deliberately not Turing-complete**. The restriction is the point:
  "Information-flow analysis reduces to graph reachability" and
  "capability scope checking reduces to constraint satisfaction over a
  bounded domain."
- It was **codesigned with the AI that generates code in it**: "The
  language and the generator are pieces of the same system." This is the
  "codesign for legibility" argument from his Substack, made into a product.
- The compiler decides four properties, each answered "holds" or "does not
  hold", "a theorem, not a probability":
  1. **Information flow**: what data can reach what output.
  2. **Capability attenuation**: what external systems the agent may
     contact, and under what conditions.
  3. **Tool-use scope**: which functions the agent may invoke, with what
     argument shapes.
  4. **Workflow integrity**: which actions require human authorization, and
     whether that requirement can be bypassed.
- The agent "is free to act within that space but cannot leave it. The
  system gets to surprise us exactly once, at generation time."

The research page backs this with four of his papers, all real and all
his: Ur (PLDI'10), the Ur/Web optimizing compiler (ICFP'15), Ur/Web (POPL'15)
and **UrFlow**, static checking of security policies that vary with
database contents (OSDI'10). UrFlow is the direct ancestor of property 1.

## 4. Assessment

**What is strong.**

- The thesis is right, and it is ours: a sentence in a prompt is an
  intent, not a property, and the limit belongs in a checker and a runner,
  not in the model's judgement.
- Restricting the language to make the properties decidable is an honest
  engineering trade. It buys yes/no answers where a general-purpose
  language gets only approximations.
- The provenance is real. Ur/Web is fifteen years of work, and UrFlow
  proved policy checking of this kind in 2010.

**What the public material does not show.**

- **The link between sections 2 and 3 is not stated.** The MCP product page
  makes no verification claim at all; the proofs are on the technology
  page. Whether the rules an administrator toggles are compiled into
  NectryCore and checked there, or checked by a conventional rule engine
  at the gateway, is not said. Until Nectry says so, treat the shipped
  guarantee as "a deterministic gateway with an audit log", which is useful
  and is not a theorem.
- **The proof is relative to the rules, and the rules are written by an
  LLM.** Mozart drafts the specification from plain language. The review
  step is the only thing between an ambiguous English request and the
  property that gets proved. "Only let Gmail send to @nectry.com" says
  nothing about cc, bcc, forwarding or reply-all, and a proof against the
  wrong rule is a proof of the wrong thing.
- **The guarantee covers only what passes through the gateway.** An agent
  holding any other tool, credential or network path is outside it. The
  claim "AI catastrophes eliminated" holds only for a deployment in which
  Nectry is the agent's sole route to the world.
- **The trusted base is large.** Ur/Web compiles to C and runs on an
  ordinary OS with a database. The tool semantics, meaning what a SaaS API
  call actually does, are modelled, not proved. In Nectry Cloud, the
  customer also trusts Nectry's servers with the data in flight.
- No pricing, customers, funding, evaluation or third-party audit is
  published (2026-10-05).

## 5. Against Codex

The nearest Codex work is ACCP
(`docs/Designs/Active/Compiler/AgentCompilerControlProtocol.md`), whose
first section states the same arrangement: "the limits live in the type
checker and the runner, never in the agent." Codex already ships an ACCP
conduit as an MCP server (`codex_run`, pure computation and buffered
Console only), so the two projects sell the same shape through the same
protocol.

| question | Nectry | Codex (ACCP) |
|---|---|---|
| What bounds the agent | NectryCore properties, decided at generation time | the effect row on `opening`, proved by the type checker, plus the runner's link surface |
| Language | restricted, not Turing-complete, so that properties are decidable | general-purpose; the row is the decidable part |
| Properties | information flow, capability attenuation, tool scope, workflow integrity | capability and scope (`effect-covered-by`; scopes narrow, never widen) |
| Who writes the policy | administrator, assisted by Mozart (LLM), then reviewed | grantor, as a `ProseGrant`, lowered by `PolicyProse.codex` |
| Evidence per action | audit log | a forensic fact per run and per refusal (source hash, compiler digest, row, grant, output hash) |
| Trusted base | Ur/Web, C compiler, OS, database, SaaS APIs, and Nectry Cloud when used | the seed's own fixed point; bare metal is not an ACCP runner today (raw memory is not confined) |
| Market | integrations for Google, Slack, Salesforce and others, shipping in beta | no third-party integrations |

**What Codex should take from Nectry.**

1. **Information flow is the property ACCP does not state.** The rows
   bound which effects and scopes a program uses, not which data can reach
   which output. Nectry's property 1, and UrFlow behind it, name the gap
   exactly. A program granted `[Console, Network "api.example"]` can still
   send everything it read to that host.
2. **Workflow integrity is a fourth row kind worth naming**: an action that
   requires a human, provably unbypassable. The `ProseGrant` and lease
   machinery is the natural home for it.
3. **A restricted sublanguage for policy.** `PolicyProse.codex` lowers
   grants into policy combinators. A policy language kept deliberately
   non-Turing-complete would let the conduit answer questions about the
   policy itself ("can any lease ever reach `/`?") as decisions, not runs.
4. **The specification step is the weak point for both projects.**
   Nectry's natural-language-to-rule review is the same seam as
   `ProseGrant`. Codex's Codex Prose Language (no implicit referent, no
   implicit quantity, no implicit order) is a stronger answer than
   free English, and is worth putting in front of Chlipala for that reason.

**Collaboration angle.** This adds a sixth angle to the five in the
companion note, and for the agent-safety market it is the most current:
Nectry proves properties of a restricted language that an LLM writes;
Codex proves effect bounds of a general language and owns its stack down
to bare metal. A Nectry rule set compiled to a Codex runner, or Codex
rows carrying NectryCore-style information-flow labels, is a concrete
joint artifact that neither side can build alone.

---

## Sources

- Nectry: https://nectry.com/ and the pages /technology/, /team/,
  /research/, /product/mcp/, /faq/ (read 2026-10-05)
- Adam Chlipala: https://adam.chlipala.net
- CB Insights: https://www.cbinsights.com/compare/nectry-vs-nocodez
- MIT ILP demo day: https://ilp.mit.edu/AprilDemoDay
- UrFlow, "Static Checking of Dynamically-Varying Security Policies in
  Database-Backed Applications", OSDI'10:
  http://adam.chlipala.net/papers/UrFlowOSDI10/
