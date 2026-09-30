# clip-prompt-oracle.py -- SDXL's text conditioning for whole prompts through
# Diffusion Forge's own ClassicTextProcessingEngine, written as reference data
# for codex/test/apps/clip-sdxl-prompt.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/clip-prompt-oracle.py <out dir>
#
# The engines are Forge's, configured as sdxl.py:39-63 configures them, over the
# f32 CLIP-L and OpenCLIP-G that build/clip-encoder-oracle.py loads; opts is a
# stand-in whose only field, emphasis, is Forge's default "Original"
# (modules/shared_options.py). The engine parses (word:1.2) emphasis, cuts 75-
# token chunks with the comma backtrack of 20 and BREAK, re-pads G after the
# first EOS, and scales each chunk's hidden states by the token weights keeping
# the chunk's mean.
#
# One file per prompt, little-endian: the chunk count as i32; CLIP-L's tokens
# for every chunk, 77 per chunk, as i32; the token weights as f32; cond_l
# (77 n x 768), cond_g (77 n x 1280) and pooled (1280) as f32. The same chunks
# through the f16 models, with the same emphasis arithmetic, are reported
# against the f32 answer over all rows and over the rows that are not a chunk's
# BOS: that deviation is the tolerance a consumer's grade is set by.
import importlib.util, os, struct, sys, types
import torch

FORGE = r'D:\AI\DiffusionForge\webui'
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
from transformers import CLIPTokenizer

spec = importlib.util.spec_from_file_location('clip_encoder_oracle', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'clip-encoder-oracle.py'))
ceo = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ceo)

STYLE = ceo.STYLE
PROMPTS = [
    ('negative', 'text, letters, words, watermark, signature, logo, ui, blurry, deformed, ugly, lowres, jpeg artifacts, photo, photograph, modern'),
    ('emphasis', '(masterpiece:1.2), ((best quality)), a [red] cat on a \\(wooden\\) table, (glowing runes:0.8), [[blurry]] (sharp:1.5 edges'),
    ('long', STYLE + 'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window, carved dragon heads on the rafters, frost on the windows, a sleeping wolf by the hearth BREAK a longship on a calm fjord at dawn'),
]

class Encoder:
    def __init__(self, transformer):
        self.transformer = transformer

def engine(model, tok, g):
    return ClassicTextProcessingEngine(text_encoder=Encoder(model), tokenizer=tok, embedding_dir=None,
        embedding_key='clip_g' if g else 'clip_l', embedding_expected_shape=2048, emphasis_name='Original',
        text_projection=g, minimal_clip_skip=2, clip_skip=2, return_pooled=g, final_layer_norm=False)

def emphasised(z, mults):
    m = torch.tensor(mults, dtype=torch.float32).reshape(-1, 1)
    original = z.mean()
    z = z * m
    return z * (original / z.mean())

def main():
    tok = CLIPTokenizer.from_pretrained(os.path.join(ceo.BASE, 'tokenizer'))
    tok2 = CLIPTokenizer.from_pretrained(os.path.join(ceo.BASE, 'tokenizer_2'))
    sd_l, sd_g = ceo.load_clip()
    ml = ceo.build('text_encoder', sd_l, False).to('cuda', torch.float32)
    mg = ceo.build('text_encoder_2', sd_g, True).to('cuda', torch.float32)
    hl = ceo.build('text_encoder', sd_l, False).to('cuda', torch.float16)
    hg = ceo.build('text_encoder_2', sd_g, True).to('cuda', torch.float16)
    el, eg = engine(ml, tok, False), engine(mg, tok2, True)
    for name, prompt in PROMPTS:
        chunks_l, _ = el.process_texts([prompt])
        chunks_g, _ = eg.process_texts([prompt])
        chunks = chunks_l[0]
        if [c.tokens for c in chunks] != [c.tokens for c in chunks_g[0]]:
            sys.exit('REFUSE: %s chunks differently for the two engines' % name)
        with torch.inference_mode():
            cond_l = el([prompt])[0].float().cpu()
            cond_g, pooled = eg([prompt])
            cond_g, pooled = cond_g[0].float().cpu(), pooled[0].float().cpu()
            half_l, half_g, half_p = [], [], None
            for c in chunks:
                ids = c.tokens
                e = ids.index(tok.eos_token_id)
                ids_g = ids[:e + 1] + [tok2.pad_token_id] * (76 - e)
                zl, _ = ceo.encode(hl, torch.tensor([ids], device='cuda'), False)
                zg, pg = ceo.encode(hg, torch.tensor([ids_g], device='cuda'), True)
                half_l.append(emphasised(zl[0].float().cpu(), c.multipliers))
                half_g.append(emphasised(zg[0].float().cpu(), c.multipliers))
                half_p = pg[0].float().cpu() if half_p is None else half_p
        half_l, half_g = torch.cat(half_l), torch.cat(half_g)
        n = len(chunks)
        with open(os.path.join(out_dir, name + '.ref'), 'wb') as f:
            f.write(struct.pack('<i', n))
            for c in chunks:
                f.write(struct.pack('<77i', *c.tokens))
            for c in chunks:
                f.write(struct.pack('<77f', *c.multipliers))
            for x in (cond_l, cond_g, pooled):
                f.write(x.contiguous().numpy().astype('<f4').tobytes())
        body = [r for r in range(77 * n) if r % 77 != 0]
        err = ' '.join('%s f16 max err %.5f, non-BOS rows %.5f' % (k, (x - h).abs().max().item(), (x - h)[body].abs().max().item())
                       for k, x, h in (('cond_l', cond_l, half_l), ('cond_g', cond_g, half_g)))
        weights = sorted(set(round(w, 6) for c in chunks for w in c.multipliers))
        print('%s: %d chunk(s), weights %s; %s; pooled f16 max err %.5f' % (name, n, weights, err, (pooled - half_p).abs().max().item()))

main()
