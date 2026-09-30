# sag-oracle.py -- the reference for SAG's blur map (Forge's extensions-builtin/sd_forge_sag):
# its own create_blur_map and gaussian_blur_2d, lifted out of forge_sag.py, over attention
# probabilities and an uncond prediction built below, f32 on the CUDA device, at the
# extension's defaults (blur sigma 2, threshold 1).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/sag-oracle.py <out dir>
#
# Writes <out dir>/sag.bin: per case in CASES order, the degraded 1 x 4 x LH x LW, f32
# little-endian. Every input is a multiple of a power of two, so the probabilities, their
# head means and column sums are exact in f32 and the mask is the same on both sides.

import ast
import math
import os
import sys
import types

import torch
from einops import rearrange, repeat

SRC = r'D:\AI\DiffusionForge\webui\extensions-builtin\sd_forge_sag\scripts\forge_sag.py'
HEADS = 2
# latent height, width, and the attended block's rows (SDXL: the latent halved twice by
# ceiling, SD1.5: three times)
CASES = [(16, 16, 16), (64, 96, 96), (75, 75, 361)]


def lift():
    tree = ast.parse(open(SRC, encoding='utf-8').read(), SRC)
    nodes = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in ('create_blur_map', 'gaussian_blur_2d')]
    env = {'torch': torch, 'math': math, 'rearrange': rearrange, 'repeat': repeat, 'shared': types.SimpleNamespace()}
    exec(compile(ast.Module(nodes, []), SRC, 'exec'), env)
    return env['create_blur_map']


def unit(rows):
    return 2.0 ** -(2 * rows - 1).bit_length()


def attn(rows, dev):
    h = torch.arange(HEADS, dtype=torch.float64).reshape(HEADS, 1, 1)
    i = torch.arange(rows, dtype=torch.float64).reshape(1, rows, 1)
    j = torch.arange(rows, dtype=torch.float64).reshape(1, 1, rows)
    k = (j * 29 % 7) + ((i * 13 + h * 7) % 3)
    return (k * unit(rows) / 2.0).to(torch.float32).to(dev)


def latent(lh, lw, dev):
    i = torch.arange(4 * lh * lw, dtype=torch.float64)
    return (((i * 37 + 11) % 257 - 128) / 64.0).to(torch.float32).reshape(1, 4, lh, lw).to(dev)


def main():
    dev = torch.device('cuda')
    create_blur_map = lift()
    out = bytearray()
    for lh, lw, rows in CASES:
        a = attn(rows, dev)
        mask = (a.mean(0).sum(0) > 1.0).sum().item()
        d = create_blur_map(latent(lh, lw, dev), a, 2.0, 1.0)
        out += d.float().cpu().numpy().tobytes()
        print(lh, lw, rows, 'mask', mask, 'of', rows)
    with open(os.path.join(sys.argv[1], 'sag.bin'), 'wb') as f:
        f.write(bytes(out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
