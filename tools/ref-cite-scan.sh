#!/bin/bash
# Scan a .codex file for builtin usage and add cites for ones that are actually called,
# excluding names defined in the same file.
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

process_file() {
  local src=$1
  [ -f "$src" ] || return 0

  # Names defined at top-level (start of an indented definition line).
  local -A defined=()
  while IFS= read -r nm; do
    defined[$nm]=1
  done < <(grep -oE "^  [a-z][a-z0-9-]+" "$src" | awk '{print $1}' | sort -u)

  # Extract WHOLE hyphenated identifiers (no word-boundary surprises).
  # Then filter against the builtin MAP to find chapter needs.
  local -A chapters_needed=()
  while IFS= read -r tok; do
    [ -n "${defined[$tok]:-}" ] && continue
    [ -n "${MAP[$tok]:-}" ] && chapters_needed[${MAP[$tok]}]=1
  done < <(grep -oE "[a-z][a-z0-9-]*" "$src" | sort -u)

  [ ${#chapters_needed[@]} -eq 0 ] && return 0

  local -a to_add=()
  for c in "${!chapters_needed[@]}"; do
    grep -q "cites Codex chapter $c\b" "$src" || to_add+=("$c")
  done
  [ ${#to_add[@]} -eq 0 ] && return 0

  # Sort for deterministic output
  mapfile -t to_add < <(printf '%s\n' "${to_add[@]}" | sort)

  local tmp
  tmp=$(mktemp)
  awk -v inserts="$(printf '  cites Codex chapter %s\n' "${to_add[@]}")" '
    BEGIN { done = 0 }
    /^Chapter:/ && !done { print; print inserts; done = 1; next }
    { print }
  ' "$src" > "$tmp"
  mv "$tmp" "$src"
  echo "  $(basename "$src"): +cites ${to_add[*]}"
}

# Pass dirs as args; default: foreword and samples
if [ $# -eq 0 ]; then
  set -- foreword samples
fi
for dir in "$@"; do
  for f in "$dir"/*.codex; do
    [ -f "$f" ] || continue
    process_file "$f"
  done
done
