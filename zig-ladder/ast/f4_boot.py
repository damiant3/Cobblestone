#!/usr/bin/env python3
"""F4 of the fib ladder: BOOT the CDX the subject emitted.

F3 carves fib out of the content buffer and calls it from a host process,
which proves the instructions are real but says nothing about the binary
around them -- the header, the entry point, __start, the runtime init, the
serial path. This reassembles the whole file the way the compiler's own
emit-binary-tail does (header-bytes, then content, then tail-bytes), hands
it to the VM, and expects fib's own program to say 6765.

Both dumps are booted. They are byte-identical, so a disagreement here
would mean the reassembly is wrong, not the plug.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import codex_vm  # noqa: E402

HERE = pathlib.Path(__file__).parent

# Each rung's subject prints something only it can print, so the boot has a
# specific answer to be right about rather than merely "some output".
RUNGS = [
    ("fibx", "6765"),
    ("scale", "2147\n3\n11"),
]


def parse_sections(path):
    """Pull the byte sections out of a fibx dump. Fails loud: a section that
    does not match its declared length is a corrupt dump, not a short one."""
    lines = path.read_text(errors="replace").splitlines()
    lens, sections, i = {}, {}, 0
    while not lines[i].startswith("---"):
        key, _, val = lines[i].partition(" ")
        lens[key] = int(val)
        i += 1
    while i < len(lines):
        name = lines[i].strip("- ")
        i += 1
        body = []
        while lines[i] != ".":
            body.append(lines[i])
            i += 1
        i += 1
        if name != "symbols":
            by = bytes(int(t) for line in body for t in line.split())
            want = lens[f"{name}-len"]
            if len(by) != want:
                raise SystemExit(f"{path.name}: {name} says {want} bytes, dump carries {len(by)}")
            sections[name] = by
    return sections


def boot_side(rung, side, expected):
    dump_name = f"{rung}.{'truth' if side == 'truth' else 'zigout'}"
    cdx_name = f"{rung}-from-{'truth' if side == 'truth' else 'zig'}.cdx"
    src = HERE / dump_name
    s = parse_sections(src)
    cdx = s["header"] + s["content"] + s["tail"]
    out_path = HERE / cdx_name
    out_path.write_bytes(cdx)
    print(f"{dump_name} -> {cdx_name}: {len(cdx)} bytes "
          f"({len(s['header'])} header + {len(s['content'])} content + {len(s['tail'])} tail)")

    out = codex_vm.run_cdx(str(out_path), timeout=300, idle_timeout=120)
    lines = [l.rstrip("\r") for l in out.decode(errors="replace").splitlines()
             if not l.startswith(("WD:", "HEAP:", "STACK:"))]
    printed = "\n".join(lines).strip()
    ok = printed == expected
    print(f"  booted, printed: {printed!r}  {'ok' if ok else 'WRONG (want ' + expected + ')'}")
    return ok


if __name__ == "__main__":
    results = []
    for rung, expected in RUNGS:
        for side in ("truth", "zigout"):
            results.append(boot_side(rung, side, expected))
    if not all(results):
        raise SystemExit("F4 FAIL: an emitted binary printed the wrong thing")
    print(f"F4 PASS: {len(results)} emitted binaries boot and print what their subjects say")
