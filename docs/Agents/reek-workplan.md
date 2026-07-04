# Agent Reek Work Plan

**Date**: 2026-07-04
**Stream**: //Codex/reek (parent //Codex/main)
**Active workstream**: Magic Format Solver (`apps/games/magic`)
**Last CL**: 6995 (reek). NOT copied up to main (app-only; Damian: main
doesn't need it. Run `magic Test` 101/0 first if pushing.)

Status of record for the app: `docs/Designs/Apps/MagicSolver/Active/
MagicFormatSolver.md` (its "Current state" section is kept current).

---

## Current focus: Magic Format Solver

A Comprehensive-Rules classic-MTG engine + GA format solver + set tester
in `apps/games/magic`, surfaced in a WPF Command Bridge. Everything is
app code -- **no seed rebuild ever needed**. One thing per CL: source +
a self-checking PASS/FAIL demo + a memory/time verdict; `magic Test`
must stay **101/0**.

### Done (through CL 6995)

- **Mechanics**: full ABU combat (first strike, trample, vigilance,
  flying/reach, deathtouch-kills, regeneration, all evasion), sweepers,
  the X-spell family, prevention (Fog/CoP), auras/anthems, triggers,
  activated abilities (incl. tap-AI auto-firing Royal Assassin/Scepter/
  Disk), belief model, priority window. Pool = **146 authored cards**
  (Power Nine complete).
- **Deckbuilding realism**: mana artifacts in decks; fast-mana AI
  (turn-1 Mox taps turn 1); two-colour **splash** decks (spells +
  creatures via dual lands / splash basics / any-rocks); the GA evolves
  the splash colour.
- **AI depth**: control closes games (unblockable win-cons always
  attack); AI **London mulligan** in the pre-game (tightened LEA metagame
  spread 100 -> 67).

### Constraint (locked with Damian, 2026-07-04)

The mulligan is a **static, simple** strategy on purpose -- a controlled
variable so the solver measures **deck strength**, not mulligan skill.
Do NOT add adaptive / hand-aware / opponent-aware mulligan AI.

Also locked: cards are **authored IR** (CardLibrary.codex), not parsed --
the OracleParse runtime parser is a bootstrap shim, not the source of
truth; do not extend it.

### Next levers (pick highest value when redirected)

1. **AI depth** (the real remaining metagame lever): smarter sequencing
   -- hold removal for genuine threats, play around counters, deploy a
   clock. Control still loses to fast aggro on LEA (White Weenie 100%).
2. **GA**: evolve deck ratios/colour harder; run the evolution loop to
   convergence and report format balance.
3. **Cards**: fill ABU toward ~295 (niche remainders); UntapTarget
   (Twiddle).
4. **Splash refinement**: heavier splash mana; smarter dual selection.

Not: deckbuilding basics (done), mulligan strategy (intentionally static).

---

## Paused workstreams (resolved / on hold)

- **FontExplorer / TrueTypeWriter / GuiOS** (see
  `reek-fontexplorer-workplan.md`): TTF crash RESOLVED (CL 5846), GuiOS
  polish RESOLVED (CL 5856). f64 GPU-kernel fix resolved. Idle.
- **Circuits EDA** (see `reek-circuits-workplan.md`): schematic editor
  foundation laid; mouse-click VM-input bug pending a codex-vm fix. Idle.
