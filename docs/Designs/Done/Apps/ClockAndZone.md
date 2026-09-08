# The Clock, its face, and where in the world it is

Damian, 2026-09-07, at the running desk:

> make that clock app beautiful with clock face and timezone picker with app
> like the old days that showed the mercader projection and you picked yours
> stripe, then your specific local so you do all the adjusting for daylight
> savings and other regional stuff. Also add that, and allow picking of
> different default locale formats for like, if you are not american you use
> that backwards time format that tries to be clever but isn't.

Four things, and only the first is what the app has today.

## What is there

All five stages are landed (val, 2026-09-07) and the campaign is closed.
`apps/works/GopLocale.codex` is the calendar and the formats, `GopZones.codex`
the zone table with its rules and stripes, `GopZone.codex` the record on the
stick, `GopWorld.codex` the Mercator picture, and `GopClock.codex` the pane
with its face, world and formats views and the editor behind them. The offset
stays a DISPLAY offset, never written to the part; the stored hour byte stays
biased by twelve.

## The stage list

| | stage | what it is | state |
|---|---|---|---|
| 1 | the zone MODEL | offsets in minutes, a zone table, DST rules, and the locale formats. Pure arithmetic over a civil date | **DONE, val 22884.** Arm `codex/test/apps/clock-locale` |
| 2 | the record on the stick | `CTZ2`: zone id and format ids beside the offset, readable by a `CTZ1` reader and vice versa | **DONE, val 22906.** Arm `codex/test/apps/clock-encode-test` |
| 3 | the FACE | an analogue clock, hands and ticks, drawn from a 6 degree table | **DONE, val 22888.** Arm `codex/test/apps/clock-face` |
| 4 | the MAP | a Mercator world, 24 stripes, click a stripe to filter the list | **DONE, val 22894.** Arm `codex/test/apps/clock-world` |
| 5 | the pane | face, map and list in one window with the editor behind it | **DONE, val 22906**, painted by `desk-clock-art` (arm `codex/test/apps/desk-clock-art`, main 23040); the taskbar clock reads the zone (main 23066, arm `clock-band`) |

The four model arms re-run green on seed `48F973C8` (2026-09-08).

Stages 1 and 2 are the ones the other three are built on and are the ones that
can be wrong invisibly, so they go first and they carry the arms.

## Stage 1. The model

### Offsets are MINUTES

`zone-min`/`zone-max` become -720 and +840. Every existing caller reads whole
hours, so the two are kept as hours and a minutes pair is added beside them
rather than the hour form being deleted underneath the wizard.

The half-hour and three-quarter-hour zones are not exotic and are not
negotiable: India, Iran, Afghanistan, Myanmar, central Australia, Newfoundland,
Chatham, the Marquesas. A design that cannot say +05:30 cannot say where a
sixth of the world's people live.

### A zone is a name, a base offset, and a RULE

```
ZoneRow = { zn-name, zn-city, zn-base-minutes, zn-rule, zn-stripe }
```

`zn-stripe` is which of the 24 map bands the zone is drawn in, which is the
base offset rounded to the nearest hour and NOT the DST offset: a stripe that
moved twice a year would make the map lie for half the year.

The rules are the shapes, not the countries, because the countries change and
the shapes do not:

| rule | starts | ends | who |
|---|---|---|---|
| `zr-none` | -- | -- | most of Asia, Africa, the tropics |
| `zr-eu` | last Sunday March 01:00 UTC | last Sunday October 01:00 UTC | Europe |
| `zr-us` | second Sunday March 02:00 local | first Sunday November 02:00 local | North America |
| `zr-au` | first Sunday October 02:00 local | first Sunday April 03:00 local | south-east Australia |
| `zr-nz` | last Sunday September 02:00 | first Sunday April 03:00 | New Zealand |
| `zr-south` | third Sunday October | third Sunday February | southern generic |

The southern rules are the reason this is a rule id and not a pair of month
numbers: below the equator the interval WRAPS the year end, so "is it between
the two dates" is the wrong test and answers backwards for exactly the people
who most need it to be right.

### DST is decided on the civil date, and the day of week is computed

There is no calendar in the tree that answers "the last Sunday in March", so
this stage brings a day-of-week (Sakamoto, which is a table and three
divisions) and an nth-weekday-of-month. Both are pure and both get arms with
hand-checked dates, because a day-of-week that is off by one is off by one on
EVERY date and looks entirely plausible on any single one.

### Locale formats are two independent choices

Damian named the clock; the date is the same problem and is worse.

- **The clock**: `lf-24` or `lf-12`. Twelve carries AM and PM and prints 12
  rather than 0 at midnight.
- **The date order**: `lf-dmy` (most of the world), `lf-mdy` (the United
  States), `lf-ymd` (China, Japan, Korea, and ISO 8601).

They are independent because they vary independently: Canada writes a 12 hour
clock with a Y-M-D date, and the pane has no business coupling them.

## Stage 2. The record

`CTZ2`, sixteen bytes, and the first eight are byte-for-byte what `CTZ1` wrote:

```
0..3   magic 'C' 'T' 'Z' '1'      unchanged, so a CTZ1 reader still works
4      whole-hour offset + 12     unchanged, rounded from the minutes
5..7   zero                       unchanged
8..9   offset in minutes + 720    little endian
10..11 zone id                    little endian, 0 means "just the offset"
12     clock format
13     date format
14..15 zero
```

A `CTZ1` reader takes the first eight bytes and gets the same whole-hour zone
it always did. A `CTZ2` reader that finds a short file takes the hour offset
and defaults the rest. Neither has to know about the other, which is the whole
reason the magic does not change.

## What this design does NOT claim

- **Not a tzdata port.** The table is a few dozen zones with the rule they
  follow today, hand written, dated, and wrong the moment a government moves a
  date. It is not a database and must not grow into one behind a name that
  says it is; a zone whose rule changed is a one line edit and a re-measure.
- **Historical dates are not right.** The rules are applied to every year,
  including years before the rule existed. The clock shows now, and nothing
  here stamps a file.
- **The map is a picture, not a projection engine.** It is a coarse bitmap
  drawn once. Nothing computes a Mercator from coordinates, and a click picks
  a STRIPE by x, not a country by shape.
- **No leap seconds, no sub-minute offsets, no lunar calendars.**
