# Desktop and web font pack

Inter is the default UI family. Source Sans 3, Atkinson Hyperlegible Next,
IBM Plex Sans and Noto Sans provide alternative appearances. IBM Plex Mono
is the code face for the desktop and web. All imported faces are static TrueType fonts under
SIL Open Font License 1.1; `font-pack.json` pins source revisions, hashes,
weights and notices. Redistribution must retain the accompanying notices.

## Preview

From the repository root in PowerShell:

```powershell
build/desk.ps1 -ListFonts
build/desk.ps1 -FontName inter
build/desk.ps1 -FontName atkinson
build/desk.ps1 -FontName plex
```

The launcher copies `seed/Codex.img` into `build-output` before selecting
the face. Close the previous desktop before launching another choice.
The Cobblestone button opens the app menu. Desktop chrome and linked app
panes share the selected UI face. Editor source and file source/hex previews
use Plex Mono; cursor and syntax positions use that face's measured advance.
Files and Editor listings use separate, clipped name and size columns.
Both font atlases load before the desktop's app heap marks and survive app
close/reopen. Legacy images retain bitmap fallback for a missing face.
Font selection applies at launch; changing
a live session's retained font allocations is not supported.

```powershell
codex/plugs/html/build.ps1
codex/plugs/html/run.ps1 -Src apps/notes/NotesPage.codex -Out build-output/notes.html -FontName inter
```

Open the resulting HTML in a browser. The shared HTML runner embeds the
selected family's weights, Plex Mono and license notices in the document.
No font server or locally installed typeface is needed. App CSS can still
select a specific font for a specialized element.

## Boot image

```powershell
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build-output/Codex.codex
build/build-boot-img.ps1 -Out build-output/Codex-fonts.img -FontName inter -Kernel seed/Codex.cdx
```

The default 32 MiB image carries all twelve faces, notices, `FONTS.JSN` and
`UIFONT.CFG`. The last file is a 16-byte zero-padded ASCII FAT filename.
`-FontName` chooses the initial face. An explicit `-Font` without `-FontName`
retains the legacy single-font image path; `-Font ''` exercises bitmap fallback.
Without a target identity, the hardware image starts the first-boot wizard.

## Native coverage diagnostic

```powershell
build/compile.ps1 -Src apps/works/FontPackCheck.codex -Out build-output/font-check.cdx -Log build-output/font-check.log -Kernel seed/Codex.cdx
tools/codex-vm.exe -kernel build-output/font-check.cdx -disk build-output/Codex-fonts.img -mem 3072 -headless -output build-output/font-check.out
Get-Content build-output/font-check.out
```

`FontPackCheck` checks all twelve image filenames and visible ASCII codes
33 through 126 at 16 ppem. Success prints twelve `PASS` rows and returns zero.
A missing/invalid face or empty/unresolved glyph prints `FAIL` and returns a
positive failure count. VM debug exit zero normally maps to host process exit
one; judge the guest result and diagnostic output. Scratch is reclaimed after
each glyph and face.

The native renderer supports static TrueType outlines and compound glyphs.
Hint bytecode, variable fonts, CFF, color glyphs, complex-script shaping and
full Unicode text layout are outside the current desktop path. Imported
families contain broader character coverage than the ASCII desktop atlas uses.
Font selection does not repair existing widget sizing or clipping defects.
