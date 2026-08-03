# WebApp Base Template

Status: landing (first CLs 2026-06-10). Owner: reek.

## Problem

44+ app directories grew by free-form accretion. The ~22 single-file
browser apps (apps/<name>/<Name>Page.codex -> web/<name>.html via the
HTML plug) each carried a verbatim copy of:

- the JS-bridge runtime stubs (~15 functions; 19/22 apps identical),
- the eight theme builders (22/22 identical),
- the style-injection plumbing (22/22 identical shape),
- wk-attach / widget-box / widget-box-click (19/22 identical),
- id-num trailing-number parsing (16 apps, two variants -- one of
  which silently breaks past index 9),
- no-pick (21/22).

Consequences: every new runtime primitive (button clicks CL 3629,
clickable boxes CL 3630, text input CL 3635, audio CL 3640) had to be
stubbed into N files by hand, and nothing proved the copies agreed
with the runtime HtmlEmitter actually ships. A fix could not be
verified once -- it had to be checked per app. With one person testing
44 apps, that is the difference between a generic fix and no fix.

## Design

One quire, `WebApp` (apps/webapp), registered in build/quire-map.ps1:

| Chapter | Contents | Why it is one thing |
|---|---|---|
| WebRuntime | dom-*, state-*, register-handlers, register-input-handler, set-render, inject-theme-css, mount-widget-themed, play-tone, show-prompt, set-timeout | THE stub contract. HtmlEmitter binds calls by function name (`is-html-builtin`), so the definitions are location-independent; this chapter is the single source of the names and types. |
| WebTheme | bsn, bn, ez, bdr, eu, exy, ws, ss-flat, inject-app-style | Pure constructors over UI Theme types + the init plumbing. |
| WebWidgets | wk-attach, widget-box, widget-box-click, id-num (multi-digit), no-pick | Tree constructors over UI Widget. The multi-digit id-num supersedes the single-digit variant some apps carried. |

An app keeps: palette/Theme value, CSS text, widget tree, data
helpers, handlers, `opening`. An app's entry shrinks to:

```
opening = act
  let t = inject-app-style <app>-theme <app>-css
  in let s = set-render render-<app>
  in let h = register-handlers no-pick <app>-click
  in render-<app> 0
end
```

### What stays per-app, deliberately

CSS strings and Theme palettes (brand identity), widget trees, handler
logic, domain data helpers. No attempt to abstract the tree shape --
17/22 apps share a view-branching pattern, but forcing a generic
page-shell would couple unrelated apps to one layout's churn.

### Deviants

- ChatPage: server-gated (fetch/polling stubs, ChatTheme chapter);
  port together with ChatServer work.
- BridgeWebPage (helm): composes shared Helm chapters already; cites
  WebApp where useful, no forced restructure.
- FishTankPage: print-line HTML generator, not a widget app. Out of
  scope.
- Multi-file apps carry the same stub block in their *Theme.codex
  chapters (ChatTheme, ExplorerTheme, MagicTheme, MobileTheme) -- port
  in the per-app pass.

## Test story (the point of the exercise)

1. Base conformance battery in codex/test/apps: pure-logic .expected
   tests for WebTheme builders, id-num, wk-attach/widget-box
   construction. Testing the base once tests all consumers.
2. The build is the test: build/build-apps.ps1 across the manifest is
   the compile gate for every port CL.
3. A post-build assertion script greps every generated web/*.html for
   runtime invariants (handler wiring present, theme injected) -- the
   browser-facing check that does not need a browser, applied
   uniformly.

## Rollout

1. CL: quire + design doc + proof port (photos, news), artifacts
   regenerated. (this CL)
2. CLs: port remaining single-file apps in batches of 2-4,
   regenerating each batch (p4 edit the .html, LF->CRLF normalize).
3. CL: manifest-driven build-apps.ps1 + regen hygiene automation.
4. CL: conformance battery + HTML assertion script.

## Memory and time-complexity verdict

The quire moves existing definitions; it adds none. Bundles gain three
chapter headers and lose the same definitions from the root chapter --
net source size per app DECREASES (ports delete more than the cites
add). No new loops, no new accumulation, no recursion without a base
case (wk-attach/id-num-loop are the existing bounded recursions).
Compile-time heap unchanged within noise; verified by compiling ported
apps through the standard pipeline.
