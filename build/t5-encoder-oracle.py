# t5-encoder-oracle.py -- Flux's T5-XXL encoder over one prompt chunk, written
# as the Codex chapter apps/diffusion/T5Reference, which
# codex/test/apps/t5-encoder-step grades apps/diffusion/T5Encoder against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/t5-encoder-oracle.py <out.codex>
#
# The model is transformers' T5EncoderModel under FLUX.1-schnell's
# text_encoder_2/config.json, in float32 on the CPU (19 GB), every weight of
# models/text_encoder/t5xxl_fp8_e4m3fn.safetensors cast from e4m3 to f32 as
# Forge casts fp8 storage at use. The input is PROMPT through FLUX.1-schnell's
# tokenizer_2, </s>, and <pad> to 256 (Forge's T5 engine at weight 1.0); the
# ids are written out so the guest needs no tokenizer. Recorded, by forward
# hooks: the embedding's output, each of the 24 blocks' outputs, and the final
# layer norm's, each as shape, mean, root mean square and the values at SAMPLES
# positions (j * STRIDE + OFFSET) mod numel. The run is repeated with every
# Linear's input rounded to f16 first, which is the guest's GEMM operand
# precision; that run's worst sampled deviation from the f32 one, over the
# block's rms, is written beside each block as the tolerance it earns. Also
# written: T5's relative position bucket for every distance j - i from -255 to
# 255 (transformers' own _relative_position_bucket, bidirectional, 32 buckets,
# max distance 128), which the guest computes itself. Last, EMPH through Forge's
# own T5TextProcessingEngine.tokenize_line and EmphasisOriginal.after_transformers
# (modules.shared stubbed with opts.emphasis Original) over the same f32 model:
# its conditioned output, sampled as above.
import math, os, struct, sys
import numpy as np
import torch

WEBUI = r'D:\AI\DiffusionForge\webui'
HF = os.path.join(WEBUI, r'backend\huggingface\black-forest-labs\FLUX.1-schnell')
CKPT = os.path.join(WEBUI, r'models\text_encoder\t5xxl_fp8_e4m3fn.safetensors')
PROMPT = 'a photo of a red apple on a wooden table, soft window light'
EMPH = 'a photo of a (red:1.3) apple on a [wooden] table, ((soft window light))'
SAMPLES, STRIDE, OFFSET = 64, 7919, 13
TOKENS = 256

def real(v):
    s = np.format_float_positional(np.float32(v), unique=True, trim='0')
    if s.endswith('.'): s += '0'
    return '(0.0 - ' + s[1:] + ')' if s.startswith('-') else s

def main():
    out = sys.argv[1]
    from transformers import T5Config, T5EncoderModel, T5TokenizerFast
    from safetensors import safe_open
    tok = T5TokenizerFast.from_pretrained(os.path.join(HF, 'tokenizer_2'))
    ids = tok(PROMPT, add_special_tokens=True)['input_ids']
    ids = ids + [0] * (TOKENS - len(ids))
    cfg = T5Config.from_json_file(os.path.join(HF, 'text_encoder_2', 'config.json'))
    with torch.device('meta'):
        model = T5EncoderModel(cfg)
    state = {}
    with safe_open(CKPT, framework='pt') as f:
        for k in f.keys():
            state[k] = f.get_tensor(k).to(torch.float32)
    model.load_state_dict(state, strict=True, assign=True)
    model = model.float().eval()
    x = torch.tensor([ids])

    def run(f16_operands):
        hooks, outs = [], []
        if f16_operands:
            for m in model.modules():
                if isinstance(m, torch.nn.Linear):
                    hooks.append(m.register_forward_pre_hook(lambda mod, a: (a[0].half().float(),)))
        grab = lambda mod, a, o: outs.append((o[0] if isinstance(o, tuple) else o)[0].float().numpy().astype(np.float64).reshape(-1))
        hooks.append(model.encoder.embed_tokens.register_forward_hook(grab))
        for b in model.encoder.block:
            hooks.append(b.register_forward_hook(grab))
        hooks.append(model.encoder.final_layer_norm.register_forward_hook(grab))
        with torch.no_grad():
            model(input_ids=x)
        for h in hooks: h.remove()
        if len(outs) != 26:
            raise SystemExit('REFUSE: %d recorded states, not 26' % len(outs))
        return outs

    from transformers.models.t5.modeling_t5 import T5Attention
    buckets = T5Attention._relative_position_bucket(torch.arange(-(TOKENS - 1), TOKENS), bidirectional=True, num_buckets=cfg.relative_attention_num_buckets, max_distance=cfg.relative_attention_max_distance).tolist()
    ref = run(False)
    import types
    shared = types.ModuleType('modules.shared'); shared.opts = types.SimpleNamespace(emphasis='Original')
    sys.modules['modules.shared'] = shared
    if 'modules' not in sys.modules:
        pkg = types.ModuleType('modules'); pkg.__path__ = []; sys.modules['modules'] = pkg
    sys.modules['modules'].shared = shared
    sys.path[:0] = [WEBUI, os.path.join(WEBUI, 'packages_3rdparty'), os.path.join(WEBUI, 'repositories', 'huggingface_guess')]
    from backend.text_processing import emphasis
    from backend.text_processing.t5_engine import T5TextProcessingEngine
    eng = T5TextProcessingEngine.__new__(T5TextProcessingEngine)
    eng.tokenizer = tok; eng.emphasis = emphasis.EmphasisOriginal(); eng.min_length = TOKENS; eng.id_end = 1; eng.id_pad = 0
    echunks, _ = eng.tokenize_line(EMPH)
    if len(echunks) != 1:
        raise SystemExit('REFUSE: EMPH gives %d chunks' % len(echunks))
    e = emphasis.EmphasisOriginal()
    with torch.no_grad():
        ez = model(input_ids=torch.tensor([echunks[0].tokens])).last_hidden_state
        e.tokens = [echunks[0].tokens]; e.multipliers = torch.asarray([echunks[0].multipliers]).to(ez); e.z = ez
        e.after_transformers()
    emph = e.z[0].float().numpy().astype(np.float64).reshape(-1)
    f16 = run(True)
    names = ['embedding'] + ['block %d' % i for i in range(24)] + ['final']
    lines = ['Chapter: T5Reference', '  cites Diffusion chapter UNetReference', '',
             ' Generated by build/t5-encoder-oracle.py: T5-XXL (t5xxl_fp8_e4m3fn cast to f32)',
             ' over one prompt chunk, in float32. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Blocks', '',
             '  t5-ref-ids : List Integer = [%s]' % ', '.join(str(i) for i in ids), '']
    blocks, devs = [], []
    for k, (name, v) in enumerate(zip(names, ref)):
        n = v.size
        rms = math.sqrt((v * v).mean())
        pos = [(j * STRIDE + OFFSET) % n for j in range(SAMPLES)]
        dev = max(abs(f16[k][p] - v[p]) for p in pos) / rms
        d = 't5-ref-%d' % k
        blocks.append(d)
        devs.append(real(dev))
        lines += ['  %s : UNetRefBlock' % d,
                  '  %s = UNetRefBlock { urb-name = "%s", urb-c = %d, urb-h = %d, urb-w = 1, urb-mean = %s, urb-rms = %s, urb-samples = [%s] }'
                  % (d, name, 4096, TOKENS, real(v.mean()), real(rms), ', '.join(real(v[p]) for p in pos)), '']
    lines += ['  t5-ref-blocks : List UNetRefBlock', '  t5-ref-blocks = [%s]' % ', '.join(blocks), '',
              '  t5-ref-f16-dev : List Real = [%s]' % ', '.join(devs), '',
              '  t5-ref-buckets : List Integer = [%s]' % ', '.join(str(b) for b in buckets), '',
              '  t5-ref-emph-prompt : Text = "%s"' % EMPH, '']
    en = emph.size
    epos = [(j * STRIDE + OFFSET) % en for j in range(SAMPLES)]
    erms = math.sqrt((emph * emph).mean())
    lines += ['  t5-ref-emph : UNetRefBlock',
              '  t5-ref-emph = UNetRefBlock { urb-name = "emphasis", urb-c = 4096, urb-h = %d, urb-w = 1, urb-mean = %s, urb-rms = %s, urb-samples = [%s] }'
              % (TOKENS, real(emph.mean()), real(erms), ', '.join(real(emph[p]) for p in epos)), '', 'Page 1', '']
    open(out, 'w', newline='\r\n').write('\n'.join(lines))
    print('ids', ids[:16], 'f16 deviation', [float(d.replace('(0.0 - ', '-').rstrip(')')) for d in devs])

main()
