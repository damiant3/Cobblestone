#!/bin/bash
# Whole-compiler truth arm: every chapter but the driver, through the back end.
set -e
. "$(dirname "$0")/oracle_lib.sh"
truth_arm whole
