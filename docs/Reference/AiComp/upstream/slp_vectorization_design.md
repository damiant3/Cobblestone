# SLP Vectorization Design Document

## 1. Introduction

### 1.1 Overview

Superword Level Parallelism (SLP) is an auto-vectorization technique that converts scalar operations in straight-line code into SIMD (Single Instruction Multiple Data) vector operations. Unlike traditional loop vectorization, SLP operates within basic blocks and identifies independent isomorphic instructions that can be packed together.

### 1.2 Key References

- **Original Paper**: "Exploiting Superword Level Parallelism with Multimedia Instruction Sets" - Larsen & Amarasinghe, PLDI 2000

### 1.3 Goals

- Automatically identify vectorization opportunities in straight-line code
- Pack isomorphic scalar instructions into vector instructions
- Ensure correctness through legality checks
- Make profitable vectorization decisions through cost modeling
- The pass should run on HIR and after LoopUnroll and CSE
- Use the existing data dependency graph

---

## 2. Core Concepts

### 2.1 Pack

A **pack** is a set of independent, isomorphic scalar instructions that can be replaced by a single vector instruction.

```
Scalar instructions:          Vector instruction:
  add r0, a0, b0                vadd <r0,r1,r2,r3>,
  add r1, a1, b1          →          <a0,a1,a2,a3>,
  add r2, a2, b2                     <b0,b1,b2,b3>
  add r3, a3, b3
```

### 2.2 Seed

A **seed** (roots of DDG) is the starting point for building packs. Seeds are typically consecutive memory accesses (loads or stores) because:

1. **Hardware Constraint**: Vector memory instructions require contiguous addresses
2. **Natural Alignment**: Memory addresses provide clear pairing criteria
3. **Expansion Anchor**: Memory operations serve as convergence/divergence points in dataflow

**Why Stores are Preferred Seeds**:
- Stores are data sinks with single value sources
- Following use-def chains upward yields clear, non-branching paths
- Loads may have multiple users, creating complex expansion paths

### 2.3 Isomorphic Instructions

Two instructions are isomorphic if they:
- Have the same opcode
- Have the same type
- Have the same number of operands
- Are in the same basic block

---

## 3. Algorithm Overview

```
┌─────────────────────────────────────────┐
│  Phase 1: Seed Discovery                │
│  - Scan for consecutive memory accesses │
└───────────────────┬─────────────────────┘
                    ↓
┌─────────────────────────────────────────┐
│  Phase 2: Pack Extension                │
│  - Extend along use-def chains          │
│  - Reorder commutative operands         │
└───────────────────┬─────────────────────┘
                    ↓
┌─────────────────────────────────────────┐
│  Phase 3: Legality Checking             │
│  - Memory dependence analysis (RAW)     │
│  - Alias analysis                       │
└───────────────────┬─────────────────────┘
                    ↓
┌─────────────────────────────────────────┐
│  Phase 4: Cost Model Evaluation         │
│  - Compare scalar vs vector cost        │
│  - Account for shuffle/insert overhead  │
└───────────────────┬─────────────────────┘
                    ↓
┌─────────────────────────────────────────┐
│  Phase 5: Scheduling                    │
│  - Build dependency graph               │
│  - Topological sort                     │
└───────────────────┬─────────────────────┘
                    ↓
┌─────────────────────────────────────────┐
│  Phase 6: Code Generation               │
│  - Generate vector instructions         │
│  - Insert shuffle/extract as needed     │
└─────────────────────────────────────────┘
```

---

## 4. Phase 1: Seed Discovery

### 4.1 Algorithm

```
function FindSeeds(BasicBlock BB):
    seeds = []
    memory_ops = CollectMemoryOperations(BB)
    
    for each pair (op1, op2) in memory_ops:
        if AreConsecutive(op1.address, op2.address):
            seeds.append(Pack(op1, op2))
    
    return seeds
```

### 4.2 Consecutiveness Check

Two memory addresses are consecutive if:
```
address_diff = addr2 - addr1
is_consecutive = (address_diff == element_size)
```

---

## 5. Phase 2: Pack Extension

### 5.1 Bottom-Up Extension (Use-Def Chain)

Starting from seeds, extend upward by examining operands:

```
function ExtendPack(Pack P):
    worklist = [P]
    all_packs = []
    
    while worklist is not empty:
        pack = worklist.pop()
        all_packs.append(pack)
        
        for i in range(num_operands):
            operand_group = GetOperands(pack, i)
            
            if CanPack(operand_group):
                new_pack = CreatePack(operand_group)
                worklist.append(new_pack)
    
    return all_packs
```

### 5.2 Packing Conditions

```
function CanPack(instructions[]):
    # Same opcode
    if not AllSameOpcode(instructions):
        return false
    
    # Same type
    if not AllSameType(instructions):
        return false
    
    # No dependencies between instructions
    if HasInternalDependence(instructions):
        return false
    
    # For memory operations: must be consecutive
    if IsMemoryOp(instructions[0]):
        if not AreAllConsecutive(instructions):
            return false
    
    return true
```

### 5.3 Example

```c
// Source code
int t0 = a[0] + b[0];
int t1 = a[1] + b[1];
int r0 = t0 * c[0];
int r1 = t1 * c[1];
d[0] = r0;
d[1] = r1;
```

Extension process:
```
Step 1: Seed = {store d[0], store d[1]}
        ↓
Step 2: Values stored are (r0, r1)
        → Pack_Mul = {mul r0, mul r1}
        ↓
Step 3: Operands of Pack_Mul:
        Left:  (t0, t1) → Pack_Add = {add t0, add t1}
        Right: (c0, c1) → Pack_LoadC = {load c[0], load c[1]}
        ↓
Step 4: Operands of Pack_Add:
        Left:  (a0, a1) → Pack_LoadA = {load a[0], load a[1]}
        Right: (b0, b1) → Pack_LoadB = {load b[0], load b[1]}
```

---

## 6. Handling Commutative Operations

### 6.1 The Problem

When packing instructions with commutative operations, operands may be in different orders:

```c
t0 = a[0] + b[0];
t1 = b[1] + a[1];   // Operands swapped!

// When packing (t0, t1):
// Left operands:  (a[0], b[1]) - don't match
// Right operands: (b[0], a[1]) - don't match
```

### 6.2 Solution: Operand Reordering

For commutative operations (add, mul, and, or, etc.), we can swap operands to enable packing:

```
function TryReorderOperands(Pack P):
    if not IsCommutative(P[0].opcode):
        return false
    
    left_ops = GetLeftOperands(P)
    right_ops = GetRightOperands(P)
    
    # Check if current order works
    if CanPack(left_ops) and CanPack(right_ops):
        return true
    
    # Try swapping operands of second instruction
    SwapOperands(P[1])
    left_ops = GetLeftOperands(P)
    right_ops = GetRightOperands(P)
    
    if CanPack(left_ops) and CanPack(right_ops):
        return true
    
    # Revert if swapping didn't help
    SwapOperands(P[1])
    return false
```

### 6.3 Example

```c
t0 = a[0] + b[0];
t1 = b[1] + a[1];   // Swap to: a[1] + b[1]

After reordering:
  Left operands:  (a[0], a[1]) → consecutive loads ✓
  Right operands: (b[0], b[1]) → consecutive loads ✓
```

---

## 7. Handling Pack Size vs Vector Length

### 7.1 When Pack Size > VLEN

If a pack contains more elements than the hardware vector width supports:

```
Pack = {s0, s1, s2, s3, s4, s5, s6, s7}  // 8 elements
VLEN = 4  // Hardware supports 4 elements
```

**Solution: Split the pack**

```
function SplitPack(Pack P, int vlen):
    result = []
    
    for i in range(0, len(P), vlen):
        sub_pack = P[i : i + vlen]
        result.append(sub_pack)
    
    return result

# Result:
# Pack_1 = {s0, s1, s2, s3}
# Pack_2 = {s4, s5, s6, s7}
```

### 7.2 Non-Power-of-Two Pack Sizes

For pack sizes like 5, 6, 7:

```
Pack = {s0, s1, s2, s3, s4}  // 5 elements
VLEN = 4
```

**Options**:

1. **Truncate + Scalar**: Vectorize 4, leave 1 as scalar
   ```
   Pack_Vec = {s0, s1, s2, s3}  → vstore
   Scalar   = {s4}              → store
   ```

2. **Padding with Mask**: Pad to next power of 2, use masked operations
   ```
   Pack_Padded = {s0, s1, s2, s3, s4, undef, undef, undef}
   → masked vstore
   ```

---

## 8. Legality Checking

### 8.1 Memory Dependence Analysis (RAW Only)

Ensure vectorization doesn't violate Read-After-Write dependencies:

**RAW (Read After Write)**: A load must read the value written by a preceding store.

```
function CheckRAWDependence(Pack P):
    first = P[0]
    last = P[len(P)-1]
    
    for inst in InstructionsBetween(first, last):
        if IsLoad(inst):
            for p_inst in P:
                if IsStore(p_inst):
                    if MayAlias(p_inst.address, inst.address):
                        if p_inst comes before inst in original order:
                            # Vectorizing might move store, breaking RAW
                            return ILLEGAL
    
    return LEGAL
```

**Example of Illegal Vectorization**:
```c
store a[0], x    // S1
y = a[0]         // L1: RAW dependence on S1
store a[1], z    // S2

// Vectorizing {S1, S2} would execute both stores together
// L1 might read wrong value if S2 is moved before it
// This violates the RAW dependence!
```

### 8.2 Alias Analysis

Determine if pointers may reference the same memory:

```
function CheckAliasSafety(LoadPack, StorePack):
    for load in LoadPack:
        for store in StorePack:
            alias = AliasAnalysis(load.ptr, store.ptr)
            
            if alias == MayAlias or alias == MustAlias:
                if RangesOverlap(load, store):
                    return UNSAFE
    
    return SAFE
```

**Alias Results**:
- `NoAlias`: Definitely different locations
- `MayAlias`: Possibly same location (conservative)
- `MustAlias`: Definitely same location

**Handling May-Alias with Runtime Checks**:
```c
// Generate runtime alias check
if (c + 4 <= a || a + 4 <= c) {
    vectorized_version();
} else {
    scalar_version();
}
```

### 8.3 Contiguity Verification

Verify that addresses are truly consecutive:

```
function VerifyContiguity(Pack P):
    element_size = GetElementSize(P[0])
    
    for i in range(1, len(P)):
        expected_addr = P[0].address + i * element_size
        actual_addr = P[i].address
        
        diff = ComputeDiff(actual_addr, expected_addr)
        
        if diff != 0:
            return NOT_CONTIGUOUS
    
    return CONTIGUOUS
```

### 8.4 Complete Legality Check

```
function CheckVectorizationLegality(Pack P):
    # 1. Contiguity
    if not VerifyContiguity(P):
        return ILLEGAL, "Non-contiguous addresses"
    
    # 2. RAW dependence
    if not CheckRAWDependence(P):
        return ILLEGAL, "RAW dependence violation"
    
    # 3. Alias analysis (for stores)
    if ContainsStores(P):
        if not CheckAliasSafety(P):
            return ILLEGAL, "Potential alias conflict"
    
    return LEGAL, None
```

---

## 10. Scheduling

### 10.1 Dependency Graph Construction

Use the existing Data Dependency Graph (DDG)

### 10.2 Topological Sort

Order packs respecting dependencies:

```
function SchedulePacks(Graph G):
    scheduled = []
    ready = [p for p in G.nodes if G.InDegree(p) == 0]
    
    while ready is not empty:
        pack = SelectBest(ready)  # Heuristic selection
        scheduled.append(pack)
        
        for successor in G.Successors(pack):
            G.RemoveEdge(pack, successor)
            if G.InDegree(successor) == 0:
                ready.append(successor)
    
    return scheduled
```

### 10.3 Handling Mixed Scalar/Vector Code

When some instructions can't be vectorized:

```
Vectorized:              Scalar:
vload A[0:3]            load A[4]
vload B[0:3]            load B[4]
vadd                    add
vstore D[0:3]           store D[4]

// Schedule must respect dependencies between both
```

---

## 11. Code Generation

### 11.1 Pack to Vector Instruction Translation

```
function GenerateVectorCode(Pack P):
    if IsLoadPack(P):
        return GenerateVectorLoad(P)
    
    if IsStorePack(P):
        return GenerateVectorStore(P)
    
    if IsArithmeticPack(P):
        return GenerateVectorArithmetic(P)
    
    # ... other cases
```

### 11.2 Vector Load Generation

```
Pack: {load a[0], load a[1], load a[2], load a[3]}

Generated instruction:
  vload v0, [addr_a]    ; load 4 consecutive elements
```

### 11.3 Vector Store Generation

```
Pack: {store d[0], store d[1], store d[2], store d[3]}

Generated instruction:
  vstore v0, [addr_d]   ; store 4 consecutive elements
```

### 11.4 Handling Insert/Extract

When pack interacts with scalar code:

```c
// x is scalar, not in any pack
t0 = a[0] + x;
t1 = a[1] + x;

Generated instructions:
  vload  v0, [addr_a]       ; load <a[0], a[1]>
  vbroadcast v1, x          ; broadcast x to all lanes
  vadd   v2, v0, v1         ; vector add
```

### 11.5 Shuffle Generation

When operand order doesn't match:

```c
r0 = t0 + c[1];
r1 = t1 + c[0];  // c needs to be shuffled

Generated instructions:
  vload   v0, [addr_c]          ; load <c[0], c[1]>
  vshuffle v1, v0, <1, 0>       ; shuffle to <c[1], c[0]>
  vadd    v2, v_t, v1           ; vector add
```

### 11.6 Complete Code Generation Example

**Input**:
```c
void example(int *a, int *b, int *d) {
    d[0] = a[0] + b[0];
    d[1] = a[1] + b[1];
}
```

**Generated Vector Assembly**:
```asm
example:
  vload   v0, [a]       ; v0 = <a[0], a[1]>
  vload   v1, [b]       ; v1 = <b[0], b[1]>
  vadd    v2, v0, v1    ; v2 = <a[0]+b[0], a[1]+b[1]>
  vstore  v2, [d]       ; store to d[0], d[1]
  ret
```

---

## 12. Summary

### 12.1 Algorithm Flow Summary

| Phase | Input | Output | Key Operations |
|-------|-------|--------|----------------|
| Seed Discovery | Basic Block | Initial Packs | Find consecutive memory ops |
| Pack Extension | Seeds | All Packs | Follow use-def chains |
| Legality Check | Packs | Legal Packs | RAW dependence, alias, contiguity |
| Cost Model | Legal Packs | Profitable Packs | Compare scalar vs vector cost |
| Scheduling | Profitable Packs | Ordered Packs | Topological sort |
| Code Generation | Ordered Packs | Vector Instructions | Generate vector code |

### 12.2 Key Design Decisions

1. **Seed Selection**: Prefer stores over loads for cleaner expansion
2. **Extension Direction**: Bottom-up (use-def) is primary
3. **Commutative Handling**: Reorder operands to enable packing
4. **Pack Splitting**: Split oversized packs to fit VLEN
5. **Cost Model**: Conservative - only vectorize when clearly profitable
6. **Legality**: Check RAW dependence, alias, and contiguity

### 12.3 Future Improvements

- **VW-SLP**: Variable width for different packs
- **Graph-based SLP**: Global optimization of pack selection
