#!/bin/bash
# Lower-milestone truth arm: bare-metal IRChapter dump banked as lower.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm lower
