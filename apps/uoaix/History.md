# UOAIX: the lineage

UOAIX, the Ultima Offline AI eXperiment, takes its name from UOX, the
Ultima Offline eXperiment: the first Ultima Online server emulator. Damian
decoded the packets UOX was built on, and the protocol document he wrote is
the root of the packet guides the community used for the next decade.

## The record

| when | what | source |
|---|---|---|
| 1997 | Jaegermeister (Denny Zuko) writes the first UOX for the pre-alpha client, starting from a client decryption key obtained from friends at Origin; the project is abandoned ahead of the beta and release clients, and Cironian rewrites it from scratch as UOX2 | UOX3 emulator timeline |
| 1997 | Damian decodes the packets for Jaegermeister's UOX and writes `UOPackets.doc`, titled "UOX Protocol" | Damian, 2026-10-04 |
| 1997-09-24 | Origin Systems releases Ultima Online | Wikipedia, "Ultima Online" |
| 1997-10-22 | First public UOX3 release, version 0.37 | UOX3 emulator timeline |
| early 1998 | Menace (Dennis, of Menasoft) creates GreyWorld, the C++ line of UOX; Damian works on it with him | UOX3 emulator timeline; Damian, 2026-10-04 |
| after Damian leaves | GreyWorld becomes TUS (The Ultimate Server), which becomes Sphere (SphereServer) around April 2000 | UOX3 emulator timeline; Damian, 2026-10-04 |
| 2000-05-19 | Jerrith's UO Packets Guide (`JUOPackets.doc`), whose introduction says: "This guide is based on the UOPackets.doc document "UOX Protocol" prepared by Damian." | Jerrith's guide, read 2026-10-04 |
| about 2001 | Wolfpack, grown from Ripper's customized UOX3; Damian works on it with a German developer and Thiago (Thiago A Correa, a Wolfpack developer in June 2001) | UOX3 emulator timeline; Damian, 2026-10-04 |
| 2001-11-09 | Part of the Wolfpack team leaves to start Lonewolf | UOX3 emulator timeline |
| 2026-10-04 | UOAIX starts: a new server written in Codex for the 1.25.32 client, with an AI keeper and agent townsfolk (`UOAIX.md`) | this project |

## The original `UOPackets.doc`

Not yet found (searched 2026-10-04). Where the search went:

- **The packet guide archives** at `download.uo98.org/Documents/Packet Guides/`
  and its mirror `mirror.ashkantra.de/joinuo/Documents/Packet Guides/` hold
  Jerrith's derived guide (`JUOPackets.doc`, the 2000-05-19 `.doc` and `.htm`
  versions) and later guides, but not `UOPackets.doc` itself.
- **The Wayback Machine's index** lists `jerrith.com/projects/JUOPackets.doc`
  (captured 2005-01-17). It has no packet document under `uox.org`,
  `uox3.org`, `cironian.com`, `torfo.org`, `menasoft.com` or
  `grayworld.com`. Further lookups were refused with rate limiting (HTTP
  429), and the UOX3 timeline page sits behind a browser check.
- **SourceForge** (opened November 1999): the UOX3 project's oldest source
  releases (`uox_a20010324_source.zip`, `UOX3WC00001src.zip` in its
  `OldFiles`) carry no document and no mention of Damian, `UOPackets` or
  Jaegermeister. Wolfpack's areas (`wpdev`, `wolfpack`) start at 2002
  releases and were not opened.
- **Whole-domain Wayback scans** of `geocities.com`, `members.aol.com`,
  `tripod.com` and `angelfire.com` for a `UOPackets` file are refused or
  time out (HTTP 503 and 504): those domains are too large to scan whole,
  so a lookup there needs the original page's address. In 1997 the UOX
  developers hosted their sites and servers on their own home machines
  (Damian, 2026-10-04), which the Wayback Machine rarely captured.
- **Leads not yet tried:** 1997 Usenet archives
  (`rec.games.computer.ultima.online`), and the hosting the 1997 UOX pages
  used (GeoCities and similar personal sites), each through the Wayback
  Machine.

When the original is found, the copy goes in this directory as
`UOPackets.doc` with its source and capture date.

## Sources

- Jerrith's UO Packets Guide: `http://uo.torfo.org/packetguide/`; archived
  copies under `download.uo98.org/Documents/Packet Guides/`
- UOX3 emulator timeline: `https://www.uox3.org/history/timeline.shtml`
- POL's history of the emulators: `https://www.polserver.com/history/`
- Wikipedia, "Ultima Online": `https://en.wikipedia.org/wiki/Ultima_Online`
