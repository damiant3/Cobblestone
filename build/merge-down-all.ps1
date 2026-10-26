# Forwarding entry for catalog ID agents.merge-down-all (tools/agents/merge-down-all.ps1).
# Remove when no operator runbook, skill or fleet tool names build/merge-down-all.ps1.
$fwdRoot = Split-Path $PSScriptRoot
$fwdCatalog = Get-Content -LiteralPath (Join-Path $fwdRoot 'build/tool-catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json
$fwdTool = @($fwdCatalog.tools | Where-Object id -eq 'agents.merge-down-all')
if ($fwdTool.Count -ne 1) { throw 'Catalog must identify exactly one agents.merge-down-all' }
$global:LASTEXITCODE = 0
& (Join-Path $fwdRoot $fwdTool[0].path) @args
exit $LASTEXITCODE
