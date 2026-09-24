# Proposal: a memory lease ledger for the box

*Status: proposal, not built (Damian, 2026-09-24: implement if memory kills
recur often enough to cost him). Owner when built: fester (build tooling).*

## The problem

The box is 15.8 GiB with one DIMM out. On 2026-09-23 the harness killed seven
lane runs for low memory. Three causes:

1. **A defect:** Renode with `--console` and an end-of-file stdin grew about
   5 MB a second, to 7 and 8 GB. Fixed by fester (Renode now gets its own
   hidden console in `test-cross` and `test-boards`).
2. **Two fan-outs each admitted on a free-memory reading taken at its own
   launch.** `Get-VmAdmittedSlots` (`build/vm-config.ps1`, used by `test.ps1`,
   `test-cross-batch.ps1`, `deck-headroom.ps1`) reads free memory once, at
   launch. It cannot see a second fan-out that starts a minute later, so two
   runs that each fit alone overcommit together.
3. **The standing load:** six Claude sessions hold about 2.8 GB, plus Edge
   and the editor.

Cause 2 is what this proposal closes. Coordination by message would also close
it, but every message costs the sender and the receiver a turn, and most runs
fit with room to spare.

## The proposal

**Small runs stay unasked.** One guest of 1 GiB or less (a single compile, a
single test run) launches as today.

**Big runs take a lease from a script, not from root.** Before a battery, a
BVT above `-Jobs 1`, a Renode run or any fan-out, the launcher calls:

```powershell
build/box-lease.ps1 -Lane red -GiB 4 -Minutes 40     # returns a lease id, or waits
build/box-lease.ps1 -Release <id>                    # at exit, or the lease expires
```

The script keeps one ledger, `D:\Projects\.agentgrid\box-leases.json`, and
admits a lease only when:

    sum(live leases) + this lease + floor  <=  total physical memory - standing load

with the standing load measured at call time (committed memory of everything
that is not a leased run) and a floor of 2 GiB. A lease that does not fit
waits in a first-come queue and is admitted when earlier leases release or
expire. The file is updated under a lock (an exclusive file handle), so two
launches at the same moment cannot both pass.

**The lease is taken inside the scripts, not by the lanes.** `bvt.ps1`,
`test-cross-batch.ps1`, `test.ps1` and the battery scripts call `box-lease.ps1`
themselves, sized from their own `-Jobs` and the measured per-guest costs
(`CoordinationProtocol.md`, "The token does not cover RAM": about 1 GiB per
run guest, 0.25 GiB per compile guest; Renode about 1.05 GB, measured
2026-09-23). A lane cannot forget a lease it never has to remember.

**Root only watches.** At each pulse root reads the ledger and acts on three
things only: a lease queued longer than 30 minutes, a lease whose run uses far
more than it declared, and a release gate that needs the whole box (root holds
one lease for the whole box for the gate's compiler stages).

## Cost

- Normal case: zero messages; a few lines of script per launch.
- Build: about one session (the script, wiring into the four scripts above,
  a paragraph in `CoordinationProtocol.md`, and an arm that launches two
  fan-outs together and shows the second waits).

## What would say it is not worth building

A week of lane work with no memory kill after the Renode fix. Cause 1 produced
the two largest kills on 2026-09-23; if cause 2 alone does not recur, the
ledger is machinery that manages a cost smaller than itself (L-LESS).
