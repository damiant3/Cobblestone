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

**The comment row is convertible and buys nothing, measured 2026-08-24.** All
three emitters render a comment and a raw line the same way: `ScComment` is
`pad & "# " & text` and `ScRaw` is `pad & text` in `PowerShellEmit`, `BashEmit`
and `KshEmit` alike, so `ScRaw "# x"` and `ScComment "x"` are byte-identical
everywhere today. 648 of the 6,392 `ScRaw` payloads begin `"# "` and would
convert with no drift; 55 more begin `#` without the space and would not. The
conversion moves the count by 10 per cent of the corpus and changes nothing a
reader, an emitter or a target can observe. Its only value is a target that
does not exist yet -- one whose comment marker is not `#` -- which is the same
theoretical portability section 3 already flags, and it is 648 sites of churn
across load-bearing build scripts to buy it. **Not taken**, and named here so
the next owner does not take it for the count.

That is also the first real answer to the sizing question below: the categories
are not equally worth converting, and at least one of them is a no-op. Size
what a conversion BUYS, not just whether a constructor exists.
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

**`quiremapScript` was named here as the one to do first, and that was wrong.**
It is 90% raw and almost all of it is a single PowerShell hashtable literal
written one `ScRaw` line per entry, which does make it the clearest case of the
AST being used as a string list. The claim that it "converts to one table plus
one reader with no new nodes" does not survive reading the emitter, measured
2026-08-24: `SeOrderedMap` emits **one line**,
`[ordered]@{ 'a' = x; 'b' = y }` with `; ` separators
(`PowerShellEmit.codex:395`, `emit-ps-map-fields` at 57), against a shipped
`quire-map.ps1` that spells a multi-line `@{` with `#` comments interleaved
between the entries. Different construct, different layout, so the conversion
cannot be proved by `match / 0 drift` at all.

**The general rule this is an instance of, and it is what section 3's 70 per
cent hides.** The set that converts under this campaign's oracle is not "raw
payloads with a constructor that does the job", it is the narrower **"raw
payloads whose text a constructor already reproduces character for
character"**. Section 3's table is a CATEGORY estimate and was never a
byte-identity estimate. `CompileScript` is what the narrow set looks like when
it is large -- 13 messages, 3 guards, 84 assignments, 23 conditionals, every
one byte-identical -- because those payloads were literally what the
constructors emit. A hashtable literal, and every cause-1 wall, are not.

Sizing the convertible fraction is unmeasured and is the number that says what
this campaign can actually deliver. Nobody should take another generator on the
strength of its raw SHARE until that exists.

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
| `join-path [parts]` **LANDED** | `SeRaw "Join-Path ..."` | **167**, not 350, and only 23 of them convert without drift; see step 2c and the deck floor |
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
3. Convert generators one at a time, each its own CL, each proved by
   `match / 0 drift`. Not `quiremapScript`; see section 4.
4. Revisit the cmdlet residue only after tiers 1 and 2 are done, against what
   is actually left rather than against today's 25.9%.

**Owner: reek**, by Damian's direction 2026-08-16 ("you can own this bit, as no
other agents are actively workin on this code"). The generators moved off
fester's claims row, which kept `deck-headroom`.

## 9. Progress

| Step | State |
|---|---|
| 1. Deprecate `ScRaw` / `SeRaw` in `ShellTypes` prose | DONE, main 15606. **It did not work**: see the re-measurement below |
| 2a. `SeText` + `msg`, all three emitters, arms | DONE |
| 2b. `need-file`, `set`, `if-set` | DONE 2026-08-24, `ShellBuild.codex` |
| 2c. `SePathJoinN` + `join-path`, arms in all three emitters | **DONE 2026-08-24**, reek 19130, landed with the deck constant bump ruling 20 authorised. Converts the 23 parenthesized sites; the 126 bare ones still need a decision, below |
| 3. Convert generators, one CL each | `bootarm64Script`, 7 sites (main 15616). `CompileScript` 2026-08-24, the section-7 worked example: 13 `SeRaw` messages to `msg`, 3 guards to `need-file`, 84 `ScAssign` to `set`, 23 `ScIf (SeVar ...)` to `if-set`, escaped quotes 90 to 64, `compile match 399 / 0 drift` |
| 3b. Lift red's `-Internal` fast gate into `BuildScript.codex` | DONE, main 15643. `build match 897 0` with an EMPTY `generated-scripts-baseline.txt` |
| 4. Cmdlet residue | not until 1-3 are done, and against what is left |

**`join-path` could not be a builder alone, and section 6 sized it wrong.**
`SePathJoin` is BINARY and `emit-ps-expr` renders it parenthesized
(`PowerShellEmit.codex:353`), so a builder folding over it emits
`(Join-Path (Join-Path a b) c)` where the raw sites spell `Join-Path a b c`
flat. Same result, different text, so the builder alone would drift shipped
scripts and spend the byte-identical oracle this campaign rests on. It was
written for the 2b CL and withdrawn unshipped rather than left as a definition
nothing calls. Step 2c is the node: **`SePathJoinN (List ShellExpr)`**, arms in
all three emitters, `join-path` as its builder.

**The 350 was wrong and the shape underneath it matters more than the count**
(L-ADJECTIVE: a number standing in for a structure). Measured 2026-08-24 over
the raw payloads in `codex/build/*Script.codex` that mention `Join-Path`:

| shape | sites | converts with no drift |
|---|---|---|
| payload begins `(Join-Path ` | 23 | **yes**, today |
| payload begins `Join-Path ` bare | 126 | no |
| `Join-Path` embedded further in | 18 | no |
| total | 167 | |

`SePathJoinN` emits PARENTHESIZED, which is what `SePathJoin` already does and
what makes the node safe in any expression position. That converts the 23 and
leaves the 126: a bare site would gain two characters, which is behaviour
preserving and is still 126 lines of drift across `build.ps1`, `test.ps1` and
their siblings. **That is a decision, not a mechanical step**, and it is not
this campaign's to take alone, because `check-generated-scripts.ps1` has no
`-Write` precisely so the shipped script stays the maintained side. The two
ways out are a one-time reviewed reparenthesization of those 126 lines, or a
statement-level assign-a-path command that never needs the parens. Neither is
started.

**The deck floor is what actually stops this campaign, and it was reached
before anybody noticed.** Step 2c emits correctly and proves byte-identical on
every generator (`compile match 399 / 0 drift`, full sweep 57 generators, 0
drifted, 0 broken) and it still fails `build/build.ps1 -Internal`:

```
FAIL: 1 unit(s) below a margin of 1.25.
  margin  1.23  derived   64  needs   52  CHECK-RESOLVE  codex\build\cdxtopeScript.codex
```

Measured with a control, the fix state and the depot state of the same six
files, `deck-headroom.ps1 -Quire codex\build -WithSelf -Fresh` each way:

| unit | without 2c | with 2c |
|---|---|---|
| `cdxtopeScript` | 51 of 64, margin **1.25** | 52 of 64, margin **1.23** FAIL |
| `testScript` | 50, 1.28 | 51, **1.25** |
| `BuildScript` | 50, 1.28 | 51, **1.25** |
| `vmconfigScript` | 49, 1.31 | 50, 1.28 |

**Every unit moved by exactly one point**, and `cdxtopeScript` was sitting on
the 1.25 floor with zero slack before this change existed. 192 bytes of added
foreword source cost `cdxtopeScript` 4.75 MB of check deck
(341,753,928 to 346,503,656 bytes used), because every generator bundles these
chapters and a new `ShellExpr` constructor widens the checker's state at every
`when` over the type.

So the constraint is not this node. **`ShellExpr` cannot gain another
constructor at the current deck derivation**, and this campaign's method is
adding vocabulary to `ShellTypes` and `ShellBuild`. Step 4, the cmdlet
residue, is explicitly the step that "may need a NEW node", so it is blocked by
the same wall, and two more units are now standing exactly on the floor where
one was.

**RESOLVED 2026-08-24 by ruling 20, and the fix was a constant.** red called it
a measured constant bump rather than ProportionalDecks, on the precedent of
PR 77's emit-deck 24 to 28 MB the same week, with the note that new machinery
managing a cost smaller than itself is L-LESS unless the bump recurs a third
time. `demand-check-floor` went 648 to 704 MB in `BuildSettings.codex` and 2c
landed with it: `cdxtopeScript` now needs **48 of 64, margin 1.33**, over 59
units. The `-MinMargin` floor was NOT touched, which the standing rules forbid.

The bump was computed, not trialled. A unit's required scale is
`ceil(used / ((floor - band) / 100))`, and that model was checked against five
measured units first -- cdxtope 52, testScript 51, BuildScript 51, vmconfig 50,
checkdoccounts 48 -- all predicted exactly. Solving it for a target left three
points of slack under the 1.25 line, about four more `ShellExpr` constructors at
the measured 4.75 MB each, for 35.8 MB more reservation at scale 64.

**What is left of the original wall is the paragraph below, which still stands
for step 4.** Three points is four constructors, not an unlimited supply.

**This was a decision for the deck lane, not for this campaign to route around.**
`ProportionalDecks.md` owns `deck-scale-min` / `deck-scale-margin` /
`deck-scale-anchor`, and `deck-headroom.ps1` exists precisely to validate a
change to them. The alternative is to shrink `cdxtopeScript` itself, which is
step 3 on the largest wall in the catalog (87 KB in 24 lines, 1,180 `ScRaw`,
93 per cent raw) and would have to be shown to reduce the CHECK-RESOLVE demand
rather than merely the line count -- unmeasured, and not assumed here.
**Step 1 did not work, and this is the number that says so.** Re-measured
2026-08-24 against main 19112: **6,396 `ScRaw` and 570 `SeRaw` across 36 of the
58 generators, 1,900 escaped quotes.** On 2026-08-16 it was 5,504 / 570 / 1,707
across 35 of 56. So `SeRaw` held, and **`ScRaw` gained 892 sites in eight
days** while the prose deprecating it sat in `ShellTypes.codex` where the
compiler never reads it. A deprecation with no runner is the shape
`LESSONS.md` records as a test suite with no runner, one level down. The
mechanical check is cheap -- count `ScRaw` across `codex/build/*Script.codex`
and refuse an increase -- but a new gate is red's clearance to give, not this
campaign's to assume.
**The oracle held on the first real conversion**: seven `SeRaw` sites in
`bootarm64Script` became `msg`, its escaped-quote count fell 34 to 20, and the
emitted script was byte-identical. That is the loop this campaign runs in, and
it is worth doing one generator per CL precisely so each one gets that proof.

**Not yet done and deliberately so: a naive reader on the after-form.** The
author of a style cannot judge whether it reads better to someone who does not
already know what the script does (`R-NAIVE`). That is owed before the bulk
conversion, not after.

## 10. Handoff, 2026-08-24

Replaces the 2026-08-16 handoff, which recommended `quiremapScript` first and
named step 2b as the next action. Both are settled or wrong now.

**Where it stands.** Steps 1, 2a and 2b are done. Step 2c is written and
SHELVED on a red gate (reek 19130) and is not the campaign's to unblock: see
the deck floor in section 9, which red routed to Damian's rulings queue on
2026-08-24. Step 3 has two generators converted, `bootarm64Script` and
`CompileScript`.

**Do not take the next generator on the strength of its raw share.** That is
what section 4's table ranks by and it is the wrong ranking: what converts is
the payloads whose text a constructor already reproduces character for
character, not the payloads that have a constructor for the job. Size that
fraction first. Until it exists, this campaign cannot say what it delivers.

**The loop, unchanged and still the point:** change the generator, run
`build/check-generated-scripts.ps1 -Diff <name>` until the diff is empty, then
gate. `match / 0 drift` is proof of byte-identical emission.

**Pass the depot seed, not `build-output`.** `check-generated-scripts.ps1`
hardcodes `build-output\bare-metal\Codex.cdx` at line 76 and that path holds
whichever kernel ran last: measured 2026-08-24 it was `DE3B400E` while the
depot seed was `A01C1547`. Copy the seed over it before a run whose answer you
intend to publish, and read the `compiler:` digest the script prints.

**`CDX1070` is why the walls exist**, so the readable form REQUIRES naming the
sub-block; there is no formatting-only fix. Splitting `g01` into
`g01-head & g01b & g01-tail` is the worked example and it cost nothing in
emission.

**The 25.9 per cent cmdlet residue should not be forced.** `g01b`'s body is
`ScRaw` on purpose: an ordered hashtable literal, a generic HashSet and a
`$(if ...)` subexpression have no constructor. That is step 4, it is
deliberately last, and it is also behind the deck floor because it is the step
that needs a new node.
