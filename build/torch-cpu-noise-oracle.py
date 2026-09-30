# torch-cpu-noise-oracle.py -- the reference for img2img's posterior draw:
# torch.randn on the CPU from the global generator after torch.manual_seed,
# as Diffusion Forge's DiagonalGaussianDistribution.sample draws it
# (backend/nn/vae.py:27-29; the seed is the one modules/rng.py's randn last
# gave torch.manual_seed, (seed + 100000) % 65536).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/torch-cpu-noise-oracle.py <out dir>
#
# Writes <out dir>/torch-cpu-noise.bin, f32 little-endian, the cases in order:
CASES = [
    (44769, (1, 4, 64, 64)),    # (20260929 + 100000) % 65536, a 512 px latent
    (44769, (1, 4, 128, 128)),  # the same seed, a 1024 px latent
    (0, (1, 4, 5, 5)),          # 100 values: the last 16 are drawn again
    (65535, (1, 4, 8, 8)),
]
# and prints each case with its first four values.

import os
import sys

import numpy as np
import torch


def main():
    out = sys.argv[1]
    parts = []
    for seed, shape in CASES:
        torch.manual_seed(seed)
        x = torch.randn(shape)
        parts.append(x.numpy().astype('<f4').ravel())
        print(seed, shape, [float(v) for v in x.flatten()[:4]])
    with open(os.path.join(out, 'torch-cpu-noise.bin'), 'wb') as f:
        f.write(np.concatenate(parts).tobytes())
    print(f'wrote {sum(p.size for p in parts)} values')
    return 0


if __name__ == '__main__':
    sys.exit(main())
