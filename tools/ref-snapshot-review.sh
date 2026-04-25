#!/bin/bash
# For each unverified sample, run and write the raw captured output to a
# sidecar .pending file. Human reviews each, moves correct ones to .expected.
set -u
cd "$(dirname "$0")/.."

# List of unverified samples — everything in the PASS_UNVERIFIED bucket.
UNVERIFIED=(MathLib arith-neg-mod arithmetic bitwise-test builtins-test
            c5-int-field cite-fn-call effectful-hello effects-demo
            empty-list-branch expr-calculator func-in-record greeting
            is-prime-fancy list-append-perf-N8-L7 list-append-perf-min
            mini-bootstrap multi-lambda-in-record multiline-app
            paren-field-chain polymorphism-coverage proofs prose-banking
            prose-greeting stage1-test state-demo string-ops tco-stress
            test-42 test-bs3-maybe-pattern test-bs3-noparam-ctor
            test-bs3-simple-ctor test-call test-fact1 test-fact2 test-fact3
            test-fact5 test-if test-mul test-rec test-run-process
            type-checker-test w1 w3)

for name in "${UNVERIFIED[@]}"; do
    elf="build-output/probes/samples-ref/$name/$name.elf"
    out="samples/$name.pending"
    if [ ! -f "$elf" ]; then
        echo "missing-elf" > "$out"
        continue
    fi
    wsl bash tools/ref-run-for-sweep.sh "$elf" "$out" 2>/dev/null
done
