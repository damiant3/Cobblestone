# dat-oracle.py -- the reference for Diffusion chapter Dat: spandrel's DAT,
# the loader Forge's DAT upscaler uses (modelloader.load_spandrel_model; no
# half, spandrel reports none), on the CUDA device.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/dat-oracle.py <out dir>
#
# Writes <out dir>/dat-tile.bin, f32 little-endian: the 3 x 256 x 128 output
# for the 3 x 64 x 32 tile (c, y, x) = ((97 c + 31 y + 17 x) mod 256) / 255,
# a non-square tile so a transposed axis cannot pass; group 0 block 2 is a
# rolled block.
#
# Writes <out dir>/dat-picture.bin: the 256 x 192 RGB picture of
# esrgan-picture-oracle.py, then its 1024 x 768 upscale by Forge's own
# upscale_with_model, upscale_pil_patch and the byte conversions
# (modules/upscaler_utils.py) over images.py's Grid, split_grid and
# combine_grid, compiled from their text at run time, at the default options
# DAT_tile 192 and DAT_tile_overlap 8.
import ast
import contextlib
import logging
import math
import os
import sys
import types
from collections import namedtuple
from typing import Callable

import numpy as np
import spandrel
import torch
import tqdm
from PIL import Image

WEBUI = r'D:\AI\DiffusionForge\webui'
PATH = os.path.join(WEBUI, r'models\DAT\DAT_x4.pth')
W, H = 256, 192


def forge_source(rel, keep, ns):
    tree = ast.parse(open(os.path.join(WEBUI, rel), encoding='utf-8').read())
    tree.body = [n for n in tree.body if isinstance(n, (ast.FunctionDef, ast.ClassDef)) and n.name in keep]
    exec(compile(tree, rel, 'exec'), ns)


def main():
    out = sys.argv[1]
    model = spandrel.ModelLoader(device='cuda').load_from_file(PATH)
    assert model.architecture.name == 'DAT' and not model.supports_half
    net = model.model.eval()
    c, y, x = np.meshgrid(np.arange(3), np.arange(64), np.arange(32), indexing='ij')
    tile = torch.tensor(((97 * c + 31 * y + 17 * x) % 256) / 255.0, dtype=torch.float32)[None].cuda()
    with torch.no_grad():
        y32 = net(tile).float().cpu()
    print('tile output', tuple(y32.shape))
    with open(os.path.join(out, 'dat-tile.bin'), 'wb') as f:
        f.write(y32.numpy().astype('<f4').tobytes())
    shared = types.SimpleNamespace(opts=types.SimpleNamespace(enable_upscale_progressbar=False), state=types.SimpleNamespace(interrupted=False, skipped=False))
    ns = {'np': np, 'torch': torch, 'tqdm': tqdm, 'Image': Image, 'math': math, 'namedtuple': namedtuple, 'shared': shared, 'logger': logging.getLogger('forge'), 'Callable': Callable,
          'devices': types.SimpleNamespace(without_autocast=contextlib.nullcontext), 'torch_utils': types.SimpleNamespace(get_param=lambda m: next(m.parameters()))}
    forge_source(r'modules\images.py', {'Grid', 'split_grid', 'combine_grid'}, ns)
    ns['images'] = types.SimpleNamespace(Grid=ns['Grid'], split_grid=ns['split_grid'], combine_grid=ns['combine_grid'])
    forge_source(r'modules\upscaler_utils.py', {'pil_image_to_torch_bgr', 'torch_bgr_to_pil_image', 'upscale_pil_patch', 'upscale_with_model'}, ns)
    yy, xx = np.mgrid[0:H, 0:W]
    pic = np.stack([(3 * xx + yy) % 256, (xx * yy // 7) % 256, (5 * (xx ^ yy)) % 256], axis=2).astype(np.uint8)
    ans = np.asarray(ns['upscale_with_model'](net, Image.fromarray(pic, 'RGB'), tile_size=192, tile_overlap=8).convert('RGB'))
    print('picture answer', ans.shape)
    with open(os.path.join(out, 'dat-picture.bin'), 'wb') as f:
        f.write(pic.tobytes())
        f.write(ans.tobytes())
    return 0


if __name__ == '__main__':
    sys.exit(main())
