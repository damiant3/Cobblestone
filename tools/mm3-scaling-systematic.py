#!/usr/bin/env python3
"""
Systematic MM3 scaling measurement.
Series 1: N independent functions (22KB, 32KB, 42KB, 52KB, 62KB)
Series 2: N functions calling each other (chain depth 2)
Series 3: N functions calling each other (chain depth 3)
Series 4: N functions with a shared type dependency
"""
import subprocess, time, sys, os

KERNEL = "/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"

def run_test(label, source):
    """Send source to bare metal kernel, measure time to first output byte."""
    src_bytes = source.encode("latin-1") + b"\x04"
    size = len(src_bytes)

    r_fd, w_fd = os.pipe()
    proc = subprocess.Popen(
        ["qemu-system-x86_64", "-kernel", KERNEL, "-serial", "stdio",
         "-display", "none", "-no-reboot", "-m", "512"],
        stdin=r_fd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    os.close(r_fd)

    import threading
    def send():
        try:
            time.sleep(0.5)
            # Pace: 4KB chunks with 10ms delay to avoid pipe/FIFO overflow
            i = 0
            while i < len(src_bytes):
                os.write(w_fd, src_bytes[i:i+4096])
                i += 4096
                time.sleep(0.01)
            time.sleep(3600)
        except:
            pass
    t = threading.Thread(target=send, daemon=True)
    t.start()

    t0 = time.time()
    got_output = False
    buf = b""
    while time.time() - t0 < 120:
        b = proc.stdout.read(1)
        if b == b"":
            break
        buf += b
        if b == b"\n" and len(buf) > 10:
            got_output = True
            break

    elapsed = time.time() - t0
    proc.kill()
    try:
        os.close(w_fd)
    except:
        pass

    status = "OK" if got_output else "FAIL"
    print(f"  {label:40s} {size:6d}B  {elapsed:6.2f}s  {status}")
    sys.stdout.flush()
    return got_output, elapsed

print("=== Series 1: Independent functions ===")
for target_kb in [22, 32, 42, 52, 62]:
    # ~46 bytes per function pair (type sig + body + blank line)
    n = (target_kb * 1024) // 46
    lines = []
    for i in range(1, n + 1):
        lines.append(f"f{i} : Integer -> Integer")
        lines.append(f"f{i} (x) = x + {i}")
        lines.append("")
    lines.append("main : Integer")
    lines.append("main = f1 0")
    run_test(f"N={n} independent ({target_kb}KB)", "\n".join(lines))

print()
print("=== Series 2: Chain depth 2 (f calls g) ===")
for target_kb in [22, 32, 42, 52, 62]:
    n = (target_kb * 1024) // 92  # ~92 bytes per pair
    lines = []
    for i in range(1, n + 1):
        lines.append(f"g{i} : Integer -> Integer")
        lines.append(f"g{i} (x) = x + {i}")
        lines.append(f"f{i} : Integer -> Integer")
        lines.append(f"f{i} (x) = g{i} (x + 1)")
        lines.append("")
    lines.append("main : Integer")
    lines.append("main = f1 0")
    run_test(f"N={n} chain-2 ({target_kb}KB)", "\n".join(lines))

print()
print("=== Series 3: Chain depth 3 ===")
for target_kb in [22, 32, 42, 52, 62]:
    n = (target_kb * 1024) // 138
    lines = []
    for i in range(1, n + 1):
        lines.append(f"h{i} : Integer -> Integer")
        lines.append(f"h{i} (x) = x + {i}")
        lines.append(f"g{i} : Integer -> Integer")
        lines.append(f"g{i} (x) = h{i} (x + 1)")
        lines.append(f"f{i} : Integer -> Integer")
        lines.append(f"f{i} (x) = g{i} (x + 1)")
        lines.append("")
    lines.append("main : Integer")
    lines.append("main = f1 0")
    run_test(f"N={n} chain-3 ({target_kb}KB)", "\n".join(lines))

print()
print("=== Series 4: Shared type dependency ===")
for target_kb in [22, 32, 42, 52, 62]:
    n = (target_kb * 1024 - 100) // 70  # subtract type def overhead
    lines = []
    lines.append("Pair = Pair { x : Integer, y : Integer }")
    lines.append("")
    for i in range(1, n + 1):
        lines.append(f"f{i} : Pair -> Integer")
        lines.append(f"f{i} (p) = p.x + p.y + {i}")
        lines.append("")
    lines.append("main : Integer")
    lines.append("main = f1 (Pair { x = 1, y = 2 })")
    run_test(f"N={n} with-type ({target_kb}KB)", "\n".join(lines))

print()
print("=== Done ===")
