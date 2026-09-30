# esrgan-oracle.py -- the reference for Diffusion chapter Esrgan: spandrel's
# ESRGAN model, the loader Forge's upscalers use (modules/modelloader.py,
# load_spandrel_model), run on the CUDA device over one tile.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/esrgan-oracle.py <out dir>
#
# The tile is 3 x 32 x 32, value (c, y, x) = ((97 c + 31 y + 17 x) mod 256) / 255.
# Writes <out dir>/esrgan-tile.bin, f32 little-endian, for RealESRGAN x4plus:
# the 3 x 128 x 128 output in f32, then the same with the model and the tile
# in f16 (Forge's prefer_half), widened to f32.
import os
import sys

import numpy as np
import spandrel
import torch

PATH = r'D:\AI\DiffusionForge\webui\models\RealESRGAN\RealESRGAN_x4plus.pth'


def main():
    out = sys.argv[1]
    c, y, x = np.meshgrid(np.arange(3), np.arange(32), np.arange(32), indexing='ij')
    tile = torch.tensor(((97 * c + 31 * y + 17 * x) % 256) / 255.0, dtype=torch.float32)[None].cuda()
    model = spandrel.ModelLoader().load_from_file(PATH).model.eval().cuda()
    with torch.no_grad():
        y32 = model(tile).float().cpu()
        y16 = model.half()(tile.half()).float().cpu()
    print('output', tuple(y32.shape), 'max |f32 - f16|', float((y32 - y16).abs().max()))
    with open(os.path.join(out, 'esrgan-tile.bin'), 'wb') as f:
        f.write(y32.numpy().astype('<f4').tobytes())
        f.write(y16.numpy().astype('<f4').tobytes())
    return 0


if __name__ == '__main__':
    sys.exit(main())
