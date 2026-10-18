# UOAIX: player-made dungeons

Owner: none yet. Status: idea, being written with Damian; nothing ruled beyond his words below, nothing built
(UOAIX-243).

Damian, 2026-10-12: "an idea to start writing down with me is the way to get players involved in content creation and
event planning. my idea is letting the decorators/playwrights earn "spawners" that let them decorate their own dungeon
in game, collect ticket sales to go in, let players join, run the dungeon, and then rate them for public guidance to the
proper/best crawls."

## The loop

1. **Earn.** A decorator or playwright earns spawners.
2. **Build.** The author decorates a dungeon of their own in game with the decorator's tools (`UoaixDecorator.md`) and
   places spawners in it.
3. **Sell.** The author sets a ticket price; entry takes a ticket, and the ticket sales go to the author.
4. **Run.** Players join a crawl and run the dungeon.
5. **Rate.** Players who ran it rate it; the ratings are public and guide players to the best crawls.

## What UOAIX already has

- The decorator: placement, move, remove, marks, land editing, blocking (`UoaixDecorator.md`). Today it is a GM aspect
  granted by the owner (`apps/uoaix/GmContract.md`, power 8192 `decorate`).
- Plays: playwright roles, sets (named bundles of decorator placements in one rectangle), casts and cues
  (`UoaixPlays.md`). Also GM aspects today.
- Spawning: regions, primary spawners and their descendants (`apps/uoaix/WorldSpawns.md`).
- Tickets as shop income (`apps/uoaix/BritainShops.md`).
- Player housing, a placed deed's house and its plot (`apps/uoaix/UOAIX.md`, "Houses come from deeds").

## Questions for Damian

| # | question | root's lean |
|---|---|---|
| Q1 | Who can author: any player who takes up the trade, or only a player the owner dubs decorator or playwright (today's GM aspects)? | A player trade with a limited tool set, so authoring is play, not staff work. |
| Q2 | How is a spawner earned: crafted, bought, quest reward, or paid from ticket income and ratings? | Earned from completed, well-rated crawls, so good authors grow. |
| Q3 | Where is a dungeon: a private instance (a copy per party), a fixed plot in the world (a dungeon deed, like a house deed), or both? | A deeded plot under the world, entered through a door the author owns. |
| Q4 | What may a spawner raise: any creature, or a list bounded by the spawner's tier, with a cap on total challenge? | Tiered list and a challenge budget per dungeon. |
| Q5 | Loot: does the dungeon drop loot, who pays for it (the author's stock, the world, nothing), and how is a farm prevented (an author running alts through their own dungeon)? | Loot comes from the author's own chests; no world loot. |
| Q6 | Death and risk inside: the world's normal death rules, or a safe crawl? | The world's rules. |
| Q7 | Ratings: who may rate (only a player who entered and finished or died inside), what scale, and how does the list rank (average, count, recency)? | Only ticketed runners, 1 to 5, ranked by a confidence-weighted average. |
| Q8 | Plays inside dungeons: does a playwright's cast and cue system (`UoaixPlays.md`) run inside, so a crawl can be a staged story? | Yes, the same set and cue records. |
| Q9 | Where do players find crawls: a board in each city, a crier, the web admin's public pages? | A dungeon board in each city's inn. |
