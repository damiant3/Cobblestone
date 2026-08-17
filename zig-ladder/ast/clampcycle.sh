#!/bin/bash
# Clamp-attribution zig arm: the same emission through the plug, diffed.
set -e
. "$(dirname "$0")/oracle_lib.sh"
$(arm_for clamp) clamp
