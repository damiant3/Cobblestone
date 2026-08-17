# secrets -- backlog

## 1. `pbkdf-iterations = 100000` is a PBKDF2 constant in an Argon2-shaped parameter

`VaultCrypto.codex:57` sets `pbkdf-iterations = 100000` and `:76` passes it as
`pb-time-cost`. Those are not the same quantity. `pb-time-cost` is a PASS count
over the whole block array (`Pbkdf.codex:96` `pbkdf-fill-passes`), not a
PBKDF2 iteration count, and the chapter's own default is 3 (`:38`). With
`pb-block-count = 4096` the unlock does

    100000 passes * 4095 blocks = 409,500,000 block mixes

against 12,285 at the chapter default, a factor of 33,333. Each mix runs
`pb-xor-blocks` and `pb-replace-block`, about 65 list operations, and
`pb-xor-blocks` builds a FRESH 32-element list per block per pass, so the
same figure is also roughly 409.5 million transient allocations on a heap
with no collector. The prose at `Pbkdf.codex:72-80` records that this
chapter already died once on transient copies at these block counts.

**Reached, not latent.** `derive-master-key` is called from `Vault.codex:58`
and `VaultCrypto.codex:93`, so this is every vault unlock.

**No wall-clock number is recorded here on purpose** -- nobody has timed it,
and the arithmetic above is inspection. Measure before choosing the fix.

The likely fix is one constant, but it is a security parameter and picking it
is a judgement call for whoever owns this app: `pb-time-cost` in the low
single digits with `pb-block-count` carrying the work factor, which is how
the Argon2-style construction in `Pbkdf.codex` is meant to be tuned.

Found by blu 2026-08-16 while checking Track D row 19 cites; the verdict is
blu's and confirmed against source by reek, who corrected the mechanism (the
passes do no `sha256`; the hashing is the one-time `pb-expand-blocks`).
Neither lane owns `apps/secrets`, which is why it is written down here
rather than carried in a message.
