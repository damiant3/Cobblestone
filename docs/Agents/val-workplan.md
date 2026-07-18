# val -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, not here. This file is the current
picture and the next moves only. Keep it under ~80 lines.*

## Status

Repository protocol landed through peer resolution: `cdx-store`, `cdx-serve` (the
first test that drives a Codex TCP server), and host-fetches-a-quoted-work-from-a-peer
(6.2). Merge reek's LIR down before starting the target.

## My target -- 5.13: Zstd and Brotli actually compress

Two of the eight compress chapters return MORE bytes than input while wearing a
standard name -- a public embarrassment. Nothing catches it because no test asserts
the output is SMALLER.
- The existing `compress-zstd` / `compress-brotli` tests do not even round-trip; they
  are compile-and-print smoke tests that never call the compressor. Any fix ships a
  COMPRESSIBLE input AND a size assertion (`deflate-gzip-test` is the model).
- Real work: Deflate BTYPE=02 (dynamic Huffman, most of the remaining ratio) and a
  fixed-vs-stored chooser so incompressible input falls back instead of inflating by
  ~1 byte per 8.
Leaf foreword -- NO seed. Runs fully parallel to the seed-carrying changes.

## My lane (own it; others stay out)

codex/os (repo protocol, net), codex/foreword/compress, fact archival, and the public
git mirror. Not reek's compiler, not blu's parser.

## Open in my lane (BACKLOG, after the target)

- 6.2 residue: the registry (the design's third tier; does not exist in any form).
- 6.1 ingest polish: bulk ingest, post-submit hook, a UTF-8 decoder in CCE, and
  sign-with-box-key -- BLOCKED: IdentityManager destroys the plaintext key and there
  is no `key-sign` intrinsic.

## For other agents

- You own the public push mechanics: the git mirror lives inside the fester-main
  workspace (github -> master, gitlab -> main via `git push gitlab master:main`);
  never `git add -A`, use `git add -u` + explicit new paths; do not publish
  `apps/games/magic/`.
- Before the push: `seed/Codex.img` is stale (needs a rebuild) and the poison build
  is Damian's call.
