# build.ps1 — full compiler build, verification, and test.
#
# On success, prints only a story. On failure, prints technical details.
# Phases: clean → source → CDX build → sign → canary → sem-equiv →
#         text fixed point → CDX fixed point → test battery.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir    = Join-Path $PSScriptRoot 'output'
$SeedCdx   = Join-Path $Repo 'seed\Codex.cdx'
$SutCdx    = Join-Path $OutDir 'Sut.cdx'
$CodexSrc  = Join-Path $OutDir 'Codex.codex'
$Concat    = Join-Path $PSScriptRoot 'concat-codex-self.ps1'
$Compile   = Join-Path $PSScriptRoot 'compile.ps1'
$BuildLog  = Join-Path $OutDir 'build.log'

function Invoke-BuildCdx {
    param([string]$InputFile, [string]$Kernel, [string]$Output)
    $logFile = [System.IO.Path]::GetTempFileName()
    $tmpOut = Join-Path (Split-Path $Output) "build_cdx_tmp.cdx"
    $stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    if ($Kernel -ne $stage0) { Copy-Item -Force $Kernel $stage0 }
    & pwsh -NoProfile -File $Compile -Src $InputFile -Out $tmpOut -Log $logFile -Repl 2>&1 | Out-Null
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        Write-Host ''
        Write-Host 'FAIL: CDX build failed'
        Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Last 10 | ForEach-Object { Write-Host "  $_" }
    } else {
        Move-Item -Force $tmpOut $Output
    }
    Remove-Item -Force $logFile, $tmpOut -ErrorAction SilentlyContinue
    return $ok
}

function Invoke-BuildText {
    param([string]$InputFile, [string]$Kernel, [string]$Output)
    $logFile = [System.IO.Path]::GetTempFileName()
    $tmpOut = Join-Path (Split-Path $Output) "build_text_tmp.codex"
    $stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    if ($Kernel -ne $stage0) { Copy-Item -Force $Kernel $stage0 }

    $inputFile2 = [System.IO.Path]::GetTempFileName()
    $outputFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    try {
        $srcBytes = Normalize-TripleNewlines ([System.IO.File]::ReadAllBytes($InputFile))
        $sb = [System.Text.StringBuilder]::new($srcBytes.Length + 100)
        [void]$sb.Append("TEXT`n")
        [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($srcBytes))
        [void]$sb.Append([char]4)
        [System.IO.File]::WriteAllText($inputFile2, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

        $vmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
        $vmArgs = @('-kernel', $stage0, '-input', $inputFile2, '-output', $outputFile, '-mem', '2048', '-headless')
        $proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
        $proc.WaitForExit(600000)
        if (-not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            Write-Host ''; Write-Host 'FAIL: TEXT build timed out'; return $false
        }

        if (-not (Test-Path $outputFile) -or (Get-Item $outputFile).Length -eq 0) {
            Write-Host ''; Write-Host 'FAIL: TEXT build produced no output'
            if (Test-Path $stderrFile) { Get-Content $stderrFile | Select-Object -Last 5 | ForEach-Object { Write-Host "  $_" } }
            return $false
        }

        $raw = [System.IO.File]::ReadAllText($outputFile) -replace "`r", ''
        $lines = $raw -split "`n"
        $textLines = [System.Collections.Generic.List[string]]::new()
        $halted = $false
        foreach ($rl in $lines) {
            if ($rl.StartsWith('CODEGEN-HALTED') -or $rl.StartsWith('CODEGEN-ERRORS')) { $halted = $true; break }
            if (-not $rl.StartsWith('WD:') -and -not $rl.StartsWith('HEAP:') -and -not $rl.StartsWith('STACK:')) { $textLines.Add($rl) }
        }
        if ($halted) {
            Write-Host ''; Write-Host 'FAIL: TEXT build halted with errors'; return $false
        }
        [System.IO.File]::WriteAllText($Output, ($textLines -join "`n"))
        return $true
    } finally {
        Remove-Item -Force $inputFile2, $outputFile, $stderrFile -ErrorAction SilentlyContinue
    }
}

# ===================================================================
$buildTimer = [System.Diagnostics.Stopwatch]::StartNew()
$phaseTimings = [ordered]@{}
function Measure-Phase([string]$Name, [scriptblock]$Block) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Block
    $sw.Stop()
    $script:phaseTimings[$Name] = $sw.Elapsed
}

Write-Host 'The day is warm, yet there is a cooling breeze.'

# -- clean
Measure-Phase 'clean' {
    $buildOut = Join-Path $Repo 'build-output'
    if (Test-Path $buildOut) { Remove-Item -Recurse -Force $buildOut }
    if (Test-Path $OutDir) {
        $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
        $archive = Join-Path $PSScriptRoot "output-$stamp"
        Rename-Item $OutDir $archive
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Get-ChildItem $Repo -Recurse -Depth 3 -Include '*.bak','*.tmp','*.snap' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# -- source
Measure-Phase 'source-concat' {
    if (-not (Test-Path -PathType Leaf $SeedCdx)) { Write-Host "FAIL: $SeedCdx missing"; exit 1 }
    if (-not (Test-Path -PathType Leaf $Concat))  { Write-Host "FAIL: $Concat missing"; exit 1 }
    & pwsh -NoProfile -File $Concat -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $CodexSrc
    if (-not (Test-Path -PathType Leaf $CodexSrc)) { Write-Host 'FAIL: source concat produced no file'; exit 1 }
}

# Check if source constants match the seed — warn if they differ.
$chkConst = Join-Path $PSScriptRoot 'check-constants.ps1'
if (Test-Path $chkConst) {
    & pwsh -NoProfile -File $chkConst 2>&1 | ForEach-Object { Write-Host "  $_" }
}

Write-Host 'The latest in a series of personal crises seems insurmountable.'
Write-Host 'You are being pulled apart in all directions.'
Write-Host ''

# -- CDX build
Measure-Phase 'cdx-build' {
    if (-not (Invoke-BuildCdx -InputFile $CodexSrc -Kernel $SeedCdx -Output $SutCdx)) { exit 1 }
}

Write-Host 'Yet this afternoon walk in the countryside slowly brings relaxation'
Write-Host 'to your harried mind. The soil and strain of modern high-tech living'
Write-Host 'begins to wash off in layers.'
Write-Host ''

# -- sign
Measure-Phase 'sign' {
$SigningKey = 'D:\Projects\signing.key'
if (Test-Path -PathType Leaf $SigningKey) {
    $compileScript = Join-Path $PSScriptRoot 'compile.ps1'
    $runScript = Join-Path $PSScriptRoot 'test-run.ps1'
    $cdxRaw = [System.IO.File]::ReadAllBytes($SutCdx)
    $keyBytes = [System.IO.File]::ReadAllBytes($SigningKey)
    $hashBytes = $cdxRaw[8..39]
    $keyList = ($keyBytes | ForEach-Object { $_.ToString() }) -join ', '
    $hashList = ($hashBytes | ForEach-Object { $_.ToString() }) -join ', '
    $signSrc = Join-Path $OutDir 'cdx-sign-inline.codex'
    $signSrcText = @"
Chapter: CdxSignInline
  cites Foreword chapter Console
  cites Foreword chapter Ed25519
  cites Foreword chapter Sha512
Section: Helpers
  bytes-to-csv : List Integer, Integer, Integer, Text -> Text
  bytes-to-csv (bs) (i) (len) (acc) =
    if i >= len then acc
    else let sep = if i == 0 then "" else ","
    in bytes-to-csv bs (i + 1) len (acc & sep & show (list-at bs i))
Section: Body
  opening : [Console] Nothing = act
    let key = [$keyList]
    in let hash = [$hashList]
    in let pub = ed25519-public-key key
    in let sig = ed25519-sign key pub hash
    in act
      print-line-uni (bytes-to-csv pub 0 32 "")
      print-line-uni (bytes-to-csv sig 0 64 "")
    end
  end
"@
    [System.IO.File]::WriteAllText($signSrc, $signSrcText)
    $signCdx = Join-Path $OutDir 'cdx-sign.cdx'
    $signLog = Join-Path $OutDir 'cdx-sign.log'
    $signOut = Join-Path $OutDir 'cdx-sign.out'
    & pwsh -NoProfile -File $compileScript -Src $signSrc -Out $signCdx -Log $signLog 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: sign tool compile failed'; Get-Content $signLog -TotalCount 10 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }; exit 1 }
    & pwsh -NoProfile -File $runScript -Kernel $signCdx -OutFile $signOut 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: sign tool run failed'; exit 1 }
    $signRaw = [System.IO.File]::ReadAllText($signOut)
    $signClean = $signRaw -replace '[^\x20-\x7E\r\n]', ''
    $signLines = $signClean -split "`n" | Where-Object { $_.Trim() -match '^\d' }
    $pubBytes = $signLines[0].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
    $sigBytes = $signLines[1].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
    if ($pubBytes.Count -ne 32 -or $sigBytes.Count -ne 64) { Write-Host "FAIL: bad sign output (pub=$($pubBytes.Count) sig=$($sigBytes.Count))"; exit 1 }
    for ($i = 0; $i -lt 32; $i++) { $cdxRaw[40 + $i] = $pubBytes[$i] }
    for ($i = 0; $i -lt 64; $i++) { $cdxRaw[72 + $i] = $sigBytes[$i] }
    [System.IO.File]::WriteAllBytes($SutCdx, $cdxRaw)
}
}

Write-Host 'That willow tree near the stream looks comfortable and inviting.'

# -- canary
Measure-Phase 'canary' {
$canarySrc      = Join-Path $Repo 'codex\test\factorial.codex'
$canaryExpected = Join-Path $Repo 'codex\test\factorial.expected'
$canaryCdx      = Join-Path $OutDir 'canary-factorial.cdx'
$canaryLog      = Join-Path $OutDir 'canary-compile.log'
$canaryOut      = Join-Path $OutDir 'canary-run.out'
if (-not (Test-Path -PathType Leaf $canarySrc))      { Write-Host "FAIL: $canarySrc missing"; exit 1 }
if (-not (Test-Path -PathType Leaf $canaryExpected)) { Write-Host "FAIL: $canaryExpected missing"; exit 1 }
$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$runScript     = Join-Path $PSScriptRoot 'test-run.ps1'
& pwsh -NoProfile -File $compileScript -Src $canarySrc -Out $canaryCdx -Log $canaryLog 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: canary compile — SUT cannot compile factorial.codex'
    Get-Content $canaryLog -TotalCount 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
    exit 1
}
& pwsh -NoProfile -File $runScript -Kernel $canaryCdx -OutFile $canaryOut 2>&1 | Out-Null
$expectedBytes = if (Test-Path $canaryExpected) { [System.IO.File]::ReadAllText($canaryExpected) -replace "`r",'' } else { '' }
$actualBytes   = if (Test-Path $canaryOut)      { [System.IO.File]::ReadAllText($canaryOut) }                       else { '' }
if ($expectedBytes -ne $actualBytes) {
    Write-Host 'FAIL: canary output mismatch'
    Write-Host "  expected: $($expectedBytes.Trim())"
    Write-Host "  got:      $($actualBytes.Trim())"
    exit 1
}
}

Write-Host 'You settle beneath it and the buzz of dragonflies and the whisper'
Write-Host 'of the willow''s swaying branches bring a deep peace.'
Write-Host ''

# -- size sanity check
$seedSize = (Get-Item $SeedCdx).Length
$sutSize = (Get-Item $SutCdx).Length
$drift = [math]::Abs($sutSize - $seedSize)
$maxDrift = [int]($seedSize * 0.05)
if ($drift -gt $maxDrift) {
    Write-Host "FAIL: SUT size drifted too far from seed"
    Write-Host "  seed: $seedSize bytes  SUT: $sutSize bytes  drift: $drift (max $maxDrift)"
    exit 1
}

$textStage1 = Join-Path $OutDir 'stage1.codex'
$textStage2 = Join-Path $OutDir 'stage2.codex'

Write-Host 'Searching inward for tranquility and happiness, you close your eyes.'
Measure-Phase 'text-stage1' {
    if (-not (Invoke-BuildText -InputFile $CodexSrc -Kernel $SutCdx -Output $textStage1)) { exit 1 }
}

Measure-Phase 'sem-equiv' {
    $semEquivScript = Join-Path $PSScriptRoot 'compare-codex-semantic.ps1'
    & pwsh -NoProfile -File $semEquivScript -Source $CodexSrc -Stage1 $textStage1 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'FAIL: semantic equivalence — stage1 does not match source'
        & pwsh -NoProfile -File $semEquivScript -Source $CodexSrc -Stage1 $textStage1
        exit 1
    }
}

Write-Host 'A high-pitched cascading sound like crystal wind-chimes impinges'
Write-Host 'on your floating awareness.'
Write-Host ''

Measure-Phase 'text-stage2' {
    if (-not (Invoke-BuildText -InputFile $textStage1 -Kernel $SutCdx -Output $textStage2)) { exit 1 }
}

Measure-Phase 'text-fixedpoint' {
    $th1 = (Get-FileHash -Algorithm SHA256 $textStage1).Hash
    $th2 = (Get-FileHash -Algorithm SHA256 $textStage2).Hash
    if ($th1 -ne $th2) {
        Write-Host 'FAIL: text round-trip — stage1 !== stage2'
        Write-Host "  stage1: $((Get-Item $textStage1).Length) bytes  $th1"
        Write-Host "  stage2: $((Get-Item $textStage2).Length) bytes  $th2"
        exit 1
    }
}

Write-Host 'As you open your eyes, you see a shimmering blueness rise from the ground.'
Write-Host ''

$cdxStage1 = Join-Path $OutDir 'stage1.cdx'
$cdxStage2 = Join-Path $OutDir 'stage2.cdx'
$testKernel = $cdxStage1

Write-Host 'It is difficult to look at the blueness directly. The sound seems'
Write-Host 'to be emanating from this glowing portal.'
Measure-Phase 'cdx-stage1' {
    if (-not (Invoke-BuildCdx -InputFile $CodexSrc -Kernel $SutCdx -Output $cdxStage1)) { exit 1 }
}

Measure-Phase 'cdx-fixedpoint' {
    $sutHash = (Get-FileHash -Algorithm SHA256 $SutCdx).Hash
    $ch1 = (Get-FileHash -Algorithm SHA256 $cdxStage1).Hash
    if ($sutHash -eq $ch1) {
        Write-Host '(SUT === stage1 — hard fixed point in one pass)'
    } else {
        if (-not (Invoke-BuildCdx -InputFile $CodexSrc -Kernel $cdxStage1 -Output $cdxStage2)) { exit 1 }
        $ch2 = (Get-FileHash -Algorithm SHA256 $cdxStage2).Hash
        if ($ch1 -ne $ch2) {
            Write-Host 'FAIL: CDX fixed point — stage1 !== stage2'
            Write-Host "  stage1: $((Get-Item $cdxStage1).Length) bytes  $ch1"
            Write-Host "  stage2: $((Get-Item $cdxStage2).Length) bytes  $ch2"
            exit 1
        }
    }
}

Write-Host 'Light seems to bend and distort around it, while the sound waves'
Write-Host 'become so intense, they appear to become visible.'
Write-Host ''

Copy-Item -Force $cdxStage1 (Join-Path $OutDir 'NewSeed.cdx')

Write-Host 'The portal hangs there for a moment; then with the rush of an'
Write-Host 'imploding vacuum, it sinks into the ground.'

Measure-Phase 'test-battery' {
    $testScript = Join-Path $PSScriptRoot 'test.ps1'
    $testOut = Join-Path $OutDir 'test-results.txt'
    & pwsh -NoProfile -File $testScript -CodexCdx $testKernel -Jobs 4 > $testOut 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'FAIL: test battery'
        Get-Content $testOut | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
}

Write-Host 'Something remains suspended in mid-air for a moment before falling'
Write-Host 'to earth with a heavy thud.'
Write-Host ''

Write-Host 'Somewhat shaken by this vision, you rise to your feet to investigate.'
Write-Host 'A crude circle of stones surrounds the spot where the portal appeared.'
Write-Host 'There is something glinting in the grass.'
Write-Host ''
Write-Host 'You pick it up. It is a compiler.'
Write-Host 'It is completely self-contained and needs no other tools to function.'
Write-Host 'On the handle is inscribed: "CODEX".'
Write-Host ''

$buildTimer.Stop()
Write-Host '-- Phase Timings ----------------------------------'
$maxName = ($phaseTimings.Keys | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
foreach ($kv in $phaseTimings.GetEnumerator()) {
    $secs = $kv.Value.TotalSeconds
    $pad = $kv.Key.PadRight($maxName)
    Write-Host ("  {0}  {1,7:N1}s" -f $pad, $secs)
}
Write-Host ("  {0}  {1,7:N1}s" -f 'TOTAL'.PadRight($maxName), $buildTimer.Elapsed.TotalSeconds)
Write-Host ''

# Update constants hash to match the new seed.
if (Test-Path $chkConst) {
    & pwsh -NoProfile -File $chkConst -Update 2>&1 | ForEach-Object { Write-Host "  $_" }
}

exit 0
