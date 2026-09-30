# soft-inpaint-oracle.py -- Diffusion Forge's soft inpainting arithmetic over
# formula inputs, written as the two files codex/test/apps/diffusion-soft-inpaint
# grades apps/diffusion/SoftInpaint against:
# reals.ref, little-endian f64 (per settings A then B, per sigma: the modified
# mask, 192 values, then latent_blend, 4 x 192; then the two weighted histogram
# filter passes over the latent distance, 192 each), and bytes.ref (per
# settings: the adaptive mask at latent size before the resize, 192 bytes, then
# the overlay mask at 128 x 96).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/soft-inpaint-oracle.py <out dir>
#
# Every function runs as Forge's own text, compiled from
# extensions-builtin/soft-inpainting/scripts/soft_inpainting.py:
# get_modified_nmask, latent_blend, weighted_histogram_filter and
# apply_adaptive_masks, the last with modules.processing and modules.images
# answering Forge's own create_binary_mask, uncrop and resize_image. The mask is
# float16, Forge's devices.dtype on this card (modules/devices.py:57), made as
# processing.py:1823-1831 makes it with mask_round off.
import ast, os, struct, sys, types
import numpy as np
import torch
from PIL import Image

WEBUI = r'D:\AI\DiffusionForge\webui'
SOFT = r'extensions-builtin\soft-inpainting\scripts\soft_inpainting.py'
LW, LH = 16, 12
HW = LW * LH
W, H = LW * 8, LH * 8
SIGMAS = [14.6, 1.2, 0.03]
SETTINGS = [(1.0, 0.5, 4.0, 0.0, 0.5, 2.0), (2.0, 1.5, 2.0, 0.6, 0.8, 3.0)]


def forge_function(rel, name, env):
    src = open(os.path.join(WEBUI, rel), encoding='utf-8').read()
    for node in ast.parse(src).body:
        if isinstance(node, (ast.FunctionDef, ast.ClassDef)) and node.name == name:
            exec(compile(ast.Module([node], []), rel, 'exec'), env)
            return env[name]
    sys.exit('REFUSE: no %s in %s' % (name, rel))


def init_at(c, p): return ((p * 37 + c * 11) % 97) / 97 * 4 - 2
def denoised_at(c, p): return ((p * 53 + c * 29 + 5) % 89) / 89 * 4 - 2
def samples_at(c, p): return init_at(c, p) + (((p * 7 + c * 3) % 13) - 6) * (0.35 if p % LW >= 8 else 0.05)
def mask_at(p): return min(255, max(0, (p % LW - 4) * 40 + (p // LW) * 7))


def latent(f):
    return torch.tensor([[[[f(c, y * LW + x) for x in range(LW)] for y in range(LH)] for c in range(4)]], dtype=torch.float32)


def main():
    out = sys.argv[1]
    import math
    env = {'np': np, 'math': math, 'float64': lambda t: torch.float64}
    Settings = forge_function(SOFT, 'SoftInpaintingSettings', env)
    modified = forge_function(SOFT, 'get_modified_nmask', env)
    blend = forge_function(SOFT, 'latent_blend', env)
    histogram = forge_function(SOFT, 'weighted_histogram_filter', env)
    forge_function(SOFT, 'smootherstep', env)
    kernel_of = forge_function(SOFT, 'get_gaussian_kernel', env)
    adaptive = forge_function(SOFT, 'apply_adaptive_masks', env)

    ienv = {'Image': Image, 'LANCZOS': Image.Resampling.LANCZOS, 'opts': types.SimpleNamespace(upscaler_for_img2img=None)}
    resize_image = forge_function(r'modules\images.py', 'resize_image', ienv)
    penv = {'Image': Image}
    binary = forge_function(r'modules\processing.py', 'create_binary_mask', penv)
    uncrop = forge_function(r'modules\processing.py', 'uncrop', penv)
    pkg = types.ModuleType('modules')
    proc = types.ModuleType('modules.processing'); proc.create_binary_mask = binary; proc.uncrop = uncrop
    imgs = types.ModuleType('modules.images'); imgs.resize_image = resize_image
    pkg.processing, pkg.images = proc, imgs
    sys.modules.update({'modules': pkg, 'modules.processing': proc, 'modules.images': imgs})

    a = latent(init_at)
    b = latent(denoised_at)
    s = latent(samples_at)
    latmask = np.array([[mask_at(y * LW + x) for x in range(LW)] for y in range(LH)], dtype=np.float32) / 255
    nmask = torch.asarray(np.tile(latmask[None], (4, 1, 1))).type(torch.float16)

    reals, raw = [], bytearray()
    for st in SETTINGS:
        cfg = Settings(*st)
        for sigma in SIGMAS:
            t = modified(cfg, nmask, torch.tensor([sigma, sigma])[0])
            reals += [float(v) for v in t[0].flatten()]
            reals += [float(v) for v in blend(cfg, a.clone(), b.clone(), t).flatten()]
    distance = torch.norm(s - a, p=2, dim=1)[0].float().numpy()
    kernel, center = kernel_of(stddev_radius=1.5, max_radius=2)
    f1 = histogram(distance, kernel, center, percentile_min=0.9, percentile_max=1, min_width=1)
    f2 = histogram(f1, kernel, center, percentile_min=0.25, percentile_max=0.75, min_width=1)
    reals += [float(v) for v in f1.flatten()] + [float(v) for v in f2.flatten()]
    for st in SETTINGS:
        cfg = Settings(*st)
        # The lines of apply_adaptive_masks up to Image.fromarray, for the
        # latent-size bytes; the whole function for the overlay mask.
        lm = nmask[0].float()
        ms = 1 - (torch.clamp(lm, min=0, max=1) ** (cfg.mask_blend_scale / 2))
        ms = (0.5 * (1 - cfg.composite_mask_influence) + ms * cfg.composite_mask_influence)
        ms = (ms / (1.00001 - ms)).cpu().numpy()
        cm = f2 / (cfg.composite_difference_threshold * ms)
        cm = 1 / (1 + cm ** cfg.composite_difference_contrast)
        cm = (255. * (1 - env['smootherstep'](cm))).astype(np.uint8)
        raw += cm.tobytes()
        masks = adaptive(cfg, nmask, a, s, [Image.new('RGBA', (W, H))], W, H, None)
        raw += np.asarray(masks[0].convert('L')).tobytes()
    os.makedirs(out, exist_ok=True)
    open(os.path.join(out, 'reals.ref'), 'wb').write(struct.pack('<%dd' % len(reals), *reals))
    open(os.path.join(out, 'bytes.ref'), 'wb').write(bytes(raw))
    print('reals', len(reals), 'bytes', len(raw))


if __name__ == '__main__':
    main()
