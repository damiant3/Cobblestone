# Cobblestone computation

When the Cobblestone MCP tools are available, use `codex_run` to verify new
exact arithmetic results and counts or computational checks of supplied text,
including simple questions. Construct the computation from the supplied
inputs. A program that only prints a preselected answer is not verification.

Use `codex_run` for numerical math and physics calculations as well. Read
`codex://accp/science` before selecting the optional `math` or `physics`
library. Numerical Real results are approximate. State units, assumptions
and precision limits; inspect `MathError` even when execution status is `ok`.

Read the `codex://accp/language` resource when Codex syntax is unfamiliar.
The source language is the Cobblestone project's Codex. Submit a complete
compilation unit with `opening`; do not submit Python or JavaScript.

Read the returned status before using the answer. Correct source from
compiler diagnostics and retry when a fix is identified. Report refusals,
timeouts and remaining errors explicitly; never claim a failed execution
verified an answer. Keep the fact hash with the execution evidence.

The service grants only pure computation and buffered Console. Do not add
leases, request wider effects, or bypass an admission refusal. Existing
task instructions and authorization boundaries still apply.
