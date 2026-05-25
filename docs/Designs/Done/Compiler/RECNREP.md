# Codex Deterministic Record and Replay

## Status

**Stage:** Design  
**Scope:** Compiler, Runtime, Verifier, OS (Agents)  
**Applies to:** Strike IR, Scheduler, Capability/Effect System

---

## 1. Goals

**Primary goals**

- **Deterministic replay of Codex executions** at the IR/agent level, not at the syscall/CPU level.
- **No change to observable semantics:** replayed runs are indistinguishable from original runs.
- **Low overhead:** only true sources of nondeterminism are recorded.
- **Verifier‑friendly:** replay logs are structured so they can be used as proof artifacts.

**Non‑goals**

- Emulating barbarian record‑and‑replay (syscall logs, thread interleavings, signals).
- Replaying arbitrary foreign binaries.
- Time‑travel debugging at arbitrary machine instructions.

---

## 2. Determinism model

Codex assumes:

- **Single‑artifact substrate:** compiler == OS == runtime.
- **Preemptive scheduling via PIT (CL 711):**
  - Hardware timer interrupts can trigger context switches.
  - In **record mode**, each preemption decision is captured as a `ScheduleEvent`.
  - In **replay mode**, the scheduler takes preemption decisions from the log instead of the timer.
- **Phase discipline:** deck/bivy/strike separation; no cross‑phase mutation.
- **Capability‑scoped effects:** all external interactions go through capabilities.
- **No ambient global state:** all state is reachable via capabilities.

Under these assumptions, a Codex execution is fully determined by:

1. **Program text** (Codex source / CDX).
2. **Initial capability set** (what the program is allowed to touch).
3. **Initial agent graph** (which agents exist at time 0).
4. **External inputs** (user input, time, randomness, external devices), as seen through capabilities.
5. **Scheduler decisions** (including PIT‑driven preemptions), as recorded in `ScheduleEvent`s.

The only nondeterminism we must record is (4) and (5).

---

## 3. Replay abstraction

A **Replay Session** consists of:

- **Program ID:** hash of CDX or Codex text.
- **Initial Context ID:** hash of initial capabilities + agent graph.
- **Replay Log:** a sequence of **Replay Events**.

Replay is valid iff:

- Program ID matches.
- Initial Context ID matches.
- The runtime replays the same Replay Log.

If these hold, the execution is guaranteed to be bit‑for‑bit identical at the IR/agent level.

---

## 4. Replay events

Replay events are defined at the **Codex substrate level**, not at the hardware level.

### 4.1 Event types

- **`InputEvent`**
  - **When:** an agent reads from an input capability (keyboard, file, network, hardware RNG, etc.).
  - **Fields:**
    - `agent_id`
    - `capability_id`
    - `op` (e.g., `read`, `recv`, `get-random`)
    - `payload` (bytes or structured value)
  - **Hardware RNG (RDRAND):**
    - Modeled as `InputEvent` with `op = "get-random"`.
    - All uses of RDRAND must go through a replay‑aware capability.

- **`TimeEvent`**
  - **When:** an agent queries time or timers fire.
  - **Fields:**
    - `agent_id`
    - `capability_id`
    - `logical_time` (abstract time value)

- **`SpawnEvent`**
  - **When:** a new agent is created via `fork`/spawn.
  - **Fields:**
    - `parent_agent_id`
    - `child_agent_id`
    - `entry_point` (function id)
    - `initial_caps` (capability set snapshot)

- **`ScheduleEvent`**
  - **When:** the scheduler chooses the next runnable agent (including PIT‑driven preemption and IPC blocking/wake).
  - **Fields:**
    - `from_agent_id`
    - `to_agent_id`
    - `reason` (enum):
      - `yield`
      - `await`
      - `spawn`
      - `exit`
      - `preempt` (PIT‑driven)
      - `chan-send-block`
      - `chan-recv-block`

- **`ExitEvent`**
  - **When:** an agent terminates.
  - **Fields:**
    - `agent_id`
    - `exit_code` (if any)

### 4.2 Device seed

At boot, Codex generates a **device seed** from RDRAND:

- In **record mode**:
  - The seed derivation is recorded as either:
    - 4 `InputEvent`s (`op = "get-random"`) for each RDRAND call, or
    - a single compound `InputEvent` containing the full seed.
- In **replay mode**:
  - The same sequence/event is consumed.
  - Device identity and any seed‑derived behavior are deterministic across replays.

### 4.3 Event ordering

Events are recorded in **scheduler order**:

- At each yield/await/preempt/IPC block/wake/agent transition, the runtime appends the relevant events.
- The log is a linear sequence; no partial orders are exposed.

Because scheduling is driven by the log in replay mode, this sequence is sufficient to reconstruct the entire run.

---

## 5. Runtime integration

### 5.1 Execution modes

- **Normal mode**
  - No logging.
  - Behavior as today.

- **Record mode**
  - Runtime writes Replay Events to a log capability.
  - All external inputs/time/scheduling decisions (including PIT preemptions and IPC block/wake) are recorded.

- **Replay mode**
  - Runtime reads Replay Events from a log capability.
  - External inputs/time/scheduling decisions are **taken from the log**, not from the environment or PIT.

### 5.2 Scheduler hooks

The scheduler is extended with:

- `record_schedule(from, to, reason)` in record mode.
- `next_scheduled_agent()` in replay mode:
  - Reads the next `ScheduleEvent`.
  - Asserts `from` matches current agent.
  - Switches to `to`.

Yield/await/fork/exit, PIT preemption, and IPC paths (`chan-kern-send-block`, `chan-kern-recv-block`) are instrumented to emit or consume the corresponding `ScheduleEvent`s.

### 5.3 Capability hooks

Capabilities that can introduce nondeterminism (IO, time, randomness, hardware RNG) must:

- In **record mode**:
  - Perform the operation.
  - Record the result as an `InputEvent` or `TimeEvent`.

- In **replay mode**:
  - Skip the real operation.
  - Read the next matching event from the log.
  - Return the recorded payload.

Capabilities that are pure or deterministic given their inputs do not participate in replay.

---

## 6. Verifier and proof integration

Replay logs are **first‑class artifacts** for the verifier:

- Each Replay Session has a **Replay ID** (hash of the log).
- Claims can be annotated with:
  - “Holds for all executions” (log‑independent).
  - “Holds for Replay ID X” (log‑specific).

The verifier can:

- Re‑run a program in replay mode with a given log.
- Check that:
  - All claimed properties hold.
  - All observed behaviors match the log.

This enables:

- **Proof replay:** re‑checking proofs against recorded executions.
- **Regression checking:** ensuring future compiler/runtime changes preserve behavior for existing logs.

---

## 7. Compiler integration

The compiler must preserve replay semantics:

- **No hidden nondeterminism**:
  - Optimizations must not introduce new sources of nondeterminism.
  - All nondeterministic operations must go through replay‑aware capabilities.

- **Stable IR boundaries (forward‑looking constraint)**:
  - Yield/await/preempt points are treated as semantic boundaries.
  - Agent creation and termination remain explicit.
  - Even though Codex does not optimize yet, future optimizations must respect these boundaries.

- **Replay‑safe transformations**:
  - Transformations that reorder pure computations are allowed.
  - Transformations that reorder or merge replay‑visible events must preserve the event sequence.

The compiler may optionally emit:

- **Replay metadata**:
  - Mapping from IR locations to replay events.
  - Useful for debugging and verifier diagnostics.

---

## 8. File format

**Replay Log Format (CRF – Codex Replay Format)**

- **Header**
  - `magic = "CRF1"`
  - `program_hash`
  - `context_hash`
  - `flags` (e.g., compression, encryption)
  - (No fixed `agent_count`; dynamic agents are described by `SpawnEvent`s.)

- **Body**
  - Length‑prefixed sequence of Replay Events.
  - Each event:
    - `event_type`
    - `agent_id`
    - `payload_len`
    - `payload_bytes`

The format is intentionally simple and append‑friendly.

---

## 9. Failure modes and invariants

**Invariants**

- In replay mode:
  - Every replay‑visible operation must consume exactly one matching event.
  - The event sequence must be fully consumed by the end of execution.
- Program and context hashes must match the log header.

**Failure modes**

- **Mismatch:** event type or agent id does not match expectation → hard failure.
- **Exhausted log:** runtime needs an event but log is empty → hard failure.
- **Unused tail:** log has remaining events after program exit → warning or failure (configurable).

---

## 10. Future extensions

- **Partial replay**
  - Start from a snapshot + suffix of a log.
- **Selective replay**
  - Replay only some capabilities; others run live.
- **Interactive debugging**
  - Step through replay events with a debugger attached.
- **Verifier‑generated logs**
  - Synthesized logs for symbolic or constrained executions.