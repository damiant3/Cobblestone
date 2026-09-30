# mint-clip-bpe-disk.ps1 -- the disk codex/test/apps/clip-bpe-forge reads: CLIP's
# vocab.json and merges.txt, byte for byte, behind a one-sector header.
#
#   pwsh build/mint-clip-bpe-disk.ps1 -Out <image> [-Source <tokenizer dir>] [-Extra <file,file>]
#
# Sector 0 holds the vocab length at byte 0 and the merges length at byte 4,
# both le32. vocab.json starts at sector 1 and merges.txt at the first sector
# after it. Each -Extra file (a repo path) follows on the next sector boundary;
# byte 8 holds their count and byte 12 + 8i the i-th one's start sector and
# length, le32 each. The image ends on a sector boundary. Called by mint-test-disk.ps1
# from the test's .disk-mint recipe, whose sha256 pins the two files. Every
# CLIP family folder in Forge carries the same two files (Diffusion.md, stage 4).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$Source = 'D:\AI\DiffusionForge\webui\backend\huggingface\stabilityai\stable-diffusion-xl-base-1.0\tokenizer',
    [string]$Extra = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$vocabPath = Join-Path $Source 'vocab.json'
$mergesPath = Join-Path $Source 'merges.txt'
foreach ($p in $vocabPath, $mergesPath) {
    if (-not (Test-Path -PathType Leaf $p)) { Write-Output "REFUSE: no $p"; exit 1 }
}
$vocab = [IO.File]::ReadAllBytes($vocabPath)
$merges = [IO.File]::ReadAllBytes($mergesPath)
$extras = @($Extra -split ',' | Where-Object { $_ } | ForEach-Object {
    if (-not (Test-Path -PathType Leaf $_)) { Write-Output "REFUSE: no $_"; exit 1 }
    , [IO.File]::ReadAllBytes((Resolve-Path $_).Path)
})
if (12 + 8 * $extras.Count -gt 512) { Write-Output "REFUSE: $($extras.Count) extra files overflow the header sector"; exit 1 }
$vocabSectors = [int][Math]::Ceiling($vocab.Length / 512)
$mergesSectors = [int][Math]::Ceiling($merges.Length / 512)
$sector = 1 + $vocabSectors + $mergesSectors
$starts = @()
foreach ($e in $extras) { $starts += $sector; $sector += [int][Math]::Ceiling($e.Length / 512) }
$image = New-Object byte[] ($sector * 512)
[BitConverter]::GetBytes([uint32]$vocab.Length).CopyTo($image, 0)
[BitConverter]::GetBytes([uint32]$merges.Length).CopyTo($image, 4)
[Array]::Copy($vocab, 0, $image, 512, $vocab.Length)
[Array]::Copy($merges, 0, $image, (1 + $vocabSectors) * 512, $merges.Length)
if ($extras.Count -gt 0) { [BitConverter]::GetBytes([uint32]$extras.Count).CopyTo($image, 8) }
for ($i = 0; $i -lt $extras.Count; $i++) {
    [BitConverter]::GetBytes([uint32]$starts[$i]).CopyTo($image, 12 + 8 * $i)
    [BitConverter]::GetBytes([uint32]$extras[$i].Length).CopyTo($image, 16 + 8 * $i)
    [Array]::Copy($extras[$i], 0, $image, $starts[$i] * 512, $extras[$i].Length)
}
[IO.File]::WriteAllBytes($Out, $image)
Write-Output "minted $Out, vocab $($vocab.Length) bytes, merges $($merges.Length) bytes"
exit 0
