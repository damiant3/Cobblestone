# ACCP requester authentication and the lease token

Status: ruled (section 7). Rung A: the sealed `GrantMsg` (val), the conduit admission (red). Rung B built (reek): `apps/accp/AccpRequestAuth.codex`, `serve.ps1 -RequestBinding`, wire contract in `apps/accp/README.md` "Request binding". Owner val. It closes the
second precondition `AgentCompilerControlProtocol.md` section 13.6 sets for
rung 6 (L3): "bind leases to an authenticated requester". The first, keeping
subject, scope and quota through policy lowering, is
`AccpDecidablePolicy.md` rulings 1 and 2.

## 1. The gap

ACCP section 7 admits a non-ambient row when, among other things, "the lease
is unexpired and its holder is the requester's identity". At head none of
that can be checked:

- the conduit refuses any request carrying `lease` (`apps/accp/AccpProtocol.codex`,
  `ap-request`), and a request carries no identity at all;
- `GrantMsg` (`codex/os/trust/AgentProtocol.codex`) names one identity,
  `from`, which `lease-grant` (`LeaseManager.codex`) makes the lease's
  holder, so the GRANTOR is not in the message;
- no chapter verifies a `GrantMsg` signature, and its signed bytes are not
  defined. The only signature check in `codex/os/trust` is `Handshake.codex`'s
  `ed25519-verify` over a nonce.

So a lease today is whatever a requester says it is. This design defines who
the requester is (section 2), what a lease is (section 3) and what the conduit
checks before a granted run (section 4).

## 2. Who the requester is

Two bindings, because the conduit runs in two settings.

**Instance binding (stdio, the MCP server).** The conduit is a child process
its harness launches for one agent, and its only input is that child's stdin.
The launch configuration names the requester's public key and the trusted
grantor keys, and the conduit reads it once at start. Every request on that
stdin is the configured requester's: the operating system's process boundary
is the authentication, and no request is signed. `describe` reports the
requester key and a hash of the grantor set, so a harness and an auditor can
see what the instance was bound to. This holds only while no other principal
can write to the child's stdin, which is true of a stdio child and false of any
socket; a conduit with instance binding refuses to listen anywhere else.

**Request binding (any shared transport: HTTP, a socket, a relay).** Each
request carries `requester` (an Ed25519 public key) and `signature` over the
SHA-256 of the request's canonical encoding (keys sorted, no whitespace,
`signature` itself omitted), which includes a `nonce` and an `issued_at`. The
conduit refuses a request whose signature does not verify, whose `issued_at`
is more than 60 seconds from its clock, or whose nonce it has seen inside that
window. The seen-set lives in memory for the window, so a conduit restarted
inside the window accepts one replay of a request signed just before. For any
run whose row holds a non-ambient label, a replayed read is a second use of a
grant and a replayed write a second effect, so every such run uses a
CHALLENGE instead: the requester first asks for a nonce, the
conduit draws it from its entropy source and keeps it, and the signed run must
carry that nonce, which the conduit spends on use. A nonce the conduit never
issued is refused, so a restart, which forgets every issued nonce, cannot be
replayed into.

Instance binding is the rung that serves the MCP conduit and is enough for
rung 6. Request binding is needed only when a conduit is shared.

## 3. The lease token

A lease is a `GrantMsg` (`codex/os/trust/AgentProtocol.codex`) sealed by its
grantor, carried in the request's `lease` field as the hex of
`encode-grant-body` (`TrustTransport.codex`):

| lease field | `GrantMsg` field |
|---|---|
| lease id | `id` |
| holder | `from` |
| grantor | `grantor` |
| audience | `audience` |
| capability, direction, scope | `capability` (`name`, `direction`, `scope`) |
| window | `not-before`, `expires` |
| policy hash | `policy-sha256` (32 bytes) |
| flow rules | `flow-rules` |
| gates | `gates` |
| conditions | `conditions` |

`signature` is Ed25519 by `grantor` over the SHA-256 of the 15 bytes
`codex-grant-v1` and a zero, followed by `grant-unsigned-body`, which is the
wire body without its signature field (`GrantSeal.codex`: `grant-digest`,
`grant-seal`, `grant-verify`). Every field above is signed. The conduit
decodes with `decode-grant-checked`, which answers `None` unless the bytes
are the canonical encoding of the grant they decode to, so a short field or
trailing bytes are refused before any check. A request signature (section 2)
uses its own tag the same way, so a signed object of one kind never verifies
as the other. `flow-rules` carries the forbidden pairs of
`AccpInformationFlow.md`, and `gates` the approval requirements of
`docs/Designs/Active/Security/HumanApprovalGrant.md` in whatever form that
design's rulings fix; both are empty lists when unused. A trust node refuses a
received grant whose seal does not verify (`TrustNode.codex`,
`node-eval-msg`).

Text fields are encoded one CCE code per byte (`frame-encode-text`), so a
signer outside Codex must encode CCE; a lease is minted by a Codex tool.

## 4. What the conduit checks

A run whose row has a label outside the ambient set is admitted only if a
presented lease satisfies all of these, each a distinct refusal reason:

1. **authentic**: `grantor` is in the instance's trusted set and `signature`
   verifies (`ed25519-verify`, `codex/foreword/core/Ed25519.codex`);
2. **held by the requester**: `holder` equals the authenticated requester
   (section 2);
3. **current**: `not_before <= now < expires`;
4. **covering**: every non-ambient label of the row is covered by the lease's
   capability name (`effect-covered-by`: equal, or the family before the dot)
   and direction (2 covers both, 0 only a `.Read` label, 1 only a `.Write`
   label, and a label with neither suffix needs 2). The leased scope is
   `capability.scope`, and a lease whose `scope.path` differs from it is
   refused. The row label's scope sits inside
   the lease's scope by `scope-subsumes` (`TypeChecker.codex`), the
   scope-against-scope relation. Not `scope-admits`, which tests a RESOURCE
   against a grant: its portless-resource rule would let a row scope
   `api.example.com` pass a lease of `api.example.com:443`, which the comment
   above `scope-subsumes` names as laundering. `path-admits` is textual, so a
   path scope holding a `.` or `..` segment, or an empty segment, is refused
   in both the row and the lease rather than compared. Both are reimplemented in
   `apps/accp`, as `ai-needs` is, since L2 cannot cite compiler chapters;
5. **still allowed**: `policy_sha256` names the policy the conduit holds, and
   `eval-policy` on that policy allows each covered label NOW, so a lease whose
   window has closed or whose policy has changed stops admitting without a
   revocation step. This is where the Jake policy (ACCP section 7) refuses a
   run outside 15:00 to 18:00;
6. **flow and gates**: the lease's flow rules and gates, by their own designs.
   Until a design's checks are built, a lease carrying a non-empty field of
   that kind is refused, never admitted with the field ignored;
7. **addressed to this conduit**: the lease's `audience` equals this
   instance's identity key (`describe`), so a lease issued for one conduit
   does not admit at another that trusts the same grantor;
8. **not self-granted**: `grantor` differs from `holder`, and the instance
   refuses to start with its requester key in its grantor set;
9. **bounded**: `expires - not_before` is at most the instance's configured
   maximum lifetime, and `now` is the host clock of the runner's machine.

The admission and the run happen in one request over one compile: the module
the runner executes is built from the IR whose row was admitted, and the
fact records that source's hash. A lease is a STANDING grant: inside its
window it admits every program its capability covers, as many times as the
holder asks, exactly as the Jake grant in ACCP section 7 does. Bounding use is
the quota (`AccpDecidablePolicy.md` ruling 2), and binding one effect to one
approved program is `HumanApprovalGrant.md`.

The run's fact (ACCP principle 6) carries the requester key, the lease id, the
grantor key and the policy hash, so every granted effect traces to who granted
it and under which policy.

A changed policy revokes every lease that names the old hash. A narrower
revocation (one lease, one holder) is section 7 item 3.

## 5. What this does not do

- **Key custody.** The requester key is the harness's; a harness that lets
  another process use it lets that process act as the agent. The instance
  binding at least makes the bound key visible in `describe`.
- **Quota.** Counting minutes against a quota needs state across runs, which
  the conduit does not keep; it waits on `AccpDecidablePolicy.md` ruling 2.
- **Rate and abuse limits** on a shared transport.

## 6. Proof

`codex/test/apps/accp-lease-*`, plain requests and tokens built by hand with a
test grantor key, through the conduit's admission:

- must refuse, one arm per section 4 check: an untrusted grantor; a bad
  signature; a token whose holder is another key; before `not_before`; at
  `expires`; a label outside the lease's capability; a scope outside the
  lease's; a scope portless where the lease names a port (the `scope-subsumes`
  arm); a policy hash the conduit does not hold; the Jake policy outside its
  window; another conduit's `audience`; a holder equal to the grantor; a
  lifetime over the maximum; a non-empty `flow_rules` or `gates` before their
  checks exist; an instance configured to listen on a socket (refuses to
  start); and, under request binding, a replayed nonce, a nonce the conduit
  never issued, a skewed `issued_at`, and a lease signature presented as a
  request signature;
- must admit: the Jake policy's scoped write inside its window (ACCP rung 6's
  acceptance line), and an ambient-only run with no lease, unchanged;
- each refusal arm is shown to reach its check by sabotaging that one check
  (match count printed and non-zero) and watching the arm admit (L-FALSIF).

## 7. Decisions this design asks of Damian

Item 1 ruled yes (root, 2026-11-01: it orders rungs A and B and both are built). Damian, 2026-11-01: "2 b, 3 a": the token is an extended `GrantMsg` carrying a grantor and defined signed bytes, which also gives the trust protocol grant verification; revocation is by policy-hash change.

1. **Instance binding first.** Rung 6 on the stdio MCP conduit with the
   requester and grantor keys in the launch configuration, request binding
   later. Recommended: yes.
2. **Token home.** Conduit-local token (recommended, section 3) or an extended
   `GrantMsg` carrying a grantor and defined signed bytes, which also gives the
   trust protocol the grant verification it lacks today.
3. **Revocation.** Policy-hash change only (recommended for rung 6), or also a
   revocation list the grantor signs and the conduit reloads.

## 8. Rungs

| rung | delivers | acceptance |
|---|---|---|
| A | instance binding; the lease token and its nine checks; the fact fields | section 6 corpus; the Jake policy admitted in the window and refused outside, with the policy hash in the refusal |
| B | request binding with timestamp, nonce and challenge | the replay and skew arms |
