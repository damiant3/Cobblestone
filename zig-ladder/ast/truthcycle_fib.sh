#!/bin/bash
# Fib-milestone truth arm: the front-end chain on fib, IR text banked as fib.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm fib
