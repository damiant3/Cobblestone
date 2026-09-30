# esrgan-picture-oracle.py -- the reference for Diffusion chapter Esrgan's
# whole-picture upscale: Diffusion Forge's own Extras upscale ("R-ESRGAN 4x+",
# RealESRGAN_x4plus, by 4) through its API, which runs
# modules/upscaler_utils.py's upscale_with_model with the ESRGAN_tile and
# ESRGAN_tile_overlap options.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/esrgan-picture-oracle.py <out dir>
#
# with Forge serving its API on port 7861 (launch.py --api --nowebui --port
# 7861). The picture is 256 x 192 RGB, (r, g, b) at (x, y) = ((3x + y) mod
# 256, (x y / 7) mod 256, 5 (x xor y) mod 256), two overlapping tiles wide.
# Writes <out dir>/esrgan-picture.bin: the picture's bytes, then Forge's
# 1024 x 768 answer's RGB bytes; prints the tile options.
import base64
import io
import json
import os
import sys
import urllib.request

import numpy as np
from PIL import Image

API = 'http://127.0.0.1:7861/sdapi/v1/'
W, H = 256, 192


def call(path, body=None):
    req = urllib.request.Request(API + path, data=None if body is None else json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=600) as r:
        return json.loads(r.read())


def main():
    y, x = np.mgrid[0:H, 0:W]
    pic = np.stack([(3 * x + y) % 256, (x * y // 7) % 256, (5 * (x ^ y)) % 256], axis=2).astype(np.uint8)
    buf = io.BytesIO()
    Image.fromarray(pic, 'RGB').save(buf, 'PNG')
    opts = call('options')
    print('ESRGAN_tile', opts.get('ESRGAN_tile'), 'ESRGAN_tile_overlap', opts.get('ESRGAN_tile_overlap'))
    r = call('extra-single-image', {'image': base64.b64encode(buf.getvalue()).decode(), 'resize_mode': 0, 'upscaling_resize': 4, 'upscaler_1': 'R-ESRGAN 4x+'})
    out = np.asarray(Image.open(io.BytesIO(base64.b64decode(r['image']))).convert('RGB'))
    print('answer', out.shape)
    with open(os.path.join(sys.argv[1], 'esrgan-picture.bin'), 'wb') as f:
        f.write(pic.tobytes())
        f.write(out.tobytes())
    return 0


if __name__ == '__main__':
    sys.exit(main())
