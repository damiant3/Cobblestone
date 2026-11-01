# ACCP policy as a decidable language

Status: ruled by root (section 7), Damian may overturn. Owner val. Source row:
`apps/accp/accp-backlog.md`, "Policy is not a decidable sublanguage"
(`docs/Reference/Nectry.md` section 5, item 3).

## 1. The gap

ACCP's grants are policies (`codex/os/trust/PolicyEngine.codex`), written
through `ProseGrant` text and lowered by `lower-policy`
(`codex/os/trust/PolicyProse.codex`). The only thing anything does with a
policy is EVALUATE it on one context: `eval-policy` answers allowed or not for
one capability, direction, trust score, time and agent. Nothing answers a
question ABOUT a policy:

- can any context reach `FileSystem` for writing under `/`?
- is there any time at which agent K may use `Network` for writing?

(A policy names a capability by its base name, `Network`, and its direction as
a separate byte, `cap-dir-read`, `cap-dir-write` or `cap-dir-readwrite`;
`CdxVerifier.codex` builds its contexts that way.)
- does this edit to the policy allow anything the old one refused?
- which of its rules can never fire?

Nectry's NectryCore restricts its policy language so that questions of this
kind are decisions with a yes or no, not test runs. This design finds that
PolicyEngine is ALREADY such a language, states the procedure that decides
it, and names the two places where today's language cannot express the
question the row asks.

## 2. PolicyEngine is already decidable

A policy is an ordered list of rules, each a condition and an outcome; the
first rule whose condition holds decides, and no match is a deny
(`eval-rules`). A condition is a finite formula (`AllOf`, `AnyOf`, `NotCond`,
`AlwaysTrue`) over four kinds of atom, each a test on one field of the
six-field `PolicyContext` (the sixth, `scope`, is read only by an
`AllowWithScope` outcome, `policy-scope-within`):

| atom | field | test |
|---|---|---|
| `CapabilityMatch name dir` | capability, direction | equality on the name; direction equal, or `dir` 2 (either) |
| `TrustAbove t` | trust-score | `>= t` |
| `TimeWindow a b` | current-time | `a <= time <= b` |
| `AgentIs id` | agent-id | equality |

There is no recursion, no arithmetic on fields and no unbounded data. So every
question in section 1 is decided by **region enumeration**:

1. For each field, collect the constants the policy's atoms name: capability
   names with their directions, agent ids, trust thresholds, window ends.
2. Pick candidate values per field such that every atom's answer is realised
   by some candidate, using points, not intervals (an interval between two
   integer ends can be empty):
   - capability: each named name, and one name the policy never mentions;
   - direction: a byte (0 to 255). Each `dir` value named by an atom, and one
     value no atom names. In an atom, `dir` 2 (`cap-dir-readwrite`) is the
     wildcard; as a CONTEXT value 2 is an ordinary byte that only `dir` 2
     atoms match, so it is not "every direction";
   - agent: each named id, and one id the policy never mentions (this is what
     makes `NotCond (AgentIs x)` and `NotCond (CapabilityMatch ...)`
     reachable);
   - trust (0 to 65535): each threshold t and t - 1, clipped to the domain
     (a threshold above 65535 is a rule that never holds; one at or below 0
     always holds);
   - time (any Integer): each window end e and e - 1 and e + 1, except that
     e - 1 is skipped at the smallest Integer and e + 1 at the largest, where
     the step would trap (a window with a > b holds nowhere and contributes
     only its points; the lowest end's e - 1 is the value below every end);
   - scope: the empty scope, each scope an `AllowWithScope` names, and one
     scope no rule names. A scoped rule's answer depends only on which named
     scopes contain the context's scope, and those always form a chain whose
     deepest member realises the same answers, so these points cover every
     case.
3. Every combination of candidates, with capability and direction chosen
   together, is a context, and each is evaluated with `eval-policy` itself.
   Every context of the real domain agrees with some candidate on every atom,
   so the candidates realise every decision the policy can make.
4. A question is answered by scanning the representatives: "can K ever use
   `Network.Write`" holds exactly when some representative with that agent and
   capability is allowed, and that representative is the witness.

**The answer comes with a witness, and the witness is checked by the
evaluator the runtime uses.** The enumerator never decides by its own
reading of a rule: it builds contexts and asks `eval-policy`. A wrong region
cut can make it miss a context (a false "never"), never invent one, and the
section 6 corpus is built to catch exactly that.

Cost: the product of the candidate counts, (names + 1) x (named directions +
1) x (agents + 1) x (2 x thresholds) x (3 x window ends + 1), times one
evaluation each. A
`ProseGrant` policy of a dozen grants over three agents is thousands of
evaluations; a policy with hundreds of window ends is not. The procedure
refuses past 200,000 evaluations rather than run unbounded, naming the
candidate count of each field in the refusal (section 7 item 3,
`decide-budget`); the product is capped as it is built, so it cannot wrap.
Questions that fix a field (one agent, one capability) collapse that factor
to one, which is the common case.

## 3. What today's language cannot ask

**Scope is not in the language.** `PolicyContext` has no scope field, and the
outcome `AllowWithScope s` produces a decision whose only trace of `s` is the
reason text "allowed within s" (`PolicyEngine.codex`, `eval-rules`).
`PolicyDecision` has no scope field, so no caller can narrow by it:
`CdxVerifier.codex` `check-all-caps` reads `allowed` and the reason text, and
`VerifyCache.codex` encodes the scope only into a cache key. So
`AllowWithScope "/home/"` allows exactly what `Allow` allows. The row's own example question, "can any lease
ever reach `/`?", therefore has no meaning in the language today: no policy
can refuse `/` while allowing `/home/`. `AllowWithDuration` is the same
shape: its duration is reason text.

**A grant's subject and quota are not in the lowered policy.** `ProseGrant`
carries `subject`, `quota-minutes` and `quota-period`, and `pol-lower-grants`
lowers only the capability and the windows (`AllOf [CapabilityMatch ...,
AnyOf windows]`). The lowered rule has no `AgentIs`, so a grant written for
"Jake's agent" allows that capability to every agent the policy is consulted
for, and "no more than an hour a day" is not something the policy refuses.
Region enumeration answers these truthfully, and the truthful answer to "can
any other agent use it?" is yes, which is the point of being able to ask.

All three are gaps in the policy language, and they are also gaps in what the
runtime enforces today. Section 7 asks whether to close them as part of this
work.

## 4. The questions shipped

Each is a function over a `Policy` answering a verdict and, when one exists,
a witness `PolicyContext`:

- `policy-can-allow pol cap dir`: some context with exactly this capability
  and direction byte is allowed (with a scope, once section 3's first gap is
  closed). "Any direction" is asked as one call per direction candidate.
- The questions are about the rules. A policy's `min-trust` is a second gate,
  applied by `verify-author` and not by `eval-policy`, so a witness is reported
  with its trust score and the policy's `min-trust` beside it, and a caller
  asking about the verifier's whole decision intersects the two.
- `policy-widens old new`: some context is allowed by `new` and refused by
  `old`. This is the one a grantor needs on every edit, and it is a scan of
  the regions of both policies' constants together.
- `policy-dead-rules pol`: the rules that decide no candidate context, read
  from `PolicyDecision.matched-rule`. A rule shadowed in part still decides
  some context and is not dead; one shadowed in full, or with an
  unsatisfiable condition, is. A diagnostic for the person writing the policy.

## 5. Where it sits

In `codex/os/trust/PolicyDecide.codex`, beside `PolicyEngine.codex`, as a
chapter that cites it, so that the enumerator and the evaluator are the same code the verifier
and the trust node run. The ACCP conduit exposes the questions as a read-only
operation over a policy it holds (no program runs), when the conduit holds
policies, which is the same lease precondition the flow design names
(`AccpInformationFlow.md` section 4).

## 6. Proof of the procedure

A procedure that answers "never" for everything looks exactly like one that
works for a grantor who never sees a counterexample (L-FALSIF). Each rung
lands with a corpus of hand-written policies in `codex/test/apps/policy-decide-*`,
graded in both directions:

- **must find a witness**, one policy per atom kind and combinator: a
  capability allowed only in a window's last minute, only at a trust exactly
  equal to a threshold, only for one named agent, only under a `NotCond`
  reachable through an unnamed agent or an unnamed direction, only at a
  direction byte other than 0, 1 and 2, only as the second rule after a
  narrower deny;
- **domain edges**: thresholds 0 and 65535 and one above 65535, a window with
  start after stop, windows whose ends are adjacent integers, a time below
  every window;
- **must answer never**, the same policies with the reaching rule removed or
  shadowed;
- **each witness is re-evaluated** by `eval-policy` in the test and must be
  allowed, so a wrong witness fails on its own;
- each region cut is shown to matter by sabotaging it (dropping the "at the
  boundary" region of `TimeWindow`, say, match count printed and non-zero) and
  watching its arm answer never.

## 7. Rulings

Root, 2026-11-01, each recommendation below as written: rulings 1 and 2 fix
grants that allow more than their text says (`AllowWithScope` allowing
everything, a subject widened to every agent, an advisory quota), under
Damian's 2026-11-01 rule that Codex is "safe by default"; ruling 3 refuses
past a fixed count, with the count in the refusal. Damian may overturn any
of the three.

1. **Scope in the policy language.** Add a scope field to `PolicyContext`,
   tested by the same path, authority and channel relation the type checker
   uses (`scope-admits`), and make `AllowWithScope` yield a scope the lease
   is narrowed to. Recommended: yes, as its own change before rung 1, because
   today's `AllowWithScope` silently allows everything (section 3), which is a
   defect whether or not this design proceeds. It changes `PolicyContext`, so
   every constructor of one is a caller to update.
2. **Subject and quota.** Lower a grant's subject as `AgentIs` (recommended:
   yes, in the same change as ruling 1; it is a one-line lowering and the
   current behaviour widens every grant to every agent). Lower `ProseGrant`
   quotas into something the runtime refuses
   (a counter the conduit keeps per lease and period), or state in
   `PolicyProse` that quota is advisory. Recommended: lower it; a grant text
   that reads "no more than an hour a day" and allows more is the
   English-to-grant seam of Nectry item 4 in its plainest form.
3. **The enumeration budget.** Refuse a question past a fixed number of
   representative evaluations (recommended, with the count in the refusal), or
   let the caller pass a budget.

## 8. Quota (rung 2), proposed interface

A quota is use of a grant, measured as the execution time of the runs it
admitted, per period: `per day` is the host's local calendar day, `per
session` is the lease's whole window. Three parts, one per owner:

1. **Lowering** (`PolicyEngine`, `PolicyProse`, val). A grant with a quota
   lowers to the outcome `AllowWithQuota (minutes) (period) (inner)`, which
   wraps the outcome the grant would have had (`Allow` or
   `AllowWithApproval`), so a grant with both an approval and a quota keeps
   both. `eval-policy` decides it as `inner` decides, names the quota in the
   reason, and carries it in `PolicyDecision.quota-minutes` (-1 when none)
   and `quota-period` beside `matched-rule`. A grant without a quota lowers
   as today. Built: graded by `codex/test/apps/policy-prose-approval`.
2. **The ledger** (`apps/accp/serve.ps1`, red). The conduit restores its heap
   after every request and each session opens a new journal, so a counter in
   the conduit forgets on restart, which is a refusal reset by a restart. The
   host keeps the count instead: `-UsageLedger <file>`, an append-only
   JSON-lines file of `{grantor, holder, rule, period, ms}`, read once at
   start and appended after every granted run with the execute phase's
   measured milliseconds. It is keyed by grantor, holder and the deciding
   rule rather than by lease, so a fresh lease for the same grant does not
   reset the count. A bridge `usage` answers the milliseconds already spent
   for one key.
3. **Admission** (`apps/accp/AccpGrant.codex`, red). When the deciding rule
   carries a quota, admission asks `usage`, refuses at or past the quota
   ("quota of 60 minutes per day is spent"), and otherwise lowers the run's
   `ms` limit to what remains, so no run can carry the total past the
   quota; the run's measured milliseconds are charged after it.

Acceptance: a grant of an hour a day, with a ledger holding 60 minutes for
that lease and day, refuses; holding 59 minutes 59 seconds, it admits with an
`ms` limit of 1000; a different day, holder or rule starts at zero, a fresh lease does not; a conduit
restarted over the same ledger keeps the count.

## 9. Rungs

| rung | delivers | acceptance |
|---|---|---|
| 0 | region enumerator over the six fields (`codex/os/trust/PolicyDecide.codex`); `policy-can-allow`, `policy-widens`, `policy-dead-rules`; witnesses re-evaluated | built: `codex/test/apps/policy-decide-corpus`, both directions, each region cut sabotaged in the CL |
| 1 | scope in `PolicyContext` and an enforced `AllowWithScope` (ruling 1); scope regions by path, authority and channel | the "reach `/`" question answered on a policy that allows only `/home/`; every `PolicyContext` caller updated and compiled |
| 2 | quota lowered and enforced (ruling 2) | a grant of an hour a day refuses the sixty-first minute |
| 3 | the questions as a read-only ACCP operation over a held policy | built (reek): the `policy` op (`apps/accp/AccpPolicyAsk.codex`, README "Policy questions"); `codex/test/apps/accp-policy-ask`, and the `test.ps1` policy arms on a held allowing and denying policy |
