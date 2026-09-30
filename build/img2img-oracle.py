# img2img-oracle.py -- Diffusion Forge's img2img, inpaint and hires-fix
# arithmetic over formula inputs, written as the two files
# codex/test/apps/diffusion-img2img-parts grades apps/diffusion/Img2Img against:
# reals.ref, little-endian f64 (the 8 schedule lengths, the schedules, the
# posterior, the latent resize, img2img over a stand-in model masked then
# not, masked content latent nothing then latent noise, the latent resize
# taken down), and bytes.ref (per mask, opaque then
# transparent: binary, blurred, overlay 64 x 48, reduced and latent 8 x 6;
# then the pasted-back picture, 64 x 48 x 3).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/img2img-oracle.py <out dir>
#
# Forge's own functions run where they stand alone: setup_img2img_steps
# (modules/sd_samplers_common.py), create_binary_mask and apply_overlay
# (modules/processing.py), taken from the installed source and executed with
# opts.img2img_fix_steps False; DiagonalGaussianDistribution (backend/nn/vae.py)
# with torch.randn answering the formula draws. The rest is Forge's own lines
# from StableDiffusionProcessingImg2Img.init and sample_hr_pass, over the same
# libraries: cv2.GaussianBlur, PIL resize, paste and alpha_composite, and
# torch.nn.functional.interpolate. The Karras schedule is k-diffusion's over
# the SDXL discrete sigmas, as build/sdxl-unet-oracle.py builds it.
import ast, os, struct, sys, types
import numpy as np
import torch
import cv2
from PIL import Image, ImageOps

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path[:0] = [WEBUI, os.path.join(WEBUI, 'packages_3rdparty'), os.path.join(WEBUI, 'repositories', 'huggingface_guess')]

def forge_function(rel, name, env):
    src = open(os.path.join(WEBUI, rel), encoding='utf-8').read()
    for node in ast.parse(src).body:
        if isinstance(node, ast.FunctionDef) and node.name == name:
            exec(compile(ast.Module([node], []), rel, 'exec'), env)
            return env[name]
    sys.exit('REFUSE: no %s in %s' % (name, rel))

def forge_module(rel, env):
    src = open(os.path.join(WEBUI, rel), encoding='utf-8').read()
    body = [n for n in ast.parse(src).body if not isinstance(n, ast.ImportFrom)]
    exec(compile(ast.Module(body, []), rel, 'exec'), env)
    return types.SimpleNamespace(**env)

W, H = 64, 48
BLUR = 4
SEED = 1234
CASES = [(6, 0.5, False), (6, 0.75, False), (20, 0.3, False), (10, 1.0, False), (6, 0.7, True), (4, 0.35, True), (6, 1.0, True), (3, 0.2, True)]

def mask_rgba(i, alpha):
    x, y = i % W, i // W
    inside = (x - 30) ** 2 + (y - 22) ** 2 < 15 ** 2 or (x > 50 and y < 10)
    v = 255 if inside else 0
    if alpha:
        return (200, 90, 30, v)
    g = 255 - (x * 7 + y * 3) % 40 if inside else (x * 7 + y * 3) % 40
    return (g, (g * 3) % 256, 255 - g, 255)

def picture(i):
    return ((i * 37 + 11) % 256, (i * 91 + 7) % 256, (i * 13 + 200) % 256)

def generated(i):
    return ((i * 53 + 3) % 256, (i * 29 + 101) % 256, (i * 71 + 17) % 256)

def moment(i): return ((i * 37 + 11) % 257 - 128) / 64.0
def draw(i): return ((i * 53 + 17) % 263 - 131) / 128.0
def latent(i): return ((i * 13 + 5) % 251 - 125) / 256.0
def call_draw(i, k): return ((i * 29 + k * 101 + 7) % 241 - 120) / 128.0


def main():
    out = sys.argv[1]
    opts = types.SimpleNamespace(img2img_fix_steps=False)
    setup = forge_function(r'modules\sd_samplers_common.py', 'setup_img2img_steps', {'opts': opts})
    binary = forge_function(r'modules\processing.py', 'create_binary_mask', {'Image': Image})
    overlay = forge_function(r'modules\processing.py', 'apply_overlay', {'Image': Image, 'uncrop': None})
    from k_diffusion import external, sampling
    from backend.nn.vae import DiagonalGaussianDistribution

    betas = torch.linspace(0.00085 ** 0.5, 0.012 ** 0.5, 1000, dtype=torch.float64) ** 2
    acp = torch.cumprod(1.0 - betas, dim=0)
    den = external.DiscreteEpsDDPMDenoiser(torch.nn.Identity(), acp.float(), quantize=False)
    reals, data = [], bytearray()
    flat, lens = [], []
    for k, (steps, d, hr) in enumerate(CASES):
        p = types.SimpleNamespace(steps=steps, denoising_strength=d)
        s, t_enc = setup(p, steps if hr else None)
        sig = sampling.get_sigmas_karras(s, float(den.sigma_min), float(den.sigma_max))
        tail = sig[s - t_enc - 1:].tolist()
        flat += tail
        lens.append(len(tail))
    reals += [float(n) for n in lens] + flat

    # The posterior over a 4 x 4 x 6 latent: moments, then the draws.
    hw = 24
    params = torch.tensor([moment(i) for i in range(8 * hw)], dtype=torch.float32).reshape(1, 8, 4, 6)
    eps = torch.tensor([draw(i) for i in range(4 * hw)], dtype=torch.float32).reshape(1, 4, 4, 6)
    real_randn = torch.randn
    torch.randn = lambda *a, **k: eps.clone()
    post = DiagonalGaussianDistribution(params).sample() * 0.13025
    torch.randn = real_randn
    reals += post.reshape(-1).tolist()

    # Inpaint's masks, as StableDiffusionProcessingImg2Img.init makes them.
    for tag, alpha in (('l', False), ('a', True)):
        img = Image.new('RGBA', (W, H))
        img.putdata([mask_rgba(i, alpha) for i in range(W * H)])
        image_mask = binary(img, round=True)
        raw = np.array(image_mask).reshape(-1)
        np_mask = np.array(image_mask)
        kernel_size = 2 * int(2.5 * BLUR + 0.5) + 1
        np_mask = cv2.GaussianBlur(np_mask, (kernel_size, 1), BLUR)
        image_mask = Image.fromarray(np_mask)
        np_mask = np.array(image_mask)
        np_mask = cv2.GaussianBlur(np_mask, (1, kernel_size), BLUR)
        image_mask = Image.fromarray(np_mask)
        blurred = np.array(image_mask).reshape(-1)
        np_mask = np.array(image_mask)
        np_mask = np.clip((np_mask.astype(np.float32)) * 2, 0, 255).astype(np.uint8)
        mask_for_overlay = Image.fromarray(np_mask)
        latmask = image_mask.convert('RGB').resize((W // 8, H // 8))
        latmask = np.moveaxis(np.array(latmask, dtype=np.float32), 2, 0) / 255
        latmask = np.around(latmask[0]).reshape(-1)
        reduced = np.array(image_mask.convert('RGB').resize((W // 8, H // 8)))[:, :, 0].reshape(-1)
        for a in (raw, blurred, np.array(mask_for_overlay).reshape(-1), reduced, latmask):
            data += bytes(int(v) for v in a)
        if not alpha:
            image = Image.new('RGB', (W, H))
            image.putdata([picture(i) for i in range(W * H)])
            image_masked = Image.new('RGBa', (image.width, image.height))
            image_masked.paste(image.convert("RGBA").convert("RGBa"), mask=ImageOps.invert(mask_for_overlay.convert('L')))
            over_image = image_masked.convert('RGBA')
            gen = Image.new('RGB', (W, H))
            gen.putdata([generated(i) for i in range(W * H)])
            result, _ = overlay(gen, None, over_image)
            composite = bytes(np.array(result).reshape(-1))

    # The hires pass's latent resize, 4 x 6 x 8 to 4 x 9 x 12.
    x = torch.tensor([latent(i) for i in range(4 * 48)], dtype=torch.float32).reshape(1, 4, 6, 8)
    up = torch.nn.functional.interpolate(x, size=(9, 12), mode='bilinear', antialias=False)
    reals += up.reshape(-1).tolist()
    data += composite

    # img2img over a stand-in model D(x)_i = x_i / 2 + x_(i+1) / 4, Euler over case 0's schedule,
    # masked then not: the mask lines of CFGDenoiser.forward
    # (sd_samplers_cfg_denoiser.py:178-181, 204-205) around the model, the
    # start from sample_img2img, and the final blend of
    # StableDiffusionProcessingImg2Img.sample (processing.py:1868).
    p = types.SimpleNamespace(steps=6, denoising_strength=0.5)
    s, t_enc = setup(p, None)
    sig = sampling.get_sigmas_karras(s, float(den.sigma_min), float(den.sigma_max))[s - t_enc - 1:]
    init = torch.tensor([latent(i) for i in range(96)], dtype=torch.float32).reshape(1, 4, 4, 6)
    noise = torch.tensor([draw(i) for i in range(96)], dtype=torch.float32).reshape(1, 4, 4, 6)
    nmask = torch.tensor([1.0 if (i % 24) % 6 < 3 else 0.0 for i in range(96)], dtype=torch.float32).reshape(1, 4, 4, 6)
    mask = 1.0 - nmask
    for masked in (True, False):
        calls = [0]
        def model(x, sigma, **kw):
            k = calls[0]
            calls[0] += 1
            if masked:
                fresh = torch.tensor([call_draw(i, k) for i in range(96)], dtype=torch.float32).reshape(1, 4, 4, 6)
                x = x * nmask + (fresh * sigma + init) * mask
            d = x * 0.5 + torch.roll(x.reshape(-1), -1).reshape(x.shape) * 0.25
            return d * nmask + init * mask if masked else d
        xi = noise * sig[0] + init
        res = sampling.sample_euler(model, xi, sig, disable=True)
        if masked:
            res = res * nmask + init * mask
        reals += res.reshape(-1).tolist()

    # Masked content on that init latent under that mask (processing.py:1834-1840),
    # the masks in devices.dtype on the GPU as Forge makes them: latent nothing,
    # then latent noise, whose draw is Forge's own create_random_tensors
    # (rng.ImageRNG, the GPU noise source) for seed SEED.
    dev = torch.device('cuda')
    shared = types.SimpleNamespace(device=dev, opts=types.SimpleNamespace(forge_try_reproduce='None', randn_source='GPU', eta_noise_seed_delta=0))
    rng = forge_module(r'modules\rng.py', {'shared': shared, 'devices': types.SimpleNamespace(device=dev, cpu=torch.device('cpu')), 'rng_philox': None})
    create_random_tensors = forge_function(r'modules\processing.py', 'create_random_tensors', {'rng': rng})
    il = init.to(dev)
    m16 = mask.to(dev).type(torch.float16)
    n16 = nmask.to(dev).type(torch.float16)
    reals += (il * m16).reshape(-1).tolist()
    reals += (il * m16 + create_random_tensors(il.shape[1:], [SEED]) * n16).reshape(-1).tolist()

    # Latent upscale's resize taken down, 4 x 9 x 12 to 4 x 6 x 8 (processing.py:1818).
    xd = torch.tensor([latent(i) for i in range(4 * 108)], dtype=torch.float32).reshape(1, 4, 9, 12)
    reals += torch.nn.functional.interpolate(xd, size=(6, 8), mode="bilinear").reshape(-1).tolist()
    with open(os.path.join(out, 'reals.ref'), 'wb') as f:
        f.write(struct.pack('<%dd' % len(reals), *reals))
    with open(os.path.join(out, 'bytes.ref'), 'wb') as f:
        f.write(bytes(data))
    print('%d schedules (%d sigmas); posterior %d; masks %dx%d; bilinear %d; %d reals, %d bytes' % (len(CASES), len(flat), post.numel(), W, H, up.numel(), len(reals), len(data)))
main()
