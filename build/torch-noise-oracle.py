# torch-noise-oracle.py -- the reference for codex/test/apps/diffusion-torch-noise:
# Diffusion Forge's default noise (modules/rng.py, randn_source "GPU"):
# torch.randn on the CUDA device from a torch.Generator seeded with the
# seed, drawn again and again from the same generator as ImageRNG.next does.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/torch-noise-oracle.py <out dir>
#
# The draws depend on the card: torch's CUDA normal spreads a tensor over at
# most multiProcessorCount x (maxThreadsPerMultiProcessor / 256) blocks, and
# the engine assumes 204 (the RTX 4060 Ti's 34 x 6). This script refuses to
# write on a card with another count.
#
# Writes <out dir>/torch-noise.bin, f32 little-endian, the cases in order:
#   (4, 64, 64)    seed 0           draws 0, 1      16384 values each
#   (4, 128, 128)  seed 20260929    draws 0, 1, 2   65536 values each
#   (4, 80, 192)   seed 4294967295  draws 0, 1      61440 values each
#   (16, 128, 128) seed 1234        draws 0, 1      262144 values each

import os
import sys

import numpy as np
import torch

CASES = [((4, 64, 64), 0, 2), ((4, 128, 128), 20260929, 3), ((4, 80, 192), 4294967295, 2), ((16, 128, 128), 1234, 2)]


def main():
    out = sys.argv[1]
    p = torch.cuda.get_device_properties(0)
    blocks = p.multi_processor_count * (p.max_threads_per_multi_processor // 256)
    print(f'{p.name}: {p.multi_processor_count} SMs, {p.max_threads_per_multi_processor} threads each, {blocks} blocks; torch {torch.__version__}')
    if blocks != 204:
        print('refused: the engine assumes 204 blocks')
        return 1
    parts = []
    for shape, seed, draws in CASES:
        g = torch.Generator('cuda').manual_seed(seed)
        for _ in range(draws):
            parts.append(torch.randn(shape, device='cuda', generator=g).cpu().numpy().astype('<f4').ravel())
    data = np.concatenate(parts)
    with open(os.path.join(out, 'torch-noise.bin'), 'wb') as f:
        f.write(data.tobytes())
    print(f'wrote {data.size} values')
    return 0


if __name__ == '__main__':
    sys.exit(main())
