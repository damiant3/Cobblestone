# jonquil.ps1 -- standing-gate tripwire for a self-reproducing IR quine.
#
# The jonquil is a narcissus: this catches a definition caught admiring its
# own reflection in the IR (it re-emits itself), named for the flower, not
# the Grecian lad who drowned in his.
#
# The trusting-trust defense here rests on one property: a payload that
# survives the diverse-double-compile rebuild MUST have reached the readable
# IR that becomes the C# arm, and is therefore VISIBLE as text (see
# docs/OperatorsManual.md "The witness has a negative control" and
# docs/Test/Active/DDC-QUINE-ARM.md). That visibility was asserted, not
# enforced. This check enforces it for the tractable construction: it emits
# the compiler's own IR and fails the build if any definition carries, as
# string data, the IR-def header for its OWN name -- the signature of a
# definition that re-emits itself (the DDC-QUINE-ARM quine in ir-emit-def).
#
# SCOPE, stated honestly. This is a tripwire for the naive/tractable
# construction, NOT a complete quine detector: general detection is
# undecidable (Rice), and a machine-code-only Thompson trojan leaves NO IR
# trace, so this check cannot see it and does not replace the DDC witness. It
# hardens the readable-intermediate claim; it is not the witness.
#
# A def is emitted on one line as `  (def "NAME" ...)`. Its honest IR builds
# the header `(def ` and the quoted name by SEPARATE concatenation, so the
# contiguous escaped token `(def \"NAME\"` never appears in an honest def.
# The DDC-QUINE-ARM construction embeds that exact escaped self-header as ONE
# contiguous text literal, which is what this scans for. That is NOT what a
# quine MUST do: the same split-concatenation the honest emitter uses -- and
# that the arm itself applies to its own `@QQ@` marker (DDC-QUINE-ARM.md) --
# would spread the header across adjacent text-lits and slip this substring
# scan, as would a mutual "A embeds B, B embeds A" pair (each def is checked
# only against its OWN name). That is the Rice-undecidable tail the scope note
# above disclaims; this catches the tractable single-def form, not the general
# case.

param(
    [string]$Repo   = (Split-Path -Parent $PSScriptRoot),
    [string]$Kernel = '',
    [string]$Src    = '',
    [string]$IrFile = '',      # scan an existing IR log instead of producing one
    [string]$WorkDir = ''
)

$ErrorActionPreference = 'Stop'

function Get-IrDefLines {
    param([string]$LogPath)
    $lines = [System.IO.File]::ReadAllLines($LogPath)
    $b = [Array]::IndexOf($lines, 'IR-BEGIN')
    $e = [Array]::IndexOf($lines, 'IR-END')
    if ($b -lt 0 -or $e -lt 0) {
        Write-Host "jonquil: FAIL -- no IR-BEGIN/IR-END in $LogPath"
        exit 1
    }
    return , ($lines[($b + 1)..($e - 1)])
}

# Produce the IR if we were not handed one.
if (-not $IrFile) {
    if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
    if (-not $WorkDir) { $WorkDir = Join-Path $Repo 'build-output\quine-check' }
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

    if (-not $Src) {
        $Src = Join-Path $WorkDir 'Compiler.codex'
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'concat-codex-self.ps1') `
            -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $Src | Out-Null
    }
    if (-not (Test-Path -PathType Leaf $Src)) {
        Write-Host "jonquil: FAIL -- source concat produced no file ($Src)"; exit 1
    }
    if (-not (Test-Path -PathType Leaf $Kernel)) {
        Write-Host "jonquil: FAIL -- kernel missing ($Kernel)"; exit 1
    }

    $IrFile = Join-Path $WorkDir 'compiler.ir.log'
    $irOut  = Join-Path $WorkDir 'compiler.ir'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') `
        -Src $Src -Out $irOut -Log $IrFile -Kernel $Kernel -IrUni | Out-Null
    if (-not (Test-Path -PathType Leaf $IrFile)) {
        Write-Host "jonquil: FAIL -- IR emit produced no log ($IrFile)"; exit 1
    }
}

$defLines = Get-IrDefLines $IrFile
$scanned  = 0
$hits     = @()

foreach ($line in $defLines) {
    $t = $line.TrimStart()
    if (-not $t.StartsWith('(def "')) { continue }
    $close = $t.IndexOf('"', 6)
    if ($close -lt 0) { continue }
    $name = $t.Substring(6, $close - 6)
    $scanned++
    # The escaped self-header a self-reproducing def must embed as string data.
    $selfHeader = '(def \"' + $name + '\"'
    if ($t.Contains($selfHeader)) {
        $at = $t.IndexOf($selfHeader)
        $snip = $t.Substring($at, [Math]::Min(90, $t.Length - $at))
        $hits += [pscustomobject]@{ Name = $name; Snippet = $snip }
    }
}

if ($hits.Count -gt 0) {
    Write-Host ''
    Write-Host "jonquil: FAIL -- $($hits.Count) definition(s) embed their own IR-def header (self-reproducing quine signature):"
    foreach ($h in $hits) {
        Write-Host "  def `"$($h.Name)`" carries: ...$($h.Snippet)..."
    }
    Write-Host '  A definition that re-emits itself into the IR is the DDC-QUINE-ARM construction.'
    Write-Host '  See docs/Test/Active/DDC-QUINE-ARM.md. This must not ship.'
    exit 1
}

Write-Host "jonquil: OK ($scanned defs scanned, no self-reproducing IR quine)"
exit 0
