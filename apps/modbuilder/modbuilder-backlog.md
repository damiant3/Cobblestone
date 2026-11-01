# ModBuilder -- open work

The app's register. The plan, the milestones and the campaign are
`docs/Designs/Active/Apps/ModBuilder.md`; a closed row is deleted.

| # | item | state |
|---|---|---|
| MB-1 | **Deferred (Damian, 2026-09-29): the separate Kickstarter campaign page is reviewed and "looks good, if we decide to use it"; whether to use it is his call later, "not ripe yet".** `site/ModBuilderPage.codex` and generated `site/index.html` keep the draft ribbon until then. The player workspace is published at `cobblestoneproject.com/modbuilder/`; `website.md` owns that route. | Deferred |
| MB-4 | **Valheim features that need progression Damian's character has not reached are untested in play.** Damian played the browser-built and native pack on 2026-09-29: the features he can reach work and are decent for now. The rest waits on a character with the materials and stations those features need. | open |
| MB-22 | **Test coverage: 27 of 27 library chapters are reached by no computing test** | `pwsh build/coverage-census.ps1 -Tree apps/modbuilder -List` names them (2026-10-26). Open: a computing test for each, or a deletion. |
