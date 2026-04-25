#!/bin/bash
# Scan samples/*.codex for language-feature usage. Purely syntactic grep —
# approximate but good enough for a coverage-at-a-glance table.
cd "$(dirname "$0")/.."

has() { grep -l "$1" samples/*.codex 2>/dev/null | grep -v ".expected\|.failing\|.skip" | wc -l; }
hasE() { grep -lE "$1" samples/*.codex 2>/dev/null | grep -v ".expected\|.failing\|.skip" | wc -l; }

printf "%-40s %s\n" "Feature" "Samples touching it"
printf "%-40s %s\n" "----" "-------------------"
printf "%-40s %s\n" "record { ... }"          "$(has 'record {')"
printf "%-40s %s\n" "variant | ctor"          "$(hasE '^ *\|')"
printf "%-40s %s\n" "type parameter (a) (b)"  "$(hasE '\([a-z]\) *\(')"
printf "%-40s %s\n" "when / is pattern match" "$(has 'when ')"
printf "%-40s %s\n" "if / then / else"        "$(has 'if ')"
printf "%-40s %s\n" "let-in"                  "$(has 'let ')"
printf "%-40s %s\n" "List literal [ ]"        "$(hasE '\[[^]]*,[^]]*\]')"
printf "%-40s %s\n" "lambda \\"               "$(has '\')"
printf "%-40s %s\n" "act-block / <- / end"    "$(has 'act$\| <- ')"
printf "%-40s %s\n" "Effects — [Console]"     "$(has '\[Console\]')"
printf "%-40s %s\n" "Effects — [FileSystem]"  "$(has '\[FileSystem\]')"
printf "%-40s %s\n" "Effects — [State]"       "$(has '\[State\]')"
printf "%-40s %s\n" "Effects — [Time]/[Random]" "$(hasE '\[Time\]|\[Random\]')"
printf "%-40s %s\n" "Effects — [Concurrent]"  "$(has '\[Concurrent\]')"
printf "%-40s %s\n" "run-state"               "$(has 'run-state')"
printf "%-40s %s\n" "handle … is (handler)"   "$(has 'handle ')"
printf "%-40s %s\n" "fork / await / par / race" "$(hasE '\b(fork|await|par|race)\b')"
printf "%-40s %s\n" "linear FileHandle"       "$(has 'linear ')"
printf "%-40s %s\n" "proof / claim / qed"     "$(hasE 'claim |proof |qed')"
printf "%-40s %s\n" "dependent type (x:T)->"  "$(hasE '\([a-z]+ *: *[A-Z]')"
printf "%-40s %s\n" "Prose (intro sentences)" "$(grep -lE '^ [A-Z]' samples/*.codex 2>/dev/null | grep -v '.expected\|.failing\|.skip' | wc -l)"
printf "%-40s %s\n" "cites Foreword chapter"  "$(has 'cites Foreword')"
printf "%-40s %s\n" "cites Codex chapter"     "$(has 'cites Codex chapter')"
printf "%-40s %s\n" "Number literals (x.y)"   "$(hasE '[0-9]+\.[0-9]+')"
printf "%-40s %s\n" "Char literal 'x'"        "$(hasE \"'.'\")"
printf "%-40s %s\n" "Bitwise bit-*"           "$(has 'bit-')"
printf "%-40s %s\n" "TCO / deep recursion"    "$(has 'tco-stress\|count')"
printf "%-40s %s\n" "Closure in record"       "$(has 'closure-in-record')"
echo
echo "Verified (have .expected):"
ls samples/*.expected 2>/dev/null | wc -l
echo "Skipped (have .skip):"
ls samples/*.skip 2>/dev/null | wc -l
echo "Expected-fail (have .failing):"
ls samples/*.failing 2>/dev/null | wc -l
