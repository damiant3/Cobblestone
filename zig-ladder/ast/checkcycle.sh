#!/bin/bash
# Check-milestone zig arm: plug the subject IR, zig-run it, diff against truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
zig_arm check
