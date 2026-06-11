# Codex Browser

A web browser built from first principles inside the Codex stack.
No HTML, no CSS, no JavaScript, no backward compatibility. A new web
built on compiled documents, typed data channels, and capability-verified
code — running bare-metal inside codex-vm today, on real hardware
tomorrow.

---

## 1. Vision

The modern web browser is a 30-year archaeological site. HTML was a
document format; CSS was bolted on for style; JavaScript was bolted on
for behavior; then the browser became an operating system pretending
to be a document viewer. The result: a Turing-complete runtime with
ambient authority, no compile-time verification, and a security model
made of string-matching heuristics (CSP, CORS, SameSite cookies).

Codex has the tools to do this properly:

- **One language** for structure, style, and behavior (Codex)
- **Compile-time capability verification** via effect types — the
  browser knows what a page can do before it runs
- **Content-addressed documents** — integrity is intrinsic, not
  bolted on with certificates
- **Trust lattice** — identity and authority without certificate
  authorities
- **Typed data channels** — the server sends compiled frames, then
  streams typed data into declared slots

A page is not a string of markup interpreted at runtime. A page is
a compiled program with a verified capability profile that produces
a widget tree.

### What "Browser" Means Here

The Codex Browser is three things:

1. **A document viewer** — renders compiled Codex pages
2. **An app runtime** — executes capability-gated Codex programs
   with live data binding
3. **A navigation shell** — address bar, tabs, history, trust
   decisions

The boundary between "document" and "app" is not a mode switch.
Every page is a compiled program. A static article is a program
with `[Display]` effects that returns a widget tree from inline
data. An interactive dashboard is a program with `[Display, Network]`
effects that binds to live data channels. The capability profile is
the only difference — the architecture is the same.

---

## 2. Architecture Overview

```
                           Codex Browser
  ┌─────────────────────────────────────────────────────────┐
  │  Navigation Shell (tabs, address bar, history, trust)   │
  ├─────────────────────────────────────────────────────────┤
  │  Page Runtime                                           │
  │  ┌───────────┐  ┌──────────┐  ┌──────────────────────┐ │
  │  │ Compiler  │  │ Verifier │  │ Capability Gate      │ │
  │  │ (source → │→ │ (type +  │→ │ (effect profile →    │ │
  │  │  CDX)     │  │  effect  │  │  user consent →      │ │
  │  │           │  │  check)  │  │  sandboxed exec)     │ │
  │  └───────────┘  └──────────┘  └──────────────────────┘ │
  │                        ↓                                │
  │  ┌──────────────────────────────────────────────────┐   │
  │  │ Widget Tree (live, data-bound)                   │   │
  │  │  ┌─────────┐ ┌──────────┐ ┌──────────────────┐  │   │
  │  │  │ Layout  │→│ Render   │→│ Compositor       │  │   │
  │  │  │ (flex)  │ │ (themed) │ │ (surface → GOP)  │  │   │
  │  │  └─────────┘ └──────────┘ └──────────────────┘  │   │
  │  └──────────────────────────────────────────────────┘   │
  ├─────────────────────────────────────────────────────────┤
  │  Data Layer                                             │
  │  ┌──────────┐  ┌───────────┐  ┌──────────────────────┐ │
  │  │ Fetcher  │  │ Channel   │  │ Cache (content-hash) │ │
  │  │ (HTTP +  │  │ Manager   │  │                      │ │
  │  │  trust)  │  │ (typed    │  │                      │ │
  │  │          │  │  streams) │  │                      │ │
  │  └──────────┘  └───────────┘  └──────────────────────┘ │
  ├─────────────────────────────────────────────────────────┤
  │  Platform                                               │
  │  NetworkStack │ DnsResolver │ TLS │ Surface │ Event     │
  └─────────────────────────────────────────────────────────┘
```

### Layer Summary

| Layer | Responsibility | Key Modules |
|-------|---------------|-------------|
| Navigation Shell | Tab management, address bar, history, bookmarks, trust consent UI | Window, Widget, TextField, Event |
| Page Runtime | Compile source, verify types + effects, execute in sandbox | Compiler pipeline, Verifier, AccessControl |
| Widget Tree | Live widget tree with data bindings, layout, rendering | Widget, Layout, BoxModel, Render, Font, RichText, Scroll |
| Data Layer | Fetch pages, manage typed data channels, content cache | HttpClient, WebSocket, Channel, Sha256, Json |
| Platform | Bare-metal I/O: network, display, input | NetworkStack, Tcp, DnsResolver, Surface, Event, Keyboard |

---

## 3. The Codex Page Format

### 3.1 A Page Is a Codex Program

A page is a `.codex` file (or pre-compiled `.cdx`) that exports a
standard entry point. The browser compiles it, verifies its capability
profile, and executes it in a sandboxed context.

```
Chapter: MyPage
  cites Foreword chapter Widget
  cites Foreword chapter RichText
  cites Foreword chapter Layout

 A simple article page. No network access, no storage — just
 a widget tree built from inline data.

Section: Page

  page-capability : [Display] WidgetNode
  page-capability = display-only

  page : PageContext -> [Display] WidgetNode
  page (ctx) =
    let title = rich-text "bold" "Welcome to Codex"
    in let body = rich-text "normal" "This is a page."
    in layout-column [
      widget-label title,
      widget-label body
    ]
```

### 3.2 Page Entry Points

Every page must export one of these typed entry points:

| Entry Point | Type | Use Case |
|-------------|------|----------|
| `page` | `PageContext -> [Display] WidgetNode` | Static document — widget tree from inline data |
| `app` | `PageContext -> [Display, Network] WidgetNode` | Live app — can open data channels |
| `frame` | `FrameSpec -> [Display] WidgetNode` | Data-driven frame — widget tree with typed binding slots |

`PageContext` provides the page's environment:

```
  PageContext = record {
    viewport-width  : Integer,
    viewport-height : Integer,
    theme           : Theme,
    trust-level     : TrustLevel,
    page-address    : Address
  }
```

### 3.3 The Frame Model (Send Frame, Pump Data)

This is the core architectural innovation. A frame separates the
compiled UI structure from the live data that populates it.

**Step 1 — Serve the frame.** The server sends a compiled CDX that
declares typed data slots:

```
Chapter: ProductListing
  cites Foreword chapter Widget

Section: Data Schema

  Product = record {
    name  : Text,
    price : Integer,
    image : Address
  }

Section: Frame

  frame : FrameSpec -> [Display] WidgetNode
  frame (spec) =
    let products = spec.bind "products" (List Product)
    in layout-column (list-map product-card products)

  product-card : Product -> WidgetNode
  product-card (p) =
    layout-row [
      widget-label (rich-text "bold" p.name),
      widget-label (rich-text "normal" (show p.price))
    ]
```

**Step 2 — Stream data.** Once the frame is loaded and verified,
the browser opens a typed data channel to the server. The server
streams `Product` records. The frame's bindings update, the widget
tree rebuilds, and the renderer repaints the dirty region.

```
  Server                          Browser
    │                                │
    │── CDX frame ──────────────────►│ compile, verify, render skeleton
    │                                │
    │◄─ BIND "products" List Product─│ open typed channel
    │                                │
    │── Product { "Widget", 42 } ───►│ bind, rebuild, repaint
    │── Product { "Gadget", 17 } ───►│ bind, rebuild, repaint
    │── ...                         ►│
    │                                │
    │── CLOSE ──────────────────────►│ channel closed, frame frozen
```

**Why this is better than HTML + SSE:**

- The frame is compiled and type-verified before any data arrives
- The data stream is typed — a malformed record is a type error,
  not a DOM injection
- No runtime parsing of markup — the frame is machine code that
  produces widget nodes
- Layout is pre-determined by the frame — data arrival triggers
  a constrained rebuild, not a full reflow
- The capability profile is fixed — the frame declared `[Display]`
  only, so even with a data channel it cannot make outbound network
  calls, touch the filesystem, or access the camera

### 3.4 Source vs. Pre-Compiled Delivery

Pages can be delivered as:

| Format | Extension | Trust Requirement | Use Case |
|--------|-----------|-------------------|----------|
| Source | `.codex` | Any trust level | Client compiles + verifies (maximum transparency) |
| Compiled CDX | `.cdx` | Publisher must be trusted | Pre-compiled, signed, client verifies signature + capability profile |
| Signed bundle | `.cpk` | Publisher must be trusted | CDX + assets + data schema, signed as a unit |

Source delivery is the default for the open web. The browser compiles
the page locally, so it can verify every type, every effect, every
capability claim. Pre-compiled CDX is for trusted publishers who want
faster load times — the signature proves the CDX was compiled from
the claimed source.

---

## 4. Capability Model

### 4.1 Effects Are the Security Model

Codex effect types are the browser's permission system. Every page
declares its effects in its type signature. The browser's verifier
confirms the code matches the declaration. The user sees the
capability profile before the page runs.

```
  -- This page can only paint pixels. Safe to run without consent.
  page : PageContext -> [Display] WidgetNode

  -- This page can paint and make network calls. Prompt the user.
  app : PageContext -> [Display, Network] WidgetNode

  -- This page wants camera access. Explicit consent required.
  app : PageContext -> [Display, Network, Camera] WidgetNode
```

### 4.2 Capability Tiers

| Tier | Effects | User Consent | Description |
|------|---------|-------------|-------------|
| **Static** | `[Display]` | None | Pure rendering — inline data only. Safe by construction. |
| **Connected** | `[Display, Network]` | Implicit for trusted publishers | Can open data channels to its origin server. |
| **Interactive** | `[Display, Network, Storage]` | Prompt | Can persist state locally (bookmarks, preferences). |
| **Sensor** | `[Display, Network, Camera]` or `[..., Microphone]` | Explicit per-device | Hardware access — always prompted, never remembered. |
| **System** | `[Display, Network, FileSystem]` | Explicit + trust threshold | Local file access — requires high trust level. |

### 4.3 Compile-Time Verification

The key innovation: **capability verification happens at compile
time, not runtime.** The browser's compiler checks that the page's
code uses only the effects it declared. If a page declares
`[Display]` but calls `http-get`, the compiler rejects it with a
type error. The page never runs.

This eliminates entire classes of web vulnerabilities:

| Web Vulnerability | Codex Equivalent | Why It Can't Happen |
|-------------------|-----------------|---------------------|
| XSS (script injection) | No string-to-code eval | Pages are compiled, not interpreted |
| CSRF (cross-site request forgery) | Effect type mismatch | A `[Display]` page cannot make network calls |
| Data exfiltration | Capability gate | Camera/mic require declared effects + consent |
| Supply chain attack | Trust lattice verification | Every dependency is content-addressed and signed |
| Cookie theft | No ambient authority | No cookies — sessions are capability tokens |
| DOM clobbering | No DOM | Widget tree is typed — no string-based lookup |

### 4.4 Ambient Authority Is Dead

There are no cookies. There are no ambient credentials. There is no
`document.cookie`, no `localStorage` that any script can read, no
implicit session tokens attached to every request.

Authentication is explicit:

1. The user holds an Ed25519 identity (from the trust lattice)
2. To authenticate to a server, the user signs a challenge
3. The signed token is a capability — scoped, expiring, revocable
4. The page must declare `[Network]` to even receive the token
5. The token is passed explicitly, not attached to every request

---

## 5. Address Scheme

### 5.1 Content-Hash Addresses

Every page has an intrinsic address: the SHA-256 hash of its source
(or the hash of its signed CDX bundle). This address is permanent,
immutable, and self-verifying.

```
  codex://sha256:3a7b9c4d...f2e1/
```

### 5.2 Named Addresses (Petnames)

Content hashes are not human-friendly. The naming layer maps
human-readable names to content hashes, signed by the publisher:

```
  codex://damian.forge/my-page
```

Resolution:

1. Look up `damian` in the trust lattice — find the Ed25519 public key
2. Query the publisher's name server for `my-page`
3. Receive a signed record: `{ name: "my-page", hash: "sha256:3a7b...", version: 42 }`
4. Verify the signature against the publisher's key
5. Fetch the content by hash from any available source
6. Verify the content matches the hash

No DNS. No certificate authorities. No domain registrars. The
publisher's identity IS their public key. The name mapping is signed.
The content is hash-verified. Trust flows through the lattice, not
through a hierarchy of organizations.

### 5.3 Version Resolution

Named addresses can include version constraints:

```
  codex://damian.forge/my-page@latest     -- latest signed version
  codex://damian.forge/my-page@42         -- exact version
  codex://damian.forge/my-page@^40        -- version 40 or newer
  codex://sha256:3a7b.../                 -- immutable, forever
```

`@latest` is the default. The publisher signs a version chain —
each version record points to the content hash and the previous
version. The browser can verify the entire history.

### 5.4 Discovery

How do you find pages without a search engine? Three mechanisms:

1. **Direct link** — someone sends you an address
2. **Publisher index** — a publisher signs an index of their pages
   (itself a page with `[Display]` effects)
3. **Lattice walk** — browse the trust lattice to find publishers
   vouched by people you trust

Search is a page too — a search service is just a page with
`[Display, Network]` that queries an index server. The browser
does not have a built-in search engine. Search is an app, not a
platform feature.

---

## 6. Data Channels

### 6.1 Typed Streaming Protocol

The frame model requires a protocol for streaming typed data from
server to client. This replaces HTTP request/response for live data.

```
Channel Protocol (over WebSocket or raw TCP):

  Client → Server:  BIND channel-name TypeSignature
  Server → Client:  ACK channel-name
  Server → Client:  DATA channel-name <encoded record>
  Server → Client:  DATA channel-name <encoded record>
  ...
  Server → Client:  CLOSE channel-name
  Client → Server:  UNBIND channel-name
```

### 6.2 Encoding

Channel data is encoded in a binary format derived from the Codex
type system. The frame's compiled type signature serves as the
schema — no external schema registry, no version negotiation.

| Type | Wire Encoding |
|------|--------------|
| Integer | 8 bytes, little-endian |
| Text | 4-byte length + CCE bytes |
| Boolean | 1 byte (0 or 1) |
| Record | Fields in declaration order, no tags |
| Variant | 1-byte tag + payload |
| List | 4-byte count + elements |
| Maybe | 1-byte tag (0=Nothing, 1=Just) + payload |

The encoding is compact and zero-copy-friendly. No JSON parsing,
no protobuf compilation step, no schema evolution headaches. The
type is the schema. If the server's type doesn't match the frame's
declared binding type, the channel handshake fails.

### 6.3 Channel Lifecycle

```
  Browser                            Server
    │                                   │
    │  1. Fetch frame (HTTP GET)        │
    │──────────────────────────────────►│
    │◄──────── CDX frame ──────────────│
    │                                   │
    │  2. Compile + verify (local)      │
    │  3. Render skeleton               │
    │  4. Discover bindings             │
    │                                   │
    │  5. Open channel (WebSocket)      │
    │──────────────────────────────────►│
    │  BIND "products" (List Product)   │
    │──────────────────────────────────►│
    │◄──────── ACK "products" ─────────│
    │                                   │
    │  6. Stream records                │
    │◄──────── DATA Product {...} ─────│
    │  7. Rebuild widget tree           │
    │  8. Repaint dirty region          │
    │◄──────── DATA Product {...} ─────│
    │  9. Incremental rebuild + repaint │
    │                                   │
    │  10. Navigate away                │
    │── UNBIND "products" ────────────►│
    │── close WebSocket ──────────────►│
```

---

## 7. Layout and Rendering

### 7.1 Layout Engine

Built on the existing UI foreword. The layout model is flexbox-only
— no float, no position:absolute, no grid (initially). Flexbox
covers 95% of real layouts and is well-understood.

| Concept | Implementation |
|---------|---------------|
| Box model | `BoxModel.codex` — margin, border, padding, content |
| Flex layout | `Layout.codex` — row/column, flex weights, min-size |
| Scrolling | `Scroll.codex` — viewport + scrollbar |
| Text layout | `Font.codex` + `RichText.codex` — runs, styles, wrapping |
| Theming | `Theme.codex` + `Render.codex` — themed painting |

### 7.2 Rendering Pipeline

```
  Widget Tree → Layout (flex) → LayoutRect Tree → Render (themed)
      → Surface (composited) → GOP Framebuffer
```

1. The page produces a `WidgetNode` tree
2. `Layout` computes `LayoutRect` positions (flex algorithm)
3. `Render` walks the `LayoutRect` tree, resolves styles from
   `Theme`, calls `Rasterizer` primitives (filled rects, borders,
   text blitting)
4. `Surface` composites the page surface with the browser chrome
   (tabs, address bar) using z-order and dirty-rect tracking
5. The compositor writes to the GOP framebuffer

### 7.3 Incremental Repaint

When data arrives on a channel:

1. The binding slot updates
2. The frame function re-evaluates for the affected subtree only
3. Layout runs on the changed subtree (parent flex reflows if size
   changed)
4. Render repaints the dirty region (tracked by Surface)

Full-page reflow is never needed for data updates — the frame's
structure is fixed, only data-bound leaf values change. This is
a fundamental advantage over HTML, where a DOM mutation can trigger
arbitrary reflow.

### 7.4 Text Rendering

Current: bitmap font (5x7, CCE codes 0-96 in `Font.codex`).

Target: scalable font rendering. Two options:

1. **Bitmap font atlas** — pre-rendered at common sizes (12, 14, 16,
   18, 24, 32px). Simple, fast, sufficient for UI text. Can be
   generated offline and shipped as a CDX asset.

2. **Outline font renderer** — parse TrueType/OpenType, rasterize
   glyphs with Bezier curve evaluation. The foreword already has
   `Bezier.codex` and `Rasterizer.codex`. This is substantial work
   but achievable.

Recommendation: start with bitmap atlas, build outline renderer as
a separate workstream.

---

## 8. Navigation Shell

### 8.1 Chrome Layout

```
┌──────────────────────────────────────────────────────┐
│ [Tab 1] [Tab 2] [Tab 3]                        [+]  │
├──────────────────────────────────────────────────────┤
│ [<] [>] [R]  codex://damian.forge/my-page   [Trust]  │
├──────────────────────────────────────────────────────┤
│                                                      │
│                                                      │
│               Page Content Area                      │
│                                                      │
│                                                      │
│                                                      │
└──────────────────────────────────────────────────────┘
```

### 8.2 Components

| Component | Description |
|-----------|-------------|
| Tab bar | Horizontal tab strip. Each tab owns a page runtime instance. |
| Address bar | Text input showing current address. Accepts `codex://` addresses and petnames. |
| Navigation buttons | Back, forward, reload. History is a per-tab list of content hashes. |
| Trust indicator | Shows the page's capability tier and publisher identity. Color-coded: green (Static), blue (Connected), yellow (Interactive), red (Sensor/System). |
| Page viewport | Scrollable viewport rendering the page's widget tree. |

### 8.3 Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Ctrl+T | New tab |
| Ctrl+W | Close tab |
| Ctrl+L | Focus address bar |
| Ctrl+R | Reload page |
| Alt+Left | Back |
| Alt+Right | Forward |
| Ctrl+Tab | Next tab |
| Ctrl+Shift+Tab | Previous tab |
| F5 | Reload |
| Escape | Stop loading |

---

## 9. Trust and Identity

### 9.1 Publisher Identity

A publisher is an Ed25519 keypair. The public key IS the identity —
no registration, no authority, no namespace collision. Publishers
sign their pages, their name mappings, and their version chains.

### 9.2 Trust Decisions

When the browser loads a page from an unknown publisher:

1. Display the capability profile (tier + specific effects)
2. Display the publisher's public key (truncated)
3. Display trust lattice path if one exists (e.g., "Vouched by Alice,
   who you trust")
4. User decides: run once, trust this publisher, or refuse

Trust decisions are stored locally and scoped:

```
  TrustDecision = record {
    publisher    : Ed25519PublicKey,
    max-tier     : CapabilityTier,
    granted      : DateTime,
    expires      : Maybe DateTime,
    vouched-by   : Maybe Ed25519PublicKey
  }
```

### 9.3 Trust Tiers vs. Page Tiers

A publisher's trust level is a ceiling. If you trust `damian.forge`
at the Connected tier, their pages can run `[Display]` or
`[Display, Network]` without prompting. A page requesting
`[Display, Network, Camera]` would still prompt because it exceeds
the granted tier.

### 9.4 Content Integrity

Every page is content-addressed. After fetching, the browser:

1. Hashes the received bytes (SHA-256)
2. Compares against the expected hash (from the address or name record)
3. If the hashes don't match, the page is rejected

This eliminates man-in-the-middle attacks without TLS (though TLS
is still used for transport confidentiality). Even if the transport
is compromised, a tampered page fails the hash check.

---

## 10. Implementation Status

### Original Five-Phase Plan (for reference)

The original design envisioned five sequential phases: Static Pages,
Network Fetch + Trust, Data Channels, Interactive Pages, Rich Content.
Each phase was framed as a feature slab that blocked on the previous
one. Implementation revealed a different natural order — the browser
shell, trust model, data channels, and address scheme could all be
built in parallel because they share types but not control flow.

### What Was Actually Built (3,219 lines, 17 files)

The skeleton is a working app (`apps/browser/`) with all major
subsystems wired end-to-end. Built-in pages render through the full
pipeline: address resolution → page loading → widget tree
construction → layout → framebuffer rendering.

| Module | Lines | Status |
|--------|-------|--------|
| `Browser.codex` | 649 | Full state machine: init, chrome rendering, tab management, navigation with page loading, trust prompt UI (allow/always/deny), mouse hit testing (tab bar, address bar, trust dialog, page content with link detection), address bar key handling, scroll, render pipeline, resize |
| `DataChannel.codex` | 347 | Binary wire protocol: uint16/uint32/int64 LE encode/decode, text field encoding, binary DATA message parser (marker + name-length + payload-length), text message parser (ACK/CLOSE/ERROR), channel manager with open/close/drain/stats, full message processing pipeline |
| `PageRuntime.codex` | 314 | 7 built-in pages (newtab, hello, about, dashboard, trust info, history, settings), address resolution chain (local → cache → remote), page loading pipeline, inline page registry |
| `AddressBar.codex` | 258 | Full text editor (insert, backspace, delete, cursor left/right/home/end, selection, select-all), address normalization (bare name → codex://local/, dotted → codex://), autocomplete with suggestion list navigation |
| `ContentAddress.codex` | 245 | CID-inspired binary encoding/decoding (38-byte format), hex display, name records with Ed25519 signing/verification, address parsing (local/named/hash), content hash verification, name record operations |
| `Tab.codex` | 227 | Navigation with history trimming on new nav, scroll integration (page-up/down/top), loading/error/empty page templates, capability tracking per tab |
| `PageFetcher.codex` | 207 | HTTP fetch pipeline with FetchOptions, response processing (status codes, size limits), header extraction, content hash verification, signature verification, result formatting |
| `BrowserEvent.codex` | 199 | Key code constants, modifier handling (ctrl/alt/shift), ctrl/alt/plain key mapping, scroll/page-up/down/home/end actions, action classification and naming |
| `BrowserTheme.codex` | 165 | Chrome color palette (bg, tabs, address bar, buttons, hover/active states), trust tier colors (5 tiers), page default colors (text, links, headings, code, error/warning/success backgrounds), spacing and layout constants |
| `TrustManager.codex` | 159 | Trust store with grant/lookup, tier operations (level/name/color/allows), capability checking (granted/needed/escalation verdict), effect-to-tier classification, Ed25519 signature verification |
| `History.codex` | 116 | Visit recording with dedup and visit counting, prefix search for autocomplete, recent entries, suggestion formatting |
| `ContentCache.codex` | 114 | LRU cache with content-hash keys, touch-on-access, oldest-tick eviction, capacity management |
| `PageSandbox.codex` | 98 | Capability gate (auto-trust static, prompt for higher tiers), source size gate (1 MB default), embedding verification (EROS factory axioms: capability ceiling, all-grants-within-parent check) |
| `opening.codex` | 17 | Entry point: init theme, init browser state, run |
| 3 sample pages | 104 | hello.codex, about.codex, dashboard.codex |

### What Changed From the Original Plan

1. **App, not quire.** The original plan placed the browser in
   `codex/browser/` as a quire. It lives in `apps/browser/` instead
   — it is an application with an `opening` entry point, not a
   library. If shared components emerge (e.g., the content address
   scheme), they can be extracted to a foreword module later.

2. **Built-in pages instead of in-browser compilation.** The
   original plan assumed Phase 1 would compile `.codex` source in
   the browser. Instead, the MVP uses inline page functions compiled
   into the browser binary. This sidesteps the hard PageRuntime
   problem (Section 15) and lets us validate the entire rendering
   pipeline without waiting for in-process compilation.

3. **Trust prompt is built, not deferred.** The original plan
   deferred trust UI to Phase 2. It is built now — the full
   allow-once / trust-publisher / deny flow with pending trust
   state, keyboard handling, and rendering.

4. **Data channels are built, not deferred.** Binary wire encoding
   (uint16/32/64 LE, text fields, binary DATA messages) is
   implemented. Channel management (open/close/drain/stats) is
   wired. The original plan deferred this to Phase 3.

5. **History module added.** Not in the original plan. Needed for
   address bar autocomplete and will feed the history page.

### Remaining Phases

The original five-phase plan collapses into three:

**Phase A — Bare-metal integration.** Wire the browser to real
keyboard/mouse events, the GOP framebuffer, and CCE character
encoding. The logic is built; these are integration stubs (see
Section 15).

**Phase B — Network fetch.** Wire PageFetcher to the live TCP/IP
stack via NetworkStack sessions. DNS resolution, HTTP GET, hash
verification, signature verification — all built, need the network
plumbing.

**Phase C — In-browser compilation.** Invoke the compiler pipeline
to compile fetched `.codex` source into a widget tree. This is the
one remaining hard problem (see Section 15, Critical Path).

---

## 11. App Structure

The browser is an app at `apps/browser/`.

```
apps/browser/
  codex.project.json     -- project config, dependencies
  opening.codex          -- entry point (browser-init, browser-run)
  Browser.codex          -- main state machine, chrome, event loop
  Tab.codex              -- per-tab state, navigation, scroll, templates
  AddressBar.codex       -- text editing, autocomplete, suggestions
  PageRuntime.codex      -- page loading, built-in pages, address resolution
  PageFetcher.codex      -- HTTP fetch, hash verification, signature check
  DataChannel.codex      -- typed data channels, binary wire protocol
  ContentAddress.codex   -- CID-inspired hashing, name records, addresses
  TrustManager.codex     -- trust store, capability tiers, Ed25519
  PageSandbox.codex      -- capability gate, source size gate, embedding
  ContentCache.codex     -- LRU content-addressed cache
  BrowserTheme.codex     -- colors, spacing, layout constants
  BrowserEvent.codex     -- keyboard shortcuts, event classification
  History.codex          -- browsing history, autocomplete suggestions
  pages/
    hello.codex          -- sample: minimal static page
    about.codex          -- sample: browser info and capability tiers
    dashboard.codex      -- sample: system status with inline data
```

Dependencies declared in `codex.project.json`:
`codex.foreword`, `codex.foreword.ui`, `codex.foreword.encode`,
`codex.os.net`, `codex.os.trust`.

---

## 12. Wire Protocol Summary

### Page Fetch (HTTP)

```
GET /my-page HTTP/1.1
Host: damian.forge
Accept: application/codex, text/codex

HTTP/1.1 200 OK
Content-Type: application/codex       (source) or
              application/cdx         (compiled)
Content-Hash: sha256:3a7b9c4d...f2e1
X-Codex-Publisher: ed25519:ab12...cd34
X-Codex-Signature: <signature of content hash>
X-Codex-Capabilities: Display,Network
X-Codex-Version: 42

<page bytes>
```

### Data Channel (WebSocket upgrade)

```
GET /channels HTTP/1.1
Upgrade: websocket
X-Codex-Page: sha256:3a7b9c4d...f2e1
X-Codex-Auth: <capability token>

--- after upgrade ---

Client: BIND products List{name:Text,price:Integer,image:Address}
Server: ACK products
Server: DATA products <binary Product record>
Server: DATA products <binary Product record>
Client: UNBIND products
```

---

## 13. What We Don't Build

| Feature | Why Not |
|---------|---------|
| HTML/CSS parser | We have our own document format |
| JavaScript engine | Pages are compiled Codex |
| Cookie jar | No ambient authority — capability tokens instead |
| DOM | Widget tree is the model — typed, not string-indexed |
| CORS | Capability effects replace origin-based restrictions |
| Service workers | The page IS compiled code — no need for a script layer |
| Web extensions | Browser features are Codex modules — extend by writing code |
| PDF viewer | PDFs are a legacy format — build a Codex document viewer |
| Developer tools | The Codex debugger serves this role directly |
| Bookmarks sync | Trust lattice + fact store replaces cloud sync |

---

## 14. Research Areas

### 14.1 Ladybird and Servo Lessons

Both projects (alpha-stage as of mid-2026) confirm that building a
browser from scratch is viable but surface key lessons:

- **Hit testing is hard.** Ladybird replaced naive hit-testing with
  a pre-computed AccumulatedVisualContext tree. Our flex-only layout
  simplifies this significantly — no z-index stacking contexts, no
  position:absolute — but we still need efficient point-in-rect
  lookup for event routing.

- **Profiling-driven optimization matters.** Servo found garbage
  collection and pseudo-class invalidation were major bottlenecks.
  Our compiled model avoids GC entirely, but layout recalculation
  on data updates needs profiling once we have real pages.

- **IPC security hardening.** Both projects emphasize process
  isolation. On bare metal we don't have processes yet (structured
  concurrency is in design — see CAMP-IIIC). For Phase 1 we run
  pages in-process with effect-type isolation only. Hardware-level
  isolation (page tables per tab) is a future milestone.

### 14.2 Content-Addressed Web (IPFS Developments)

IPFS reached production viability in 2025-2026:

- **Service Worker Gateway** enables HTTP gateway retirement — content
  verification moves client-side. Analogous to our model where the
  browser verifies content hashes directly.

- **Bitswap optimization** (80-98% message reduction) proves that
  content-addressed fetching can be efficient at scale.

- **Bluesky** (40M+ users on IPFS-backed infrastructure) demonstrates
  content addressing at social-media scale.

#### IPFS CID Format (Research Complete)

CIDv1 is a self-describing content identifier:
`<version-varint> <content-type-varint> <multihash>`, where multihash
is `<hash-func-varint> <digest-size-varint> <digest-bytes>`.

| Field | Encoding | Example Values |
|-------|----------|---------------|
| Version | unsigned varint | `0x01` (CIDv1) |
| Content codec | unsigned varint | `0x55` (raw), `0x70` (dag-pb), `0x71` (dag-cbor) |
| Hash function | unsigned varint | `0x12` (SHA2-256), `0x1E` (SHA2-512), `0xB220` (BLAKE2b-256) |
| Digest length | unsigned varint | `0x20` (32 bytes for SHA-256) |
| Digest | raw bytes | 32 bytes |

String encoding prepends a multibase prefix: `b` (base32lower),
`z` (base58btc). Total binary size for SHA-256: 36 bytes (1+1+2+32).

**Adaptation for Codex:** Our `ContentAddress` uses a simplified
variant: version byte `0x01`, codec `0xC0` (codex source) or
`0xC1` (cdx binary), SHA-256 multihash. No multibase — addresses
are always binary internally, hex-encoded for display. This gives
us self-describing addresses with room for future hash algorithms.

#### IPNS Mutable Name Resolution (Research Complete)

IPNS records are signed mutable pointers to content hashes:

| Field | Type | Purpose |
|-------|------|---------|
| Value | bytes | The content path being pointed to |
| Sequence | uint64 | Monotonic version counter |
| TTL | uint64 | Cache duration in nanoseconds |
| Validity | RFC3339 string | Expiration timestamp |
| SignatureV2 | bytes | Ed25519 signature over CBOR-encoded data |

V2 signing: `sign("ipns-signature:" || DAG-CBOR(data))` where data
is `{Sequence, TTL, Validity, ValidityType, Value}` with sorted keys.
Record max: 10 KiB.

**Adaptation for Codex:** Our `NameRecord` follows the same pattern
but uses Codex binary encoding (not CBOR) and the trust lattice for
key discovery (not DHT). The signature covers the full record
including the publisher's public key, making records self-verifying.

#### Delegated Routing (Research Complete)

IPFS delegated routing is a simple HTTP API:
- `GET /routing/v1/providers/{cid}` — find who has content
- `GET /routing/v1/ipns/{name}` — resolve a name to content
- Streaming via `application/x-ndjson` (one JSON object per line)

**Adaptation for Codex:** Our initial fetch uses plain HTTP — the
server IS the content provider. Delegated routing becomes relevant
when we add peer-to-peer content distribution (post-Phase 5).

### 14.3 Capability-Based Browser Security (Research Complete)

No production system has achieved compile-time capability
verification for web content. This is genuinely novel territory.

#### seL4 Capability Model

seL4 organizes capabilities in CNodes (capability nodes) forming a
hierarchical namespace. Key mechanisms:

- **Mint**: Derive a new capability with reduced rights (never
  expanded). Supports badging — a 28-64 bit data word for recipient
  identification without exposing the original capability.
- **Copy**: Duplicate capabilities between slots, enabling delegation
  while maintaining provenance.
- **Revoke**: `seL4_CNode_Revoke` recursively deletes all
  capabilities derived from a target. Cascading — invalidating an
  inner node immediately invalidates its entire subtree.

#### EROS/KeyKOS Factory Pattern

EROS implements a formally verified constructor/factory pattern for
creating confined subsystems:

- A **factory** creates a specific type of confined domain, receiving
  only the capabilities needed to construct its objects
- Objects created by the same factory **cannot communicate** without
  explicit introduction, preventing covert channels
- A client can **cryptographically verify** a factory's
  trustworthiness
- This is the only OS protection primitive with **formal mathematical
  proof** of enforcement

#### Adopted Design: Page Embedding Capability Model

For nested pages (Page A embeds Page B), we adopt the EROS factory
pattern combined with seL4's cascading revocation:

1. **Factory isolation**: The outer page creates the inner page via
   a factory function. The inner page receives only the capabilities
   explicitly passed — it cannot discover or access the outer page's
   capabilities.

2. **Capability ceiling**: The inner page's declared effects must be
   a subset of the outer page's effects. A `[Display]` page cannot
   embed a `[Display, Network]` page — the factory rejects it.

3. **Cascading revocation**: If the user revokes the outer page's
   `Network` capability, all inner pages that received `Network`
   from it are revoked simultaneously (seL4 subtree model).

4. **Communication via typed channels only**: The outer and inner
   page communicate through the same typed channel protocol used
   for server data streams. No shared memory, no ambient access.

5. **Mint for delegation**: When the outer page passes a channel
   reference to the inner page, it mints a derived capability with
   reduced rights (e.g., read-only view of a data channel).

### 14.4 Compiled UI Delivery (Research Complete)

The frame model (send compiled widget tree, pump data) is novel.

#### WASM Component Model Type System

The Component Model uses WIT (Wasm Interface Types) for cross-module
type agreement:

- **Structural typing** for data: records, variants, lists, options,
  results achieve compatibility through structural equivalence (same
  shape = same type)
- **Nominal typing** for resources: each resource definition produces
  a unique identity, ensuring strong abstraction boundaries
- All cross-component calls transit through **lift/lower trampolines**,
  creating a validated membrane

#### Adopted Design: Channel Type Agreement

Our data channel protocol adopts WIT's structural/nominal split:

- **Data types** (records, variants, lists flowing through channels)
  use **structural matching** — the frame declares `Product = record
  { name : Text, price : Integer }` and the server sends records
  with matching field names and types. No registration, no schema
  ID. Shape is the contract.

- **Capabilities** (channel handles, auth tokens) use **nominal
  matching** — a capability token is opaque, identified by its
  mint origin, and cannot be forged by constructing a same-shaped
  record.

- **Type checking at bind time**: When a frame sends `BIND "products"
  List Product`, the server responds with its type signature. The
  browser structurally compares the two. Mismatch = bind rejected.
  No data flows until types agree.

### 14.5 Font Rendering on Bare Metal (Research Complete)

#### Minimum Viable TrueType

Seven tables are required for basic glyph rendering:

| Table | Purpose | Notes |
|-------|---------|-------|
| `cmap` | Character code → glyph index | Format 4 sufficient for Latin |
| `glyf` | Glyph outlines (quadratic Bezier) | Simple contours first, composite later |
| `head` | Font header, bounding box limits | Also determines `loca` format |
| `hhea` | Horizontal metrics header | Ascent, descent, line gap |
| `hmtx` | Per-glyph advance width + LSB | Needed for character positioning |
| `loca` | Byte offsets into `glyf` | Short or long format per `head` |
| `maxp` | Total glyph count | Bounds checking |

Skip initially: GSUB/GPOS (ligatures/kerning), hinting (fpgm/prep),
OS/2, name, post.

#### Architecture (stb_truetype-inspired)

1. **Table parsing**: ~500-1000 lines. Read table directory, extract
   offsets for the 7 required tables.
2. **Glyph extraction**: `cmap` lookup → `loca` offset → `glyf`
   contour data → list of quadratic Bezier curves.
3. **Rasterization**: Feed Bezier curves into our existing
   `Rasterizer.codex` (`fb-polygon` or scanline fill). stb_truetype
   uses scanline rasterization with winding-number fill — ~1500
   lines for the core rasterizer, reducible to ~500 since we already
   have Bezier evaluation.
4. **Bitmap caching**: Pre-render glyphs at target sizes into a
   texture atlas (512x512 sufficient for ASCII/Latin-1 at 4 sizes).

#### Implementation Plan

Phase 1 (MVP): Continue using the existing 5x7 bitmap font
(`Font.codex`). It renders CCE codes 0-96 and is sufficient for
browser chrome and simple pages.

Phase 2 (Rich Content): Build a `TrueType.codex` foreword module
with table parsing, glyph extraction, and atlas generation. Connect
to the existing Bezier and Rasterizer modules. Estimated: ~1500
lines of Codex, no external dependencies.

A CCE-native font format (glyph outlines indexed by CCE code, no
Unicode tables) would be simpler still — ~800 lines — but limits
us to fonts designed specifically for Codex.

### 14.6 Page Embedding (Frames — Design Complete)

A page embedding another page uses the EROS factory pattern
(Section 14.3). The formal model:

**Axiom 1 — Capability ceiling.** An embedded page's declared
effects must be a subset of the embedding page's granted effects.
A `[Display]` page cannot embed a `[Display, Network]` page.

**Axiom 2 — Factory isolation.** The embedded page is created via
a factory that provides only the capabilities explicitly listed in
the embed declaration. The embedded page cannot discover the
embedding page's other capabilities.

**Axiom 3 — Communication via channels.** The embedding page and
embedded page communicate only through typed data channels. No
shared widget tree, no shared memory.

**Axiom 4 — Cascading revocation.** Revoking a capability from
the embedding page cascades to all embedded pages that received
that capability (seL4 derivation tree).

**Axiom 5 — Independent consent.** If the embedded page requests
capabilities not held by the embedding page (e.g., Camera), the
browser prompts the user independently. The embedding page never
learns whether consent was granted.

```
  -- Embedding syntax in a page
  embed : Address, List CapabilityGrant -> [Display] WidgetNode
  embed (addr) (grants) =
    let sandbox = page-factory addr grants
    in sandbox.widget-tree
```

The embed function returns a WidgetNode subtree rendered by the
embedded page. The embedding page treats it as an opaque widget —
it can position and size it, but cannot inspect or modify its
contents.

---

## 15. Remaining Stubs

Six integration stubs remain. These are functions with correct type
signatures and placeholder bodies that need bare-metal wiring.

### 15.1 Event Polling (browser-poll-event)

**Location:** `BrowserEvent.codex`
**Current:** Returns a timer event (no-op tick).
**Needs:** Wire to `codex.os.kernel/Keyboard.codex` for key events
and `codex.os.kernel/Mouse.codex` for mouse events. The kernel
modules already produce `Event` records — this stub needs to read
them from the kernel event queue.

### 15.2 Character Encoding (char-from-code, char-from-byte)

**Location:** `Browser.codex`, `DataChannel.codex`
**Current:** Returns empty text.
**Needs:** CCE character lookup. The foreword `CCE.codex` module
handles Codex Character Encoding. These stubs need to call
`cce-char-to-text` or equivalent to convert a CCE code point to a
single-character Text value for address bar input and binary
protocol text decoding.

### 15.3 Text-to-Bytes Conversion (payload-to-text, bytes-to-text-range)

**Location:** `DataChannel.codex`
**Current:** Iterates bytes and calls `char-from-byte` (empty).
**Needs:** Same CCE integration as 15.2. Once `char-from-byte`
works, `payload-to-text` works automatically.

### 15.4 Integer Parsing (parse-ver)

**Location:** `PageFetcher.codex`
**Current:** Returns 0.
**Needs:** Parse a decimal integer from a Text string. The foreword
`Parse.codex` module likely has this. Wire `text-to-integer` or
equivalent.

### 15.5 Framebuffer Presentation

**Location:** `Browser.codex` (browser-render-frame, browser-compose-and-present)
**Current:** Renders to a `Framebuf` via `render-tree` and creates
a `Compositor`. The framebuffer is not presented to the screen.
**Needs:** Write the compositor's output framebuffer to the GOP
display. On codex-vm, this means writing to the VBE framebuffer
address. The kernel `VgaGraphics.codex` module handles this —
wire `compositor-render` output to `vga-blit-framebuf` or the
GOP `blt` operation.

### 15.6 Network Session Integration

**Location:** `PageFetcher.codex`
**Current:** Calls `http-serialize-request` and
`http-parse-response` but does not send/receive over the network.
**Needs:** Create a `NetSession` via `NetworkStack.codex`, call
`net-connect` to establish TCP, send the serialized HTTP request
bytes, receive the response bytes, then call `http-parse-response`.
The networking modules handle TCP state, IP framing, and ARP — the
fetcher just needs to call `net-send` / `net-receive-segment` in
a loop until the response is complete.

### Critical Path: In-Browser Compilation

The one remaining hard problem is compiling fetched `.codex` source
into a widget tree. Four options were considered:

| Option | Approach | Status |
|--------|----------|--------|
| 1. In-process | Browser includes compiler pipeline | Viable — compiler is in the selfhost binary already |
| 2. VM-in-VM | Nested VM compiles page | Blocked on pure-Codex VMX host (Gap 5) |
| 3. Compiler service | Separate process over channels | Blocked on structured concurrency (CAMP-IIIC) |
| 4. Pre-compiled only | CDX files loaded directly | **Current MVP — built-in pages as inline functions** |

The MVP (Option 4) is implemented. The path to Option 1: the
compiler's `opening` function accepts source on stdin and emits CDX
on stdout. To compile a page in-process, the browser would call the
compiler pipeline functions directly (parse → desugar → scope →
check → lower → emit), extract the `page` entry point from the
emitted CDX, and call it with viewport dimensions. This requires
the browser binary to link the compiler quire — adding ~29K lines
to the binary, but no new infrastructure.

---

## 16. Open Questions (Resolved and Remaining)

### Resolved

1. **Page size limits.** Decided: 1 MB source limit, configurable
   via `SandboxConfig.sb-max-source-bytes`. Implemented in
   `PageSandbox.codex`.

2. **Caching compiled pages.** Decided: LRU cache keyed by content
   hash, 64 entries default. Implemented in `ContentCache.codex`.

3. **Error pages.** Decided: built-in error page template with
   address, error message, retry/home buttons. Implemented in
   `Tab.codex` (`render-error-page`).

4. **Page-to-page navigation.** Decided: link widgets have IDs
   prefixed with `link-`, click handler maps ID to address,
   browser-navigate is called. Implemented in `Browser.codex`
   (`browser-handle-page-click`, `widget-link-address`).

5. **Server-side compilation.** Decided: yes, but only from trusted
   publishers. The trust tier system handles this — a signed CDX
   from a publisher trusted at the Connected tier or above skips
   local compilation.

### Remaining

1. **Compilation latency.** Still unmeasured. The compiler handles
   ~1.2 MB in ~10 seconds for the selfhost. A typical page (5-50 KB)
   should be sub-second. Measure once Option 1 (in-process
   compilation) is wired.

2. **Multi-process isolation.** Still blocked on structured
   concurrency (CAMP-IIIC). Effect-type isolation is the only
   security boundary. Hardware isolation (page tables per tab) is
   a long-term goal.

3. **Progressive rendering.** Designed but not implemented. The
   frame model naturally supports it — render skeleton, fill data
   channels progressively.

4. **Scroll state persistence.** Should scroll position survive
   back/forward navigation? Currently it resets to top on every
   navigation. Could store scroll offset in history entries.

5. **Bookmark model.** Not designed. Likely a list of
   `ContentAddress` entries stored in the trust lattice fact store.

---

## 17. Multimedia Playback

### 17.1 Architecture

A media player on the Codex web is not a special browser element
or a plugin. It is a **compiled Codex page with `[Display, Network,
Audio]` effects** that receives compressed media over typed data
channels and decodes locally. The capability model naturally handles
media permissions — a page must declare `Audio` effects to play
sound, and the browser verifies this at compile time.

```
  Server                           Browser
    │                                 │
    │── CDX frame (player UI) ──────►│ compile, verify, render controls
    │                                 │
    │◄─ BIND "video" VideoPacket ────│
    │◄─ BIND "audio" AudioPacket ────│
    │                                 │
    │── VideoPacket (keyframe) ─────►│ decode → framebuf → VBE display
    │── AudioPacket (PCM chunk) ────►│ decode → HDA DMA buffer
    │── VideoPacket (delta) ────────►│ apply delta → repaint
    │── AudioPacket (PCM chunk) ────►│ write to HDA ring
    │── ...                          │
```

### 17.2 Media Stream Wire Format

**Video packet** (over data channel "video"):

| Offset | Size | Field |
|--------|------|-------|
| 0 | 1 | Frame type: `0x49` (keyframe 'I') or `0x50` (delta 'P') |
| 1 | 4 | Presentation timestamp (ms, LE uint32) |
| 5 | 4 | Payload length (LE uint32) |
| 9 | N | Compressed frame data |

**Audio packet** (over data channel "audio"):

| Offset | Size | Field |
|--------|------|-------|
| 0 | 1 | Codec tag: `0x01` (PCM), `0x02` (Ogg), `0x03` (MP3) |
| 1 | 4 | Presentation timestamp (ms, LE uint32) |
| 5 | 4 | Payload length (LE uint32) |
| 9 | N | Audio data (codec-specific) |

### 17.3 Audio Output (Intel HDA)

codex-vm emulates an Intel HDA controller with host waveOut output.
The `AudioOutput.codex` module (188 lines) implements the bare-metal
HDA driver:

1. **Reset** the controller via GCTL register
2. **Allocate** DMA buffer (64 KB) and BDL (4 entries x 16 bytes)
3. **Configure** stream format: 48 kHz, 16-bit, stereo
4. **Write** decoded PCM samples to the DMA buffer
5. **Start** stream — controller DMA-reads and outputs via waveOut

Buffer provides ~340ms of audio at 48kHz stereo, sufficient to
absorb network jitter.

### 17.4 Video Rendering

Decoded video frames (raw RGB) are written to a `Framebuf` and
then blitted to the VBE framebuffer via the `Display.codex` module.
The `MediaPlayer.codex` module (340 lines) handles:

- Keyframe + delta frame decoding (using foreword `VideoCodec`)
- Frame scaling (nearest-neighbor) for fullscreen
- Synchronization: video frames are keyed by presentation timestamp
- Player controls: play/pause/stop/seek/volume/mute/fullscreen

### 17.5 Supported Codecs

| Category | Codec | Module | Status |
|----------|-------|--------|--------|
| Audio | PCM (raw) | Built-in | Encode + decode |
| Audio | WAV | `Wav.codex` | Encode (decode: strip header) |
| Audio | MP3 | `Mp3.codex` | Encode (decoder needed) |
| Audio | Ogg Vorbis | `Ogg.codex` | Container encode (decoder needed) |
| Audio | FLAC | `Flac.codex` | Encode (decoder needed) |
| Video | Raw RGB | Built-in | Direct blit |
| Video | MJPEG | `Jpeg.codex` | Encode (decoder needed) |
| Video | Codex Native | `VideoCodec.codex` | Keyframe + delta encode/decode |
| Container | MP4 | `Mp4.codex` | Box/moov construction |
| Container | AVI | `Avi.codex` | Full encode |

The foreword currently has encoders for most formats. Decoders are
the main gap — MP3 decode and JPEG decode are the priorities for
rich media playback. For the MVP, PCM audio and Codex Native video
(keyframe + delta) work end-to-end without additional decoders.

### 17.6 Contrast: Why This Is Different

The legacy web:
- HTML5 `<video>` element with codec negotiation (`canPlayType`)
- MSE (Media Source Extensions) for adaptive streaming
- EME (Encrypted Media Extensions) for DRM — opaque binary blobs
- Codec wars (H.264 vs VP8 vs AV1) driven by patent licensing
- Flash plugin (retired 2020, 25 years of security holes)

Codex Browser:
- Media player IS a compiled page — no special element, no plugin
- Codecs are foreword modules — compiled into the page, verified
- Capability gate: `[Audio]` effect required, compile-time checked
- No DRM — content is content-addressed and trust-verified
- No codec negotiation — the frame's type signature declares what
  it needs, the browser structurally matches

---

## 18. Naming

Working name: **Codex Browser** (or just "the browser" in context).

Candidates for a proper name:

- **Lens** — you look through it to see the web
- **Scroll** — reading a document, unrolling content
- **Folio** — a page, a leaf, a collection
- **Quill** — the tool that reads what was written
- **Beacon** — trust-verified signals in the dark
