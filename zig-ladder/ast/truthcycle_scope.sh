#!/bin/bash
# Scope-milestone truth arm: bare-metal scope+resolve dump banked as scope.truth.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm scope
