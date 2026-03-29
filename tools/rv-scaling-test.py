#!/usr/bin/env python3
"""RISC-V bare metal scaling test.

Generates Codex programs with 50..250 functions (step 50),
compiles each to riscv-bare via the CLI, sends the binary to
QEMU, and reports compile time + QEMU execution output.
"""

import subprocess, sys, time, os, tempfile, textwrap

SIZES = [50, 100, 150, 200, 250]
CLI = "dotnet run --project tools/Codex.Cli --"
QEMU = "qemu-system-riscv64 -machine virt -nographic -bios none"
TIMEOUT = 5  # seconds per QEMU run
BASE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(BASE)


def generate_codex(n_funcs):
    """Generate a Codex program with n_funcs functions that chain calls."""
    lines = []
    lines.append(f"Chapter: Scaling Test ({n_funcs} functions)\n")
    lines.append("Section: Functions\n")

    # Generate n_funcs-1 helper functions: f1..f{n-1}
    # Each adds its index to the argument
    for i in range(1, n_funcs):
        lines.append(f"  f{i} : Integer -> Integer")
        lines.append(f"  f{i} (x) = x + {i}\n")

    # main chains them all: f1 (f2 (f3 (... (fN-1 0) ...)))
    lines.append("  main : Integer")
    call = "0"
    for i in range(n_funcs - 1, 0, -1):
        call = f"f{i} ({call})"
    lines.append(f"  main = {call}\n")

    return "\n".join(lines)


def expected_result(n_funcs):
    """Sum of 1..n_funcs-1."""
    return (n_funcs - 1) * n_funcs // 2


def run_test(n_funcs):
    src = generate_codex(n_funcs)
    expected = expected_result(n_funcs)

    # Write source to temp file
    src_path = os.path.join(ROOT, f"tools/_rv_scale_{n_funcs}.codex")
    bin_path = os.path.join(ROOT, f"tools/_rv_scale_{n_funcs}.bin")
    with open(src_path, "w") as f:
        f.write(src)

    src_size = len(src.encode("utf-8"))

    # Compile
    t0 = time.perf_counter()
    result = subprocess.run(
        f"dotnet run --project tools/Codex.Cli -- build {src_path} --target riscv-bare",
        shell=True, capture_output=True, text=True, cwd=ROOT, timeout=60
    )
    compile_time = time.perf_counter() - t0

    if result.returncode != 0 or not os.path.exists(bin_path):
        print(f"  {n_funcs:4d} funcs | {src_size:6d}B src | COMPILE FAILED")
        if result.stderr:
            print(f"         stderr: {result.stderr[:200]}")
        cleanup(src_path, bin_path)
        return False

    bin_size = os.path.getsize(bin_path)

    # Convert Windows path to WSL path for QEMU
    wsl_bin = bin_path.replace("D:\\", "/mnt/d/").replace("\\", "/")

    # Run in QEMU
    t1 = time.perf_counter()
    try:
        qemu_result = subprocess.run(
            f"wsl -e bash -c \"timeout {TIMEOUT} {QEMU} -kernel {wsl_bin} 2>&1\"",
            shell=True, capture_output=True, text=True, timeout=TIMEOUT + 5
        )
        qemu_time = time.perf_counter() - t1
        output = qemu_result.stdout.strip().split("\n")[0].strip() if qemu_result.stdout else "NO OUTPUT"
    except subprocess.TimeoutExpired:
        qemu_time = TIMEOUT
        output = "TIMEOUT"

    # Check correctness
    try:
        actual = int(output)
        correct = "OK" if actual == expected else f"WRONG (expected {expected})"
    except ValueError:
        actual = output
        correct = f"PARSE FAIL (expected {expected})"

    print(f"  {n_funcs:4d} funcs | {src_size:6d}B src | {bin_size:6d}B bin | "
          f"compile {compile_time:5.2f}s | qemu {qemu_time:5.2f}s | "
          f"output={output} {correct}")

    cleanup(src_path, bin_path)
    return correct == "OK"


def cleanup(*paths):
    for p in paths:
        try:
            os.unlink(p)
        except FileNotFoundError:
            pass


def main():
    print("RISC-V Bare Metal Scaling Test")
    print("=" * 80)
    print(f"  {'Size':>4s} funcs | {'Source':>6s}   | {'Binary':>6s}     | "
          f"{'Compile':>7s}   | {'QEMU':>5s}     | Result")
    print("-" * 80)

    all_ok = True
    for n in SIZES:
        ok = run_test(n)
        if not ok:
            all_ok = False

    print("-" * 80)
    if all_ok:
        print("ALL PASSED")
    else:
        print("SOME FAILURES")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
