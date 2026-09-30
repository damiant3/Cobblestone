# resize-oracle.py -- the reference for Diffusion chapter Resize: Pillow's
# Image.resize with LANCZOS in Forge's Python (Pillow 9.5), over the 256 x 192
# picture of build/esrgan-picture-oracle.py.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/resize-oracle.py <out dir>
#
# Writes <out dir>/resize-lanczos.bin: the picture's RGB bytes, then its
# resizes in SIZES order, each as RGB bytes.
SIZES = [(384, 288), (100, 75), (256, 150), (200, 192)]

import os
import sys

import numpy as np
from PIL import Image

W, H = 256, 192


def main():
    y, x = np.mgrid[0:H, 0:W]
    pic = np.stack([(3 * x + y) % 256, (x * y // 7) % 256, (5 * (x ^ y)) % 256], axis=2).astype(np.uint8)
    im = Image.fromarray(pic, 'RGB')
    with open(os.path.join(sys.argv[1], 'resize-lanczos.bin'), 'wb') as f:
        f.write(pic.tobytes())
        for w, h in SIZES:
            f.write(np.asarray(im.resize((w, h), resample=Image.LANCZOS)).tobytes())
    print('wrote', SIZES)
    return 0


if __name__ == '__main__':
    sys.exit(main())
