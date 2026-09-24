param(
    [Parameter(Mandatory)][string]$InputsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$OldPlug,
    [Parameter(Mandatory)][string]$NewPlug,
    [Parameter(Mandatory)][string]$Zig,
    [Parameter(Mandatory)][string[]]$Subjects,
    [string[]]$Unsupported = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$inputsPath = (Resolve-Path -LiteralPath $InputsDirectory).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'contract-readers: output directory must be new' }
[void][IO.Directory]::CreateDirectory($outputPath)
$zigPath = (Resolve-Path -LiteralPath $Zig).Path
$zigHash = (Get-FileHash -LiteralPath $zigPath).Hash
$plugs = @(
    @{Name = 'old'; Path = (Resolve-Path -LiteralPath $OldPlug).Path},
    @{Name = 'new'; Path = (Resolve-Path -LiteralPath $NewPlug).Path}
)
foreach ($plug in $plugs) { $plug.Hash = (Get-FileHash -LiteralPath $plug.Path).Hash }
if ($plugs[0].Hash -ceq $plugs[1].Hash) { throw 'contract-readers: frozen readers must be distinct' }
$subjectSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($subject in $Subjects) {
    if (-not $subjectSet.Add($subject)) { throw 'contract-readers: duplicate subject' }
}
foreach ($subject in $Unsupported) {
    if (-not $subjectSet.Contains($subject)) { throw 'contract-readers: unsupported subject was not selected' }
}
$rows = [Collections.Generic.List[object]]::new()
try {
    foreach ($subject in $Subjects) {
        if ($subject -notmatch '^[a-z][a-z-]*$') { throw 'contract-readers: invalid subject basename' }
        $ir = Join-Path $inputsPath "$subject.ir"
        $expectedPath = Join-Path $inputsPath "$subject.expected"
        $irHash = (Get-FileHash -LiteralPath $ir).Hash
        $expectedHash = (Get-FileHash -LiteralPath $expectedPath).Hash
        foreach ($plug in $plugs) {
            $tag = "$subject-$($plug.Name)"
            if ((Get-FileHash -LiteralPath $plug.Path).Hash -cne $plug.Hash) { throw 'contract-readers: frozen plug changed' }
            $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
            if ($free -le 1.5) { throw "contract-readers: RAM admission refused ($free GiB)" }
            $zigSource = Join-Path $outputPath "$tag.zig"
            & pwsh -NoProfile -File (Join-Path $repo 'build/run-plug.ps1') -Plug $plug.Path -InFile $ir -Output $zigSource -Port 9145 -MemMB 3072 *> (Join-Path $outputPath "$tag-emit.log")
            $emitExit = $LASTEXITCODE
            if ($emitExit -ne 0) { throw "$tag emit failed: exit $emitExit" }
            $exe = Join-Path $outputPath "$tag.exe"
            $buildLog = Join-Path $outputPath "$tag-build.log"
            & $zigPath build-exe $zigSource "-femit-bin=$exe" *> $buildLog
            $buildExit = $LASTEXITCODE
            $row = [ordered]@{
                Subject = $subject; Reader = $plug.Name; ReaderSha256 = $plug.Hash
                IrSha256 = $irHash; ExpectedSha256 = $expectedHash; ZigSha256 = $zigHash
                FreeGiB = $free; EmitExit = $emitExit; BuildExit = $buildExit; Result = 'UNGRADED'
            }
            $rows.Add($row)
            if ($Unsupported -ccontains $subject) {
                $diagnostic = [IO.File]::ReadAllText($buildLog)
                $errors = [regex]::Matches($diagnostic, '(?m)^(?:.+:\d+:\d+: )?error: ([^\r\n]+)\r?$')
                $otherErrors = @($errors | Where-Object { $_.Groups[1].Value -cne "use of undeclared identifier 'b_'" })
                if ($buildExit -eq 0 -or $errors.Count -eq 0 -or $otherErrors.Count -ne 0) {
                    throw "$tag expected free-binder boundary changed: $buildLog"
                }
                $row.Result = 'UNSUPPORTED_FREE_BINDER'
                Write-Host "$tag UNSUPPORTED: free b_ ($buildLog)"
            } else {
                if ($buildExit -ne 0) { throw "$tag typed build failed: $buildLog" }
                $out = Join-Path $outputPath "$tag.out"
                $err = Join-Path $outputPath "$tag.stderr"
                $program = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
                $programHandle = $program.Handle
                if (-not $program.WaitForExit(60000)) {
                    Stop-Process -Id $program.Id -Force
                    throw "$tag hosted run timed out"
                }
                $row.RunExit = $program.ExitCode
                if ($row.RunExit -ne 0) { throw "$tag hosted run failed" }
                if ((Get-Item -LiteralPath $out).Length -ne 0 -and (Get-Item -LiteralPath $err).Length -ne 0) {
                    throw "$tag wrote both output channels; ordering is ungraded"
                }
                $row.OutputChannel = if ((Get-Item -LiteralPath $out).Length -ne 0) { 'stdout' } else { 'stderr' }
                $actualPath = if ($row.OutputChannel -eq 'stdout') { $out } else { $err }
                $actual = [IO.File]::ReadAllText($actualPath).Replace("`r`n", "`n")
                $expected = [IO.File]::ReadAllText($expectedPath).Replace("`r", '')
                if ($actual -cne $expected) { throw "$tag exact output mismatch: $out" }
                $row.Result = 'EXACT_OUTPUT_PASS'
                Write-Host "$tag exact output PASS"
            }
            if ((Get-FileHash -LiteralPath $ir).Hash -cne $irHash -or (Get-FileHash -LiteralPath $expectedPath).Hash -cne $expectedHash) {
                throw 'contract-readers: input changed during read'
            }
        }
    }
    foreach ($plug in $plugs) {
        if ((Get-FileHash -LiteralPath $plug.Path).Hash -cne $plug.Hash) { throw 'contract-readers: frozen plug changed' }
    }
    if ((Get-FileHash -LiteralPath $zigPath).Hash -cne $zigHash) { throw 'contract-readers: Zig changed' }
    Set-Content -LiteralPath (Join-Path $outputPath 'exit.txt') -Value 0
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $outputPath 'failure.txt')
    Set-Content -LiteralPath (Join-Path $outputPath 'exit.txt') -Value 1
    throw
} finally {
    $rows.ToArray() | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'results.json')
}
