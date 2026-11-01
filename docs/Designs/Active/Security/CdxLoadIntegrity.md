# CDX load integrity: the open gaps

Owner: reek. These are the only open items left from the SignatureSlippage
campaign; every other row there is landed or settled with no defect.

## 1. The signature does not cover the header -- CLOSED (option A)

CDX format version 2: the content hash at bytes 8..39, which the Ed25519
signature covers, is SHA-256 of header bytes 0..7 and 136..223 followed by the
SHA-256 of the body, so flags, section table, entry, stack, heap and trust
threshold are signed. Every verifier refuses any other version.
`codex/test/apps/verify-cdx-at` arms `entry-edit`, `table-edit`, `version` and
`flag-set` edit those fields without re-signing; under the version 1 verifier
the entry and version edits were accepted.

## 2. Verify and grant read the image twice (at-address load) -- CLOSED (option b, root's pick 2026-10-31; Damian may correct)

`cdx-private-copy` (`CdxVerifier.codex`) copies the image once into the heap,
and `evaluate-load-at` (`VerifiedLoader.codex`) verifies the copy, grants from
it and names it as the bytes to run (`load-image`); `cdxc-chain`
(`apps/works/CdxChain.codex`) checks and chains from the copy. A copy that
would not fit the heap (RAM size at 4072 less the boot stack's 33 pages of
2 MiB) is refused before any allocation. Option (a) was refused because the
verifier's own metadata read already followed the hash, so it closed no
window a writer could not reach another way.

`codex/test/apps/verify-cdx-at` arm `after-load write`: a write to the
original after approval leaves the bytes to run equal to the verified image;
with the original named instead, as before, they differ. Arm `no room`
refuses a 1 TiB length, which was approved before. Cost on the seed
(3,699,981 bytes, 2026-10-31, two runs): heap +13,000 bytes and about 45.5 ms
before, +3,712,981 bytes and about 52.7 ms after (HPET 14,318,180 Hz). The
copy is never freed: a caller that loads in a loop restores its own heap mark.

Open: (c), mapping the region read-only or quiescing the writing device, for
a device-writable region once OTA activation has a production consumer and
an image-sized copy is too dear.

## 3. The firmware step trusts a verdict passed in -- CLOSED (option a, root's pick 2026-10-31; Damian may correct)

`fw-execute-update` (`codex/os/net/Lwm2mFirmware.codex`) takes the trust
lattice, policy, time, fact store and fuel, and runs `evaluate-load-at` on its
own staged bank (`fw-stage`, `fw-written`); no verdict is passed in. `fw-new`
records the slot the bank is staged for (`fw-slot`), and Gate B refuses when
the boot config's inactive slot is no longer that slot.
`codex/test/apps/ota-lwm2m-loopback` arm `moved slot` refuses a moved config
(result 8); before the change a verdict computed on another image activated
a foreign staged image (result 1).
