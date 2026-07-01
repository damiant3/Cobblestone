# CodexMagic Mobile -- Design & Plan

## What This Is

A .NET MAUI phone app for CodexMagic players. Account management,
store, collection, clan interaction, and tournament signup -- the
on-the-go companion to the explorer web portal and game server.

Built via the Codex MAUI plug: `.codex` source compiles to IR,
the plug emits C# code-behind into the net8.0 MAUI project template.
Targets Android and iOS primarily, Windows/macOS as bonus.

## Server Relationship

The mobile app is a **thin client** of two servers that are being
merged into one:

1. **Explorer server** (`apps/explorer/ExplorerServer.codex` +
   `server.ps1`) -- currently the auth, creations, and content
   portal. HTTP bridge on :8888, CDX server on :9100.
   Auth endpoints: `/api/auth/register`, `/api/auth/login`,
   `/api/auth/me`, `/api/auth/logout`. Token-based sessions
   via `?t=<token>` query param, stored client-side.

2. **CodexMagic game server** (`apps/games/codexmagic/MagicServer.codex`,
   `ClanServer.codex`) -- game engine, economy, clans, matchmaking.
   API surface under `/api/magic/*` and `/api/clan/*`.

The merge will unify auth (one account, one token) and put both
API trees behind the same HTTP endpoint. The mobile app targets
the merged API from day one -- it calls `/api/auth/*` for login
and `/api/magic/*` + `/api/clan/*` for everything else.

## Pages

### 1. Login / Register

First screen. Two modes toggled by a tab or link.

**Register:**
- Handle (unique, kebab-case)
- Display name
- Password (entered twice)
- Age bracket selector (Kid / Teen / Adult)
- POST `/api/auth/register?u=<handle>&d=<display>&p=<password>&age=<bracket>`
- On success: auto-login, navigate to Home

**Login:**
- Handle
- Password
- POST `/api/auth/login?u=<handle>&p=<password>`
- On success: store token in Preferences (`cx_tok`), navigate to Home
- "Forgot password" link (future -- out of scope for v1)

**Session resume:** On app launch, check `Preferences.Get("cx_tok")`.
If present, call `/api/auth/me?t=<token>`. If valid, skip to Home.
If expired/invalid, clear token and show Login.

### 2. Home (Dashboard)

Post-login landing page. Shows:
- Player identity: handle, display name, rating, rank badge
- ManaCoin balance (from `/api/magic/store/balance?account=N`)
- Clan membership summary (name, role, clan rank) or "No clan"
- Quick-action buttons: Open Packs, My Collection, Clan, Store
- Current season name + days remaining
- Recent match history (last 3, from `/api/magic/account/history`)

Data: single fetch to `/api/magic/account/profile?id=N` plus
`/api/magic/store/balance`.

### 3. Store

Two tabs: **Packs** and **Mana Coin**.

**Packs tab:**
- List of available pack types from `/api/magic/store/packs`
- Each shows: name, card count, rarity distribution, price in ManaCoin
- "Buy & Open" button per pack
- Buy flow: POST `/api/magic/store/crack?type=<type>&account=N&seed=S`
- Result: card reveal animation (or simple list for v1), cards added
  to collection

**Mana Coin tab:**
- Current balance display
- Purchase tiers (predefined amounts)
- POST `/api/magic/store/buy-coin?account=N&amount=N`
- Balance updates on success

### 4. Collection

Scrollable list of owned cards from token queries.

- GET `/api/magic/collection?account=N` (new endpoint -- returns
  list of {token-id, template-id, name, rarity, type, colors})
- Filter by: color (W/U/B/R/G/C), type (Creature/Instant/etc.),
  rarity (Common through Mythic)
- Sort by: name, rarity, type, date acquired
- Card detail view: tap a card to see full stats, keywords, rules
  text, provenance (mint source, copy-of)
- Deck builder: out of scope for v1 (use web portal)

### 5. Clan

Three states based on membership:

**No clan:**
- Browse public clans: list from `/api/clan/list` (new endpoint)
- Each shows: name, tag, member count, rank, age class, description
- "Apply to Join" button -> POST `/api/clan/member/join?clan=N&account=N`
- "Create Clan" button -> form with name, tag, age class, description
  -> POST `/api/clan/create?name=X&tag=X&age=X&desc=X`

**Member of a clan:**
- Clan status panel: name, tag, rank, reputation, member count
- Member list with roles (Founder/Officer/Veteran/Member/Recruit)
- Clan coin balance (from clan economy)
- Clan library: available cards to borrow
  - Checkout flow: `/api/clan/library/checkout?token=N&deposit=D`
  - Return flow: `/api/clan/library/return?token=N`
- Tournament list: upcoming and active
  - Join button: `/api/clan/tournament/join?id=N`
- Challenge list: pending and recent results
- Clan coin exchange: buy/sell clan coin for ManaCoin
  - Shows exchange rate, fee percentage
  - `/api/clan/economy/exchange?direction=buy&amount=N`

**Officer+ features** (role >= 3):
- Mint clan coin: `/api/clan/economy/mint?to=N&amount=N`
- Create tournament: form with name, format, structure, entry fee,
  prize pool
- Issue challenge to another clan

### 6. Tournaments & Events

- List of tournaments the player is registered for or eligible for
- Global tournaments: `/api/magic/tournaments/list`
- Clan tournaments: `/api/clan/tournament/list`
- Each shows: name, format, structure, entry fee, prize pool,
  player count, status (Registration/InProgress/Completed)
- Register button for open tournaments
- Match schedule and results for in-progress tournaments
- Leaderboard view: `/api/magic/leaderboard?board=global`

### 7. Profile & Settings

- Edit display name
- View match history (paginated)
- Season rewards: claimable rewards from
  `/api/magic/season/rewards?account=N`
- Claim button: `/api/magic/season/claim?account=N&reward=N`
- App settings: theme (terminal/lcars/minimal), notifications
- Logout: clear token, navigate to Login

## Navigation

MAUI Shell with bottom tab bar (5 tabs):
- Home (dashboard icon)
- Store (shopping bag)
- Collection (cards icon)
- Clan (shield icon)
- Profile (person icon)

Login/Register is a modal page outside the tab shell.
Sub-pages (card detail, tournament detail, clan detail) push
onto the navigation stack within their tab.

## Data Flow

```
  Phone App (.codex -> MAUI C#)
       |
       | HTTPS (JSON)
       v
  Explorer/Magic Server (merged)
       |
       +-- /api/auth/*      (accounts, tokens)
       +-- /api/magic/*     (game, economy, matchmaking)
       +-- /api/clan/*      (clan operations)
       +-- /api/gallery     (public content)
```

All API calls go through the runtime builtins:
- `fetch-get-then url callback` for GET
- `fetch-then url "POST" body callback` for POST
- Token attached as `?t=<token>` query parameter
- Responses parsed with `json-parse-obj` / `json-obj-field`

State management via `state-get`/`state-set` for app state
(current page, selected filters, loaded data). Reactive
re-rendering via `set-render` / `state-set-render`.

## Architecture: One Chapter Per Page

Each page is a `.codex` chapter that:
1. Defines its widget tree builder (`home-page-tree : Integer -> WidgetNode`)
2. Defines its event handlers (`home-on-click : Text -> Integer`)
3. Fetches data on mount and rebuilds the tree on callback

The entry point (`opening`) sets up navigation, checks auth,
and mounts the initial page.

```
apps/codexmagic-mobile/
  Design.md          -- this document
  MobileApp.codex    -- entry point, navigation, auth check
  LoginPage.codex    -- login / register
  HomePage.codex     -- dashboard
  StorePage.codex    -- packs and mana coin
  CollectionPage.codex -- card collection browser
  ClanPage.codex     -- clan membership, library, tournaments
  TournamentPage.codex -- tournament list, registration
  ProfilePage.codex  -- settings, history, season rewards
  MobileTheme.codex  -- app theme (dark-gold variant)
  MobileStubs.codex  -- runtime stubs (cites MauiStubs)
```

## New API Endpoints Needed

The merged server needs a few endpoints the mobile app expects
but that don't exist yet:

| Endpoint | Purpose |
|----------|---------|
| `GET /api/magic/collection?account=N` | List owned card tokens with template details |
| `GET /api/clan/list` | Browse public clans (name, tag, rank, members, age class) |
| `POST /api/clan/create` | Create a new clan |
| `GET /api/magic/tournaments/list` | Global tournament list |
| `GET /api/magic/leaderboard?board=X` | Leaderboard by board type |

Existing endpoints that the app uses as-is:
- `/api/auth/*` (register, login, me, logout)
- `/api/magic/account/*` (profile, history)
- `/api/magic/store/*` (packs, crack, balance, buy-coin)
- `/api/magic/queue/*` (join, status, cancel)
- `/api/magic/season/*` (current, rewards, claim)
- `/api/clan/*` (status, member, economy, library, tournament, challenge)

## Theme

Dark theme based on the explorer's `dark-gold` palette, adapted
for mobile:
- Background: near-black (#0A0A0A)
- Primary: gold (#D4A017)
- Secondary: dark bronze (#3D2E1F)
- Accent: bright gold (#FFD700)
- Text: off-white (#E8E8E8)
- Error: red (#CC0000)
- Card rarity colors: Common (grey), Uncommon (green), Rare (blue),
  Mythic (orange), Legendary Mythic (gold glow)

Larger touch targets than web: minimum 44pt hit areas for buttons.
Font size minimum 14pt for body text.

## Implementation Order

Phase 1 -- Skeleton (auth + navigation):
1. MobileApp.codex: Shell navigation, auth check, page routing
2. LoginPage.codex: Register and login forms
3. HomePage.codex: Dashboard with profile fetch
4. MobileTheme.codex: Dark-gold theme
5. MobileStubs.codex: Runtime stubs

Phase 2 -- Economy:
6. StorePage.codex: Pack list, buy & open, mana coin purchase
7. CollectionPage.codex: Card list with filters

Phase 3 -- Social:
8. ClanPage.codex: Browse/join/create clans, member list, economy
9. TournamentPage.codex: Tournament list, registration

Phase 4 -- Polish:
10. ProfilePage.codex: Settings, history, season rewards
11. Card detail views, animations, error handling

Each phase produces a buildable app that can be tested on a phone
via the MAUI plug pipeline:
```powershell
codex/plugs/maui/run.ps1 -Src apps/codexmagic-mobile/MobileApp.codex -Build
```

## Verification

- Smoke test: build each phase, open in VS, run on Windows
- API test: point at local explorer server (:8888), register
  account, log in, browse store, buy pack
- Phone test: deploy to Android emulator or physical device
- Clan test: create clan, invite second account, verify member
  list, library checkout, tournament join

## Risks

- **Server merge not done yet**: The explorer and codexmagic servers
  are separate. The mobile app targets the merged API surface. Until
  the merge, test against whichever server has the endpoint. Auth
  works against explorer; game/clan endpoints work against codexmagic.

- **API gaps**: Some endpoints don't exist yet (collection, clan list,
  clan create, tournament list, leaderboard). These need to be added
  to the server as part of the merge or as a separate CL.

- **Offline**: v1 has no offline support. All data requires network.
  Future: cache collection and clan status locally.

- **Push notifications**: v1 polls on app-resume. Future: push via
  MAUI Essentials or Firebase/APNs.
