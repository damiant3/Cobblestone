#!/usr/bin/env python3
"""Ring-fed zig plug runner: feed an IR-CCE file into ringplug.cdx over
the serial ring (the compiler's own intake) and save the decoded zig
text. The TCP transport costs ~130 bytes of guest heap per IR byte
before the parser sees anything (IrmemHarness); the ring intake is a
machine-code loop at one byte per byte, which is what admits IRs past
the TCP ceiling (fibx at 11.2 MB, compiler.ir at 15.8 MB).

The guest mimics the compiler's text-plug wire (SIZE: line + raw CCE),
so compile_ring's capture works unchanged with the plug cdx as the
kernel. The payload is decoded host-side by cce.py; any byte >= 97
(multibyte CCE, unimplemented there) fails the run rather than leaving
placeholders in a .zig file.

Usage: plug_run_ring.py <ir> <out.zig> [plug_cdx]
"""
import pathlib
import re
import sys

import ring_compile
from cce import decode


def run_ring_plug(ir_path, out_path, plug_cdx=None, mem_mb=3072, timeout=1800):
    here = pathlib.Path(__file__).parent
    plug_cdx = plug_cdx or str(here / "ast" / "ringplug.cdx")
    ir = open(ir_path, "rb").read()
    if b"\x00" in ir:
        raise SystemExit(f"{ir_path}: contains NUL; read-serial-cce would stop early")
    blob_path = str(out_path) + ".blob"
    with open(blob_path, "wb") as f:
        f.write(b"RING zig\n" + ir + b"\x00")
    cce_path = str(out_path) + ".cce"
    ok = ring_compile.compile_ring(blob_path, cce_path, mem_mb=mem_mb,
                                   timeout=timeout, seed=plug_cdx)
    if not ok:
        return False
    payload = open(cce_path, "rb").read()
    text = decode(payload)
    bad = re.search(r"<\d+>", text)
    if bad:
        off = bad.start()
        raise SystemExit(f"{cce_path}: undecodable CCE byte near char {off}: "
                         f"...{text[max(0, off - 40):off + 40]}...")
    open(out_path, "w").write(text)
    print(f"wrote {out_path} ({len(text)} chars from {len(payload)} CCE bytes)")
    return True


if __name__ == "__main__":
    ok = run_ring_plug(sys.argv[1], sys.argv[2],
                       sys.argv[3] if len(sys.argv) > 3 else None)
    sys.exit(0 if ok else 1)
