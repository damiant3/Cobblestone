# VLIW ISA Specification (Virtual Machine)

This document describes the virtual VLIW SIMD ISA implemented by the
`Machine` class in `problem.py`. The ISA is a Python-level representation:
programs are lists of instruction bundles (Python dicts), not binary encodings.

## Architecture Summary

- Word size: 32-bit unsigned; arithmetic results wrap modulo 2**32.
- Memory: shared flat array of 32-bit words; addresses are word indices.
- Scratch: per-core register file of 32-bit words, default size 1536.
- Vector length: VLEN = 8 lanes; vector operands are contiguous scratch ranges.
- Cores: N_CORES defaults to 1; each core has its own scratch and PC.

## Memory Architecture

This VM has two data storage spaces: global memory and per-core scratch.

### Global Memory (mem)

- A single flat array (`Machine.mem`) of 32-bit words shared by all cores.
- Word-addressed: address `A` refers to `mem[A]` (not bytes).
- The memory size is fixed by the initial `mem_dump` length; there is no
  allocation instruction.
- Loads read from committed memory; stores are buffered and committed after the
  bundle finishes (see "Execution Model").

### Scratch (register file)

- Each core has a private scratch array (`Core.scratch`) of 32-bit words.
- Scratch addresses in slots are indices into this array.
- Vectors are represented as contiguous scratch ranges: a vector base `V`
  refers to lanes `scratch[V + 0] .. scratch[V + VLEN-1]`.

### Addressing (indirect)

Load/store instructions take an *address register* (a scratch location) whose
contents is the memory index to access.

Example: `("load", dest, addr)` performs `scratch[dest] = mem[scratch[addr]]`.

### Ordering, Conflicts, and Multicore Notes

- Loads within a bundle see memory as it was at bundle start (stores in the
  same bundle are not visible until commit).
- If multiple stores in a bundle target the same `mem[...]` index, the last
  executed store wins (depends on engine/slot order).
- With multiple cores enabled, memory is shared but cores are stepped
  sequentially each cycle; a later-stepped core can observe earlier-stepped
  core writes from the "same cycle".

### Vector Memory Access

- `vload`/`vstore` operate on contiguous blocks: lane i accesses base+i.
- There is no gather/scatter vector load. The helper `load_offset` can be used
  to build a gather by issuing one slot per lane:
  `("load_offset", dest, addr, i)` loads `mem[scratch[addr+i]]` into
  `scratch[dest+i]`.

### Take-home Memory Image Layout (informational)

The benchmark kernel uses a conventional layout built by `build_mem_image` in
`problem.py`:

- Header (words 0..6):
  - `mem[0]`: rounds
  - `mem[1]`: n_nodes
  - `mem[2]`: batch_size
  - `mem[3]`: forest_height
  - `mem[4]`: forest_values_p (base address of node values)
  - `mem[5]`: inp_indices_p (base address of input indices)
  - `mem[6]`: inp_values_p (base address of input values)
- Arrays:
  - `mem[forest_values_p : forest_values_p + n_nodes]`: forest node values
  - `mem[inp_indices_p : inp_indices_p + batch_size]`: input indices
  - `mem[inp_values_p : inp_values_p + batch_size]`: input values
  - Note: in this repo's current `build_mem_image` implementation, the returned
    list ends at `inp_values_p + batch_size` (there is no guaranteed extra room
    beyond the input values region).

## Program Representation

An instruction bundle is a dict mapping engine names to a list of slot tuples.
Engines not present in the dict do nothing for that bundle.

Example:
```python
{
  "load": [("load", 0, 10)],
  "alu": [("+", 1, 0, 2)],
  "flow": [("cond_jump", 3, 42)],
}
```

## Execution Model

- Each simulator step, each RUNNING core fetches and executes 1 instruction
  bundle (at its current PC).
- All slots read the pre-bundle state. Writes are buffered and committed after
  all slots in the bundle execute, so same-bundle data dependencies do not work.
- Control-flow (`jump`, `cond_jump*`, `halt`, `pause`) does not cancel the rest
  of the current bundle; it only affects the next fetch (and/or the core state).
- If multiple slots write the same location in a bundle, the last executed slot
  wins. This is an implementation detail (depends on engine/slot order); avoid
  write conflicts.
- The cycle counter increments by 1 after stepping all cores if any executed
  bundle had at least one non-`debug` engine. Bundles containing only `debug`
  slots do not advance the cycle counter.
- There is no explicit `nop` opcode. In this Python-level encoding, unused
  engines/slots are simply omitted. A fully empty bundle `{}` is a no-op that
  advances PC but does not increment the cycle counter.
- The PC is incremented before executing the bundle. Jumps and branches update
  the already-incremented PC.
- `pause` sets a core to PAUSED; the next `run()` call resumes it. `halt` stops.
  Note: cores are stepped sequentially in core-id order, and this interpreter
  commits each core's writes before stepping the next core.

## Slot Limits (per bundle)

| Engine | Max slots |
| --- | --- |
| alu | 12 |
| valu | 6 |
| load | 2 |
| store | 2 |
| flow | 1 |
| debug | 64 (not enforced by the interpreter) |

## Engines and Slots

All operands are scratch addresses unless noted as immediate. Memory addresses
come from scratch values and are word-indexed.

## Addressing and Bounds

- PCs and memory/scratch addresses are Python list indices.
- Out-of-range indices raise `IndexError`. Negative indices use Python's
  negative-index behavior (indexing from the end), which is almost certainly
  unintended; treat negative indices as invalid/undefined behavior.
- Arithmetic results are masked to 32 bits; loads do not mask values on read.

### ALU engine

Slot form: `(op, dest, a1, a2)`

Instruction meanings:
- `+`: add `a1` and `a2`, write to `dest`.
- `-`: subtract `a2` from `a1`, write to `dest`.
- `*`: multiply `a1` and `a2`, write to `dest`.
- `//`: integer floor divide `a1` by `a2`, write to `dest`.
- `cdiv`: ceil divide, computed as `(a1 + a2 - 1) // a2` (expects `a2 > 0`).
- `^`: bitwise xor of `a1` and `a2`, write to `dest`.
- `&`: bitwise and of `a1` and `a2`, write to `dest`.
- `|`: bitwise or of `a1` and `a2`, write to `dest`.
- `<<`: logical left shift `a1` by `a2` bits, write to `dest`.
- `>>`: logical right shift `a1` by `a2` bits, write to `dest`.
- `%`: remainder of `a1 % a2`, write to `dest`.
- `<`: set `dest` to 1 if `a1 < a2`, else 0.
- `==`: set `dest` to 1 if `a1 == a2`, else 0.

All results are reduced modulo 2**32. Division by zero or negative shift counts
raise a Python error in the simulator, so treat them as invalid.

### VALU engine (vector)

Vector operands are base scratch addresses; lane i uses `base + i`.

Slot forms:
- `("vbroadcast", dest, src)`: replicate scalar `src` into all lanes
  `dest..dest+VLEN-1`.
- `("multiply_add", dest, a, b, c)`: per-lane multiply-add; for each lane
  i: `dest[i] = (a[i] * b[i] + c[i]) mod 2**32`.
- `(op, dest, a1, a2)`: per-lane ALU op, using the same op set as scalar ALU.

### LOAD engine

- `("load", dest, addr)`: load `mem[scratch[addr]]` into `dest`.
- `("load_offset", dest, addr, offset)`: load
  `mem[scratch[addr + offset]]` into `dest + offset`. This is a helper for
  treating `addr..addr+VLEN-1` as a vector of addresses and `dest..dest+VLEN-1`
  as a vector destination.
- `("vload", dest, addr)`: vector load; for i in 0..VLEN-1:
  `dest[i] = mem[scratch[addr] + i]`.
- `("const", dest, val)`: load immediate constant `val` into `dest` (mod 2**32).

### STORE engine

- `("store", addr, src)`: store `scratch[src]` into `mem[scratch[addr]]`.
- `("vstore", addr, src)`: vector store; for i in 0..VLEN-1:
  `mem[scratch[addr] + i] = scratch[src + i]`.

### FLOW engine

- `("select", dest, cond, a, b)`: scalar conditional select; if `scratch[cond]`
  is non-zero, write `scratch[a]` to `dest`, else write `scratch[b]`.
- `("add_imm", dest, a, imm)`: add immediate `imm` to `scratch[a]`, write to
  `dest` (mod 2**32).
- `("vselect", dest, cond, a, b)`: per-lane conditional select using vector
  condition `cond` and vector sources `a` and `b`.
- `("halt",)`: stop the core permanently.
- `("pause",)`: pause the core if pauses are enabled; `run()` resumes it.
- `("trace_write", val)`: append `scratch[val]` to the core trace buffer.
- `("cond_jump", cond, addr)`: if `scratch[cond]` is non-zero, set PC to the
  immediate absolute address `addr` (0-based instruction index).
- `("cond_jump_rel", cond, offset)`: if `scratch[cond]` is non-zero, add the
  immediate `offset` to the current PC (already incremented).
- `("jump", addr)`: unconditional jump to immediate absolute address `addr`
  (0-based instruction index).
- `("jump_indirect", addr)`: set PC to `scratch[addr]`.
- `("coreid", dest)`: write the current core id to `dest`.

### DEBUG engine

Debug slots are ignored for timing. If debug is enabled:
- `("compare", addr, key)`: assert `scratch[addr]` equals `value_trace[key]`.
- `("vcompare", addr, keys)`: assert `scratch[addr:addr+VLEN]` equals the
  corresponding trace values for `keys`.

Other debug slots (for example `("comment", ...)`) are treated as no-ops.
