# Market -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this application's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

| Source | Gap |
|---|---|
| `MarketWeb.codex:177`, `:189` | Health inserts the store name raw; `products-json-loop` inserts product IDs/names raw. JSON quote/backslash/control escaping is absent. The current active-product provider returns an empty list, so the product branch is a helper-contract gap until populated. Source-inspected 2026-10-01 at main 33668. |
