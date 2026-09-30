# flux-lora-oracle.py -- the reference for codex/test/apps/diffusion-flux-lora: a kohya
# Flux LoRA merged into Flux schnell's float8_e4m3fn weights by Diffusion Forge's own
# code, as its default "Diffusion in Low Bits: Automatic" runs it (not online):
# model_lora_keys_unet over the checkpoint's keys under "diffusion_model."
# (packages_3rdparty/comfyui_lora_collection/lora.py:267), load_lora, and
# merge_lora_to_weight (backend/patcher/lora.py:76) with the patch tuple
# LoraLoader.refresh hands it, (strength, patch, 1.0, None, None), in float32 on
# CUDA, cast back to e4m3.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/flux-lora-oracle.py <out dir>
#
# Writes <out dir>/flux-lora.bin, per target in TARGETS order, le64 each: the
# element count, the bytes the merge changed, the sum over i of merged[i] times
# (i mod 251 + 1), a zero's sign ignored (0x80 read as 0x00: the guest's rank dot runs
# in f64 and Forge's in f32, which can leave the sign of a zero sum different; 1 byte of
# 37,748,736 in double_blocks.7.txt_mlp.2 and of 47,185,920 in single_blocks.37.linear2,
# measured 2026-09-29); then SAMPLES positions (j * STRIDE + 7) mod n as le32 and
# the bytes there before and after.

import os
import struct
import sys
import types

import torch
from safetensors import safe_open
from safetensors.torch import load_file

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path[:0] = [WEBUI, os.path.join(WEBUI, 'packages_3rdparty'), os.path.join(WEBUI, 'repositories', 'huggingface_guess')]
CKPT = os.path.join(WEBUI, 'models', 'Stable-diffusion', 'flux1-schnell-fp8-e4m3fn.safetensors')
LORA = os.path.join(WEBUI, 'models', 'Lora', 'FLUX.1-dev-lora-Dark-Fantasy.safetensors')
STRENGTH = 0.8
TARGETS = ['double_blocks.0.img_attn.qkv.weight', 'double_blocks.7.txt_mlp.2.weight', 'double_blocks.18.img_mod.lin.weight',
           'single_blocks.0.linear1.weight', 'single_blocks.37.linear2.weight', 'single_blocks.9.modulation.lin.weight']
SAMPLES, STRIDE = 1024, 7919


def main():
    from backend.patcher.lora import model_lora_keys_unet, load_lora, merge_lora_to_weight
    dev = torch.device('cuda')
    torch.backends.cuda.matmul.allow_tf32 = False
    with safe_open(CKPT, 'pt') as f:
        names = list(f.keys())
        sd = {'diffusion_model.' + k: None for k in names}
        model = types.SimpleNamespace(state_dict=lambda: sd, config=types.SimpleNamespace(huggingface_repo='black-forest-labs/FLUX.1-schnell'),
                                      diffusion_model=types.SimpleNamespace(config={'depth': 19, 'depth_single_blocks': 38, 'hidden_size': 3072}))
        key_map = model_lora_keys_unet(model, {})
        patches, _ = load_lora(load_file(LORA), key_map)
        out = bytearray()
        for t in TARGETS:
            w = f.get_tensor(t).to(dev)
            p = patches['diffusion_model.' + t]
            merged = merge_lora_to_weight([(STRENGTH, p, 1.0, None, None)], w, 'diffusion_model.' + t, torch.float32)
            a = w.view(torch.uint8).reshape(-1).cpu()
            b = merged.view(torch.uint8).reshape(-1).cpu()
            na = torch.where(a == 128, torch.zeros_like(a), a)
            nb = torch.where(b == 128, torch.zeros_like(b), b)
            n = a.numel()
            idx = torch.arange(n, dtype=torch.int64)
            check = int((nb.to(torch.int64) * (idx % 251 + 1)).sum())
            changed = int((na != nb).sum())
            out += struct.pack('<3q', n, changed, check)
            pos = [(j * STRIDE + 7) % n for j in range(SAMPLES)]
            out += struct.pack('<%di' % SAMPLES, *pos)
            out += bytes(a[pos].tolist()) + bytes(b[pos].tolist())
            print(t, tuple(w.shape), 'changed', changed, 'of', n)
    with open(os.path.join(sys.argv[1], 'flux-lora.bin'), 'wb') as fo:
        fo.write(bytes(out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
