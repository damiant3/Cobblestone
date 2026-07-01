# WHPX: host BSOD during workspace reorganization session — 2026-05-03

## Summary

Host BSOD on 2026-05-03 while agent **Cam** was active. Session involved
major depot reorganization (CLs 690–703: tool retirement, directory
renames, p4 moves) and one `codex.build/pingpong-self.ps1` run (background,
exited 0 — PASS). BSOD occurred after the pingpong completed and the
workspace was being cleaned + force-synced.

**Agent**: Cam
**Workspace**: `D:\Projects\NewRepository-cam` (`BigWhite_Codex_cam`)
**Config**: `-accel whpx,hyperv=off`, `-machine kernel-irqchip=off`, no CPU pinning (CL 555 config)

## Timeline

| Time (approx) | Event |
|---|---|
| ~04:50 | Session start; depot cleanup CLs submitted (690–699) |
| ~05:15 | `codex.build/pingpong-self.ps1` launched in background (CL 697 test) |
| ~05:20 | Pingpong completes: PASS, stage1 === stage2 at 881,521 bytes |
| ~05:30–06:37 | Further CLs (701–703): samples → codex.test, foreword → codex.foreword, tools → codex.build |
| ~06:36 | Workspace nuked + `p4 sync -f //Codex/main/...` |
| After sync | **BSOD** |

## QEMU state at crash time

The pingpong QEMU processes should have been long gone (~70 min before
BSOD). No sweep or other QEMU-invoking script was active at the time.
The crash happened during or after a `p4 sync -f` — no QEMU in flight.

Possible explanations:
- Stale WHPX partition from the earlier pingpong not fully released
  (winhvr.sys holding state after guest exit)
- Unrelated: Windows Defender real-time scan contention during the
  force sync (500+ files written rapidly)
- Random WHPX driver corruption accumulating across sessions

## Crash details

**Bugcheck**: unknown — no minidump analysis yet.

**Requested**: check `C:\Windows\Minidump\` for the crash dump and
run `!analyze -v` if WinDbg is available.

## Context

This is incident #8+ in the ongoing WHPX BSOD cluster. Prior mitigations
(`kernel-irqchip=off`, jobs≤4, no CPU pinning) reduced frequency but did
not eliminate the bug. Upstream QEMU issue:
gitlab.com/qemu-project/qemu/-/work_items/3460.

## Impact

Session interrupted. No data loss (all CLs were submitted before BSOD).
Workspace was mid-sync — re-run `p4 sync -f //Codex/main/...` after
reboot to complete.
