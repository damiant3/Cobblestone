#!/usr/bin/env python3
"""IR (or .codex) to a signed arm64 Mach-O on this Mac.

  CODEX_ROOT=... SANDBOX=... ./compile-macho.py eight-queens.codex -o queens

Seed compiles the source to IR. The ARM64 plug (built once into the
sandbox) emits Darwin wire. wrap_macho.py links and codesigns.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def need_env(name: str) -> str:
    v = os.environ.get(name)
    if not v:
        raise SystemExit(f"set {name}")
    return v


def qemu_root() -> Path:
    named = os.environ.get("COBBLESTONE_QEMU")
    if named:
        return Path(named).expanduser().resolve()
    sib = Path(need_env("CODEX_ROOT")).resolve().parent / "cobblestone-qemu"
    if sib.is_dir():
        return sib
    raise SystemExit("set COBBLESTONE_QEMU to the cobblestone-qemu checkout")


def encode_cce_ascii(s: str) -> bytes:
    sys.path.insert(0, str(qemu_root()))
    import cce
    inv = {ch: b for b, ch in cce.TABLE.items()}
    out = bytearray()
    for ch in s:
        if ch not in inv:
            raise SystemExit(f"no CCE for {ch!r}")
        out.append(inv[ch])
    return bytes(out)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("-o", "--out", required=True)
    args = ap.parse_args()

    root = Path(need_env("CODEX_ROOT"))
    sandbox = Path(need_env("SANDBOX"))
    sandbox.mkdir(parents=True, exist_ok=True)
    qemu = qemu_root()
    sys.path.insert(0, str(qemu))
    os.environ.setdefault("PYTHONDONTWRITEBYTECODE", "1")

    pwsh = os.environ.get("PWSH", "/opt/homebrew/bin/pwsh")
    src = Path(args.src).resolve()
    work = sandbox / "macho"
    work.mkdir(exist_ok=True)

    plug_src = sandbox / "arm64plug-source.codex"
    plug_cdx = sandbox / "arm64plug.cdx"
    if not plug_cdx.is_file():
        print("############ arm64 plug bundle", flush=True)
        subprocess.check_call(
            [pwsh, "-NoProfile", "-File", str(HERE / "bundle-qemu.ps1")],
            env=dict(os.environ, CODEX_ROOT=str(root), SANDBOX=str(sandbox)),
        )
        blob = sandbox / "arm64plug-cdx.blob"
        raw = plug_src.read_bytes()
        blob.write_bytes(b"CDX map\n" + raw + b"\x04")
        print(f"############ arm64 plug compile ({len(raw)} bytes)", flush=True)
        import ring_compile
        ok = ring_compile.compile_ring(str(blob), str(plug_cdx))
        if not ok or not plug_cdx.is_file():
            raise SystemExit("ARM64 plug compile failed")

    ir = work / "prog.ir"
    if src.suffix == ".ir":
        ir.write_bytes(src.read_bytes())
    else:
        print("############ compile to IR (seed, QEMU)", flush=True)
        src_blob = work / "prog-ir.blob"
        src_blob.write_bytes(b"IR-CCE decks=172\n" + src.read_bytes() + b"\x04")
        import ring_compile
        ok = ring_compile.compile_ring(str(src_blob), str(ir))
        if not ok or not ir.is_file():
            raise SystemExit("IR compile failed")

    print("############ ARM64 darwin wire (plug, QEMU)", flush=True)
    mode = encode_cce_ascii("IR-CCE darwin") + bytes([1])
    feed = work / "prog-darwin.blob"
    feed.write_bytes(mode + ir.read_bytes() + b"\x00")
    wire = work / "prog.wire"
    import ring_compile
    ok = ring_compile.compile_ring(
        str(feed), str(wire), seed=str(plug_cdx), sentinel=b"WIRE-END"
    )
    if not ok or not wire.is_file() or wire.stat().st_size == 0:
        raise SystemExit("ARM64 darwin emit failed")

    print("############ wrap Mach-O", flush=True)
    out = Path(args.out).resolve()
    subprocess.check_call([sys.executable, str(HERE / "wrap_macho.py"), str(wire), "-o", str(out)])
    print(f"wrote {out}", flush=True)


if __name__ == "__main__":
    main()
