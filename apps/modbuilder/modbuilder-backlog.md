# ModBuilder -- open work

The app's register. The plan, the milestones and the campaign are
`docs/Designs/Active/Apps/ModBuilder.md`; a closed row is deleted.

| # | item | state |
|---|---|---|
| MB-1 | **Deferred (Damian, 2026-09-29): the separate Kickstarter campaign page is reviewed and "looks good, if we decide to use it"; whether to use it is his call later, "not ripe yet".** `site/ModBuilderPage.codex` and generated `site/index.html` keep the draft ribbon until then. The player workspace is published at `cobblestoneproject.com/modbuilder/`; `website.md` owns that route. | Deferred |
| MB-4 | **Valheim features that need progression Damian's character has not reached are untested in play.** Damian played the browser-built and native pack on 2026-09-29: the features he can reach work and are decent for now. The rest waits on a character with the materials and stations those features need. | open |
| MB-12 | **The rendered world probe fails its linked-storage panel check.** `test-world.ps1 -Render` (create mode) on the 2026-09-26 02:08 mod source ends `PRISM WORLD TEST FAIL` at "combined slots use the visible native container panel", after every HUD check passes; the same source passes without `-Render`. | open |
| MB-13 | **The Shovel and hoe pack's open edges.** The decor ornamentals (bushes, ferns, vines) show the Replant icon in the hoe menu; only the pickables have their harvest's icon. The `tools` card has no illustration. `vines` is placed as the game's own prefab and its size in the open world is unmeasured. Dig is graded on its terrain settings (`m_raiseDelta` -0.5), not on a measured height change. | open |
