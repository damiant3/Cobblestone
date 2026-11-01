# ACCP: Agent-Compiler Control Protocol

ACCP is the standalone computation service under `apps/accp`, registered as the
`Accp` quire. Source citations use `cites Accp chapter AccpMath` or the relevant
chapter. The app owns its conduit, numerical libraries, runtime build, host
adapter, MCP binding and focused tests.

The conduit is Codex source compiled to `Accp.cdx`. The existing console PE
writer packages that code as `Accp.exe` for Windows. The conduit owns JSON
validation, effect admission, WASM import inspection, pipeline decisions and
run-record construction. `serve.ps1` supplies byte transport, process limits,
artifact hashing and journal persistence. `HostIo.cs` binds Windows Job
Objects and suspended process creation through PowerShell's existing runtime;
there is no .NET application, NuGet package or new SDK to install.

The host requires Windows, PowerShell 7, `wasmtime` and `wat2wasm` on PATH.
Building also requires the repository's `tools/codex-vm.exe`, explicit depot
seed, compiler source and shared WASM backend source. `build-runtime.ps1`
assembles a private stdio WASM emitter, compiles that emitter to a hosted
Windows executable, then compiles and emits the compiler and lens modules.
No Prism page, page manifest, page build or pre-existing plug binary is an input.
Shared lower-level compiler, cite resolver and PE packaging helpers remain
repository toolchain dependencies.

`runtime.json` records input hashes, seed, native emitter, assembled source and
WASM artifact identities. `manifest.json` covers runtime provenance, executable
entries and source libraries. These identify the build; they do not assert a
self-host fixed point or cross-target parity. Runtime intermediates and logs
stay under the selected output directory's `runtime-build/`.

From the repository root:

```powershell
pwsh -NoProfile -File apps/accp/build.ps1 -Kernel seed/Codex.cdx
pwsh -NoProfile -File apps/accp/test.ps1 -Kernel seed/Codex.cdx
```

The build runs compile guests serially and checks free RAM before every launch.
The default rebuilds the runtime from source. `-ReuseRuntime` allows reuse only
when the source/tool fingerprint and runtime artifact hashes agree. The focused
test compiles the separate `AccpProbe.cdx` entry and grades the production
service. The test is outside the compiler battery. Build outputs and proof
logs live under `apps/accp/build-output/`.

Run a request:

```powershell
$source = @'
Chapter: Answer
Section: Entry
  opening : [Console] Nothing = act
    print-line-uni (show (6 * 7))
  end
Page 1
'@
@{ op='run'; id='answer'; source=$source } | ConvertTo-Json -Compress |
    pwsh -NoProfile -File apps/accp/serve.ps1
```

The response has `status: "ok"`, `stdout: "42\n"`, module digests, timings
and a `fact` hash. Stderr reports the journal path and resource measurements.
Keep stdin open and send multiple JSON lines to reuse the resident conduit
and the two precompiled native module images. Each compile, lens and program
execution uses a fresh Wasmtime process. EOF closes the service; Ctrl-C or
parent termination kills owned child processes through Job Objects.

For two responses from one service session, including an effect refusal:

```powershell
$requests = @(
    @{ op='run'; id='allowed'; source=$source },
    @{ op='run'; id='denied'; source=$source.Replace('[Console]', '[Console, Network.Read]') }
)
$requests | ForEach-Object { $_ | ConvertTo-Json -Compress } |
    pwsh -NoProfile -File apps/accp/serve.ps1
```

The second response is `refused` with `needs: ["Network.Read"]`.

## Wire

One UTF-8 JSON object per line, one response per complete request. The default
entry speaks the ACCP L2 protocol. `-Mcp` selects the Codex MCP entry described
below. HTTP is not implemented.

| op | fields | answer |
|---|---|---|
| `describe` | optional string `id` | module/host identities, ambient set, ceilings |
| `check` | `source`, optional `id`, `limits`, `prose` | diagnostics or entry row and `outside_ambient`; with `prose: true`, the CDX1101 to CDX1110 Codex Prose Language warnings of `We say:` blocks as `prose_findings` |
| `run` | `source`, optional `input`, `id`, `limits` | buffered stdout, exact output bytes in `stdout_base64`, output hash |
| `lens` | `source`, `plug: "wasm"`, optional `id`, `limits` | WAT text |

Sources must be complete compilation units. The optional `library` field
selects `none` (default), `math`, `physics`, `experiment`, or `exact` (integers past 64 bits and exact decimals, `AccpExact.codex` over `Foreword chapter BigInt`); physics includes
math, and experiment includes physics. The
build bundles the selected Codex chapters and their dependencies. No arbitrary
library resolver or compiler flags are exposed. The fixed request mode is
`IR-UNI decks=12`. Compile overflow/refusal remains explicit; the
service does not silently increase budgets. `opening` must be unique, take
zero parameters and return plain Integer, Text, Boolean, Char or Nothing,
optionally wrapped in a concrete effect row.

Pure computation and buffered `Console` are ambient. Directional Console
labels are supported. Other effects are reported by `check` and refused by
`run` and `lens` unless a `run` presents a lease the instance admits (Instance
binding below). The WASM
inspector independently permits only correctly typed `fd_read`/`fd_write`
imports. Execution receives finite stdin and captured stdout/stderr, no
directory, environment or network grant. Unsupported target builtins trap.

`limits` can contain `ms` (1 through 10000, default 2000) and `stdout_bytes`
(1 through 65536, default 65536). Client limits only narrow host ceilings.
The execution deadline includes compile, lens, assembly and program JIT;
bounded refusal/journal completion can finish after that deadline. Partial
frames wait for newline or EOF with bounded storage; this local stdio service
has no idle-input timer. Responses distinguish diagnostics, refusal, trap,
timeout and infrastructure error. Compiler/lens output is capped at 16 MiB;
source and input at 64 KiB each; the public frame at 256 KiB.
The source ceiling includes the selected library and caller source together.

Compiler/lens linear memory is capped at 512 MiB and program memory at 64 MiB.
Execution jobs have a 2 GiB committed-memory ceiling. The hosted Codex runtime
reserves a 3 GiB virtual arena and commits it in 64 MiB chunks as pages are
first touched, so the conduit job ceiling is 4 GiB; the reservation is not
charged against the commit limit. Each completed request
restores the conduit heap checkpoint. Stderr reports both checkpoint and
request allocation bytes, conduit RSS and execution-job peak commit.

## Instance binding

`-Requester <key> -Grantors <key,key,...> -Identity <key>` bind the instance to
one requester (`AccpRequesterAuth.md` section 2): every key is an Ed25519 public
key as 64 lowercase hex digits, the requester is the only principal on the
conduit's stdin, the grantors are the keys whose leases it will trust, and the
identity is the instance's own key, which a lease's audience must name.
`-MaxLeaseSeconds` (default 3600, at most 86400) bounds a lease's lifetime.
The service refuses to start on a malformed key, a key listed twice, a requester
inside the grantor set, or an identity equal to the requester or a grantor.
`describe` and every fact carry `binding`: the transport (`stdio`), the
requester and identity keys, `grantors_sha256` (SHA-256 of the sorted keys
joined by commas, ASCII), the grantor count and the lifetime bound; `binding`
is `null` when the instance is unbound.

A `run` whose row leaves the ambient set may carry `lease`: lowercase hex of
`encode-grant-body` (`Trust chapter GrantSeal`), a sealed `GrantMsg`. The
conduit decodes it with `decode-grant-checked`, reads the host's clock through
the `now` bridge, and admits only when `ag-refusal` (`AccpGrant.codex`) finds no
failing check of `AccpRequesterAuth.md` section 4: the instance is bound, the
grantor is trusted, the signature verifies, the grant is not self-granted, its
holder is the requester and its audience this instance's identity, its window is
current and no longer than `-MaxLeaseSeconds`, `scope.path` equals
`capability.scope`, every flow rule parses, it carries no gates or conditions, every
non-ambient label is covered (name, direction and scope, `AccpLease.codex`),
and its policy hash names the policy the instance holds, and that policy still
allows every non-ambient label now (`ag-admit`, check 5). `-Policy <file>` is the
grantor's prose (`Trust chapter PolicyProse`, at most 64 KiB) and
`-NameBook <file>` the grantor's signed book as lowercase hex
(`name-book-encode`); the conduit lowers the prose through the book, which must
be the lease grantor's, and evaluates each label with `eval-policy` at the host's
local week-minute (Monday 00:00 is 0). `binding.policy_sha256` is the SHA-256 of
the policy file's bytes, so changing the policy revokes every lease drawn
against it. A grant with a quota ("for no more than 1 hour per day") is counted by the host: `-UsageLedger <file>` is an append-only JSON-lines file of `{grantor, holder, rule, period_key, ms}`, read at start, so a restart keeps the count, and keyed by grant rather than lease, so a fresh lease does not reset it (`period_key` is the local date for `per day`, `lease:<id>` otherwise). The conduit refuses at or past the quota and refuses any quota on an instance with no ledger; the host caps the run's execution at what remains and charges its measured milliseconds after it. A grant that needs a human approval is refused until the approval check exists. A refusal names the first failing check; the response and the fact's
`grant.lease` carry the lease's id, grantor, holder and policy hash. A lease on any op but `run`, or on a run that needs no
lease, is refused.

A lease's `flow-rules` forbid flows (`AccpInformationFlow.md`, rung 0). A rule
is `SOURCE -> SINK`, each end an effect name and an optional JSON-quoted scope:
`FileSystem.Read "/patient/" -> Network.Write "api.example"`. An end with no
scope matches every scope. A rule that does not parse, or whose two ends are
both `Console`, refuses the lease at admission. An admitted lease whose row
holds both ends of a rule (names and scopes overlapping, the source label able
to read and the sink label able to write, `AccpFlow.codex`) is refused with
`stage: flow` and `flow: [{"source": ..., "sink": ...}]` naming every pair;
the fact records `flow`. Rung 1 then runs both IR flow analyses: `analysis`
is the first (`AccpFlowIr.codex`): `verdict` is `path`, `no path` or
`unanalysed: <reason>`, and `reach` names each forbidden pair a path joins
with the defs holding its sink; `analysis_second` is the second
(`AccpFlowCheck.codex`) as one text, `no path`, `path: <rule indices>` or
`unanalysed: <reason>`. When both say `no path` the run is admitted and its
answer carries both fields; any other pair keeps the `stage: flow` refusal
with both (`AccpInformationFlow.md` section 8 item 2). A rule-text refusal
and a row/scope mismatch are refused without the analyses. The fact records
both fields.

Under any flow rule, a program whose IR names a raw-memory builtin (rung 0b:
`afl-raw-memory` in `AccpFlow.codex`, every builtin that reads or writes an
address it is given or moves the allocator) is refused with `stage: flow`,
`needs: ["no raw memory under a flow rule"]` and `raw: [...]` naming each
builtin found; the fact records `raw`. An IR string that does not end refuses
as `unscannable IR`. `raw-memory-census.ps1`, run by `build.ps1`, refuses a
bundle whose compiler table holds a builtin on neither that list nor the
reviewed `raw-memory-census.json`.

## Request binding

`-RequestBinding -Grantors <key,...> -Identity <key>` (no `-Requester`; not with
`-Mcp`) binds each request instead of the instance (`AccpRequesterAuth.md`
section 2, rung B). `describe` reports `binding.transport` `request` and an
empty requester. Every `check`, `run` and `lens` then carries:

- `requester`: an Ed25519 public key, 64 lowercase hex digits; neither a grantor
  key nor the identity key;
- `nonce`: 64 lowercase hex digits;
- `issued_at`: Unix seconds as a JSON number, within 60 seconds of the host clock;
- `signature`: 128 lowercase hex digits, Ed25519 by `requester` over the SHA-256
  of the 17 bytes `codex-request-v1` and a zero, followed by the request's
  canonical encoding: the object without `signature`, keys in ascending
  code-point order at every level, no whitespace outside strings, every other
  value byte for byte as the conduit receives it (ASCII, each character above
  U+007F written `\uXXXX` in lowercase hex, which is what the host's wire makes of it).

The host refuses a nonce it has seen in the last 121 seconds (4096 at most held),
so a restart inside that window forgets them. A run whose row leaves the ambient
set must also use a nonce from `{"op":"challenge"}`, which answers
`{"status":"ok","nonce":...,"expires_in":60}` (unsigned, 256 outstanding at
most); the conduit spends it at admission and refuses one it never issued, so a
restart cannot be replayed into. Refusals are `stage: authentication` (format,
clock, key, signature, a seen nonce) or `stage: admission` (a nonce that is not
an unspent challenge). The response and the fact's `grant.requester` carry the
authenticated key, and every lease check runs against it as the holder. On any
other instance the four fields and `challenge` are refused, and `describe` and
`challenge` are never signed.

## Policy questions

`{"op":"policy","question":...}` answers a question about the held policy
(`AccpDecidablePolicy.md` rung 3) without compiling or running anything. The
instance must hold `-Policy` and `-NameBook`, and the book's grantor must be one
of `-Grantors`; the policy is lowered through that book exactly as a lease's
admission lowers it (`ag-policy-from`).

- `"question":"can-allow","label":"Network.Read"`: can any context reach the
  label, its direction read from the suffix as admission reads it;
- `"question":"widens","policy":"<prose>"`: does the proposed policy, lowered
  through the held book, allow any context the held policy refuses;
- `"question":"dead-rules"`: the held rules that decide no context, each as
  `position:name`.

An answer is `{"status":"ok","question":...,"holds":...,"witness":...,
"min_trust":...,"evaluations":...,"policy_sha256":...}` (`dead` in place of
`holds` and `witness` for dead-rules). The witness is a context `eval-policy`
allowed: `capability`, `direction`, `trust_score`, `current_time` (the policy's
week-minute), `agent_id`, `scope`; a field the policy never names holds a
stand-in such as `~unnamed-agent~`. `min_trust` is beside it because the
verifier's trust gate is not part of `eval-policy`. A question needing more
than 200,000 evaluations, an unbound instance, no held policy, an untrusted or
undecodable book, or a policy that does not lower is `status: refused`,
`stage: policy`. Under request binding the op is signed like `check`.

## Journal and recovery

The default journal is a new `accp-<session>.jsonl` in the bundle directory.
`-Journal <new-file>` selects another new file; existing files are refused.
`-JournalBytes` lowers the default 32 MiB session ceiling, down to 1 MiB.
Admission reserves 1 MiB for the next bounded record. A full or
unwritable journal prevents further execution. An append failure stops the
service and reports an audit error.

Each JSONL line contains `fact` and `record`. Recompute `fact` as SHA-256 of
the exact UTF-8 bytes of the `record` JSON object, excluding the outer wrapper
and newline. `previous` links to the preceding fact, starting with 64 zeroes.
The conduit constructs successful and refused-request records; the adapter
constructs explicitly labelled records if the conduit fails. Records retain
request/source/input/response hashes, identities, effective limits, status,
row/scopes, missing capabilities and refusal stage/reason. Invalid request
schemas have null source/input hashes because no program was admitted.
Records omit source/input text and stdout/WAT payloads. Output bytes are covered by the response hash
and, for successful execution, the output hash. The journal is flushed before
acknowledging the result. This is local evidence, not a signed trust fact.

`source_sha256` identifies caller source. `compiled_source_sha256` identifies
the combined library and caller source, or is null when assembly is refused.
Responses and records name the selected `library` and the bundled artifact
hashes in `libraries`. The adapter verifies the library manifest hashes before
serving and retains the verified source in memory for the session.

To verify `response_sha256`, retain the exact received response line, remove
its final `,"fact":"<64 hex digits>"` property and restore the closing `}`.
Hash that UTF-8 text without the line terminator. Do not parse and reserialize
the response before hashing: whitespace and property order are part of the
recorded bytes. The journal alone cannot reconstruct stdout, WAT or the full
compiler diagnostics; retain the response when those payloads are needed.

`apps/accp/verify-fact.ps1 -Journal <file> [-Response <line> | -ResponseBase64 <_meta.accp_response_base64>]` performs this recipe outside the service: every fact hashes its record, the chain links from 64 zeroes, and a retained response hashes to its record's `response_sha256`. It prints `VERIFIED` or `REFUSED:` with the first mismatch and exits 1. Run it on a closed journal; the service holds its journal open while it runs.

Normal shutdown removes the session's temporary native images and program
files. Hard parent termination kills the processes but can leave the temporary
directory named in the startup stderr line. The focused cancellation test
removes only the directory created by that test. Preserve journals separately
when deleting build outputs.

The intended deployment is Cobblestone OS/Kernel. The OS adapter must replace
the byte channels, monotonic clock, artifact execution/isolation and journal
storage without moving protocol or admission policy out of Codex. Running
untrusted programs directly in the current bare-metal address space remains
outside this implementation. Browser isolation, leases, signed descriptions
and the OS adapter remain later ACCP rungs.

## MCP and agent use

```powershell
pwsh -NoProfile -File apps/accp/build.ps1 -Kernel seed/Codex.cdx -Mcp
pwsh -NoProfile -File apps/accp/mcp-test.ps1
pwsh -NoProfile -File apps/accp/serve.ps1 -Mcp
```

`AccpMcp.cdx` owns MCP initialization, discovery, resources and calls. The
adapter uses the corresponding console executable in place of `Accp.exe`;
there is still one resident Codex process. Each configured client instance
reserves the hosted runtime's 3 GiB arena described above and commits only
the chunks it touches.

`codex_run` also takes `lease`, the same hex sealed grant the L2 wire carries (Instance binding above); a refusal is a tool error whose `reason` names the failed check. The five tools are `codex_run`, `codex_check`, `codex_lens`,
`codex_describe` and `codex_clarify`. `codex_clarify` takes English `text`
(at most 16384 characters) and an optional `register` and answers in the
conduit itself, with no compile, no execution and no journal record
(`AccpClarify.codex` over `OS chapter Clarifier`): the SOLID sentences, each
flagged word with its kind, sentence and question, referent questions
first, and `next_question`. The scan is lexical: fixed word and phrase lists per question kind (referent,
domain, cardinality, threshold, temporal, modality, connective), the first
mention of a `the X` noun phrase as a referent, except a fixed list of
referents every reader knows (`the sun`), and a Technical register
detected from code-like tokens. `it`, `its`, `they`, `them`, `their` and `theirs` are bound, not flagged,
when the sentence before (or, when that sentence holds no candidate, the one
before it) holds exactly one candidate, a determiner or pronoun, whose noun
(after any adjective) agrees in number; `he`, `him`, `his`, `she`, `her` and
`hers` are bound when that window holds exactly one capitalised name. The `/clarify` skill (`.claude/skills/clarify`) calls `codex_clarify`.
A session still opens its journal file (in the bundle directory unless
`-Journal` names another) even when it only clarifies. The language card is the resource `codex://accp/language`.
`codex://accp/science` documents the numerical library API, domains, units,
precision limitations and complete examples. Read the science card before use.
Server instructions and tool descriptions explain computation and failure
handling. Install the short standing rule in `agent-instructions.md` in the
Codex user's `~/.codex/AGENTS.md` and the Claude user's `~/.claude/CLAUDE.md`,
preserving existing instructions. The rule applies when the tools are
available and requires a computation over the supplied inputs.

Register a built, stable checkout with each harness. For Claude Code:

```powershell
claude mcp add --scope user --transport stdio cobblestone -- pwsh -NoProfile -File D:/Projects/Cobblestone-red-main/apps/accp/serve.ps1 -Mcp
```

The equivalent Codex user `config.toml` entry is:

```toml
[mcp_servers.cobblestone]
command = "C:/Program Files/PowerShell/7/pwsh.exe"
args = ["-NoProfile", "-File", "D:/Projects/Cobblestone-red-main/apps/accp/serve.ps1", "-Mcp"]
startup_timeout_sec = 30
tool_timeout_sec = 30
enabled_tools = ["codex_run", "codex_check", "codex_lens", "codex_describe", "codex_clarify"]
default_tools_approval_mode = "approve"
```

Replace the checkout path for another installation. Preserve configured
Codex execution runtimes, including `node_repl`: code-mode models need the
existing runtime to reach tools. Do not disable that server to isolate an
MCP acceptance test. Verify the new server in a fresh harness session.

MCP supports protocol versions 2024-11-05, 2025-06-18 and 2025-11-25, with
2025-11-25 offered to a client requesting an unknown version. Initialization
must complete before tool calls. Notifications receive no response and never
execute a tool. Cancellation notifications are consumed after the serial
request completes; execution deadlines and parent shutdown provide the
active-run stop bounds. No background tasks or resource subscriptions are
advertised.

Tool results include JSON text, `structuredContent`, and `isError`. Diagnostics,
refusals, traps and timeouts are tool errors. Malformed JSON-RPC, unknown methods
or tools, and invalid call envelopes use JSON-RPC errors. Execution uses the
same admission and audit path as L2. Discovery and language-card reads do not
create execution records. `_meta.accp_response_base64` preserves the exact L2
response bytes for fact/response-hash verification after MCP wrapping.

`-ProtocolLog <file>` records raw incoming and outgoing MCP frames for diagnosis,
with a 32 MiB ceiling. That optional log contains program source and results;
ordinary execution journals retain the hashes and decision metadata instead.

`harness-test.ps1 -Client codex|claude -Scenario count|recovery` launches one
fresh authenticated client, saves events/protocol/journal files under build
output, and uses an isolated temporary working directory with the standing
rule. Supply `-Executable` when the CLI is not on PATH and `-Model` only when
choosing an available model for that test. `-WithoutInstructions` provides a
control. The runner verifies actual `codex_run` client events, source and
response hashes, the journal chain and scenario results. Count requires the
supplied text and character operations in successful source; inspect that
source for input-derived work because the checker does not prove data flow.
Recovery requires the unchanged source diagnostic followed by the exact
routine-name correction and execution returning 42. Use `-VerifyDirectory
<artifact-directory>` to grade an existing capture without another model run.

## Numerical math and physics

Build with `-Mcp`, then run `pwsh -NoProfile -File
apps/accp/science-test.ps1`. The focused runner exercises the production
MCP path, mathematical domain failures and library selection. Binary64 results
are compared with analytic answers or the host math library using explicit
tolerances; journal facts retain the actual submitted computations.

Select `library: "math"` for square roots, integer powers, natural logarithms,
exponentials, trigonometry, central derivatives, Simpson integration,
bisection roots and scalar Runge-Kutta ODEs. Select `library: "physics"` for
the math API plus classical motion, forces, kinetic energy, gravity, circular
orbits, projectiles, ideal springs, waves, Ohm's law and sensible heat.

For example, call `codex_run` with `library: "physics"` and source:

```text
Chapter: Projectile
  cites Accp chapter AccpPhysics
Section: Entry
  opening : [Console] Nothing = act
    print-line-uni (math-format (physics-projectile-range 20.0 (math-pi / 4.0) 9.81))
  end
Page 1
```

The example assumes uniform gravity of 9.81 m/s^2, equal launch and landing
height, and no drag. The result is metres. Physics arguments follow documented
SI conventions; dimensional consistency is not enforced by the type system.
Physical constants remain caller inputs rather than hidden environmental facts.

Every supported calculation returns `MathResult`, either `MathValue (Real)`
or `MathError (Text)`. `math-format` displays either a value or `error:` and a
reason. A successful execution can return a mathematical error; inspect both.
Math uses binary64 approximations. Fixed-step calculus requires caller-selected
steps and convergence checks; the methods do not provide certified error bounds,
symbolic algebra, arbitrary-precision arithmetic or stiffness detection.

The library sources are `apps/accp/AccpMath.codex` and
`apps/accp/AccpPhysics.codex`. Math reuses `Gpu.DeviceMath` for square roots
and trigonometry. Only the science card's API is supported; bundled dependency
names are implementation details. Each build records `math.codex` and
`physics.codex` digests in the manifest. No seed change is required.

## Structured experiments

Select `library: "experiment"` and read `codex://accp/experiments` for checked
quantities, structured results, coupled ODEs, parameter sweeps and SVG export.
The callable API and examples are in `AccpExperimentCard.codex`; the complete
contract is [experiments.md](experiments.md).

`codex_run` accepts `output: "json"` to require one JSON object on stdout and
expose that object as `result`, preserving original stdout bytes and hashes.
Malformed output becomes an execution-response error at stage `result`.
Valid JSON with scientific status `error`, or convergence `not_converged`,
remains a completed execution; inspect the scientific fields separately.
Metadata is program-reported and does not certify a physical model or numerical
error bound. SVG export returns `PlotSvg` or `PlotError`; saving the SVG is a
caller action, and arbitrary stdout is not automatically rendered.

After building with `-Mcp`, run
`pwsh -NoProfile -File apps/accp/experiment-test.ps1` for the focused proof.
The witness records requests, responses, journal, numeric trajectory and SVG
artifacts under its reported output directory. Existing math/physics, MCP and
L2 proofs remain separate. Installed versioned bundles require `-Bundle`.
