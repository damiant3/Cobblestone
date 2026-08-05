# test-exception-handler.ps1 -- Test the bare-metal exception handler output
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
    [string]$CodexCdx = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ((-not $CodexCdx)) {
    $CodexCdx = (Join-Path $Repo 'seed\Codex.cdx')
}
$compile = (Join-Path $PSScriptRoot 'compile.ps1')
$outDir = (Join-Path $Repo 'build-output\exc-test')
if ((Test-Path -PathType Container $outDir)) {
    Remove-Item -Recurse -Force $outDir
}
New-Item -ItemType Directory -Force $outDir | Out-Null

$stage0Dir = (Join-Path $Repo 'build-output\bare-metal')
New-Item -ItemType Directory -Force $stage0Dir | Out-Null
Copy-Item -Force $CodexCdx (Join-Path $stage0Dir 'Codex.cdx')


$samples = @(@{ Name = 'exc-div-zero'; Pattern = '!EXC='; NeedStack = $true }, @{ Name = 'exc-null-read'; Pattern = '!EXC='; NeedStack = $true }, @{ Name = 'exc-gpf'; Pattern = '!EXC='; NeedStack = $true }, @{ Name = 'exc-deep-frames'; Pattern = '!EXC='; NeedStack = $true; NeedFrames = 3 }, @{ Name = 'exc-stack-heap'; Pattern = 'OUT OF MEMORY'; NeedStack = $false; TimeoutSec = 90 })

$pass = 0
$fail = 0


foreach ($s in $samples) {
    $src = (Join-Path $Repo ([string]([string]'codex\test\' + $s.Name) + '.codex'))
    $cdx = (Join-Path $outDir ([string]$s.Name + '.cdx'))
    $log = (Join-Path $outDir ([string]$s.Name + '.log'))

    Write-Host -NoNewline ([string]$s.Name + ': ')
    & 'pwsh' -NoProfile -File $compile -Src $src -Out $cdx -Log $log
    if ((-not ($LASTEXITCODE -eq 0))) {
        Write-Host 'FAIL (compile)'
        $fail++
        continue
    }


    $so = (Join-Path $outDir ([string]$s.Name + '.stdout'))
    $se = (Join-Path $outDir ([string]$s.Name + '.stderr'))
    $budget = $(if ($s.ContainsKey('TimeoutSec')) { $s.TimeoutSec } else { 45 })
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $cdx, '-serial', 'stdio', '-mem', '3072', '-timeout', $budget) -WindowStyle Hidden -PassThru -RedirectStandardOutput $so -RedirectStandardError $se
    $proc | Wait-Process -Timeout ($budget + 30) -ErrorAction SilentlyContinue
    if ((-not $proc.HasExited)) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }

    $output = ''
    foreach ($f in @($so, $se)) {
        $t = (Get-Content $f -Raw -ErrorAction SilentlyContinue)
        if ($t) {
            $output += $t
        }
    }
    Remove-Item -Force -ErrorAction SilentlyContinue $so
    Remove-Item -Force -ErrorAction SilentlyContinue $se
    [System.IO.File]::WriteAllText((Join-Path $outDir ([string]$s.Name + '.out')), $output)


    $hasPattern = $output.Contains($s.Pattern)
    $dumpLines = @((($output -split '\n') | Where-Object { ($_ -match '^S\[') })).Count
    $stackOk = $(if ($s.NeedStack) { ($dumpLines -gt 13) } else { $true })
    $frameLines = @((($output -split '\n') | Where-Object { ($_ -match '^F\[') })).Count
    $needFrames = $(if ($s.ContainsKey('NeedFrames')) { $s.NeedFrames } else { 0 })
    $framesOk = (-not ($frameLines -lt $needFrames))

    if ((($hasPattern -and $stackOk) -and $framesOk)) {
        Write-Host ([string]([string]'PASS (pattern=' + $hasPattern) + ([string]([string]([string]' stack=' + $dumpLines) + ([string]' frames=' + $frameLines)) + ')'))
        $pass++
    } else {
        Write-Host ([string]([string]'FAIL (pattern=' + $hasPattern) + ([string]([string]([string]' stack=' + $dumpLines) + ([string]' frames=' + $frameLines)) + ([string]([string]' need=' + $needFrames) + ')')))
        Write-Host ([string]'  output: ' + $output.Substring(0, ([math]::Min(200, $output.Length))))
        $fail++
    }

}

Write-Host ''
Write-Host ([string]([string]'Exception handler tests: pass=' + $pass) + ([string]' fail=' + $fail))
if (($fail -gt 0)) {
    exit 1
}
exit 0
