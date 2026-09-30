# variation-oracle.py -- the reference for codex/test/apps/diffusion-variation:
# Diffusion Forge's own modules/rng.py (slerp and ImageRNG.first) executed with
# stand-in devices / shared modules (randn_source "GPU", no eta noise seed
# delta), so the variation seed, its strength and seed resize are Forge's code.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/variation-oracle.py <out dir>
#
# The draws depend on the card (see torch-noise-oracle.py); this refuses to
# write on a card whose CUDA normal spreads over other than 204 blocks.
#
# Writes <out dir>/variation.bin, f32 little-endian, ImageRNG(...).first()[0]
# for each case in CASES order.

import os
import sys
import types

import numpy as np
import torch

RNG = r'D:\AI\DiffusionForge\webui\modules\rng.py'

# (shape, seed, subseed, strength, resize-from h, resize-from w)
CASES = [
    ((4, 64, 64), 7201, 99, 0.3, 0, 0),
    ((4, 64, 64), 7201, 99, 1.0, 0, 0),
    ((4, 64, 64), 20260929, 5, 0.5, 384, 640),
    ((4, 64, 64), 11, 0, 0.0, 520, 456),
    ((16, 64, 64), 1, 2, 0.7, 0, 0),
]


def forge_rng():
    dev = torch.device('cuda')
    opts = types.SimpleNamespace(randn_source='GPU', forge_try_reproduce='None', eta_noise_seed_delta=0)
    devices = types.ModuleType('modules.devices')
    devices.device = dev
    devices.cpu = torch.device('cpu')
    shared = types.ModuleType('modules.shared')
    shared.opts = opts
    shared.device = dev
    philox = types.ModuleType('modules.rng_philox')
    pkg = types.ModuleType('modules')
    pkg.devices, pkg.shared, pkg.rng_philox = devices, shared, philox
    sys.modules.update({'modules': pkg, 'modules.devices': devices, 'modules.shared': shared, 'modules.rng_philox': philox})
    ns = {'__name__': 'modules.rng'}
    with open(RNG, encoding='utf-8') as f:
        exec(compile(f.read(), RNG, 'exec'), ns)
    return ns


def main():
    out = sys.argv[1]
    p = torch.cuda.get_device_properties(0)
    blocks = p.multi_processor_count * (p.max_threads_per_multi_processor // 256)
    print(f'{p.name}: {blocks} blocks; torch {torch.__version__}')
    if blocks != 204:
        print('refused: the engine assumes 204 blocks')
        return 1
    rng = forge_rng()
    parts = []
    for shape, seed, sub, strength, fh, fw in CASES:
        g = rng['ImageRNG'](shape, [seed], subseeds=[sub], subseed_strength=strength, seed_resize_from_h=fh, seed_resize_from_w=fw)
        parts.append(g.first()[0].float().cpu().numpy().astype('<f4').ravel())
    data = np.concatenate(parts)
    with open(os.path.join(out, 'variation.bin'), 'wb') as f:
        f.write(data.tobytes())
    print(f'wrote {data.size} values')
    return 0


if __name__ == '__main__':
    sys.exit(main())
