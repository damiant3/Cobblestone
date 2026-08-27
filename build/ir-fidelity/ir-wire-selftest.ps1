param([string]$LogPath, [string]$Kernel = 'seed\Codex.cdx')

. (Join-Path $PSScriptRoot 'ir-wire.ps1')

# The round-trip arm needs a LIVE wire, not a banked fixture: a fixture would
# keep passing after the emitter changed shape, which is the one thing the arm
# is there to notice. With no -LogPath, compile a case program to get one.
if (-not $LogPath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    if (-not [System.IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $repoRoot $Kernel }
    $LogPath = Join-Path $env:TEMP ("ir-wire-selftest-" + [System.IO.Path]::GetRandomFileName() + ".log")
    $src = Join-Path $PSScriptRoot 'cases\linear-param\a.codex'
    $compile = Join-Path (Split-Path -Parent $PSScriptRoot) 'compile.ps1'
    $h = @{
        Src = $src
        Out = [System.IO.Path]::ChangeExtension($LogPath, '.ir')
        Log = $LogPath
        Kernel = $Kernel
        IrUni = $true
        Passes = 'none'
    }
    Push-Location $repoRoot
    try { & $compile @h 2>&1 | Out-Null }
    finally { Pop-Location }
    if (-not (Get-IrWireText -LogPath $LogPath)) {
        Write-Output "  FAIL  could not produce a live wire to test against; log at $LogPath"
        exit 1
    }
}

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Output "  ok    $name" }
    else { Write-Output "  FAIL  $name $detail"; $script:fail++ }
}

# 1. Round-trip over the real wire. If the reader silently drops or invents a
#    cell, the re-emitted text stops matching the source modulo whitespace.
$wire = Get-IrWireText -LogPath $LogPath
$tree = ConvertFrom-IrWire -Text $wire
$norm = { param($s) ($s -replace '\s+', ' ').Trim() }
$rt = Format-IrNode $tree
Check 'round-trip preserves the wire' ((& $norm $rt) -eq (& $norm $wire)) "len $($rt.Length) vs $($wire.Length)"

# 2. Positive discrimination: it must report DIFFERENT cells as different.
$a = ConvertFrom-IrWire -Text '(chapter (defs (def "f" "C" (params (param "n" int-default)) (fn int-default int-default) (int-lit 1) 0 0)))'
$b = ConvertFrom-IrWire -Text '(chapter (defs (def "f" "C" (params (param "n" text)) (fn text int-default) (int-lit 1) 0 0)))'
$pa = Format-IrNode (Get-IrCell $a 'def:f/param/0')
$pb = Format-IrNode (Get-IrCell $b 'def:f/param/0')
Check 'distinguishes int-default from text' ($pa -ne $pb) "'$pa' vs '$pb'"
Check 'reads the atom exactly' ($pa -eq 'int-default' -and $pb -eq 'text') "'$pa' '$pb'"

# 3. Negative control: an identical pair must NOT be reported as different.
$c = ConvertFrom-IrWire -Text '(chapter (defs (def "f" "C" (params (param "n" int-default)) (fn int-default int-default) (int-lit 1) 0 0)))'
Check 'identical wires compare equal' ((Format-IrNode (Get-IrCell $a 'def:f')) -eq (Format-IrNode (Get-IrCell $c 'def:f')))

# 4. A missing def returns null, so the harness can say UNSUPPORTED rather than
#    silently comparing two nulls and calling it agreement.
Check 'missing def returns null' ($null -eq (Get-IrCell $a 'def:nosuch'))
Check 'out-of-range param returns null' ($null -eq (Get-IrCell $a 'def:f/param/7'))

# 5. Malformed input must throw, not return a plausible tree.
$threw = $false
try { ConvertFrom-IrWire -Text '(def "unterminated' | Out-Null } catch { $threw = $true }
Check 'unterminated string throws' $threw
$threw = $false
try { ConvertFrom-IrWire -Text '(a b))' | Out-Null } catch { $threw = $true }
Check 'trailing input throws' $threw

# 6. Escapes survive a round trip, since ir-quote-char emits exactly these three.
$esc = ConvertFrom-IrWire -Text '(t "a\\b\"c\nd")'
Check 'escapes decode' ($esc[1].Value -eq "a\b`"c`nd") "got '$($esc[1].Value)'"
Check 'escapes re-encode' ((Format-IrNode $esc) -eq '(t "a\\b\"c\nd")') "got '$(Format-IrNode $esc)'"

# 7. Quoted and bare atoms are not conflated: "text" the name is not text the type.
$q = ConvertFrom-IrWire -Text '(x "text" text)'
Check 'quoted vs bare kept apart' ((Format-IrNode $q[1]) -ne (Format-IrNode $q[2])) "$(Format-IrNode $q[1]) vs $(Format-IrNode $q[2])"

if ($fail -gt 0) { Write-Output "SELFTEST: $fail FAILED"; exit 1 }
Write-Output 'SELFTEST: all passed'
exit 0
