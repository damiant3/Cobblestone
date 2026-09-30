# flux-vae-oracle.py -- the reference for codex/test/apps/flux-vae: Diffusion
# Forge's own VAE (backend/nn/vae.py IntegratedAutoencoderKL) configured as its
# Flux engine loads ae.safetensors (16 latent channels, no quant_conv, no
# post_quant_conv) encodes vae-decode-oracle.py's 256x256 picture to a latent
# and decodes that latent back, in f32 on the GPU.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/flux-vae-oracle.py <ae.safetensors> <out dir>
#
# Writes into <out dir>:
#   flux-vae-latent.bin     16 x 32 x 32 f32 little-endian, channel-major: the
#                           posterior MEAN (deterministic), unscaled, the tensor
#                           the decoder receives
#   flux-vae-reference.bin  256 x 256 x 3 u8, row-major RGB: the f32 decode mapped
#                      by clamp((x + 1) / 2, 0, 1) * 255, rounded
#   vae-source.png, vae-reference.png, vae-f16.png, for the eye
# and prints the largest activation magnitude at each decoder stage, and the
# f16 decode's pixel difference from the f32 one, which bound what an f16
# activation path can be held to.
import os, sys
import numpy as np
import torch
from safetensors import safe_open

sys.path.insert(0, r'D:\AI\DiffusionForge\webui')
sys.path.insert(0, r'D:\AI\DiffusionForge\webui\packages_3rdparty')
from backend.nn.vae import IntegratedAutoencoderKL

ckpt, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

sd = {}
with safe_open(ckpt, framework='pt') as f:
    for k in f.keys():
        sd[k] = f.get_tensor(k).float()

vae = IntegratedAutoencoderKL(block_out_channels=(128, 256, 512, 512), layers_per_block=2, latent_channels=16,
                              down_block_types=('DownEncoderBlock2D',) * 4, up_block_types=('UpDecoderBlock2D',) * 4,
                              scaling_factor=0.3611, shift_factor=0.1159, use_quant_conv=False, use_post_quant_conv=False)
vae.load_state_dict(sd, strict=True)
vae = vae.cuda().eval()

# The picture: a diagonal colour gradient, three discs and a checker patch,
# every value a closed formula so the source can be rebuilt anywhere.
n = 256
y, x = np.mgrid[0:n, 0:n].astype(np.float32)
img = np.stack([x / (n - 1), y / (n - 1), 1.0 - (x + y) / (2 * (n - 1))], axis=-1)
for cx, cy, r, col in [(80, 90, 40, (0.9, 0.1, 0.1)), (170, 80, 30, (0.1, 0.8, 0.2)), (128, 180, 50, (0.95, 0.9, 0.2))]:
    m = (x - cx) ** 2 + (y - cy) ** 2 <= r * r
    img[m] = col
chk = ((x // 8 + y // 8) % 2).astype(np.float32)
patch = (x >= 176) & (x < 240) & (y >= 176) & (y < 240)
img[patch] = np.stack([chk, chk, chk], axis=-1)[patch]

def to_png(a, path):
    from PIL import Image
    Image.fromarray(a).save(path)

to_png((img * 255).round().astype(np.uint8), os.path.join(out, 'flux-vae-source.png'))

stages = []
def hook(name):
    def f(mod, inp, outp):
        stages.append((name, float(outp.detach().abs().max())))
    return f

with torch.no_grad():
    t = torch.from_numpy(img).permute(2, 0, 1)[None].cuda() * 2.0 - 1.0
    z = vae.encoder(t)
    mean = torch.chunk(z, 2, dim=1)[0]
    d = vae.decoder
    hooks = [d.conv_in.register_forward_hook(hook('conv_in'))]
    hooks += [d.mid.block_1.register_forward_hook(hook('mid.block_1')), d.mid.attn_1.register_forward_hook(hook('mid.attn_1')), d.mid.block_2.register_forward_hook(hook('mid.block_2'))]
    for i in reversed(range(4)):
        for j in range(3):
            hooks.append(d.up[i].block[j].register_forward_hook(hook('up.%d.block.%d' % (i, j))))
        if i != 0:
            hooks.append(d.up[i].upsample.register_forward_hook(hook('up.%d.upsample' % i)))
    hooks.append(d.conv_out.register_forward_hook(hook('conv_out')))
    x32 = vae.decode(mean)
    for h in hooks:
        h.remove()
    x16 = vae.half().decode(mean.half()).float()

def to_u8(xt):
    a = ((xt[0].permute(1, 2, 0).float().cpu().numpy() + 1.0) / 2.0).clip(0.0, 1.0)
    return (a * 255.0).round().astype(np.uint8)

ref, r16 = to_u8(x32), to_u8(x16)
mean.float().cpu().numpy().astype('<f4').tofile(os.path.join(out, 'flux-vae-latent.bin'))
ref.tofile(os.path.join(out, 'flux-vae-reference.bin'))
to_png(ref, os.path.join(out, 'flux-vae-reference.png'))
to_png(r16, os.path.join(out, 'flux-vae-f16.png'))

diff = np.abs(ref.astype(np.int32) - r16.astype(np.int32))
src = (img * 255).round().astype(np.int32)
print('latent %s mean %.4f std %.4f absmax %.4f' % (tuple(mean.shape), float(mean.mean()), float(mean.std()), float(mean.abs().max())))
for name, v in stages:
    print('absmax %-16s %.1f' % (name, v))
print('f16 decode vs f32: max %d, mean %.3f, nan %d' % (diff.max(), diff.mean(), int(torch.isnan(x16).sum())))
print('f32 decode vs source: mean abs %.2f' % np.abs(ref.astype(np.int32) - src).mean())
