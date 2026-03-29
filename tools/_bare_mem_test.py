#!/usr/bin/env python3
"""Test bare metal self-compile at various memory sizes."""
import subprocess, time, sys, threading, os

KERNEL = "/mnt/d/Projects/NewRepository-cam/Codex.Codex/out/Codex.Codex.elf"
SOURCE = "/mnt/d/Projects/NewRepository-cam/tools/_all-source.codex"

with open(SOURCE, "rb") as f:
    src = f.read()

filename = SOURCE.encode() + b"\n"

for mem in [512, 256, 128, 64, 32, 16, 8]:
    r_fd, w_fd = os.pipe()
    proc = subprocess.Popen(
        ["qemu-system-x86_64", "-kernel", KERNEL, "-serial", "stdio",
         "-display", "none", "-no-reboot", "-m", str(mem)],
        stdin=r_fd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    os.close(r_fd)

    def send():
        try:
            time.sleep(1)
            os.write(w_fd, filename)
            time.sleep(0.1)
            i = 0
            while i < len(src):
                os.write(w_fd, src[i:i+4096])
                i += 4096
                time.sleep(0.02)
            os.write(w_fd, b"\x04")
            time.sleep(120)
        except: pass
        finally: os.close(w_fd)

    sender = threading.Thread(target=send, daemon=True)
    sender.start()

    t0 = time.time()
    output = b""
    stall_start = None
    import select
    while time.time() - t0 < 120:
        ready, _, _ = select.select([proc.stdout], [], [], 1.0)
        if ready:
            b = proc.stdout.read(1)
            if b == b"": break
            output += b
            stall_start = None
        else:
            if stall_start is None: stall_start = time.time()
            elif time.time() - stall_start > 5: break

    proc.kill()
    elapsed = time.time() - t0
    status = "OK" if len(output) > 1000 else "FAIL"
    print(f"  -m {mem:>4}: {len(output):>7} bytes in {elapsed:.1f}s — {status}")
    if status == "FAIL":
        break
