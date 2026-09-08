# Lambda parameter spans (COMPILER-60)

## What is wrong

A lambda parameter has no span, so nothing the checker recorded about it can
ever be looked up.

`ALambdaExpr` is `(List Name) (AExpr) (SourceSpan)` (`Ast/AstNodes.codex:41`)
and `Name` is `{ value : Text }` (`Core/Name.codex:4`), so a lambda's
parameters share one span for the whole lambda and have none of their own.
`lower-lambda-params-acc` stamps every parameter with that lambda span
(`IR/Lowering.codex:877`) and takes the parameter's TYPE by peeling the
expectation (`:872`). At a polymorphic call the expectation is the callee's
declared parameter with its type variables still in it, so the parameter slot
carries `(tvar N)` while the body's uses of the same name are emitted resolved,
and the def disagrees with itself on the wire.

`lower-def-params` takes `List AParam`, and `AParam` is
`{ name : Name, span : SourceSpan }`. The def path already has what the lambda
path lacks; that asymmetry is the whole defect.

Measured at head by `build/ir-fidelity/ir-fidelity.ps1 -Disagree`: 3 sites over
2 programs of 615 clean (11 refused, out of the denominator). `typeclass-poly:7`
`equals` gives `__lam_0` `y` `(tvar 16)` against `int-default`;
`typeclass-poly:19` `sort-key` gives `__lam_2` `x` `(tvar 19)` against
`int-default`; `typeclass-smoke:11` `to-text` gives `__lam_2` `b` `(tvar 16)`
against `boolean`. The control in the same file is `typeclass-smoke:8`
`to-text (x) = show x`, whose parameter is already `int-default` and which the
detector does not flag.

## What is NOT needed, which is most of what the row's first reading assumed

**The parser and the lexer do not change.** `LambdaExpr` is
`(Token) (List Token) (Expr)` (`Syntax/SyntaxNodes.codex:23`), and
`collect-lambda-params` (`Syntax/ParserExpressions.codex:883`) already collects
real tokens with real spans. The span exists and is thrown away at exactly one
line: `desugar-lambda-param : Token -> Name` (`Ast/Desugarer.codex:233`) answers
`make-name (token-text tok)` and drops `token-span tok`.

The selector form (`.field`, `ParserExpressions.codex:914`) synthesises its
`__r` parameter as a Token carrying `dot-tok`'s line, column and file-id, so it
has a usable span too and needs no special case.

## The change

1. `desugar-lambda-param : Token -> AParam`, keeping `token-span tok`.
2. `ALambdaExpr (List AParam) (AExpr) (SourceSpan)`, and its copier
   (`AstNodes.codex:407`) copying params as `AParam` rather than as names.
3. The checker records each lambda parameter's solved type under that span, the
   way it already records for the sites `lookup-expr-type` answers.
4. `lower-lambda-params-acc` takes the recorded type at the parameter's own span
   when the peeled expectation still carries type variables, falling back to the
   peeled type otherwise. `bind-lambda-to-ctx` (`Lowering.codex:856`) needs the
   same treatment or the body binds against the unresolved type again.

Step 3 is the one that has to be got right: a span recorded but never written to
by the checker leaves `lookup-expr-type` answering `ErrorTy`, which is
indistinguishable at the lowering site from today's behaviour and would ship as
a silent no-op.

## R-COST

**Per parameter, in the AST:** one `AParam` in place of one `Name`, so one
`SourceSpan` more. `SourceSpan` is two `SourcePosition` records plus `file-id`
and `provenance` (`Core/SourceText.codex:16`), and `SourcePosition` is three
integers, so the addition is two small records and two fields per parameter, and
it is proportional to source text rather than to anything at runtime.

**Per parameter, in the type table:** one `ExprTypeEntry`
(`Types/Unifier.codex:21`, a key and a type pointer) inserted into
`st.expr-types`, which is a SORTED list read by `bsearch-expr-type-pos`. The
memory is negligible; the insertion path is what to watch, and it is the same
path every other recorded expression already takes.

**Times the lambda count on the self-compile, measured 2026-09-07** over
`build-output/disagreefix/Codex.codex`, 66,370 lines, the largest program in the
tree: **162 lambdas carrying 317 parameters**, and **zero** leading-dot
selectors. So the whole cost on the compiler compiling itself is 317 additional
`AParam` records and 317 additional `ExprTypeEntry` insertions. Method, so the
number can be re-taken (L-COUNT): column-2 prose skipped (exactly one leading
space), string literals blanked, then `\` followed by identifiers up to `->`.
It counts source lambdas, not lambdas instantiated at runtime.

That is small, and R-COST runs the other way from the intuition here for the
same reason it does on COMPILER-59: the parameters cost nothing today only
because nothing is recorded for them.

## The grade

`build/ir-fidelity/ir-fidelity.ps1 -Disagree -Kernel <candidate>` must read
**0 sites over 615**, against 3 over 2 programs at head. Seed-affecting, so the
chain is the scratch fixed point, the BVT, then the signed and self-verified
seed, each a granted run.

Two things this design does not claim. The 3 sites are the ones the detector can
see; a def whose disagreement is between two type variables is invisible to it
by construction, since it discriminates a tvar against a concrete type. And the
detector has never been shown to fire on a def-path parameter, only on lambdas,
so a regression on the def path would not be caught by this grade.

## What the change as designed actually does, measured 2026-09-07

**It does not reach the grade, and it regresses a def.** Built twice off the
depot seed (candidates `40D70FA13AD14646` and `83F52E4B2FB833B0`), both compile
clean, and both read **2 sites over 1 program** where head reads 3 over 2.

What it fixes: `typeclass-smoke` goes entirely clean and both `__lam_2` sites
go. The lambda half of the design works.

What it breaks: `typeclass-poly`'s `convert` DEF, whose wire at head is
`(param "x" int-default) (param "y" int-default)`, fully resolved, and which
under the change carries `y` as `(tvar 312)` against a body of `int-default`.
That is a new disagreement on the def path, not a lambda one.

**It is NOT the def parameters' spans.** `synth-bare-methods` and
`synth-spec-methods` build the instance-method def params and
`synth-instance-fields` builds the lambda params, all from the same `m.params`
tokens, so the first reading was that a def parameter and a lambda parameter had
come to share one span key. Returning the two def sites to `synthetic-span` and
leaving only the lambda sites with real ones changed nothing: the second
candidate reads the same 2 sites, the same names, the same type variables. So
the residue is somewhere the parameter spans are not, and the next actor starts
from that rather than from the shared-key theory.

The remaining lambda site is `typeclass-poly` `__lam_0` `x` `(tvar 302)` against
`int-default`.
