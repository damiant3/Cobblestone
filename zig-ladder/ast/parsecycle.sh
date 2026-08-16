#!/bin/bash
# Parse-milestone zig arm: plug the parser IR, zig-run it, diff against truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
zig_arm parse
