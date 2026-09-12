#!/usr/bin/env python3
"""Wrap an ARM64 plug wire as a Mach-O that ld will accept.

The wire is the same layout compile-arm64.ps1 parses. Data is placed at
align-8(code), matching a64-patch-rodata. A four-instruction trampoline
is _start so dyld has a symbol; it branches into the image at the
recorded __start (or the first function).
"""
from __future__ import annotations

import argparse
import os
import struct
import subprocess
import sys
import tempfile
from pathlib import Path


def parse_wire(raw: bytes) -> tuple[bytes, bytes, int]:
    wire_off = 0
    for wi in range(min(64, max(0, len(raw) - 12))):
        cl, dl, fc = struct.unpack_from("<iii", raw, wi)
        if 0 < cl < 16_000_000 and 0 <= dl < 1_000_000 and 0 < fc < 10_000:
            if wi + 12 + cl + dl <= len(raw) + 64:
                wire_off = wi
                break
    wire = raw[wire_off:]
    if len(wire) < 12:
        raise SystemExit("wire too short")
    code_len, data_len, func_count = struct.unpack_from("<iii", wire, 0)
    code = wire[12 : 12 + code_len]
    data = wire[12 + code_len : 12 + code_len + data_len]
    pos = 12 + code_len + data_len
    entry = 0
    seen = False
    for _ in range(func_count):
        if pos + 6 > len(wire):
            break
        (name_len,) = struct.unpack_from("<H", wire, pos)
        name = wire[pos + 2 : pos + 2 + name_len]
        (off,) = struct.unpack_from("<i", wire, pos + 2 + name_len)
        # Names on the wire are CCE, not ASCII. The first recorded
        # function is __start on the Darwin path (offset 0 is real).
        if not seen:
            entry = off
            seen = True
        pos += 2 + name_len + 4
        _ = name
    if len(code) != code_len:
        raise SystemExit(f"short code: {len(code)} of {code_len}")
    return code, data, entry


def image_bytes(code: bytes, data: bytes) -> bytes:
    pad = (8 - (len(code) % 8)) % 8
    return code + (b"\x00" * pad) + data


def write_and_link(image: bytes, entry: int, out: Path) -> None:
    sdk = subprocess.check_output(["xcrun", "-sdk", "macosx", "--show-sdk-path"], text=True).strip()
    with tempfile.TemporaryDirectory() as td:
        tdir = Path(td)
        (tdir / "image.bin").write_bytes(image)
        # entry is a BYTE offset in the image (func table stores insn*4? )
        # a64-record-func stores st.insn-count, and compile-arm64 uses it as
        # a byte offset added to the load address after *4 in the ELF path.
        # Check: a64-record-func stores insn-count. ELF: entryOffset used as
        # loadAddr + textStart + entryOffset -- and Stdio says they scale *4
        # for ELF. The wire offset is the raw stored value.
        #
        # compile-arm64.ps1: $entryOffset = $foff from the 4B offset field,
        # then $entry = $loadAddr + $textStart + $entryOffset
        # Arm64Stdio: "a64-record-func stores insn-count, an INSTRUCTION
        # INDEX, and a64-build-elf adds its argument to the load address as
        # BYTES, so the index is scaled by 4 here."
        #
        # The host ELF wrap in compile-arm64.ps1 does NOT scale -- it uses
        # $foff raw. The first function after the vector table is a large
        # insn index * 4 if stored as index... I need to check a64-record-func.
        asm = tdir / "crt.s"
        asm.write_text(
            f""".globl _start
.p2align 2
_start:
    adrp x17, _codex_image@PAGE
    add x17, x17, _codex_image@PAGEOFF
    add x17, x17, #{entry}
    br x17

.globl _codex_image
.p2align 3
_codex_image:
    .incbin "image.bin"
"""
        )
        obj = tdir / "crt.o"
        subprocess.check_call(["as", "-arch", "arm64", "-o", str(obj), str(asm)], cwd=td)
        subprocess.check_call(
            [
                "ld",
                "-o",
                str(out),
                str(obj),
                "-lSystem",
                "-syslibroot",
                sdk,
                "-e",
                "_start",
                "-arch",
                "arm64",
            ]
        )
    subprocess.check_call(["codesign", "-s", "-", "-f", str(out)])


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("wire")
    ap.add_argument("-o", "--out", required=True)
    args = ap.parse_args()
    code, data, entry = parse_wire(Path(args.wire).read_bytes())
    img = image_bytes(code, data)
    write_and_link(img, entry, Path(args.out))
    print(f"macho {args.out}  image={len(img)} entry={entry}")


if __name__ == "__main__":
    main()
