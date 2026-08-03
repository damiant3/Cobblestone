# Capability Refinement

**Status**: Direction and time-boxing SHIPPED. Scope enforced on direct
calls, computed paths, and callee narrowing (CDX4002, ring 1, 2026-07-13);
the indirect route through an unscoped helper is OPEN -- see below.
**Date**: 2026-03-26 (status refreshed 2026-07-13)

| Axis | State |
|---|---|
| **1. Direction** (dotted sub-effects) | **SHIPPED.** `capability-vocabulary` in `codex/compiler/Types/TypeChecker.codex` (~3150) names `Console.Read`/`Console.Write`, `FileSystem.Read`/`FileSystem.Write`, `Network.Read`/`Network.Write`, `Gpu.Compute`/`Gpu.Memory`. The sub-effect lattice is `effect-covered-by` / `find-dot` (~812-827): a declared `FileSystem` covers a body's `FileSystem.Read`, but not the reverse. Violations are CDX2031. Tests: `codex/test/errors/effect-dotted-allow.codex` and `effect-dotted-deny.codex`. |
| **2. Scope** (path/host parameterization) | **RING 1 SHIPPED.** CDX4002 fires on a direct call outside its grant, on a computed path, and on a callee declaring a wider scope. The indirect route through an unscoped helper is OPEN. Filesystem paths only; network host/port is not wired. See below. |
| **3. Time-boxing** | **SHIPPED.** `with-timeout` is real syntax -- `AWithTimeoutExpr` threads through 7 compiler files (Lexer, Parser, ParserCore, ParserExpressions, TypeChecker, CodexEmitter, IRTextEmitter). Test: `codex/test/with-timeout-test.codex`. |

## Scope enforcement: what CDX4002 now catches, and what it does not

**Ring 1 shipped (2026-07-13).** CDX4002 is raised. Three routes are
closed; one is open and named below. Read this section before assuming
a scoped grant means what it says.

A correction to the record first: an earlier revision of this document
claimed "there is no CDX4002 anywhere in the tree." That was wrong.
`cdx-scope-violation : Integer = 4002` was **defined and registered**
in `CdxCodes.codex` all along, with the description "An effect
operation's argument is outside the granted scope for that capability."
It was simply never raised. A diagnostic that exists, reads correctly,
and fires nowhere is worse than a missing one -- it makes the grep for
"is this enforced?" come back green.

### Closed

| Route | Behaviour |
|---|---|
| **Direct call outside scope** | `file-exists "/etc/passwd"` under `[FileSystem.Read "/config/"]` is CDX4002. Pinned by `codex/test/errors/scope-violation`. |
| **Computed path** | `file-exists (sneak "")` is CDX4002 -- the compiler cannot prove where a path it did not read will point, and a scope that admits the unprovable is not a scope. Pass a literal, or widen the declaration. |
| **Callee declares a wider scope** | A callee declared over `"/etc/"` called from a caller granted `"/config/"` is CDX4002. A scope may narrow, never widen; a caller cannot delegate authority it was never granted. |
| **Narrowing** | `"/config/sub/deep.toml"` under a `"/config/"` grant compiles. Pinned by `codex/test/scope-allow`. |
| **No scope declared** | Unaffected. An empty scope is unrestricted and covers everything; the whole check is gated on some label actually carrying a scope, so no existing program changes behaviour. |

### The indirect route -- closed 2026-07-13

The first cut of this check left one route open: a helper that declared
no scope of its own laundered the access, because `read-file`,
`write-file`, and `file-exists` were registered in `TypeEnv.codex` with
`empty-row`. **They were typed pure.** A function that read the
filesystem carried no effect at all, so it needed no declaration, and
there was no row for a scope to ride through a call.

Those nine filesystem builtins now carry honest `[FileSystem.Read]` /
`[FileSystem.Write]` rows, which closes the route twice over:

```
peek : Text -> Boolean
peek (p) = file-exists p          -- CDX2031: FileSystem.Read not declared
```

and once it declares the effect honestly but not the scope:

```
peek : Text -> [FileSystem.Read] Boolean
peek (p) = file-exists p

opening : [Console, FileSystem.Read "/config/"] Nothing = act
  print-line-uni (if peek "/etc/passwd" then "read it" else "absent")
end
-- CDX4002: 'peek' performs FileSystem.Read with no scope of its own, so
-- it may touch anything, and this signature grants FileSystem.Read only
-- over a narrower scope.
```

A callee with no scope may touch anything, and under a narrowed grant
that is a widening -- the caller cannot hand on authority it was never
given. So an unscoped callee is not skipped, it is *measured*, and its
empty scope fails to sit inside a narrowed grant exactly as `"/etc/"`
would. That is what makes a scope survive an indirect call.

**A consequence worth knowing.** A scoped function may only reach its
resources through *literal* paths. Threading a path parameter through a
function that declares a scope is not provable and is refused -- declare
the scope at the boundary where the literal is known, or widen the
grant. This is a real restriction, and it is sound: a function that
accepts an arbitrary path while promising `/config/` is promising
something it cannot keep.

### Implementation as built

- `UnificationState.scope-grants` holds the labels the def under check
  declares -- per-def context set once at the def boundary, like
  `effect-exempt`. It is never threaded through unification.
- `lint-effect-scope` runs at every application in `infer-application`,
  and short-circuits on `row-any-scoped` before doing anything, so it
  costs nothing for the (currently entire) body of code that names no
  scope.
- `scoped-op-effect` maps an operation to the dotted sub-effect a grant
  is written against, so `read-file` answers to a `FileSystem.Read`
  grant and to a bare `FileSystem` grant alike, through the existing
  `effect-covered-by` lattice.
- Any one covering grant permits: a signature declaring both
  `[FileSystem]` and `[FileSystem.Read "/config/"]` has granted the
  wider of the two, and we hold it to what it actually said.

Runtime enforcement (scope data in the process capability table, checked
at the syscall boundary) remains a later ring and is not blocking.

Everything else in this document -- the direction markers, the dotted
vocabulary, `with-timeout` -- is already in the compiler. Read the
sections below as background on *why* scope is shaped the way it is,
not as a to-do list.

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

(This section describes the 2026-03-26 starting point. Direction and
time-boxing have since been built -- see the status table at the top.)

What was missing: the capabilities were **all-or-nothing**. If a process had
`CAP_FILESYSTEM`, it could read and write any file. If it had `CAP_NETWORK`,
it could reach any host. There was no way to say "this function can read files
in `/data/` but not `/etc/`" or "this network call can only reach `api.example.com`"
or "this capability expires after 30 seconds."

Direction and expiry are now expressible. **Path and host scope still
are not** -- that is the one axis left.

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

Capabilities should be **parameterized** -- not "can access the filesystem"
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

Capabilities should **expire** -- not "can write to the log forever" but
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
  (a closure capturing a time-boxed capability would be an error -- connects
  to linear closure analysis).
- **Runtime (bare metal)**: The process capability table gains a "valid until"
  tick count per capability bit. The syscall handler compares against the
  system tick counter. Expired capabilities are automatically denied.
  `SYS_GET_TICKS` already exists -- the infrastructure is in place.

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

The type of this expression is pure (no residual effects) -- the `with-timeout`
handler eliminates the `[FileSystem.Read "/config/"]` effect.

---

## Connection to Existing Systems

**Linear closures (Step 4)**: A time-boxed capability is inherently linear --
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
`docs/Designs/Active/Features/V3-REPOSITORY-FEDERATION.md`) are the
**same structure** operating at different layers. From
`docs/PM/Stories/Vision/DistributedAgentOS.md`:

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
  I have indirect trust in Bob's code -- but less. If `main` grants
  `[IO "/data/"]` and calls `process-file`, that function inherits a
  narrower scope -- never wider.
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
Capabilities aren't names in a hierarchy -- they're **positions in the trust
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

| Step | What | State |
|------|------|-------|
| ~~1~~ | ~~Direction markers in effect syntax~~ | **DONE** -- dotted names parse and type-check |
| ~~2~~ | ~~Direction-aware capability checker~~ | **DONE** -- `capability-vocabulary` + `effect-covered-by`, CDX2031 |
| ~~3~~ | ~~Split bare metal CAP bits (Read/Write)~~ | **DONE** -- the vocabulary maps to cap ids via `manifest-cap-id` |
| 4 | Compile-time scope checking (CDX4002) | **RING 1 SHIPPED 2026-07-13.** Direct calls, computed paths, and callee narrowing are enforced. The indirect route through an unscoped helper is open and blocked on the builtins carrying honest effect rows. |
| ~~5~~ | ~~Scope in EffectfulType~~ | **DONE (carried, not checked)** -- `EffectfulTy effs scopes inner`, `EffectLabel.scope`. The slot is there; step 4 is what reads it. |
| **6** | **Runtime scope enforcement** | Open. Depends on step 4. Not blocking -- compile-time first. |
| ~~7~~ | ~~`with-timeout` syntax + semantics~~ | **DONE** -- `AWithTimeoutExpr` across 7 compiler files |
| ~~8~~ | ~~Runtime time-boxing (tick-based expiry)~~ | **DONE** |

The whole remaining plan is step 4, then step 6.

Note on step 5: it is listed done because the type *carries* the scope,
which is what the step asked for. It is not enforcement. The
distinction is exactly the trap this document's old status hid -- a
field that is populated and never read looks like a shipped feature
from every angle except the one that matters.

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

---

## External Research (IRISA Harvest, 2026-06-23)

See `docs/Reference/IRISA_Research_Harvest.md` for full context.

### SOTERN -- Intent-Based Security

The SOTERN team (IRISA D2) studies "intent-based security" --
expressing security policies as high-level intents that the system
enforces automatically. Instead of writing explicit capability
grants, the user states an intent ("only signed code runs", "no
network access for untrusted code") and the system derives the
enforcement mechanism.

**Applicability:** Our effect system already encodes what a function
can do (`[Console, FileSystem]`). Intent-based security would add a
higher layer: a prose-level security policy that the compiler
verifies against the capability lattice. Example: a CPL assertion
`intent: no function with Network capability may access identity
keys` would be checked against the capability refinement lattice at
compile time. This connects the prose layer (V2 narration) to the
capability system -- security policies become load-bearing prose,
not comments.
