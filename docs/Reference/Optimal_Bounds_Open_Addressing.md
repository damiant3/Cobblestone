# Optimal Bounds for Open Addressing Without Reordering

Farach-Colton, Krapivin, Kuszmaul. arXiv 2501.02305, January 2025.
cs.DS / math.CO.

PDF: https://arxiv.org/pdf/2501.02305

## Why This Matters

Open-addressing hash tables that never move an element after insertion.
Since 1985 (Yao, "Uniform Hashing is Optimal"), the community believed
worst-case expected probe complexity for greedy open addressing was
Θ(δ⁻¹) where δ = 1 − load factor. This paper disproves that conjecture
and gives tight bounds for both greedy and non-greedy non-reordering
schemes.

For Codex: any future hash table in the runtime or compiler is
bump-allocated with no GC. Reordering elements means copying, which
means either double the memory or a stop-the-world pause. A scheme
that never reorders and still achieves O(1) amortized probes is
directly applicable.

## Key Results

### Elastic Hashing (non-greedy, non-reordering)

- Amortized expected probe complexity: **O(1)**
- Worst-case expected probe complexity: **O(log δ⁻¹)**
- Worst-case expected insertion time: **O(log δ⁻¹)**

This is optimal — matching lower bound Ω(log δ⁻¹) for any
non-reordering scheme (Theorem 5).

Prior belief: O(1) amortized probes required reordering (Robin Hood,
cuckoo). This shows reordering is unnecessary.

### Funnel Hashing (greedy, non-reordering)

- Worst-case expected probe complexity: **O(log² δ⁻¹)**
- High-probability worst-case: **O(log² δ⁻¹ + log log n)**
- Amortized expected probe complexity: **O(log δ⁻¹)**

Greedy means: each element is placed during its own insert, no
deferred decisions. This directly disproves Yao's conjecture.

### Yao's Conjecture (1985) — Tightened

Conjecture: any greedy open-addressed hash table must have worst-case
expected probe complexity at least (1 − o(1))δ⁻¹.

Funnel hashing achieves O(log² δ⁻¹) = o(δ⁻¹), showing Yao's specific
bound was too pessimistic. But the structural insight survives: greedy
IS provably constrained. The tight greedy lower bound is Ω(log² δ⁻¹)
vs Ω(log δ⁻¹) for non-greedy — greedy is worse by a log factor.
Yao overclaimed on the exact floor, not on the fundamental limitation
of greedy strategies.

### Lower Bounds (tight)

| Scheme class | Worst-case expected | Amortized expected |
|---|---|---|
| Non-reordering | Ω(log δ⁻¹) | — |
| Greedy | Ω(log² δ⁻¹) | — |
| Greedy high-prob | Ω(log² δ⁻¹) w.p. > 1/2 | — |

## Comparison to Classical Schemes

| Scheme | Amortized | Worst-case | Reorders? |
|---|---|---|---|
| Linear probing (Knuth 1963) | O(δ⁻²) | O(δ⁻²) | No |
| Uniform probing (Yao 1985) | O(log δ⁻¹) | O(δ⁻¹) | No |
| Robin Hood | O(1) | O(log n) | Yes |
| Cuckoo | O(1) | O(1) | Yes |
| **Elastic (this paper)** | **O(1)** | **O(log δ⁻¹)** | **No** |
| **Funnel (this paper)** | **O(log δ⁻¹)** | **O(log² δ⁻¹)** | **No** |

## How They Work

### Elastic Hashing

Partition the array into hierarchically halving subarrays
A₁, A₂, …, A_{⌈log n⌉} where |A_{i+1}| = |Aᵢ|/2 ± 1.

Two-dimensional probe sequence: hash functions h_{i,j}(x) mapped to 1D
via injection φ(i,j) ≤ O(i·j²). This separates insertion cost from
search cost — an element placed via many insertion probes still has a
short search probe sequence.

Insertions organized into batches maintaining load balance across
levels. Three cases per insertion depending on free space in the
current and next level. The rare "both levels nearly full" case uses
uniform probing and occurs with probability O(1/|Aᵢ|²).

### Funnel Hashing

Split the array into α = O(log δ⁻¹) levels with geometrically
decreasing sizes (ratio 3/4), plus a special final array.

Each level subdivided into subarrays of size β = O(log δ⁻¹). Insertion
attempts each level sequentially with up to β probes per attempt.

If all α levels fail, the final array uses two-part fallback:
- Part B: uniform probing with log log n probe limit
- Part C: two-choice hashing with buckets of size 2 log log n

This is greedy — each element placed on insert, no deferred work.

## Proof Techniques

- Inductive argument for greedy lower bound: some insertion at load
  factor (1 − 2⁻ⁱ) must cost Ω(i log δ⁻¹)
- Non-reordering lower bound via disjoint slot sets that each appear
  in the first 2c probes with probability ≥ 1/16
- Probabilistic load-balancing analysis for the batch structure
- Two-choice hashing analysis for the funnel fallback tier
