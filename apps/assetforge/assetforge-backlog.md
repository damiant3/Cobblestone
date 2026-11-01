# AssetForge -- open capabilities

App-domain backlog. `docs/PM/CurrentPlan.md` carries the shape and the
priority order for the platform. Anything that is this application's own
behaviour lives here. An entry says what is still missing and nothing
else; a closed entry is DELETED rather than annotated.

| # | Capability | State of the gap |
|---|---|---|
| ASSETFORGE-2 | The 3D mesh | `forge-generate` with `fc-generate-3d` runs `AI chapter ImageTo3d` over the picture, but no TripoSR weights load into a `Recon3dState`, so `codex/test/apps/assetforge-card-art` runs with the mesh off. |
