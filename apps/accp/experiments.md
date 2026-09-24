# ACCP experiments

This extension selects `library: "experiment"`, which includes math and physics.
`output: "json"` on `codex_run` requires one JSON object on stdout and exposes
that object as `result` alongside the original output bytes and fact. Execution
status and scientific result status are separate. A result reports values,
units, method, tolerance, refinement differences, convergence, assumptions and
sampled data. Program-reported metadata is not an independent certificate.

Decimal numeric fields are previews in scientific notation. `binary64` carries
the exact signed 64-bit bit patterns as decimal strings for values, coordinates,
samples, tolerances and refinement differences. Use those bit patterns for
lossless replay or comparisons near a tolerance. ODE settings also retain exact
initial-state and interval bits. Decimal formatting does not certify rounding;
display rounding at the largest finite magnitude is capped to a finite decimal
preview. Plot tick labels are abbreviated; data coordinates retain full values.

## Quantities

`quantity value symbol` produces `QuantityValue (Quantity)` or
`QuantityError (Text)`. A quantity stores a finite binary64 magnitude in SI and
seven integer dimension exponents: length, mass, time, current, temperature,
amount and luminous intensity. Addition/subtraction require equal dimensions;
multiplication/division combine exponents. Converting to an incompatible unit
fails. Named units are a bounded explicit list; affine Celsius/Fahrenheit are
not silently treated as linear scales. Existing scalar physics functions keep
their documented SI conventions.

## Numerical experiments

Coupled ODE callbacks have type `Real, List Real -> List Real`. State component
labels and units accompany the result. Every derivative vector must match the
state length and contain finite numbers. Numeric callbacks use declared unit
conventions; their internal equations are not dimensionally inferred. Use the
quantity operations to check an equation separately.

Classical RK4 produces a sampled trajectory. A paired run with twice as many
steps compares corresponding retained samples against caller-provided absolute
tolerances for each component. The result reports `refinement_passed` or
`not_converged`, with the observed component differences. Agreement is not a
certified bound on solution error. Singular, discontinuous, stiff and chaotic
problems require analysis beyond this check. Reversed intervals integrate
backward and preserve trajectory order. Equal endpoints return the initial
state once without evaluating the callback, with convergence `not_applicable`.
Published values and trajectories come from the refined run. Each difference
is the maximum absolute coarse/fine difference over retained samples. Both
endpoints count toward the sample cap; sampling never changes integration steps.

Limits: at most eight components, 1024 coarse steps, 256 samples and 1024
retained scalar cells. Sampling stride must divide the coarse step count.
The fine run permits 2048 steps and doubles the stride, retaining matching
sample times. The existing source, time, output and memory ceilings still apply. Callbacks
receive fresh state lists so callback mutation cannot change the integrator's
saved state. No library operation changes a caller-owned list.

Parameter sweeps evaluate one `Real -> MathResult` callback for every requested
parameter, preserving caller order. An invalid point reports failure without
publishing a successful partial table. Sweeps preserve duplicates and have no
convergence claim. Callback reproducibility requires deterministic callbacks;
a callback can still mutate objects captured by its own closure.

Quantity arithmetic returns `QuantityResult`; conversion with `quantity-in`
returns `MathResult`. Experiment errors return `ExperimentError`, serialized
as status `error`, null value and empty coordinates/samples. A completed
calculation that fails refinement returns its fine trajectory with status `ok`
and convergence `not_converged`; consumers must inspect both fields.

The normative callable API, supported symbols, JSON schema and complete
examples are in `AccpExperimentCard.codex`, served at
`codex://accp/experiments`. JSON `settings` records ODE initial state, time
interval, coarse/fine step counts and stride. Sweep coordinates retain every
parameter. Save submitted source and callback alongside results and the fact;
the journal retains source and library hashes, not the source itself.

## Plots and evidence

SVG export plots one selected result component against the sampled independent
variable. Plots are 800 by 480; x increases rightward and y upward. Segments
follow sample order, including backward integration. Plot values use declared
units without conversion. Constant ranges pad by max(1, abs(value) * 0.05);
unrepresentable padding is refused. Nonconverged trajectories can be plotted
with the convergence state in the title. Plot failure is `PlotError`; valid
output is `PlotSvg`. Labels are XML-escaped. SVG output contains
no scripts, external resources or host file access. Saving an artifact is a
caller action. Arbitrary stdout is never automatically rendered as trusted SVG.

The guide resource supplies complete copied examples. Focused acceptance uses
analytic oscillator solutions, damping, refinement failure, malformed callbacks,
dimension mismatch, mixed-unit conversion, caller-list preservation, sweep
failure and SVG XML/coordinate checks. The unchanged math/physics, MCP and L2
suites remain regression checks. The installed bundle and live MCP discovery
are verified before claiming availability.

Cost: quantity operations use constant bounded dimension work. RK4 time and
allocation are O(steps * components), plus callback cost; saved trajectory space
is O(samples * components). Refinement doubles integration work per fine run
and preserves the retained-sample caps.
Serialization and plotting use linear fragment accumulation, not repeated
concatenation of growing prefixes. Request heap checkpoints reclaim conduit
scratch. Compiler source and seed are outside this extension.
