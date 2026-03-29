#!/usr/bin/env python3
"""Test EmitBinary right-operand clobbering at spill threshold.

Generate programs with N let-bindings (to exhaust callee-saved regs),
then a binary op using two of those locals. All within a single
2-parameter function, so we don't hit the >8 arg limit.

Hypothesis: when both operands of a binary op are spilled locals,
LoadLocal for the left clobbers the temp register holding the right.
"""

import subprocess, sys, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = os.path.join(ROOT, "tools", "Codex.Cli")


def generate_program(n_lets):
    """Generate: f(x, y) = let a1 = x+1 in let a2 = a1+1 in ... let aN = a{N-1}+1 in aN + y
    main = f 1 1000
    Expected: (1 + N) + 1000
    The final 'aN + y' is a binary op where both sides must be loaded from locals.
    With enough lets, both aN and y will be in spill slots.
    """
    lines = []
    lines.append(f"f : Integer -> Integer -> Integer")

    # Build the body: nested lets
    body_parts = []
    prev = "x"
    for i in range(1, n_lets + 1):
        body_parts.append(f"let a{i} = {prev} + 1 in")
        prev = f"a{i}"

    # Final expression: last let var + y (binary op with two locals)
    body_parts.append(f"{prev} + y")

    body = "\n    ".join(body_parts)
    lines.append(f"f (x) (y) =\n    {body}")
    lines.append("")
    lines.append("main : Integer")
    lines.append(f"main = f 1 1000")

    return "\n".join(lines) + "\n"


def run_test(n_lets):
    expected = (1 + n_lets) + 1000
    src = generate_program(n_lets)

    src_path = os.path.join(ROOT, "tools", f"_rv_spill_{n_lets}.codex")
    elf_path = os.path.join(ROOT, "tools", f"_rv_spill_{n_lets}")

    with open(src_path, "w") as f:
        f.write(src)

    result = subprocess.run(
        ["dotnet", "run", "--project", CLI, "--", "build", src_path, "--target", "riscv"],
        capture_output=True, text=True, cwd=ROOT, timeout=60
    )

    if result.returncode != 0 or not os.path.exists(elf_path):
        print(f"  N={n_lets:3d} lets | COMPILE FAILED | {result.stderr[:200].strip()}")
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
        print(f"  N={n_lets:3d} lets | QEMU FAILED: {e}")
        cleanup(src_path, elf_path)
        return None

    status = "OK" if actual == expected else f"WRONG (got {actual}, expected {expected})"
    print(f"  N={n_lets:3d} lets | expected={expected:5d} | actual={str(actual):>5s} | {status}")
    cleanup(src_path, elf_path)
    return actual == expected


def cleanup(*paths):
    for p in paths:
        try:
            os.unlink(p)
        except FileNotFoundError:
            pass


def main():
    print("RISC-V Spill + BinaryOp Test")
    print("=" * 70)
    print("  f(x, y) = let a1 = x+1 in ... let aN = a{N-1}+1 in aN + y")
    print("  main = f 1 1000  =>  expected = (1 + N) + 1000")
    print("  S2-S11 = 10 callee-saved. Params x,y use 2. Spill at let ~9+")
    print("=" * 70)

    for n in list(range(1, 20)) + [25, 30, 40, 50]:
        run_test(n)

if __name__ == "__main__":
    sys.exit(main())
