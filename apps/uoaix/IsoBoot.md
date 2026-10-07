# Dual BIOS and UEFI ISO

`build-iso.ps1 -BiosCdx <cdx> -UefiImage <img> -Nasm <nasm.exe> -Out <new.iso>`
packages a BIOS runtime payload and the UEFI image's first ESP into an
El Torito ISO. The builder verifies CDX section bounds/tiling, content hash
and expected multiboot trampoline addresses. The UEFI input must be the
GPT/FAT16 image emitted by `build/build-img.ps1`, with the ESP first and
at most 65535 sectors. The builder checks extraction bounds; it is not a
general GPT repair or validation utility. Supply a native Windows NASM.
No Unix toolchain is required. Output paths must be new.

The ISO has a BIOS no-emulation entry and a UEFI platform entry referring
to the FAT volume. Layout follows the [Phoenix/IBM El Torito specification](https://read.seas.harvard.edu/~kohler/class/04f-aos/ref/hardware/boot-cdrom.pdf).
The BIOS loader uses extended INT 13 reads, INT 15 block copies, A20 and
E820, then enters the compiler's existing 32-bit trampoline. It reports
the contiguous usable RAM extent above 1 MiB through the runtime RAM cell,
capped at 3 GiB. A missing suitable E820 region, disk error or copy error
refuses boot. The loader admits a minimum 768 MiB region end; the deployment
proof runs at 1 GiB. The raw BIOS payload is bounded to 16 MiB.
E820 traversal is capped at 256 entries and skips entries whose returned
extended attributes mark the region disabled.

BIOS payloads use the normal CDX startup. UEFI payloads use the PE opening
entry and initialize the runtime after ExitBootServices. These are distinct
entry paths inside the same ISO, not byte-identical executable payloads.
Build both from the same application source with the corresponding startup
wrapper. The builder does not prove semantic equality of arbitrary inputs.
The receipt records both input hashes, the assembler hash and the ISO hash.

The optical image is read-only. Persistent state belongs on a separate
writable virtio disk. The ISO builder copies only the UEFI ESP and the BIOS
payload; it does not include the GPT data partition. Installation and operator
data provisioning remain separate work. Do not publish operator client data.

## Storage proof

Build BIOS and UEFI variants of the combined TownStore proof, then run
`proofs/test-iso-storage.ps1 -Iso <iso> -DataTemplate <synthetic GPT image> -OutDir <new directory>`.
The BIOS variant omits the opening's explicit `runtime-init 0`, since normal
CDX startup already initializes the runtime. The UEFI variant is emitted by
`test-uefi-storage.ps1 -CombinedTown` and uses its usual PE flags.

From the repository root, use new directories and an installed NASM path:

```powershell
$uefiProof = Join-Path (Get-Location) 'build-output/uoaix/iso-uefi-input'
$isoProof = Join-Path (Get-Location) 'build-output/uoaix/iso-input'
$assembler = 'C:/Tools/nasm/nasm.exe'
pwsh -File apps/uoaix/proofs/test-uefi-storage.ps1 -Kernel seed/Codex.cdx -CombinedTown -OutDir $uefiProof
if ($LASTEXITCODE) { throw 'UEFI input proof failed' }
New-Item -ItemType Directory $isoProof -ErrorAction Stop | Out-Null
$source = [IO.File]::ReadAllText("$uefiProof/uefi-storage.codex")
$line = '    initialized <- runtime-init 0'
if ([regex]::Matches($source,[regex]::Escape($line)).Count -ne 1) { throw 'Startup line ambiguous' }
[IO.File]::WriteAllText("$isoProof/bios.codex",$source.Replace($line,''))
pwsh -File build/compile.ps1 -Src "$isoProof/bios.codex" -Out "$isoProof/bios.cdx" -Log "$isoProof/compile.log" -Kernel seed/Codex.cdx
if ($LASTEXITCODE) { throw 'BIOS compile failed' }
pwsh -File apps/uoaix/build-iso.ps1 -BiosCdx "$isoProof/bios.cdx" -UefiImage "$uefiProof/pristine.img" -Nasm $assembler -Out "$isoProof/uoaix.iso"
if ($LASTEXITCODE) { throw 'ISO build failed' }
pwsh -File apps/uoaix/proofs/test-iso-storage.ps1 -Iso "$isoProof/uoaix.iso" -DataTemplate "$uefiProof/pristine.img" -OutDir "$isoProof/acceptance"
```

The harness boots the same ISO under QEMU TCG BIOS and OVMF, each at 1 GiB.
Each mode saves and recovers in separate VM processes. The data fixture's
ESP is cleared, preventing a fallback boot from the virtio disk; UEFI serial
also must identify DVD-ROM boot. The exact combined-state oracles, unchanged
recovery hashes and outside-partition byte comparison must pass. Firmware
variables are fresh on each UEFI boot. The ISO hash must remain unchanged.

The boot loader uses fixed low-memory scratch and 32 KiB transfers. Builder
heap/time are linear in input/output image bytes and retain whole-file arrays.
The host proof retains two disk arrays and compares bytes linearly. Runtime
heap behavior remains the selected payload's contract.

The proof establishes synthetic storage boot/recovery, not a complete shard
ISO, KVM or Vultr acceptance. Game/admin composition, DHCP, authenticated port
acceptance, installation, operator data and interrupted-commit recovery remain
stage-D work. codex-vm's UEFI disk-image proof is separate from this QEMU
optical boot proof.
