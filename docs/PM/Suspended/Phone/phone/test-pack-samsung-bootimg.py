#!/usr/bin/env python3
"""
test-pack-samsung-bootimg.py — Self-test for the Samsung boot image packer.

Creates synthetic kernel, ramdisk, and DTB blobs, packs them, then validates
the output with inspect-bootimg.py logic. No real device files needed.

Run: python3 phone/test-pack-samsung-bootimg.py
"""
import os
import struct
import subprocess
import sys
import tempfile

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PACKER = os.path.join(SCRIPT_DIR, 'pack-samsung-bootimg.py')
PAGE_SIZE = 4096


def align(size, page):
    return ((size + page - 1) // page) * page


def create_test_blob(path, size, fill=0xAA):
    """Create a binary blob of the given size filled with a byte pattern."""
    with open(path, 'wb') as f:
        f.write(bytes([fill]) * size)


def read_header(path):
    """Read and parse boot image header fields."""
    with open(path, 'rb') as f:
        hdr = f.read(PAGE_SIZE)
    fields = {}
    fields['magic'] = hdr[0:8]
    fields['kernel_size'] = struct.unpack_from('<I', hdr, 0x08)[0]
    fields['kernel_addr'] = struct.unpack_from('<I', hdr, 0x0C)[0]
    fields['ramdisk_size'] = struct.unpack_from('<I', hdr, 0x10)[0]
    fields['ramdisk_addr'] = struct.unpack_from('<I', hdr, 0x14)[0]
    fields['second_size'] = struct.unpack_from('<I', hdr, 0x18)[0]
    fields['second_addr'] = struct.unpack_from('<I', hdr, 0x1C)[0]
    fields['tags_addr'] = struct.unpack_from('<I', hdr, 0x20)[0]
    fields['page_size'] = struct.unpack_from('<I', hdr, 0x24)[0]
    fields['dt_size'] = struct.unpack_from('<I', hdr, 0x28)[0]
    return fields


def run_test():
    errors = 0

    with tempfile.TemporaryDirectory(prefix='bootimg-test-') as tmpdir:
        kernel_path = os.path.join(tmpdir, 'Image.gz')
        ramdisk_path = os.path.join(tmpdir, 'initrd.img')
        dt_path = os.path.join(tmpdir, 'dtb.img')
        output_path = os.path.join(tmpdir, 'recovery.img')

        # Sizes matching real S7 Edge (approximately)
        kernel_size = 9_455_372
        ramdisk_size = 9_208_464
        dt_size = 6_414_336

        print("=== Test 1: Pack with DTB (Samsung format) ===")
        create_test_blob(kernel_path, kernel_size, 0x4B)   # 'K' for kernel
        create_test_blob(ramdisk_path, ramdisk_size, 0x52)  # 'R' for ramdisk
        create_test_blob(dt_path, dt_size, 0x44)            # 'D' for DTB

        result = subprocess.run(
            [sys.executable, PACKER,
             '--kernel', kernel_path,
             '--ramdisk', ramdisk_path,
             '--dt', dt_path,
             '--output', output_path,
             '--seandroid'],
            capture_output=True, text=True
        )
        print(result.stdout)
        if result.returncode != 0:
            print(f"FAIL: packer exited with code {result.returncode}", file=sys.stderr)
            print(result.stderr, file=sys.stderr)
            errors += 1
        else:
            # Validate header
            hdr = read_header(output_path)
            checks = [
                ('magic', hdr['magic'], b'ANDROID!'),
                ('kernel_size', hdr['kernel_size'], kernel_size),
                ('ramdisk_size', hdr['ramdisk_size'], ramdisk_size),
                ('dt_size', hdr['dt_size'], dt_size),
                ('page_size', hdr['page_size'], PAGE_SIZE),
                ('second_size', hdr['second_size'], 0),
            ]
            for name, actual, expected in checks:
                if actual != expected:
                    print(f"  FAIL: {name} = {actual}, expected {expected}")
                    errors += 1
                else:
                    print(f"  OK: {name} = {actual}")

            # Validate total file size
            actual_size = os.path.getsize(output_path)
            expected_size = (PAGE_SIZE  # header
                           + align(kernel_size, PAGE_SIZE)
                           + align(ramdisk_size, PAGE_SIZE)
                           + align(dt_size, PAGE_SIZE)
                           + 16)  # SEANDROIDENFORCE
            if actual_size != expected_size:
                print(f"  FAIL: file_size = {actual_size}, expected {expected_size}")
                errors += 1
            else:
                print(f"  OK: file_size = {actual_size}")

            # Validate trailing bytes (should be exactly SEANDROIDENFORCE = 16 bytes)
            computed = (PAGE_SIZE
                       + align(kernel_size, PAGE_SIZE)
                       + align(ramdisk_size, PAGE_SIZE)
                       + align(dt_size, PAGE_SIZE))
            trailing = actual_size - computed
            if trailing != 16:
                print(f"  FAIL: trailing_bytes = {trailing}, expected 16")
                errors += 1
            else:
                print(f"  OK: trailing_bytes = {trailing} (SEANDROIDENFORCE)")

            # Validate SEANDROIDENFORCE at end of file
            with open(output_path, 'rb') as f:
                f.seek(-16, 2)
                trailer = f.read(16)
            if trailer != b'SEANDROIDENFORCE':
                print(f"  FAIL: trailer = {trailer!r}, expected SEANDROIDENFORCE")
                errors += 1
            else:
                print(f"  OK: trailer = SEANDROIDENFORCE")

        print()
        print("=== Test 2: Pack without DTB (should have dt_size=0) ===")
        output2 = os.path.join(tmpdir, 'no-dt.img')
        result2 = subprocess.run(
            [sys.executable, PACKER,
             '--kernel', kernel_path,
             '--ramdisk', ramdisk_path,
             '--output', output2,
             '--no-seandroid'],
            capture_output=True, text=True
        )
        print(result2.stdout)
        if result2.returncode != 0:
            print(f"FAIL: packer exited with code {result2.returncode}", file=sys.stderr)
            errors += 1
        else:
            hdr2 = read_header(output2)
            if hdr2['dt_size'] != 0:
                print(f"  FAIL: dt_size = {hdr2['dt_size']}, expected 0")
                errors += 1
            else:
                print(f"  OK: dt_size = 0 (no DTB)")

    print()
    if errors == 0:
        print("ALL TESTS PASSED ✓")
    else:
        print(f"FAILED: {errors} error(s)")
    return errors


if __name__ == '__main__':
    sys.exit(run_test())
