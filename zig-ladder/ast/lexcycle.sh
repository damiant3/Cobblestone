#!/bin/bash
# Lex-milestone zig arm: plug the lexer IR, zig-run it, diff against truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
zig_arm lex
