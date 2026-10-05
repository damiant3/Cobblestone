[CmdletBinding()]
param(
    [ValidateSet('All', 'Backend')][string]$Scope = 'All',
    [string]$OutDir = '',
    [string]$BrowserPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$kernel = Join-Path $repo 'seed/Codex.cdx'
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/codexmagic-ai-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { throw "Output directory already exists: $OutDir" }
New-Item -ItemType Directory -Path $OutDir | Out-Null
$kernelHash = (Get-FileHash $kernel -Algorithm SHA256).Hash
$progress = Join-Path $OutDir 'progress.txt'
$verdict = Join-Path $OutDir 'verdict.txt'
"kernel: $kernel [$kernelHash]" | Set-Content $progress
Write-Host "Artifacts: $OutDir"
Write-Host "kernel: $kernel [$kernelHash]"

function Assert-RunMemory {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    "freeKiB=$free" | Add-Content $progress
    if ($free -le 1572864) { throw 'Insufficient free RAM for one serial guest' }
}

function Invoke-AiCompile([string]$Source, [string]$Name) {
    Assert-RunMemory
    "compile $Name" | Add-Content $progress
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $Source -Out "$OutDir/$Name.cdx" -Log "$OutDir/$Name.log" -Kernel $kernel 1> "$OutDir/$Name.compile.stdout" 2> "$OutDir/$Name.compile.stderr"
    if ($LASTEXITCODE -ne 0) {
        Get-Content "$OutDir/$Name.log" -Tail 30
        throw "Compile $Name failed: $LASTEXITCODE"
    }
    Get-Content "$OutDir/$Name.compile.stderr" | Where-Object { $_ -like 'kernel:*' } | Write-Host
}

function Invoke-AiGolden([string]$Name, [string]$ExpectedFile) {
    Assert-RunMemory
    "run $Name" | Add-Content $progress
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel "$OutDir/$Name.cdx" -OutFile "$OutDir/$Name.out" 1> "$OutDir/$Name.run.stdout" 2> "$OutDir/$Name.run.stderr"
    if ($LASTEXITCODE -ne 0) { throw "Run $Name failed: $LASTEXITCODE" }
    $actual = [IO.File]::ReadAllText("$OutDir/$Name.out").Replace("`r", '')
    $expected = [IO.File]::ReadAllText($ExpectedFile).Replace("`r", '')
    if ($actual -cne $expected) {
        Get-Content "$OutDir/$Name.out"
        throw "Golden mismatch: $Name"
    }
    "PASS $Name" | Add-Content $progress
    Write-Host "PASS $Name"
}

Push-Location $repo
try {
    if ($Scope -eq 'All') {
        if (-not $BrowserPath) { $BrowserPath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe' }
        $BrowserPath = (Resolve-Path -LiteralPath $BrowserPath).Path
        & node -e 'if(typeof WebSocket!=="function")throw Error("Node must provide built-in WebSocket")'
        if ($LASTEXITCODE -ne 0) { throw 'Browser grader requires Node with built-in WebSocket' }
    }
    Invoke-AiCompile (Join-Path $PSScriptRoot 'AIGameplayTest.codex') 'AIGameplayTest'
    Invoke-AiGolden 'AIGameplayTest' (Join-Path $PSScriptRoot 'AIGameplayTest.expected')

    $server = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'MagicServer.codex')).Replace('Chapter: MagicServer', 'Chapter: CodexMagic--MagicServer') -replace '(?m)^  opening :', '  api-server-entry :'
    $core = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AIGameplayTest.codex')).Replace('Chapter: AIGameplayTest', 'Chapter: CodexMagic--AIGameplayTest') -replace '(?m)^  opening :', '  api-core-entry :'
    $tail = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/ai-api.codex.inc'))
    [IO.File]::WriteAllText("$OutDir/ApiCheck.codex", $server + "`r`n" + $core + "`r`n" + $tail, [Text.UTF8Encoding]::new($false))
    Invoke-AiCompile "$OutDir/ApiCheck.codex" 'ApiCheck'
    Invoke-AiGolden 'ApiCheck' (Join-Path $PSScriptRoot 'tests/ai-api.expected')

    if ($Scope -eq 'All') {
        Assert-RunMemory
        'build HTML plug' | Add-Content $progress
        & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/html/build.ps1') -Force 1> "$OutDir/html-build.stdout" 2> "$OutDir/html-build.stderr"
        if ($LASTEXITCODE -ne 0) { throw "HTML plug build failed: $LASTEXITCODE" }
        $plugOutput = Join-Path $repo 'codex/plugs/html/build-output'
        Copy-Item -LiteralPath "$plugOutput/build.log", "$plugOutput/plug-source.codex", "$plugOutput/html-plug.cdx" -Destination $OutDir
        Assert-RunMemory
        'build isolated GamePage' | Add-Content $progress
        $priorTemp = $env:TEMP
        $priorTmp = $env:TMP
        $pageTemp = New-Item -ItemType Directory -Path "$OutDir/html-temp"
        try {
            $env:TEMP = $pageTemp.FullName
            $env:TMP = $pageTemp.FullName
            & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/html/run.ps1') -Src (Join-Path $PSScriptRoot 'GamePage.codex') -Out "$OutDir/ai-game-$PID.html" -Compiler $kernel 1> "$OutDir/page.stdout" 2> "$OutDir/page.stderr"
            if ($LASTEXITCODE -ne 0) { throw "GamePage build failed: $LASTEXITCODE" }
        } finally {
            $env:TEMP = $priorTemp
            $env:TMP = $priorTmp
            foreach ($evidence in @("$plugOutput/run-ai-game-$PID.log", "$plugOutput/last-run-ai-game-$PID.ir")) {
                if (Test-Path $evidence) { Copy-Item -LiteralPath $evidence -Destination $OutDir }
            }
        }
        'run browser with mock API' | Add-Content $progress
        & node (Join-Path $PSScriptRoot 'tests/ai-browser.mjs') "$OutDir/ai-game-$PID.html" $OutDir $BrowserPath 1> "$OutDir/browser.stdout" 2> "$OutDir/browser.stderr"
        if ($LASTEXITCODE -ne 0) { throw "Browser grader failed: $LASTEXITCODE" }
    }
    if ((Get-FileHash $kernel -Algorithm SHA256).Hash -cne $kernelHash) { throw 'Depot seed changed during verification' }
    $result = if ($Scope -eq 'All') { 'PASS native, actual API handlers, generated page with mock API' } else { 'PASS native and actual API handlers; browser not run (Backend scope)' }
    $result | Set-Content $verdict
    Write-Host $result
} catch {
    "FAIL $_" | Set-Content $verdict
    throw
} finally {
    Pop-Location
}
