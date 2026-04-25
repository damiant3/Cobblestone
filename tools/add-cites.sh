#!/bin/bash
# For each Codex.Codex/*.codex file, scan for cite-gated builtin usage and
# insert `cites Codex chapter X` lines under the Chapter: header.
# Idempotent: skips files that already have a cite for a given chapter.
set -u
cd "$(dirname "$0")/.."

# Builtin name -> chapter
declare -A CHAPTER
for n in show; do CHAPTER[$n]=General; done
for n in print-line read-line; do CHAPTER[$n]=Console; done
for n in file-exists list-files open-file close-file read-all read-file write-file write-binary; do CHAPTER[$n]=Files; done
for n in get-args get-env current-dir run-process; do CHAPTER[$n]=Process; done
for n in text-length substring text-replace text-split text-contains text-starts-with text-compare text-concat-list text-to-integer text-to-double-bits integer-to-text; do CHAPTER[$n]=Text; done
for n in char-at char-to-text char-code char-code-at code-to-char is-letter is-digit is-whitespace; do CHAPTER[$n]=Characters; done
for n in negate abs min max int-mod; do CHAPTER[$n]=Numbers; done
for n in bit-and bit-or bit-xor bit-shl bit-shr bit-not; do CHAPTER[$n]=Bitwise; done
for n in list-length list-at list-insert-at list-set-at list-snoc list-contains map; do CHAPTER[$n]=Lists; done
for n in fork await par race run-state; do CHAPTER[$n]=Concurrency; done
for n in get-state set-state; do CHAPTER[$n]=State; done
for n in now; do CHAPTER[$n]=Time; done
for n in random-integer; do CHAPTER[$n]=Random; done
for n in __record-set __heap-save __heap-restore __heap-advance __list-with-capacity __buf-write-byte __buf-write-bytes __buf-read-bytes __linked-list-empty __linked-list-push __linked-list-to-list; do CHAPTER[$n]=Runtime; done

for f in "$@"; do
    # Collect needed chapters (exclude builtins that appear inside quoted strings only — a simple approx: require name to appear at least once not-between-quotes is hard; use tokenized grep)
    declare -A NEEDED
    NEEDED=()
    for name in "${!CHAPTER[@]}"; do
        # Match as standalone token: preceded by start/non-id, followed by non-id
        if grep -qE "(^|[^A-Za-z0-9_-])${name}([^A-Za-z0-9_-]|\$)" "$f"; then
            NEEDED[${CHAPTER[$name]}]=1
        fi
    done

    # Also detect existing cites to skip
    declare -A EXISTING
    EXISTING=()
    while IFS= read -r line; do
        ch=$(echo "$line" | sed -nE 's/.*cites Codex chapter ([A-Z][A-Za-z]*).*/\1/p')
        [ -n "$ch" ] && EXISTING[$ch]=1
    done < <(grep -E "cites Codex chapter" "$f" || true)

    # Build cite lines to insert
    cite_lines=""
    for ch in General Console Files Process Text Characters Numbers Bitwise Lists Concurrency State Time Random Runtime; do
        if [ -n "${NEEDED[$ch]:-}" ] && [ -z "${EXISTING[$ch]:-}" ]; then
            cite_lines+="  cites Codex chapter $ch"$'\n'
        fi
    done

    if [ -n "$cite_lines" ]; then
        # Insert AFTER the first line starting with "Chapter:"
        tmp=$(mktemp)
        awk -v lines="$cite_lines" '
            /^Chapter:/ && !inserted {
                print
                printf "%s", lines
                inserted=1
                next
            }
            { print }
        ' "$f" > "$tmp" && mv "$tmp" "$f"
        echo "cited: $f ($(echo "$cite_lines" | grep -c '.'))"
    fi

    unset NEEDED EXISTING
done
