# clip-sd15-oracle.py -- SD1.5's text conditioning for whole prompts through
# Diffusion Forge's own ClassicTextProcessingEngine, written as reference data
# for codex/test/apps/clip-sd15-prompt.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/clip-sd15-oracle.py <out dir>
#
# The engine is configured as backend/diffusion_engine/sd15.py:36-47 configures
# it: CLIP-L alone, clip skip 1, the final LayerNorm, no pooled output; opts is a
# stand-in whose only field, emphasis, is Forge's default "Original". The weights
# are the checkpoint's cond_stage_model.transformer.text_model tensors under
# transformers' CLIPTextModel with the runwayml v1-5 text_encoder config
# (huggingface_guess model_list.py SD15), loaded strictly, in f32.
#
# One file per prompt, little-endian: the chunk count as i32; the tokens for
# every chunk, 77 per chunk, as i32; the token weights as f32; cond (77 n x 768)
# as f32. The same chunks through the f16 model, with the same emphasis
# arithmetic, are reported against the f32 answer over all rows and over the
# rows that are not a chunk's BOS: that deviation is the tolerance a consumer's
# grade is set by.
import os, struct, sys, types
import torch
from safetensors import safe_open

FORGE = r'D:\AI\DiffusionForge\webui'
CKPT = os.path.join(FORGE, r'models\Stable-diffusion\realisticVisionV60B1_v20Novae.safetensors')
BASE = os.path.join(FORGE, r'backend\huggingface\runwayml\stable-diffusion-v1-5')
sys.path[:0] = [FORGE, os.path.join(FORGE, 'repositories', 'huggingface_guess'), os.path.join(FORGE, 'packages_3rdparty')]
out_dir = sys.argv[1]
sys.argv = sys.argv[:1]
modules = types.ModuleType('modules')
shared = types.ModuleType('modules.shared')
shared.opts = types.SimpleNamespace(emphasis='Original')
modules.shared = shared
sys.modules['modules'] = modules
sys.modules['modules.shared'] = shared
from backend.text_processing.classic_engine import ClassicTextProcessingEngine
from transformers import CLIPTextConfig, CLIPTextModel, CLIPTokenizer

STYLE = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, '
PROMPTS = [
    ('cat', 'a photo of a cat'),
    ('emphasis', '(masterpiece:1.2), ((best quality)), a [red] cat on a \\(wooden\\) table, (glowing runes:0.8), [[blurry]] (sharp:1.5 edges'),
    ('long', STYLE + 'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window, carved dragon heads on the rafters, frost on the windows, a sleeping wolf by the hearth BREAK a longship on a calm fjord at dawn'),
    ('empty', ''),
    ('two', STYLE + 'a longship with a striped sail on a calm fjord at dawn, (mist:1.2) over the water, snowy peaks, a lighthouse of stacked stones, ravens circling, oars dipping in unison, shields along the rail, a carved serpent prow, golden light on the waves'),
]
PREFIX = 'cond_stage_model.transformer.'

class Encoder:
    def __init__(self, transformer):
        self.transformer = transformer

def build(sd, dt):
    m = CLIPTextModel(CLIPTextConfig.from_pretrained(os.path.join(BASE, 'text_encoder')))
    missing, unexpected = m.load_state_dict(sd, strict=False)
    missing = [k for k in missing if k != 'text_model.embeddings.position_ids']
    unexpected = [k for k in unexpected if k != 'text_model.embeddings.position_ids']
    if missing or unexpected:
        sys.exit('REFUSE: missing %s unexpected %s' % (missing[:5], unexpected[:5]))
    return m.eval().to('cuda', dt)

def emphasised(z, mults):
    m = torch.tensor(mults, dtype=torch.float32).reshape(-1, 1)
    original = z.mean()
    z = z * m
    return z * (original / z.mean())

def main():
    with safe_open(CKPT, framework='pt') as f:
        sd = {k[len(PREFIX):]: f.get_tensor(k) for k in f.keys() if k.startswith(PREFIX + 'text_model.')}
    tok = CLIPTokenizer.from_pretrained(os.path.join(BASE, 'tokenizer'))
    ml, hl = build(sd, torch.float32), build(sd, torch.float16)
    engine = ClassicTextProcessingEngine(text_encoder=Encoder(ml), tokenizer=tok, embedding_dir=None,
        embedding_key='clip_l', embedding_expected_shape=768, emphasis_name='Original',
        text_projection=False, minimal_clip_skip=1, clip_skip=1, return_pooled=False, final_layer_norm=True)
    for name, prompt in PROMPTS:
        chunks = engine.process_texts([prompt])[0][0]
        with torch.inference_mode():
            cond = engine([prompt])[0].float().cpu()
            half = []
            for c in chunks:
                out = hl(torch.tensor([c.tokens], device='cuda'), output_hidden_states=True)
                z = hl.text_model.final_layer_norm(out.hidden_states[-1])
                half.append(emphasised(z[0].float().cpu(), c.multipliers))
        half = torch.cat(half)
        n = len(chunks)
        with open(os.path.join(out_dir, name + '.ref'), 'wb') as f:
            f.write(struct.pack('<i', n))
            for c in chunks:
                f.write(struct.pack('<77i', *c.tokens))
            for c in chunks:
                f.write(struct.pack('<77f', *c.multipliers))
            f.write(cond.contiguous().numpy().astype('<f4').tobytes())
        body = [r for r in range(77 * n) if r % 77 != 0]
        weights = sorted(set(round(w, 6) for c in chunks for w in c.multipliers))
        print('%s: %d chunk(s), weights %s; cond f16 max err %.5f, non-BOS rows %.5f' % (name, n, weights, (cond - half).abs().max().item(), (cond - half)[body].abs().max().item()))

main()
