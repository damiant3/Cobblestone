#!/bin/bash
# Fib-milestone zig arm: the same chain through the plug, diffed.
set -e
. "$(dirname "$0")/oracle_lib.sh"
zig_arm fib
