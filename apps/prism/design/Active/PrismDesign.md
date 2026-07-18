# Prism -- Codex Through Every Lens

## What It Is

A web application that shows Codex source code in the center and the
output of every compiler plug arrayed around it. Browse a function,
see it simultaneously as ARM64 machine code, RISC-V, x86-64, WASM,
Python, Rust, JavaScript, C#, Go, Haskell, and 40 more targets. The
compiler is the server. The plugs are live.

The name: a prism splits one beam of light into a spectrum. Codex
source goes in; every target language comes out, fanned across the
display.

## Architecture

```
                    Browser
                      |
                   HTTP/JSON
                      |
              +-------+-------+
              | Compiler CDX  |
              | (codex-vm)    |
              |               |
              | WebServer     |
              | + Frontend    |
              | + IR Emitter  |
              +---+-----------+
                  |  |  |  |
            TCP   |  |  |  |  TCP
           9101  9102 ... 9150
              |  |  |  |
          +---+--+--+--+---+
          | plug | plug | plug |
          | VMs  | VMs  | VMs  |
          +------+------+------+
```

### Components

**1. Compiler VM (main process)**

The self-hosted compiler CDX, extended with:
- WebServer routes for the UI and API
- The full frontend pipeline (lex, parse, desugar, scope, check, lower)
- IR text emission via `emit-ir-chapter`
- TCP client code to dispatch IR to plug sidecars

This is NOT a new binary. It's the same compiler, booted with a
different entry mode that starts the web server instead of compiling
stdin.

**2. Plug Sidecars (N processes)**

Each plug CDX boots in its own codex-vm instance, listening on a
dedicated TCP port (9101, 9102, ...). They already do this -- the
existing `run.ps1` pattern starts a TCP listener on port 9100 and
the plug connects to it. Prism inverts this slightly: the plugs
listen, the compiler connects.

Active plugs for launch (high-value targets):
- x86-64 (in-process, no sidecar needed)
- ARM64, RISC-V (machine code)
- WASM (web target)
- Python, JavaScript, TypeScript, Rust, Go, C#, Java
- Haskell, OCaml, Scheme (academic/FP)

Full fleet (50 plugs) available but UI defaults to a curated subset.

**3. Browser UI**

Static HTML/CSS/JS served by the compiler VM. No framework, no
build step. The UI has three panels:

```
+------------------------------------------+
|  [file browser]  |  Codex Source  (center)|
|  apps/           |                       |
|  codex/          |  opening : ...        |
|  foreword/       |   let x = parse ...   |
|                  |   in check x          |
+------------------+-----------------------+
|  ARM64  | RISC-V | Python | Rust | JS ...|
|  mov x0 | mv a0  | def op | fn o | func  |
|  bl chk | jal ch | check  | chec | check |
|  ...    | ...    | ...    | ...  | ...   |
+------------------------------------------+
```

Top: file tree (left) + source (right).
Bottom: plug output tabs/panels, horizontally scrollable.

## API

### `GET /`
Serve the static HTML UI.

### `GET /api/files`
List available source files. Returns JSON array of paths.

### `GET /api/source?path=codex/compiler/opening.codex`
Return the raw source text of a file.

### `POST /api/compile`
Body: `{ "source": "..." }` (raw Codex source text)
Response: `{ "ir": "...", "x86": "...", "diagnostics": [...] }`

Runs the full frontend pipeline on the source text. Returns:
- `ir`: The IR s-expression text (what plugs consume)
- `x86`: The x86-64 output (in-process, always available)
- `diagnostics`: Any compiler warnings/errors

### `POST /api/plug`
Body: `{ "ir": "...", "plug": "python" }`
Response: `{ "output": "...", "plug": "python" }`

Sends IR to a specific plug sidecar and returns its output. The
browser calls this N times in parallel (one per active plug) after
receiving the IR from `/api/compile`.

### `GET /api/plugs`
List available plug sidecars and their status (alive/dead/port).

## Invocation

### Host Launch Script (`apps/prism/run.ps1`)

```powershell
# 1. Boot plug sidecars (each in its own codex-vm, background)
$plugs = @(
  @{name="python";  port=9101; cdx="codex/plugs/python/python-plug.cdx"},
  @{name="javascript"; port=9102; cdx="codex/plugs/javascript/js-plug.cdx"},
  @{name="rust";    port=9103; cdx="codex/plugs/rust/rust-plug.cdx"},
  # ...
)
foreach ($p in $plugs) {
  Start-Process tools/codex-vm.exe -ArgumentList `
    "-kernel $($p.cdx) -net -port $($p.port)" -NoNewWindow
}

# 2. Boot the compiler VM with web server mode
tools/codex-vm.exe -kernel seed/Codex.cdx `
  -net -port 8080 -mode prism
```

The `-mode prism` flag tells the compiler's `dispatch-on-mode` to
start the web server instead of compiling stdin. Port 8080 is the
HTTP port exposed to the host browser.

### Plug Sidecar Protocol

Each plug sidecar runs a loop:
1. Listen on its assigned TCP port
2. Accept connection from compiler VM
3. Receive IR text (length-prefixed)
4. Run codegen (e.g., `py-emit-module`)
5. Send back target language text (length-prefixed)
6. Loop (keep alive for next request)

This is a minor change from the current run-once model: the plug
stays alive after producing output, waiting for the next IR payload.

## Implementation Plan

### Phase 1 -- Static Compiler Explorer (read-only)

- Add `prism` mode to compiler's `dispatch-on-mode`
- Wire WebServer with routes: `/`, `/api/files`, `/api/source`
- Serve static HTML UI from embedded strings or a data section
- Browse source files, view Codex code -- no compilation yet

### Phase 2 -- In-Process Compilation

- Add `/api/compile` route
- Call frontend pipeline from the web handler
- Return IR text + x86-64 output + diagnostics
- UI shows source + IR + x86 side-by-side

### Phase 3 -- Plug Fan-Out

- Add TCP client pool for plug connections
- Add `/api/plug` and `/api/plugs` routes
- Modify plug entry points to loop (accept multiple requests)
- Launch script boots sidecar fleet
- UI shows all active plug outputs

### Phase 4 -- Polish

- Syntax highlighting (Codex + target languages)
- Function-level navigation (click a function, see just that def)
- Diff view (compare two plug outputs)
- Permalink (share a specific file + function view)
- Performance: cache IR for unchanged source

## Dependencies

- `codex/os/net/WebServer.codex` -- HTTP server (exists)
- `codex/os/net/NetIO.codex` -- TCP client (exists)
- `codex/compiler/opening.codex` -- frontend pipeline (exists)
- `codex/compiler/Emit/IRTextEmitter.codex` -- IR emission (exists)
- All plug CDX binaries -- built by existing `run.ps1` scripts

## Open Questions

1. **Plug VM lifecycle.** Keep all 50 alive, or boot on demand? RAM
   cost of 50 codex-vm instances vs latency of cold boot (~2s each).

2. **Source file access.** The compiler VM reads from its serial
   input today. For Prism, it needs to read arbitrary `.codex` files
   from disk. The `read-file` builtin handles this, but we need to
   verify it works for paths outside the compile pipeline.

3. **Concurrent requests.** WebServer.codex handles one request at a
   time today. For plug fan-out, we need either sequential dispatch
   (slower but safe) or concurrent TCP sends (needs fork/await or
   polling loop). Sequential is fine for Phase 3; concurrency is
   Phase 4 optimization.

4. **IR size.** The full compiler IR is ~1.8 MB+. Should Prism
   compile individual functions/chapters rather than the full program?
   Per-chapter compilation is the natural unit.
