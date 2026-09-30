# sd15-unet-oracle.py -- one SD1.5 UNet step through Diffusion Forge's own UNet,
# written out as the Codex chapter the guest's SD1.5 UNet is graded against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/sd15-unet-oracle.py <checkpoint> <out.codex> [<sampler.codex>]
#   ... build/sd15-unet-oracle.py <checkpoint> <out.codex> --odd   (a 15 x 11 latent, chapter UNet15OddReference)
#
# The model is Forge's IntegratedUNet2DConditionModel (backend/nn/unet.py:481)
# configured by Forge's own detect_unet_config from the checkpoint's keys, with
# SD1.5's num_heads 8 and num_head_channels -1 (huggingface_guess model_list.py,
# SD15 unet_extra_config), loaded strictly and run in float32 on CUDA. The
# inputs are build/sdxl-unet-oracle.py's formulas, the context 77 x 768 and no
# label; every one is a multiple of 1/256, exact in f16. The sample positions
# are UNetReference's, whose block record the chapter reuses. Each block's output
# (the input blocks, the middle block, the output blocks and the final out) is
# recorded as its shape, mean, root mean square and the values at SAMPLES
# positions (j * STRIDE + OFFSET) mod numel.
import inspect, math, os, sys
import numpy as np
import torch

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path.insert(0, WEBUI)
sys.path.insert(0, os.path.join(WEBUI, 'repositories', 'huggingface_guess'))
sys.path.insert(0, os.path.join(WEBUI, 'packages_3rdparty'))

H = W = 16
CHAPTER, NAME = 'UNet15Reference', 'unet15-ref'
T = 500.0
SAMPLES, STRIDE, OFFSET = 64, 7919, 13
PREFIX = 'model.diffusion_model.'

def latent(i): return ((i * 37 + 11) % 257 - 128) / 128.0
def context(i): return ((i * 13 + 5) % 251 - 125) / 256.0

def real(v):
    s = np.format_float_positional(np.float32(v), unique=True, trim='0')
    if s.endswith('.'): s += '0'
    return '(0.0 - ' + s[1:] + ')' if s.startswith('-') else s

# --sag runs the step as SAG's uncond call, as build/sdxl-unet-oracle.py --sag does:
# sd_forge_sag's own attn_and_record keyed ("middle", 0, 0), then its create_blur_map
# over the latent SAG_X0 builds; writes UNet15SagReference.
def sag_record():
    import ast, types
    from einops import rearrange, repeat
    from backend import attention, memory_management
    src = os.path.join(WEBUI, 'extensions-builtin', 'sd_forge_sag', 'scripts', 'forge_sag.py')
    tree = ast.parse(open(src, encoding='utf-8').read(), src)
    nodes = [n for n in tree.body if (isinstance(n, ast.FunctionDef) and n.name in ('attention_basic_with_sim', 'create_blur_map', 'gaussian_blur_2d')) or (isinstance(n, ast.ClassDef) and n.name == 'SelfAttentionGuidance')]
    env = {'torch': torch, 'math': math, 'einsum': torch.einsum, 'rearrange': rearrange, 'repeat': repeat, 'attention': attention,
           'attn_precision': memory_management.force_upcast_attention_dtype(), 'shared': types.SimpleNamespace(), 'calc_cond_uncond_batch': None}
    exec(compile(ast.Module(nodes, []), src, 'exec'), env)
    cap = {}
    m = types.SimpleNamespace(set_model_sampler_post_cfg_function=lambda f, disable_cfg1_optimization=False: cap.setdefault('post', f),
                              set_model_attn1_replace=lambda f, b, n, i: cap.setdefault('rep', (f, (b, n, i))))
    env['SelfAttentionGuidance']().patch(types.SimpleNamespace(clone=lambda: m), 0.5, 2.0, 1.0)
    return cap, env['create_blur_map']

def SAG_X0(i): return ((i * 37 + 11) % 257 - 128) / 64.0

def main():
    global sys_args, H, W, CHAPTER, NAME
    sys_args = list(sys.argv)
    sag = '--sag' in sys_args
    if sag:
        sys_args.remove('--sag')
        CHAPTER, NAME = 'UNet15SagReference', 'unet15-sag-ref'
    if '--odd' in sys_args:
        sys_args.remove('--odd')
        H, W, CHAPTER, NAME = 15, 11, 'UNet15OddReference', 'unet15-odd-ref'
    ckpt, out = sys_args[1], sys_args[2]
    sys.argv = sys.argv[:1]
    from safetensors import safe_open
    from huggingface_guess import detection
    from backend.nn.unet import IntegratedUNet2DConditionModel

    with safe_open(ckpt, framework='pt') as f:
        keys = [k for k in f.keys() if k.startswith(PREFIX)]
        cfg = detection.detect_unet_config({k: torch.empty(f.get_slice(k).get_shape(), device='meta') for k in keys}, PREFIX)
        if cfg.get('adm_in_channels') is not None or cfg.get('context_dim') != 768:
            print('REFUSE: not an SD1.5 UNet: %s' % cfg)
            sys.exit(1)
        cfg['num_heads'] = 8
        cfg['num_head_channels'] = -1
        accepted = inspect.signature(IntegratedUNet2DConditionModel.__init__).parameters
        for k in [k for k in cfg if k not in accepted]:
            del cfg[k]
        with torch.device('cuda'):
            model = IntegratedUNet2DConditionModel(**cfg).float().eval()
        params = model.state_dict()
        names = {k[len(PREFIX):] for k in keys}
        if names != set(params):
            print('REFUSE: %d model tensors not in the checkpoint, %d checkpoint tensors not in the model' % (len(set(params) - names), len(names - set(params))))
            sys.exit(1)
        with torch.no_grad():
            for k in keys:
                params[k[len(PREFIX):]].copy_(f.get_tensor(k).to('cuda'))

    blocks = []
    def hook(name):
        def f(m, a, o): blocks.append((name, o.detach().float().cpu()))
        return f
    for i, m in enumerate(model.input_blocks): m.register_forward_hook(hook('input %d' % i))
    model.middle_block.register_forward_hook(hook('middle'))
    for i, m in enumerate(model.output_blocks): m.register_forward_hook(hook('output %d' % i))
    model.out.register_forward_hook(hook('out'))

    x = torch.tensor([latent(i) for i in range(4 * H * W)], dtype=torch.float32).reshape(1, 4, H, W)
    ctx = torch.tensor([context(i) for i in range(77 * 768)], dtype=torch.float32).reshape(1, 77, 768)
    opts = {}
    if sag:
        cap, create_blur_map = sag_record()
        opts = {'patches_replace': {'attn1': {cap['rep'][1]: cap['rep'][0]}}, 'cond_or_uncond': [1]}
    with torch.no_grad():
        model(x.cuda(), timesteps=torch.tensor([T]).cuda(), context=ctx.cuda(), transformer_options=opts)

    lines = ['Chapter: ' + CHAPTER, '  cites Diffusion chapter UNetReference', '',
             ' Generated by build/sd15-unet-oracle.py from %s: one SD1.5 UNet step' % os.path.basename(ckpt),
             ' through Diffusion Forge\'s own UNet in float32. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Blocks', '',
             '  %s-t : Real = %s' % (NAME, real(T)), '']
    names = []
    for k, (name, t) in enumerate(blocks):
        v = t.reshape(-1).numpy().astype(np.float64)
        n = v.size
        s = ', '.join(real(v[(j * STRIDE + OFFSET) % n]) for j in range(SAMPLES))
        d = '%s-%d' % (NAME, k)
        names.append(d)
        lines += ['  %s : UNetRefBlock' % d,
                  '  %s = UNetRefBlock { urb-name = "%s", urb-c = %d, urb-h = %d, urb-w = %d, urb-mean = %s, urb-rms = %s, urb-samples = [%s] }'
                  % (d, name, t.shape[1], t.shape[2], t.shape[3], real(v.mean()), real(math.sqrt((v * v).mean())), s), '']
    lines += ['  %s-blocks : List UNetRefBlock' % NAME, '  %s-blocks = [%s]' % (NAME, ', '.join(names)), '']
    if sag:
        post = cap['post']
        scores = post.__closure__[post.__code__.co_freevars.index('attn_scores')].cell_contents
        rows = scores.shape[-1]
        mask = (scores.reshape(1, -1, rows, rows).mean(1).sum(1) > 1.0).reshape(-1).int().tolist()
        x0 = torch.tensor([SAG_X0(i) for i in range(4 * H * W)], dtype=torch.float32).reshape(1, 4, H, W).cuda()
        with torch.no_grad():
            degraded = create_blur_map(x0, scores, 2.0, 1.0).reshape(-1).float().cpu().numpy().astype(np.float64)
        lines += ['  %s-mask : List Integer' % NAME, '  %s-mask = [%s]' % (NAME, ', '.join(str(v) for v in mask)), '',
                  '  %s-degraded : List Real' % NAME, '  %s-degraded = [%s]' % (NAME, ', '.join(real(v) for v in degraded)), '']
        print('sag mask', sum(mask), 'of', len(mask))
    lines += ['Page 1', '']
    open(out, 'w', newline='\r\n').write('\n'.join(lines))
    print('%d blocks, config %s' % (len(blocks), cfg))
    if len(sys_args) > 3:
        euler(model, ctx.cuda(), sys_args[3])

# The sampler reference, as build/sdxl-unet-oracle.py's: k-diffusion's own
# sample_euler over DiscreteEpsDDPMDenoiser (quantized, as there), SD1.5's scaled-linear betas
# 0.00085 to 0.012 over 1000 steps (v1-inference.yaml, the same as SDXL's), a
# Karras schedule of EULER_STEPS, no guidance, the start noise(i) times
# sigma_max; every step's sigma, t and output latent are recorded, as the
# chapter Sampler15Reference over SamplerReference's record.
EULER_STEPS = 3
def noise(i): return ((i * 53 + 17) % 263 - 131) / 128.0

def euler(model, ctx, out):
    from k_diffusion import external, sampling
    betas = torch.linspace(0.00085 ** 0.5, 0.012 ** 0.5, 1000, dtype=torch.float64) ** 2
    acp = torch.cumprod(1.0 - betas, dim=0)
    class Eps(torch.nn.Module):
        def forward(self, x, t, **kw): return model(x, timesteps=t, context=ctx)
    den = external.DiscreteEpsDDPMDenoiser(Eps(), acp.float().cuda(), quantize=True)
    sigmas = sampling.get_sigmas_karras(EULER_STEPS, float(den.sigma_min), float(den.sigma_max), device='cuda')
    x = torch.tensor([noise(i) for i in range(4 * H * W)], dtype=torch.float32).reshape(1, 4, H, W).cuda() * sigmas[0]
    steps = []
    def cb(d): steps.append((float(d['sigma']), float(den.sigma_to_t(d['sigma'].reshape(1))[0])))
    xs = []
    with torch.no_grad():
        for i in range(EULER_STEPS):
            x = sampling.sample_euler(den, x, sigmas[i:i + 2], callback=cb, disable=True)
            xs.append(x.detach().float().cpu())
    lines = ['Chapter: Sampler15Reference', '  cites Diffusion chapter SamplerReference', '',
             ' Generated by build/sd15-unet-oracle.py: k-diffusion sample_euler over Forge\'s', ' SD1.5 UNet, no guidance. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Steps', '']
    names = []
    for k, ((sg, t), xt) in enumerate(zip(steps, xs)):
        v = xt.reshape(-1).numpy().astype(np.float64)
        n = v.size
        s = ', '.join(real(v[(j * STRIDE + OFFSET) % n]) for j in range(SAMPLES))
        names.append('sampler15-ref-%d' % k)
        lines += ['  sampler15-ref-%d : SamplerRefStep' % k,
                  '  sampler15-ref-%d = SamplerRefStep { srs-sigma = %s, srs-t = %s, srs-rms = %s, srs-samples = [%s] }' % (k, real(sg), real(t), real(math.sqrt((v * v).mean())), s), '']
    lines += ['  sampler15-ref-steps : List SamplerRefStep', '  sampler15-ref-steps = [%s]' % ', '.join(names), '', 'Page 1', '']
    open(out, 'w', newline='\r\n').write('\n'.join(lines))
    print('%d Euler steps, sigmas %s' % (EULER_STEPS, [float(s) for s in sigmas]))

main()
