# ModBuilder Central

Status: proposal (root, 2026-09-25, from Damian's brief). Site:
`modbuilder.cobblestoneproject.com`. ModBuilder ships free for Valheim first
(Damian, 2026-09-25); a Kickstarter campaign funds the next games.

## The pitch

Mod the games you own. A mod is shared as signed source, built on your own
machine against your own copy of the game, and trusted through the people you
choose to trust, not through a download site. Nobody hands you a DLL.

## The problem it answers

Mod sites distribute opaque binaries. A player trusts the site, the uploader
and every loader in the chain (BepInEx, Harmony, Doorstop) with native code
running beside their saves. A mod that breaks on a game update is fixed only
when its author returns. ModBuilder changes three things:

1. **Source, not binaries.** What travels is Codex source plus a manifest. The
   player's machine compiles it against the player's own installed game.
2. **Receipts.** Every build records the source revisions, the compiler, the
   game's own assembly hashes and the output hash. Two players who build the
   same mod for the same game version get the same DLL hash, and can say so.
3. **A trust lattice instead of a site.** Authors sign; players and reviewers
   vouch; the player's policy decides what installs. No central site holds the
   keys.

## What exists today (measured 2026-09-25)

One game, proven end to end: **Valheim** (Windows x64, Unity 6000.0.75f1,
Mono). Source of truth `apps/modbuilder/design/Active/GameTargets.md`.

| piece | where | state |
|---|---|---|
| Mod source in Codex | `apps/modbuilder/mods/valheim/` (LinkedStorage, MeadowsSpawns, CircumhorizontalArc) | built |
| Feature catalogue and subset builds | `features.json`, Prism `#mod=`, `apps/modbuilder/mods.html` | built; `mods.html` not deployed |
| Codex to typed C# to DLL | `codex/plugs/csharp/CsSyntax.codex`, `codex/plugs/unity/*` with the game bindings in `apps/modbuilder/bindings/valheim/`, Roslyn against the installed game | proven (byte-level DLL comparison with a failing negative control) |
| Loader, no BepInEx or Harmony | owned C++ bootstrap as a `winhttp.dll` proxy, pins game and player hashes | proven in play |
| Isolated test worlds and vanilla revert | `apps/modbuilder/test-game.ps1`, `test-world.ps1`, `run.json` receipts | proven |
| In-play acceptance | arc-only subset build 20260925.022424, accepted by Damian | done; full linked-storage mouse/controller acceptance open (MB-4) |
| Author signing | Prism stage 2c, `codex/plugs/sign/SignStdio.codex` (Ed25519) | built, not yet used by the game pipeline |
| Trust lattice, policy, import gate | `codex/os/trust/TrustLattice.codex`, `PolicyEngine`, `codex/foreword/core/ImportGate.codex` | built, not yet used by the game pipeline |
| Mac output | ARM64 Mach-O writer (PRISM-13) | built; never run on a Mac (13c) |

**Stated limits, carried into the campaign unchanged:** solo play only; Mono
Unity games only (IL2CPP games are out of scope); no sandboxing claim for
native code; no games with anti-cheat or online play.

## Technical plan

Each milestone is a unit the fleet already knows how to prove.

| milestone | work | size |
|---|---|---|
| **M0 Extraction** | Done: the game parts live in `apps/modbuilder/` (mods, game bindings, `GameTargets.md`, `target-toolchain.ps1`, `test-game.ps1`, `test-world.ps1`, `test-mods.mjs`, `mods.html`). The Unity plug stays in `codex/plugs/unity/`, where the plug tooling expects it. `build-bridge.ps1` and `CsSyntax` are shared. The Targets panel and `#mod=` remain in the Prism page until M1 (MB-5). | done |
| **M1 Valheim complete** | Close MB-4's manual acceptance; move the Targets panel and `#mod=` onto a ModBuilder page (MB-5); deploy the ModBuilder page; a game-update flow that re-pins hashes and rebuilds every installed mod from source. | small |
| **M2 Signed source packages** | A mod package = source + manifest + author signature. Install runs `ImportGate` (hash, signature, key score) under the player's `TrustLattice` policy, then builds and compares receipts. | medium |
| **M3 Sharing without a site** | A content-addressed index of packages, served as static files from any mirror; vouches are signed facts synced by `FactSync`; no accounts. | medium |
| **M4 Valheim features** | More features in the Valheim pack beyond storage, meadows and the arc, each one a showcase for the campaign. | ongoing |

M0 to M4 are phase 1: ModBuilder for Valheim, free, needing no funding. The
campaign pays for phase 2:

| milestone | work | size |
|---|---|---|
| **M5 The next game pack** | One binding library for a named Mono Unity single-player game (the `ValheimBindings` pattern), its bootstrap profile and its test worlds. | per game |
| **M6 More packs** | Further games, chosen by backer vote. | per game |
| **Backports** | A feature built for one game is backported to every other game it fits, Valheim included, as it comes online (Damian, 2026-09-25). | ongoing |
| **M7 Mac and Linux** | Run PRISM-13c on a real Mac; Linux hosts. | medium |

## Campaign plan

Root's proposal; every number below is Damian's to set.

**Funding goal and stretch goals**

Every goal and tier buys whatever its budget delivers (Damian, 2026-09-25): the number of games per dollar is found by iteration and by how much code each new game reuses, so no goal promises a count.

The campaign opens once ModBuilder is free and working for Valheim, so every
backer can try the real product before pledging.

| goal | amount (proposed) | funds |
|---|---|---|
| Base | $30,000 | the next game (M5), named on the campaign page, then as many further games as the budget delivers |
| Stretch 1 | $50,000 | more games, chosen by backer vote, as far as the money goes |
| Stretch 2 | $75,000 | Mac and Linux hosts (M7) |
| Stretch 3 | $100,000 | IL2CPP research: a written feasibility study, not a promise |

Kickstarter takes 5 per cent plus payment processing of about 3 per cent plus
a per-pledge fee, so about 8 to 10 per cent of the total comes off the top.

**Reward tiers (digital only: no shipping, no fulfilment cost)**

| tier | pledge | reward |
|---|---|---|
| Supporter | $5 | name on the backers page |
| Early access | $15 | the new game's pack before its public release |
| Founder | $35 | early access plus a founder key vouched into the lattice at launch |
| Game voter | $75 | founder, plus a vote on the stretch-goal game packs |
| Pack sponsor | $250 | credit on a game pack of the backer's choosing |
| Studio | $1,000 | a session with the team to plan a pack for a studio's own Mono game |
| Your game | $5,000 | work on the game the backer chooses, if it is a single-player Unity game on Mono: whatever $5,000 delivers (Damian: every game gets work with a $5,000 pledge) |

**What the campaign needs**

| need | owner |
|---|---|
| Kickstarter account, identity and bank verification | Damian |
| DNS: a CNAME record `modbuilder` pointing at `damiant3.github.io` | Damian (Cloudflare) |
| A Pages repository for the site (like CobblestoneWeb) with a `CNAME` file | root |
| The campaign video (2 to 3 minutes): a mod built live from source into Valheim, the receipt shown, the vanilla revert | Damian records; the fleet scripts the demo |
| Real screenshots of the product for every product claim | fleet |
| Press kit, FAQ, support address, a community channel | root drafts; Damian chooses the channel |
| A per-game review of each game's modding terms before a pack ships | Damian |

**Rules the campaign must keep**

- **AI disclosure.** Kickstarter requires a project to disclose AI-generated
  content. The site's illustrations come from Diffusion Forge and are labelled
  as illustrations; every image of the product itself is a real screenshot.
- **No game assets.** No game logos, screenshots of a game's own art as
  marketing, or trademarks beyond naming the supported game.
- **Honest scope.** Valheim is free and working before the campaign opens;
  every other game is what the campaign funds; the stated limits above appear
  on the campaign page.
- **No redistribution of game code.** The pipeline compiles against the
  player's installed copy and ships none of it; the page says so.

## Timeline (proposed)

1. Phase 1: M0 and M1, then ModBuilder goes live, free for Valheim, on the
   site; M2 and M3 follow, and Valheim features (M4) keep landing.
2. Before the campaign: choose the next game, script and record the video
   against the live Valheim product, draft the Kickstarter page.
3. The 30-day campaign.
4. After funding: M5, then the voted packs.
