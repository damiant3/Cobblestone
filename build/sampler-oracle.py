# sampler-oracle.py -- the reference for codex/test/apps/diffusion-sampler:
# Diffusion Forge's own k-diffusion samplers (k_diffusion/sampling.py
# sample_euler and sample_dpmpp_sde) and Karras schedule over SDXL's discrete
# sigmas (backend/modules/k_prediction.py Prediction, linear_start 0.00085,
# linear_end 0.012, 1000 steps), driven by an analytic denoiser: the posterior
# mean of x0 ~ N(mu, s^2) seen through Gaussian noise of scale sigma,
# D(x, sigma) = (s^2 x + sigma^2 mu) / (s^2 + sigma^2).
#
#   D:\AI\DiffusionForge\system\python\python.exe build/sampler-oracle.py <out dir>
#
# Every noise draw a sampler takes is recorded, so the guest can feed its own
# sampler the same draws. Writes into <out dir>, all little-endian:
#   sampler-sigmas.bin   f64 x (STEPS + 1): Forge's Karras sigmas, trailing 0
#   sampler-mu.bin       f32 x N: mu
#   sampler-x0.bin       f32 x N: the starting latent, noise times sigma_max
#   sampler-noise.bin    f32 x 2 (STEPS - 1) N: DPM++ SDE's draws, in order (the
#                        last step is an Euler step and draws nothing)
#   sampler-euler.bin    f32 x N: sample_euler's result
#   sampler-dpmpp.bin    f32 x N: sample_dpmpp_sde's result
#   sampler-euler-a.bin, sampler-dpmpp-2m.bin, sampler-dpmpp-2m-sde.bin
#                        f32 x N: sample_euler_ancestral, sample_dpmpp_2m and
#                        sample_dpmpp_2m_sde (midpoint) on the same sigmas
#   sampler-heun.bin     f32 x N: sample_heun, which draws nothing
#   sampler-dpm2.bin     f32 x N: sample_dpm_2, which draws nothing
#   sampler-dpm2a.bin, sampler-noise-dpm2a.bin
#                        f32 x N and x (STEPS - 1) N: sample_dpm_2_ancestral and its draws
#   sampler-dpmpp-2s-a.bin, sampler-noise-dpmpp-2s-a.bin
#                        the same for sample_dpmpp_2s_ancestral
#   sampler-dpmpp-2m-sde-heun.bin, sampler-noise-dpmpp-2m-sde-heun.bin
#                        the same for sample_dpmpp_2m_sde, solver_type heun
#   sampler-dpmpp-3m-sde.bin, sampler-noise-dpmpp-3m-sde.bin
#                        the same for sample_dpmpp_3m_sde
#   sampler-noise-euler-a.bin, sampler-noise-dpmpp-2m-sde.bin
#                        f32 x (STEPS - 1) N: their draws, taken after the above
#   sampler-lcm.bin, sampler-ddpm.bin, sampler-dpm-fast.bin,
#   sampler-dpm-adaptive.bin, sampler-restart-20.bin, sampler-restart-36.bin
#                        f32 x N: LCM, DDPM, DPM fast and adaptive (eta 1) and
#                        Restart at 20 and 36 steps, each with its
#                        sampler-noise-*.bin draws
#   sampler-mu-uncond.bin, sampler-ddim-cfgpp.bin
#                        f32 x N: the unconditional mean and ddim_cfgpp at CFG 3
#   sched-lcm.bin        f64 x (STEPS + 1): the LCM wrapper's get_sigmas
#   sched-exponential.bin, sched-sgm-uniform.bin, sched-simple.bin,
#   sched-normal.bin, sched-ddim.bin, sched-polyexponential.bin, sched-uniform.bin,
#   sched-turbo-6.bin, sched-turbo-16.bin
#                        f64 x (STEPS + 1): Forge's own schedulers
#                        (modules/sd_schedulers.py) for STEPS steps
#   sampler-ipndm.bin, sampler-ipndm-v.bin, sampler-heunpp2.bin, sampler-deis.bin
#                        f32 x N: those samplers from x0, drawing nothing
#   sampler-plms.bin     f32 x N: the timestep plms from xt
#   sched-kl-optimal.bin, sched-beta.bin, sched-align-your-steps.bin,
#   sched-align-your-steps-gits.bin, sched-align-your-steps-11.bin,
#   sched-align-your-steps-32.bin, sched-ays-sd15.bin, sched-ays-gits-sd15.bin,
#   sched-ays-32-sd15.bin (SD1.5's tables),
#   sched-ays-11-steps.bin (11 steps, the table itself), sched-ays-8-steps.bin
#                        f64: the schedulers that need no model
# and prints sigma_min, sigma_max and the sigmas.
import os, sys
import torch

sys.path.insert(0, r'D:\AI\DiffusionForge\webui')
sys.path.insert(0, r'D:\AI\DiffusionForge\webui\packages_3rdparty')
from k_diffusion import sampling
from backend.modules.k_prediction import Prediction

out = sys.argv[1]
os.makedirs(out, exist_ok=True)

STEPS, N, S2 = 6, 1024, 0.25
pred = Prediction(prediction_type='epsilon', beta_schedule='linear', linear_start=0.00085, linear_end=0.012, timesteps=1000)
smin, smax = pred.sigmas[0].item(), pred.sigmas[-1].item()
sigmas = sampling.get_sigmas_karras(STEPS, smin, smax)

g = torch.Generator().manual_seed(20260929)
mu = torch.randn(N, generator=g) * 0.8
x0 = torch.randn(N, generator=g) * sigmas[0]

def model(x, sigma, **kw):
    s = sigma.reshape(-1)[0].double()
    return ((S2 * x.double() + s * s * mu.double().reshape(x.shape)) / (S2 + s * s)).float()

draws = []
def noise_sampler(sigma, sigma_next):
    z = torch.randn(N, generator=g)
    draws.append(z)
    return z

xe = sampling.sample_euler(model, x0.clone()[None], sigmas, disable=True)[0]
xd = sampling.sample_dpmpp_sde(model, x0.clone()[None], sigmas, disable=True, noise_sampler=noise_sampler)[0]

# Stage 6: more samplers on the same sigmas, each fed recorded draws taken after
# the draws above, so the files above do not move.
def recorder(store):
    def ns(sigma, sigma_next):
        z = torch.randn(N, generator=g)
        store.append(z)
        return z
    return ns

draws_ea, draws_2msde = [], []
xea = sampling.sample_euler_ancestral(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_ea))[0]
x2m = sampling.sample_dpmpp_2m(model, x0.clone()[None], sigmas, disable=True)[0]
x2ms = sampling.sample_dpmpp_2m_sde(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_2msde))[0]
xheun = sampling.sample_heun(model, x0.clone()[None], sigmas, disable=True)[0]
xdpm2 = sampling.sample_dpm_2(model, x0.clone()[None], sigmas, disable=True)[0]

# Forge's own schedulers (modules/sd_schedulers.py) over the model wrap Forge
# builds (k_diffusion.external.ForgeScheduleLinker on the predictor). The module
# reads modules.shared only inside functions this does not call.
import types
sys.modules['modules.shared'] = types.ModuleType('modules.shared')
from modules import sd_schedulers
from k_diffusion.external import ForgeScheduleLinker
wrap = ForgeScheduleLinker(pred)
sched = {x.name: x for x in sd_schedulers.schedulers}
def schedule(name):
    s = sched[name]
    kw = {'inner_model': wrap} if s.need_inner_model else {}
    return s.function(n=STEPS, sigma_min=smin, sigma_max=smax, device='cpu', **kw)
sched_out = {k: schedule(k) for k in ('exponential', 'sgm_uniform', 'simple', 'normal', 'ddim', 'polyexponential', 'uniform')}
# turbo reads the predictor through inner_model.inner_model.forge_objects.unet.model.
turbo_inner = types.SimpleNamespace(inner_model=types.SimpleNamespace(forge_objects=types.SimpleNamespace(unet=types.SimpleNamespace(model=types.SimpleNamespace(predictor=pred)))))
for tn in (STEPS, 16):
    sched_out['turbo-%d' % tn] = sd_schedulers.turbo_scheduler(tn, smin, smax, turbo_inner, 'cpu')

# The timestep samplers (modules/sd_samplers_timesteps_impl.py) through Forge's
# eps estimation: CFGDenoiser.forward with classic_ddim_eps_estimation scales x
# by sqrt (sigma^2 + 1) for the timestep's sigma and answers (x - D) / sigma;
# alphas_cumprod is CompVisTimestepsDenoiser's 1 / (sigma^2 + 1). Timesteps are
# CompVisSampler.get_timesteps', and the start is a fresh unit-variance draw.
from modules import sd_samplers_timesteps_impl as ts_impl
class Inner: pass
class EpsModel:
    def __init__(self):
        self.inner_model = Inner()
        self.inner_model.inner_model = Inner()
        self.inner_model.inner_model.alphas_cumprod = 1.0 / (pred.sigmas ** 2.0 + 1.0)
    def __call__(self, x, t, **kw):
        acd = self.inner_model.inner_model.alphas_cumprod
        fake = ((1 - acd) / acd) ** 0.5
        sigma = fake[t.round().long().clip(0, int(fake.shape[0]))]
        b = (-1,) + (1,) * (x.dim() - 1)
        xs = x * ((sigma ** 2.0 + 1.0) ** 0.5).view(b)
        return (xs - model(xs, sigma)) / sigma.view(b)
timesteps = torch.clip(torch.asarray(list(range(0, 1000, 1000 // STEPS))) + 1, 0, 999)
xt = torch.randn(N, generator=g)
xddim = ts_impl.ddim(EpsModel(), xt.clone()[None], timesteps, disable=True)[0]
# unipc reads its options from shared.opts; these are Forge's defaults
# (modules/shared_options.py).
ts_impl.shared.opts = types.SimpleNamespace(uni_pc_variant='bh1', uni_pc_skip_type='time_uniform', uni_pc_order=3, uni_pc_lower_order_final=True)
xunipc = ts_impl.unipc(EpsModel(), xt.clone().reshape(1, 4, 16, 16), timesteps, extra_args={}, callback=lambda d: None, disable=True).reshape(-1)
# Draw-taking samplers added after the timestep ones, so xt and every draw above keep their place.
draws_dpm2a = []
xdpm2a = sampling.sample_dpm_2_ancestral(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_dpm2a))[0]
draws_2sa = []
x2sa = sampling.sample_dpmpp_2s_ancestral(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_2sa))[0]
draws_2msh = []
x2msh = sampling.sample_dpmpp_2m_sde(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_2msh), solver_type='heun')[0]
draws_3m = []
x3m = sampling.sample_dpmpp_3m_sde(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_3m))[0]

def put(name, t, dt):
    t.to(dt).numpy().astype('<f8' if dt == torch.float64 else '<f4').tofile(os.path.join(out, name))

put('sampler-sigmas.bin', sigmas, torch.float64)
put('sampler-mu.bin', mu, torch.float32)
put('sampler-x0.bin', x0, torch.float32)
put('sampler-noise.bin', torch.cat([d.reshape(-1) for d in draws]), torch.float32)
put('sampler-euler.bin', xe, torch.float32)
put('sampler-dpmpp.bin', xd, torch.float32)
put('sampler-euler-a.bin', xea, torch.float32)
put('sampler-noise-euler-a.bin', torch.cat([d.reshape(-1) for d in draws_ea]), torch.float32)
put('sampler-dpmpp-2m.bin', x2m, torch.float32)
put('sampler-dpmpp-2m-sde.bin', x2ms, torch.float32)
put('sampler-noise-dpmpp-2m-sde.bin', torch.cat([d.reshape(-1) for d in draws_2msde]), torch.float32)
put('sampler-heun.bin', xheun, torch.float32)
put('sampler-dpm2.bin', xdpm2, torch.float32)
put('sampler-dpm2a.bin', xdpm2a, torch.float32)
put('sampler-noise-dpm2a.bin', torch.cat([d.reshape(-1) for d in draws_dpm2a]), torch.float32)
put('sampler-dpmpp-2s-a.bin', x2sa, torch.float32)
put('sampler-noise-dpmpp-2s-a.bin', torch.cat([d.reshape(-1) for d in draws_2sa]), torch.float32)
put('sampler-dpmpp-2m-sde-heun.bin', x2msh, torch.float32)
put('sampler-noise-dpmpp-2m-sde-heun.bin', torch.cat([d.reshape(-1) for d in draws_2msh]), torch.float32)
put('sampler-dpmpp-3m-sde.bin', x3m, torch.float32)
put('sampler-noise-dpmpp-3m-sde.bin', torch.cat([d.reshape(-1) for d in draws_3m]), torch.float32)
for k, v in sched_out.items():
    put('sched-%s.bin' % k.replace('_', '-'), v, torch.float64)
    print(k, ' '.join('%.9g' % t for t in v.tolist()))
print('draws euler a %d, dpm++ 2m sde %d' % (len(draws_ea), len(draws_2msde)))
put('sampler-xt.bin', xt, torch.float32)
put('sampler-ddim.bin', xddim, torch.float32)
put('sampler-unipc.bin', xunipc, torch.float32)
xlms = sampling.sample_lms(model, x0.clone()[None], sigmas, disable=True)[0]
put('sampler-lms.bin', xlms, torch.float32)
# Deterministic rows: none draws from g, so every file above keeps its bytes.
# sample_heunpp2's randn_like reads torch's global generator and is multiplied
# by a zero churn.
put('sampler-ipndm.bin', sampling.sample_ipndm(model, x0.clone()[None], sigmas, disable=True)[0], torch.float32)
put('sampler-ipndm-v.bin', sampling.sample_ipndm_v(model, x0.clone()[None], sigmas, disable=True)[0], torch.float32)
put('sampler-heunpp2.bin', sampling.sample_heunpp2(model, x0.clone()[None], sigmas, disable=True)[0], torch.float32)
put('sampler-deis.bin', sampling.sample_deis(model, x0.clone()[None], sigmas, disable=True)[0], torch.float32)
from k_diffusion import deis
print('deis coeffs', [[float(c) for c in row] for row in deis.get_deis_coeff_list(sigmas, 3)])
xplms = ts_impl.plms(EpsModel(), xt.clone().reshape(1, 4, 16, 16), timesteps, disable=True).reshape(-1)
put('sampler-plms.bin', xplms, torch.float32)
# The schedulers that read shared: Beta's alpha and beta are Forge's defaults
# (modules/shared_options.py), and Align Your Steps picks its table by is_sdxl.
sd_schedulers.shared.opts = types.SimpleNamespace(beta_dist_alpha=0.6, beta_dist_beta=0.6)
sd_schedulers.shared.sd_model = types.SimpleNamespace(is_sdxl=True)
sched_more = {k: schedule(k) for k in ('kl_optimal', 'beta', 'align_your_steps', 'align_your_steps_GITS', 'align_your_steps_11', 'align_your_steps_32')}
sched_more['ays-11-steps'] = sd_schedulers.get_align_your_steps_sigmas(11, smin, smax, 'cpu')
# 6 steps land on the 11-point table's knots; 8 interpolate between them.
sched_more['ays-8-steps'] = sd_schedulers.get_align_your_steps_sigmas(8, smin, smax, 'cpu')
sd_schedulers.shared.sd_model = types.SimpleNamespace(is_sdxl=False)
sched_more['ays-sd15'] = schedule('align_your_steps')
sched_more['ays-gits-sd15'] = schedule('align_your_steps_GITS')
sched_more['ays-32-sd15'] = schedule('align_your_steps_32')
for k, v in sched_more.items():
    put('sched-%s.bin' % k.replace('_', '-').lower(), v, torch.float64)
    print(k, ' '.join('%.9g' % t for t in v.tolist()))
print('unipc mean %.6f std %.6f' % (xunipc.mean(), xunipc.std()))
print('timesteps', timesteps.tolist(), 'ddim mean %.6f std %.6f' % (xddim.mean(), xddim.std()))
print('sigma_min %.9g sigma_max %.9g' % (smin, smax))
print('sigmas', ' '.join('%.9g' % v for v in sigmas.tolist()))
print('draws %d, euler mean %.6f std %.6f, dpmpp mean %.6f std %.6f' % (len(draws), xe.mean(), xe.std(), xd.mean(), xd.std()))

# LCM (modules/sd_samplers_lcm.py): sample_lcm on the Karras sigmas, and the
# LCM wrapper's own get_sigmas, which is what its Automatic schedule runs.
# Forge's sampling_function calls the UNet directly, so the wrapper's c_skip
# and c_out never apply.
# The module imports the whole webui, so only its two definitions are loaded.
import ast
from k_diffusion import utils as kd_utils
from k_diffusion.external import DiscreteEpsDDPMDenoiser
lcm_src = open(r'D:\AI\DiffusionForge\webui\modules\sd_samplers_lcm.py').read()
lcm_defs = [d for d in ast.parse(lcm_src).body if getattr(d, 'name', None) in ('LCMCompVisDenoiser', 'sample_lcm')]
sd_samplers_lcm = types.SimpleNamespace()
lcm_ns = {'torch': torch, 'utils': kd_utils, 'sampling': sampling, 'DiscreteEpsDDPMDenoiser': DiscreteEpsDDPMDenoiser, 'default_noise_sampler': sampling.default_noise_sampler, 'trange': sampling.trange}
exec(compile(ast.Module(body=lcm_defs, type_ignores=[]), 'sd_samplers_lcm.py', 'exec'), lcm_ns)
sd_samplers_lcm.sample_lcm, sd_samplers_lcm.LCMCompVisDenoiser = lcm_ns['sample_lcm'], lcm_ns['LCMCompVisDenoiser']
draws_lcm = []
xlcm = sd_samplers_lcm.sample_lcm(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_lcm))[0]
lcm_wrap = sd_samplers_lcm.LCMCompVisDenoiser(types.SimpleNamespace(forge_objects=types.SimpleNamespace(unet=types.SimpleNamespace(model=types.SimpleNamespace(predictor=pred)))))
put('sampler-lcm.bin', xlcm, torch.float32)
put('sampler-noise-lcm.bin', torch.cat([d.reshape(-1) for d in draws_lcm]), torch.float32)
put('sched-lcm.bin', lcm_wrap.get_sigmas(STEPS), torch.float64)
print('lcm schedule', ' '.join('%.9g' % v for v in lcm_wrap.get_sigmas(STEPS).tolist()))

# DDPM (backend/modules/k_diffusion_extra.py sample_ddpm).
from backend.modules import k_diffusion_extra
draws_ddpm = []
xddpm = k_diffusion_extra.sample_ddpm(model, x0.clone()[None], sigmas, disable=True, noise_sampler=recorder(draws_ddpm))[0]
put('sampler-ddpm.bin', xddpm, torch.float32)
put('sampler-noise-ddpm.bin', torch.cat([d.reshape(-1) for d in draws_ddpm]), torch.float32)

# Restart (modules/sd_samplers_extra.py) restarts only from 20 steps, and
# changes its restart shape at 36; it draws through torch.randn_like, so the
# draws are taken from g by replacing it for the run.
from modules import sd_samplers_extra
def restart(steps):
    store = []
    keep = torch.randn_like
    def rl(x):
        z = torch.randn(x.shape, generator=g)
        store.append(z.reshape(-1))
        return z
    torch.randn_like = rl
    try:
        xr = sd_samplers_extra.restart_sampler(model, x0.clone()[None], sampling.get_sigmas_karras(steps, smin, smax), disable=True)[0]
    finally:
        torch.randn_like = keep
    put('sampler-restart-%d.bin' % steps, xr, torch.float32)
    put('sampler-noise-restart-%d.bin' % steps, torch.cat(store) if store else torch.zeros(N), torch.float32)
    print('restart %d steps: %d draws' % (steps, len(store)))
for rs in (20, 36):
    restart(rs)

# DPM fast at Forge's eta, eta_ancestral 1 (modules/shared_options.py), over
# the model's own sigma_min and sigma_max for 6 steps.
draws_fast = []
xfast = sampling.sample_dpm_fast(model, x0.clone()[None], smin, smax, STEPS, disable=True, eta=1.0, noise_sampler=recorder(draws_fast))[0]
put('sampler-dpm-fast.bin', xfast, torch.float32)
put('sampler-noise-dpm-fast.bin', torch.cat([d.reshape(-1) for d in draws_fast]), torch.float32)
print('dpm fast draws %d' % len(draws_fast))

# DDIM CFG++ at eta_ddim 0 through the classic eps estimation with guidance:
# an unconditional posterior mean about mu_u, guided D = Du + CFG (Dc - Du),
# and last_noise_uncond = (x - Du) / sigma on the scaled x. Forge sets
# cond_scale_miltiplier and reads it nowhere, so the scale is the full CFG.
CFG = 3.0
mu_u = torch.randn(N, generator=g) * 0.8
def post(x, s, m):
    return (S2 * x.double() + s * s * m.double().reshape(x.shape)) / (S2 + s * s)
class CfgEpsModel(EpsModel):
    def __call__(self, x, t, **kw):
        acd = self.inner_model.inner_model.alphas_cumprod
        fake = ((1 - acd) / acd) ** 0.5
        sigma = fake[t.round().long().clip(0, int(fake.shape[0]))]
        b = (-1,) + (1,) * (x.dim() - 1)
        xs = x * ((sigma ** 2.0 + 1.0) ** 0.5).view(b)
        s = sigma.reshape(-1)[0].double()
        dc = post(xs, s, mu).float()
        du = post(xs, s, mu_u).float()
        den = du + (dc - du) * CFG
        self.last_noise_uncond = (xs - du) / sigma.view(b)
        return (xs - den) / sigma.view(b)
xcfgpp = ts_impl.ddim_cfgpp(CfgEpsModel(), xt.clone()[None], timesteps, disable=True)[0]
put('sampler-mu-uncond.bin', mu_u, torch.float32)
put('sampler-ddim-cfgpp.bin', xcfgpp, torch.float32)

# DPM adaptive at Forge's eta 1 and the function's own defaults (order 3, rtol
# 0.05, atol 0.0078, h_init 0.05, the I controller at safety 0.81); one draw
# per accepted step.
draws_ad = []
xad, info_ad = sampling.sample_dpm_adaptive(model, x0.clone()[None], smin, smax, disable=True, eta=1.0, noise_sampler=recorder(draws_ad), return_info=True)
put('sampler-dpm-adaptive.bin', xad[0], torch.float32)
put('sampler-noise-dpm-adaptive.bin', torch.cat([d.reshape(-1) for d in draws_ad]), torch.float32)
print('dpm adaptive', info_ad, 'draws %d' % len(draws_ad))
