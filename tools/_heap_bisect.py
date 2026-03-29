#!/usr/bin/env python3
"""Bisect minimum working heap for full self-compile output (usermode)."""
import subprocess, sys

SOURCE = "/mnt/d/Projects/NewRepository-cam/tools/_all-source.codex"
TARGET = 166000  # minimum bytes for "full" output

def test_heap(heap_mb, result_mb=1):
    # Patch the constants, rebuild, test
    codegen = "src/Codex.Emit.X86_64/X86_64CodeGen.cs"

    with open(codegen, 'r') as f:
        content = f.read()

    # Patch usermode brk size
    import re
    content = re.sub(
        r'Li\(m_text, growReg, \(\d+L \+ \d+\) \* 1024 \* 1024\)',
        f'Li(m_text, growReg, ({heap_mb}L + {result_mb}) * 1024 * 1024)',
        content)
    content = re.sub(
        r'Li\(m_text, growReg, \d+L \* 1024 \* 1024\);  // result',
        f'Li(m_text, growReg, {heap_mb}L * 1024 * 1024);  // result',
        content)

    with open(codegen, 'w') as f:
        f.write(content)

    # Build
    r = subprocess.run(["dotnet", "build", "src/Codex.Emit.X86_64/Codex.Emit.X86_64.csproj"],
                       capture_output=True, text=True)
    if r.returncode != 0:
        return -1

    r = subprocess.run(["dotnet", "run", "--project", "tools/Codex.Cli", "--",
                        "build", "Codex.Codex/", "--target", "x86-64"],
                       capture_output=True, text=True)
    if r.returncode != 0:
        return -1

    # Test
    r = subprocess.run(
        ["wsl", "-e", "bash", "-c",
         f'echo "{SOURCE}" | timeout 30 qemu-x86_64 /mnt/d/Projects/NewRepository-cam/Codex.Codex/out/Codex.Codex 2>/dev/null | wc -c'],
        capture_output=True, text=True)
    try:
        return int(r.stdout.strip())
    except:
        return 0

# Binary search
lo, hi = 25, 57
print(f"Bisecting working heap: {lo}-{hi} MB")
while hi - lo > 1:
    mid = (lo + hi) // 2
    subprocess.run(["rm", "-f", "Codex.Codex/out/Codex.Codex"], capture_output=True)
    out = test_heap(mid)
    status = "OK" if out >= TARGET else "FAIL"
    print(f"  {mid} MB: {out} bytes — {status}")
    if out >= TARGET:
        hi = mid
    else:
        lo = mid

print(f"\nMinimum working heap: {hi} MB")
