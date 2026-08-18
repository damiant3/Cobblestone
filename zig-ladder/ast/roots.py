#!/usr/bin/env python3
"""The one place scripts in this directory reach the ladder's root module.

Python puts the running script's own directory on sys.path and nothing above
it, so a script in ast/ cannot import ladder_root without first saying where
the ladder root is -- and the only thing it knows is where it is. That step is
unavoidable. Doing it in twelve scripts is not, and twelve copies of a level
count is exactly the shape that let the old sites disagree about whether they
meant the ladder or the checkout.

So it happens once, here. Everything in ast/ asks this module, this module asks
ladder_root, and ladder_root is the only thing that knows where the checkout
is. The count below reaches the LADDER, which is fixed relative to this file
and moves with it; nothing here counts its way to the checkout.
"""

import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))  # ladder-root-bootstrap

from ladder_root import CODEX, LADDER, CodexRootError  # noqa: E402,F401

# ast/ itself, which every script here wants and several spelled by hand.
HERE = LADDER / 'ast'
