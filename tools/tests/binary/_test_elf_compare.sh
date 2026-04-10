#!/bin/bash
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
BM=/tmp/bm-small.elf

echo "Reference: $(wc -c < "$REF") bytes"
echo "Self-hosted: $(wc -c < "$BM") bytes"
echo ""

echo "=== LOAD segments ==="
echo "-- Ref --"
readelf -l "$REF" 2>/dev/null | grep LOAD
echo "-- BM --"
readelf -l "$BM" 2>/dev/null | grep LOAD
echo ""

echo "=== Disassembly of 64-bit entry code ==="
python3 - "$REF" "$BM" << 'PYEOF'
import struct, subprocess, sys

for name, path in [("Reference", sys.argv[1]), ("Self-hosted", sys.argv[2])]:
    data = open(path, "rb").read()
    phoff = struct.unpack_from("<I", data, 28)[0]
    # Far jump is at trampoline offset 0xa4 from LOAD start
    fj_abs = phoff + 0xa4
    entry64 = struct.unpack_from("<I", data, fj_abs + 1)[0]
    file_entry = entry64 - 0x100000 + phoff

    p_filesz = struct.unpack_from("<I", data, phoff + 16)[0]

    print(f"--- {name} ---")
    print(f"  File: {len(data)} bytes, LOAD filesz: {p_filesz}")
    print(f"  64-bit entry: 0x{entry64:x} (file offset 0x{file_entry:x})")

    # Count RETs in 64-bit code
    code64 = data[file_entry:]
    rets = sum(1 for b in code64 if b == 0xc3)
    print(f"  RET count: {rets} (rough function count)")

    # Disassemble first 300 bytes
    tmp = f"/tmp/code64_{name}.bin"
    open(tmp, "wb").write(data[file_entry:file_entry+300])
    result = subprocess.run(
        ["objdump", "-D", "-b", "binary", "-m", "i386:x86-64", tmp],
        capture_output=True, text=True
    )
    lines = result.stdout.strip().split("\n")
    for line in lines[7:40]:
        print(f"  {line}")
    print()
PYEOF
