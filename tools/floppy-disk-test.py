#!/usr/bin/env python3
"""Floppy Disk Edition: streaming self-compile on bare metal x86-64."""
import subprocess, time, sys, threading, os

KERNEL = "/mnt/d/Projects/NewRepository-cam/Codex.Codex/out/Codex.Codex.elf"
SOURCE = "/mnt/d/Projects/NewRepository-cam/tools/_all-source.codex"

with open(SOURCE, "rb") as f:
    src = f.read()

filename = SOURCE.encode() + b"\n"
print(f"Kernel: {os.path.getsize(KERNEL)} bytes", file=sys.stderr)
print(f"Source: {len(src)} bytes", file=sys.stderr)

r_fd, w_fd = os.pipe()

proc = subprocess.Popen(
    ["qemu-system-x86_64", "-kernel", KERNEL, "-serial", "stdio",
     "-display", "none", "-no-reboot", "-m", "512"],
    stdin=r_fd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

os.close(r_fd)

def send_source():
    try:
        time.sleep(1)
        os.write(w_fd, filename)
        time.sleep(0.1)
        i = 0
        while i < len(src):
            os.write(w_fd, src[i:i+4096])
            i += 4096
            time.sleep(0.05)
        os.write(w_fd, b"\x04")
        time.sleep(300)
    except:
        pass
    finally:
        os.close(w_fd)

sender = threading.Thread(target=send_source, daemon=True)
sender.start()

t0 = time.time()
output = b""
last_report = 0
stall_start = None

while time.time() - t0 < 300:  # 5 minute timeout
    # Non-blocking read with 1-second timeout
    import select
    ready, _, _ = select.select([proc.stdout], [], [], 1.0)
    if ready:
        b = proc.stdout.read(1)
        if b == b"":
            print(f"\nQEMU exited after {time.time()-t0:.1f}s", file=sys.stderr)
            break
        output += b
        stall_start = None
        if len(output) - last_report >= 50000:
            last_report = len(output)
            print(f"  ...{len(output)} bytes ({time.time()-t0:.1f}s)", file=sys.stderr)
    else:
        # No data for 1 second
        if stall_start is None:
            stall_start = time.time()
        elif time.time() - stall_start > 5:
            print(f"\nStalled for 5s at {len(output)} bytes, assuming done ({time.time()-t0:.1f}s)", file=sys.stderr)
            break

proc.kill()
elapsed = time.time() - t0
print(f"\nTotal: {len(output)} bytes in {elapsed:.1f}s", file=sys.stderr)

if output:
    text = output.decode("utf-8", errors="replace")
    has_using = "using System" in text
    has_class = "public static class" in text
    has_streaming = "compile_streaming" in text or "stream_defs" in text
    ends_brace = text.rstrip().endswith("}")
    type_defs = text.count("sealed record") + text.count("abstract record")
    defs = text.count("public static")
    print(f"Valid header: {has_using}, Class: {has_class}", file=sys.stderr)
    print(f"Streaming fns: {has_streaming}, Ends }}: {ends_brace}", file=sys.stderr)
    print(f"Type defs: {type_defs}, Definitions: {defs}", file=sys.stderr)

    with open("/tmp/baremetal-output.cs", "wb") as f:
        f.write(output)
    print(f"Saved to /tmp/baremetal-output.cs", file=sys.stderr)
