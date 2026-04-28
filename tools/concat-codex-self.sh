#!/bin/bash
# Concatenate Codex.Codex/ source with quire-prefixed chapters and any
# cited foreword chapters preloaded. Output goes to stdout. Mirrors
# REF Codex.Cli dump-source for the bootstrap regen path that runs
# selfhost.dll directly without REF.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
CODEX_DIR="${1:-$REPO/Codex.Codex}"
FOREWORD_DIR="$REPO/foreword"

emit_with_quire() {
    local file="$1"
    local quire="$2"
    if [ -z "$quire" ]; then
        cat "$file"
    else
        sed "1s/^Chapter:\s*\(.*\)$/Chapter: ${quire}--\1/" "$file"
    fi
    printf '\n\n'
}

# 1. Cited forewords: scan source for `cites Foreword chapter X`, dedupe,
#    sort, prepend each as Foreword--X chapter.
cited_forewords=$(
    {
        find "$CODEX_DIR" -maxdepth 2 -name '*.codex' -print0 | xargs -0 grep -hE 'cites\s+Foreword\s+chapter\s+[A-Za-z_][A-Za-z0-9_-]*' || true
    } | sed -E 's/.*cites\s+Foreword\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*).*/\1/' \
      | sort -u
)
for fw in $cited_forewords; do
    fw_path="$FOREWORD_DIR/$fw.codex"
    [ -f "$fw_path" ] || continue
    emit_with_quire "$fw_path" "Foreword"
done

# 2. Root .codex files in CODEX_DIR (sorted).
for f in $(find "$CODEX_DIR" -maxdepth 1 -name '*.codex' | sort); do
    emit_with_quire "$f" ""
done

# 3. Subdirectory .codex files (one level), with subdir name as quire prefix.
for d in $(find "$CODEX_DIR" -mindepth 1 -maxdepth 1 -type d | sort); do
    quire=$(basename "$d")
    for f in $(find "$d" -maxdepth 1 -name '*.codex' | sort); do
        emit_with_quire "$f" "$quire"
    done
done
