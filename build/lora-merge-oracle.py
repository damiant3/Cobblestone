# lora-merge-oracle.py -- LoRA weights merged by Diffusion Forge's own patcher,
# written out as the Codex chapter the guest's LoRA merge is graded against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/lora-merge-oracle.py <checkpoint> <lora-dir> <out.codex>
#
# Each LoRA file is parsed by Forge's load_lora (packages_3rdparty/
# comfyui_lora_collection/lora.py:32) over the kohya key map Forge builds for
# a UNet weight (lora.py:273: "lora_unet_" + the name without ".weight", dots
# to underscores), and every target is merged by Forge's merge_lora_to_weight
# (backend/patcher/lora.py:76) with the patch tuple LoraLoader.refresh hands
# it: (strength, patch, 1.0, None, None), in float32 on CUDA, TF32 off (the
# PyTorch default, which Forge does not change), then cast back to the
# checkpoint's f16. The key map is Forge's model_lora_keys_unet (lora.py:267)
# over the checkpoint's UNet names and the config Forge's detect_unet_config
# gives their shapes, so a LoRA in either kohya naming (LDM or diffusers)
# patches the same weight. For each target the chapter records SAMPLES positions
# (j * STRIDE + OFFSET) mod numel, the f16 bits of the checkpoint weight
# there, and the f16 bits Forge's merge leaves there.
import os, sys
import torch

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path.insert(0, WEBUI)
sys.path.insert(0, os.path.join(WEBUI, 'repositories', 'huggingface_guess'))
sys.path.insert(0, os.path.join(WEBUI, 'packages_3rdparty'))

LORAS = [('pixel-art-xl.safetensors', 0.8), ('papercut-xl.safetensors', 0.5)]
TARGETS = [
    'input_blocks.4.1.proj_in.weight',
    'input_blocks.4.1.transformer_blocks.0.attn1.to_q.weight',
    'input_blocks.7.1.transformer_blocks.3.ff.net.0.proj.weight',
    'middle_block.1.transformer_blocks.9.attn2.to_k.weight',
    'output_blocks.2.1.transformer_blocks.9.ff.net.2.weight',
    'output_blocks.5.1.proj_out.weight',
]
SAMPLES, STRIDE, OFFSET = 256, 7919, 13
PREFIX = 'model.diffusion_model.'

# Text encoders. Forge's SDXL loader renames CLIP-L's prefix to clip_l.transformer.
# and converts OpenCLIP-G to Hugging Face names with transformers_convert
# (comfyui_lora_collection/utils.py:67), which splits attn.in_proj into q, k and
# v row slices; the map below is discovered by running that function on marker
# tensors, and a target is a (checkpoint tensor, first row, rows) slice.
TE_LORAS = [('myststyle-slime-universe-xl_epoch_10.safetensors', 0.7), ('9HNHMWJZDSGD8WE7FFJCRVB8M0.safetensors', 0.6)]
TE_L_FROM, TE_L_TO = 'conditioner.embedders.0.transformer.text_model.', 'clip_l.transformer.text_model.'
TE_G_FROM, TE_G_TO = 'conditioner.embedders.1.model.', 'clip_g.transformer.text_model.'
TE_TARGETS = [
    'clip_l.transformer.text_model.encoder.layers.5.self_attn.q_proj.weight',
    'clip_l.transformer.text_model.encoder.layers.10.mlp.fc2.weight',
    'clip_g.transformer.text_model.encoder.layers.20.self_attn.q_proj.weight',
    'clip_g.transformer.text_model.encoder.layers.20.self_attn.k_proj.weight',
    'clip_g.transformer.text_model.encoder.layers.20.self_attn.v_proj.weight',
    'clip_g.transformer.text_model.encoder.layers.31.self_attn.out_proj.weight',
    'clip_g.transformer.text_model.encoder.layers.0.mlp.fc1.weight',
]

# The other kinds, which no installed file uses: a synthetic LoRA written to
# codex/test/gpu-files/lora-kinds.safetensors, one module per kind, every value
# ((i * 37 + 11) mod 17 - 8) / 32 of its flat index (exact in f16 and bf16).
KINDS_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'codex', 'test', 'gpu-files', 'lora-kinds.safetensors')
KINDS_STRENGTH = 0.75
KIND_TARGETS = [
    ('input_blocks.4.1.transformer_blocks.0.attn1.to_k.weight', 'loha'),
    ('input_blocks.4.1.transformer_blocks.0.attn1.to_v.weight', 'lokr-w2-factored'),
    ('input_blocks.4.1.transformer_blocks.0.attn2.to_q.weight', 'lokr-w1-factored'),
    ('input_blocks.4.1.transformer_blocks.0.norm1.weight', 'diff'),
    ('input_blocks.4.1.transformer_blocks.0.attn2.to_out.0.weight', 'glora'),
    ('input_blocks.4.1.transformer_blocks.0.attn2.to_out.0.bias', 'diff_b'),
    ('input_blocks.4.1.transformer_blocks.0.norm2.weight', 'w_norm'),
    ('input_blocks.4.1.transformer_blocks.0.norm2.bias', 'b_norm'),
    ('input_blocks.4.1.transformer_blocks.0.attn1.to_out.0.weight', 'dora'),
    ('input_blocks.1.0.in_layers.2.weight', 'locon-mid'),
    ('input_blocks.1.0.out_layers.3.weight', 'loha-tucker'),
    ('input_blocks.2.0.in_layers.2.weight', 'lokr-t2'),
]
# A text-encoder bias slice: diff_b on OpenCLIP-G's k projection, which Forge
# holds as rows 1280 .. 2560 of the fused attn.in_proj_bias.
KIND_TE_MODULE = 'lora_te2_text_model_encoder_layers_20_self_attn_k_proj'
KIND_TE_TARGET = 'clip_g.transformer.text_model.encoder.layers.20.self_attn.k_proj.bias'

def kind_values(shape, dtype, seed):
    n = 1
    for s in shape: n *= s
    v = torch.tensor([(((i + seed) * 37 + 11) % 17 - 8) / 32.0 for i in range(n)], dtype=torch.float32)
    return v.reshape(shape).to(dtype)

def kinds_file():
    sd = {}
    def mod(t): return 'lora_unet_' + t[:-len('.weight')].replace('.', '_')
    m = mod(KIND_TARGETS[0][0])
    sd[m + '.hada_w1_a'] = kind_values([640, 4], torch.bfloat16, 1)
    sd[m + '.hada_w1_b'] = kind_values([4, 640], torch.bfloat16, 2)
    sd[m + '.hada_w2_a'] = kind_values([640, 4], torch.bfloat16, 3)
    sd[m + '.hada_w2_b'] = kind_values([4, 640], torch.bfloat16, 4)
    sd[m + '.alpha'] = torch.tensor(2.0, dtype=torch.bfloat16)
    m = mod(KIND_TARGETS[1][0])
    sd[m + '.lokr_w1'] = kind_values([8, 8], torch.float16, 5)
    sd[m + '.lokr_w2_a'] = kind_values([80, 4], torch.float16, 6)
    sd[m + '.lokr_w2_b'] = kind_values([4, 80], torch.float16, 7)
    sd[m + '.alpha'] = torch.tensor(8.0, dtype=torch.float16)
    m = mod(KIND_TARGETS[2][0])
    sd[m + '.lokr_w1_a'] = kind_values([8, 2], torch.float16, 8)
    sd[m + '.lokr_w1_b'] = kind_values([2, 8], torch.float16, 9)
    sd[m + '.lokr_w2'] = kind_values([80, 80], torch.float16, 10)
    m = mod(KIND_TARGETS[3][0])
    sd[m + '.diff'] = kind_values([640], torch.float32, 11)
    m = mod(KIND_TARGETS[4][0])
    sd[m + '.a1.weight'] = kind_values([4, 640], torch.bfloat16, 12)
    sd[m + '.a2.weight'] = kind_values([640, 4], torch.bfloat16, 13)
    sd[m + '.b1.weight'] = kind_values([4, 640], torch.bfloat16, 14)
    sd[m + '.b2.weight'] = kind_values([640, 4], torch.bfloat16, 15)
    sd[m + '.alpha'] = torch.tensor(1.0, dtype=torch.bfloat16)
    sd[m + '.diff_b'] = kind_values([640], torch.float16, 16)
    m = mod(KIND_TARGETS[6][0])
    sd[m + '.w_norm'] = kind_values([640], torch.bfloat16, 17)
    sd[m + '.b_norm'] = kind_values([640], torch.float32, 18)
    sd[KIND_TE_MODULE + '.diff_b'] = kind_values([1280], torch.float16, 19)
    m = mod(KIND_TARGETS[8][0])
    sd[m + '.lora_up.weight'] = kind_values([640, 4], torch.bfloat16, 20)
    sd[m + '.lora_down.weight'] = kind_values([4, 640], torch.bfloat16, 21)
    sd[m + '.alpha'] = torch.tensor(2.0, dtype=torch.bfloat16)
    sd[m + '.dora_scale'] = (kind_values([1, 640], torch.float32, 22) + 1.0).to(torch.bfloat16)
    m = mod(KIND_TARGETS[9][0])
    sd[m + '.lora_up.weight'] = kind_values([320, 4, 1, 1], torch.float16, 23)
    sd[m + '.lora_down.weight'] = kind_values([4, 320, 1, 1], torch.float16, 24)
    sd[m + '.lora_mid.weight'] = kind_values([4, 4, 3, 3], torch.float16, 25)
    sd[m + '.alpha'] = torch.tensor(4.0, dtype=torch.float16)
    m = mod(KIND_TARGETS[10][0])
    for h, seed in (('1', 26), ('2', 29)):
        sd[m + '.hada_w' + h + '_a'] = kind_values([4, 320], torch.float16, seed)
        sd[m + '.hada_w' + h + '_b'] = kind_values([4, 320], torch.float16, seed + 1)
        sd[m + '.hada_t' + h] = kind_values([4, 4, 3, 3], torch.float16, seed + 2)
    sd[m + '.alpha'] = torch.tensor(4.0, dtype=torch.float16)
    m = mod(KIND_TARGETS[11][0])
    sd[m + '.lokr_w1'] = kind_values([4, 8], torch.float16, 32)
    sd[m + '.lokr_w2_a'] = kind_values([4, 80], torch.float16, 33)
    sd[m + '.lokr_w2_b'] = kind_values([4, 40], torch.float16, 34)
    sd[m + '.lokr_t2'] = kind_values([4, 4, 3, 3], torch.float16, 35)
    sd[m + '.alpha'] = torch.tensor(2.0, dtype=torch.float16)
    return sd

# SD1.5: realisticVision beside the SDXL checkpoint, and a synthetic LoRA with
# an LDM-named attention module, a diffusers-named module on a 1x1 conv
# proj_in (4-D factors), and a lora_te_ module on CLIP-L (cond_stage_model.).
SD15_CKPT = 'realisticVisionV60B1_v20Novae.safetensors'
SD15_FILE = os.path.join(os.path.dirname(KINDS_FILE), 'lora-sd15.safetensors')
SD15_STRENGTH = 0.9
SD15_L_FROM = 'cond_stage_model.transformer.text_model.'
SD15_MODULES = [
    ('lora_unet_input_blocks_1_1_transformer_blocks_0_attn1_to_q', [320, 4], [4, 320], 4.0),
    ('lora_unet_up_blocks_3_attentions_2_proj_in', [320, 4, 1, 1], [4, 320, 1, 1], 8.0),
    ('lora_te_text_model_encoder_layers_3_self_attn_v_proj', [768, 4], [4, 768], 2.0),
]

def sd15_section(ckpt_dir, load_lora, merge_lora_to_weight, model_lora_keys_unet, model_lora_keys_clip, detection, SimpleNamespace, safe_open, load_file, save_file):
    path = os.path.join(ckpt_dir, SD15_CKPT)
    with safe_open(path, 'pt', device='cpu') as f:
        keys = [k for k in f.keys() if k.startswith(PREFIX)]
        cfg = detection.detect_unet_config({k: torch.empty(f.get_slice(k).get_shape(), device='meta') for k in keys}, PREFIX)
        tkeys = [k for k in f.keys() if k.startswith(SD15_L_FROM)]
    names = {'diffusion_model.' + k[len(PREFIX):]: None for k in keys}
    model = SimpleNamespace(state_dict=lambda: names, diffusion_model=SimpleNamespace(config=cfg),
                            config=SimpleNamespace(huggingface_repo='runwayml/stable-diffusion-v1-5'))
    got = model_lora_keys_unet(model, {})
    umap = got[1] if isinstance(got, tuple) else got
    tnames = {'clip_l.transformer.text_model.' + k[len(SD15_L_FROM):]: k for k in tkeys}
    got = model_lora_keys_clip(SimpleNamespace(state_dict=lambda: {k: None for k in tnames}), {})
    tmap = got[1] if isinstance(got, tuple) else got
    sd = {}
    for i, (m, up, down, alpha) in enumerate(SD15_MODULES):
        sd[m + '.lora_up.weight'] = kind_values(up, torch.float16, 30 + 2 * i)
        sd[m + '.lora_down.weight'] = kind_values(down, torch.float16, 31 + 2 * i)
        sd[m + '.alpha'] = torch.tensor(alpha, dtype=torch.float16)
    save_file(sd, SD15_FILE)
    to_load = {m: umap[m] if m in umap else tmap[m] for m, _, _, _ in SD15_MODULES}
    patch, _ = load_lora(load_file(SD15_FILE), to_load)
    rows = []
    with safe_open(path, 'pt', device='cpu') as f:
        for m, _, _, _ in SD15_MODULES:
            key = to_load[m]
            full = PREFIX + key[len('diffusion_model.'):] if key.startswith('diffusion_model.') else tnames[key]
            base = f.get_tensor(full)
            if base.dtype != torch.float16:
                raise SystemExit(full + ' is ' + str(base.dtype) + ', not f16')
            merged = merge_lora_to_weight([(SD15_STRENGTH, patch[key], 1.0, None, None)], base.cuda(), key, computation_dtype=torch.float32).cpu()
            n = base.numel()
            pos = [(j * STRIDE + OFFSET) % n for j in range(SAMPLES)]
            fb, fm = base.flatten().view(torch.int16), merged.flatten().view(torch.int16)
            rows.append((full, base.shape[0], n // base.shape[0], [int(fb[p]) & 0xFFFF for p in pos], [int(fm[p]) & 0xFFFF for p in pos]))
    census = sorted((k, v[len('diffusion_model.'):]) for k, v in umap.items()
                    if k.startswith('lora_unet_') and v.endswith('.weight')
                    and k != 'lora_unet_' + v[len('diffusion_model.'):-len('.weight')].replace('.', '_'))
    out = ['', ' SD1.5 (' + SD15_CKPT + '): codex/test/gpu-files/lora-sd15.safetensors merged at one',
           " strength, and Forge's diffusers-form kohya key map for that UNet.", '',
           '  lora-ref-sd15-ckpt : Text = "' + SD15_CKPT + '"', '',
           '  lora-ref-sd15-file : Text = "lora-sd15.safetensors"', '',
           '  lora-ref-sd15-strength : Real = ' + repr(SD15_STRENGTH)]
    for i, r in enumerate(rows):
        out += ['', '  lora-ref-sd15-' + str(i) + ' : LoraRefSlice',
                '  lora-ref-sd15-' + str(i) + ' = LoraRefSlice { lrs-tensor = "' + r[0] + '", lrs-row0 = 0, lrs-rows = ' + str(r[1]) + ', lrs-cols = ' + str(r[2])
                + ', lrs-base = ' + codex_list(r[3]) + ', lrs-merged = ' + codex_list(r[4]) + ' }']
    out += ['', '  lora-ref-sd15-targets : List LoraRefSlice',
            '  lora-ref-sd15-targets = [' + ', '.join('lora-ref-sd15-' + str(i) for i in range(len(rows))) + ']', '',
            '  lora-ref-sd15-kohya : List Text',
            '  lora-ref-sd15-kohya = [' + ', '.join('"' + k + '"' for k, _ in census) + ']', '',
            '  lora-ref-sd15-ldm : List Text',
            '  lora-ref-sd15-ldm = [' + ', '.join('"' + v + '"' for _, v in census) + ']']
    moved = sum(sum(1 for a, b in zip(r[3], r[4]) if a != b) for r in rows)
    return out, 'sd15 targets %d moved %d sd15 map %d' % (len(rows), moved, len(census))

def te_sources(f):
    """Forge's HF name -> (checkpoint tensor, first row, rows) for every text-encoder weight."""
    from comfyui_lora_collection.utils import transformers_convert
    src = {}
    for k in f.keys():
        if k.startswith(TE_L_FROM) and k.endswith(('.weight', '.bias')):
            n = f.get_slice(k).get_shape()[0]
            src[TE_L_TO + k[len(TE_L_FROM):]] = (k, 0, n)
    g = [k for k in f.keys() if k.startswith(TE_G_FROM + 'transformer.resblocks.')]
    marks = {}
    for i, k in enumerate(g):
        n = f.get_slice(k).get_shape()[0]
        marks[k] = torch.arange(n, dtype=torch.float64) + i * 1e6
    out = transformers_convert(dict(marks), TE_G_FROM, TE_G_TO, 32)
    for k, v in out.items():
        if k.startswith(TE_G_TO) and k.endswith(('.weight', '.bias')) and v.dim() == 1:
            first = int(v[0].item())
            src[k] = (g[first // 1000000], first % 1000000, v.shape[0])
    return src

def codex_list(xs):
    return '[' + ', '.join(str(x) for x in xs) + ']'

def main():
    ckpt, lora_dir, out = sys.argv[1], sys.argv[2], sys.argv[3]
    sys.argv = sys.argv[:1]
    from types import SimpleNamespace
    from safetensors import safe_open
    from safetensors.torch import load_file
    from huggingface_guess import detection
    from comfyui_lora_collection.lora import load_lora, model_lora_keys_unet
    from backend.patcher.lora import merge_lora_to_weight

    # Forge's key map over the checkpoint's own UNet names and the config
    # Forge detects from their shapes: both the LDM kohya names and the
    # diffusers kohya names (lora.py:273 and :283).
    with safe_open(ckpt, 'pt', device='cpu') as f:
        keys = [k for k in f.keys() if k.startswith(PREFIX)]
        cfg = detection.detect_unet_config({k: torch.empty(f.get_slice(k).get_shape(), device='meta') for k in keys}, PREFIX)
    names = {'diffusion_model.' + k[len(PREFIX):]: None for k in keys}
    model = SimpleNamespace(state_dict=lambda: names, diffusion_model=SimpleNamespace(config=cfg),
                            config=SimpleNamespace(huggingface_repo='stabilityai/stable-diffusion-xl-base-1.0'))
    got = model_lora_keys_unet(model, {})
    key_map = got[1] if isinstance(got, tuple) else got

    translated = sorted((k, v[len('diffusion_model.'):]) for k, v in key_map.items()
                        if k.startswith('lora_unet_') and v.endswith('.weight')
                        and k != 'lora_unet_' + v[len('diffusion_model.'):-len('.weight')].replace('.', '_'))
    patches = {}
    for name, strength in LORAS:
        sd = load_file(os.path.join(lora_dir, name))
        modules = sorted({k[:-len('.lora_down.weight')] for k in sd if k.endswith('.lora_down.weight')})
        unmapped = [m for m in modules if m not in key_map]
        if unmapped:
            raise SystemExit(name + ': Forge maps no weight for ' + unmapped[0])
        patch_dict, _ = load_lora(sd, {m: key_map[m] for m in modules})
        for key, patch in patch_dict.items():
            patches.setdefault(key, []).append((strength, patch, 1.0, None, None))

    rows = []
    with safe_open(ckpt, 'pt', device='cpu') as f:
        for t in TARGETS:
            key = 'diffusion_model.' + t
            if key not in patches or len(patches[key]) != len(LORAS):
                raise SystemExit('not every LoRA patches ' + t)
            base = f.get_tensor(PREFIX + t)
            if base.dtype != torch.float16:
                raise SystemExit(t + ' is ' + str(base.dtype) + ', not f16')
            merged = merge_lora_to_weight(patches[key], base.cuda(), key, computation_dtype=torch.float32).cpu()
            flat_b = base.flatten().view(torch.int16)
            flat_m = merged.flatten().view(torch.int16)
            n = flat_b.numel()
            pos = [(j * STRIDE + OFFSET) % n for j in range(SAMPLES)]
            rows.append((t, list(base.shape), [int(flat_b[p]) & 0xFFFF for p in pos], [int(flat_m[p]) & 0xFFFF for p in pos]))

    from comfyui_lora_collection.lora import model_lora_keys_clip
    with safe_open(ckpt, 'pt', device='cpu') as f:
        te_src = te_sources(f)
    te_names = {k: None for k in te_src}
    got = model_lora_keys_clip(SimpleNamespace(state_dict=lambda: te_names), {})
    te_map = got[1] if isinstance(got, tuple) else got
    te_keys = sorted((k, te_src[v]) for k, v in te_map.items() if v in te_src and not k.endswith('text_projection'))
    te_patches = {}
    for name, strength in TE_LORAS:
        sd = load_file(os.path.join(lora_dir, name))
        modules = sorted({k[:-len('.lora_down.weight')] for k in sd if k.endswith('.lora_down.weight') and k.startswith('lora_te')})
        unmapped = [m for m in modules if m not in te_map]
        if unmapped:
            raise SystemExit(name + ': Forge maps no text-encoder weight for ' + unmapped[0])
        patch_dict, _ = load_lora(sd, {m: te_map[m] for m in modules})
        for key, patch in patch_dict.items():
            te_patches.setdefault(key, []).append((strength, patch, 1.0, None, None))
    te_rows = []
    with safe_open(ckpt, 'pt', device='cpu') as f:
        for t in TE_TARGETS:
            if t not in te_patches or len(te_patches[t]) != len(TE_LORAS):
                raise SystemExit('not every text-encoder LoRA patches ' + t)
            ck, r0, n = te_src[t]
            base = f.get_tensor(ck)[r0:r0 + n].contiguous()
            if base.dtype != torch.float16:
                raise SystemExit(ck + ' is ' + str(base.dtype) + ', not f16')
            merged = merge_lora_to_weight(te_patches[t], base.cuda(), t, computation_dtype=torch.float32).cpu()
            flat_b = base.flatten().view(torch.int16)
            flat_m = merged.flatten().view(torch.int16)
            count = flat_b.numel()
            pos = [(j * STRIDE + OFFSET) % count for j in range(SAMPLES)]
            te_rows.append((ck, r0, n, count // n, [int(flat_b[p]) & 0xFFFF for p in pos], [int(flat_m[p]) & 0xFFFF for p in pos]))

    from safetensors.torch import save_file
    ksd = kinds_file()
    save_file(ksd, KINDS_FILE)
    kpatch, _ = load_lora(load_file(KINDS_FILE), {m: key_map[m] if m in key_map else te_map[m] for m in {k.split('.', 1)[0] for k in ksd}})
    kind_rows = []
    with safe_open(ckpt, 'pt', device='cpu') as f:
        for t, kind in KIND_TARGETS:
            key = 'diffusion_model.' + t
            if key not in kpatch:
                raise SystemExit('the kinds file does not patch ' + t)
            base = f.get_tensor(PREFIX + t)
            if base.dtype != torch.float16:
                raise SystemExit(t + ' is ' + str(base.dtype) + ', not f16')
            merged = merge_lora_to_weight([(KINDS_STRENGTH, kpatch[key], 1.0, None, None)], base.cuda(), key, computation_dtype=torch.float32).cpu()
            flat_b = base.flatten().view(torch.int16)
            flat_m = merged.flatten().view(torch.int16)
            n = flat_b.numel()
            pos = [(j * STRIDE + OFFSET) % n for j in range(SAMPLES)]
            nrows = list(base.shape)[0]
            kind_rows.append((t, nrows, n // nrows, [int(flat_b[p]) & 0xFFFF for p in pos], [int(flat_m[p]) & 0xFFFF for p in pos], kind))
        # DoRA at strength exactly 1, where Forge replaces the weight (lora.py:67-71).
        t = 'input_blocks.4.1.transformer_blocks.0.attn1.to_out.0.weight'
        base = f.get_tensor(PREFIX + t)
        merged = merge_lora_to_weight([(1.0, kpatch['diffusion_model.' + t], 1.0, None, None)], base.cuda(), t, computation_dtype=torch.float32).cpu()
        n = base.numel()
        pos = [(j * STRIDE + OFFSET) % n for j in range(SAMPLES)]
        dora_full = (t, base.shape[0], n // base.shape[0], [int(base.flatten().view(torch.int16)[p]) & 0xFFFF for p in pos], [int(merged.flatten().view(torch.int16)[p]) & 0xFFFF for p in pos])
        ck, r0, n = te_src[KIND_TE_TARGET]
        base = f.get_tensor(ck)[r0:r0 + n].contiguous()
        merged = merge_lora_to_weight([(KINDS_STRENGTH, kpatch[KIND_TE_TARGET], 1.0, None, None)], base.cuda(), KIND_TE_TARGET, computation_dtype=torch.float32).cpu()
        pos = [(j * STRIDE + OFFSET) % n for j in range(SAMPLES)]
        kind_te = (ck, r0, n, 1, [int(base.view(torch.int16)[p]) & 0xFFFF for p in pos], [int(merged.view(torch.int16)[p]) & 0xFFFF for p in pos])

    lines = ['Chapter: LoraReference', '',
             ' Generated by build/lora-merge-oracle.py from ' + os.path.basename(ckpt) + ': LoRA weights merged',
             ' by Diffusion Forge\'s own patcher in float32 and cast to f16. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Reference', '',
             '  LoraRefTarget = record {', '    lrt-name : Text,', '    lrt-rows : Integer,', '    lrt-cols : Integer,',
             '    lrt-base : List Integer,', '    lrt-merged : List Integer', '  }', '',
             '  lora-ref-files : List Text',
             '  lora-ref-files = [' + ', '.join('"' + n + '"' for n, _ in LORAS) + ']', '',
             '  lora-ref-strengths : List Real',
             '  lora-ref-strengths = [' + ', '.join(repr(s) for _, s in LORAS) + ']', '',
             '  lora-ref-samples : Integer = ' + str(SAMPLES), '',
             '  lora-ref-stride : Integer = ' + str(STRIDE), '',
             '  lora-ref-offset : Integer = ' + str(OFFSET)]
    for i, r in enumerate(rows):
        lines += ['', '  lora-ref-' + str(i) + ' : LoraRefTarget',
                  '  lora-ref-' + str(i) + ' = LoraRefTarget { lrt-name = "' + r[0] + '", lrt-rows = ' + str(r[1][0]) + ', lrt-cols = ' + str(r[1][1])
                  + ', lrt-base = ' + codex_list(r[2]) + ', lrt-merged = ' + codex_list(r[3]) + ' }']
    lines += ['', '  lora-ref-targets : List LoraRefTarget',
              '  lora-ref-targets = [' + ', '.join('lora-ref-' + str(i) for i in range(len(rows))) + ']']
    lines += ['', " Forge's whole diffusers-form kohya key map for this UNet: every lora_unet_",
              ' name that is not the LDM form of its weight, with the weight it names.', '',
              '  lora-ref-kohya : List Text',
              '  lora-ref-kohya = [' + ', '.join('"' + m + '"' for m, _ in translated) + ']', '',
              '  lora-ref-ldm : List Text',
              '  lora-ref-ldm = [' + ', '.join('"' + l + '"' for _, l in translated) + ']']
    lines += ['', ' The text encoders: a target is rows lrs-row0 .. lrs-row0 + lrs-rows of a',
              ' checkpoint tensor, sampled within that slice.', '',
              '  LoraRefSlice = record {', '    lrs-tensor : Text,', '    lrs-row0 : Integer,', '    lrs-rows : Integer,',
              '    lrs-cols : Integer,', '    lrs-base : List Integer,', '    lrs-merged : List Integer', '  }', '',
              '  lora-ref-te-files : List Text',
              '  lora-ref-te-files = [' + ', '.join('"' + n + '"' for n, _ in TE_LORAS) + ']', '',
              '  lora-ref-te-strengths : List Real',
              '  lora-ref-te-strengths = [' + ', '.join(repr(s) for _, s in TE_LORAS) + ']']
    for i, r in enumerate(te_rows):
        lines += ['', '  lora-ref-te-' + str(i) + ' : LoraRefSlice',
                  '  lora-ref-te-' + str(i) + ' = LoraRefSlice { lrs-tensor = "' + r[0] + '", lrs-row0 = ' + str(r[1]) + ', lrs-rows = ' + str(r[2])
                  + ', lrs-cols = ' + str(r[3]) + ', lrs-base = ' + codex_list(r[4]) + ', lrs-merged = ' + codex_list(r[5]) + ' }']
    lines += ['', '  lora-ref-te-targets : List LoraRefSlice',
              '  lora-ref-te-targets = [' + ', '.join('lora-ref-te-' + str(i) for i in range(len(te_rows))) + ']']
    lines += ['', " Forge's text-encoder key map for this checkpoint (every form), each key with",
              ' the checkpoint tensor and first row of the weight it names.', '',
              '  lora-ref-te-kohya : List Text',
              '  lora-ref-te-kohya = [' + ', '.join('"' + k + '"' for k, _ in te_keys) + ']', '',
              '  lora-ref-te-tensor : List Text',
              '  lora-ref-te-tensor = [' + ', '.join('"' + s[0] + '"' for _, s in te_keys) + ']', '',
              '  lora-ref-te-row0 : List Integer',
              '  lora-ref-te-row0 = ' + codex_list(s[1] for _, s in te_keys)]
    lines += ['', ' The other kinds: codex/test/gpu-files/lora-kinds.safetensors (LoHa, LoKr with',
              ' either factor low-rank, a full diff, GLoRA, the bias patches, DoRA on a LoRA, LoCon',
              ' with lora_mid, LoHa with hada_t1 / t2 and LoKr with lokr_t2 on 3x3 convs) merged at',
              ' one strength.', '',
              '  lora-ref-kinds-file : Text = "lora-kinds.safetensors"', '',
              '  lora-ref-kinds-strength : Real = ' + repr(KINDS_STRENGTH), '',
              '  lora-ref-kind-names : List Text',
              '  lora-ref-kind-names = [' + ', '.join('"' + r[5] + '"' for r in kind_rows) + ']']
    for i, r in enumerate(kind_rows):
        lines += ['', '  lora-ref-kind-' + str(i) + ' : LoraRefTarget',
                  '  lora-ref-kind-' + str(i) + ' = LoraRefTarget { lrt-name = "' + r[0] + '", lrt-rows = ' + str(r[1]) + ', lrt-cols = ' + str(r[2])
                  + ', lrt-base = ' + codex_list(r[3]) + ', lrt-merged = ' + codex_list(r[4]) + ' }']
    lines += ['', '  lora-ref-kind-targets : List LoraRefTarget',
              '  lora-ref-kind-targets = [' + ', '.join('lora-ref-kind-' + str(i) for i in range(len(kind_rows))) + ']']
    lines += ['', '  lora-ref-dora-full : LoraRefTarget',
              '  lora-ref-dora-full = LoraRefTarget { lrt-name = "' + dora_full[0] + '", lrt-rows = ' + str(dora_full[1]) + ', lrt-cols = ' + str(dora_full[2])
              + ', lrt-base = ' + codex_list(dora_full[3]) + ', lrt-merged = ' + codex_list(dora_full[4]) + ' }']
    lines += ['', '  lora-ref-kind-te : LoraRefSlice',
              '  lora-ref-kind-te = LoraRefSlice { lrs-tensor = "' + kind_te[0] + '", lrs-row0 = ' + str(kind_te[1]) + ', lrs-rows = ' + str(kind_te[2])
              + ', lrs-cols = 1, lrs-base = ' + codex_list(kind_te[4]) + ', lrs-merged = ' + codex_list(kind_te[5]) + ' }']
    sd15_lines, sd15_note = sd15_section(os.path.dirname(ckpt), load_lora, merge_lora_to_weight, model_lora_keys_unet, model_lora_keys_clip,
                                         detection, SimpleNamespace, safe_open, load_file, save_file)
    lines += sd15_lines
    print(sd15_note)
    with open(out, 'w', newline='\n') as o:
        o.write('\n'.join(lines) + '\n')
    moved = sum(sum(1 for a, b in zip(r[2], r[3]) if a != b) for r in rows)
    print('targets', len(rows), 'samples', SAMPLES * len(rows), 'moved by the merge', moved, 'translated modules', len(translated),
          'te targets', len(te_rows), 'te moved', sum(sum(1 for a, b in zip(r[4], r[5]) if a != b) for r in te_rows), 'te keys', len(te_keys),
          'kind targets', len(kind_rows), 'kinds moved', sum(sum(1 for a, b in zip(r[3], r[4]) if a != b) for r in kind_rows))

if __name__ == '__main__':
    main()
