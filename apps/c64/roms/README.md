# C64 ROMs: MEGA65 Open ROMs

`RomData.codex` and `c64-roms.disk` hold the Open ROMs of the MEGA65 project,
free replacement ROMs for the Commodore 64. Thank you to the MEGA65 team:
<https://mega65.org> and <https://github.com/MEGA65/open-roms>.

Copyright Paul Gardner-Stephen, 2019; copyright Roman Standzikowski
(FeralChild64), 2019-2021. They are free software under the GNU Lesser General
Public License, version 3 or later (`LICENSE`, `COPYING.LESSER`, `COPYING`); some
BASIC files are MIT licensed (Microsoft), as `LICENSE` states.

The binaries are the project's generic set, taken from commit
`ad178dbe4d48cd6a317737a8e0e7e662f7e33d32` of <https://github.com/MEGA65/open-roms>, where their
corresponding source is:

- `bin/basic_generic.rom`, SHA-256 `54A1464B4B27C9DC61BBD62A818FDD12EC99AF9089111005A5ADD0AD0E6BD5EC`
- `bin/kernal_generic.rom`, SHA-256 `88E86ED3D0C710EDAB8F90AD146FAA8DE1EAD11F43494B176C7B54724CA721C6`
- `bin/chargen_openroms.rom`, SHA-256 `5E3451466841B93DF7E01E4B635B07B8D8633351BAE483B1961D96B3131186E7`

Regenerate with `apps/c64/build-rom-disk.ps1` and `apps/c64/build-rom-data.ps1`.
