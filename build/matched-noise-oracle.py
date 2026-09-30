# matched-noise-oracle.py -- Forge's outpainting mk2 get_matched_noise over
# formula inputs, written as the file codex/test/apps/diffusion-matched-noise
# grades apps/diffusion/MatchedNoise against: noise.ref, little-endian f64,
# case A then case B, each the H x W x 3 result in C order.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/matched-noise-oracle.py <out dir>
#
# get_matched_noise runs as Forge's own text, compiled from
# scripts/outpainting_mk_2.py, over numpy 1.26 and skimage 0.21. Case A: 64 x
# 128, the left 32 columns masked, noise_q 1, color variation 0.05. Case B:
# the same picture, the right 24 columns and the bottom 16 rows masked,
# noise_q 1.5, color variation 0.3.
import ast, os, struct, sys
import numpy as np
import skimage

WEBUI = r'D:\AI\DiffusionForge\webui'
H, W = 64, 128


def forge_function(rel, name, env):
    src = open(os.path.join(WEBUI, rel), encoding='utf-8').read()
    for node in ast.parse(src).body:
        if isinstance(node, ast.FunctionDef) and node.name == name:
            exec(compile(ast.Module([node], []), rel, 'exec'), env)
            return env[name]
    sys.exit('REFUSE: no %s in %s' % (name, rel))


def picture():
    return np.array([[[(x * 7 + y * 13 + c * 50 + (x * y) % 23) % 256 for c in range(3)] for x in range(W)] for y in range(H)], dtype=np.uint8)


def mask(case):
    m = np.zeros((H, W, 3))
    if case == 0:
        m[:, :32, :] = 1.0
    else:
        m[:, W - 24:, :] = 1.0
        m[H - 16:, :, :] = 1.0
    return m


def main():
    out = sys.argv[1]
    matched = forge_function(r'scripts\outpainting_mk_2.py', 'get_matched_noise', {'np': np, 'skimage': skimage})
    vals = []
    for case, (q, cv) in enumerate([(1.0, 0.05), (1.5, 0.3)]):
        src = (np.asarray(picture()) / 255.0).astype(np.float64)
        r = matched(src, mask(case), q, cv)
        vals += [float(v) for v in r.reshape(-1)]
    os.makedirs(out, exist_ok=True)
    open(os.path.join(out, 'noise.ref'), 'wb').write(struct.pack('<%dd' % len(vals), *vals))
    print('reals', len(vals))


if __name__ == '__main__':
    main()
