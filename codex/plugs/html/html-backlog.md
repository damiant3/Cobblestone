1.1 - The UI foreword defines these and the HTML plug ignores them: widget
shadow, gradient, and accent-border styles; Grid and Split layouts;
`Observable` / `BindingTable` reactive binding; `HandlerTable` /
`EventPath` event routing; `KeyframeSeq` animation; Overlay (tooltip,
popup, context menu, modal); Scroll (`overflow`); Charts and Vector (no
SVG or canvas emission at all). Accessibility has primitives
(`dom-set-aria`, `dom-set-role`) but nothing applies them automatically.

1.2 - The widget event bridge calls `_wkOnClick`, `_wkOnInput` and
`_wkOnPick`, and nothing declares them. `if(_wkOnClick)` on an undeclared
identifier is a ReferenceError, not a falsy test, so the listener dies the
first time a widget is clicked. Emitting `typeof _wkOnClick === 'function'`
guards it; wiring `set-render` and `register-handlers` to real assignments
is the actual fix, since both currently emit as the constant 0.
