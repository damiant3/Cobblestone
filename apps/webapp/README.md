# WebApp -- the shared base for browser apps

The `WebApp` quire is the single home for everything the single-file
browser apps (apps/<name>/<Name>Page.codex, compiled through the HTML
plug) used to duplicate per app:

- `WebRuntime` -- the JS-bridge stub contract (dom-*, state-*,
  register-handlers, register-input-handler, set-render,
  inject-theme-css, mount-widget-themed, play-tone, show-prompt,
  set-timeout). These names bind to the HTML plug runtime via
  `is-html-builtin` in `codex/plugs/html/HtmlEmitter.codex`.
- `WebTheme` -- the eight theme builders (bsn, bn, ez, bdr, eu, exy,
  ws, ss-flat) and `inject-app-style theme css`.
- `WebWidgets` -- `wk-attach`, `widget-box`, `widget-box-click`,
  multi-digit `id-num`, `no-pick`.

An app keeps only: its palette/Theme, its CSS text, its widget tree,
its data helpers, and its click/input handlers.

Adding a runtime primitive: add the JS to HtmlEmitter, rebuild the
plug (`codex/plugs/html/build.ps1`), add the typed stub to
`WebRuntime`, regenerate apps (`build/build-apps.ps1`). One change,
every app gets it.

Design: `design/Done/BaseTemplate.md`.
