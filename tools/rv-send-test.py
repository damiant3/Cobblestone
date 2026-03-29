#!/usr/bin/env python3
"""Send a small Codex program to RISC-V bare metal compiler over serial."""

import subprocess, sys, time

QEMU = "qemu-system-riscv64 -machine virt -nographic -bios none"
BIN = "/mnt/d/Projects/NewRepository-cam/tools/_all-source.bin"
TIMEOUT = 30

# Simple test program
SOURCE = "main : Integer\nmain = 42\n"

def main():
    print(f"Sending {len(SOURCE)} bytes to RISC-V bare metal compiler...")
    print(f"Source: {SOURCE.strip()}")
    print()

    # Launch QEMU with stdin piped
    proc = subprocess.Popen(
        ["wsl", "-e", "bash", "-c",
         f"{QEMU} -kernel {BIN} -serial mon:stdio"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )

    # Give QEMU time to boot and reach the UART read loop
    time.sleep(2)

    # Send source followed by EOT (0x04)
    payload = SOURCE.encode("ascii") + b"\x04"
    print(f"Sending {len(payload)} bytes (including EOT)...")
    proc.stdin.write(payload)
    proc.stdin.flush()

    # Wait for output
    print(f"Waiting up to {TIMEOUT}s for compilation output...")
    try:
        stdout, stderr = proc.communicate(timeout=TIMEOUT)
        output = stdout.decode("utf-8", errors="replace")
        print(f"\n{'='*60}")
        print(f"STDOUT ({len(output)} chars):")
        print(output[:2000] if output else "(empty)")
        if stderr:
            err = stderr.decode("utf-8", errors="replace")
            print(f"\nSTDERR ({len(err)} chars):")
            print(err[:500])
    except subprocess.TimeoutExpired:
        proc.kill()
        stdout, stderr = proc.communicate()
        output = stdout.decode("utf-8", errors="replace")
        print(f"\nTIMEOUT after {TIMEOUT}s")
        print(f"Output so far ({len(output)} chars):")
        print(output[:2000] if output else "(empty)")

    return 0

if __name__ == "__main__":
    sys.exit(main())
