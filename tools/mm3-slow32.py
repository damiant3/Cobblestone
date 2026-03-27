#!/usr/bin/env python3
"""One test: 32KB, 512-byte chunks, 500ms between each."""
import subprocess, time, sys, os, threading

KERNEL = "/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"

# Build 32KB source
lines = []
for i in range(1, 713):
    lines.append(f"f{i} : Integer -> Integer")
    lines.append(f"f{i} (x) = x + {i}")
    lines.append("")
lines.append("main : Integer")
lines.append("main = f1 0")
src = ("\n".join(lines) + "\x04").encode("latin-1")
print(f"Source: {len(src)} bytes, sending in 512B chunks with 500ms gaps", file=sys.stderr)

r_fd, w_fd = os.pipe()
proc = subprocess.Popen(
    ["qemu-system-x86_64", "-kernel", KERNEL, "-serial", "stdio",
     "-display", "none", "-no-reboot", "-m", "512"],
    stdin=r_fd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
os.close(r_fd)

def send():
    try:
        time.sleep(1)
        i = 0
        chunk_num = 0
        while i < len(src):
            n = os.write(w_fd, src[i:i+512])
            i += n
            chunk_num += 1
            print(f"  chunk {chunk_num}: sent {i}/{len(src)}", file=sys.stderr)
            time.sleep(0.5)
        print("  all chunks sent", file=sys.stderr)
        time.sleep(3600)
    except Exception as e:
        print(f"  send error: {e}", file=sys.stderr)

t = threading.Thread(target=send, daemon=True)
t.start()

t0 = time.time()
buf = b""
while time.time() - t0 < 300:
    b = proc.stdout.read(1)
    if b == b"":
        print(f"QEMU exited after {time.time()-t0:.1f}s", file=sys.stderr)
        break
    buf += b
    if b == b"\n" and len(buf) > 20:
        print(f"GOT OUTPUT after {time.time()-t0:.1f}s ({len(buf)} bytes)", file=sys.stderr)
        break

proc.kill()
try:
    os.close(w_fd)
except:
    pass
