#!/usr/bin/env python3
"""Send a simple Codex program to the RISC-V bare metal compiler via QEMU serial."""

import subprocess, sys, time

QEMU = "qemu-system-riscv64 -machine virt -nographic -bios none"
BIN = "/mnt/d/Projects/NewRepository-cam/tools/_all-source.bin"
TIMEOUT = 30

SOURCE = "main : Integer\nmain = 42\n"

proc = subprocess.Popen(
    ["wsl", "-e", "bash", "-c", f"{QEMU} -kernel {BIN}"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
)

# Boot time
time.sleep(2)

# Send source + EOT
payload = SOURCE.encode("ascii") + b"\x04"
print(f"Sending {len(payload)}B: {SOURCE.strip()!r}")
proc.stdin.write(payload)
proc.stdin.flush()

try:
    stdout, stderr = proc.communicate(timeout=TIMEOUT)
    out = stdout.decode("utf-8", errors="replace")
    print(f"\nOutput ({len(out)} chars):")
    print(out[:3000] if out else "(empty)")
except subprocess.TimeoutExpired:
    proc.kill()
    stdout, _ = proc.communicate()
    out = stdout.decode("utf-8", errors="replace")
    print(f"\nTIMEOUT after {TIMEOUT}s. Output so far ({len(out)} chars):")
    print(out[:3000] if out else "(empty)")
