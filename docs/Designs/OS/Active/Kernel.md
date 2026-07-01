# Codex.OS Kernel Infrastructure

**Date**: 2026-05-01
**Status**: Substantially shipped -- boot, scheduler, IPC, shell, filesystem built. Pending: some design questions (shell grammar, multi-user).
**Depends on**: Verifier (CL 626), Identity (CL 627), Crypto (CL 541),
Capability system (CLs 539-580), Process table (exists in X86_64Boot)
**Covers**: Boot sequence, Scheduler, IPC, Filesystem, Shell

These five components are interdependent. The boot sequence loads the
filesystem, starts the scheduler, and launches the shell. The scheduler
dispatches processes that communicate via IPC. The filesystem persists
facts that all components depend on. Designing them separately would
create seams; this document designs them together.

---

## What Already Exists

| Component | What's Built | Where |
|-----------|-------------|-------|
| Boot (Ring 0) | Multiboot → long mode, PIC, IDT, serial I/O | `X86_64Boot.codex` |
| Process table | 16 slots × 256 bytes, CR3, capability bits | `X86_64Boot.codex:490` |
| Syscall handler | write(1), read-key(2), get-ticks(3) + capability check | `X86_64Boot.codex:623` |
| Timer | PIT at 18 Hz, tick counter at 0x7000 | `X86_64Boot.codex:49` |
| Watchdog | Stall detection via RIP/heap sampling | `X86_64Boot.codex:220` |
| Page tables | Identity-mapped PML4/PDPT/PD | `X86_64Boot.codex:513` |
| Capability grant | `emit-grant-capability` — OR bit into proc table | `X86_64Boot.codex:554` |
| In-process concurrency | fork/await (sequential, CAMP-IIIC Phase 1) | `X86_64.codex` (CL 623) |
| Fact store foreword | In-memory HAMT, 5 fact kinds | `foreword/FactStore.codex` |
| FileSystem effect | open/read/write/close (interface only) | `foreword/FileSystem.codex` |

---

## 1. Boot Sequence

### The Problem

The kernel boots and runs the compiler. A real OS needs an init
sequence that loads facts from disk, starts the scheduler, establishes
identity, and launches the shell. The boot sequence is the capability
root — whoever runs first can grant everything. Getting this wrong is
a privilege escalation vulnerability.

### Design

The boot sequence has seven stages. Each stage runs with the minimum
capabilities it needs. No stage runs with all capabilities.

```
Stage 0: Hardware init         (existing — multiboot, long mode, PIC, IDT, serial)
Stage 1: Block driver init     (new — detect storage device, read sectors)
Stage 2: Fact store load       (new — read root hash, load fact HAMT from disk)
Stage 3: Verifier load         (new — load verifier binary from fact store, self-verify)
Stage 4: Scheduler start       (new — init process table, start idle process)
Stage 5: Identity establish    (new — load or generate device/user keypairs)
Stage 6: Shell launch          (new — start shell process with user's capabilities)
```

**Capability root rule**: Stage 0 runs in Ring 0 with full hardware
access. Stage 1 gets block device access only. Stage 2 gets read-only
fact store access. Stage 3 gets verifier execution. Stage 4 gets
process creation. Stages 5-6 run as user-level processes with granted
capabilities. Each stage explicitly drops capabilities it no longer
needs before proceeding.

**First-boot variant**: If Stage 2 finds no fact store on disk (empty
device), the boot sequence branches to the first-boot ceremony
(Identity design doc, CL 627): generate device keypair, prompt for
user setup, create initial trust lattice, write root facts to disk.

### Block Driver

The minimum viable block driver reads and writes 512-byte sectors from
an ATA/IDE device (the simplest interface, widely emulated by QEMU).
PIO mode (port I/O, no DMA) is sufficient for boot.

Syscalls added:
- `block-read(sector, count, buffer)` — read sectors to buffer
- `block-write(sector, count, buffer)` — write sectors from buffer

Both require the `BlockDevice` capability (new bit 10).

---

## 2. Scheduler

### The Problem

The process table has 16 slots but no scheduler. There is no context
switch, no time slicing, no CPU quota enforcement.

### Design

**Algorithm**: Priority round-robin with 4 priority levels.

```
Priority 0: Kernel (boot stages, verifier)
Priority 1: System (scheduler, fact store daemon)
Priority 2: Normal (user programs, agents)
Priority 3: Background (fact compaction, garbage collection)
```

Each priority level has a run queue (linked list via the process
table's next-ptr field). The scheduler always picks the highest-priority
non-empty queue. Within a priority, round-robin.

**Time slice**: 3 ticks (~167 ms at 18 Hz). Configurable per priority:

| Priority | Slice (ticks) | Rationale |
|----------|--------------|-----------|
| 0 | No preemption | Kernel code is trusted and short |
| 1 | 6 | System services need latency |
| 2 | 3 | Default for user code |
| 3 | 1 | Background yields aggressively |

**Context switch**: On timer tick, if the current process has exhausted
its slice:

1. Save callee-saved registers (RBX, RBP, R12-R15) to the process
   table entry.
2. Save RSP, R10 (heap pointer), deck-pos to the process table.
3. Load next process's saved state from its process table entry.
4. If the next process has a different CR3, reload CR3 (TLB flush).
5. `iretq` into the restored context.

Cost: ~30 instructions for same-CR3 switch, ~50 with TLB flush.

**Process table entry additions** (within the existing 256-byte slot):

```
Offset  72: saved RSP          (8B)
Offset  80: saved R10          (8B)  heap pointer
Offset  88: saved deck-pos     (8B)
Offset  96: saved RBX          (8B)
Offset 104: saved RBP          (8B)
Offset 112: saved R12          (8B)
Offset 120: saved R13          (8B)
Offset 128: saved R14          (8B)
Offset 136: saved R15          (8B)
Offset 144: saved RIP          (8B)  return address
Offset 152: saved RFLAGS       (8B)
Offset 160: priority           (8B)  0-3
Offset 168: slice-remaining    (8B)  ticks left in current slice
Offset 176: next-proc          (8B)  linked list for run queue
Offset 184: state              (8B)  0=free, 1=ready, 2=running, 3=blocked, 4=zombie
Offset 192: parent-proc        (8B)  process that created this one
Offset 200: exit-code          (8B)
Offset 208: blocked-on         (8B)  IPC: which resource we're waiting for
Offset 216-255: reserved       (40B)
```

This fits within the existing 256-byte entry. The current `state` at
offset 0 moves to offset 184 (the old offset 0 is repurposed — or
aliased for backward compatibility during migration).

**CPU quota**: Each process has a `max-ticks` field. The scheduler
decrements it every tick. When it reaches zero, the process is killed
(state = zombie, exit-code = QUOTA_EXCEEDED). The capability system
sets the quota at process creation based on the policy.

**Starvation prevention**: If a priority-2 or priority-3 process has
not run for 100 ticks (~5.5 seconds), promote it to the next higher
priority temporarily (one slice, then demote back).

---

## 3. Inter-Process Communication

### The Problem

Two processes on the same kernel need to communicate. The agent
protocol is for network agents; kernel-level IPC is undesigned.

### Design

**Model**: Typed message passing via capability-gated channels.

A **channel** is a kernel object with:
- A bounded message buffer (ring buffer, N slots × M bytes)
- A sender capability and a receiver capability
- A type tag (the kernel doesn't interpret the message, but the
  verifier checks that sender and receiver agree on the type)

```
Channel = record {
  buffer-base   : Address     -- ring buffer in kernel memory
  buffer-size   : Integer     -- number of slots
  slot-size     : Integer     -- bytes per message
  write-pos     : Integer     -- producer index
  read-pos      : Integer     -- consumer index
  sender-proc   : Integer     -- process ID of sender (or -1 for any)
  receiver-proc : Integer     -- process ID of receiver (or -1 for any)
  type-hash     : Bytes32     -- SHA-256 of the message type
}
```

**Syscalls added**:

| Syscall | RAX | Args | Description |
|---------|-----|------|-------------|
| `chan-create` | 10 | RDI=slots, RSI=slot-size | Create channel, return channel ID |
| `chan-send` | 11 | RDI=chan-id, RSI=msg-ptr | Send message (blocks if full) |
| `chan-recv` | 12 | RDI=chan-id, RSI=buf-ptr | Receive message (blocks if empty) |
| `chan-close` | 13 | RDI=chan-id | Close channel, wake blocked processes |
| `chan-try-send` | 14 | RDI=chan-id, RSI=msg-ptr | Non-blocking send (returns 0/1) |
| `chan-try-recv` | 15 | RDI=chan-id, RSI=buf-ptr | Non-blocking receive (returns 0/1) |

All require the `IPC` capability (new bit 11).

**Blocking semantics**: When a process blocks on `chan-send` (buffer
full) or `chan-recv` (buffer empty), the scheduler sets its state to
`blocked` and records the channel ID in `blocked-on`. When the other
side sends/receives, the kernel wakes the blocked process (state →
ready, push to run queue).

**Channel ownership**: The process that creates a channel owns it.
Ownership can be transferred by sending the channel ID over another
channel (capability passing). The verifier checks that the receiving
process has the IPC capability.

**No shared memory**: Processes do not share address space. Messages
are copied through the kernel's channel buffer. This is slower than
shared memory but eliminates data races by construction.

---

## 4. Filesystem (Facts on Disk)

### The Problem

The fact store is in-memory. Policies, forensic records, trust lattice,
and program binaries are all facts. When the machine reboots, they're
gone.

### Design

**Model**: Content-addressed append-only log with a HAMT index.

The on-disk format has three regions:

```
Sector 0:        Superblock (512 bytes)
Sectors 1-N:     Fact log (append-only, sequential writes)
Sectors N+1-M:   HAMT index pages (copy-on-write, random access)
```

**Superblock**:
```
Offset  0: magic            (8B)  "CODEXFS1"
Offset  8: log-head         (8B)  sector offset of next free log slot
Offset 16: index-root       (8B)  sector offset of HAMT root page
Offset 24: fact-count        (8B)  total facts stored
Offset 32: index-gen        (8B)  generation counter (for crash recovery)
Offset 40: content-hash     (32B) SHA-256 of the HAMT root page
Offset 72-511: reserved
```

**Fact log entry**:
```
Offset  0: hash             (32B) SHA-256 of content
Offset 32: kind             (2B)  FactKind enum
Offset 34: author-key       (32B) Ed25519 public key
Offset 66: timestamp        (8B)  tick count at creation
Offset 74: content-length   (4B)  bytes
Offset 78: content          (variable) the fact's payload
Padding to sector boundary.
```

Facts are written sequentially to the log. The HAMT index maps
hash → log-sector for O(log32 n) lookup. The HAMT uses copy-on-write:
when a page is modified, a new page is written and the parent pointer
is updated. The superblock's `index-root` always points to a
consistent HAMT.

**Crash recovery**: The superblock is double-buffered (sector 0 and
sector 1). Writes alternate between the two copies. On boot, the
kernel reads both and uses the one with the higher `index-gen` that
has a valid `content-hash`. This ensures atomicity: either the new
superblock is fully written (use it) or it isn't (use the old one).
No journal needed — the append-only log and COW HAMT are inherently
crash-safe.

**Wear leveling**: Not in scope for V1. The log is append-only, which
naturally distributes writes. COW HAMT pages are allocated from a free
list. Explicit wear leveling is deferred to V2 (when flash devices are
a real target).

**Capability integration**: The `FileSystem` effect maps to fact store
operations:
- `FileSystem.Read` → `fact-load`, `fact-has`
- `FileSystem.Write` → `fact-store`, `fact-create`

Path scoping (`[FileSystem.Read "/config/"]`) maps to fact kind
filtering: the scope prefix selects which fact kinds the process can
access.

**Syscalls added**:

| Syscall | RAX | Args | Description |
|---------|-----|------|-------------|
| `fact-read` | 20 | RDI=hash-ptr, RSI=buf-ptr | Read fact by hash |
| `fact-write` | 21 | RDI=content-ptr, RSI=len | Write fact, return hash |
| `fact-exists` | 22 | RDI=hash-ptr | Check existence, return 0/1 |

All require the appropriate `FileSystem` capability.

---

## 5. Shell

### The Problem

The human-OS interface. Not bash — a typed, capability-aware prose
interface.

### Design

The Shell is a user-level process (priority 2) that reads prose
commands, type-checks them, and executes them. It is the only process
that interacts directly with the human via serial/console I/O.

**Command grammar**: A subset of the Codex Prose Language (CPL)
designed for imperative commands:

```
install <name> from <author> (trust: <threshold>)
grant <name> [<capabilities>]
run <name> <arguments>
revoke <name> [<capabilities>]
status <name>
list programs
list capabilities for <name>
trust <author-key> (score: <value>)
```

Each command is parsed using the existing Codex parser's prose mode.
The parsed command is type-checked: `install` requires the `Install`
capability, `grant` requires `CapabilityAdmin`, `run` requires the
capabilities that the target program needs.

**Command execution**:
1. Read a line from serial/console.
2. Parse as a CPL command.
3. Type-check against the current user's capabilities.
4. If type-check fails, print the error (via the Clarifier if
   the error is ambiguity — suggest a correction).
5. If type-check succeeds, execute:
   - `install`: call the verifier on the CDX binary, add to fact store
   - `grant`: update capability table for the target process
   - `run`: create a new process, grant capabilities, start it
   - `revoke`: remove capabilities from the target process
   - `status`: query process table
   - `list`: enumerate fact store / capability table
   - `trust`: update trust lattice

**Effect tracking**: The shell declares `[Console, Install, CapabilityAdmin,
FileSystem, Identity]`. Each command uses a subset. The effect system
ensures the shell cannot exceed its grant.

**The Clarifier**: When a command is ambiguous (e.g., "run parser" when
multiple programs match "parser"), the shell invokes the Clarifier
(designed in `docs/Designs/Language/Clarifier.md`) to ask for
clarification: "Did you mean json-parser by alice or xml-parser by
bob?"

---

## Sequencing

| Step | Component | Effort | Blocks On |
|------|-----------|--------|-----------|
| 1 | Block driver (PIO ATA) | Medium | Nothing |
| 2 | On-disk fact store | Large | Step 1 |
| 3 | Scheduler (round-robin + context switch) | Large | Nothing |
| 4 | IPC (channels) | Medium | Step 3 |
| 5 | Boot sequence (7 stages) | Medium | Steps 1-4 |
| 6 | Shell (command parsing + execution) | Medium | Steps 2-5 |

Steps 1-2 (storage) and 3-4 (scheduling/IPC) are independent tracks.
Step 5 (boot) integrates them. Step 6 (shell) is the capstone.

---

## New Capability Bits

| Bit | Name | Used By |
|-----|------|---------|
| 10 | `BlockDevice` | Block driver (boot stage 1 only) |
| 11 | `IPC` | Channel operations |
| 12 | `ProcessCreate` | Scheduler (creating new processes) |
| 13 | `Install` | Shell (installing CDX binaries) |
| 14 | `CapabilityAdmin` | Shell (granting/revoking capabilities) |

---

## Open Questions

1. **ATA vs. virtio**: ATA PIO is the simplest block interface but
   slow. QEMU supports virtio-blk which is faster and simpler (MMIO,
   no port I/O dance). Which should V1 target?

2. **Fact store size**: How large can the HAMT index get before
   performance degrades? The in-memory HAMT is O(log32 n) per lookup.
   The on-disk version adds one sector read per HAMT level. For 1M
   facts, that's ~4 sector reads per lookup. Acceptable?

3. **Process limit**: 16 slots is hardcoded. Is this enough for an OS
   with a shell, a verifier, a fact store daemon, and user programs?
   Expanding requires changing the process table layout.

4. **Shell language**: Should the shell accept full Codex expressions
   or only a restricted command grammar? Full Codex is more powerful
   but harder to type interactively.

5. **Multi-user**: The current design assumes one user per device. If
   multiple users share a device (school, family), the shell needs a
   session concept and per-user capability sets. Deferred to V2.

6. **Formal models for low-level isolation (IRISA, 2026-06-23):**
   The SUSHI team (IRISA D3) builds formal models for low-level
   security mechanisms — proving that page table setups and VMX
   isolation actually prevent guest-to-host escapes. Relevant to
   Gap 5 (DevHypervisor): we run bare-metal with no OS mitigations
   (no ASLR, no KPTI), so the boot sequence's capability stage
   model and the VMX guest isolation are our only protection layers.
   SUSHI's microarchitectural attack research (Spectre/Meltdown
   class) could inform whether our identity-mapped, no-ASLR layout
   has exploitable microarchitectural side channels when running
   untrusted guest code in VMX partitions. Their compiler-support-
   for-security work could also inform hardening the boot stages
   against fault injection. See `docs/Reference/IRISA_Research_Harvest.md`.
