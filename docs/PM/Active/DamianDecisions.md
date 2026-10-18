# Decisions for Damian

Every item below waits on Damian and on no one else (sections 0 and 6
measured 2026-10-04; the rest 2026-09-29). Sources: `docs/PM/CurrentPlan.md`
(val's row, "For Damian", and the Prism and OCI rows),
`apps/modbuilder/modbuilder-backlog.md` and `apps/uoaix/UOAIX.md` section
10. When Damian rules on an item, the lane that owns the row applies the
ruling there and deletes the item here.

## 0. Right now

| # | decision | what waits |
|---|---|---|
| 0.1 | **Standing GO (Damian, 2026-10-04) for local development client runs** against the codex-vm server "as long as i am not typing": the driver checks physical input idle before every click and aborts otherwise. Open: nothing; this row is a standing reminder until stages 0 and 1 pass (login through character creation pass; the create packet is reek's fix in progress). | stages 0 and 1 acceptance (`apps/uoaix/GameServer.md`) |
| 0.2 | **Publish the UOAIX page** (Damian, 2026-10-06: "we will deal with publish and release later") on cobblestoneproject.com (built, with the UO art; preview `D:\Projects\Cobblestone-val\apps\landing\web\uoaix.html`). | the public page |

## 6. UOAIX, the shard (`apps/uoaix/UOAIX.md` section 10)

Each row carries root's recommendation (R) where root has one.

| # | id | decision | R |
|---|---|---|---|
| 6.4 | R6 | Host ruled: Vultr, custom ISO. Still open: region, plan size, when the shard goes public; and opening the Vultr account (yours). | the region nearest you; the smallest 1 GiB plan; public after stage 6 |
| 6.5 | UOAIX-84 | Ghost visibility beyond your rule (a ghost is seen when it speaks, or by a player who wins the Spirit Speak contest). ServUO also shows a ghost to every viewer while the ghost is in war mode (the "manifest"), and always to a viewer with Spirit Speak 100.0 or more, with no contest (`D:/Projects/uo-reference/ServUO/Server/Mobile.cs:9230-9232`). Option: add the war-mode manifest. | not added; your rule as written is built |
| 6.6 | stage K | Weapon sub-skills (UOAIX.md section 5, skills: a longsword fighter is no good with a katana until trained). `Skills.md` caps an untrained instrument at 20.0 and lifts the cap only by a master's training, and no master exists in the live shard. Built as written, every fighter's effective weapon skill, British's included, falls to 20.0 at the next boot. Options: (a) every existing character's every weapon starts at the character's current parent skill, and new characters follow the cap; (b) every existing character's most-used weapon kind starts at the parent skill and every other kind at 20.0; (c) masters ship first (a trainer per weapon kind in Britain), then the cap, with (a) for existing characters; (d) no cap until masters exist: the sub-skill grows by use from the parent's level. Held until you choose. | (root) |
| 6.7 | stage A | GM coin favors in game (`favor-coins`, bit 512): a dubbed GM pays real royal-treasury coin to a player's purse. **Land now:** the coin moves through the live currency (`eu-pay` from Lord British's actor 1) and the grant's per-action and per-day coin limits apply, but the GM audit row lives only in the in-memory stand-in (`GmMemory`), so a restart erases who paid whom while the coin stays moved; exposure is one GM's daily coin limit per game day, untraceable after a restart. **Wait for the Codex DB:** coin favors stay refused (today's behaviour); exposure is none, and GMs cannot reward players with coin. | wait, unless you want favors before the database: then land with a small daily limit |

## 1. Time-bound

| # | decision | options | what waits on the decision |
|---|---|---|---|
| 1.2 | **Sitting 18 shape** | accept two boots: first, the diag ladder (sink, vmx); then Ctrl-Alt-Del; finally, the desk image with Damian typing (WORKS-19). Or name a different shape. | red composes the card after blu's repair of the sitting 17 medium loss (`bank-desync`, main 30132). The card must say how one stick carries two images. |

## 2. ModBuilder

| # | row | the decision |
|---|---|---|
| 2.1 | MB-1 | Deferred by Damian 2026-09-29: whether to use the Kickstarter page ("not ripe yet"). |
| 2.2 | MB-4 | Play-test of the features that need progression Damian's character has not reached. |

## 3. Rulings on open rows

Each row carries the options and root's recommendation (R). A one-word answer
("R", or the option letter) is enough.

### 3.11

| # | row | options | R |
|---|---|---|---|
| 3.11 | "a virtual desktop space to move into" (`docs/Designs/Active/OS/ShellRefinement.md` 6.4, Damian 2026-09-07): which meaning | (a) the drag clamp that landed 2026-09-08 (main 23592, recorded then as Damian's reading B): a window drags down until only the window's titlebar stays in the panel, uncovering the windows behind; nothing further is built; (b) several virtual desktops, each holding the desktop's own windows, with a switcher on the taskbar; (c) one desk larger than the screen, with the view panning across the desk | **(a)**, and the 6.4 row closes: the clamp already lets every window move out of the way, and (b) and (c) each add desk-wide state to `GopDesk`, whose `ds` cell block is full |

### 3.12 to 3.14, CORE-12 persisted keys (`codex/foreword/core/core-backlog.md` CORE-12, census by fester 2026-10-07)

| # | site | options | R |
|---|---|---|---|
| 3.12 | `apps/works/DevConsoleBoot.codex:336`: the dev console's signing key comes from `generate-keypair (sha256 ...)`, which feeds 8 digest words to the key as bytes. The seed is a constant, therefore no secrecy is lost either way. | (a) seed the key with `sha256-bytes`: the key changes, and every fact already signed on a dev disk stops verifying until the dev disk is rebuilt; (b) keep the word path | **(a)**: the key matches the 8 test sites fester moved (main 38681), and dev disks rebuild from source |
| 3.13 | `codex/os/kernel/IdentityManager.codex:265`, `idm-derive-random`: the persisted identity's salt and IV ask for 16 bytes and receive 8 digest words above 255, then 8 zeros, which is at most 64 bits of randomness where 128 bits are meant. | (a) convert the digest words to 16 real bytes: an identity created or re-keyed after the change carries a full 128-bit salt and IV; every stored identity keeps working, because each record keeps the record's own `id-salt` and `id-iv` (`IdentityManager.codex:111`, `:165`); (b) keep the word path | **(a)**: nothing stored breaks |
| 3.14 | `apps/secrets/AuditLog.codex:68`, `hash-audit-entry`: each audit entry hashes the entry's text bytes followed by the previous entry's digest words, and `audit-verify-chain` walks the chain from a word-list genesis hash. | (a) move the chain to bytes with a version field in the chain: a log written before the move verifies under version 0, and new entries write version 1; (b) move the chain to bytes without a version: every log written before the move fails verification; (c) keep the word path | **(a)** |

## 4. Blocked on an asset or hardware Damian holds

| # | row | what the row needs |
|---|---|---|
| 4.1 | fishtank 1.1 | A Stable Diffusion endpoint for the two empty sprite assets. |
| 4.2 | WORKS-3 | Diffusion weights for `GopDiffusion.codex`. |
| 4.3 | fishtank 1.5, SPARK-6, FW-2 | A look at each page on a real GPU. |

## 5. Deferred by Damian (no action)

- Prism stage 4, the Claude panel: Damian's API key and a billed call.
- `OracleCloudArm64.md` phases 5b-5d: Damian's OCI account.
- FW-1's three fix options.
- plugs 2.101: cobol, elixir, nim and objc drop a match guard.
- The product quire's rows: held by the customer.
- PRISM-13, a Mac for the Mac target pill (2026-09-30: no Mac in the near term).

### 5.1 CORE-9, our own certificate authority (`codex/foreword/core/core-backlog.md`): DEFERRED by Damian 2026-09-30, the current state is tolerable

What exists: we can mint a self-signed root, issue a leaf under it, and verify
the chain (val, 2026-09-28). **Nothing outside the tests calls any of that
(0 callers, 2026-09-29):** no install, no server and no device carries a
certificate our CA issued. Identity today is `IDENTITY.DAT` on a stick.

**R for all three: defer CORE-9 until a consumer exists** (the first server or
device that needs a certificate we issue). Each answer below depends on that
consumer, and choosing now builds machinery for a system that does not exist
(the WORKS-61 precedent: "do not take it until a CONSUMER exists").

The three questions, for when a consumer exists:

- **Naming.** Today every certificate says `Codex DEV-ONLY CA` and `Codex DEV-ONLY
  issued`. The marker exists so that nobody mistakes a test certificate for a
  real one. The question is only what a REAL certificate says instead.
  (a) keep the DEV-ONLY names forever; (b) a production root named `Cobblestone
  Root CA`, and each device or server named by the fingerprint of its own
  public key rather than by a human name. **R: (b), at the first consumer.**
  A key fingerprint cannot collide or be spoofed by a look-alike name.
- **Revocation** (how a certificate is withdrawn before it expires, for example
  after its key is stolen). (a) a CRL: the CA signs a list of withdrawn serial
  numbers, and each verifier fetches the list; works offline and can ride a
  stick; the list is only as fresh as its last publish. (b) OCSP: each verifier
  asks a live responder about each certificate; always fresh, but needs a
  responder that is always up, tells the responder who is connecting to whom,
  and verifiers usually accept the certificate when the responder is down, so
  the check fails open; Let's Encrypt shut its OCSP service down in 2025 in
  favour of CRLs. (c) short-lived certificates (days, not years) and no
  revocation: a stolen key stops working when its certificate expires, and the
  CA reissues automatically. **"X509Chain checks one"** means the verifier
  refuses a withdrawn certificate; without the check, a stolen key stays
  trusted until its notAfter date. **R: (c)**, with (a) added only if a
  consumer needs certificates longer-lived than a few days. Not (b).
- **Where the CA private key lives.** (a) a file on the issuing machine
  (simplest; one compromise of that machine forges everything); (b) on a
  dedicated stick, encrypted under a passphrase, inserted only to sign (the
  "offline root" the industry uses; fits the stick model we already fly);
  (c) in hardware (a TPM or a security key): strongest, and a dependency we
  did not build. **R: (b), two-tier**: the root key lives on its own stick and
  signs an intermediate CA key; the intermediate lives on the issuing machine
  and issues the short-lived certificates. Losing the intermediate costs one
  re-sign, not the root.
