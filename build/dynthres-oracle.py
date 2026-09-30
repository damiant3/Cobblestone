# dynthres-oracle.py -- the reference for dynamic thresholding (Forge's
# extensions-builtin/sd_forge_dynamic_thresholding): its own DynThresh.dynthresh
# (lib_dynamic_thresholding/dynthres_core.py), as the extension builds it
# (experiment_mode 0, max_steps 999), over the cond and uncond predictions built
# below, f32 on the CUDA device.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/dynthres-oracle.py <out dir>
#
# Writes <out dir>/dynthres.bin: per case in CASES order, the result 1 x 4 x 16 x 16,
# f32 little-endian. codex/test/apps/diffusion-dynthres repeats CASES.
import importlib.util, os, sys
import torch

SRC = r'D:\AI\DiffusionForge\webui\extensions-builtin\sd_forge_dynamic_thresholding\lib_dynamic_thresholding\dynthres_core.py'
MODES = ["Constant", "Linear Down", "Cosine Down", "Half Cosine Down", "Linear Up", "Cosine Up", "Half Cosine Up", "Power Up", "Power Down", "Linear Repeating", "Cosine Repeating", "Sawtooth"]
# mimic, percentile, mimic mode, mimic min, cfg mode, cfg min, sched, separate, ZERO, STD, phi, cfg, step
CASES = [(7.0, 1.0, 0, 0.0, 0, 0.0, 1.0, True, False, False, 1.0, 12.0, 300),
         (7.0, 0.95, 0, 0.0, 0, 0.0, 1.0, True, False, False, 1.0, 12.0, 300),
         (7.0, 0.9, 0, 0.0, 0, 0.0, 1.0, False, False, False, 1.0, 12.0, 300),
         (7.0, 1.0, 0, 0.0, 0, 0.0, 1.0, True, False, True, 1.0, 12.0, 300),
         (7.0, 1.0, 0, 0.0, 0, 0.0, 1.0, False, True, True, 1.0, 12.0, 300),
         (7.0, 0.97, 0, 0.0, 0, 0.0, 1.0, True, True, False, 0.7, 12.0, 300)] + \
        [(7.0, 1.0, m, 1.0, (m + 5) % 12, 0.5, 1.5, True, False, False, 1.0, 12.0, 400) for m in range(12)]

def main():
    spec = importlib.util.spec_from_file_location('dynthres_core', SRC)
    core = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(core)
    i = torch.arange(1024, dtype=torch.float64)
    cond = (((i * 37 + 11) % 257 - 128) / 64.0).to(torch.float32).reshape(1, 4, 16, 16).cuda()
    uncond = (((i * 53 + 17) % 263 - 131) / 64.0).to(torch.float32).reshape(1, 4, 16, 16).cuda()
    out = bytearray()
    for k, (mim, pct, mm, mmin, cm, cmin, sched, sep, zero, std, phi, cfg, step) in enumerate(CASES):
        d = core.DynThresh(mim, pct, MODES[mm], mmin, MODES[cm], cmin, sched, 0, 999, sep, 'ZERO' if zero else 'MEAN', 'STD' if std else 'AD', phi)
        d.step = step
        with torch.no_grad():
            r = d.dynthresh(cond, uncond, cfg, None)
        out += r.float().cpu().numpy().tobytes()
        print(k, float(r.abs().max()))
    with open(os.path.join(sys.argv[1], 'dynthres.bin'), 'wb') as f:
        f.write(bytes(out))
    return 0

if __name__ == '__main__':
    sys.exit(main())
