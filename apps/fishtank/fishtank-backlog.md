# FishTank -- open capabilities

App-domain register, same rules as the others: an entry says what is still
missing and nothing else, a closed entry is DELETED, and a gap that is still
real is never quietly dropped.

Opened 2026-08-15 by red, after Damian asked why the tank "looks like
fingerpainting".

## 1.1 -- TWO SPRITE ASSETS ARE EMPTY, and they are the fingerpainting

**The art is not the problem and neither is the shader.** The clownfish,
guppy, angelfish and the reef backdrop are photographic and cleanly cut out
with transparent-BLACK edges (measured: transparent pixels are `A=0 R=0 G=0
B=0`, body pixels vivid). Two of the thirteen atlas cells are blank, and the
renderer draws the dead shrimp four times and the dead coral twice. Pale
smears over a dark tank is what that looks like.

Measured per asset, opaque pixels against pixels sampled:

| asset | opaque | verdict |
|---|---|---|
| `fg-coral-left.png` | **2 / 22,176 = 0.0%** | EMPTY |
| `creature-shrimp.png` | **~70 / 14,400 = 0.5%** | EMPTY |
| `fg-rock-right.png` | 1,643 / 23,716 = 6.9% | sparse, real |
| `fg-coral-center.png` | 3,305 / 22,176 = 14.9% | sparse, real |
| `fg-rock-left.png` | 10,601 / 23,716 = 44.7% | good |
| `fg-coral-right.png` | 10,499 / 22,176 = 47.3% | good |

**Rebuilding the atlas offline and looking at it is what made this obvious**
-- eleven vivid cells and two blank ones. That is the instrument to reach for
first if the tank ever looks wrong again; it costs one script and answers in
one glance. Pack order is `fishTexNames.concat(fgTexNames)` into 2048x2048,
each cell scaled to fit 512.

**No code change fixes this.** Both assets must be regenerated:

```powershell
apps/fishtank/generate-creatures.ps1     # Stable Diffusion, 127.0.0.1:7860
```

That endpoint was DOWN on 2026-08-15, which is why it was not done. Two
uncut sources exist for the shrimp and both need background removal:
`creature-db/shrimp/reference/side.png` (vivid, white background, soft
shadow) and `assets/creature-shrimp.png` (vivid red-and-white cleaner shrimp,
dark background, cropped at the frame edge). Hand-rolling that removal in
PowerShell was tried and abandoned: the white-background cut leaves holes in
the body and an unremoved patch inside the tail curl, because the shrimp's
belly is as bright and as neutral as the backdrop.

Until they are regenerated, red 15322 (dev stream) stops both from being
drawn. The species and both texture names stay DEFINED, so a regenerated
asset drops straight back in with no code change.

## 1.2 -- The v2 renderer cannot be seen without a headed browser on a real GPU

Nothing in the tree renders this app, and the two obvious routes are both
dead ends. Worth writing down because each costs half an hour to rediscover.

- **`web/fishtank.html` IS NOT A PAGE.** It is the FishTankPage emitter
  transpiled to JavaScript: opening it runs `print_line(...)` and prints the
  aquarium's SOURCE into the document, which is why a browser shows a wall of
  text. It is not corrupt and it is not mis-typed.
- **`web/fishtank-wasm.html` used to hang at "Initializing WebGPU + WASM"
  and that is FIXED (main 21967).** The attribution here was wrong: the
  opening was never a stub, and the module runs. Three faults, each hiding
  the next. The emitted JS was a SYNTAX ERROR, because 22 of the bridge's JS
  numeric literals were written with Codex's `#` hex prefix. `print-line-raw`
  terminates with the raw serial byte 10, which decodes to the character `7`
  inside a CCE payload and glued `7function` onto the next token. And the
  page's import object supplied `fd_write` but not the `fd_read` the module
  also requires, which is a LinkError at load. Measured after: 52 fish after
  `init_aquarium`, all 8 sampled fish moving over 60 ticks.
- **Headless Chrome cannot render it at all.** WebGPU never initialises and
  the canvas stays black, with `--enable-unsafe-webgpu`,
  `--use-webgpu-adapter=swiftshader` and `--use-angle=vulkan` all tried.
- **A headed capture is what works**, and the trap is that a background
  process cannot take the Windows foreground, so `CopyFromScreen` silently
  captures whatever window has focus -- the terminal -- and returns a
  plausible PNG. Check the shot; do not assume it.

What DOES work: serve `apps/fishtank/web` over HTTP, assemble a shell page
that carries the seven DOM ids `fishtank.js` expects (`c`, `fish-count`,
`fps`, `species`, `status`, `temp`, `tod`) and loads it, open it headed, and
capture only once the window is genuinely frontmost. `web/fishtank.js` is the
standalone v2 renderer and needs no WASM.

**A `run.ps1` for this app would retire this entry.** There is none.

## 1.3 -- The page emitter prints CCE, so its output is not usable as HTML

`FishTankPage`'s `opening` uses `print-line-raw`, so compiling it and running
it under codex-vm yields CCE bytes rather than UTF-8, and the captured
"HTML" is mojibake. The transpiled-to-JS route in 1.2 is the reason the
checked-in page exists at all. Either the emitter should print through the
Unicode path, or the build step that captures it must decode CCE.

## 1.4 -- Verify red 15322 on screen, then copy it up

Submitted to `//Codex/red` only, deliberately: `node --check` passes on the
emitted JS but the after-shot was never captured, for the reasons in 1.2.
Three changes, all in `FishTankBridge.codex` with `web/fishtank.js` kept in
step:

1. **The atlas destroyed aspect ratio.** It clamped each axis independently
   (`Math.min(img.width,512)`, `Math.min(img.height,512)`), so every 576x768
   and 768x576 foreground sprite was packed into 512x512 and drawn about a
   third wrong in one axis. Now scaled by the longer side. This one is a real
   bug and it affects the GOOD sprites too, so it is worth keeping whatever
   is decided about the dead assets.
2. The two dead `fg-coral-left` placements removed.
3. The dead shrimp school no longer spawned. Shrimp is species index 7, the
   last entry, so no other index shifts.

## 1.5 -- OPEN (Damian, 2026-09-02): the wasm page RUNS and still looks bad

With the load faults in 1.2 fixed, Damian looked at the page and the verdict
was that it and spark both "need a lot of work" and stay off the public site
until they are brought up. That is a judgement on what is on screen, not on
whether the module ticks, and NOTHING here diagnoses it: no cause has been
measured and none should be guessed. What is known is only that the
simulation runs. Whoever takes this looks at the page first and writes down
what is actually wrong before changing anything.
