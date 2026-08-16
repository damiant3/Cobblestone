#!/bin/bash
# Lir-milestone truth arm: bare-metal emitted-byte dump banked as lir.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm lir
