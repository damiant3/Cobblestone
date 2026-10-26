# Forwarding entry for catalog ID agents.measure-context (tools/agents/measure-context.ps1).
# Remove when no operator runbook, skill or fleet tool names build/measure-context.ps1.
$fwdRoot = Split-Path $PSScriptRoot
$fwdCatalog = Get-Content -LiteralPath (Join-Path $fwdRoot 'build/tool-catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json
$fwdTool = @($fwdCatalog.tools | Where-Object id -eq 'agents.measure-context')
if ($fwdTool.Count -ne 1) { throw 'Catalog must identify exactly one agents.measure-context' }
$global:LASTEXITCODE = 0
& (Join-Path $fwdRoot $fwdTool[0].path) @args
exit $LASTEXITCODE
