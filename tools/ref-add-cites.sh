#!/bin/bash
# For each failing sample, scan its build.log for "Undefined name: 'X'",
# map X → chapter via the static table below, and add `cites Codex chapter Y`
# atop the sample's Chapter: header.
set -u
cd "$(dirname "$0")/.."

declare -A MAP=(
  [show]=General
  [file-exists]=Files [list-files]=Files
  [get-args]=Process [get-env]=Process [current-dir]=Process [run-process]=Process
  [text-length]=Text [substring]=Text [text-replace]=Text [text-split]=Text
  [text-contains]=Text [text-starts-with]=Text [text-compare]=Text
  [text-concat-list]=Text [text-to-integer]=Text [text-to-double-bits]=Text
  [integer-to-text]=Text
  [char-at]=Characters [char-to-text]=Characters [char-code]=Characters
  [char-code-at]=Characters [code-to-char]=Characters
  [is-letter]=Characters [is-digit]=Characters [is-whitespace]=Characters
  [negate]=Numbers [abs]=Numbers [min]=Numbers [max]=Numbers [int-mod]=Numbers
  [bit-and]=Bitwise [bit-or]=Bitwise [bit-xor]=Bitwise
  [bit-shl]=Bitwise [bit-shr]=Bitwise [bit-not]=Bitwise
  [list-length]=Lists [list-at]=Lists [list-insert-at]=Lists [list-set-at]=Lists
  [list-snoc]=Lists [list-contains]=Lists [map]=Lists
  [fork]=Concurrency [await]=Concurrency [par]=Concurrency [race]=Concurrency
  [run-state]=Concurrency
  [record-set]=Runtime
  [heap-save]=Runtime [heap-restore]=Runtime [heap-advance]=Runtime
  [list-with-capacity]=Runtime
  [buf-write-byte]=Runtime [buf-write-bytes]=Runtime [buf-read-bytes]=Runtime
  [linked-list-empty]=Runtime [linked-list-push]=Runtime [linked-list-to-list]=Runtime
)

add_cites_to_file() {
  local src=$1 ; shift
  local -a uniq
  mapfile -t uniq < <(printf '%s\n' "$@" | sort -u)
  # Skip chapters already cited
  local -a to_add=()
  for c in "${uniq[@]}"; do
    grep -q "cites Codex chapter $c\b" "$src" || to_add+=("$c")
  done
  [ ${#to_add[@]} -eq 0 ] && return 0

  local tmp
  tmp=$(mktemp)
  # Insert cite lines on the line after the first "Chapter:" line.
  # Use awk for robust insertion.
  awk -v inserts="$(printf '  cites Codex chapter %s\n' "${to_add[@]}")" '
    BEGIN { done = 0 }
    /^Chapter:/ && !done { print; print inserts; done = 1; next }
    { print }
  ' "$src" > "$tmp"
  mv "$tmp" "$src"
}

process_sample() {
  local name=$1
  local src="samples/$name.codex"
  local log="build-output/probes/samples-ref/$name/build.log"
  [ -f "$log" ] || { echo "  $name: no log"; return 0; }

  local -a names=()
  mapfile -t names < <(grep -oE "Undefined name: '[a-z][a-z0-9-]*'" "$log" \
                       | sed "s/Undefined name: '//; s/'//" | sort -u)
  [ ${#names[@]} -eq 0 ] && { echo "  $name: no undefined names in log"; return 0; }

  local -a chapters=()
  local -a unknown=()
  for n in "${names[@]}"; do
    if [ -n "${MAP[$n]:-}" ]; then
      chapters+=("${MAP[$n]}")
    else
      unknown+=("$n")
    fi
  done
  if [ ${#unknown[@]} -gt 0 ]; then
    echo "  $name: unknown (not a typed builtin): ${unknown[*]}"
  fi
  if [ ${#chapters[@]} -gt 0 ]; then
    local uniq
    uniq=$(printf '%s\n' "${chapters[@]}" | sort -u | tr '\n' ' ')
    add_cites_to_file "$src" "${chapters[@]}"
    echo "  $name: +cites $uniq"
  fi
}

for s in arith-neg-mod bitwise-test builtins-test c5-int-field cite-fn-call \
         closure-in-record empty-list-branch expr-calculator func-in-record \
         is-prime-fancy list-append-perf-N8-L7 list-append-perf-min \
         list-test mini-bootstrap multi-lambda-in-record safe-divide \
         stage1-test state-demo string-ops; do
  process_sample "$s"
done
