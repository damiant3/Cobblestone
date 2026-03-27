#!/usr/bin/env python3
"""Measure bare metal compilation scaling: 2, 4, 8, 16, 32 functions."""
import subprocess, time, tempfile, os

KERNEL = "/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"

for N in [2, 4, 8, 16, 32]:
    # Build source
    lines = []
    for i in range(1, N+1):
        lines.append(f"f{i} : Integer -> Integer")
        lines.append(f"f{i} (x) = x + {i}")
        lines.append("")
    lines.append("main : Integer")
    lines.append("main = f1 0")
    src = "\n".join(lines) + "\x04"

    # Run QEMU, feed source via stdin, capture first byte of output
    t0 = time.time()
    proc = subprocess.Popen(
        ["qemu-system-x86_64", "-kernel", KERNEL, "-serial", "stdio",
         "-display", "none", "-no-reboot", "-m", "512"],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    proc.stdin.write(src.encode("latin-1"))
    proc.stdin.flush()
    # Read until we see 'u' from 'using System;' (first output byte)
    while True:
        b = proc.stdout.read(1)
        if b == b'u' or b == b'':
            break
    t1 = time.time()
    proc.kill()
    print(f"N={N:3d}: {t1-t0:.2f}s")
