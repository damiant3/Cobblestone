#!/bin/bash
# Fibx-milestone zig arm: the same emission through the plug, diffed.
# Which transport fibx needs is arm_for's to say, not this script's -- it
# said zig_arm here for a while after the sweep had already moved fibx to
# the ring, so the two disagreed about what the rung even ran.
set -e
. "$(dirname "$0")/oracle_lib.sh"
$(arm_for fibx) fibx
