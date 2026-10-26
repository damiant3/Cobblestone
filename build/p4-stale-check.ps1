# Forwarding entry for catalog ID agents.p4-stale-check (tools/agents/p4-stale-check.ps1).
# Remove when codex/build/BuildScript.codex and build/build.ps1 call the catalog path.
$fwdRoot = Split-Path $PSScriptRoot
$fwdCatalog = Get-Content -LiteralPath (Join-Path $fwdRoot 'build/tool-catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json
$fwdTool = @($fwdCatalog.tools | Where-Object id -eq 'agents.p4-stale-check')
if ($fwdTool.Count -ne 1) { throw 'Catalog must identify exactly one agents.p4-stale-check' }
$global:LASTEXITCODE = 0
& (Join-Path $fwdRoot $fwdTool[0].path) @args
exit $LASTEXITCODE
