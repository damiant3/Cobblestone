# sdxl-unet-oracle.py -- one SDXL UNet step through Diffusion Forge's own
# UNet, written out as the Codex chapter the guest's UNet is graded against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/sdxl-unet-oracle.py <checkpoint> <out.codex> [<sampler out.codex>]
#
# The model is Forge's IntegratedUNet2DConditionModel (backend/nn/unet.py:481)
# configured by Forge's own detect_unet_config from the checkpoint's keys, with
# SDXL's num_head_channels 64 (huggingface_guess model_list.py:23), loaded
# strictly and run in float32 on CUDA. The inputs are built from the formulas
# below, which the guest repeats; every one is a multiple of 1/256, exact in
# f16. Each block's output (the input blocks, the middle block, the output
# blocks and the final out) is recorded as its shape, mean, root mean square
# and the values at SAMPLES positions (j * STRIDE + OFFSET) mod numel.
import inspect, math, os, sys
import numpy as np
import torch

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path.insert(0, WEBUI)
sys.path.insert(0, os.path.join(WEBUI, 'repositories', 'huggingface_guess'))
sys.path.insert(0, os.path.join(WEBUI, 'packages_3rdparty'))

H = W = 16
T = 500.0
SAMPLES, STRIDE, OFFSET = 64, 7919, 13
PREFIX = 'model.diffusion_model.'

def latent(i): return ((i * 37 + 11) % 257 - 128) / 128.0
def context(i): return ((i * 13 + 5) % 251 - 125) / 256.0
def label(i): return ((i * 29 + 3) % 241 - 120) / 256.0

def real(v):
    s = np.format_float_positional(np.float32(v), unique=True, trim='0')
    if s.endswith('.'): s += '0'
    return '(0.0 - ' + s[1:] + ')' if s.startswith('-') else s

# --freeu b1 b2 s1 s2 runs the step under sd_forge_freeu's own output-block
# patch (lifted by build/freeu-oracle.py) and writes UNetFreeUReference.
def freeu_patch(b1, b2, s1, s2):
    import importlib.util, types
    spec = importlib.util.spec_from_file_location('freeu_oracle', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'freeu-oracle.py'))
    fo = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fo)
    captured = {}
    patcher = types.SimpleNamespace(model=types.SimpleNamespace(diffusion_model=types.SimpleNamespace(config={'model_channels': 320})))
    patcher.clone = lambda: types.SimpleNamespace(set_model_output_block_patch=lambda p: captured.setdefault('p', p))
    fo.lift()(patcher, b1, b2, s1, s2)
    return captured['p']

# --sag runs the step as SAG's uncond call: sd_forge_sag's own attn_and_record on the
# middle block's first self-attention, keyed ("middle", 0, 0), and writes
# UNetSagReference with the mask and create_blur_map of the latent SAG_X0 builds.
def sag_record():
    import ast, types
    from einops import rearrange, repeat
    from torch import einsum
    from backend import attention, memory_management
    src = os.path.join(WEBUI, 'extensions-builtin', 'sd_forge_sag', 'scripts', 'forge_sag.py')
    tree = ast.parse(open(src, encoding='utf-8').read(), src)
    nodes = [n for n in tree.body if (isinstance(n, ast.FunctionDef) and n.name in ('attention_basic_with_sim', 'create_blur_map', 'gaussian_blur_2d')) or (isinstance(n, ast.ClassDef) and n.name == 'SelfAttentionGuidance')]
    env = {'torch': torch, 'math': math, 'einsum': einsum, 'rearrange': rearrange, 'repeat': repeat, 'attention': attention,
           'attn_precision': memory_management.force_upcast_attention_dtype(), 'shared': types.SimpleNamespace(), 'calc_cond_uncond_batch': None}
    exec(compile(ast.Module(nodes, []), src, 'exec'), env)
    cap = {}
    m = types.SimpleNamespace(set_model_sampler_post_cfg_function=lambda f, disable_cfg1_optimization=False: cap.setdefault('post', f),
                              set_model_attn1_replace=lambda f, b, n, i: cap.setdefault('rep', (f, (b, n, i))))
    env['SelfAttentionGuidance']().patch(types.SimpleNamespace(clone=lambda: m), 0.5, 2.0, 1.0)
    return cap, env['create_blur_map']

def SAG_X0(i): return ((i * 37 + 11) % 257 - 128) / 64.0

def main():
    global sys_args
    freeu = None
    if '--freeu' in sys.argv:
        k = sys.argv.index('--freeu')
        freeu = [float(v) for v in sys.argv[k + 1:k + 5]]
        del sys.argv[k:k + 5]
    # --pag: sd_forge_perturbed_attention's attn1 replace (its attn_proc answers v)
    # on the middle block, keyed by Forge's own set_model_options_patch_replace.
    pag = '--pag' in sys.argv
    sag = '--sag' in sys.argv
    if sag:
        sys.argv.remove('--sag')
    if pag:
        sys.argv.remove('--pag')
    sys_args = list(sys.argv)
    ckpt, out = sys.argv[1], sys.argv[2]
    sys.argv = sys.argv[:1]
    from safetensors import safe_open
    from huggingface_guess import detection
    from backend.nn.unet import IntegratedUNet2DConditionModel

    # Config from shapes alone, then weights one tensor at a time straight
    # into a CUDA model: host memory holds one tensor, not the checkpoint.
    with safe_open(ckpt, framework='pt') as f:
        keys = [k for k in f.keys() if k.startswith(PREFIX)]
        cfg = detection.detect_unet_config({k: torch.empty(f.get_slice(k).get_shape(), device='meta') for k in keys}, PREFIX)
        cfg['num_heads'] = -1
        cfg['num_head_channels'] = 64
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
    ctx = torch.tensor([context(i) for i in range(77 * 2048)], dtype=torch.float32).reshape(1, 77, 2048)
    y = torch.tensor([label(i) for i in range(2816)], dtype=torch.float32).reshape(1, 2816)
    opts = {'patches': {'output_block_patch': [freeu_patch(*freeu)]}} if freeu else {}
    if pag:
        from backend.patcher.base import set_model_options_patch_replace
        opts = set_model_options_patch_replace({'transformer_options': opts}, lambda q, k, v, to: v, 'attn1', 'middle', 0)['transformer_options']
    if sag:
        cap, create_blur_map = sag_record()
        opts = {'patches_replace': {'attn1': {cap['rep'][1]: cap['rep'][0]}}, 'cond_or_uncond': [1]}
    with torch.no_grad():
        model(x.cuda(), timesteps=torch.tensor([T]).cuda(), context=ctx.cuda(), y=y.cuda(), transformer_options=opts)
    if sag:
        post = cap['post']
        scores = post.__closure__[post.__code__.co_freevars.index('attn_scores')].cell_contents
        rows = scores.shape[-1]
        mask = (scores.reshape(1, -1, rows, rows).mean(1).sum(1) > 1.0).reshape(-1).int().tolist()
        x0 = torch.tensor([SAG_X0(i) for i in range(4 * H * W)], dtype=torch.float32).reshape(1, 4, H, W).cuda()
        with torch.no_grad():
            degraded = create_blur_map(x0, scores, 2.0, 1.0).reshape(-1).float().cpu().numpy().astype(np.float64)

    lines = ['Chapter: UNetReference', '',
             ' Generated by build/sdxl-unet-oracle.py from %s: one SDXL UNet step' % os.path.basename(ckpt),
             ' through Diffusion Forge\'s own UNet in float32. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Blocks', '',
             '  UNetRefBlock = record {', '    urb-name : Text,', '    urb-c : Integer,', '    urb-h : Integer,',
             '    urb-w : Integer,', '    urb-mean : Real,', '    urb-rms : Real,', '    urb-samples : List Real', '  }', '',
             '  unet-ref-h : Integer = %d' % H, '', '  unet-ref-w : Integer = %d' % W, '',
             '  unet-ref-t : Real = %s' % real(T), '', '  unet-ref-samples : Integer = %d' % SAMPLES, '',
             '  unet-ref-stride : Integer = %d' % STRIDE, '', '  unet-ref-offset : Integer = %d' % OFFSET, '']
    names = []
    for k, (name, t) in enumerate(blocks):
        v = t.reshape(-1).numpy().astype(np.float64)
        n = v.size
        s = ', '.join(real(v[(j * STRIDE + OFFSET) % n]) for j in range(SAMPLES))
        d = 'unet-ref-%d' % k
        names.append(d)
        lines += ['  %s : UNetRefBlock' % d,
                  '  %s = UNetRefBlock { urb-name = "%s", urb-c = %d, urb-h = %d, urb-w = %d, urb-mean = %s, urb-rms = %s, urb-samples = [%s] }'
                  % (d, name, t.shape[1], t.shape[2], t.shape[3], real(v.mean()), real(math.sqrt((v * v).mean())), s), '']
    lines += ['  unet-ref-blocks : List UNetRefBlock', '  unet-ref-blocks = [%s]' % ', '.join(names), '', 'Page 1', '']
    if freeu:
        lines[0] = 'Chapter: UNetFreeUReference'
        lines[3] = ' through Diffusion Forge\'s own UNet in float32, under sd_forge_freeu b1 %g b2 %g s1 %g s2 %g. Regenerate, do not edit.' % tuple(freeu)
        lines = [l.replace('UNetRefBlock', 'UNetFuRefBlock').replace('unet-ref-', 'unet-fu-ref-').replace('urb-', 'ufb-') for l in lines]
    if pag:
        lines[0] = 'Chapter: UNetPagReference'
        lines[3] = ' through Diffusion Forge\'s own UNet in float32, the middle block\'s attn1 answering v (PAG). Regenerate, do not edit.'
        lines = [l.replace('UNetRefBlock', 'UNetPagRefBlock').replace('unet-ref-', 'unet-pag-ref-').replace('urb-', 'upb-') for l in lines]
    if sag:
        lines[0] = 'Chapter: UNetSagReference'
        lines[3] = ' through Diffusion Forge\'s own UNet in float32 as SAG\'s uncond call (sd_forge_sag attn_and_record). Regenerate, do not edit.'
        lines = [l.replace('UNetRefBlock', 'UNetSagRefBlock').replace('unet-ref-', 'unet-sag-ref-').replace('urb-', 'usb-') for l in lines]
        tail = lines.index('Page 1')
        lines[tail:tail] = ['  unet-sag-mask : List Integer', '  unet-sag-mask = [%s]' % ', '.join(str(v) for v in mask), '',
                            '  unet-sag-degraded : List Real', '  unet-sag-degraded = [%s]' % ', '.join(real(v) for v in degraded), '']
        print('sag mask', sum(mask), 'of', len(mask))
    open(out, 'w', newline='\r\n').write('\n'.join(lines))
    print('%d blocks, config %s' % (len(blocks), cfg))
    if len(sys_args) > 3:
        euler(model, ctx.cuda(), y.cuda(), sys_args[3])

# The sampler reference: k-diffusion's own sample_euler (webui/k_diffusion,
# the copy Forge samples with) over DiscreteEpsDDPMDenoiser, quantized to the
# nearest discrete timestep as Forge's KModel runs the UNet
# (backend/modules/k_prediction.py:148-151, k_model.py:35), SDXL's
# scaled-linear betas 0.00085 to 0.012 over 1000 steps, a Karras schedule of
# EULER_STEPS from the table's own sigma_min and sigma_max, no guidance. The
# start is noise(i) times sigma_max; every step's sigma, t and output latent
# are recorded.
EULER_STEPS = 3
def noise(i): return ((i * 53 + 17) % 263 - 131) / 128.0

def euler(model, ctx, y, out):
    from k_diffusion import external, sampling
    betas = torch.linspace(0.00085 ** 0.5, 0.012 ** 0.5, 1000, dtype=torch.float64) ** 2
    acp = torch.cumprod(1.0 - betas, dim=0)
    class Eps(torch.nn.Module):
        def forward(self, x, t, **kw): return model(x, timesteps=t, context=ctx, y=y)
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
    lines = ['Chapter: SamplerReference', '',
             ' Generated by build/sdxl-unet-oracle.py: k-diffusion sample_euler over Forge\'s', ' UNet, no guidance. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Steps', '',
             '  SamplerRefStep = record {', '    srs-sigma : Real,', '    srs-t : Real,', '    srs-rms : Real,', '    srs-samples : List Real', '  }', '',
             '  sampler-ref-steps-count : Integer = %d' % EULER_STEPS, '', '  sampler-ref-sigma-last : Real = %s' % real(float(sigmas[EULER_STEPS])), '']
    names = []
    for k, ((sg, t), xt) in enumerate(zip(steps, xs)):
        v = xt.reshape(-1).numpy().astype(np.float64)
        n = v.size
        s = ', '.join(real(v[(j * STRIDE + OFFSET) % n]) for j in range(SAMPLES))
        names.append('sampler-ref-%d' % k)
        lines += ['  sampler-ref-%d : SamplerRefStep' % k,
                  '  sampler-ref-%d = SamplerRefStep { srs-sigma = %s, srs-t = %s, srs-rms = %s, srs-samples = [%s] }' % (k, real(sg), real(t), real(math.sqrt((v * v).mean())), s), '']
    lines += ['  sampler-ref-steps : List SamplerRefStep', '  sampler-ref-steps = [%s]' % ', '.join(names), '', 'Page 1', '']
    open(out, 'w', newline='\r\n').write('\n'.join(lines))
    print('%d Euler steps, sigmas %s' % (EULER_STEPS, [float(s) for s in sigmas]))

main()
