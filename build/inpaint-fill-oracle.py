# inpaint-fill-oracle.py -- the reference for codex/test/apps/diffusion-inpaint-fill:
# Diffusion Forge's own masking.fill (modules/masking.py), its source lifted out
# of the module, over the picture and blurred mask built below, in CASES order.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/inpaint-fill-oracle.py <out dir>
#
# Writes <out dir>/inpaint-fill.bin: each case's mask (w x h bytes) then its
# filled RGB bytes. 72 x 40 is narrower than twice the box radius at 64 and 256
# (Pillow's BoxBlur edgeA > edgeB branch); 200 x 136 is wider at 64.

import ast
import os
import sys

import cv2
import numpy as np
from PIL import Image, ImageFilter, ImageOps

MASKING = r'D:\AI\DiffusionForge\webui\modules\masking.py'
CASES = [(72, 40), (200, 136)]


def forge_fill():
    with open(MASKING, encoding='utf-8') as f:
        tree = ast.parse(f.read(), MASKING)
    fn = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'fill')
    ns = {'Image': Image, 'ImageFilter': ImageFilter, 'ImageOps': ImageOps}
    exec(compile(ast.Module(body=[fn], type_ignores=[]), MASKING, 'exec'), ns)
    return ns['fill']


def main():
    fill = forge_fill()
    with open(os.path.join(sys.argv[1], 'inpaint-fill.bin'), 'wb') as f:
        for w, h in CASES:
            y, x = np.mgrid[0:h, 0:w]
            pic = np.stack([(3 * x + y) % 256, (x * y // 7) % 256, (5 * (x ^ y)) % 256], axis=2).astype(np.uint8)
            inside = ((x - w * 2 // 5) ** 2 + (y - h // 2) ** 2 < (h // 3) ** 2).astype(np.uint8) * 255
            mask = cv2.GaussianBlur(cv2.GaussianBlur(inside, (21, 1), 4), (1, 21), 4)
            out = fill(Image.fromarray(pic, 'RGB'), Image.fromarray(mask, 'L'))
            f.write(mask.tobytes())
            f.write(np.asarray(out, dtype=np.uint8).tobytes())
    print(f'wrote {len(CASES)} cases')
    return 0


if __name__ == '__main__':
    sys.exit(main())
