# freeu-oracle.py -- the reference for FreeU (Forge's extensions-builtin/sd_forge_freeu):
# its own Fourier_filter and patch_freeu_v2, lifted out of forge_freeu.py, applied as
# Forge's UNet applies an output_block_patch (backend/nn/unet.py:737) to (h, hsp)
# pairs built below, in f32 on the CUDA device, model_channels 320 (SDXL and SD1.5).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/freeu-oracle.py <out dir>
#
# Writes <out dir>/freeu.bin, per case in CASES order: h then hsp after the patch,
# f32 little-endian, each 1 x C x H x W.

import ast
import os
import sys
import types

import torch

SRC = r'D:\AI\DiffusionForge\webui\extensions-builtin\sd_forge_freeu\scripts\forge_freeu.py'
# (preset b1, b2, s1, s2), channels, side
PRESETS = {'forge': (1.01, 1.02, 0.99, 0.95), 'sdxl': (1.3, 1.4, 0.9, 0.2)}
CASES = [('forge', 1280, 8), ('forge', 640, 16), ('forge', 320, 8), ('sdxl', 1280, 8), ('sdxl', 640, 16)]


def lift():
    tree = ast.parse(open(SRC, encoding='utf-8').read(), SRC)
    nodes = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in ('Fourier_filter', 'patch_freeu_v2')]
    env = {'torch': torch, 'FreeUForForge': types.SimpleNamespace(doFreeU=True)}
    exec(compile(ast.Module(nodes, []), SRC, 'exec'), env)
    return env['patch_freeu_v2']


def tensor(c, side, salt, dev):
    i = torch.arange(c * side * side, dtype=torch.float64)
    v = ((i * (37 + salt) + 11) % 257 - 128) / 64.0 + torch.sin(i * 0.01 * (salt + 1))
    return v.to(torch.float32).reshape(1, c, side, side).to(dev)


def main():
    dev = torch.device('cuda')
    patch_freeu_v2 = lift()
    out = bytearray()
    for preset, c, side in CASES:
        captured = {}
        patcher = types.SimpleNamespace(model=types.SimpleNamespace(diffusion_model=types.SimpleNamespace(config={'model_channels': 320})))
        patcher.clone = lambda: types.SimpleNamespace(set_model_output_block_patch=lambda p: captured.setdefault('p', p))
        patch_freeu_v2(patcher, *PRESETS[preset])
        h, hsp = captured['p'](tensor(c, side, 0, dev), tensor(c, side, 5, dev), {})
        out += h.float().cpu().numpy().tobytes() + hsp.float().cpu().numpy().tobytes()
        print(preset, c, side)
    with open(os.path.join(sys.argv[1], 'freeu.bin'), 'wb') as f:
        f.write(bytes(out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
