#!/bin/bash
# Lex-milestone truth arm: bare-metal token dump banked as lex.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm lex
