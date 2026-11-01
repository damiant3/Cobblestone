# tz-oracle.py -- one listing per zone from Python's zoneinfo over the pip
# tzdata package (zic's TZif output), for comparison with codex/test/tz-*.
#
#   python -I tz-oracle.py <zone> [<zone> ...]   (or "all")
#
# Per zone: "Z <name>", "I <utoff> <abbr>" (the type before the first
# transition), then "T <utc seconds> <utoff> <abbr>" for every change of
# offset or abbreviation before 2038-01-01 UTC. The TZif transitions come
# from the pure-Python reader's tables; the years after the last one come
# from the file's footer rule, found by a day scan and bisected to the second.
# Run with PYTHONPATH naming the pip tzdata target.
import bisect
import sys
from datetime import datetime, timedelta, timezone
import zoneinfo
from zoneinfo import _zoneinfo

LIMIT = 2145916800
EPOCH = datetime(1970, 1, 1, tzinfo=timezone.utc)


def kind(z, t):
    d = (EPOCH + timedelta(seconds=t)).astimezone(z)
    return (int(d.utcoffset().total_seconds()), d.tzname())


def listing(name, limit=LIMIT):
    z = _zoneinfo.ZoneInfo.no_cache(name)
    out = ["Z " + name]
    before = z._tti_before
    if before is None:
        before = z._ttinfos[0] if z._ttinfos else None
    if before is None:
        last = kind(z, 0)
    else:
        last = (int(before.utcoff.total_seconds()), before.tzname)
    out.append("I %d %s" % last)
    t_last = None
    for t, tti in zip(z._trans_utc, z._ttinfos):
        if t >= limit:
            break
        k = (int(tti.utcoff.total_seconds()), tti.tzname)
        if k != last:
            out.append("T %d %d %s" % (t, k[0], k[1]))
            last = k
        t_last = t
    after = z._tz_after
    if isinstance(after, _zoneinfo._ttinfo):
        k = (int(after.utcoff.total_seconds()), after.tzname)
        if k != last and t_last is not None:
            out.append("T %d %d %s" % (t_last + 1, k[0], k[1]))
        return out
    t = (t_last + 1) if t_last is not None else -2208988800
    if t_last is None:
        last = kind(z, t)
    while t < limit:
        nxt = min(t + 86400, limit)
        k = kind(z, nxt) if nxt < limit else last
        if k != last:
            lo, hi = t, nxt
            while hi - lo > 1:
                mid = (lo + hi) // 2
                if kind(z, mid) == last:
                    lo = mid
                else:
                    hi = mid
            out.append("T %d %d %s" % (hi, k[0], k[1]))
            last = k
        t = nxt
    return out


def offsets_of(name):
    # The offset in force from each transition on, read from this script's own
    # listing (zic's TZif table, then the footer's bisected transitions), so the
    # round-trip check never goes through astimezone's fold detection, which
    # mislabels a fold under a minute wide (Asia/Riyadh 1947) and the first fold
    # after a table ends (America/Ciudad_Juarez 2022-11-30).
    starts, offs, initial = [], [], None
    for line in listing(name):
        p = line.split(" ")
        if p[0] == "I":
            initial = int(p[1])
        elif p[0] == "T":
            starts.append(int(p[1]))
            offs.append(int(p[2]))
    return starts, offs, initial


def utc_offset(table, u):
    starts, offs, initial = table
    i = bisect.bisect_right(starts, u)
    return initial if i == 0 else offs[i - 1]


def resolve(z, table, local):
    base = datetime(1970, 1, 1) + timedelta(seconds=local)
    us = []
    for f in (0, 1):
        off = int(base.replace(tzinfo=z, fold=f).utcoffset().total_seconds())
        us.append(local - off)
    if us[0] == us[1]:
        return "E %d" % us[0]
    back = [utc_offset(table, u) + u == local for u in us]
    if back[0] and back[1]:
        return "A %d %d" % (min(us), max(us))
    return "S %d %d" % (us[0], us[1])


def local_lines(name):
    z = _zoneinfo.ZoneInfo.no_cache(name)
    table = offsets_of(name)
    out = []
    prev = None
    for line in listing(name):
        p = line.split(" ")
        if p[0] == "I":
            prev = int(p[1])
        elif p[0] == "T":
            t, b = int(p[1]), int(p[2])
            for loc in (t + prev - 1, t + prev, t + b - 1, t + b):
                out.append("L %d %s" % (loc, resolve(z, table, loc)))
            prev = b
    return out

if __name__ == "__main__":
    names = sys.argv[1:]
    if names == ["all"]:
        names = sorted(zoneinfo.available_timezones())
    for n in names:
        print("\n".join(listing(n)))
