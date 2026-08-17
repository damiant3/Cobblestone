#!/bin/bash
# Stage 2 of the fixed point: compile stage 1's emitted text and emit again.
# text.truth == pingpong.truth is the whole claim, so it is checked here
# rather than asserted: pingpong_fixed_point is the test that comment was.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm pingpong
pingpong_fixed_point
