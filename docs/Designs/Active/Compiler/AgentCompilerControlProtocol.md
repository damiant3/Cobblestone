# Agent-Compiler Control Protocol (ACCP)

**Status: design, proposed. Opened 2026-09-16 at Damian's direction. No
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
