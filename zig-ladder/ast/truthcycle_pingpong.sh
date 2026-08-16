#!/bin/bash
# Stage 2 of the fixed point: compile stage 1's emitted text and emit again.
# text.truth == pingpong.truth is the whole claim.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm pingpong
