# pixel-oracle.py -- Diffusion Forge's decoded-image-to-byte conversion over
# f32 inputs chosen at every byte boundary, printed as the two Codex list
# literals codex/test/apps/diffusion-pixel-forge grades
# apps/diffusion/ImageOut's io-pixel against: the inputs as f32 bit patterns,
# then the bytes Forge writes for them.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/pixel-oracle.py
#
# The conversion is Forge's own lines, modules/processing.py:1012-1013
# (torch f32, clamp((x + 1) / 2, 0, 1)) and :1038-1039 (255. times the numpy
# f32 array, then astype(np.uint8), which truncates).
# Inputs: for each byte k in 1..255, the f32 nearest 2k/255 - 1 and the three
# f32 values either side of it; zeros, +-1, +-1.5, +-inf, the smallest
# normal and subnormal of each sign, values near 2^-26 on each side, and 256
# draws uniform in [-1.1, 1.1] from a fixed seed.
import numpy as np
import torch

def f32(v):
    return np.float32(v)

xs = []
for k in range(1, 256):
    c = f32(2.0 * k / 255.0 - 1.0)
    below, above = [c], [c]
    for i in range(3):
        below.append(np.nextafter(below[-1], f32(-2)))
        above.append(np.nextafter(above[-1], f32(2)))
    xs += list(reversed(below[1:])) + [c] + above[1:]
xs += [f32(0), f32(-0.0), f32(1), f32(-1), f32(1.5), f32(-1.5), f32(np.inf), f32(-np.inf)]
tiny = np.finfo(np.float32).tiny
sub = np.nextafter(f32(0), f32(1))
xs += [f32(tiny), f32(-tiny), sub, -sub]
for p in (-27, -26, -25, -24):
    for s in (1, -1):
        v = f32(s * 2.0 ** p)
        xs += [np.nextafter(v, f32(0)), v, np.nextafter(v, f32(s * 2))]
rng = np.random.default_rng(20260929)
xs += list(rng.uniform(-1.1, 1.1, 256).astype(np.float32))

arr = np.array(xs, dtype=np.float32)
t = torch.from_numpy(arr.copy()).float()
t = torch.clamp((t + 1.0) / 2.0, min=0.0, max=1.0)
out = (255. * t.numpy()).astype(np.uint8)

bits = arr.view(np.uint32)
print(len(xs))
print(', '.join(str(int(b)) for b in bits))
print(', '.join(str(int(b)) for b in out))
