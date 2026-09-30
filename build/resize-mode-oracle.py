# resize-mode-oracle.py -- the reference for codex/test/apps/diffusion-resize-mode:
# Diffusion Forge's own images.resize_image (modules/images.py), its source
# lifted out of the module and run with opts.upscaler_for_img2img None, so every
# resize is Pillow's LANCZOS as img2img does it, over the 256 x 192 picture of
# build/resize-oracle.py.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/resize-mode-oracle.py <out dir>
#
# Writes <out dir>/resize-mode.bin: each case's RGB bytes in CASES order, then
# each L_CASES case's L bytes.

import ast
import os
import sys
import types

import numpy as np
from PIL import Image

IMAGES = r'D:\AI\DiffusionForge\webui\modules\images.py'
W, H = 256, 192
# (resize_mode, width, height)
CASES = [(0, 320, 256), (1, 192, 256), (1, 320, 128), (2, 320, 192), (2, 192, 256)]
# An L picture (an inpaint mask) through the same function; mode 3 takes its fill branch.
L_CASES = [(0, 320, 256), (1, 192, 256), (2, 320, 192), (2, 192, 256), (3, 320, 192)]


def forge_resize_image():
    with open(IMAGES, encoding='utf-8') as f:
        tree = ast.parse(f.read(), IMAGES)
    fn = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'resize_image')
    ns = {'Image': Image, 'LANCZOS': Image.LANCZOS,
          'opts': types.SimpleNamespace(upscaler_for_img2img=None),
          'shared': types.SimpleNamespace(sd_upscalers=[])}
    exec(compile(ast.Module(body=[fn], type_ignores=[]), IMAGES, 'exec'), ns)
    return ns['resize_image']


def main():
    y, x = np.mgrid[0:H, 0:W]
    pic = np.stack([(3 * x + y) % 256, (x * y // 7) % 256, (5 * (x ^ y)) % 256], axis=2).astype(np.uint8)
    im = Image.fromarray(pic, 'RGB')
    resize_image = forge_resize_image()
    with open(os.path.join(sys.argv[1], 'resize-mode.bin'), 'wb') as f:
        for mode, w, h in CASES:
            out = resize_image(mode, im, w, h)
            f.write(np.asarray(out.convert('RGB'), dtype=np.uint8).tobytes())
        gray = Image.fromarray(((3 * x + x * y // 7) % 256).astype(np.uint8), 'L')
        for mode, w, h in L_CASES:
            out = resize_image(mode, gray, w, h)
            f.write(np.asarray(out.convert('L'), dtype=np.uint8).tobytes())
    print(f'wrote {len(CASES)} cases')
    return 0


if __name__ == '__main__':
    sys.exit(main())
