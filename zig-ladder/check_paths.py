#!/usr/bin/env python3
"""Assert the ladder can find everything it needs, without running a rung.

This exists so a path refactor can be cleared in seconds instead of in hour two
of a sweep. Every script here used to derive its roots by counting directories
up from itself, and the two roots it might have wanted -- the ladder's own tree
and the Codex checkout -- happened to be one level apart, so the count silently
carried the answer and the scripts already disagreed about it. Moving the ladder
out of the checkout makes the two unrelated, and every site has to say which one
it meant.

Getting that classification wrong is the whole risk of the move, and it is not a
risk worth discovering from a red rung: a rung reports a diff, and a diff does
not say "you resolved against the wrong root". So the classification is checked
directly. Each thing the ladder opens is named here with the root it belongs to;
if a path does not resolve, this says which one and why in one line.

The second half is the completeness check. A conversion that missed a site is
indistinguishable from a finished one until that site runs, so a leftover
level-count is reported as a failure in its own right rather than left to be
noticed later.
"""

import pathlib
import re
import sys

from ladder_root import CODEX, LADDER

# What a checkout has to provide. Tracked in the Codex repository and used
# unmodified; the ladder patches none of it.
FROM_CHECKOUT = [
    ('codex/compiler/opening.codex', 'the compiler driver, and the marker that says this IS a checkout'),
    ('seed/Codex.cdx', 'the seed: the trusted binary every measurement is compared against'),
    ('build/concat-codex-self.ps1', 'used by recon.sh to concatenate the compiler'),
    ('codex/plugs/common/plug-build-lib.ps1', "the author's bundler library, sourced by every bundle_*.ps1"),
    ('codex/plugs/zig/ZigEmitter.codex', 'the plug under test'),
    ('codex/test/plug-oracle-arith.codex', "the clamp rung's subject"),
]

# Products of the author's gated PowerShell build, NOT tracked. A fresh clone
# does not have them, and the rungs that need them would otherwise fail on a
# missing file that names nothing.
FROM_GATED_BUILD = [
    ('codex/plugs/zig/build-output/zig-plug.cdx', 'the TCP arm pushes IR through this'),
    ('codex/plugs/zig/build-output/plug-source.codex', 'the bundled plug source'),
]

# The ladder's own tree.
FROM_LADDER = [
    ('ast/emit_harness.py', 'the shared harness template'),
    ('ast/oracle_lib.sh', 'the rung machinery'),
    ('ast/allcycles.sh', 'the sweep'),
    ('codex_vm.py', 'the QEMU driver'),
    ('ring_compile.py', 'the ring transport'),
]

# Level-counting, in any of the three languages the ladder is written in. A
# match is a site the move would break, or has already broken.
LEFTOVERS = [
    (r'\.parent\.parent', 'python: counts directories up from __file__'),
    (r'dirname[^\n]*\.\.', 'shell: counts directories up from BASH_SOURCE'),
    (r"PSScriptRoot[^\n]*'\.\.'", 'powershell: counts directories up from PSScriptRoot'),
]

SKIP = {'ladder_root.py', 'check_paths.py'}


def check_group(entries, root, label, hint=None):
    bad = []
    for rel, why in entries:
        path = root / rel
        if not path.exists():
            bad.append(f'  MISSING {rel}\n    ({why})' + (f'\n    {hint}' if hint else ''))
    print(f'{label}: {len(entries) - len(bad)}/{len(entries)}')
    return bad


def check_leftovers():
    bad = []
    for path in sorted(LADDER.rglob('*')):
        if path.suffix not in {'.py', '.sh', '.ps1'} or path.name in SKIP:
            continue
        try:
            text = path.read_text(errors='replace')
        except OSError:
            continue
        for pattern, why in LEFTOVERS:
            for m in re.finditer(pattern, text):
                line = text[:m.start()].count('\n') + 1
                bad.append(f'  {path.relative_to(LADDER)}:{line}  {why}')
    return bad


def main():
    print(f'CODEX  {CODEX}')
    print(f'LADDER {LADDER}\n')

    failures = []
    failures += check_group(FROM_CHECKOUT, CODEX, 'from the checkout (tracked)')
    failures += check_group(
        FROM_GATED_BUILD, CODEX, 'from the checkout (built, not tracked)',
        hint="run the author's build/build.ps1 once; this is a product, not source")
    failures += check_group(FROM_LADDER, LADDER, 'from the ladder')

    left = check_leftovers()
    print(f'unconverted root derivations: {len(left)}')

    if failures:
        print('\nUNRESOLVED PATHS:')
        print('\n'.join(failures))
    if left:
        print('\nSITES STILL COUNTING DIRECTORIES:')
        print('\n'.join(left))

    if failures or left:
        print('\nFAIL')
        return 1
    print('\nOK: every path resolves and nothing counts directories')
    return 0


if __name__ == '__main__':
    sys.exit(main())
