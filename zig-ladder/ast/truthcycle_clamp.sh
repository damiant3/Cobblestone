#!/bin/bash
# Clamp-attribution truth arm: the whole compiler, compiling plug-oracle-arith.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm clamp
