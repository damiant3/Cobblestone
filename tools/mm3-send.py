#!/usr/bin/env python3
"""Paced serial sender for MM3. Sends source to stdout with delays."""
import time, sys

SOURCE = "/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.codex"

time.sleep(3)  # Wait for kernel boot

with open(SOURCE, 'rb') as f:
    data = f.read()

sys.stderr.write(f"Sending {len(data)} bytes in 4KB chunks...\n")

i = 0
while i < len(data):
    chunk = data[i:i+4096]
    sys.stdout.buffer.write(chunk)
    sys.stdout.buffer.flush()
    i += 4096
    time.sleep(0.1)

sys.stdout.buffer.write(b'\x04')
sys.stdout.buffer.flush()
sys.stderr.write("Source sent. Waiting for compilation...\n")

time.sleep(1800)
