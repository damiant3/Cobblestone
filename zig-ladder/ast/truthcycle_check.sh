#!/bin/bash
# Check-milestone truth arm: bare-metal type-check dump banked as check.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm check
