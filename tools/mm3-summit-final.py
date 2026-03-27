#!/usr/bin/env python3
"""MM3 Summit: send full compiler source, capture output via subprocess."""
import subprocess, time, sys, threading, os

KERNEL = "/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
SOURCE = "/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.codex"

with open(SOURCE, "rb") as f:
    src = f.read()

print(f"Sending {len(src)} bytes...", file=sys.stderr)

# Use os.pipe so we control both ends — no EOF until we close write end
r_fd, w_fd = os.pipe()

proc = subprocess.Popen(
    ["qemu-system-x86_64", "-kernel", KERNEL, "-serial", "stdio",
     "-display", "none", "-no-reboot", "-m", "512"],
    stdin=r_fd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

os.close(r_fd)  # We only need the write end

def send_source():
    try:
        time.sleep(1)
        i = 0
        while i < len(src):
            os.write(w_fd, src[i:i+4096])
            i += 4096
        os.write(w_fd, b"\x04")
        # Keep pipe open for 1 hour
        time.sleep(3600)
    except:
        pass
    finally:
        os.close(w_fd)

sender = threading.Thread(target=send_source, daemon=True)
sender.start()

t0 = time.time()
output = b""
while time.time() - t0 < 3600:
    b = proc.stdout.read(1)
    if b == b"":
        elapsed = time.time() - t0
        print(f"QEMU exited after {elapsed:.1f}s", file=sys.stderr)
        break
    output += b
    if len(output) % 10000 == 0:
        print(f"  ...{len(output)} bytes ({time.time()-t0:.1f}s)", file=sys.stderr)
    if b"Codex_Codex_Codex" in output:
        print(f"\nMM3 PROVEN in {time.time()-t0:.1f}s", file=sys.stderr)
        break

proc.kill()
print(f"\nTotal: {len(output)} bytes in {time.time()-t0:.1f}s", file=sys.stderr)
if output:
    tail = output[-500:] if len(output) > 500 else output
    sys.stdout.buffer.write(tail)
    sys.stdout.buffer.write(b"\n")
