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
| `check` | `source`, optional `id`, `limits` | diagnostics or entry row and `outside_ambient` |
| `run` | `source`, optional `input`, `id`, `limits` | buffered stdout, exact output bytes in `stdout_base64`, output hash |
| `lens` | `source`, `plug: "wasm"`, optional `id`, `limits` | WAT text |

Sources must be complete compilation units. The optional `library` field
selects `none` (default), `math`, `physics`, or `experiment`; physics includes
math, and experiment includes physics. The
build bundles the selected Codex chapters and their dependencies. No arbitrary
library resolver or compiler flags are exposed. The fixed request mode is
`IR-UNI decks=12`. Compile overflow/refusal remains explicit; the
service does not silently increase budgets. `opening` must be unique, take
zero parameters and return plain Integer, Text, Boolean, Char or Nothing,
optionally wrapped in a concrete effect row.

Pure computation and buffered `Console` are ambient. Directional Console
labels are supported. Other effects are reported by `check` and refused by
`run` and `lens`. Any `lease` field is refused as unsupported. The WASM
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

The four tools are `codex_run`, `codex_check`, `codex_lens` and
`codex_describe`. The language card is the resource `codex://accp/language`.
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
enabled_tools = ["codex_run", "codex_check", "codex_lens", "codex_describe"]
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
