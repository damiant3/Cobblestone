# The shell DSL is unreadable, and ScRaw is deprecated

*Owner: reek (Damian, 2026-08-16). The generators are this campaign's claim;
`deck-headroom` stays fester's.*

The complaint is one symptom with three independent causes, which have
different fixes and different costs.

| # | Cause | Shape |
|---|---|---|
| 1 | Walls: whole definitions on one line | `cdxtopeScript.codex` is 87 KB in 24 lines |
| 2 | `ScRaw`: raw shell text smuggled through the AST | the live count is `build/shell-raw-baseline.txt` |
| 3 | Escaping: quoted shell inside a quoted Codex string | |

`ShellBuild`'s `sh-` helpers are **aliases, not abstraction**:
`sh-assign (name) (value) = ScAssign name value` renames a constructor and
removes nothing. Adding more of those makes the vocabulary bigger and the
scripts no shorter.

## 1. Why the walls exist, and it is not sloppiness

**`CDX1070` forbids an application whose arguments continue on the next line**,
so any constructor taking a block (`ScIf`, `ScFor`, `ScWhile`, `ScTry`) must
have its entire subtree on one line **or have the block named**. An author who
will not name the block gets a wall. Inlining and wall-making are the same act.

Three layouts compile:

```
flat, leading comma            [ ScSetStrictMode
                               , ScAssign "Stage0" (SeVar "Kernel")
                               ]

nested, block named above      missing-kernel : List ShellCmd = [ ... ]
                               ...  , ScIf cond missing-kernel []

nested, block let-bound        let guard = [ ... ] in [ ..., ScIf c guard [] ]
```

**The style rule: name the block.** It is what `CDX1070`'s own message tells you
to do, it is what makes the structure visible, and it costs one definition with
a name that says why the block exists. There is no formatting-only fix.

## 2. ScRaw is deprecated

`ScRaw (Text)` passes shell text through the AST untouched. A raw `if (...) {`
and a matching `}` are two unrelated leaves, so the AST does not know a block
exists: nothing can indent it, no other emitter can target it, and a reader
cannot see the nesting. It also drags cause 3 along, because the shell text has
to be re-quoted inside a Codex string.

**The portability it defeats is currently theoretical, which is an argument for
fixing it now rather than an argument that it does not matter.** Every generator
cites `PowerShellEmit`; one cites `BashEmit` and the gate reports that one's
target does not exist. `KshEmit` is cited only by `foreword-all-compile` and by
`shell-text-quoting`, which are tests. Three emitters exist and one is
load-bearing, so `ScRaw` has been free.

**Comment payloads are convertible and buy nothing** (measured 2026-08-24). All
three emitters render a comment and a raw line the same way: `ScComment` is
`pad & "# " & text` and `ScRaw` is `pad & text`, so `ScRaw "# x"` and
`ScComment "x"` are byte-identical everywhere today. The conversion moves the
count by about a tenth of the corpus and changes nothing a reader, an emitter or
a target can observe; its only value is a target that does not exist yet, one
whose comment marker is not `#`. **Not taken**, and named here so the next owner
does not take it for the count.

**Do not rank a generator by its raw SHARE.** What converts is not "raw payloads
with a constructor that does the job" but the narrower **"raw payloads whose
text a constructor already reproduces character for character"**. `CompileScript`
is what the narrow set looks like when it is large, because those payloads were
literally what the constructors emit; a hashtable literal, and every cause-1
wall, are not. **Sizing that convertible fraction is unmeasured, and it is the
number that says what this campaign can deliver.** Nobody should take another
generator until it exists.

## 3. The ratchet

`build/check-shell-raw.ps1` is the runner. Prose was not: `ScRaw` was marked
DEPRECATED in `ShellTypes` prose at main 15606 and the count then rose for three
weeks, because nothing read the prose. A campaign whose method is conversion
cannot finish while addition outruns conversion.

`-List` prints the per-generator table, `-Update` writes
`build/shell-raw-baseline.txt`, and the bare form compares. **It fails a RISE and
it also fails a FALL that leaves the record high**, because a baseline left high
after a conversion permits the raw node straight back; the fix for that failure
is `-Update` in the changelist that did the conversion. It boots nothing, so the
cost of running it is a file read.

**A lane moves its own row: `-Update -Only <generator>` rewrites one row and
leaves every other row as the record holds it.** A blanket `-Update` absorbs
every lane's rise at once, so the row then reads as the wrong lane's debt and
the ratchet has granted permission for uses nobody justified. `-Only` takes the
generator's base name (`bvtScript`), refuses a name no generator carries, and
refuses without `-Update`, because a flag that quietly does nothing is
L-ACCEPTED in this check's own lane.

## 4. Why this refactor is unusually safe

`check-generated-scripts.ps1` recompiles each generator and diffs the emission
against the shipped `.ps1`. **`match / 0 drift` after a change is proof the
emitted script is byte-identical**, so behaviour cannot silently move. It
defaults its kernel to the DEPOT seed and refuses a `build-output` binary whose
digest differs from it, because a drift table is a statement about the compiler
that produced it.

That is a stronger oracle than most refactors get, and it is what makes an
aggressive restructure defensible. It does NOT cover readability, which has no
runner. **A naive reader on the after-form is owed before the bulk conversion**
(R-NAIVE); the author of a style cannot judge whether it reads better to someone
who does not already know what the script does.

## 5. The vocabulary

`SeText`, with `msg` as its builder, is the interpolating string node. `SeLit`
is emitted SINGLE-quoted by `ps-quote`, so it cannot carry an interpolated
`$Var`, and roughly every message in the tree has one. There was no interpolating
node at all, so the author had no option but `SeRaw` with a hand-quoted string
inside a Codex string: **the escapes were a missing node, not laziness.**
`codex/test/apps/shell-text-quoting` pins what all three emitters produce,
including the escaping ORDER, which is the part that is easy to get backwards:
the escape character has to be doubled before the quote is escaped, or the
second pass escapes what the first inserted. The dollar arm is a control in the
strict sense, because interpolation escaped away would make the node pointless.

`need-file`, `set`, `if-set`, and `SePathJoinN` with `join-path` are landed
beside it. `SePathJoin` is BINARY and renders parenthesized, so a builder
folding over it emits `(Join-Path (Join-Path a b) c)` where the raw sites spell
`Join-Path a b c` flat: `SePathJoinN (List ShellExpr)` is the node that does not
drift.

**`ShellExpr` has about three points of deck slack, which is roughly four more
constructors.** A new constructor widens every `when` over the type in three
emitters, and each measured 4.75 MB of check deck; `demand-check-floor` was
raised 648 to 704 MB (ruling 20, red) to buy that slack, on the note that new
machinery managing a cost smaller than itself is L-LESS unless the bump recurs a
third time. **Measure with `deck-headroom.ps1 -Quire codex\build -WithSelf
-Fresh` before and after adding one**, rather than assuming the headroom absorbs
it. `deck-scale-min` / `deck-scale-margin` / `deck-scale-anchor` belong to
`ProportionalDecks.md` and are not this campaign's to move.

## 6. Before and after, on real code

`CompileScript.codex:18` as it was, 206 characters on one line:

```
    let given = [ScAssign "Stage0" (SeVar "Kernel"), ScIf (SeNot (SeFileExists (SeVar "Stage0"))) [ScWriteError (SeRaw "\"MISSING: $Stage0 - the -Kernel you asked for is not there\""), ScExit (SeInt 2)] []]
```

The same intent under the rules above:

```
 The -Kernel the caller asked for. It must exist before anything else runs.

  kernel-given : List ShellCmd =
    [ set "Stage0" (var "Kernel")
    , need-file (var "Stage0") 2 (msg "MISSING: $Stage0 - the -Kernel you asked for is not there")
    ]
```

Two lines of body, no escaping, and the `if` that was structure became a name.

## 7. What is open

**Step 3, convert generators, one CL each.** The loop: change the generator, run
`build/check-generated-scripts.ps1 -Diff <name>` until the diff is empty, then
`check-shell-raw.ps1 -Update -Only <name>` in the same changelist, then gate.
Take one at a time; **do not sweep**, because the diff is the reviewable unit
and a whole-tree pass is unreviewable. Splitting `g01` into
`g01-head & g01b & g01-tail` is the worked example for a wall, and it cost
nothing in emission.

**The bare `Join-Path` set, and it is a decision rather than a mechanical step.**
Re-measured 2026-09-08 over the raw payloads in `codex/build/*Script.codex`:

| shape | sites |
|---|---|
| payload begins `(Join-Path ` | 19 |
| payload begins `Join-Path ` bare | 123 |
| `Join-Path` embedded further in | 253 |

**120 of the 123 bare payloads are DIRECTLY the right-hand side of an
`ScAssign`.** A statement-level assign-a-path command emits `$X = Join-Path a b c`
with no parentheses, byte-identical to what ships, and converts 120 with the
oracle still proving the change. The alternative, a one-time reviewed
reparenthesization, spends that oracle instead: 123 lines of intended drift
cannot be told from a mistake by `match / 0 drift`, and
`check-generated-scripts.ps1` has no `-Write` precisely so the shipped script
stays the maintained side. The embedded bucket is untouched by either option and
is where most of the `Join-Path` text now lives.

**Step 4, the cmdlet residue, is last and needs a node designed against real
call sites.** `g01b`'s body is `ScRaw` on purpose: an ordered hashtable literal,
a generic HashSet and a `$(if ...)` subexpression have no constructor. Do not
force it, and do not start it until step 3 is done and the residue can be
measured against what is actually left.
