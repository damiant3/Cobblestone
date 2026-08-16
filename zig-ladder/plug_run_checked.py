# Verified plug transfer.
#
# The guest drops the last byte of any odd-length frame: net-recv-raw derives
# its REP INSW word count with `shr rcx, 1`, which rounds down, and returns
# the full length anyway, so the caller reads one stale byte left by the
# previous frame. Silent -- the plug still reports "OK defs=N" and emits
# plausible source with a mangled identifier. See
# NewRepository/deck-record-repro/PLUG_IR_TRANSPORT.md.
#
# So we do not vote, we prove. Frame parity follows TCP payload parity
# (headers are even), plug_run keeps every write even and pads the wire total
# to even, and afterwards we read the capture back and require that no
# host->guest frame had an odd body. No odd frame means this defect cannot
# have fired.
#
# The old two-chunk-sizes-must-agree route is kept as a fallback. It is
# several times more expensive and only ever gave a vote, but it does not
# depend on our reading of the mechanism being right.
import hashlib
import os
import sys
import tempfile

import pcap_parity
import plug_run

# Fallback only. Non-256-aligned sizes, all distinct, so agreement between
# two of them is meaningful.
CHUNK_SIZES = [3000, 1500, 3500, 2500]

def run_verified(plug_cdx, ir_path, out_path, port=9145, attempts=3,
                 timeout=420, chunk_size=4096):
    """One transfer plus a proof. Falls back to agreement if the proof keeps
    failing, which would mean the kernel is segmenting somewhere we did not
    expect."""
    for i in range(attempts):
        fd, tmp = tempfile.mkstemp(suffix=".plug-attempt")
        os.close(fd)
        fd, pcap = tempfile.mkstemp(suffix=".plug-pcap")
        os.close(fd)
        try:
            if not plug_run.run_plug(plug_cdx, ir_path, tmp, port=port,
                                     chunk_size=chunk_size, timeout=timeout,
                                     pcap=pcap):
                print(f"[verified] attempt {i + 1}: transfer failed")
                continue
            odd = pcap_parity.odd_bodies(pcap, port)
            blob = open(tmp, "rb").read()
            h = hashlib.md5(blob).hexdigest()[:12]
            if not odd:
                open(out_path, "wb").write(blob)
                print(f"[verified] attempt {i + 1}: {h} {len(blob)} bytes, "
                      f"0 odd frames -> {out_path}")
                return True
            print(f"[verified] attempt {i + 1}: {h} -- {len(odd)} odd frame(s) "
                  f"on the wire, output cannot be trusted; retrying")
            for seq, plen, eth in odd[:5]:
                print(f"           odd frame: stream offset {seq}..{seq + plen - 1}"
                      f" payload={plen} eth={eth}")
        finally:
            os.unlink(tmp)
            os.unlink(pcap)
    print("[verified] parity route exhausted; falling back to agreement")
    return run_agreed(plug_cdx, ir_path, out_path, port=port, timeout=timeout)

def run_agreed(plug_cdx, ir_path, out_path, port=9145, attempts=4, timeout=420):
    fd, tmp = tempfile.mkstemp(suffix=".plug-attempt")
    os.close(fd)
    seen = {}
    try:
        for i in range(attempts):
            cs = CHUNK_SIZES[i % len(CHUNK_SIZES)]
            if not plug_run.run_plug(plug_cdx, ir_path, tmp, port=port,
                                     chunk_size=cs, timeout=timeout):
                print(f"[agreed] attempt {i + 1} (chunk {cs}): transfer failed")
                continue
            blob = open(tmp, "rb").read()
            h = hashlib.md5(blob).hexdigest()[:12]
            print(f"[agreed] attempt {i + 1} (chunk {cs}): {h} {len(blob)} bytes")
            if h in seen and seen[h] != cs:
                open(out_path, "wb").write(blob)
                print(f"[agreed] AGREED across chunk sizes {seen[h]} and {cs}"
                      f" -> {out_path}")
                return True
            seen.setdefault(h, cs)
    finally:
        os.unlink(tmp)
    print("[agreed] FAILED: no two chunk sizes produced the same output.")
    return False

# Kept so existing callers (ast/oracle_lib.sh) keep working.
run_checked = run_verified

if __name__ == "__main__":
    ok = run_verified(sys.argv[1], sys.argv[2], sys.argv[3],
                      port=int(sys.argv[4]) if len(sys.argv) > 4 else 9145)
    sys.exit(0 if ok else 1)
