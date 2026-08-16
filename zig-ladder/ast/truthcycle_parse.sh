#!/bin/bash
# Parse-milestone truth arm: bare-metal CST dump banked as parse.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm parse
