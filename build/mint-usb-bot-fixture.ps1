# Mints codex/test/apps/usb-bot.disk: 2048 sectors where byte i of sector s is
# (s*3 + i*5 + 11) mod 256, the formula usb-bot.codex checks every read against.
param([Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = 'Stop'
$b = New-Object byte[] (2048 * 512)
for ($s = 0; $s -lt 2048; $s++) { for ($i = 0; $i -lt 512; $i++) { $b[$s * 512 + $i] = [byte](($s * 3 + $i * 5 + 11) % 256) } }
$path = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location) $Out }
[IO.File]::WriteAllBytes($path, $b)
