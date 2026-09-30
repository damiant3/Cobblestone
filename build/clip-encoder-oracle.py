# clip-encoder-oracle.py -- SDXL's text conditioning for fixed prompts, as Diffusion
# Forge computes it, written as reference data for codex/test/apps/clip-sdxl-encode.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/clip-encoder-oracle.py <out dir>
#
# Forge's SDXL engine (backend/diffusion_engine/sdxl.py:39-63) takes CLIP-L's and
# OpenCLIP-G's hidden state at clip skip 2 (the penultimate layer, no final
# LayerNorm), and G's pooled output through text_projection. Weights come from
# the checkpoint by Forge's own key mapping (huggingface_guess model_list.py:296-
# 301, utils.clip_text_transformers_convert). Tokens are Forge's single chunk:
# BOS, the prompt's ids, then EOS to 77 (classic_engine.py:170-175); then every
# position after the first EOS takes the encoder's own tokenizer's pad id where
# that differs from EOS (classic_engine.py:300-303), which for SDXL's
# tokenizer_2 is "!", id 0, so G reads zeros where L reads EOS.
#
# One file per prompt, little-endian: CLIP-L's 77 token ids as i32, then cond_l
# 77 x 768, cond_g 77 x 1280 and pooled 1280 as f32, computed in f32. The same
# model run in f16 on the GPU, which is the precision Forge runs it at, is
# reported against the f32 answer over all rows and over rows 1-76: that
# deviation is the tolerance a consumer's grade is set by, and row 0 (BOS) is
# reported apart because CLIP-L's BOS row reaches 820 and sets the whole max.
import os, sys, struct
import torch
from safetensors import safe_open
from transformers import CLIPTextConfig, CLIPTextModel, CLIPTokenizer

FORGE = r'D:\AI\DiffusionForge\webui'
CKPT = os.path.join(FORGE, r'models\Stable-diffusion\dreamshaperXL_lightningDPMSDE.safetensors')
BASE = os.path.join(FORGE, r'backend\huggingface\stabilityai\stable-diffusion-xl-base-1.0')
sys.path.insert(0, os.path.join(FORGE, r'repositories\huggingface_guess'))
from huggingface_guess import utils as hg

STYLE = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, '
PROMPTS = [
    ('cat', 'a photo of a cat'),
    ('hero', STYLE + 'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window'),
]

def load_clip():
    sd = {}
    with safe_open(CKPT, framework='pt') as f:
        for k in f.keys():
            if k.startswith('conditioner.embedders.0.transformer.text_model.'):
                sd['L.' + k[len('conditioner.embedders.0.transformer.'):]] = f.get_tensor(k)
            elif k.startswith('conditioner.embedders.1.model.'):
                sd['clip_g.' + k[len('conditioner.embedders.1.model.'):]] = f.get_tensor(k)
    sd = hg.clip_text_transformers_convert(sd, 'clip_g.', 'clip_g.transformer.')
    sd_l = {k[2:]: v for k, v in sd.items() if k.startswith('L.')}
    sd_g = {k[len('clip_g.transformer.'):]: v for k, v in sd.items() if k.startswith('clip_g.transformer.')}
    return sd_l, sd_g

def build(sub, sd, projection):
    cfg = CLIPTextConfig.from_pretrained(os.path.join(BASE, sub))
    m = CLIPTextModel(cfg)
    if projection:
        m.text_projection = torch.nn.Linear(cfg.hidden_size, cfg.hidden_size, bias=False)
    missing, unexpected = m.load_state_dict(sd, strict=False)
    missing = [k for k in missing if k != 'text_model.embeddings.position_ids']
    if missing or unexpected:
        sys.exit('REFUSE: %s missing %s unexpected %s' % (sub, missing[:5], unexpected[:5]))
    return m.eval()

def encode(m, ids, projection):
    out = m(ids, output_hidden_states=True)
    cond = out.hidden_states[-2]
    pooled = m.text_projection(out.pooler_output) if projection else None
    return cond, pooled

def main():
    out_dir = sys.argv[1]
    tok = CLIPTokenizer.from_pretrained(os.path.join(BASE, 'tokenizer'))
    tok2 = CLIPTokenizer.from_pretrained(os.path.join(BASE, 'tokenizer_2'))
    sd_l, sd_g = load_clip()
    models = {}
    for dev, dt in (('cpu', torch.float32), ('cuda', torch.float16)):
        models[dt] = (build('text_encoder', sd_l, False).to(dev, dt), build('text_encoder_2', sd_g, True).to(dev, dt), dev)
    for name, prompt in PROMPTS:
        body = tok(prompt, truncation=False, add_special_tokens=False)['input_ids']
        if len(body) > 75:
            sys.exit('REFUSE: %s is %d tokens, past one chunk' % (name, len(body)))
        if tok2(prompt, truncation=False, add_special_tokens=False)['input_ids'] != body:
            sys.exit('REFUSE: %s tokenizes differently under tokenizer_2' % name)
        ids = [tok.bos_token_id] + body + [tok.eos_token_id] * (76 - len(body))
        ids_g = ids[:len(body) + 2] + [tok2.pad_token_id] * (75 - len(body))
        res = {}
        with torch.inference_mode():
            for dt, (ml, mg, dev) in models.items():
                cl, _ = encode(ml, torch.tensor([ids], device=dev), False)
                cg, pooled = encode(mg, torch.tensor([ids_g], device=dev), True)
                res[dt] = [x[0].float().cpu() for x in (cl, cg)] + [pooled[0].float().cpu()]
        ref, half = res[torch.float32], res[torch.float16]
        with open(os.path.join(out_dir, name + '.ref'), 'wb') as f:
            f.write(struct.pack('<77i', *ids))
            for x in ref:
                f.write(x.contiguous().numpy().astype('<f4').tobytes())
        err = ' '.join('%s f16 max err %.5f, rows 1-76 %.5f' % (n, (x - h).abs().max().item(), (x - h)[1:].abs().max().item())
                       for n, x, h in zip(('cond_l', 'cond_g'), ref, half))
        print('%s: %d tokens, G pad %d; %s; pooled f16 max err %.5f' % (name, len(body), tok2.pad_token_id, err, (ref[2] - half[2]).abs().max().item()))

if __name__ == '__main__':
    main()
