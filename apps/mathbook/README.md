# Mathbook

A symbolic computer algebra system and interactive math notebook. Provides a Mathematica-style notebook interface where cells evaluate symbolic expressions, produce LaTeX output, and persist state across sessions -- all without a host OS or external math libraries.

## Modules

- **Expr** -- Core symbolic expression tree (55 constructors: arithmetic, calculus, logic, sets, matrices, constants)
- **Parser** -- Recursive-descent precedence-climbing parser with infix operators, implicit multiplication, assignment, and special forms
- **Simplify** -- Fixed-point algebraic simplifier: constant folding, identity elimination, power rules, log/exp cancellation, trig identities
- **Calculus** -- Symbolic differentiation (chain/product/quotient/trig/log rules), indefinite and definite integration, Taylor series, gradient
- **Solver** -- Linear solver, quadratic formula, Newton's method, polynomial representation
- **NumberTheory** -- GCD, LCM, primality, prime factorization, modular arithmetic, Euler totient, Fibonacci, binomial coefficients, Catalan numbers
- **MatrixAlgebra** -- Symbolic matrices: add, mul, transpose, determinant, inverse (adjugate for 1x1/2x2), trace, Kronecker product, characteristic polynomial
- **Statistics** -- Mean, median, mode, variance, standard deviation, percentiles, correlation, linear regression
- **Distributions** -- Discrete (Bernoulli, Binomial, Poisson, Geometric) and continuous (Normal, Exponential) distributions; z-test, t-test, chi-squared; confidence intervals
- **Circuits** -- Digital logic gates, truth table generation, Karnaugh map simplification (up to 4 variables), netlist, binary/hex conversion
- **Proof** -- Interactive proof assistant: propositional logic with verified inference rules (modus ponens, and/or intro-elim, induction, rewrite chains)
- **Printer** -- Pretty-printer to text, LaTeX, and MathML with correct precedence
- **Plotting** -- Function sampler: evaluates expressions at N points, auto-scales axes, supports line/scatter/bar/histogram/parametric/polar series
- **Notebook** -- Top-level notebook state: ordered cells, per-cell evaluation pipeline, symbol table
- **MathbookPersist** -- JSON serialization of notebook cells and symbol bindings to disk (fact kind 39)

## Completeness

75% -- Core CAS pipeline (parse, simplify, differentiate, integrate, solve, print) is fully implemented. Statistics, distributions, number theory, matrix algebra, proof assistant, and circuits are substantively complete. Gaps: integration limited to basic patterns (no integration by parts, u-substitution, or partial fractions); matrix inverse only for 1x1 and 2x2; several proof rules are declared but left unchecked; no `opening` entry point (library awaiting a top-level UI harness).

## Codex Conformance

Full -- All 17 source files are native Codex. Backend rendering (LaTeX via MathJax, canvas plots) is emitted through plugs. No external dependencies.
