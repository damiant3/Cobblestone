# mint-test-disk.ps1 -- build a codex/test disk image from its recipe.
#
#   pwsh build/mint-test-disk.ps1 -Recipe codex/test/apps/foo.disk-mint
#
# Prints the image path (build-output/test-disks/<the recipe's path without
# -mint>) as the last line and exits 0, or prints REFUSE and exits 1. A recipe
# sits beside its test as foo.disk-mint or foo.disk2-mint; blank lines and #
# comments are ignored:
#
#   zero <bytes>                    an all-zero image
#   sparse <bytes>                  an all-zero image with the at lines written
#   at <offset> <hex>               with sparse: bytes written at that offset
#   fill <offset> <length>          with sparse: byte at absolute offset p is
#                                   (p * 2654435761 >> 24) & 0xFF, a stand-in for
#                                   file content no test reads
#   gzip <bytes>                    the image is the b64 lines, joined, decoded
#   b64 <base64>                    and gunzipped; kept for an image whose exact
#                                   bytes are the test's oracle
#   script <repo path> <args...>    runs the script from the repo root; {out} is
#                                   the image path, {dir} a fresh directory,
#                                   both relative to the repo root
#   pick <file>                     with {dir}: the file the script wrote there
#   sha256 <hex>                    required: the image must hash to this
#
# An image already minted is reused while it still hashes to the recipe's
# sha256, so a changed recipe or a hand-edited image is minted again. A refused
# mint keeps its work directory under build-output/test-disks/.work for the log.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Recipe)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -PathType Leaf $Recipe)) { Write-Output "REFUSE: no recipe at $Recipe"; exit 1 }
$recipePath = (Resolve-Path $Recipe).Path
$rel = [IO.Path]::GetRelativePath($repo, $recipePath)
if ($rel -notmatch '^codex[\\/]test[\\/].+\.disk2?-mint$') { Write-Output "REFUSE: not a codex/test disk recipe: $rel"; exit 1 }
$target = Join-Path (Join-Path $repo 'build-output\test-disks') $rel.Substring(0, $rel.Length - 5)

$lines = @(Get-Content $recipePath | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
$make = @($lines | Where-Object { $_ -match '^(zero|sparse|gzip|script)\s' })
$b64 = @($lines | Where-Object { $_ -match '^b64\s+[A-Za-z0-9+/=]+$' })
$pick = @($lines | Where-Object { $_ -match '^pick\s' })
$sha = @($lines | Where-Object { $_ -match '^sha256\s+[0-9A-Fa-f]{64}$' })
$at = @($lines | Where-Object { $_ -match '^at\s+\d+\s+([0-9A-Fa-f]{2})+$' })
$fill = @($lines | Where-Object { $_ -match '^fill\s+\d+\s+\d+$' })
if ($make.Count -ne 1 -or $sha.Count -ne 1 -or $pick.Count -gt 1 -or ($lines.Count -ne 2 + $pick.Count + $at.Count + $fill.Count + $b64.Count) -or ($at.Count + $fill.Count -gt 0 -and $make[0] -notmatch '^sparse\s') -or ($b64.Count -gt 0 -and $make[0] -notmatch '^gzip\s')) {
    Write-Output "REFUSE: $rel needs one zero/sparse/gzip/script line, one sha256 line, at most one pick line, at/fill lines only under sparse and b64 lines only under gzip"; exit 1
}
$want = ($sha[0] -split '\s+')[1].ToUpperInvariant()

if ((Test-Path -PathType Leaf $target) -and (Get-FileHash -Algorithm SHA256 $target).Hash -eq $want) { Write-Output $target; exit 0 }

$work = Join-Path $repo "build-output\test-disks\.work\$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $work | Out-Null
$words = @($make[0] -split '\s+')
$image = Join-Path $work 'image.bin'
if ($words[0] -eq 'zero' -or $words[0] -eq 'sparse') {
    $bytes = [int64]0
    if ($words.Count -ne 2 -or -not [int64]::TryParse($words[1], [ref]$bytes) -or $bytes -le 0) { Write-Output "REFUSE: $rel $($words[0]) needs one positive byte count"; exit 1 }
    $fs = [IO.File]::Create($image); $fs.SetLength($bytes)
    foreach ($a in $at) {
        $parts = $a -split '\s+'; $off = [int64]$parts[1]; $data = [Convert]::FromHexString($parts[2])
        if ($off + $data.Length -gt $bytes) { $fs.Dispose(); Write-Output "REFUSE: $rel at $off runs past the image"; exit 1 }
        $fs.Position = $off; $fs.Write($data, 0, $data.Length)
    }
    foreach ($f in $fill) {
        $parts = $f -split '\s+'; $off = [int64]$parts[1]; $len = [int64]$parts[2]
        if ($off + $len -gt $bytes) { $fs.Dispose(); Write-Output "REFUSE: $rel fill at $off runs past the image"; exit 1 }
        $data = New-Object byte[] $len
        for ($k = [int64]0; $k -lt $len; $k++) { $data[$k] = [byte](((($off + $k) * 2654435761) -shr 24) -band 0xFF) }
        $fs.Position = $off; $fs.Write($data, 0, $data.Length)
    }
    $fs.Dispose()
} elseif ($words[0] -eq 'gzip') {
    $bytes = [int64]0
    if ($words.Count -ne 2 -or -not [int64]::TryParse($words[1], [ref]$bytes) -or $bytes -le 0) { Write-Output "REFUSE: $rel gzip needs one positive byte count"; exit 1 }
    $packed = [Convert]::FromBase64String((($b64 | ForEach-Object { ($_ -split '\s+', 2)[1] }) -join ''))
    $in = [IO.MemoryStream]::new($packed); $z = [IO.Compression.GZipStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
    $fs = [IO.File]::Create($image); $z.CopyTo($fs); $got = $fs.Length; $fs.Dispose(); $z.Dispose()
    if ($got -ne $bytes) { Write-Output "REFUSE: $rel gzip unpacked $got bytes, the recipe says $bytes"; exit 1 }
} else {
    if ($words.Count -lt 2) { Write-Output "REFUSE: $rel script needs a path"; exit 1 }
    $script = Join-Path $repo $words[1]
    if (-not (Test-Path -PathType Leaf $script)) { Write-Output "REFUSE: $rel names a missing script $($words[1])"; exit 1 }
    $dir = Join-Path $work 'dir'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $scriptArgs = @($words | Select-Object -Skip 2 | ForEach-Object { $_.Replace('{out}', [IO.Path]::GetRelativePath($repo, $image)).Replace('{dir}', [IO.Path]::GetRelativePath($repo, $dir)) })
    $log = Join-Path $work 'script.log'
    Push-Location $repo
    try { & pwsh -NoProfile -File $script @scriptArgs *> $log; $rc = $LASTEXITCODE } finally { Pop-Location }
    if ($rc -ne 0) { Write-Output "REFUSE: $rel script exited $rc; log $log"; exit 1 }
    if ($pick.Count -eq 1) { $image = Join-Path $dir (($pick[0] -split '\s+', 2)[1]) }
    if (-not (Test-Path -PathType Leaf $image)) { Write-Output "REFUSE: $rel script wrote no image at $image; log $log"; exit 1 }
}
$got = (Get-FileHash -Algorithm SHA256 $image).Hash
if ($got -ne $want) { Write-Output "REFUSE: $rel minted $got, the recipe pins $want; work $work"; exit 1 }
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
Move-Item -Force $image $target
[IO.Directory]::Delete($work, $true)
Write-Output $target
exit 0
