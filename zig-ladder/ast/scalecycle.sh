#!/bin/bash
# Scale-milestone zig arm: the same emission through the plug, diffed.
set -e
. "$(dirname "$0")/oracle_lib.sh"
$(arm_for scale) scale
