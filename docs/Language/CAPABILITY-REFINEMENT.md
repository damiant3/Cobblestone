# Capability Refinement

**Status**: Design
**Date**: 2026-03-26

---

## What We Have

The Codex effect/capability system is operational at three levels:

1. **Language level**: 11 built-in effects (Console, FileSystem, Network, State,
   Time, Random, Display, Camera, Microphone, Location, Identity). User-defined
   effects via `effect ... where`. Handlers via `with Effect computation`.
2. **Type level**: `EffectfulType` tracks which effects a function performs.
   `CapabilityChecker` verifies at compile time that `main` only uses granted
   effects. Effect polymorphism via `EffectRowVariable`.
3. **Runtime level**: On bare metal (Ring 3), capability bits in the process
   table gate syscall access. `CAP_CONSOLE`, `CAP_FILESYSTEM`, `CAP_NETWORK`,
   `CAP_CONCURRENT`. Denied syscalls return -1.

What's missing: the capabilities are **all-or-nothing**. If a process has
`CAP_FILESYSTEM`, it can read and write any file. If it has `CAP_NETWORK`,
it can reach any host. There's no way to say "this function can read files
in `/data/` but not `/etc/`" or "this network call can only reach `api.example.com`"
or "this capability expires after 30 seconds."

---

## The Three Refinements

### 1. Direction

Capabilities should distinguish **read vs write** (and more generally,
the direction of data flow).

**Current**: `[FileSystem] Text` means "performs FileSystem effects, returns Text."
It doesn't distinguish reading from writing.

**Proposed**: Refine effect operations with direction markers.

```
effect FileSystem where
  read-file  : Text -> [FileSystem.Read] Text
  write-file : Text -> Text -> [FileSystem.Write] Nothing
  open-file  : Text -> [FileSystem.Read] linear FileHandle
```

A function that only reads files would have type `[FileSystem.Read] Text`.
A function that writes would have `[FileSystem.Write] Nothing`. A function
that does both would have `[FileSystem.Read, FileSystem.Write] Text`.

**Implementation path**:
- Extend `EffectType` to support dotted names: `EffectType(Name, SubEffect?)`
- `FileSystem` becomes a shorthand for `FileSystem.Read + FileSystem.Write`
- The capability checker resolves shorthand to the full set
- Bare metal: split `CAP_FILESYSTEM` into `CAP_FS_READ` and `CAP_FS_WRITE`

**Why it matters**: Read-only capabilities are safe to grant broadly. Write
capabilities are dangerous. Direction lets the type system distinguish the two,
and the OS enforce the distinction at the syscall level.

### 2. Scope

Capabilities should be **parameterized** — not "can access the filesystem"
but "can access these specific paths."

**Current**: `[FileSystem] Text` grants access to all files.

**Proposed**: Scoped effects carry a scope parameter.

```
-- Type-level scope
read-config : [FileSystem "/config/"] Text
read-config = read-file "/config/app.toml"

-- Runtime scope: path prefix check
read-file "/etc/passwd"  -- CDX4002: path "/etc/passwd" outside granted scope "/config/"
```

**Implementation path**:
- **Compile-time**: Add an optional scope literal to `EffectfulType`:
  `EffectfulType(Effects, Return, Scope?)`. The capability checker compares
  the scope of each operation call against the granted scope.
- **Runtime (bare metal)**: Store scope data in the capability table alongside
  the bitfield. For filesystem: a path prefix string pointer. For network: an
  allowed host list pointer. The syscall handler checks the argument against
  the scope before allowing the operation.
- **Incremental**: Start with compile-time only (CDX4002 diagnostic). Add
  runtime enforcement in a later ring.

**Scope types by effect**:

| Effect | Scope parameter | Example |
|--------|----------------|---------|
| FileSystem | Path prefix | `"/data/"`, `"/tmp/"` |
| Network | Host/port | `"api.example.com:443"` |
| Console | Channel | `"stdout"`, `"stderr"` |
| State | Key namespace | `"user.prefs"` |

**Why it matters**: Least privilege. A function that reads config files shouldn't
be able to read SSH keys. A function that calls one API shouldn't be able to
call any API. Scope is how you say exactly what a capability allows.

### 3. Time-boxing

Capabilities should **expire** — not "can write to the log forever" but
"can write to the log for the next 5 seconds."

**Current**: Capabilities are granted at process creation and never change.

**Proposed**: Capabilities carry an optional TTL (time-to-live).

```
-- Grant a 30-second window for network access
with-timeout 30 [Network] do
  response <- fetch "https://api.example.com/data"
  parse-json response
```

**Implementation path**:
- **Language level**: `with-timeout <seconds> [Effect] <computation>` syntax.
  Desugars to a capability grant + timer + revocation.
- **Type level**: `EffectfulType` gains an optional `Timeout` field. The
  type checker ensures time-boxed capabilities don't escape their scope
  (a closure capturing a time-boxed capability would be an error — connects
  to linear closure analysis).
- **Runtime (bare metal)**: The process capability table gains a "valid until"
  tick count per capability bit. The syscall handler compares against the
  system tick counter. Expired capabilities are automatically denied.
  `SYS_GET_TICKS` already exists — the infrastructure is in place.

**Why it matters**: Time-boxing prevents capability leaks. If a function is
supposed to do one HTTP call, it shouldn't hold the network capability
indefinitely. Time-boxing makes capabilities ephemeral by default.

---

## Composition

The three refinements compose naturally:

```
-- Direction + Scope + Time-boxing
with-timeout 10 [FileSystem.Read "/config/"] do
  config <- read-file "/config/app.toml"
  parse-config config
```

This says: "for the next 10 seconds, this computation can read files under
`/config/`, but not write, and not access anything outside that directory."

The type of this expression is pure (no residual effects) — the `with-timeout`
handler eliminates the `[FileSystem.Read "/config/"]` effect.

---

## Connection to Existing Systems

**Linear closures (Step 4)**: A time-boxed capability is inherently linear —
it must be consumed before it expires. The closure escape analysis ensures
that a closure capturing a time-boxed capability is used exactly once and
within the time window. This is the same `linear` function type mechanism
shipped today.

**Regions (CAMP-IIIA)**: Capability lifetime and region lifetime are the
same concept. A capability scoped to a region expires when the region ends.
On bare metal, this means the capability bits are cleared when the region's
arena is freed.

**CCE encoding**: Scope parameters (paths, hosts) are CCE-encoded strings
in the capability table. The boundary normalization (TAB/CR) applies.
No special handling needed.

---

## The Unified Trust Lattice

The capability system and the repository trust lattice (see
`docs/Designs/V3-REPOSITORY-FEDERATION.md`) are the **same structure**
operating at different layers. From `DistributedAgentOS.txt`:

> "The type system is the trust model. The compiler enforces it."

| Layer | Question | Lattice dimension |
|-------|----------|-------------------|
| Repository | "Do I trust this *code*?" | Author, vouch chain, proof coverage |
| Capability | "Do I trust this *function* with this *resource*?" | Direction, scope, duration |
| Runtime | "Do I trust this *process* with this *syscall*?" | Capability bits, scope data, tick expiry |

Both lattices share the same properties:

- **Multi-dimensional**: Trust is not a single number. A capability is not
  a single bit. Both are profiles across multiple axes.
- **Transitive with decay**: If I trust Alice and Alice trusts Bob's code,
  I have indirect trust in Bob's code — but less. If `main` grants
  `[IO "/data/"]` and calls `process-file`, that function inherits a
  narrower scope — never wider.
- **Threshold-gated**: The repository rejects facts below a trust threshold
  at build time. The capability system rejects operations outside scope at
  compile time (CDX4002) or runtime (syscall denied).

### Unification: Capabilities as Trust Facts

In the repository model, everything is a fact. Capabilities can be facts too:

```
-- A Trust fact in the repository
Trust(author: alice, target: hash("json-parser"), degree: Verified)

-- A Capability fact (same structure)
Capability(grantee: process-42, target: "IO", scope: "/data/", expires: tick+5000)
```

When the repository federation protocol syncs facts between machines,
capability grants could flow the same way. A device grants capabilities to
code it trusts; trust is computed from the vouching lattice; the vouching
lattice is built from repository facts.

The chain: **author vouches for code → code gets trust score → trust score
determines granted capabilities → capabilities gate runtime operations.**

This means the dotted-name problem (`FileSystem.Read.CDrive.Config`) dissolves.
Capabilities aren't names in a hierarchy — they're **positions in the trust
lattice**. The lattice dimensions are:

```
Capability {
  resource : URI        -- what (file, socket, device, API)
  direction : Read | Write | ReadWrite
  scope : URI prefix    -- where (path, host, namespace)
  duration : Ticks      -- how long
  trust : Float         -- minimum trust threshold of granting chain
}
```

A `[IO]` effect annotation in the source says "this function needs I/O."
The capability descriptor (from the process table, or the build manifest,
or the repository trust chain) says exactly *which* I/O, *in which direction*,
*to what scope*, *for how long*, and *with what trust backing*.

The type system verifies compatibility. The compiler rejects mismatches.
The OS enforces at the syscall boundary. The repository tracks who vouched
for the code that's making the request.

One lattice. Three layers.

---

## Implementation Order

| Step | What | Effort | Depends on |
|------|------|--------|------------|
| 1 | Direction markers in effect syntax | Small | Parser + TypeChecker |
| 2 | Direction-aware capability checker | Small | Step 1 |
| 3 | Split bare metal CAP bits (Read/Write) | Small | Step 2 + x86-64 backend |
| 4 | Compile-time scope checking (CDX4002) | Medium | New diagnostic infrastructure |
| 5 | Scope in EffectfulType | Medium | Step 4 + TypeChecker |
| 6 | Runtime scope enforcement | Medium | Step 5 + bare metal process table |
| 7 | `with-timeout` syntax + semantics | Medium | Parser + TypeChecker + linear analysis |
| 8 | Runtime time-boxing (tick-based expiry) | Small | Step 7 + SYS_GET_TICKS |

Steps 1-3 are near-term (direction only). Steps 4-6 are medium-term (scope).
Steps 7-8 are medium-term (time-boxing).

---

## Open Questions

1. **Scope syntax**: Should scope be a string literal (`[FileSystem "/data/"]`)
   or a type-level value? String literals are simple but not composable.
   Type-level paths would allow scope algebra (`Scope.Union`, `Scope.Intersect`).

2. **Scope inheritance**: When a function calls another function, does the
   callee inherit the caller's scope? Or must scopes be explicitly passed?
   Explicit is safer (no ambient authority) but verbose.

3. **Time-boxing granularity**: Seconds? Ticks? Both? On bare metal, ticks
   are the natural unit (timer interrupt frequency). On hosted targets,
   wall-clock seconds are more intuitive.

4. **Revocation**: Can a capability be revoked before its timeout? This
   connects to the question of whether capabilities are values (can be
   dropped) or obligations (must be consumed). Linear types suggest the
   latter.

---

## Examples

### Today (no refinement)
```
main : [Console, FileSystem] Nothing
main = do
  content <- read-file "/etc/passwd"
  print-line content
```
Compiles. Runs. Reads any file. No restrictions.

### With Direction
```
main : [Console.Write, FileSystem.Read] Nothing
main = do
  content <- read-file "/config/app.toml"
  print-line content
-- write-file "hack.txt" "pwned"  -- CDX4001: FileSystem.Write not granted
```

### With Direction + Scope
```
main : [Console.Write, FileSystem.Read "/config/"] Nothing
main = do
  content <- read-file "/config/app.toml"
  print-line content
-- read-file "/etc/passwd"  -- CDX4002: path outside scope "/config/"
```

### With Direction + Scope + Time-boxing
```
main : Nothing
main = with-timeout 5 [FileSystem.Read "/config/"] do
  content <- read-file "/config/app.toml"
  parse-and-cache content
-- After 5 seconds, FileSystem.Read capability is revoked
-- parse-and-cache runs with no filesystem access
```
