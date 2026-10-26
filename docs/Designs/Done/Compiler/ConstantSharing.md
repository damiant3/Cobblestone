# Constant Sharing (COMPILER-86)

Status: stage 1 built with record fields (blu, 2026-09-23); stage 3 built for the zig,
arm64, riscv and wasm plugs (reek, 2026-09-23 and 2026-09-24); stage 2 is not built, by design (L-LESS).

## The problem

A zero-parameter definition is emitted as a nullary function, and every
mention of it is a call (`emit-name-as-call`, `Emit/X86_64.codex`). A
`List` constant is therefore rebuilt, and its memory kept, at every
reference. GitHub issue 157 (Steve Howell): a 64-entry table read 1,000,000
times retains about 520 MB under the zig plug against 3.6 MB when bound
once; reek measured x86-64 bare metal stopping with `OUT OF MEMORY` at
10,000,000 reads. `sha256-k` is rebuilt once per SHA-256 compression.

## The constraint

Sharing one instance of a constant is correct only if nothing writes it,
and in-place writing is a documented contract, not a defect:

- `list-set-at` stores in place and returns the same list
  (`DevelopersGuide.md`, "`list-set-at` mutates in place").
- `sha256-compress` writes into the hash list it is handed, and a digest
  survives a `__heap-save` / `__heap-restore` rewind because of it
  (`DevelopersGuide.md`, "`sha256-compress` writes into the hash list").
  `sha256` passes it `sha256-h0`.
- riscv once memoised every zero-parameter def and `sha256` answered
  correctly once per program (`plugs-backlog.md` 1.100).

Copy-on-write is therefore NOT available: a copy made at the first write
moves that allocation to after a caller's heap mark, and the rewind
reclaims it. Sharing has to be decided per constant, before emission, and
a shared constant must be one that no execution can write.

## The rule

**A constant is shared if and only if no reference to it can reach a
writer.** The compiler sees the whole program as one unit, so the question
is decidable conservatively over the IR:

A value position is SAFE when the value in it is only read:

1. the list argument of a reading builtin that stores nothing through it
   and returns no alias of it: the first argument of `list-at` and
   `list-length`, and the third of `__buf-write-bytes`, whose helper
   `__buf_write_bytes` (`Emit/X86_64Helpers.codex`) loads the list through
   `r12`, stores only through the destination `rbx`, and returns the new
   offset;
2. an argument to a direct, fully applied call of a top-level definition,
   at a parameter index whose parameter is SAFE;
3. the bound value of an `IrLet` whose name is SAFE in the let body;
4. either operand of list `&`. The x86 emitter lowers every list `&`
   through `emit-append-list` to `__list_concat_many`, which allocates a
   fresh list and only loads its inputs; the LIR path takes integer
   operators only, and the in-place tail-call accumulator
   (`is-inplace-append`) applies to Text `&` alone. `__list_append`,
   which returns its left operand when the right is empty, is reached by
   no list `&`. Text `&` stays UNSAFE on both sides.

5. the value of a field `f` in a record literal, when field `f` is SAFE,
   and a read `r.f` in any of positions 1 to 4 or as the value of another
   SAFE field.

Rules 1 and 4 are claims about the x86-64 runtime; a backend that shares
(stage 3) must re-prove each against its own helpers.

Every other position is UNSAFE: a writer's argument, a return value, a
branch result, a list or constructor field, a value stored by
`r.f = v` or `__record-set`, a lambda capture, a partial application, a
call through a function value, an effect or act statement, and every
builtin not on the reading list.

A field `f` is a slot keyed by the field NAME, across every record type.
The IR wire spells a read `f/index` and a literal's field `f`
(`ir-field-with-index`, `Emit/IRTextEmitter.codex`), so the key is the
text before the slash; a key that missed the reads would ignore every
read and share a written table. A field is SAFE when every `r.f` in the program is in a SAFE position. A
record field is read by no IR form other than `IrFieldAccess` (there is no
record pattern), and the runtime's own record walkers (`show`, equality,
the copiers) only load. Raw memory is outside the analysis: `address-of`
a record, then `poke` through a pointer read from it, can write a shared
table, as it can write any other memory. Only a field that a record
literal fills with a tracked name gets a slot; a collect pass over the IR
registers those names before the real pass, and a read of any other field
is ignored because no tracked value can be in it.

A parameter or let name is SAFE when every occurrence of it is in a SAFE
position. Parameter safety is the greatest fixpoint: start every
parameter SAFE and propagate UNSAFE backwards along the "passes its
value to" edges until a pass changes nothing. The passes are capped at
64, and reaching the cap shares nothing, because stopping early would
leave a parameter SAFE that is not. A recursive reader
(`sha256-rounds` passes `k` to itself and reads it with `list-at`) stays
SAFE; any path to a writer makes every parameter upstream of it UNSAFE
(`sha256-h0` reaches `list-set-at` through `sha256-process-blocks` and
`sha256-compress`, so it keeps its per-reference build).

Only the candidate constants, parameters, and let names bound to one of
them are tracked, so an edge exists only where a tracked value is passed
as an argument. An inner binding of the same name (let, lambda, pattern,
act bind) masks the outer one; a masked head is never resolved as a
top-level call.

## Stage 1: literal Integer tables in the image (built)

A shared constant whose body is an `IrList` of `IrIntLit` elements is
emitted once, into the data segment beside the text literals, as
`[capacity = length][length][e0][e1]...`, and every reference loads its
address through the same rodata fixup `emit-text-lit` uses. It never
enters the heap, so no rewind can reclaim it, and it costs nothing at a
reference.

`capacity = length` with the block outside the heap and the deck sends
every copying writer (`__list_snoc`, `__list_insert_at`, `__list_append`)
to its copy path by arithmetic, the same argument that protects a list
view. The analysis keeps `list-set-at` from ever receiving it.

Measured 2026-09-23 over `codex/` and `apps/` with a source pattern that
sees only single-signature, flat-bracket definitions: 1,090 of 1,684 such
List constants are literal all the way down, and 338 of those have 16 or
more elements (the AES S-boxes, the Brotli and CCE tables, `sha256-k`).
On the compiler's own unit the analysis shares 38 of 41 literal Integer
candidates (measured 2026-09-23, rules 1 to 5), among them `x86-ret`, the
port I/O byte sequences, `sha256-k`, the CCE tables and `fe-one` /
`fe-zero` through the `Ge` fields. On `codex/test/ecdsa-p256` it shares
11 of 12, all seven P-256 `ec-*` tables through the `EcField` fields; the
twelfth is `sha256-h0`, a real writer.

## Later stages, not built

- Stage 2, a shared constant with a computed body, is NOT BUILT, by design
  (L-LESS). Built once at run time it would need an arena that survives
  every `__heap-restore`, `phase-compact` and REPL reset, a nesting-tolerant
  lock for its first build on SMP, and a hosted fallback, and every computed
  table measured is referenced once or twice per program.

## Stage 1 proof

`codex/test/const-share` carries both directions: a shared table read directly and through a helper parameter reports a heap delta of 0 (the pre-stage-1 compiler reports 528 bytes per read), and four constants written directly, through a function, through a let alias and through a lambda capture still read their original value on the next reference. Sharing every candidate regardless of the analysis turns all four written arms red, so the arms can fail. Two arms cover the widened reads: a table written into a byte buffer by `__buf-write-bytes` in a loop reports a heap delta of 0 (8,000,000 bytes over 100,000 writes before the widening), and a table used as the right operand of `[] & t` and then written through the result still reads its original value. Rule 5 has four arms: a table stored into a record field through a constructor parameter and read through the field directly, through a helper and through a let reports a heap delta of 0 (4,800,000 bytes over 100,000 reads before rule 5), and three tables written through a field directly, through a function and through a let alias still read their original value. Dropping the edge from a record literal's value to its field slot turns all three written arms red (99099 against 99001). For the left operand of `&`, a table used once as `t & [7]` and read in a loop reports a heap delta of 0 (8,000,000 bytes before), and a table written through `w & []`, the empty-right case, still reads its original value.

R-COST: the analysis is two IR walks, a collect pass that registers the
field names a record literal fills with a tracked name (one cell per such
field) and the pass that builds edges, allocating one cell per binding
and nothing per application, plus at most 64 passes over the slots; the
data segment grows by the shared tables' size once. The
compiler's self-compile went from 8.2 s to 7.6 s wall (2026-09-23).

## Stage 3: a plug runs the analysis itself (built for zig, arm64, riscv and wasm)

`shared-const-names` is a function of the `IRDef` list, and every plug
already parses its IR into that list, so a plug bundles `IR/ConstShare`
and asks the same question over its own definitions. Nothing crosses the
IR wire and the seed does not change. `Build-TranspilerPlug` and
`build-plug-wasm.ps1` take `-CompilerChapters` for this, and the page
manifest carries it as a row's `compiler` field. `ConstShare` calls
`sort-by` without citing `Sort`, so the plug chapter that calls
`shared-const-names` must cite `Foreword chapter Sort`, or the bundle fails
with CDX3002 on `sort-by`.

The zig plug emits a shared table as a function-local static with
`capacity = length`, and every reference returns its address:

    fn t() *CxList(i64) {
        const cx_s = struct {
            var cx_a = [_]i64{ 1, 2, 3 };
            var cx_l = CxList(i64){ .items = .{ .items = &cx_a, .capacity = 3 } };
        };
        return &cx_s.cx_l;
    }

Rules 1 and 4 re-proved against the zig prelude: `cx_list_at` and
`cx_list_len` only read; `cx_buf_write_bytes` reads `vs.items` and stores
only into `cx_heap_mem`; every list `&` is `cx_ll_concat`, which copies
both operands into a fresh list. A writer that reached a static block anyway would go to
`cx_reserve`, whose in-place resize applies only at the heap frontier, so
it copies or stops on a safety check rather than writing the table.

Proof (seed `1E936E74D45C317D`): `codex/test/const-share` matches its
`.expected` through the zig plug, heap deltas 0; the plug before this
change reports 53,600,002 and 107,200,002, and a plug that shares every
candidate turns the written arms red (99099 against 99001). Rule 5's
arms match through the zig plug too, and a field key that keeps the
`/index` suffix turns its three written arms red there. In the x25519
test `sha256-k` is shared and `sha256-h0` is not. The 10,000,000-read
reproducer runs in 0.12 s.

The arm64 plug places a shared table in the wire's data block beside the
text literals as `[capacity = length][length][e0]...`, the layout
`list-empty` gives a heap list, and the def becomes `adr x0, <length word>;
ret`. Data offsets are `list-length` of the data block, so text literals
after a table land after it. Rules 1 and 4 re-proved against the arm64
runtime: `a64-rt-list-at` and `a64-rt-text-length` (which `list-length`
uses) only load; `a64-rt-buf-write-bytes` loads the list through `x2` and
stores only at `x0 + x1`; every list `&` calls `a64-rt-list-append`, which
allocates at `x28` and only loads both operands. The LIR path reaches lists
only through calls to those helpers. `list-push` on a table sees capacity ==
length and copies.

Proof (seed `F4A63F088E61A9F1`, QEMU): `codex/test/const-share` matches its
`.expected`; the depot plug reports 107,200,000 (direct) and 214,400,000
(helper) bytes over 100,000 reads, and a plug that shares every candidate
turns exactly the seven written-through arms to 99099 while `[] & t` and
`t & []` stay 99002. `ecdsa-p256` and `ecdsa-p256-sign` pass, with
`sha256-k` and the seven `ec-*` byte tables emitted as 8-byte stubs and
`sha256-h0` unshared.

The riscv plug does the same with `auipc a0; addi a0` and `ret`; its data
offsets were already `list-length` of the data block. Rules 1 and 4 hold on
its runtime: `rv-rt-list-at` and `rv-rt-text-length` only load;
`rv-rt-buf-write-bytes` loads the list through `a2` and stores bytes only at
`a0 + a1`; `__list_append` allocates at `s1` and only loads both operands;
`list-push` grows by copying when length reaches capacity. A shared table
never reaches the riscv memo, which stays scalar-only. Proof (same seed,
QEMU): `const-share` matches; the depot plug reports 108,800,000 and
217,600,000 bytes; share-everything moves the same seven arms to 99099.
`ecdsa-p256` passes with `sha256-k` and the `ec-*` byte tables as 12-byte
stubs and `sha256-h0` unshared; `ecdsa-p256-sign` starves at the default
ceiling on both plugs and passes at 120 s (`plugs-backlog.md` 2.72).

The wasm plug's list is `[i32 length][i32 capacity][e0]...` with the pointer
at the header. A shared table is a `data` segment placed 8-aligned after the
function-arity bytes, `heap_start` moves past the last table (and does not
move when no table is shared, so a table-free module is unchanged), and the
def becomes `(func $t (result i64) (i64.const <offset>))`. Rules 1 and 4 hold
on its runtime: `$list_length`, `$list_at` and `$buf_write_bytes` only load
the list; `$list_append` allocates with `$bump_alloc` and only loads both
operands; `$list_push` stores in place only when length < capacity; and
`$cx_list_base` answers the pointer itself because a table's capacity is not
the view marker -1. Proof (same seed, wasmtime through
`hosted-wasm-test.ps1`): `const-share` passes; the depot plug reports
52,000,000 and 104,000,000 bytes; share-everything moves the same seven arms
to 99099. `ecdsa-p256` passes with `sha256-k` and the `ec-*` tables as
constants and `sha256-h0` unshared. spark, fireworks and starmap, which write
at fixed linear-memory addresses from 131072 up, share no table, so their
`heap_start` stays near 71.5 KB.
