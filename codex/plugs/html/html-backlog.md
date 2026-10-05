# HTML plug -- open capabilities

## A list append copies the list

`acc & [x]` on a list is emitted as a copy, so a loop that appends one element
per step is quadratic in the page. `text-split` (Foreword TextSearch) is that
loop: measured 2026-09-30, a 312 KB safetensors header split at its commas took
115 s in the Studio page, 74% of it in `text_split_loop`.
`apps/diffusion/BrowserLoad.codex`'s `bl-list` no longer splits (a one-pass
scan that pushes); every other page that splits a long text still pays it.
Native code extends in place when the accumulator is linearly consumed
(DevelopersGuide), so this is the emitter's to fix, not the callers'.