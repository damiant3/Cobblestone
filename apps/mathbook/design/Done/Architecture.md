# MathBook — Symbolic Math Notebook

## Vision

A Mathematica/Jupyter-style interactive math environment built in
Codex. Symbolic expressions are first-class values. Everything you
type is parsed into an expression tree, simplified, and rendered.
The notebook is both a calculator and a document.

## Modules

| File | Purpose |
|------|---------|
| `Expr.codex` | Expression tree: 36 node types covering integers, rationals, variables, arithmetic, trig, calculus operators, logic, sets, matrices, constants |
| `Printer.codex` | Pretty-printer with precedence-aware parenthesization, text mode and LaTeX output |
| `Simplify.codex` | Rewrite-rule simplifier: constant folding, identity elimination, algebraic rules, function rules (ln/exp/trig/factorial), fixed-point iteration |
| `Calculus.codex` | Symbolic differentiation (sum/product/quotient/chain/trig/exp/log rules), integration (polynomial/trig/exp), Taylor series, gradient |
| `NumberTheory.codex` | GCD, LCM, primality, factorization, modular arithmetic (mod-pow, mod-inverse), Euler totient, Fibonacci, binomial, Catalan, integer sequences |
| `Solver.codex` | Linear solver, quadratic solver (real and complex roots), Newton's method, polynomial operations (add, derivative, evaluate, to-expression) |
| `Plotting.codex` | Function plotter: sample expressions over a range, auto-scale, multiple series, parametric plots, legend |
| `Statistics.codex` | Descriptive stats (mean, median, mode, variance, stddev, percentiles, IQR), correlation, linear regression, frequency tables, summary records |
| `Notebook.codex` | Interactive notebook: cells with input/output, symbol table, expression evaluation, widget UI |

## Expression System

The `Expr` type is a 36-variant sum type. Every mathematical object is
an expression. Smart constructors enforce invariants (0+x = x, x^0 = 1,
ratio auto-reduces). The simplifier applies rewrite rules to fixed point.
The differentiator produces symbolic derivatives. The printer renders
with operator precedence.

## Notebook Model

A notebook is a list of cells. Each cell has an input, a parsed
expression, and a computed result. Cells share a symbol table — define
`f(x) = x^2` in one cell, differentiate it in the next. The widget
builder produces the notebook UI. The LaTeX printer feeds MathJax in
the browser.
