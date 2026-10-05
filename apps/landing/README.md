# Landing bundle and file URLs

The whole assembled site passes `test-file-pages.mjs` from file:// (58 pages, 2026-10-01, seed 267B6C83) and is published at cobblestoneproject.com (CobblestoneWeb 60804ba). Publication follows `docs/Agents/PublicPush.md`, "The website mirror".

The bundle must open from disk without a page server. Keep its directory structure intact. Prism and ModBuilder carry their own embedded payloads; other binary/shader consumers use build-time shared asset scripts. Optional remote APIs and the separately installed native helper are distinct from a page server.

`build.ps1` assembles the site and then invokes `pack-file-assets.mjs`. The packer emits `web/embedded` scripts and a manifest, and inserts loader references into consuming pages. The generated directory is ignored by Perforce and must accompany the pages. `-KeepStudio` preserves the existing Spark page during coordinated work; the final Studio artifact still needs its owner's exact-byte file/control proof. `-Page` does not rebuild the game and compiler payloads.

The loader returns embedded bytes for packaged local fetch URLs, including cache-busting query strings. External requests retain the native fetch path. Missing file assets are refused by name. Arcade module imports use data-URL import maps. Fishtank texture URLs use embedded data so canvas pixel reads do not depend on file-origin image access. A shared compiler pack avoids copying compiler bytes into every GPU demo.

After changing an input asset, rerun the packer. The file-page grader checks raw input and decoded pack hashes against the manifest before browsing. A separately rebuilt WASM file with an old pack is not a current bundle.

Useful focused commands, from the repository root:

```powershell
node apps/landing/pack-file-assets.mjs --only games,c64,data,mathbook
node apps/landing/test-file-pages.mjs --only games/index.html,c64/index.html,data/index.html,mathbook/index.html --out build-output/landing-file-core
```

A focused pack rewrites the manifest for that selection. Finish with the default full pack and final whole-tree proof. Default packing requires a fully assembled tree, including compiler modules and GPU source dependencies.

```powershell
pwsh apps/landing/build.ps1 -Kernel seed/Codex.cdx -KeepStudio
node apps/landing/test-file-pages.mjs --out build-output/landing-file-final
```

Read the build scripts and prepare their prerequisites first. The build requires current plug/compiler inputs; do not use stale ignored artifacts or dark lenses to hide missing prerequisites. Regenerate the landing page to include the new Valheim card. Do not replace the coordinated Studio page without its review.

The browser grader launches an owned Edge profile, navigates only file URLs, blocks external HTTP requests and runs no site server. It checks startup/local-load errors, real arcade selections and core outputs. It is not a complete all-control proof for every page. In particular, deferred live-compiler actions and Studio project/model flows need their targeted checks. Do not enable file-access bypass flags or substitute an HTTP server.

Build cost is linear in input/encoded asset and page bytes; the current implementation stages output text before writing. Each page loads only its group packs plus shared compiler data where needed. Fetch decoding is linear in that asset's bytes and creates temporary binary/string storage; no decoded-byte cache grows with repeated requests. Compiler heap/time behavior is unchanged.
