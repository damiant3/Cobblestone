# Client crashes (UOAIX Assist)

The register of every CLIENT.EXE crash seen with the Assist, the evidence kept for each, and what is known of the cause.
Owner: blu. A new crash is added as a row here, with its evidence, the same day (Damian, 2026-10-18: "create a crash
log and document this all with the project").

## Where the evidence is

- **The relay's crash file.** When the client's side of the game connection resets ("forcibly closed by the remote
  host", which a dying CLIENT.EXE leaves), the relay writes its last 1000 log lines, whole, to
  `crash-<port>-<time>.log` beside its logs (`AssistLog.Crash`, called from `GameLink.Up`), and logs `CRASH FILE`. The
  logs are in the relay's `-LogDir`: for the uo1998 relay on port 2650, `D:\Projects\Cobblestone-root\build-output\uoaix\assist`
  (`uoaix-init.md`, the relay's launch line); unset, `build-output/uoaix/assist` in the workspace that runs it. The
  keep-last-4 rotation never deletes these. A client closed by hand can reset the connection too, so a crash file is a
  crash only when Windows recorded one at the same time.
- **Windows.** Each crash is an Application event 1000 (faulting module, exception code, fault offset) and a report
  folder `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_CLIENT.EXE_*\Report.wer` (`EventTime` is a
  FILETIME). No dump is kept: the reports hold `Report.wer` only.
- **The relay logs** themselves rotate after 4 relay sessions, so a crash without a crash file loses its packets within
  hours.

## The crashes

Times are local (UTC-7). Every one is exception c0000005 (an access violation).

| when | client | fault offset | what was happening | evidence |
|---|---|---|---|---|
| 2026-10-04 14:44:25 | `D:\Projects\uoaix-client` (UOAIX shard) | 00061186 | not recorded | Report.wer |
| 2026-10-04 18:21:22 | `D:\Projects\uoaix-client` (UOAIX shard) | 00044649 | not recorded | Report.wer |
| 2026-10-05 00:45:33 | `D:\Projects\uoaix-client` (UOAIX shard) | 00044649 | not recorded | Report.wer |
| 2026-10-17 07:10:09 | `D:\Games\UO1998com-Assist` (uo1998) | 000527b7 | not recorded | Report.wer, event 1000 |
| 2026-10-17 18:37:55 | `D:\Games\UO1998com-Assist` (uo1998) | in ntdll (`PCH_5F_FROM_ntdll+0x000787FC`) | the buy circuit's Vesper bank step (01:37:55 UTC) | Report.wer, event 1000 |
| 2026-10-17 22:17:09 | `D:\Games\UO1998com-Assist` (uo1998) | 00052862 | one second after the server refused an assist pickup lift that the relay passed on to the client (05:17:09 UTC); a refused assist lift has stayed in the relay since (`GameLink.AssistLift`) | Report.wer, event 1000 |
| 2026-10-18 10:58:42 | `D:\Games\UO1998com-Assist` (uo1998) | 0004930f | Kwik walking by hand in Vesper at 2886,599 just after a recall; no assist injection and no assist packet to the client in the 70 s before; the last 3 s were the client's 4 steps (0x02) and their 4 answers (0x22), 6 world items (0x1A), 3 mobiles (0x78), a ping each way (0x73) and a status bar (0xA2) | `crash-2650-20261018-105846.log` (saved by hand from the relay log), Report.wer, event 1000 |
| 2026-10-20 20:29:55 | `D:\Games\UO1998com-Assist` (uo1998) | a hang, not a crash: the window stopped answering and one core spun (CPU +1 s a second); killed by hand at 20:31:25 | logging in as Kwik straight after logging out Moonshot (03:29:47 to 03:29:55 UTC), the client running 5 h through 12 relay bounces; the relay and the game link stayed healthy, server packets kept arriving | `assist-2650-20261020-202411.log` 03:29:47 to 03:31:25 UTC |

## What is known

- Seven crashes at six different places: five in CLIENT.EXE's own code, one in ntdll, and two of the shard client's at
  the same offset (00044649). Nothing in the evidence ties them to one Assist action: the latest happened with the
  Assist idle and the player walking.
- The cause is not proven. Two crashes came during Assist work (a bank step, a refused lift) and one with none.
- Next evidence: the crash files from here on hold each crash's last 1000 log lines. Compare the last server packets
  of two crash files at the same offset before naming a cause. A full dump needs Windows Error Reporting's LocalDumps
  key for CLIENT.EXE (an administrator registry change on Damian's machine, his to make).
