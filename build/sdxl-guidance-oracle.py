# sdxl-guidance-oracle.py -- one guided SDXL denoiser call as Forge makes it, with its
# built-in extras, written out as the Codex chapter codex/test/apps/sdxl-guidance-extras
# is graded against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/sdxl-guidance-oracle.py <checkpoint> <out.codex>
#
# Forge's own sampling_function_inner (backend/sampling/sampling_function.py) combines the
# cond and uncond predictions at CFG and runs the post-CFG functions; the extras are
# Forge's own code lifted out of extensions-builtin: FreeU's output-block patch
# (build/freeu-oracle.py), SAG's attn_and_record and post_cfg_function
# (SelfAttentionGuidance.patch), PAG's post_cfg_function
# (PerturbedAttentionGuidanceForForge.process_before_every_sampling), each installed
# where Forge's UNet patcher puts it and in script order (FreeU, SAG, PAG). Only
# calc_cond_uncond_batch is replaced: it runs each of cond and uncond as its own call,
# marked 0 or 1 in cond_or_uncond as Forge marks a batch, through k-diffusion's
# DiscreteEpsDDPMDenoiser over Forge's UNet in float32 (as build/sdxl-unet-oracle.py's
# Euler reference does). The inputs are the formulas below, which the guest repeats.
import ast, importlib.util, inspect, math, os, sys, types
import numpy as np
import torch

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path[:0] = [WEBUI, os.path.join(WEBUI, 'repositories', 'huggingface_guess'), os.path.join(WEBUI, 'packages_3rdparty')]
H = W = 16
SIGMA = 3.0
CFG = 5.0
FREEU = (1.3, 1.4, 0.9, 0.2)
SAG = (0.5, 2.0, 1.0)
PAG = 3.0
PREFIX = 'model.diffusion_model.'
EXT = os.path.join(WEBUI, 'extensions-builtin')

def latent(i): return ((i * 37 + 11) % 257 - 128) / 128.0
def context_c(i): return ((i * 13 + 5) % 251 - 125) / 256.0
def label_c(i): return ((i * 29 + 3) % 241 - 120) / 256.0
def context_u(i): return ((i * 17 + 3) % 239 - 119) / 256.0
def label_u(i): return ((i * 23 + 7) % 233 - 116) / 256.0

def real(v):
    s = np.format_float_positional(np.float32(v), unique=True, trim='0')
    if s.endswith('.'): s += '0'
    return '(0.0 - ' + s[1:] + ')' if s.startswith('-') else s

def lift(path, fnames, cnames, env):
    tree = ast.parse(open(path, encoding='utf-8').read(), path)
    nodes = [n for n in tree.body if (isinstance(n, ast.FunctionDef) and n.name in fnames) or (isinstance(n, ast.ClassDef) and n.name in cnames)]
    exec(compile(ast.Module(nodes, []), path, 'exec'), env)
    return env

def main():
    ckpt, out = sys.argv[1], sys.argv[2]
    sys.argv = sys.argv[:1]
    from safetensors import safe_open
    from huggingface_guess import detection
    from backend.nn.unet import IntegratedUNet2DConditionModel
    from backend import attention, memory_management
    from backend.patcher.base import set_model_options_patch_replace
    import backend.sampling.sampling_function as sf
    from einops import rearrange, repeat
    from k_diffusion import external

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
        with torch.no_grad():
            for k in keys:
                params[k[len(PREFIX):]].copy_(f.get_tensor(k).to('cuda'))

    conds = {'c': (torch.tensor([context_c(i) for i in range(77 * 2048)]).reshape(1, 77, 2048).cuda(), torch.tensor([label_c(i) for i in range(2816)]).reshape(1, 2816).cuda()),
             'u': (torch.tensor([context_u(i) for i in range(77 * 2048)]).reshape(1, 77, 2048).cuda(), torch.tensor([label_u(i) for i in range(2816)]).reshape(1, 2816).cuda())}

    class Eps(torch.nn.Module):
        def forward(self, x, t, which=None, to=None):
            ctx, y = conds[which]
            return model(x, timesteps=t, context=ctx, y=y, transformer_options=to)
    betas = torch.linspace(0.00085 ** 0.5, 0.012 ** 0.5, 1000, dtype=torch.float64) ** 2
    den = external.DiscreteEpsDDPMDenoiser(Eps(), torch.cumprod(1.0 - betas, dim=0).float().cuda(), quantize=True)

    def run(which, flag, x, sigma, mo):
        to = dict(mo.get('transformer_options', {}))
        to['cond_or_uncond'] = [flag]
        return den(x, sigma, which=which, to=to)

    def calc(model_, cond, uncond, x, sigma, mo):
        c = run(cond[0]['k'], 0, x, sigma, mo)
        u = run(uncond[0]['k'], 1, x, sigma, mo) if uncond is not None else torch.zeros_like(x)
        return c, u
    sf.calc_cond_uncond_batch = calc

    fo_spec = importlib.util.spec_from_file_location('freeu_oracle', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'freeu-oracle.py'))
    fo = importlib.util.module_from_spec(fo_spec)
    fo_spec.loader.exec_module(fo)

    sys.path.insert(0, os.path.join(EXT, 'sd_forge_dynamic_thresholding'))
    from lib_dynamic_thresholding.dynthres import DynamicThresholdingNode

    def build(freeu, sag, pag, dt=None):
        mo = {'transformer_options': {}}
        cap = {}
        stub = types.SimpleNamespace()
        stub.clone = lambda: stub
        stub.model = types.SimpleNamespace(diffusion_model=types.SimpleNamespace(config={'model_channels': 320}), predictor=types.SimpleNamespace(timestep=lambda s: den.sigma_to_t(s)))
        stub.set_model_sampler_cfg_function = lambda f, disable_cfg1_optimization=False: mo.__setitem__('sampler_cfg_function', f)
        stub.set_model_output_block_patch = lambda p: mo['transformer_options'].setdefault('patches', {}).setdefault('output_block_patch', []).append(p)
        stub.set_model_attn1_replace = lambda p, b, n, i: mo['transformer_options'].setdefault('patches_replace', {}).setdefault('attn1', {}).__setitem__((b, n, i), p)
        stub.set_model_sampler_post_cfg_function = lambda p, disable_cfg1_optimization=False: mo.setdefault('sampler_post_cfg_function', []).append(p)
        if dt is not None:
            DynamicThresholdingNode().patch(stub, dt[0], dt[1], 'Constant', 0.0, 'Constant', 0.0, 1.0, 'enable', 'MEAN', 'AD', 1.0)
        if freeu:
            fo.lift()(stub, *FREEU)
        if sag:
            env = lift(os.path.join(EXT, 'sd_forge_sag', 'scripts', 'forge_sag.py'), ('attention_basic_with_sim', 'create_blur_map', 'gaussian_blur_2d'), ('SelfAttentionGuidance',),
                       {'torch': torch, 'math': math, 'einsum': torch.einsum, 'rearrange': rearrange, 'repeat': repeat, 'attention': attention,
                        'attn_precision': memory_management.force_upcast_attention_dtype(), 'shared': types.SimpleNamespace(), 'calc_cond_uncond_batch': calc})
            env['SelfAttentionGuidance']().patch(stub, *SAG)
        if pag:
            env = lift(os.path.join(EXT, 'sd_forge_perturbed_attention', 'scripts', 'forge_perturbed_attention.py'), (), ('PerturbedAttentionGuidanceForForge',),
                       {'scripts': types.SimpleNamespace(Script=object, AlwaysVisible=True), 'gr': None, 'set_model_options_patch_replace': set_model_options_patch_replace, 'calc_cond_uncond_batch': calc})
            p = types.SimpleNamespace(sd_model=types.SimpleNamespace(forge_objects=types.SimpleNamespace(unet=stub)), extra_generation_params={})
            env['PerturbedAttentionGuidanceForForge']().process_before_every_sampling(p, True, PAG)
        return mo

    x = torch.tensor([latent(i) for i in range(4 * H * W)], dtype=torch.float32).reshape(1, 4, H, W).cuda()
    sigma = torch.tensor([SIGMA], dtype=torch.float32).cuda()
    arms = [('plain', False, False, False, None), ('freeu', True, False, False, None), ('sag', False, True, False, None), ('pag', False, False, True, None), ('all', True, True, True, None),
            ('dt', False, False, False, (2.0, 1.0)), ('dtall', True, True, True, (2.0, 0.95))]
    lines = ['Chapter: SdxlGuidanceReference', '',
             ' Generated by build/sdxl-guidance-oracle.py from %s: one guided SDXL' % os.path.basename(ckpt),
             ' denoiser call through Forge\'s sampling_function_inner at CFG %s, sigma %s, with' % (CFG, SIGMA),
             ' FreeU %s, SAG %s, PAG %s and dynamic thresholding (mimic 2; percentile 1, or 0.95' % (FREEU, SAG, PAG),
             ' with all three) as Forge\'s extensions install them. Regenerate, do not edit.', '',
             ' We say:', '', 'Section: Arms', '']
    for name, fu, sg, pg, dtp in arms:
        mo = build(fu, sg, pg, dtp)
        with torch.no_grad():
            d = sf.sampling_function_inner(model, x, sigma, [{'k': 'u'}], [{'k': 'c'}], CFG, mo)
        v = d.reshape(-1).float().cpu().numpy().astype(np.float64)
        lines += ['  sgr-%s : List Real' % name, '  sgr-%s = [%s]' % (name, ', '.join(real(t) for t in v)), '']
        print(name, 'rms', math.sqrt((v * v).mean()))
    lines += ['Page 1', '']
    open(out, 'w', newline='\r\n').write('\n'.join(lines))

main()
