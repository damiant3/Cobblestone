# Time zones: real zone rules on every date and time path

Owner: reek. Proposal; no build before the rulings in section 8.

## 1. The end state

Damian, 2026-11-01: "just because today is fallback day to standard time, we
should make sure our time/date stuff is properly sensitive to the various
timezones and governmental differences here". Every Codex date and time path
converts through real zone rules: daylight-saving transitions by region and
hemisphere, zones with no DST, 30- and 45-minute offsets, and rule changes
by date as governments change them. A local time that is ambiguous at a
fall-back or absent at a spring-forward is refused or named explicitly,
never guessed.

## 2. What the tree does today (census, 2026-11-01)

About 25 wall-clock consumers; the rows that decide the design:

| where | what | zone handling |
|---|---|---|
| `apps/works/GopZones.codex:19-79`, `zone-offset-on` (:106) | about 60 hard-coded IANA-style names, each a base offset and one of six DST rules | `GopLocale.loc-dst-on` (:105): six hand rules, always 60 minutes, switching at 02:00 at both ends, tested against the UTC date and hour |
| `apps/works/GopClock.codex:373` `clk-local-mins` | the desk's taskbar and clock face | adds the zone offset to the RTC hour; the hour wraps and the date never moves |
| `codex/os/dev/DateTimeSettings.codex:35-81` | 9 fixed abbreviation offsets (`tz-est` ...) | the label is shown, the offset is never applied |
| `apps/works/GopZone.codex` | `TIMEZONE.DAT` on the ESP, -720..+840 minutes and a zone id | fixed minutes |
| `codex/foreword/core/DateTime.codex` | Unix to civil and back, ISO 8601 with `Z` | none; `day-of-week` (:112) computes `(days + 4) / 7 * 7`, a multiple of 7, not a weekday |
| `apps/cvmm/Clock.codex:26`, `Calendar.codex` | epoch to time of day, calendar grid | whole hours only; `cal-day-view` calls `day-of-week 0` |
| `apps/services/TimeService.codex`, `os/net/Ntp.codex`, `foreword/encode/Sntp.codex` | RTC and NTP to Unix | none (UTC) |
| `foreword/encode/X509Chain.codex:148`, `X509Write.codex:139`, `Jwt.codex:98` | certificate validity, token expiry | UTC only, correctly; nothing in the tree turns the real clock into the `cal-now` string TLS checks against |
| desk panes, `DevConsoleBoot`, `Core/BootPaint`, `GopShot`, `GopEdit`, `GopScene` | raw RTC fields shown or stamped | none; most assume BCD and ignore register B's binary and 12-hour bits |
| `apps/cvmm/TaskScheduler.codex:29`, `foreword/core/Schedule.codex` | daily, weekly, business-hours schedules | none; daily is now + 86400 whatever the hour |
| `apps/calendar/CalendarPage.codex` | month view | "today" hard-coded; leap years by divisibility by 4 only |

There is no tzdata anywhere, and `America/Argentina` in `GopZones` is not
an IANA identifier. The RTC is read as UTC wherever a zone is applied, and
nothing reads it as local time.

## 3. The zone data and how the tree owns it

**Source.** The IANA time zone database, the text rule files (`africa`,
`antarctica`, `asia`, `australasia`, `europe`, `northamerica`,
`southamerica`, `etcetera`, `backward`), public domain. It is the only
source that records governments' changes by date; every operating system's
zone data is compiled from it.

**Vendored, pinned, ours to compile.** (Built: `build/gen-tz-source.ps1` turns the vendored files into `TzSource.codex`, one Text literal a file, and `TzRules.codex` parses it; `codex/test/tz-parse` matches the oracle's 598 zone and link names at 2026f.) One release lives at
`codex/foreword/core/tzdata/<release>/` with a manifest of each file's
SHA-256. The compiler is ours: `TzCompile.codex` reads the `Rule`, `Zone`
and `Link` lines and produces, per zone, a transition table (UTC instant,
offset in seconds, DST flag, abbreviation) and a footer rule for the years
after the last transition, the way TZif version 2 does. zic is not used:
the rules are data we read, the compiler is code we own.

**Delivery** (ruling T1). Either (a) a generated binary `TZDB.BIN`, one
record per zone, loaded from the ESP or a hosted file by `TimeZone.codex`,
or (b) a generated Codex chapter of literals. Every zone from 1970 to 2037
is a table no unit should carry unless it reads it (its size is measured at
rung 1); (a) keeps it out of every unit that cites the chapter, (b) needs no
file. Recommended: (a), with the desk
reading only the zone it shows.

**Where it runs.** `TzCompile` is a Codex program, `tools/tz-compile.codex`,
run under codex-vm at build time with the rule text on its input; it writes
the table. `TimeZone.codex` is pure: `tz-zone-of-bytes` parses one zone's
record from bytes the caller supplies, so the foreword chapter does no I/O,
and the desk (reading the ESP), the hosted tools (reading a file) and a test
(a literal) each bring the bytes their own way.

**An update is a CL.** A new IANA release replaces the vendored files and
the manifest, the generated table is rebuilt, and the oracle comparison
(section 6) runs over every zone before the CL lands.

## 4. The API

New quire chapter `codex/foreword/core/TimeZone.codex`:

- `zone-find : Text -> Maybe Zone` by IANA name, `Link` aliases included;
- `utc-offset-at : Zone, Integer -> ZoneOffset`
  (`zo-seconds`, `zo-dst`, `zo-abbrev`) for a UTC instant in seconds;
- `utc-to-local : Zone, Integer -> LocalTime`, the civil fields plus the
  offset in force;
- `local-to-utc : Zone, CivilTime -> LocalResolution`, where
  `LocalResolution = Exact (Integer) | Ambiguous (Integer) (Integer) | Skipped (Integer) (Integer)`.
  `Ambiguous` carries the two instants a repeated local time denotes (a
  fall-back), earlier first. `Skipped` carries the two instants a local time
  inside a gap would denote under the offset before the transition and under
  the offset after it (a spring-forward); neither is a local time that
  occurred, and the transition instant lies between them. No function picks
  one for the caller. `local-to-utc-choosing : Zone, CivilTime, LocalChoice -> Maybe Integer`,
  with `LocalChoice = Earlier | Later | Refuse`, is the only route to a
  single instant, and `Refuse` answers `None` on either case.
- An instant before a zone's first recorded transition takes the zone's
  first offset; one after the last takes the footer rule, so every instant
  has an offset (the start year is ruling T2).
- Unix time counts no leap seconds (POSIX), so the tzdata `leapseconds`
  file is not vendored and no conversion here inserts one.

Instants stay UTC everywhere they are stored, compared or signed (X.509,
JWT, journals, the forensic chain); a zone is applied only where a human
reads or enters a time. `DateTime.codex` keeps the civil arithmetic and
gains a correct `day-of-week`.

## 5. Consumers and what changes

| consumer | change |
|---|---|
| desk clock, taskbar, calendar pane (`GopClock`, `GopZones`, `GopLocale`, `GopZone`, `GopDesk`) | the zone is an IANA name in `TIMEZONE.DAT`; local time and date come from `utc-to-local`, so midnight moves the date; `loc-dst-on` and the 60-name table are deleted |
| setup wizard (`GopWizard wz-zone`) | picks an IANA zone, not a whole-hour offset |
| entering a time (`GopClock clk-commit`, schedules) | goes through `local-to-utc`; an ambiguous or skipped entry is shown to the user and chosen, never guessed |
| `DateTimeSettings`, cvmm `Clock`, `Calendar` | the fixed-offset table is replaced by a zone; `cal-day-view` uses the real weekday |
| `TaskScheduler`, `Schedule` | a daily or weekly schedule names its zone and its transition choice (ruling T5); the next run is the next local occurrence, through `local-to-utc-choosing`, and the run record names the choice whenever a transition made one |
| `TIMEZONE.DAT` files already on ESPs | a version byte: a version 1 record (minutes) loads as a fixed-offset zone and is shown as one until the user picks an IANA zone; version 2 holds the name |
| `HumanApprovalGrant` week windows | `in <zone>` resolves through `zone-find`; an edge inside a gap or a fold refuses the grant |
| `CalendarPage` | the Gregorian leap rule from `DateTime`; today from the clock |
| raw RTC readers (`DevConsoleBoot`, `BootPaint`, desk panes, `GopShot`, `GopEdit`, `GopScene`) | one RTC decoder honouring register B's binary and 12-hour bits (`DateTimeSettings dt-from-rtc` already does) |
| TLS `cal-now` | derived from the clock in UTC, so certificate validity is checked against real time |
| X.509, JWT, NTP, SNTP, OPC UA | unchanged: UTC by specification |

## 6. Oracles (L-ORACLE)

Two implementations we did not write, both on this box:

- Python 3.10 `zoneinfo` (`D:\AI\DiffusionForge\system\python\python.exe`)
  with the pip `tzdata` package pinned to the vendored release and
  installed into scratch: `python.exe -I -m pip install --target <dir>
  tzdata==2026.6` (2026.6 is release 2026f; the installed one is 2023d),
  then `-PyTarget <dir>`;
- .NET `TimeZoneInfo` under PowerShell 7, which resolves IANA names, as a
  second witness: its data is not IANA's, so a disagreement between the
  two oracles is investigated rather than taken as a failure of ours.

An oracle script prints, for every zone, every transition from 1970 to
2037 and the offset one second either side (L-SHAPE), and for chosen local
times at each transition the resolution (exact, ambiguous with both
instants, skipped with both bounds). The Codex test prints the same lines
from `TimeZone.codex`, and the two are compared byte for byte. The arms
include the shapes a hand table gets wrong: `Australia/Lord_Howe` (a
30-minute DST shift), `Asia/Kathmandu` (+5:45), `Asia/Kolkata` (+5:30, no
DST), `Antarctica/Troll` (a 2-hour shift), `Europe/Dublin` (negative DST in
the rules), `Africa/Casablanca` (DST suspended for Ramadan), `Pacific/Apia`
(a skipped calendar day in 2011), `America/Sao_Paulo` (DST abolished in
2019), `America/New_York` on 2026-11-01, and `Europe/London` on
2026-10-25 and 2027-03-28. A sabotage
control shifts one rule's change time by an hour, and the comparison must
name that zone and that transition.

## 7. Rungs

**Built (2026-11-01):** `TzRules.codex` (the parser, `codex/test/tz-parse`:
598 names equal to the oracle's), `TzCompile.codex` (transitions,
`codex/test/tz-compile`: all 345 zones of 2026f, 23,137 transitions,
identical to zic's TZif through 2037), `TimeZone.codex` (`Zone`,
`utc-offset-at`, `utc-to-local`, `local-to-utc`, `local-to-utc-choosing`
with `AfterGap` for T5, `tz-zone-to-bytes` / `tz-zone-of-bytes`;
`codex/test/tz-local`: 92,548 probes identical to Python's fold 0 and
fold 1), `DateTime.day-of-week` corrected (`codex/test/datetime-weekday`),
and blu's RTC decoder and `TIMEZONE.DAT` v2 (`GopRtc rtc-civil`,
`GopZone`, main 52603). The oracle commands are
`build/tz-oracle.ps1 -Release 2026f -PyTarget <pip tzdata target> -Names | -Compiled | -Local -Codex <test output>`.

Also built: instants after 2037 from each zone's footer (`codex/test/tz-future`:
13,140 transitions 2038-2100, identical to zic's footer, through the CTZ2
byte round trip), and `TZDB.BIN` (T1): `tz-db-build` and `tz-db-zone` in
`TimeZone.codex`, written by `build/tz-db.ps1 -Kernel <cdx> -Out <file>` from
`tools/tz-db.codex`, tracked at `codex/foreword/core/tzdata/2026f/TZDB.BIN`
(412,908 bytes; `codex/test/tz-db` prints the same SHA-256 for the bytes
built in the guest, and refuses a missing name, a truncated file and a wrong
magic). An alias answers under its own name.

`TZDB.BIN` is on the desk image's ESP root (`build/build-boot-img.ps1`, the
font-pack path; the release directory is named there and in its generator).
`GopTzdb gtz-load` reads one zone from it, and `rtc-utc-boot` turns the RTC
into UTC through `TIMEZONE.DAT`'s flag and zone (`codex/test/apps/gop-tzdb`).

Rung 3 built: the desk clock block holds the machine zone as (instant,
offset) integers loaded from `TZDB.BIN` at boot and on a pick, and the band,
face and art take time and date from it (`clock-zone-table`); the first-boot
wizard picks a `GopZones` row by IANA name (`wizard-zone-pick`); entering a
time is local, and a repeated or skipped time waits for the user's choice
(`clock-entry-choice`); the World list prices each row of the shown stripe
from `TZDB.BIN`, cached in the clock block once per UTC hour or stripe
(`clock-world-rows`). The hand summer-time rules are deleted: `GopZones` keeps
a row's IANA name, city, base offset and stripe, and with no zone data the
desk shows the base offset.

A scheduled task carries its zone and `LocalChoice` (`sched-task-zoned`, shown
on its row); `sched-next-in` notes a repeated or skipped hour and the outcome,
or each night `Refuse` passed and why, and the run record carries the note
the run was scheduled under (`cvmm-schedule-record`).

The desk Calendar pane's "today" is the clock's local date (`clk-local-ymd`),
and `GopScene` and `DevConsoleBoot` read the RTC through `rtc-civil`;
`Core/BootPaint` honours the 12-hour bit (`bootpaint-rtc-hour`). The live
desk read on images naming `Europe/London` and `Asia/Kathmandu`, RTC frozen
either side of 2026-10-25 01:00 UTC, agrees with .NET `TimeZoneInfo` on the
taskbar, the clock's location line and the World rows.

Rungs 4 to 6 built. Rung 4: `CalendarPage` takes
February from the Gregorian rule (it declares `opening`, so no test can cite
it, L-UNCITABLE; graded through a scratch copy); the cvmm `Calendar` day
view shows the real weekday (`cvmm-calendar-weekday`); `TaskScheduler`
`sched-next-run-in` runs a daily or weekly schedule at its local time in a
zone with a `LocalChoice` (`cvmm-schedule-zoned`); cvmm `Clock`
`epoch-to-tod-in` reads a zone (`cvmm-clock-zone`); `Schedule.sched-next`
carries the weekday and day of month past midnight (`schedule-midnight`).
`DateTimeSettings` holds a `Zone` in place of its 9 fixed offsets, keeps its
clock fields in UTC (T3) and shows local time with the zone's abbreviation
(`datetime-settings-zone`: London across the 2026-10-25 fall-back, Kathmandu
past local midnight); cvmm `ClockState` holds a `Zone` (`cvmm-clock-zone`).
Rung 5: `DateTime.unix-to-x509-now` turns Unix seconds into the 14-digit
UTC string `x509-valid-at` and `TlsEndpoint`'s `cal-now` take
(`dt-civil-of-unix`, correct before 1970; `codex/test/datetime-x509-now`);
`DateTimeSettings` `dt-x509-now` gives the same 14 digits from the RTC-read UTC
clock, and no digits from a clock that was not read, which every certificate
refuses as not yet valid (`datetime-x509-clock`, graded through
`X509Chain`'s `x509-window`); `tools/https-client` reads the RTC through
`GopRtc` and refuses to fetch on an unread clock. Rung 6: `build/tz-update.ps1`
vendors a release from its IANA tarball and an operator-supplied SHA-256 (the
rule files, `LICENSE`, `MANIFEST.txt`, `TzSource.codex`, `TZDB.BIN`) and
prints the Perforce, golden and oracle steps left to the operator;
`-Check` proves the vendored release reproduces byte for byte (2026f does).
The rule files and `LICENSE` are Perforce `binary`, so every workspace holds
IANA's bytes and `MANIFEST.txt` verifies on Windows clients too.

**Open** (section 5 rows not yet met):

- `HumanApprovalGrant` week windows (waits on that design's rung 4, root's
  ruling 2026-11-01): the clause's zone is kept as text and
  nothing resolves it through `tz-db-zone`; the conduit's `now` bridge
  (`apps/accp/serve.ps1`) reports the host's local week-minute rather than
  the grant zone's; no window edge in a gap or a fold is refused.

## 8. Rulings

Damian, 2026-11-01: "all recommended for time zones", on root's picks: T1 (a)
`TZDB.BIN` loaded per zone; T2 the full recorded history; T3 a per-machine
flag naming the zone when the RTC holds local time, UTC otherwise; T4 the
newest IANA release at the rung 1 CL, then the rung 6 update procedure; T5
`Earlier` on a repeat, the first instant after a gap, never a silent skip,
stated on every schedule and changeable per schedule. The options as first
written follow.

- **T1, delivery.** (a) a generated `TZDB.BIN` loaded per zone (recommended),
  or (b) a generated Codex chapter of literals.
- **T2, range.** 1970 to 2037 plus each zone's footer rule, or the full
  recorded history back to the 1800s.
- **T3, the RTC.** The tree reads the RTC as UTC. A machine dual-booted with
  Windows keeps local time in the RTC by default; either the desk keeps UTC
  and documents the Windows registry setting, or a per-machine flag says the
  RTC holds local time in a named zone.
- **T5, a recurring schedule at a transition.** What an unattended daily
  01:30 does when 01:30 repeats or does not exist. Recommended default,
  stated on every schedule and changeable per schedule: on a repeat, fire
  once at the first 01:30 (`Earlier`); in a gap, fire at the first instant
  after the gap; never silently skip. The alternatives are `Later` and
  `Refuse` (does not fire that night).
- **T4, release.** Which IANA release to vendor first: the newest at the
  time of the rung 1 CL, with the update procedure of rung 6 for every
  later one.

## 9. Costs (R-COST)

`utc-offset-at` is a binary search over one zone's transitions, logarithmic
in a table of at most a few hundred entries; `local-to-utc` evaluates at
most two candidate offsets. The desk loads one zone's record, not the
database. `TzCompile` runs at build time over the rule text, linear in its
line count (the text's size is measured at rung 1).
