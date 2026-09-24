# Agent-Compiler Control Protocol (ACCP)

**Current implementation:** standalone app `apps/accp`, quire `Accp`.
Section 20 owns the independent build and installed bundle contract.
Earlier proposal text and dated checkpoints remain historical records.

**Historical proposal status (2026-09-16): design, proposed. Opened at Damian's direction. No
lane owns it; it is written so an outside implementer (Steve Howell is
the candidate) can build it from this file and the files it cites.**

Companions: `docs/PM/Stories/Vision/TheLongFlight.md` (Ascent II, the
permission chain this generalises), `apps/prism/design/Active/PrismDevEnvironment.md`
(stage 4, the in-page agent loop this extends), `apps/prism/prism-backlog.md`
(PRISM-1, PRISM-5 and the essay-repl-server join this serves).

---

## 1. The idea

An agent that can state a question as a Codex program hands the program
to a warm compiler and takes the answer back. It does not reason its way
to the answer in tokens, and it does not deliberate about whether it may
compile: compiling is a tool it holds, the way it holds "write a file".

Asked how many r's are in "strawberry", the agent writes this and runs it:

```
Chapter: Strawberry

Section: Entry

  count-of : Text, Integer, Integer, Integer -> Integer
  count-of (s) (want) (i) (acc) =
   if i >= text-length s then acc
   else if char-code-at s i == want then count-of s want (i + 1) (acc + 1)
   else count-of s want (i + 1) acc

  opening : [Console] Nothing = act
    print-line-uni (show (count-of "strawberry" (char-code-at "r" 0) 0 0))
  end

Page 1
```

It prints `3`. That program was compiled and run for this document
(section 8), not written from memory.

What decides whether a program runs at once or has to ask is the effect
row on `opening`. The type checker already proves that a program's body
does nothing its row does not declare. A row inside the **ambient set**
(section 4) runs immediately. A wider row needs a grant from the trust
lattice, and that negotiation is the "control" half of the name.

Anthropic's Model Hardware Standard (research preview, 2026) gives an
agent one standard driver for physical devices, with safety limits held
in the driver rather than left to the agent's judgement. ACCP is the
same arrangement for computation: the compiler and the runner are the
driver, the effect row is the device's declared capability, and the
limits live in the type checker and the runner, never in the agent.

## 2. Principles

1. **The compiler does not change.** It stays the minimum that compiles.
   Every deployment is a layer over it. ACCP drives the compiler through
   the stdin contract the Prism page already uses: a mode line
   `IR-UNI decks=<N>` followed by the unit text and a NUL; the compiler
   answers with diagnostics and an `IR-BEGIN` ... `IR-END` block
   (`codex/plugs/wasm/page/prism.html`, `compileForAgent`, line 3146).
   There is no `-mode prism`, no network stack in the seed, and PRISM-1's
   closed route stays closed.

2. **The row of `opening` is read from the IR the compiler already
   emits.** Every def carries its type, and a concrete effect row prints
   as `(effectful (effs "Console" ...) (scopes "" ...) <ret>)`
   (`codex/compiler/Emit/IRTextEmitter.codex`, lines 263 and 749). The
   conduit parses that one form. It adds nothing to the compiler's
   output.

3. **Two instruments, and both must agree.** The row is the type-level
   bound. The runner's link surface is the second: a runner links
   exactly the host functions the grant allows and refuses a module that
   imports anything else, before instantiation. Neither is trusted
   alone, because the row does not bound raw memory: heap
   `peek-*`/`poke-*` are pure by ruling (`docs/Designs/Done/Compiler/CapabilityProbe.md`,
   the 7.3 table), and `codex/test/cap-network-denied.codex` rewrites the
   process capability table from a pure helper on bare metal. Under a
   WASM runner the same write lands in the module's own linear memory
   and touches nothing else (section 8, probe 4). **A runner that does
   not confine raw memory to the program is not an ACCP runner.**
   Bare metal is not one today.

4. **Absence restricts.** No grant, no run. A lease that expires
   mid-run stops the run. A runner that cannot supply a granted effect
   traps; it never substitutes something plausible. There is no default
   allow anywhere in the stack.

5. **A fresh instance per request, a warm module across requests.** The
   expensive step is turning the compiler module into machine code, and
   that is paid once per process. Instantiation is cheap, and a fresh
   instance per request means no request can see another's memory. This
   is the opposite of PRISM-5's sidecar, which keeps a plug instance
   alive across payloads; that is right for a plug that only transforms
   text, and wrong for a runner that executes submitted programs.

6. **Every run is a fact.** Source hash, compiler digest, row, grant,
   runner and output hash form one record, appended to the forensic
   chain (`codex/os/trust/Forensics.codex`). A refusal is recorded the
   same way, with the labels that were missing.

## 3. The layers

| layer | what it is | exists today |
|---|---|---|
| L0 compiler | `codex-compiler.wasm`, or the seed on codex-vm | yes, unchanged |
| L1 runner | compiles IR to a runnable form and runs it with a granted link surface | in-tab: the Run button (not an ACCP runner, section 5.1). Hosted: the section 8 prototype, outside the depot |
| L2 conduit | the request/response wire (section 6) over a warm L0 and L1 | no |
| L3 grant | leases and policy for rows wider than ambient | chapters exist in `codex/os/trust/`; not wired to any runner |
| L4 binding | how a harness reaches L2: an MCP server, and Prism's in-page tool loop | Prism's loop exists without a `run` tool |

Each layer consumes the one below through its published contract only.
L2 does not cite compiler chapters (PRISM-1 records why that fails:
the compiler unit is assembled by glob, so its `cites` do not resolve
from an app unit).

## 4. The ambient set

The ambient set is what a program may do with no grant. Proposed:

- the empty row (pure), and
- `Console`, bound to the request's own buffers: `Console.Read` reads the
  request's `input` field and `Console.Write` fills the response's
  `stdout`. It never reaches the host's terminal.

A program must be able to print its answer, and a program printing into
a buffer the conduit owns has no effect on the host. Everything else
(`FileSystem`, `Network`, `Time`, `Random`, `Display`, `Camera`,
`Microphone`, `Location`, `Identity`, device families) needs a grant.
Whether `State`, `Time` and `Random` also belong in the ambient set is
decision 1 in section 10.

A public conduit (a REPL anyone can reach) serves the ambient set and
nothing else. That is the answer this design gives to the join's second
item, "an answer to isolation before any submitted source is executed"
(`prism-backlog.md`, the essay-repl-server join).

## 5. Runners (L1)

### 5.1 In-tab

The in-tab run path today is the javascript lens: IR goes through
`javascript-stdio.wasm` and the emitted text runs through
`new Function('console', 'require', ...)` with a console sink and a
`require` that refuses (`prism.html`, the Run button handler, line 2678).

**That handler is not an ACCP runner, because it runs in the page's own
realm.** The page keeps the user's Claude key (`prism.claude.key`,
line 2949) and a signing key (`SIGNKEY_ITEM`, line 1601) in
`localStorage`, and `fetch` is in scope. Whether emitted code can reach
them depends on the javascript emitter never producing a reference to a
page global. That is an argument, not an instrument (principle 3).

The in-tab ACCP runner runs emitted code in a dedicated Worker built from
a Blob. A Worker has no `localStorage`. Before the program loads, the
worker deletes `fetch`, `XMLHttpRequest`, `WebSocket`, `EventSource`,
`importScripts` and `indexedDB` from its global scope. Its only channel
out is a message carrying stdout back to the page. A sandboxed iframe
with an opaque origin as the Worker's host is the stronger form and is
the implementer's call.

In-tab WASM execution of user programs is not part of this design. It
needs an assembler in the tab, and a binary WASM emitter and a Codex WAT
assembler are both ruled not built (`codex/plugs/plugs-backlog.md` 2.11,
Damian 2026-09-02: "wat2wasm is fine").

### 5.2 Hosted (the reference runner)

The pipeline:

1. L0: `codex-compiler.wasm` under a WASM engine, source in, IR out.
2. Row check against the ambient set and the grant (section 7).
3. IR through `wasm-stdio.wasm` under the same engine, WAT out.
4. `wat2wasm`, WAT to binary.
5. Run the binary in a fresh instance whose only imports are the ones
   the grant maps to.

Both page modules import exactly `wasi_snapshot_preview1.fd_write` and
`wasi_snapshot_preview1.fd_read`. The emitter adds `env.blit_framebuf`
and `env.on_key` only when a program calls them
(`codex/plugs/wasm/WasmEmitter.codex`, lines 2181-2194). Builtins with
no WASM form (`port-out-byte`, `host-socket`, and the rest of
`wat-no-such-thing`) emit `unreachable` and trap if reached. The link
surface is therefore small and fully enumerable.

The engine is wasmtime, the one `hosted-wasm-test.ps1` already requires,
and `wat2wasm` is the assembler the tree already uses. Mapping a
granted label to host functions (a pre-opened directory for
`FileSystem.Read "<scope>"`, for example) is L1 work that waits on the
emitter producing WASI calls for those builtins. Today a granted
`Network.Read` program traps (section 8, probe 5), which is principle 4
working as intended.

The hosted runner is the service anyone can run: the same two modules,
whose bytes anyone can check (section 9), under an engine they already
trust, with no dependency on this project's infrastructure.

### 5.3 Bare metal

The seed on codex-vm through `build/compile.ps1` and a test guest. It
is the reference for correctness and the arm that grades the others. It
is not an ACCP runner, for principle 3's reason, and it is cold: about
1 s per guest boot (PRISM-5, 1049 ms median, measured 2026-08-27).

## 6. The wire (L2)

JSON messages. One request, one response, and no state between requests
except the warm modules. Over stdio the framing is MCP's (section 8.2);
over HTTP each request is one POST.

**`check`**, compile only:

```
{ "op": "check", "source": "<unit text>" }
-> { "status": "ok" | "diagnostics",
     "diagnostics": ["<file>:<line>:<col>: error CDXnnnn: ..."],
     "row": ["Console"], "scopes": [""],
     "outside_ambient": [] }
```

**`run`**, compile, check the row, and run:

```
{ "op": "run", "source": "<unit text>", "input": "<stdin text>",
  "lease": "<lease id, optional>",
  "limits": { "ms": 2000, "stdout_bytes": 65536 } }
-> { "status": "ok" | "diagnostics" | "refused" | "trapped" | "timeout",
     "stdout": "3\n", "row": ["Console"],
     "needs": ["Network.Read"],
     "kernel": "<sha-256 of the compiler module>",
     "runner": "hosted-wasmtime",
     "ms": { "compile": 26, "run": 0.6, "total": 66 },
     "fact": "<hash of the run record>" }
```

`needs` is present only on `refused` and lists the row labels that
neither the ambient set nor the lease covers. A harness shows it to the
grantor. A harness never widens its own grant.

**`lens`**, transpile through a named plug: the existing
`lensForAgent` behaviour (`prism.html`), exposed over the wire.

**`describe`**, the instance states what it is:

```
{ "op": "describe" }
-> { "kernel": "<sha-256>", "lens_modules": {"wasm-stdio.wasm": "<sha-256>"},
     "runner": "hosted-wasmtime", "engine": "<name and version>",
     "ambient": ["Console"], "evidence": "parity",
     "identity": "<public key>", "signature": "<over the above>" }
```

`limits.ms` is enforced by the engine (epoch interruption or fuel in
wasmtime; a terminate timer on a Worker), and `stdout_bytes` by the
`fd_write` binding. A request that exceeds either answers `timeout` or
`trapped` and names the limit.

## 7. Grants (L3)

For a row wider than ambient, the request carries a lease, and the
conduit admits the run only if all of these hold:

- the lease is unexpired and its holder is the requester's identity;
- every row label outside the ambient set is covered by a leased
  capability, by the same direction rule the type checker uses
  (`FileSystem` covers `FileSystem.Read`, never the reverse:
  `effect-covered-by`, `codex/compiler/Types/TypeChecker.codex`);
- every label's scope sits inside the leased scope. Scopes narrow and
  never widen (CDX4002's rule, `docs/Designs/Done/Language/CAPABILITY-REFINEMENT.md`);
- the policy that issued the lease still allows it now.

The pieces are already chapters in `codex/os/trust/`:
`AgentProtocol.codex` (`AgentIdentity`, `Capability` with name,
direction and scope), `LeaseManager.codex` (`Lease` with holder,
capability, scope, expiry and an audit flag), `PolicyEngine.codex`
(`CapabilityMatch`, `TrustAbove`, `TimeWindow`, `AgentIs`, and the
combinators), `PolicyProse.codex` (`ProseGrant`: subject, capability,
days, minutes, quota), `CapabilityAudit.codex` and `Forensics.codex`.
That is 16 chapters, 3,162 lines (measured 2026-09-16). This design
consumes them and does not re-specify them. How far each is wired and
tested end to end is not re-measured here, and the implementer measures
it before relying on it.

**The relationships are all the same shape.** A parent granting a
child's agent, an owner granting a fleet lane, a studio granting a team
access to embargoed data inside a time window, an aviation authority
granting a pilot: each is a grantor issuing a `ProseGrant` that becomes
a policy fact, and a lease drawn against it. For example:

> Jake's agent may write files under `/jake/homework/` on school days
> from 15:00 to 18:00, for no more than an hour a day.

A program with row `[Console, FileSystem.Write "/jake/homework/"]` runs
inside that window. A program with `FileSystem.Write "/"` is refused with
`needs: ["FileSystem.Write \"/\""]`, and when Jake asks why, the answer
cites the policy by hash. Outside the window, the lease does not issue.
The conduit neither knows nor cares that the grantor is a parent.

## 8. What was measured (2026-09-16)

Scratch prototype, not in the depot: a Python host using wasmtime 48.0.0
and wabt 1.0.34 `wat2wasm`, on a 2-core Linux sandbox. The modules were
lifted from the deployed `apps/landing/web/compile/prism.html` embed
block, the way `page-claude-arm.js` lifts them:

- `codex-compiler.wasm`, 1,248,658 bytes, SHA-256 `ddca31bd311d46f4...`
- `wasm-stdio.wasm`, 204,798 bytes, SHA-256 `06f5c6d37790ae18...`

The host linked only `fd_read` and `fd_write` over in-memory buffers,
refused any other import, took the row off `(def "opening" ...)` in the
IR, and ran each program in a fresh instance.

| probe | result |
|---|---|
| 1. strawberry, above | `3`. Row `["Console"]`. |
| 2. same program, text "raspberry refrigerator" | `7` (correct). |
| 3. `[Console, Network.Read]`, no lease | `refused`, `needs: ["Network.Read"]`, never assembled |
| 4. a helper typed pure that calls `net-status` | the compiler refuses: `CDX2031: Effect 'Network.Read' not declared` |
| 5. probe 3 run with `Network.Read` granted | `trapped` on `unreachable`: the WASM runner has no network surface, so the grant cannot be honoured and nothing plausible stands in |
| 6. `poke-byte`/`peek-byte` at address 20536 (the bare-metal capability table) in a `[Console]` program | `42`, written into the module's own linear memory. No host effect. |

Timings on the same process: the compiler module's one-time machine-code
compile, 1.38 to 1.49 s; each request after that, compile + row check +
lens + `wat2wasm` + run, **63 to 73 ms total, of which the program ran
for 0.5 to 0.7 ms** (four warm runs). `wat2wasm` ran as a subprocess
each time and is part of that total.

Probes 4 and 6 are principle 3: probe 4 is the row doing its job, and
probe 6 is the case the row does not cover, which only the runner's
confinement makes harmless.

### 8.1 What the measurement does not show

- The compiler module was built by whatever seed deployed the page on
  2026-09-08. The digest above identifies it. It is not a claim about
  the current seed.
- Nothing here ran on the Windows box, under codex-vm, or through
  `hosted-wasm-test.ps1`.
- No grant path ran. Probe 5 shows the refusal half of a grant only.

### 8.2 The harness binding (L4)

Two bindings, one wire:

- **MCP server.** A stdio JSON-RPC server exposing `codex_run`,
  `codex_check`, `codex_lens` and `codex_describe`, over one warm L2
  process. Any MCP-capable harness mounts it once, and the tools sit
  beside its file and shell tools. `codex/foreword/encode/Json.codex`
  exists, so the server can be a hosted Codex entry in the shape of
  `apps/prism/PrismHosted.codex` (`[Console]`, stdin to stdout). A shim
  in another language is an acceptable first host, since L4 is a
  deployment layer (decision 2).
- **Prism.** A `run` tool added beside `list_files`, `read_file`,
  `write_file`, `compile`, `read_diagnostics` and `run_lens`
  (`prism.html`, line 3113), behind the same provider interface and
  executing through the 5.1 runner.

**The tool description is what makes it a reflex.** It tells the model
when to reach for the tool: any question whose answer is computed
(counting, arithmetic, dates, parsing, sorting, checking a claim about
a piece of text). It tells the model to write the program rather than
reason toward the answer. No model has Codex in its training data, so
the server also ships a one-page language card as an MCP resource: the
chapter/section/page frame, `opening : [Console] Nothing = act ... end`,
`print-line-uni`, `show`, `if`/`else`, recursion instead of loops, and
`char-code-at` for characters. The CDX diagnostics, which carry a
location and a suggested fix (Virtue 4), close the loop from there. How
many turns a model needs to converge is the first number the binding
measures.

## 9. Evidence levels (`describe`)

An instance states how strongly its compiler module is tied to the
published seed. Strongest first:

1. **Reproducible.** The module's SHA-256 equals the result of building
   it from the depot seed and depot source with the pinned `wat2wasm`.
   Anyone can re-derive it. `wat2wasm` sits in that derivation as bytes
   we did not produce (Track D).
2. **Differential.** On a stated corpus, the module's IR output is
   byte-identical to the seed's IR output for the same input. With the
   compiler's own source as the corpus, this is self-compile agreement
   between the two builds.
3. **Parity.** The target passes the same battery as the hosted x86-64
   lift (`wasm 53 = hosted linux 53`, 2026-09-01, `CurrentPlan.md`).

The measured level for the WASM target today is parity. The WASM
compiler compiling itself (Damian, 2026-09-16) is the precondition for
the self-compile form of level 2, and nobody has measured the
byte-identity yet. A policy can require a level: ambient-only runs on
any level, and `FileSystem.Write` only on a level-1 instance, for
example. That is how an instance that somebody else runs earns a grant:
it proves what it is, and the grantor's policy decides whether that is
enough. The instance signs its `describe` with its `AgentIdentity` key,
and the lattice decides whether to trust that key.

## 10. Decisions this design asks of Damian

1. **The ambient set.** Proposed: pure and `Console` on request buffers.
   Open: `State` (if it is process-local), `Time` and `Random`, each of
   which makes a run non-reproducible, which matters to the run fact.
2. **The first L4 host's language.** A non-Codex MCP shim first, then a
   hosted Codex entry, is the proposal. L4 is a layer, and the
   hosted-Codex form waits on nothing but the work.
3. **Public instances.** Proposed: ambient only, with per-request limits,
   and `describe` published.

## 11. Rungs

Each rung ends in something runnable and checkable. The implementer
takes them in order.

| rung | work | acceptance |
|---|---|---|
| 1 | Hosted runner on the box: section 5.2 under wasmtime and `wat2wasm`, driven by a script that reads the row | Section 8's six probes reproduce, with the module digests named. Control: removing the row check lets probe 3 through to the engine, where it traps rather than succeeding |
| 2 | L2 over stdio: `check`, `run`, `describe`, warm modules, fresh instances, `limits` | A 10,000-iteration loop program answers `timeout` at `limits.ms`; an `fd_write` flood answers at `stdout_bytes`; two concurrent requests cannot see each other's output |
| 3 | L4 MCP binding and the language card | A Claude session with the server mounted answers the strawberry question through `codex_run`, recorded as a transcript; the turn count to a clean compile is logged |
| 4 | Prism `run` tool on the 5.1 Worker runner | The agent arm gains a run section. Its sabotage: a program whose emitted JS reads `localStorage` gets `undefined`, and the same program on the old Run handler does not |
| 5 | Evidence level 1 for the page modules | A script rebuilds both modules from depot seed and source and prints digests equal to the deployed ones, or names the step that differs |
| 6 | L3: lease check for non-ambient rows, using `codex/os/trust/` | The Jake policy (section 7) admits the scoped write in the window and refuses it outside, with the policy hash in the refusal. The first granted effect needs rung 1's runner to map it to a WASI call |

## 12. What this design does not do

- It does not change the compiler, add a compiler mode, or put
  networking in the seed.
- It does not build a binary WASM emitter or a Codex WAT assembler
  (ruled 2026-09-02).
- It does not run agent-authored programs on bare metal (principle 3).
- It does not replace Prism stage 4. It extends that stage's tool set
  and puts one wire under it.
- It does not decide how a grantor is authenticated to the lattice.
  That belongs to `codex/os/trust/` and the Identity design.

## 13. Hosted implementation amendment (2026-09-19)

Damian authorized red to implement the first hosted service after review.
This amendment takes precedence over conflicting proposals above for the
first implementation. Earlier sections remain unchanged by request.

### 13.1 Deliverable and boundaries

Combine rungs 1 and 2 with Codex chapters in `apps/prism/` and host scripts
in `apps/prism/accp/`. Build the conduit with the depot seed to a hosted
Windows `.cdx`; the existing console PE writer packages the same code as
a Windows stdio executable. Damian requires Codex ownership and elimination
of external dependencies wherever possible. No .NET service or NuGet SDK
is introduced. PowerShell supplies process/pipe/file supervision using the
installed Wasmtime CLI. The installed `wat2wasm` remains
the assembler, invoked with `--enable-tail-call`. No compiler, emitter or
seed change is required. Extract the compiler and WASM lens bytes from the
tracked deployed Prism page; name their full SHA-256 digests. Extraction
parses the embed data without evaluating page JavaScript. The module pair
is an identified deployment artifact, not a claim of current-seed parity
or reproducibility.

The service exposes `check`, `run`, `lens` (WASM only), and `describe`.
The ambient authority is pure computation plus `Console`, restricted to
request buffers. `Console.Read` and `Console.Write` are covered by
`Console`; other effects are refused before assembly. A supplied lease is
refused as unsupported, never ignored. State, Time and Random are outside
ambient. MCP discovery/tool mounting, public HTTP, browser execution,
signed identity and lease enforcement remain later rungs.

The Codex conduit owns request parsing, validation, effect admission,
pipeline decisions, response construction and execution-record construction.
The host adapter supplies bounded child execution, byte transport, hashing
of external artifacts and journal persistence. The adapter never approves
an effect row or accepts a caller-selected executable or shell command.
The conduit neither cites compiler chapters nor changes the compiler unit.
Sources are complete units. Library resolution and caller-selected compiler
flags are absent; the mode is fixed to `IR-UNI decks=100`.

The destination is hosting inside Cobblestone OS/Kernel. Keep the runner
interface independent of Windows: compile a unit, emit a target, inspect
and execute an admitted artifact, read a monotonic clock, hash boundary
bytes, and append an execution record. Windows process handles, paths,
Job Objects and Wasmtime command lines remain adapter details. An OS runner
must provide real memory and capability confinement before receiving
untrusted programs. Replacing the adapter must not move request parsing,
admission policy or the public record/wire contracts out of Codex.

### 13.2 Isolation, lifetime and budgets

Compile the two trusted modules once per service session with the installed
`wasmtime compile`, retaining the resulting native images in the session's
private directory. Subsequent invocations use those exact images with
`--allow-precompiled`; no submitted native image is accepted. Bind cached
images to the originating module digest, engine version and build options.
The Codex conduit stays resident across requests. Each Wasmtime invocation
creates a fresh process, Store and Instance, and releases all three on exit.
This replaces the same-process embedding proposal with reusable compiled
code and a smaller dependency surface. Measure warm request latency;
process startup remains a cost and is not described as free. Submitted
programs are compiled and discarded per request, with no growing code cache.

The supervisor owns the deadline and Windows Job Objects with kill-on-close
and committed-memory ceilings covering child execution. Assign each child
to its job before allowing submitted work. On deadline or cancellation,
terminate the owned process tree. The next request starts clean execution
processes and retains the trusted native images. Module initialization has
its own bounded startup deadline. The service executes requests serially;
input backpressure replaces
an unbounded request queue. No request shares a Store or output buffer.

The resident Codex conduit checkpoints its heap before each request and
restores that checkpoint only after the journal acknowledgement and the
response have left the request's lifetime. Retain only bounded startup
metadata and the preceding record hash outside that checkpoint. Include
conduit heap high-water in the repeated-request proof, independently of
child process exit and WASM Store disposal.

Initial host ceilings, advertised by `describe`:

| resource | ceiling |
|---|---|
| UTF-8 JSON frame | 256 KiB |
| source and input | 64 KiB each |
| request deadline, including compile, lens, assembly and program JIT | 10,000 ms; default 2,000 ms |
| worker startup | 30,000 ms |
| stdout | 64 KiB; requested lower limits allowed |
| compiler/lens output and WAT/binary intermediates | 16 MiB each |
| compiler/lens linear memory | 512 MiB per Store |
| submitted program linear memory | 64 MiB |
| execution process tree committed memory | 2 GiB |
| WASM stack | 16 MiB |
| simultaneous requests | 1 |
| journal | 32 MiB per service session; refuse further execution when full |

Client limits can only narrow the host ceilings. Reject zero, negative,
oversized and malformed limits. Bounds apply to `check` and `lens` as well
as `run`. WASM epoch interruption supplements the outer deadline; fuel is
not a millisecond clock. The Codex binary inspector permits only the exact
`fd_read`/`fd_write` function imports. The CLI's WASI implementation checks
guest pointers and lengths; the adapter does not claim to replace those
callbacks. Redirect fd 0 to finite request bytes and fd 1/2 to bounded
captures, never the service terminal or protocol stdout. Drain pipes with
bounded chunks and terminate on the combined output ceiling. No preopened
directory, environment, network grant or additional guest argument is
supplied. Other WASI operations are unreachable because their imports are
refused before execution. Process memory/deadline bounds also cover work
inside WASI callbacks; the service does not claim per-callback fuel bounds.

### 13.3 Admission and wire

Use newline-delimited UTF-8 JSON objects, one response per request, with
an optional string `id` echoed in the response. This is the L2 wire, not
an MCP server. `op` is required. `check`, `run` and `lens` require `source`;
`run` accepts `input`; `lens` accepts only `plug: "wasm"`. `limits` accepts
`ms` and `stdout_bytes`. Reject unknown fields and malformed JSON, duplicate
properties, embedded source NUL, and unsupported operation values. A frame
over the ceiling is drained through its newline and refused without
retaining the frame. EOF terminates the service after the accepted frame;
Ctrl-C terminates active work. Stdout carries protocol frames only.

Compiler diagnostics are returned as diagnostics, not parsed as IR.
Require exactly one complete IR-BEGIN/IR-END block and parse the IR
structurally, including quoted strings and escapes. Locate `opening` only
inside the chapter's top-level `defs`. Require exactly one zero-parameter
definition with a supported concrete return type. The initial supported
returns are Integer, Boolean, Text, Char and Nothing, either pure or inside
one concrete effectful wrapper. Reject other types, function types,
quantifiers, row tails, malformed effect/scope lists and duplicate entries.
Never infer purity from an absent or unrecognized type. `check` can report a
valid nonambient row; `run` and `lens` refuse that row.

Admission compares each effect/scope pair against ambient authority using
directional coverage. Console scopes must be empty or the appropriate
stdin/stdout/stderr channel. Bind imports against ambient authority, not
the literal row: the emitter always imports both console functions and
prints pure return values through its generated entry wrapper. Inspect
imports before instantiation and refuse any additional import or wrong
signature. The row checker and import checker remain separate controls.

Responses carry `status`, diagnostics or a named failure stage/reason,
row/scopes when available, module digests, runner identity and timings.
Statuses include `ok`, `diagnostics`, `refused`, `trapped`, `timeout`, and
`error` for infrastructure or audit failure. `needs` belongs only to an
effect refusal. Preserve output bytes as base64 plus decoded UTF-8 `stdout`;
the byte hash uses the original bytes. `lens` returns bounded WAT text.
No failure is represented as an empty successful answer.

### 13.4 Facts and evidence

The conduit constructs records and the adapter writes a new, exclusively
owned JSONL journal for each service session. Each record includes the previous record hash, request
hash, source/input hashes, compiler/lens hashes, host/assembler identity,
fixed compiler mode, effective limits and complete response before its
`fact` field. Hash the exact UTF-8 serialized record bytes with SHA-256;
store those bytes and the hash, then return the hash as `fact`. Flush each
record before acknowledging the response. A full or unwritable journal
prevents further execution; an append failure terminates serving with an
explicit audit error. The journal retains hashes, not source or input text.

This journal is local execution evidence. It is not a signed trust fact or
a durable distributed forensic store. `Forensics.codex` does not supply
the journal implementation. `describe` reports artifact identification and
the actual engine version; no parity, reproducibility or remote attestation
claim is inferred from a digest or from an older target-wide battery.

### 13.5 Acceptance and landing

The focused runner grades the production host and the deployed module
pair serially. It prints full module digests, measured warm latency and
process memory before/after repeated requests. Required cases:

- Strawberry returns 3; raspberry refrigerator returns 7; pure and
  directional Console entries work; supplied stdin is request-local.
- Undeclared network use produces CDX2031; declared nonambient use is
  refused before assembly; memory writes stay within program linear memory.
- A test-only admission bypass reaches the otherwise refused network
  program and fails at the target rather than yielding a plausible answer.
  The production wire exposes no bypass or grant override.
- Missing/duplicate/parameterized entries, unsupported types, malformed IR,
  forged entry text inside strings and wrong import signatures are refused.
- A nonterminating computation times out; a completing control succeeds.
  Output flood, allocation exhaustion, intermediate-output ceilings and
  oversized input fail with the named bound. A following request succeeds.
- Interleaved client submissions produce separate responses and outputs;
  repeated requests dispose Stores; cancellation and shutdown leave no owned
  conduit or execution child. An assembler stall exercises the outer deadline.
- Journal hashes recompute, request input changes alter the recorded hash,
  and a full/unwritable journal prevents execution. Module tampering fails
  digest validation. Startup failure is explicit.

Keep these checks outside the compiler battery. No fixed point, seed BVT
or build token is required because this host consumes existing artifacts.
Protect the numbered CL through the documented proof isolation procedure,
run the focused checks, inspect the final diff and copy only the host paths
to main. The design amendment lands directly through red's MAIN client.
An independent reader checks this amendment and the eventual run commands.

### 13.6 Deferred corrections identified by review

Before rung 4, replace the Worker-global deletion proposal with an enforced
browser isolation contract and tests for network, storage, nested workers
and message forgery, not only absent localStorage. Before rung 6, preserve
subject, scope and quota during policy lowering and bind leases to an
authenticated requester; enforce runtime scope, expiry and revocation.
`PolicyProse.codex` currently lowers capability and time but drops subject
and quota, and `PolicyEngine.codex` renders scoped outcomes as reason text.
Reproducibility and behavioral parity are separate evidence dimensions;
a signed description alone cannot attest which module a remote host ran.

## 14. Implemented hosted contract (2026-09-19)

This amendment supersedes the corresponding implementation details in
section 13. Earlier text remains intact under Damian's append-only direction.
The runnable interface and exact commands live in
`apps/prism/accp/README.md`; the focused proof is
`apps/prism/accp/test.ps1`.

### 14.1 Codex ownership and the OS destination

`Accp.codex` is the entry. `AccpProtocol.codex`, `AccpJson.codex`,
`AccpIr.codex` and `AccpWasm.codex` own the request grammar, pipeline,
entry/effect admission, binary import inspection and execution-record
construction. The depot compiler builds `Accp.cdx`; the existing console
PE writer packages the same code for the Windows stdio process.

`serve.ps1` adapts byte channels, clock, hashes, journal storage and the
installed Wasmtime/wat2wasm executables. `HostIo.cs` supplies Windows Job
Objects, bounded pipe capture and suspended process creation through
PowerShell's existing runtime. There is no C# application or NuGet SDK.
Children enter their memory-limited, kill-on-close jobs before resuming;
only their three redirected pipe handles are inherited. Precompiled native
images of the trusted compiler/lens are reused, with their digests checked
before execution. Each invocation creates a fresh process and Store.

Cobblestone OS/Kernel is the intended host. The Codex protocol and admission
chapters stay in Codex when the adapter is replaced. The OS adapter must
supply execution confinement, byte channels, a monotonic clock, boundary
hashes and journal persistence. The current bare-metal address space is not
used to execute submitted programs.

### 14.2 Effective limits and lifecycle

The compiler mode is `IR-UNI decks=12`, matching the deployed page's first
deck setting. The conduit does not expose compiler flags or automatically
increase decks. A compile refusal remains explicit. Compiler/lens linear
memory stays at 512 MiB; submitted program linear memory stays at 64 MiB.
Execution Job Objects cap committed memory at 2 GiB. The hosted Codex
runtime reserves/commits a 3 GiB virtual arena, so the conduit's Job Object
ceiling is 4 GiB. That reservation is distinct from measured physical RSS.

The request execution deadline includes compiler, lens, assembler and JIT
work. Refusal formatting and journal acknowledgement have bounded completion
time after execution stops. Partial stdio frames use bounded storage and
wait for newline or EOF; this local binding has no idle-input timer.
Request-local heap is restored after the record acknowledgement. The proof
checks the absolute heap checkpoint across requests, as well as allocation
delta and conduit RSS, so constant per-request allocation cannot conceal
cumulative retention.

Normal shutdown removes the session's temporary artifacts. Hard parent
termination closes Job Objects and kills owned children; the temporary
directory named in startup stderr can remain. The cancellation proof removes
only its own recorded directory. Journal retention is separate from temporary
artifact cleanup.

### 14.3 Execution evidence

The journal stores the exact record JSON and its SHA-256. A record contains
the preceding hash, request/source/input/response hashes, module/conduit/host
identities, effective limits, status, effect row/scopes, missing capabilities,
refusal stage/reason and the captured output hash when execution occurred.
Invalid request schemas carry null source/input hashes. Normal records are
constructed by Codex; conduit failures produce explicitly labelled adapter
failure records. Captured output preceding a runtime trap is returned with
the failure and hashed.

Stdout, WAT and complete compiler diagnostics are retained in the response,
not duplicated in the journal. `response_sha256` hashes the exact response
UTF-8 bytes before the final `fact` property and without a line terminator.
Preserve that received representation for verification; parsing and
reserializing JSON is not the same byte sequence. The README gives the
verification boundary. `fact` hashes only the exact record object, excluding
the outer journal wrapper and newline.

The default journal ceiling remains 32 MiB. `-JournalBytes` can narrow the
ceiling to at least 1 MiB; admission reserves 1 MiB for the next record.
A full or unwritable journal prevents further execution, and append failure
stops serving with an audit error. `describe` claims artifact identification
only. No signed identity, parity, reproducibility or remote attestation is
inferred from these hashes.

### 14.4 Acceptance boundary

The hosted implementation supplies `check`, `run`, WASM `lens`, and
`describe` over local newline-delimited JSON. The focused proof covers the
six original probes, independent row/import refusal controls, malformed
entries and requests, timeout after the program reaches its body, output
and memory bounds, recovery, interleaved requests, absolute heap restoration,
journal/response hashes, audit failure, artifact tampering and cancellation.
Compiler seed behavior is unchanged by the host implementation.

MCP binding, public HTTP, the Prism browser runner, lease enforcement and
the Cobblestone OS runner remain later work. PRISM-1/PRISM-5 and the
essay-repl-server join are not closed by this local service: their consumers
still need integration with the new conduit.

## 15. MCP binding checkpoint (2026-09-19, red)

Rung 3 is implemented on red but not landed or persistently installed.
Pending/shelved CL **25842**, ten files, owns the implementation and current
run instructions. No seed change, build token or active process belongs to
this checkpoint. The lane's workplan is empty; this section and CurrentPlan's
red row are the resume entry points. The handoff was triggered by measured
Codex app-server context, 70 percent of an 828400-token effective window,
with valid compaction telemetry at 2026-09-19T15:15:23Z.

### 15.1 Files and behavior

The new `apps/prism/AccpMcp.codex`, `AccpMcpProtocol.codex` and
`AccpMcpCard.codex` implement initialization, four tools, a language-card
resource and JSON-RPC errors in Codex. `AccpProtocol.codex` factors evaluation
from response publication so the existing admission/audit path can return
its receipt to the MCP entry. `apps/prism/accp/serve.ps1 -Mcp` runs the MCP
executable instead of the L2 executable, with one resident conduit and the
same warm module images. It returns structured tool results, JSON text and
the exact L2 bytes in `_meta.accp_response_base64`.

The other shelf files are `apps/prism/accp/build.ps1`, `README.md`,
`mcp-test.ps1`, `harness-test.ps1` and `agent-instructions.md`. The build's
`-Mcp` switch builds both entries. The README explains mounting and the
standing rule. Optional `serve.ps1 -ProtocolLog <file>` captures MCP frames
with a 32 MiB cap; that diagnostic log contains source and output.

Initialization supports 2024-11-05, 2025-06-18 and 2025-11-25. Tool-call
notifications never execute, ordinary notifications are silent, unknown
methods/tools are JSON-RPC errors, and execution diagnostics/refusals are
tool results with `isError`. Cancellation notifications are consumed after
the serial request; the execution deadline and parent shutdown are the active
stop mechanisms. Public HTTP, browser execution and leases remain later work.

### 15.2 Proof already obtained

The build used depot seed
`BD66718CBE24F589E8AD8E9D7AAC47289081B00A8824ECD22DBDE77321279541`.
The latest built MCP CDX digest is
`a4adb68bc7d0c65cc26235943df0aecec2e3389977f869611421fbba75336c03`.
The compiler/lens module digests remain the identified section-14 artifacts.

- MCP wire proof: **40 pass, 0 fail**. Artifact directory under
  `apps/prism/accp/build-output/`:
  `mcp-test-e613c057ae5d45ec90ef869d7a855936`. It covers discovery, schemas,
  language resource, real compute/check/lens/describe, compiler diagnostic
  recovery, effect denial, grant-parameter rejection, malformed envelopes,
  notification nonexecution, repeated requests and heap restoration.
- Fresh Codex CLI 0.153.4, configured model GPT-5.6-Terra, counted from the
  supplied text through `codex_run`, output `3`, fact
  `8a94662db381945dadff24b02001719270908c50455663537429f0acb9cae101`.
  Artifact directory: `accp-harness-671a1aa585704c24972348e2951cb289`.
- Fresh Claude Code 2.1.270 with explicit `-Model sonnet` read the resource
  and executed the counting program, output `3`, fact
  `bd4446d8239f2ec09536021fff9bba1d1399d18507cd4e9ee70c6c8e5ee6b4b3`.
  Artifact directory: `accp-harness-e2e49a840df644789625e0dcdb54b044`.
  The configured Claude default was not changed.

Each harness artifact directory contains `events.jsonl`, `protocol.jsonl`,
`journal.jsonl`, `prompt.txt`, `mcp.json` and `exit.txt`. These are local proof
artifacts, not depot files. Check actual tool calls and input-derived source;
a correct answer or exit zero alone is insufficient.

Earlier Codex no-call runs were invalid acceptance controls: the runner had
disabled configured `node_repl`, which code-mode models need to reach tools.
The runner now preserves existing runtimes. Do not interpret those early
runs as evidence that MCP discovery failed or that metadata alone was
insufficient. The server trace showed successful discovery. Earlier build
CDX1070 and a test journal-sharing error were corrected before the wire pass.

### 15.3 Remaining work and exact next action

**First, inspect opened files and CL 25842.** Work is left open and shelved
in `D:/Projects/Cobblestone-red`, client `BigWhite_Codex_red`. If resuming in a
clean lane, unshelve 25842 into 25842, sync, resolve and inspect by the normal
procedure. Do not overwrite the existing open copy with an older shelf.

**Next, run the two missing fresh-session recovery cases, serially:**

```powershell
pwsh -NoProfile -File apps/prism/accp/harness-test.ps1 -Client codex -Scenario recovery -Executable C:/Users/Damian/AppData/Local/OpenAI/Codex/bin/7ac07f4ce733f89a/codex.exe
pwsh -NoProfile -File apps/prism/accp/harness-test.ps1 -Client claude -Scenario recovery -Model sonnet -Executable C:/Users/Damian/.local/bin/claude.exe
```

The Codex executable can move on update; discover the active installation
before reusing that path. Require a call with the original misspelled source,
CDX3002, then a corrected execution returning 42. Add a mechanical event/
journal checker rather than treating harness exit zero as acceptance.

An independent reader validated this checkpoint, the language card,
standing instruction and runbook, including the on-disk counting evidence.
Then complete the isolated final build with `-Mcp`, the existing L2 regression suite
(`test.ps1`, previously 80 checks), and `mcp-test.ps1` on the final bytes.
**That L2 regression suite has NOT been rerun after the MCP changes.**
The full compiler battery, seed fixed point and BVT are NOT RUN and are not
required for this app-only change. Heap/time: new envelope work is linear in
bounded payload size; request checkpoint restoration passed the MCP suite.
Each resident conduit still reserves the hosted 3 GiB arena; count that
commit per client separately from RSS.

Persistently registering either client is **NOT DONE**. No Codex MCP config,
Claude MCP config or global agent instruction file was modified. At this
checkpoint Codex's `~/.codex/AGENTS.md` is absent and Claude's global
`~/.claude/CLAUDE.md` is empty. Install the standing rule while preserving
other content and register only the four bounded tools. Use the stable MAIN
checkout's `serve.ps1 -Mcp` after code landing, with 30-second startup/tool
timeouts where supported. Preserve other MCP servers, especially `node_repl`.
Do not change either client's default model as part of registration.

Finally submit/copy only the app paths, install the proven bundle in the
stable checkout, verify both client registrations, update this checkpoint
by a further append-only amendment, and report the actual submitted CLs.
The earlier L2 code is main25839; its append-only design CLs are 25829/25840.

## 16. MCP binding installed (2026-09-19, red)

Rung 3 is complete for the local Windows stdio binding. Source landed as
red **25849** and main **25851**, ten app files. Section 15's pending shelf,
unrun regression/recovery and uninstalled-client statements are superseded by
this amendment. The earlier design text remains unchanged at Damian's request.
The compiler seed is unchanged; the final build printed
`kernel: seed/Codex.cdx [BD66718CBE24F589]` for both entries.

### 16.1 Installed contract

Both user-level client registrations launch
`D:/Projects/Cobblestone-red-main/apps/prism/accp/serve.ps1 -Mcp`.
The stable checkout contains the proven six bundle artifacts and manifest,
copied byte-for-byte from the isolated build. The MCP CDX digest remains
`a4adb68bc7d0c65cc26235943df0aecec2e3389977f869611421fbba75336c03`.
Source/build instructions live in `apps/prism/accp/README.md`.

Codex exposes exactly `codex_run`, `codex_check`, `codex_lens` and
`codex_describe`, with 30-second startup/tool timeouts and server-scoped
approval. Claude Code has user-level allow rules for those four tool names.
The standing computation rule is installed in the user's `.codex/AGENTS.md`
and `.claude/CLAUDE.md`. Existing configuration and model defaults were
preserved, including Codex's `node_repl`. New client sessions load the binding;
an already-running conversation does not acquire tools from a file edit.

The client writes complete Codex units, obtains syntax from
`codex://accp/language`, inspects status and repairs compiler diagnostics.
Input-derived computation and a successful tool response constitute evidence;
printing a preselected answer does not. Facts remain local hash-linked audit
records, not signed attestations. Tool visibility and standing instructions
guide model behavior; the binding cannot guarantee every future model chooses
the tool for every applicable prompt.

### 16.2 Final proof

Proof captures remain in `D:/Projects/Cobblestone-red`, the implementation
workspace. All relative proof/log paths in this section use that root.
The installed runtime and default journals live in the separate stable
`D:/Projects/Cobblestone-red-main` checkout.

The final source was shelved, coverage-checked, reverted, synchronized,
restored and checked against its saved file hashes before the build. Main's
ten landed files match those source hashes. Final local proof logs are under
`build-output/accp/`: `final-build.*.log`, `final-l2.*.log` and
`final-mcp.*.log`.

- L2 regression: **80 pass, 0 fail**, exit 0. Artifact directory under
  `apps/prism/accp/build-output/`: `test-27cdd269f364464abc180de64e62d616`.
- MCP protocol: **40 pass, 0 fail**, exit 0. Artifact directory:
  `mcp-test-010d752c10ad4eb99f3d8afb9c525f5c`.
- Fresh Codex and Claude Code counting captures from section 15 both pass
  the final capture verifier. Both sources iterate over supplied text.
- Fresh diagnostic recovery passes for Codex/Terra in
  `accp-harness-eb039701278e4ef698735062d8e732f4` and Claude Code/Sonnet in
  `accp-harness-e6576723d20f488f91575bd05fb9ba72`. Each submitted the original
  `print-lien-uni` source, received CDX3002, changed only the routine name and
  executed the corrected program with stdout `42`.

`harness-test.ps1` now grades captured client tool events, source hashes,
response hashes and journal chains. Structured results must equal the recorded
response bytes before scenario grading. Negative controls reject a missing
client event and an altered structured stdout with intact recorded bytes.
The count check requires the supplied text and character operations; source
inspection remains necessary because that check does not prove data flow.
Independent reader validation passed after identifying and closing the
structured-result evidence gap. A PowerShell interpolation parse error in
the evolving test runner was corrected before the final recovery run.

After persistent installation, fresh clients ran from empty temporary
directories with no injected instructions, MCP configuration or tool allow
overrides. Both read the language resource and counted lowercase `r` in
`raspberry refrigerator`, returning **7**. Both client exits were zero;
source/response/fact hashes match journals in the stable bundle directory.
The installed-client captures live under `build-output/accp/`:

| Client | Capture directory | Fact |
|---|---|---|
| Codex 0.153.4, configured GPT-5.6-Terra | `accp-installed-codex-4b7423b14bfa4bcf80623e9068c57f18` | `f96f87f1ad4dd62e89427daa6f366935aa36a43840876f4d147cf3766c464332` |
| Claude Code 2.1.270, explicit Sonnet test | `accp-installed-claude-d72e232284544b53badd62a688127f64` | `f676a1dd014a1a00c13cb7ec337ac5c7e49b1691cb3f13367325f6efabd5c2af` |

The configured Claude default required credits during an earlier test;
Sonnet was selected for acceptance without changing the user's default.
The full compiler battery, fixed point and BVT were not run for this app-only
change. No seed installation or public release is claimed.

### 16.3 Cost and remaining platform work

New MCP envelope work is linear in bounded payload size. Every request restores
the Codex heap checkpoint. Final L2 measurements: warm calls 178-204 ms,
resident memory 13,508,608 to 13,516,800 bytes, repeated-request allocation
spread 32 bytes. Each resident hosted conduit still commits a 3 GiB arena;
fleet capacity must account for commit separately from resident memory.
Capture verification retains data proportional to capture size and compares
client source membership across the bounded one/two-call acceptance cases.
Compiler heap/time behavior is unchanged.

Protocol, language guidance, admission and MCP dispatch are Codex `.cdx` code.
The current Windows adapter still depends on PowerShell/.NET Win32 bindings,
Wasmtime and WABT. Cobblestone OS/Kernel execution, isolation, byte transport,
clock and journal adapters remain future work. Public HTTP, browser binding,
leases and signed trust facts remain outside the completed local binding.

## 17. Installed numerical libraries (2026-09-19, red)

Main **25863**, from red **25862**, extends `run`, `check` and `lens` with
optional `library: "none" | "math" | "physics"`. Physics includes math.
The supported API and complete examples are served at `codex://accp/science`;
`apps/prism/accp/README.md` documents building and testing the bundles.
`AccpMath.codex` supplies binary64 elementary functions, central derivatives,
Simpson integration, bisection and scalar Runge-Kutta integration.
`AccpPhysics.codex` supplies classical mechanics, waves, circuits and heat
functions under explicit SI conventions and model assumptions.

The API returns `MathValue (Real)` or `MathError (Text)`. Execution `ok` does
not mean a mathematical answer exists. Numerical results are approximate;
the API supplies no symbolic algebra, arbitrary precision, dimensional type
checking or certified error bounds. Fixed-step methods require refinement
checks chosen by the caller. Domain limits and nonconvergence are explicit.

Library selection and source assembly remain in the Codex conduit. Assembly
joins JSON-encoded source strings without decoding caller source through CCE,
preserving CRLF and escaped input bytes. The combined source remains bounded
by the existing 64 KiB ceiling. Ambient effects and execution limits are
unchanged. Arbitrary external library resolution remains unavailable.
The adapter verifies bundle hashes before serving and retains verified source
in session memory. Records identify caller source, compiled source, selected
library and bundle hashes. A request refusal without a compiled unit carries
null `compiled_source_sha256`.

Both user-level client configurations select the proven versioned bundle at
`D:/Projects/Cobblestone-red-main/apps/prism/accp/build-output/science-25863`
through `serve.ps1 -Mcp -Bundle <path>`. Existing processes keep their original
loaded bundle; new sessions or MCP reconnection acquire the extension.
The standing computation rules for both clients include numerical use and
require units, assumptions, precision limits and `MathError` inspection.

Final focused evidence under `D:/Projects/Cobblestone-red/build-output/accp-science/`:
`corrected-science.log` passes 71 checks, `corrected-mcp.log` passes 40, and
`corrected-l2.log` passes 80, all exit zero. The final build prints depot
kernel digest `BD66718CBE24F589`. The Codex log-line census verifies 191 PASS
and zero FAIL lines, fact
`3ab507c52960a0c1ff6cab956d428e7e702d73afb44e5ec0671ce991d13c338a`,
retained in `count-evidence.json`. The installed bundle repeats the science
proof successfully; `installed-science.log` points to the retained wire and
journal capture. Copied science-card examples execute in that proof. Client
configuration parsing and fresh MCP execution are verified; no fresh model
conversation was run for this extension. Independent reader validation closed
the conflicting language-card wording before installation.

Final fixtures exposed CRLF source corruption as admission refusal
`missing opening`; exact JSON source assembly fixes that defect. Earlier test
wording and PowerShell syntax errors were corrected before the passing proof.
No compiler source or seed changed; the full battery and release gate were
not run. The new science witness has a classified tool-catalog entry.

Cost verdict: assembly is O(source bytes) in time and heap, bounded by source
limits and reclaimed at the request checkpoint. Scalar numerical loops are
O(steps) plus callback cost, with constant scalar working state; integer
exponentiation is O(log exponent). Library source remains resident at its
bounded bundle size. Science and L2 checks confirm checkpoint restoration;
compiler implementation heap/time behavior is unchanged.

## 18. Installed experiment bundle (2026-09-19, red)

Main **25868**, from red **25867**, adds `library: "experiment"` and
`output: "json"` for `run`. The experiment bundle includes math and physics.
The normative guide is `codex://accp/experiments`, implemented by
`apps/prism/AccpExperimentCard.codex`; `apps/prism/accp/experiments.md` states
the detailed contract. Runtime quantity operations check SI dimensions.
Numerical callback units remain explicit conventions, not inferred equations.
Structured results retain methods, assumptions, settings, sampled trajectories,
component tolerances and refinement differences. Exact binary64 strings accompany
decimal previews. Coupled RK4 publishes the fine trajectory and distinguishes
refinement agreement, nonconvergence, zero-length intervals and invalid models.
Sweeps preserve parameter order; SVG export preserves sampled order and escapes
labels without scripts or external resources.

Both user-level client configurations select
`D:/Projects/Cobblestone-red-main/apps/prism/accp/build-output/experiment-25868`.
The app-server `config/mcpServer/reload` operation reloaded the connection.
The immediate readiness assertion failed; subsequent live resource discovery
and execution verified the new bundle. The live conduit is
`19aca7828eedea72a681e2b9cc548663be8fcdf031c8a9054d9c0f2786f85296`;
the experiment source bundle is
`49b024382e7ca7b0dbbd6bc107cbce6c787e5fa2a87779aab08f3f48d6c63aaf`.

Focused final proof under the red workspace's
`build-output/accp-experiments/`: `final-experiment.log` passes 48 checks,
`final-science.log` passes 71, `final-mcp.log` passes 40 and `final-l2.log`
passes 80. Every process exits zero. The log-line census in
`count-evidence.json` verifies 239 PASS and zero FAIL lines, fact
`3c8e013e04223ef1ebcf2dff389ec3ed0fbf46ad6b863917855254d416475140`.
The installed bundle repeats all experiment checks; `installed-proof.log`
points to the retained wire, journal, JSON and SVG files. Catalog validation
and independent reader validation pass. No compiler source or seed changed;
builds identify depot kernel `BD66718CBE24F589`. No full battery or release gate
ran for this extension.

Live connected-tool evidence is in `live-evidence.json`: the damped oscillator
completes with `refinement_passed`, fact
`31c9708178d67a944295b07e9ded9c633c24dc16d2265d0019b545859b9cc6ca`.
`live-plot-evidence.json` and `damped-oscillator.svg` retain the live SVG export,
fact `bd4d75c2756fdc9b9b7a60045fca4db22d367a4fcecbb2b2ff0acc642ba66604`.
SVG XML, coordinate bounds, a constant point and label escaping are checked.
Browser visual inspection remains unrun because no browser connection was
available. Successful execution and matching refinement samples do not certify
the physical model or provide a solution-error bound.

Verification corrected a reserved `unit` parameter (CDX1060), a JSON newline
that closed the line-framed service, and decimal-preview overflow at the largest
finite binary64 value. JSON is compacted outside strings before embedding;
original stdout bytes and hashes remain available. Exact numeric bits are the
lossless representation; bounded decimal previews and abbreviated plot ticks
are presentation values.

Heap/time verdict: quantity dimension work is constant and bounded. Coupled
RK4 time and allocation are O(steps * components) plus callback cost; retained
trajectories use O(samples * components). The maximum step/component fixture
passes the existing memory/time ceilings. JSON and SVG use linear fragment
accumulation. Request checkpoint restoration passes in the experiment and L2
suites. Compiler implementation heap/time behavior is unchanged.

## 19. Historical standalone app relocation checkpoint (2026-09-19, red)

**IN FLIGHT, NOT PROVEN OR INSTALLED.** Damian requested ACCP's own top-level
app/tool category and explicitly required packaging its build independently of
Prism. Red **25871** holds the complete pending move and implementation,
open on disk and shelved with coverage checked. The installed service remains
the previously proven `apps/prism/accp/build-output/experiment-25868` bundle.
User client configurations have not changed for this relocation.

Workspace/client: `D:/Projects/Cobblestone-red`, `BigWhite_Codex_red`.
MAIN client: `BigWhite_Codex_red_main`, `D:/Projects/Cobblestone-red-main`.
Red **25870** merged main25869's experiment receipt. No code from 25871 is
submitted. At checkpoint, no owned build, guest or helper is running, no build
token is held, and `docs/Agents/red-workplan.md` remains empty. AgentGrid's
Codex app-server telemetry reached the handoff trigger: 70 percent, sample
`2026-09-19T21:30:28.784Z`, effective window 828400, compaction-valid. Use fresh
session telemetry on resume rather than that sample.

### Pending source and boundaries

- `apps/prism/Accp*.codex` and tracked files directly under
  `apps/prism/accp/` move into **`apps/accp/`**. Existing ignored outputs and
  historical proof captures stay intact. The new quire is **`Accp`**; source,
  examples and resource cards use `cites Accp chapter Accp...`.
- `apps/accp/build-runtime.ps1` is new. The parent `build.ps1` calls that
  builder, then builds conduit/MCP entries and numerical source bundles.
  `serve.ps1` verifies the added `runtime.json` artifact. Parent repository
  paths changed from three parent levels to two. No page extraction remains.
- `codex/build/quiremapScript.codex` adds the quire; the generated
  `build/host/windows/quire-map.ps1` carries the emitted entry. The stable
  `build/quire-map.ps1` forwarding path is unchanged.
- `apps/workflow/BuildActionRun.codex` now cites `Accp.AccpJson`;
  `build/host/windows/run-actions.ps1` loads `apps/accp/HostIo.cs`.
  `build/tool-catalog.json` updates moved paths/owners/IDs, classifies formerly
  legacy-pending moved scripts, and adds `accp.build.runtime`.
- No compiler, foreword, WASM emitter or seed source is edited. Shared
  low-level compiler, cite resolver, plug source-assembly and PE packaging
  helpers remain dependencies. Prism's app, landing HTML, page manifests,
  page build and existing plug binaries must not be required.

### Runtime bootstrap design requiring proof

The new runtime builder assembles the WASM stdio source from the canonical
compiler declarations, common IR parser, `WasmEmitter`, `WasmStdio` and
`PlugStdio`, using the shared chapter/cite assembly functions. The selected
depot seed compiles that unit as a hosted-Windows CDX; the PE writer packages
a private `wasm-emitter.exe` under the selected output's `runtime-build/`.
`PlugStdio` uses `read-file-uni ""`, whose hosted implementation reads Unicode
stdin until NUL/EOT (`X86_64Builtins.codex`, `emit-read-file-builtin`). Do not
substitute `WasmPlug`'s `read-serial-cce` path: that path calls the bare-metal
serial helper even in a hosted build.

The seed compiles the lens unit and a fresh canonical compiler concat to
`IR-UNI` with `text-plug` passes. The private native emitter consumes IR plus
a NUL, writes WAT, and bounded `wat2wasm --enable-tail-call` creates
`wasm-stdio.wasm` and `codex-compiler.wasm`. This pipeline has **NOT RUN**.
`HostIo.Child.Run` supplies process bounds. Output byte arrays are returned
without PowerShell pipeline enumeration. Runtime IR is capped at 32 MiB,
emitter capture at 128 MiB, children at 600 seconds and 4 GiB committed memory,
and final WASM artifacts at 16 MiB. Every compile measures free RAM first.

`runtime.json` records input/source/tool hashes, seed and resulting modules.
The default rebuilds; `-ReuseRuntime` permits reuse only after input fingerprint
and artifact hash checks. The fingerprint scans compiler/foreword source and
root-level common/WASM backend source, plus named toolchain files; it does not
scan Prism or page directories. That cache and the cold build still need proof.

### Evidence and exact continuation

`build-output/accp-standalone/` in the red workspace contains `moves.json`,
`source-hashes.json`, generator logs, generated script captures and a depot
seed print. The quire-map generator compiled and ran successfully with kernel
`BD66718CBE24F589`; the generated resolver diff adds only the Accp entry.
The capture was normalized with the test runner's trailing-empty-line rule,
then CRLF was restored. `quire-map.normalized.ps1` is the comparison artifact.
The tool-catalog check passed. These checks do **not** prove the runtime build,
the moved service, the changed build-action caller or the installation.
All ACCP runtime/relocation tests, cold isolated build, build-action consumer
proof and fresh installation are **NOT RUN**. Earlier section-18 results grade
the previous location/runtime only. No seed was installed or submitted.
The depot seed print's whole-file SHA-256 is
`BD66718CBE24F589E8AD8E9D7AAC47289081B00A8824ECD22DBDE77321279541`.

First inspect `p4 opened`, shelf 25871 and current main. Files remain open in
25871; do not unshelve over them merely because a shelf exists. Compare present
non-delete files against `source-hashes.json`. Complete the documented proof
isolation procedure, protecting both halves of every move; verify shelf
coverage before revert. Unshelve 25871 only after that preparation.

The next build is:

```powershell
pwsh -NoProfile -File apps/accp/build.ps1 -Kernel seed/Codex.cdx -Mcp
```

Prove independence using a fresh staging root containing the required compiler,
foreword, common/WASM backend sources, PE packager, build helpers/catalog,
`tools/codex-vm.exe`, seed and `apps/accp`, with **no `apps/prism`, landing page,
WASM page directory or prebuilt plug output**. Copy only explicit source/tool
inputs; do not disturb the live workspace's Prism files. Run the command against
that staging root and retain logs and runtime manifests. A fresh runtime may
expose compatibility failures hidden by the previously embedded module pair;
read diagnostics and report those failures rather than reinstalling old blobs.

Run the moved experiment, science, MCP and L2 suites serially. Compile and run
a focused build-action consumer check against the changed Accp cite and HostIo
path. Verify generator parity and catalog paths. Add ACCP's app-inventory row
and update current documentation after proof; retain historical receipt paths.
Submit/copy the move and edits with both sides accounted for, install a new
versioned bundle under the MAIN `apps/accp/build-output/`, update both user MCP
configurations, and reload through the verified app-server
`config/mcpServer/reload` method. Confirm live discovery and a computation.
The shell/MCP setup and reload schema from earlier work remain under
`build-output/accp-science/appserver-schema/`. Complete this task before backlog
work. Reclaim only owned processes; the installed MCP instances are intentional.

Cost review pending final proof: relocation does not alter numerical algorithms.
The new builder retains bounded IR/WAT byte arrays and a source-hash inventory;
source assembly/hash work is linear in source bytes. Peak runtime build memory
and elapsed time still need observation. No fixed point or full battery has run
or is claimed for this app-only relocation.

## 20. Standalone app and independent build (2026-09-19, red)

The relocation landed in red25879 and main25880. ACCP owns `apps/accp/`,
the `Accp` quire, the numerical libraries, protocol, host adapter, MCP entries,
runtime build and focused tests. `apps/accp/README.md` is the build/use entry;
`docs/CuratorsCatalogue.md` lists ACCP as an application. Source citations use
`cites Accp chapter Accp...`. Build actions import `Accp.AccpJson` and
`apps/accp/HostIo.cs`. Catalog IDs and executable entry contracts are in
`docs/Designs/Active/Build/Build.md`, ACCP packaging.

Build from the repository root:

```powershell
pwsh -NoProfile -File apps/accp/build.ps1 -Kernel seed/Codex.cdx -Mcp
```

`build-runtime.ps1` assembles canonical compiler declarations, common parser,
WASM emitter and stdio source. The chosen seed builds a private hosted Windows
emitter. `compile.ps1 -IrCce -Passes text-plug` captures SIZE-framed CCE IR;
checked CCE decoding converts only at the emitter's Unicode stdio boundary.
The emitter and WABT produce the compiler and lens WASM modules. `runtime.json` fingerprints source/tool
inputs and outputs; `manifest.json` covers the service bundle. `-ReuseRuntime`
requires matching input and artifact hashes.

No Prism app, landing HTML, WASM page directory, page manifest, page build or
prebuilt plug output is required. Shared compiler/backend sources, cite
resolution, PE packaging, the seed and VM remain repository toolchain inputs.
The cold proof used an explicit staging inventory with all excluded directories
absent. Every ACCP source file matched the staged proof input before submit.
The installed bundle was copied from the proven staging output and hash-checked.

Evidence under `D:/Projects/Cobblestone-red/build-output/accp-standalone/`:

- `staging-inputs.txt`, `isolated-repo/`, `cold-build-2.stdout.log` and
  `cold-build-2.stderr.log`: cold source build completed; exit 0.
- `runtime-reuse.log`: verified reuse after input and module hash checks.
- `experiment-test.log`, `science-test.log`, `mcp-test.log`, `test.log`:
  48, 71, 40 and 80 passes respectively, zero failures. Total 239, arithmetic
  fact `6666d675adb76f09746522c3815991b117c4a78d187901dff29279b70330a259`.
- `consumer-proof/record.json`: freshly compiled build-action executor
  completed resolve, compile, run, grade and record for arithmetic.
- Catalog, generated quire-map artifact parity and document counts passed.
  `final-source-hashes.json`, `landing-files.txt` and depot-content comparison
  verified the submitted contents and both sides of the relocation.

The seed remains
`BD66718CBE24F589E8AD8E9D7AAC47289081B00A8824ECD22DBDE77321279541`;
every compiler invocation printed prefix `BD66718CBE24F589`. No compiler or
seed source changed. No full battery, fixed point, BVT or public release is
claimed for the app relocation.

Installed script:
`D:/Projects/Cobblestone-red-main/apps/accp/serve.ps1`.
Installed bundle:
`D:/Projects/Cobblestone-red-main/apps/accp/build-output/standalone-25880`.
Both user MCP configurations now name the new script and bundle. Codex's
supported `config/mcpServer/reload` operation completed; live resource discovery
returned the `Accp` examples. A live dimension-checked calculation of 5 kg
times 2 m/s^2 returned 10 N with execution and scientific status `ok`.
`installed-proof.json` retains the response and fact
`9b11b1b8185b0d271b5953b82dc930b8adbf728a5e7f8f8cf9f03fab31ebcefe`.
Claude's configuration is updated; a new Claude harness run was not performed.

Installed compiler WASM:
`06288ad457fc55f0a47214f2f718d58445da1cb1f43a0f1d1a104afc452b58c6`.
Lens WASM:
`39766376e411316fa63195a3fec1bff0eaf6e3c27948a5cccc72fa1db8b8a1f2`.
MCP conduit:
`5fe2d980707a0d25450642832e278ad1405bc092b9320c296bad01e6350c8a10`.

Cost verdict: numerical and protocol algorithms are unchanged. Source hashing,
assembly and boundary conversion are linear in input bytes. IR and WAT buffers
are bounded; native emitter peak commit was 3,230,601,216 bytes under the 4 GiB
job ceiling, dominated by the existing hosted arena. The focused service run
reported warm latency 181-197 ms and repeated-request heap spread 16 bytes.
Cancellation removed owned children. Runtime intermediates and historical
Prism output caches remain separate from the versioned installed bundle.
