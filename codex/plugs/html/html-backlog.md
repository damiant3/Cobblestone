1.1 - The UI foreword defines these and the HTML plug ignores them: widget
shadow, gradient, and accent-border styles; Grid and Split layouts;
`Observable` / `BindingTable` reactive binding; `HandlerTable` /
`EventPath` event routing; `KeyframeSeq` animation; Overlay (tooltip,
popup, context menu, modal); Scroll (`overflow`); Charts and Vector (no
SVG or canvas emission at all). Accessibility has primitives
(`dom-set-aria`, `dom-set-role`) but nothing applies them automatically.

1.3 - `__record-set` emits an updated copy rather than mutating its target.
A call whose result is discarded leaves the original record unchanged in
HTML, unlike native Codex. Observed on 2026-09-19 with a stack constructor:
`const kind = {...node, wn_kind: WkStack}; return node` returned WkPanel.
The stack now uses a direct constructor; general mutation parity remains open.
Source: `HtmlEmitter.codex`, `emit-js-record-set`. Evidence:
`D:/Projects/Cobblestone-root/build-output/box-uplift/stack-gallery-before.html`
and its DOM negative control. Widget callback declarations and handler registration are
present; those are no longer the outstanding event-bridge defect.
