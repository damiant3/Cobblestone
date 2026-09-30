# swinir-oracle.py -- the reference for Diffusion chapter SwinIR: spandrel's
# SwinIR, the loader Forge's SwinIR extension uses (modelloader.
# load_spandrel_model; no half, spandrel reports none), on the CUDA device.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/swinir-oracle.py <out dir>
#
# Writes <out dir>/swinir-tile.bin, f32 little-endian: the 3 x 160 x 128
# output for the 3 x 40 x 32 tile (c, y, x) = ((97 c + 31 y + 17 x) mod 256)
# / 255, a non-square tile so a transposed axis cannot pass, 4 x 5 windows,
# the shifted blocks' mask on both axes.
#
# Writes <out dir>/swinir-picture.bin: the 256 x 192 RGB picture of
# esrgan-picture-oracle.py, then its 1024 x 768 upscale by Forge's own
# upscale_2, tiled_upscale_2, pil_image_to_torch_bgr and
# torch_bgr_to_pil_image, compiled from webui/modules/upscaler_utils.py's
# text at run time (Forge's API need not be up), at the installed options
# SWIN_tile 192 and SWIN_tile_overlap 8: two tiles, overlapping 128 columns.
import ast
import os
import sys
import types

import numpy as np
import spandrel
import torch
import tqdm
from PIL import Image

WEBUI = r'D:\AI\DiffusionForge\webui'
PATH = os.path.join(WEBUI, r'models\SwinIR\SwinIR_4x.pth')
W, H = 256, 192


def forge_upscaler_utils():
    src = open(os.path.join(WEBUI, r'modules\upscaler_utils.py'), encoding='utf-8').read()
    keep = {'pil_image_to_torch_bgr', 'torch_bgr_to_pil_image', 'tiled_upscale_2', 'upscale_2'}
    tree = ast.parse(src)
    tree.body = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in keep]
    state = types.SimpleNamespace(interrupted=False, skipped=False)
    shared = types.SimpleNamespace(opts=types.SimpleNamespace(enable_upscale_progressbar=False), state=state)
    torch_utils = types.SimpleNamespace(get_param=lambda m: next(m.parameters()))
    import logging
    ns = {'np': np, 'torch': torch, 'tqdm': tqdm, 'Image': Image, 'shared': shared, 'torch_utils': torch_utils, 'logger': logging.getLogger('forge'), 'Callable': object}
    exec(compile(tree, 'upscaler_utils.py', 'exec'), ns)
    return ns


def main():
    out = sys.argv[1]
    model = spandrel.ModelLoader(device='cuda').load_from_file(PATH)
    assert model.architecture.name == 'SwinIR' and not model.supports_half
    net = model.model.eval()
    c, y, x = np.meshgrid(np.arange(3), np.arange(40), np.arange(32), indexing='ij')
    tile = torch.tensor(((97 * c + 31 * y + 17 * x) % 256) / 255.0, dtype=torch.float32)[None].cuda()
    with torch.no_grad():
        y32 = net(tile).float().cpu()
    print('tile output', tuple(y32.shape), 'cudnn tf32', torch.backends.cudnn.allow_tf32, 'matmul tf32', torch.backends.cuda.matmul.allow_tf32)
    with open(os.path.join(out, 'swinir-tile.bin'), 'wb') as f:
        f.write(y32.numpy().astype('<f4').tobytes())
    yy, xx = np.mgrid[0:H, 0:W]
    pic = np.stack([(3 * xx + yy) % 256, (xx * yy // 7) % 256, (5 * (xx ^ yy)) % 256], axis=2).astype(np.uint8)
    u = forge_upscaler_utils()
    ans = np.asarray(u['upscale_2'](Image.fromarray(pic, 'RGB'), net, tile_size=192, tile_overlap=8, scale=model.scale, desc='SwinIR').convert('RGB'))
    print('picture answer', ans.shape)
    with open(os.path.join(out, 'swinir-picture.bin'), 'wb') as f:
        f.write(pic.tobytes())
        f.write(ans.tobytes())
    return 0


if __name__ == '__main__':
    sys.exit(main())
