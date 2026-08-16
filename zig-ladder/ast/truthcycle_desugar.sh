#!/bin/bash
# Desugar-milestone truth arm: bare-metal AChapter dump banked as desugar.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm desugar
