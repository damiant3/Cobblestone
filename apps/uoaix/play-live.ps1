[CmdletBinding()]
param([string]$Base='',[switch]$Plan)
# The desktop "UOAIX Server" shortcut: restarts the last live shard (its artifact, world disk and port, read from the
# newest live-* supervisor directory) through switch-live.ps1, which backs the world up and refuses a disk a codex-vm
# process holds. A supervisor still alive means the shard is up: say so and start nothing.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Base){$Base=Join-Path $repo 'build-output/uoaix'}
$dirs=@(Get-ChildItem -LiteralPath $Base -Directory -Filter 'live-*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'supervisor.json') } |
    Sort-Object { (Get-Item -LiteralPath (Join-Path $_.FullName 'supervisor.json')).LastWriteTime })
if($dirs.Count -eq 0){throw "No live-* supervisor directory under $Base"}
$last=$dirs[-1].FullName
$sup=Get-Content -LiteralPath (Join-Path $last 'supervisor.json') -Raw | ConvertFrom-Json
if($sup.supervisor -and (Get-Process -Id $sup.supervisor -ErrorAction SilentlyContinue)){
    "UOAIX live shard already running: supervisor $($sup.supervisor), $last, state $($sup.state), port $($sup.port). Nothing started."
    return
}
$runs=@(Get-ChildItem -LiteralPath $last -Directory -Filter 'run-*' | Sort-Object { [int]($_.Name -replace '^run-','') })
if($runs.Count -eq 0){throw "No run-* directory in $last"}
$run=Get-Content -LiteralPath (Join-Path $runs[-1].FullName 'run.json') -Raw | ConvertFrom-Json
$stem=$dirs[-1].Name -replace '-r\d+$',''
$k=2;while(Test-Path -LiteralPath (Join-Path $Base "$stem-r$k")){$k++}
$out=Join-Path $Base "$stem-r$k"
$argv=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'switch-live.ps1'),'-Artifact',$run.artifact,'-World',$run.stateFile,'-OutDir',$out,'-Port',[string]$sup.port)
"UOAIX restart: $($run.artifact) on $($run.stateFile), port $($sup.port), into $out"
if($Plan){"PLAN pwsh $($argv -join ' ')";return}
& pwsh @argv
if($LASTEXITCODE -ne 0){throw "switch-live.ps1 refused or failed ($LASTEXITCODE)"}
