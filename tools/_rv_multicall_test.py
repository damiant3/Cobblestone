#!/usr/bin/env python3
"""Test RISC-V with many function calls (few params each).

Generate programs with N helper functions, each taking 1-3 params,
called from main. This tests call-site arg marshaling at scale.
"""

import subprocess, sys, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = os.path.join(ROOT, "tools", "Codex.Cli")


def generate_program(n_funcs):
    """N functions: f1(x)=x+1, f2(x)=x+2, ..., fN(x)=x+N
    main = f1(f2(f3(...(fN(0))...)))
    expected = sum(1..N) = N*(N+1)/2
    """
    lines = []
    for i in range(1, n_funcs + 1):
        lines.append(f"f{i} : Integer -> Integer")
        lines.append(f"f{i} (x) = x + {i}")
        lines.append("")

    call = "0"
    for i in range(n_funcs, 0, -1):
        call = f"f{i} ({call})"

    lines.append("main : Integer")
    lines.append(f"main = {call}")
    return "\n".join(lines) + "\n"


def run_test(n_funcs):
    expected = n_funcs * (n_funcs + 1) // 2
    src = generate_program(n_funcs)

    src_path = os.path.join(ROOT, "tools", f"_rv_call_{n_funcs}.codex")
    elf_path = os.path.join(ROOT, "tools", f"_rv_call_{n_funcs}")

    with open(src_path, "w") as f:
        f.write(src)

    result = subprocess.run(
        ["dotnet", "run", "--project", CLI, "--", "build", src_path, "--target", "riscv"],
        capture_output=True, text=True, cwd=ROOT, timeout=60
    )

    if result.returncode != 0 or not os.path.exists(elf_path):
        print(f"  N={n_funcs:3d} calls | COMPILE FAILED | {result.stderr[:200].strip()}")
        cleanup(src_path, elf_path)
        return None

    wsl_elf = elf_path.replace("D:\\", "/mnt/d/").replace("\\", "/")
    try:
        qemu_result = subprocess.run(
            ["wsl", "-e", "bash", "-c", f"qemu-riscv64 {wsl_elf}"],
            capture_output=True, text=True, timeout=10
        )
        output = qemu_result.stdout.strip()
        actual = int(output) if output else None
    except Exception as e:
        print(f"  N={n_funcs:3d} calls | QEMU FAILED: {e}")
        cleanup(src_path, elf_path)
        return None

    status = "OK" if actual == expected else f"WRONG (got {actual}, expected {expected})"
    print(f"  N={n_funcs:3d} calls | expected={expected:5d} | actual={str(actual):>5s} | {status}")
    cleanup(src_path, elf_path)
    return actual == expected


def cleanup(*paths):
    for p in paths:
        try:
            os.unlink(p)
        except FileNotFoundError:
            pass


def main():
    print("RISC-V Multi-Call Scaling Test")
    print("=" * 70)
    print("  f1(f2(f3(...(fN(0))...)))  — each fi(x) = x + i")
    print("  expected = N*(N+1)/2")
    print("=" * 70)

    for n in [1, 2, 5, 10, 20, 50, 100, 150, 200]:
        run_test(n)

if __name__ == "__main__":
    sys.exit(main())
