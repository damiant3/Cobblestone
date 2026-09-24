[CmdletBinding()]
param()
$quireCatalogRoot = Split-Path $PSScriptRoot
$quireCatalog = Get-Content -LiteralPath (Join-Path $quireCatalogRoot 'build/tool-catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json
$quireAdapter = @($quireCatalog.tools | Where-Object id -eq 'build.resolve-unit.windows')
if ($quireAdapter.Count -ne 1) { throw 'Catalog must identify exactly one Windows cite resolver' }
. (Join-Path $quireCatalogRoot $quireAdapter[0].path)
