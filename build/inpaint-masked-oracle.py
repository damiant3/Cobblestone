# inpaint-masked-oracle.py -- the reference for codex/test/apps/diffusion-inpaint-masked:
# Diffusion Forge's inpaint "only masked" (inpaint_full_res, modules/processing.py
# 1718-1729, 1776-1778 and apply_overlay), from Forge's own get_crop_region_v2 and
# expand_crop_region (modules/masking.py), resize_image (modules/images.py), and
# uncrop and apply_overlay (modules/processing.py), lifted out of their modules.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/inpaint-masked-oracle.py <out dir>
#
# Writes <out dir>/inpaint-masked.bin, per case in CASES order: the crop region
# x1 y1 x2 y2 as le32; the blurred mask (SW x SH); the picture cropped and
# resized to W x H (RGB); the mask cropped and resized (L); the pasted-back
# picture (SW x SH, RGB) for the generated picture built below.

import ast
import os
import struct
import sys
import types

import cv2
import numpy as np
from PIL import Image, ImageOps

WEBUI = r'D:\AI\DiffusionForge\webui\modules'
SW, SH, W, H, BLUR = 120, 80, 64, 48, 4
# (blob centre x, y, radius, padding)
CASES = [(88, 22, 9, 8), (40, 74, 7, 0)]


def lift(rel, names, env):
    path = os.path.join(WEBUI, rel)
    src = open(path, encoding='utf-8').read()
    nodes = [n for n in ast.parse(src).body if isinstance(n, ast.FunctionDef) and n.name in names]
    exec(compile(ast.Module(nodes, []), path, 'exec'), env)
    return env


def main():
    im = lift('images.py', ['resize_image'], {'Image': Image, 'LANCZOS': Image.LANCZOS, 'opts': types.SimpleNamespace(upscaler_for_img2img=None), 'shared': types.SimpleNamespace(sd_upscalers=[])})
    mk = lift('masking.py', ['get_crop_region_v2', 'expand_crop_region'], {'Image': Image})
    pr = lift('processing.py', ['uncrop', 'apply_overlay'], {'Image': Image, 'images': types.SimpleNamespace(resize_image=im['resize_image'])})
    y, x = np.mgrid[0:SH, 0:SW]
    pic = Image.fromarray(np.stack([(3 * x + y) % 256, (x * y // 7) % 256, (5 * (x ^ y)) % 256], axis=2).astype(np.uint8), 'RGB')
    gy, gx = np.mgrid[0:H, 0:W]
    gen = Image.fromarray(np.stack([(gx * 53 + gy * 3) % 256, (gx * 29 + gy * 101) % 256, (gx * gy + 17) % 256], axis=2).astype(np.uint8), 'RGB')
    with open(os.path.join(sys.argv[1], 'inpaint-masked.bin'), 'wb') as f:
        for cx, cy, r, pad in CASES:
            inside = (((x - cx) ** 2 + (y - cy) ** 2) < r * r).astype(np.uint8) * 255
            k = 2 * int(2.5 * BLUR + 0.5) + 1
            m = cv2.GaussianBlur(cv2.GaussianBlur(inside, (k, 1), BLUR), (1, k), BLUR)
            image_mask = Image.fromarray(m)
            # processing.py:1718-1727
            mask_for_overlay = image_mask
            mask = image_mask.convert('L')
            crop_region = mk['get_crop_region_v2'](mask, pad)
            crop_region = mk['expand_crop_region'](crop_region, W, H, mask.width, mask.height)
            x1, y1, x2, y2 = crop_region
            image_mask = im['resize_image'](2, mask.crop(crop_region), W, H)
            paste_to = (x1, y1, x2 - x1, y2 - y1)
            # processing.py:1770-1778
            image_masked = Image.new('RGBa', (pic.width, pic.height))
            image_masked.paste(pic.convert("RGBA").convert("RGBa"), mask=ImageOps.invert(mask_for_overlay.convert('L')))
            overlay = image_masked.convert('RGBA')
            crop = im['resize_image'](2, pic.crop(crop_region), W, H)
            result, _ = pr['apply_overlay'](gen, paste_to, overlay)
            f.write(struct.pack('<4i', x1, y1, x2, y2))
            f.write(m.tobytes())
            f.write(np.asarray(crop.convert('RGB'), dtype=np.uint8).tobytes())
            f.write(np.asarray(image_mask.convert('L'), dtype=np.uint8).tobytes())
            f.write(np.asarray(result.convert('RGB'), dtype=np.uint8).tobytes())
            print('case', (cx, cy, r, pad), 'region', crop_region)
    return 0


if __name__ == '__main__':
    sys.exit(main())
