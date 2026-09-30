# pth-oracle.py -- the reference for Diffusion chapter PthFile: every tensor
# torch.load finds in the installed RealESRGAN checkpoints (Forge's
# models\RealESRGAN, the upscalers Forge's hires fix offers), in state-dict
# order, from the dict under params_ema (else params, else the top dict).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/pth-oracle.py <out dir>
#
# Writes <out dir>/pth-realesrgan.bin, little-endian, for each file in FILES:
#   u32 tensor count, then per tensor: u32 name length, the ASCII name, u32
#   ndim, u32 per dim, f32 first value, f32 last value (as stored, row-major).
FILES = [
    r'D:\AI\DiffusionForge\webui\models\RealESRGAN\RealESRGAN_x4plus.pth',
    r'D:\AI\DiffusionForge\webui\models\RealESRGAN\RealESRGAN_x4plus_anime_6B.pth',
]

import os
import struct
import sys

import torch


def main():
    out = bytearray()
    for p in FILES:
        sd = torch.load(p, map_location='cpu', weights_only=True)
        sd = sd.get('params_ema', sd.get('params', sd))
        out += struct.pack('<I', len(sd))
        for name, t in sd.items():
            b = name.encode('ascii')
            flat = t.contiguous().flatten().float()
            out += struct.pack('<I', len(b)) + b + struct.pack('<I', t.dim())
            out += b''.join(struct.pack('<I', d) for d in t.shape)
            out += struct.pack('<ff', float(flat[0]), float(flat[-1]))
        print(os.path.basename(p), len(sd), 'tensors, dtypes', sorted({str(t.dtype) for t in sd.values()}))
    with open(os.path.join(sys.argv[1], 'pth-realesrgan.bin'), 'wb') as f:
        f.write(out)
    print('wrote', len(out), 'bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
