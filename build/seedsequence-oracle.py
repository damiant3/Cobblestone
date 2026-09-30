# seedsequence-oracle.py -- the reference for codex/test/apps/diffusion-seedsequence:
# numpy's own SeedSequence (the numpy Diffusion Forge ships, 1.26), as
# torchsde's Brownian interval calls it. Prints one line per case, the
# format codex/test/apps/diffusion-seedsequence prints, so its .expected is
# this script's output with the test's own sabotage line appended.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/seedsequence-oracle.py

import numpy as np

CASES = [
    (0, (), 4, 4), (1, (), 4, 4), (12345, (), 8, 8), (4294967301, (), 8, 4),
    (20260929, (0, 1), 8, 4), (20260929, (1, 1), 8, 4), (20260929, (5, 3), 8, 4),
    (4294967295, (123456, 20), 8, 4), (7, (2, 1, 9), 4, 6), (9223372036854775807, (3, 2), 8, 4),
]

for entropy, key, pool, n in CASES:
    s = np.random.SeedSequence(entropy=entropy, spawn_key=key, pool_size=pool).generate_state(n)
    print(f"entropy {entropy} key {list(key)} pool {pool}: {' '.join(str(int(v)) for v in s)}")
