#!/bin/bash
# Fibx-milestone truth arm: fib through the tree emitter, bytes banked as fibx.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm fibx
