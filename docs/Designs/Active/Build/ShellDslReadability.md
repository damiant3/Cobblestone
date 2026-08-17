# The shell DSL is unreadable, and ScRaw is deprecated

*Opened 2026-08-16 by reek at Damian's direction, after he read the last
update's changes and could not read the generators: "the ScXXX stuff. totally
unreadable to me. its big walls of text."*

Every number below was measured on 2026-08-16 against main 15586. Re-measure
before acting on any of them (L-COUNT); they will all move as the work lands.

## 1. What is actually wrong, separated

The complaint is one symptom with three independent causes. They have different
fixes and different costs, and conflating them is why "make it nicer" has no
obvious first move.

| # | Cause | Size |
|---|---|---|
| 1 | Walls: whole definitions on one line | `cdxtopeScript.codex` is 87 KB in **24 lines**, 3.6 KB per line |
| 2 | `ScRaw`: raw shell text smuggled through the AST | **5,504 `ScRaw` + 570 `SeRaw`** across **35 of 56** generators |
| 3 | Escaping: quoted shell inside a quoted Codex string | **1,707** backslash-quote sequences |

A fourth thing is not wrong but is worth stating, because it looks like the fix
and is not: `ShellBuild`'s 40 `sh-` helpers are **aliases, not abstraction**.
`sh-assign (name) (value) = ScAssign name value` renames a constructor and
removes nothing. Adding more of those makes the vocabulary bigger and the
scripts no shorter.

## 2. Why the walls exist, and it is not sloppiness

**`CDX1070` forbids an application whose arguments continue on the next line.**
Measured directly: this is a parse error,

```
    , ScIf (SeNot (SeFileExists (SeVar "Stage0")))
        [ ScWriteError (SeRaw "\"MISSING\"")
        , ScExit (SeInt 2)
        ]
        []
```

> `error CDX1070: Application ended at newline; '[' is not parsed as an
> argument. Place all arguments on one line, wrap the application in
> parentheses, or bind the value with a let.`

So any constructor taking a block (`ScIf`, `ScFor`, `ScWhile`, `ScTry`) must
have its entire subtree on one line **or have the block named**. An author who
will not name the block gets a wall. That is the whole mechanism.

`Build.md` already records the same wall arriving from the other direction:
fully inlining `ablatedoctrineScript` produced *a single 25,800-character
line*. Inlining and wall-making are the same act.

**Three layouts DO compile, and all three were verified before being
recommended here:**

```
flat, leading comma            [ ScSetStrictMode
                               , ScAssign "Stage0" (SeVar "Kernel")
                               ]

nested, block named above      missing-kernel : List ShellCmd = [ ... ]
                               ...  , ScIf cond missing-kernel []

nested, block let-bound        let guard = [ ... ] in [ ..., ScIf c guard [] ]
```

**The style rule that follows: name the block.** It is what `CDX1070`'s own
message tells you to do, it is what makes the structure visible, and it costs
one definition with a name that says why the block exists.

## 3. ScRaw is deprecated

`ScRaw (Text)` passes shell text through the AST untouched. It was the cheat
early agent sessions reached for, and it spread: **51.3% of every statement in
the generators is now `ScRaw`.**

It is not merely inelegant. A raw `if (...) {` and a matching `}` are two
unrelated leaves, so the AST does not know a block exists: nothing can indent
it, no other emitter can target it, and a reader cannot see the nesting. It
also drags cause 3 along with it, since the shell text has to be re-quoted
inside a Codex string.

**The portability it defeats is currently theoretical, which is an argument for
fixing it now rather than an argument that it does not matter.** 56 generators
cite `PowerShellEmit`; exactly one cites `BashEmit`, and the gate already
reports that one's target does not exist (`testrunBashScript.codex ->
build\test-run.sh`). `KshEmit` is cited by nothing outside the compile-all
test. There are no `.sh` files under `build/`. Three emitters exist and one is
load-bearing, so `ScRaw` has been free so far.

### What the 5,501 payloads actually contain

| Category | Share | Replacement |
|---|---|---|
| cmdlet / pipeline | 25.9% | `ScRun`, `ScRunArgs`, `ScRunCapture`, `ScPipeDo` cover most; the residue is the only place a NEW node may be needed |
| assignment | 20.0% | `ScAssign` |
| `if` / brace | 19.5% | `ScIf` |
| comment | 18.9% | `ScComment` |
| `Write-*` | 3.6% | `ScEcho`, `ScWriteError` |
| loop | 3.3% | `ScFor`, `ScForEach`, `ScForLoop`, `ScWhile` |
| function / param | 2.9% | `ScFunction` |
| .NET type call | 2.7% | `ScDotNetCall`, `ScCallMethod` |
| control | 2.5% | `ScExit`, `ScReturn`, `ScThrow`, `ScBreak` |
| blank / try | 0.7% | `ScBlank`, `ScTry` |

**Roughly 70% of `ScRaw` uses have a constructor that already exists.** That
part is not a design problem, it is debt with a known answer. Only the cmdlet
residue may need a new node, and it should be designed against real call sites
rather than invented.

## 4. The catalog

Tier 1 is mechanical. Tier 2 needs a node designed first. Take a generator at a
time; **do not sweep**, because the diff is the reviewable unit and a whole-tree
pass is unreviewable.

| Generator | ScRaw | SeRaw | raw share |
|---|---|---|---|
| `cdxtopeScript` | 1180 | 0 | 93% |
| `testScript` | 793 | 1 | 78% |
| `vmconfigScript` | 615 | 0 | 92% |
| `BuildScript` | 527 | 27 | 61% |
| `buildimgScript` | 483 | 0 | 89% |
| `comparecodexsemanticScript` | 401 | 19 | 79% |
| `quiremapScript` | 303 | 0 | 90% |
| `plugbuildlibScript` | 195 | 0 | 92% |
| `compileriscvScript` | 84 | 38 | 45% |
| `bvtScript` | 100 | 21 | 49% |
| `CompileScript` | 32 | 83 | 18% |
| `buildarm64imgScript` | 84 | 23 | 46% |
| `checkvmdifferentialScript` | 101 | 0 | 86% |
| `compilearm64Script` | 72 | 29 | 41% |
| `concatcodexselfScript` | 86 | 14 | 57% |
| `testcrossScript` | 48 | 50 | 27% |
| `bootarm64Script` | 49 | 21 | 36% |
| `runplugScript` | 41 | 23 | 39% |

The remaining 17 affected generators hold fewer than 64 raw sites each.

**`quiremapScript` is the one to do first** and it is not the biggest. It is
90% raw and almost all of it is a single PowerShell hashtable literal written
one `ScRaw` line per entry, so it is the clearest case of the AST being used as
a string list, and it converts to one table plus one reader with no new nodes.

## 5. Why this refactor is unusually safe

`check-generated-scripts` recompiles each generator and diffs the emission
against the shipped `.ps1`. **`match / 0 drift` after a change is proof the
emitted script is byte-identical**, so behaviour cannot silently move.
`Build.md` states this and the baseline file is empty: measured 2026-08-15, 55
generators, 0 drifted.

That is a stronger oracle than most refactors get, and it is what makes an
aggressive restructure defensible. It does NOT cover readability, which has no
runner and is exactly the kind of claim that rots; the guard against that is
`R-NAIVE`, a reader who does not already know what the script does.

## 6. The vocabulary, sized rather than guessed

Every proposal below is justified by a count, not by taste.

| Proposed | Replaces | Sites today |
|---|---|---|
| `need-file expr code msg` | `ScIf (SeNot (SeFileExists ...)) [ScWriteError ..., ScExit ...] []` | 67 negated file-exists, 29 error-then-exit pairs |
| `msg "..."` **LANDED** | a Codex string containing an escaped shell string | **346**, not 1,707; see the correction below |
| `join-path [parts]` | `SeRaw "Join-Path ..."` | 350 |
| `set name expr` | `ScAssign` | 858 |
| `if-set name a b` | `ScIf (SeVar name) a b` | part of 518 `ScIf` |

**CORRECTION, 2026-08-16, and it is this document's own number.** The row above
first said `msg` was worth 1,707 escaped quotes. That is the tree's total and
it is not what `msg` addresses. Split by where the escapes actually live:

| Where | Count | Fixed by |
|---|---|---|
| inside `ScRaw` statement payloads | **1,200** | tier 1; they go when the statement converts |
| inside `SeRaw` expression payloads | **346** | `msg`, directly |
| elsewhere | 161 | neither, yet |

The 1,707 total is still the largest readability tax and still worth naming;
attributing all of it to one builder was wrong, and a wrong number in a plan is
how the wrong thing gets built first.

**`msg` is LANDED**, and it turned out to close a real hole rather than add a
convenience. `SeLit` is emitted SINGLE-quoted by `ps-quote`, so it cannot carry
an interpolated `$Var` -- and roughly every message in the tree has one, 123 of
the 127 standalone cases. There was no interpolating node at all, so the author
had no option but `SeRaw` with a hand-quoted string inside a Codex string. The
escapes were a missing node, not laziness, which is the one part of the
original diagnosis that was wrong.

`SeText` is that node, with `msg` as its builder. `codex/test/apps/
shell-text-quoting` pins what all three emitters produce, including the
escaping ORDER, which is the part that is easy to get backwards: the escape
character has to be doubled before the quote is escaped, or the second pass
escapes what the first inserted. The dollar arm is a control in the strict
sense -- if interpolation were escaped away the node would be pointless.

## 7. Before and after, on real code

`CompileScript.codex:18`, unedited, 206 characters on one line:

```
    let given = [ScAssign "Stage0" (SeVar "Kernel"), ScIf (SeNot (SeFileExists (SeVar "Stage0"))) [ScWriteError (SeRaw "\"MISSING: $Stage0 - the -Kernel you asked for is not there\""), ScExit (SeInt 2)] []]
```

The same intent, under the rules above:

```
 The -Kernel the caller asked for. It must exist before anything else runs.

  kernel-given : List ShellCmd =
    [ set "Stage0" (var "Kernel")
    , need-file (var "Stage0") 2 (msg "MISSING: $Stage0 - the -Kernel you asked for is not there")
    ]
```

Two lines of body, no escaping, and the `if` that was structure became a name.

## 8. Sequencing, and who owns it

1. Deprecate `ScRaw` in `ShellTypes` prose so no new use is added. **Cheap, do
   it first**, or the catalog grows while it is being worked.
2. Add the section-6 vocabulary to `ShellBuild`, with arms.
3. Convert generators one at a time, `quiremapScript` first, each its own CL,
   each proved by `match / 0 drift`.
4. Revisit the cmdlet residue only after tiers 1 and 2 are done, against what
   is actually left rather than against today's 25.9%.

**Owner: reek**, by Damian's direction 2026-08-16 ("you can own this bit, as no
other agents are actively workin on this code"). The generators moved off
fester's claims row, which kept `deck-headroom`.

## 9. Progress

| Step | State |
|---|---|
| 1. Deprecate `ScRaw` / `SeRaw` in `ShellTypes` prose | DONE, main 15606 |
| 2a. `SeText` + `msg`, all three emitters, arms | DONE |
| 2b. `need-file`, `join-path`, `set`, `if-set` | next |
| 3. Convert generators, one CL each | STARTED: `bootarm64Script`, 7 sites, `boot-arm64 match / 0 drift` (main 15616) |
| 3b. Lift red's `-Internal` fast gate into `BuildScript.codex` | DONE, main 15643. `build match 897 0` with an EMPTY `generated-scripts-baseline.txt` |
| 4. Cmdlet residue | not until 1-3 are done, and against what is left |

**The oracle held on the first real conversion**: seven `SeRaw` sites in
`bootarm64Script` became `msg`, its escaped-quote count fell 34 to 20, and the
emitted script was byte-identical. That is the loop this campaign runs in, and
it is worth doing one generator per CL precisely so each one gets that proof.

**Not yet done and deliberately so: a naive reader on the after-form.** The
author of a style cannot judge whether it reads better to someone who does not
already know what the script does (`R-NAIVE`). That is owed before the bulk
conversion, not after.

## 10. Handoff, 2026-08-16

Written at the relaunch handoff so the next owner starts from what is true
rather than from this document's opening paragraphs, which describe the problem
and not the state.

**Next action is step 2b**: `need-file`, `join-path`, `set`, `if-set` in
`codex/foreword/shell/ShellBuild.codex`, sized in section 6. Then step 3 by
generator, `quiremapScript` first for the reason in section 4.

**The loop that works, and it is the point:** change the generator, run
`build/check-generated-scripts.ps1 -Diff <name>` until the diff is empty, then
gate. `match / 0 drift` is proof of byte-identical emission, so the restructure
can be aggressive. Both conversions so far went that way on the first or second
try.

**One thing learned the hard way and worth inheriting.** `CDX1070` is why the
walls exist, so the readable form REQUIRES naming the sub-block; there is no
formatting-only fix. Splitting `g01` into `g01-head & g01b & g01-tail` is the
worked example, and it cost nothing in emission.

**The 25.9 per cent cmdlet residue is real and should not be forced.** `g01b`'s
body is `ScRaw` on purpose: an ordered hashtable literal, a generic HashSet and
a `$(if ...)` subexpression have no constructor. Converting those wants the
node designed against real call sites, which is step 4 and is deliberately last.

**Unclaimed and open**, in case the next owner wants the cheapest win: 33 of the
34 generators in the section-4 table are untouched, and `quiremapScript` at 90
per cent raw converts to one table plus a reader with no new nodes.
