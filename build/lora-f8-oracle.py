# lora-f8-oracle.py -- the reference for codex/test/apps/diffusion-lora-f8: torch's
# float8_e4m3fn casts on the CUDA device, as Forge's merge_lora_to_weight widens an
# fp8 weight to f32 and casts the merged weight back (backend/patcher/lora.py:85, 270).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/lora-f8-oracle.py <out dir>
#
# Writes <out dir>/lora-f8.bin: the 256 e4m3 bytes widened to f32 (le32 bits);
# then N f32 values (le32 bits) and their e4m3 bytes: the edges below, then
# normals at weight scale from a seeded generator.

import os
import struct
import sys

import torch

N = 4096


def edges():
    v = [0.0, -0.0, 448.0, -448.0, 464.0, 465.0, 479.9, 480.0, 1e9, float('inf'), 2.0 ** -6, 2.0 ** -7, 2.0 ** -9, 2.0 ** -10,
         1.5 * 2.0 ** -10, 2.0 ** -6 * 1.0625, 1.0625, 1.1875, 1.125 + 2.0 ** -20, 3.0 * 2.0 ** -8, 5.0 * 2.0 ** -10]
    for e in range(-9, 9):
        for m in range(16):
            v.append((1.0 + m / 16.0) * 2.0 ** e)
    return v


def main():
    dev = torch.device('cuda')
    b = torch.arange(256, dtype=torch.uint8, device=dev).view(torch.float8_e4m3fn)
    widened = b.to(torch.float32).view(torch.int32).cpu().tolist()
    vals = edges()
    vals += [-x for x in vals]
    g = torch.Generator().manual_seed(20260929)
    rest = (torch.randn(N - len(vals), generator=g) * 0.05).tolist()
    x = torch.tensor(vals + rest, dtype=torch.float32, device=dev)
    narrowed = x.to(torch.float8_e4m3fn).view(torch.uint8).cpu().tolist()
    with open(os.path.join(sys.argv[1], 'lora-f8.bin'), 'wb') as f:
        f.write(struct.pack('<256i', *widened))
        f.write(struct.pack('<%di' % N, *x.view(torch.int32).cpu().tolist()))
        f.write(bytes(narrowed))
    print('256 widened, %d narrowed (%d edges)' % (N, len(vals)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
