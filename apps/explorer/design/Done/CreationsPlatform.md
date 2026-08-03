# Creations Platform -- per-user, account-owned content

**Status: SHIPPED & verified end-to-end** (val, 2026-05-30, CLs 2836-2849).
App-level only; compiler seed untouched. A working multi-user content layer on
top of the explorer: accounts + durable per-user creations + a public gallery +
remix + export, plus the existing pip-tree designers made account-aware.

## What it does

A signed-in user composes an entity (setting / character / item) from the live
catalog, saves it to their account, browses a community gallery of everyone's
work, remixes others' creations, reopens a saved creation in its designer to
tweak, and exports their collection as a Markdown "World Codex". Everything
persists on the codex-vm disk and survives a VM restart.

## Architecture

Three layers, all over the existing framed-TCP / NE2K stack (no serial):

1. **Accounts** (`codex/os/net/Accounts.codex`, std-include). `auth-serve` loads
   accounts from disk sector 200 on boot, handles `/api/auth/register|login|
   logout|me`, and hands the route the authenticated `Account` (token via `?t=`).
   Route signature: `Maybe Account, Text -> Text`.
2. **Store** (`apps/explorer/ExplorerStore.codex`). Tight binary paged store on a
   mutable arena buffer (`alloc-bytes`/`poke-byte`/`peek-byte`). Generic disk
   primitives: `es-disk-load`/`es-disk-load-at` (read a sector region),
   `es-disk-save` (write a region, one reusable 512B temp), `es-build-page-sized`
   (parameterized page). `block-write-sector`/`block-read-sector` are the durable
   I/O; treated as pure (callable from a `Text -> Text` route).
3. **Server** (`apps/explorer/ExplorerServer.codex`). `opening = auth-serve
   (my-route)`. Creations are one ExplorerStore page in a fixed disk region
   (`cr-start=220`, `cr-sectors=32` / 16KB, `cr-max=50`), each row
   `[owner-id, kind, name, data]` (owner-id = account id as text, so a text
   compare selects "mine"). Stateless per request: save/delete re-read the region,
   mutate the row list, rewrite the full-region buffer.

## API (all JSON unless noted)

| Path | Auth | Effect |
|---|---|---|
| `/api/<table>` | no | catalog rows (biomes, races, items, ... -- `table-json`) |
| `/api/save?kind=&name=&data=` | yes | append a creation owned by the caller |
| `/api/mine` | yes | the caller's creations |
| `/api/delete?name=` | yes | remove the caller's creation by name |
| `/api/remix?by=&name=` | yes | clone another user's creation as "`<name>` (remix)" |
| `/api/gallery` | no | all users' creations as `{by,kind,name,data}` (author handle via accounts) |
| `/api/export` | yes | the caller's creations as a Markdown World Codex (text body) |

Query-param values are URL-decoded server-side (`url-decode`, done in Unicode
space via `to-unicode`/`from-unicode` so the frequency-ordered CCE table is never
assumed ASCII-contiguous) -- content with spaces/punctuation round-trips.

## Clients

- `apps/explorer/creations.html` -- login SPA. Kind-aware composer (setting =
  biomes/times/weathers/moods, character = races/classes/genders/personalities,
  item = items/materials/rarities; name = joined display names, data = joined
  prompt fragments). My-Creations list (open-in-designer + delete), Community
  gallery (remix), Export-as-Markdown.
- The plug-rendered designers (`Setting/Char/ItemDesignerApp`) are made
  account-aware at the BRIDGE layer (no plug change): `run-designers-demo.ps1`
  injects a "Save to My Creations" bar (reads the shared localStorage token,
  grabs `#hero-meta`, posts `/api/save`) and serves the SPA at `/mine`; a saved
  creation reopens via `/<kind>?prompt=<data>` (the inject pre-fills `#prompt`).

## Run

```
pwsh build/compile.ps1 -Src apps/explorer/ExplorerServer.codex -Out build-output/explorer-server.cdx -Log build-output/exsrv.log
pwsh apps/explorer/build-explorer-db.ps1
pwsh apps/explorer/run-creations-demo.ps1 -FreshDisk     # SPA at http://localhost:8888
pwsh apps/explorer/run-designers-demo.ps1                # designers + /mine
```
Regression harness: `apps/explorer/_creationstest.ps1` (TCP, save/mine isolation
+ durability across VM restart).

## Known limits / next

- **Per-request heap growth** in the WebServer/auth-loop (no-GC bump heap, no
  per-request reset): ~50-175K requests of headroom. A real fix is heap-save/
  heap-restore around request handling, but `TcpTransportState` is an immutable
  record reallocated per request, so a naive reset reclaims the live transport --
  this is reek's escape-invariant / DeckCopy work, not a quick app-level fix.
- `cr-max=50` is a SHARED cap across all users (single region). Per-user regions
  or a growable directory would scale further.
- A "proper" in-plug Save button (a `widget-btnbar` button + a CCE-safe plug
  `encode-uri` builtin + `local-storage-get`) would remove the bridge inject, but
  is a lateral move -- the inject works today across all three designers.
- Designer reopen currently restores only the prompt text, not the pip
  selections (those aren't stored). Storing selection indices would restore full
  state.
