# freeu-step-oracle.py -- the reference for codex/test/apps/diffusion-freeu-steps:
# which sampling_step each model call of each Forge sampler sees, and whether
# sd_forge_freeu's own denoiser_callback (forge_freeu.py, run from its source)
# turns FreeU on for that call, for the window 0.3 to 0.7 and the default 0 to 1.
# Forge's callback_state (modules/sd_samplers_common.py) sets
# state.sampling_step to each callback's i and launch_sampling resets it to 0
# with sampling_steps = steps.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/freeu-step-oracle.py
#
# Reads build/sampler-oracle.py's fixtures under codex/test/gpu-files (DPM
# adaptive's attempt count depends on the data and its draws) and writes
# apps/diffusion/FreeUStepReference.codex, one line per sampler and step count:
# "<label> <steps> | <step seen per call> | <on, 0.3 to 0.7> | <on, 0 to 1>".
import os, sys, types, ast
import numpy as np
import torch

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
fix = os.path.join(root, 'codex', 'test', 'gpu-files')
sys.path.insert(0, r'D:\AI\DiffusionForge\webui')
sys.path.insert(0, r'D:\AI\DiffusionForge\webui\packages_3rdparty')
from k_diffusion import sampling
from backend.modules.k_prediction import Prediction
sys.modules['modules.shared'] = types.ModuleType('modules.shared')

pred = Prediction(prediction_type='epsilon', beta_schedule='linear', linear_start=0.00085, linear_end=0.012, timesteps=1000)
smin, smax = pred.sigmas[0].item(), pred.sigmas[-1].item()
N, S2 = 1024, 0.25
def f32(name):
    return torch.from_numpy(np.fromfile(os.path.join(fix, name), dtype='<f4').copy())
mu, x0f, xtf = f32('sampler-mu.bin'), f32('sampler-x0.bin'), f32('sampler-xt.bin')
ad_draws = f32('sampler-noise-dpm-adaptive.bin').reshape(-1, N)

# FreeUForForge.denoiser_callback, from Forge's file, on a stand-in class.
fsrc = open(r'D:\AI\DiffusionForge\webui\extensions-builtin\sd_forge_freeu\scripts\forge_freeu.py').read()
cls = [d for d in ast.parse(fsrc).body if getattr(d, 'name', None) == 'FreeUForForge'][0]
cbdef = [d for d in cls.body if getattr(d, 'name', None) == 'denoiser_callback'][0]
class FreeUForForge:
    doFreeU = True
ns_cb = {'FreeUForForge': FreeUForForge}
exec(compile(ast.Module(body=[cbdef], type_ignores=[]), 'forge_freeu.py', 'exec'), ns_cb)
denoiser_callback = ns_cb['denoiser_callback']
WINDOWS = [(0.3, 0.7), (0.0, 1.0)]

state = {'step': 0, 'steps': 0, 'seen': [], 'on': [[] for _ in WINDOWS]}
def cb(d):
    state['step'] = d['i']

def model(x, sigma, **kw):
    state['seen'].append(state['step'])
    for w, (a, b) in enumerate(WINDOWS):
        FreeUForForge.freeu_start, FreeUForForge.freeu_end = a, b
        denoiser_callback(None, types.SimpleNamespace(sampling_step=state['step'], total_sampling_steps=state['steps']))
        state['on'][w].append(1 if FreeUForForge.doFreeU else 0)
    s = sigma.reshape(-1)[0].double()
    return ((S2 * x.double() + s * s * mu.double().reshape(x.shape)) / (S2 + s * s)).float()

def ns(sigma, sigma_next):
    return torch.zeros(N)

from modules import sd_samplers_timesteps_impl as ts_impl
ts_impl.shared.opts = types.SimpleNamespace(uni_pc_variant='bh1', uni_pc_skip_type='time_uniform', uni_pc_order=3, uni_pc_lower_order_final=True)
class Inner: pass
class EpsModel:
    def __init__(self):
        self.inner_model = Inner()
        self.inner_model.inner_model = Inner()
        self.inner_model.inner_model.alphas_cumprod = 1.0 / (pred.sigmas ** 2.0 + 1.0)
        self.last_noise_uncond = None
    def __call__(self, x, t, **kw):
        acd = self.inner_model.inner_model.alphas_cumprod
        fake = ((1 - acd) / acd) ** 0.5
        sigma = fake[t.round().long().clip(0, int(fake.shape[0]))]
        b = (-1,) + (1,) * (x.dim() - 1)
        xs = x * ((sigma ** 2.0 + 1.0) ** 0.5).view(b)
        e = (xs - model(xs, sigma)) / sigma.view(b)
        self.last_noise_uncond = e
        return e

from k_diffusion import utils as kd_utils
from k_diffusion.external import DiscreteEpsDDPMDenoiser
lcm_src = open(r'D:\AI\DiffusionForge\webui\modules\sd_samplers_lcm.py').read()
lcm_defs = [d for d in ast.parse(lcm_src).body if getattr(d, 'name', None) == 'sample_lcm']
lcm_ns = {'torch': torch, 'utils': kd_utils, 'sampling': sampling, 'DiscreteEpsDDPMDenoiser': DiscreteEpsDDPMDenoiser, 'default_noise_sampler': sampling.default_noise_sampler, 'trange': sampling.trange}
exec(compile(ast.Module(body=lcm_defs, type_ignores=[]), 'sd_samplers_lcm.py', 'exec'), lcm_ns)
from backend.modules import k_diffusion_extra
from modules import sd_samplers_extra

def ks(steps, discard=False):
    s = sampling.get_sigmas_karras(steps + 1 if discard else steps, smin, smax)
    return torch.cat([s[:-2], s[-1:]]) if discard else s

def kd(fn, discard=False, **kw):
    def run(steps):
        s = ks(steps, discard)
        fn(model, x0f.clone()[None], s, callback=cb, disable=True, **kw)
    return run

def ts(fn, shape=None):
    def run(steps):
        t = torch.clip(torch.asarray(list(range(0, 1000, 1000 // steps))) + 1, 0, 999)
        x = xtf.clone()[None]
        if shape: x = x.reshape(shape)
        fn(EpsModel(), x, t, extra_args={}, callback=cb, disable=True)
    return run

def fast(steps):
    sampling.sample_dpm_fast(model, x0f.clone()[None], smin, smax, steps, callback=cb, disable=True, eta=1.0, noise_sampler=ns)

def adaptive(steps):
    it = iter(ad_draws)
    sampling.sample_dpm_adaptive(model, x0f.clone()[None], smin, smax, callback=cb, disable=True, eta=1.0, noise_sampler=lambda a, b: next(it))

COUNTS = [2, 3, 6, 7, 11]
samplers = [
    ('Euler', kd(sampling.sample_euler), COUNTS),
    ('DPM++ SDE', kd(sampling.sample_dpmpp_sde, noise_sampler=ns), COUNTS),
    ('DPM++ 2M', kd(sampling.sample_dpmpp_2m), COUNTS),
    ('DPM++ 2M SDE', kd(sampling.sample_dpmpp_2m_sde, noise_sampler=ns), COUNTS),
    ('DPM++ 2M SDE Heun', kd(sampling.sample_dpmpp_2m_sde, noise_sampler=ns, solver_type='heun'), COUNTS),
    ('DPM++ 2S a', kd(sampling.sample_dpmpp_2s_ancestral, noise_sampler=ns), COUNTS),
    ('DPM++ 3M SDE', kd(sampling.sample_dpmpp_3m_sde, discard=True, noise_sampler=ns), COUNTS),
    ('Euler a', kd(sampling.sample_euler_ancestral, noise_sampler=ns), COUNTS),
    ('LMS', kd(sampling.sample_lms), COUNTS),
    ('Heun', kd(sampling.sample_heun), COUNTS),
    ('DPM2', kd(sampling.sample_dpm_2, discard=True), COUNTS),
    ('DPM2 a', kd(sampling.sample_dpm_2_ancestral, discard=True, noise_sampler=ns), COUNTS),
    ('DPM fast', fast, COUNTS),
    ('DPM adaptive', adaptive, [6, 11]),
    ('Restart', kd(sd_samplers_extra.restart_sampler), COUNTS + [20, 36]),
    ('HeunPP2', kd(sampling.sample_heunpp2), COUNTS),
    ('IPNDM', kd(sampling.sample_ipndm), COUNTS),
    ('IPNDM_V', kd(sampling.sample_ipndm_v), COUNTS),
    ('DEIS', kd(sampling.sample_deis), COUNTS),
    ('DDIM', ts(ts_impl.ddim), COUNTS),
    ('DDIM CFG++', ts(ts_impl.ddim_cfgpp), COUNTS),
    ('PLMS', ts(ts_impl.plms, (1, 4, 16, 16)), COUNTS),
    ('UniPC', ts(ts_impl.unipc, (1, 4, 16, 16)), COUNTS[1:]),
    ('LCM', kd(lcm_ns['sample_lcm'], noise_sampler=ns), COUNTS),
    ('DDPM', kd(k_diffusion_extra.sample_ddpm, noise_sampler=ns), COUNTS),
]

lines = []
for label, run, counts in samplers:
    for steps in counts:
        state['step'], state['steps'], state['seen'], state['on'] = 0, steps, [], [[] for _ in WINDOWS]
        run(steps)
        line = '%s %d | %s' % (label, steps, ' '.join(str(v) for v in state['seen']))
        for w in state['on']:
            line += ' | ' + ''.join(str(v) for v in w)
        lines.append(line)
        print(line)

out = os.path.join(root, 'apps', 'diffusion', 'FreeUStepReference.codex')
with open(out, 'w', newline='\r\n') as f:
    f.write('Chapter: FreeUStepReference\n\n')
    f.write(' Written by build/freeu-step-oracle.py from Diffusion Forge\'s own samplers and\n')
    f.write(' sd_forge_freeu\'s denoiser_callback. Regenerate, do not edit.\n\n')
    f.write(' We say:\n\n')
    f.write('  fsr-lines : List Text = [%s]\n' % ', '.join('"%s"' % l for l in lines))
print('%d lines to %s' % (len(lines), out))
