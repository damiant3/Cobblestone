# red -- workplan

**Scratch for THIS session's lane state only. Emptied at handoff, not
appended to.** Open work belongs in `docs/PM/CurrentPlan.md`; anything
durable from today is already in the doc that owns the subject.

## In flight

**Sitting 11 is FLASH-READY on `2C7030D7`, waiting on Damian's go.** Built on
main 18932 (reek's sink `died` word; root's seven-operation reset split: b3's bring-up now paints and
banks `reset-imc`, `reset-ctrl-read`, `reset-rst-write`, `reset-await-reset`,
`reset-settle-mdio`, `reset-imc-again`, `reset-icr`, then `rings-link`, `k1`,
`calibrate`; the append fix so the clock row survives; blu's SMBus read row;
ULP entry-disable wired OFF) by seed `F2DA3901`. Rehearsed 43 of 43 on both beds at 2026-08-21T22:05:46Z. Cfg `build/boot/diag-sitting11.cfg`: `b3
peer=192.168.6.141:7 ip=192.168.6.200`, `pchk1 on`, `asde on`.

What it answers: WHICH of the seven lines of `e1000-reset` the board stops
on, with CTRL banked at the RST write and `settled` at settle-mdio.

Flash recipe: echo peer on port 7 (PID 28276, still listening); dump the
stick; flash with `-ExpectHash` against the image named above.

## Shelved

Nothing shelved. No token held.
