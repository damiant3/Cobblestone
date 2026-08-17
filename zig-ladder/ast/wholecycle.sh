#!/bin/bash
# Whole-compiler zig arm: the same emission through the plug, diffed.
set -e
. "$(dirname "$0")/oracle_lib.sh"
$(arm_for whole) whole
