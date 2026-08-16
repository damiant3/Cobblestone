# CCE (Compressed Codex Encoding) decode, enough to read an IR blob as text
# and to locate a corrupted byte by its offset.
# Bands per codex/foreword/core/CCE.codex: 1 LF, 2 space, 3..12 digits,
# 13..38 lowercase and 39..64 uppercase in frequency order, 65..96 punct.
LOWER = "etaoinshrdlcumwfgypbvkjxqz"
PUNCT = ' !"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~'

def _table():
    t = {}
    t[0], t[1], t[2] = "\0", "\n", " "
    for i in range(10):
        t[3 + i] = chr(ord("0") + i)
    for i, ch in enumerate(LOWER):
        t[13 + i] = ch
        t[39 + i] = ch.upper()
    return t

TABLE = _table()

def decode(bs):
    return "".join(TABLE.get(b, f"<{b}>") for b in bs)

def window(bs, off, radius=60):
    lo, hi = max(0, off - radius), min(len(bs), off + radius)
    return decode(bs[lo:off]), decode(bs[off:off + 1]), decode(bs[off + 1:hi])

if __name__ == "__main__":
    import sys
    data = open(sys.argv[1], "rb").read()
    if len(sys.argv) > 2:
        for a in sys.argv[2:]:
            off = int(a)
            pre, at, post = window(data, off)
            print(f"-- offset {off}: byte {data[off]} = {at!r}")
            print(f"   ...{pre}[{at}]{post}...")
    else:
        sys.stdout.write(decode(data))
