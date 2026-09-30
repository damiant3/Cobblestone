# brownian-oracle.py -- the reference for the SDE samplers' noise: Diffusion
# Forge's own k-diffusion BrownianTreeNoiseSampler (k_diffusion/sampling.py)
# over torchsde's BrownianTree on the CUDA device, as
# modules/sd_samplers_common.py:340-348 builds it: one tree per image seed,
# from sigma_min to sigma_max of the schedule.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/brownian-oracle.py <out dir>
#
# The queries are the samplers' own: sample_dpmpp_sde over SDXL's Karras
# sigmas at 6 steps and sample_dpmpp_2m_sde over its Exponential sigmas at 6
# steps (Forge's Prediction sigmas, as build/sampler-oracle.py builds them),
# each run with a model answering zeros and a fresh tree, recording every
# call. DPM++ SDE asks sigma_fn(t_fn(sigma)), not sigma, so its query times
# carry the f32 log and exp round trip.
#
# Writes into <out dir>, little-endian:
#   brownian-sigmas.bin   f32 x 7 x 2: the Karras sigmas then the Exponential,
#                         each with its trailing 0
#   brownian-queries.bin  f32 x 2 x 15: each call's (sigma, sigma_next), the
#                         10 DPM++ SDE calls then the 5 DPM++ 2M SDE calls
#   brownian-noise.bin    f32 x 15 x 256: the draws of shape (4, 8, 8), seed
#                         20260929, in the same order
# and prints each call's (sigma, sigma_next).

import os
import sys

sys.path.insert(0, r'D:\AI\DiffusionForge\webui')
sys.path.insert(0, r'D:\AI\DiffusionForge\webui\packages_3rdparty')

import numpy as np
import torch
from k_diffusion import sampling
from k_diffusion.sampling import BrownianTreeNoiseSampler
from backend.modules.k_prediction import Prediction


def record(x, sigmas, queries, draws):
    smin, smax = sigmas[sigmas > 0].min(), sigmas.max()
    ns = BrownianTreeNoiseSampler(x, smin, smax, seed=[20260929])

    def call(a, b):
        w = ns(a, b)
        queries.append((a.float().item(), b.float().item()))
        draws.append(w.cpu().numpy().astype('<f4').ravel())
        return w
    return call


def main():
    out = sys.argv[1]
    pred = Prediction(prediction_type='epsilon', beta_schedule='linear', linear_start=0.00085, linear_end=0.012, timesteps=1000)
    smin, smax = pred.sigmas[0].item(), pred.sigmas[-1].item()
    x = torch.zeros((1, 4, 8, 8), device='cuda', dtype=torch.float32)
    model = lambda x, sigma, **kw: torch.zeros_like(x)
    queries, draws = [], []
    karras = sampling.get_sigmas_karras(6, smin, smax).to('cuda')
    sampling.sample_dpmpp_sde(model, x.clone(), karras, disable=True, noise_sampler=record(x, karras, queries, draws))
    expo = sampling.get_sigmas_exponential(6, smin, smax).to('cuda')
    sampling.sample_dpmpp_2m_sde(model, x.clone(), expo, disable=True, noise_sampler=record(x, expo, queries, draws))
    for a, b in queries:
        print(f'{a:.9g} {b:.9g}')
    with open(os.path.join(out, 'brownian-sigmas.bin'), 'wb') as f:
        f.write(torch.cat([karras, expo]).float().cpu().numpy().astype('<f4').tobytes())
    with open(os.path.join(out, 'brownian-queries.bin'), 'wb') as f:
        f.write(np.array(queries, dtype='<f4').tobytes())
    with open(os.path.join(out, 'brownian-noise.bin'), 'wb') as f:
        f.write(np.concatenate(draws).tobytes())
    print(f'wrote {len(draws)} draws')
    return 0


if __name__ == '__main__':
    sys.exit(main())
