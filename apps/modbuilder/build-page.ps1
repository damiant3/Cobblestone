[CmdletBinding()]
param(
    [string]$CompilerWasm = '',
    [string]$UnityWasm = '',
    [string]$OutFile = '',
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $CompilerWasm) { $CompilerWasm = Join-Path $repo 'codex/plugs/wasm/build-output/page/codex-compiler.wasm' }
if (-not $UnityWasm) { $UnityWasm = Join-Path $repo 'codex/plugs/unity/build-output/unity-stdio.wasm' }
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot 'web/modbuilder.html' }
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
foreach ($m in @($CompilerWasm, $UnityWasm)) { if (-not (Test-Path -LiteralPath $m -PathType Leaf)) { throw "Missing module: $m" } }

$mods = [Collections.Generic.List[object]]::new()
foreach ($fj in @(Get-ChildItem (Join-Path $PSScriptRoot 'mods') -Filter 'features.json' -Recurse -File | Sort-Object FullName)) {
    $cat = Get-Content -LiteralPath $fj.FullName -Raw | ConvertFrom-Json
    $files = [ordered]@{}
    foreach ($ft in $cat.features) {
        if ($ft.id -notmatch '^[a-z0-9-]+$') { throw "Invalid mod feature id: $($ft.id)" }
        foreach ($name in $ft.sources) {
            if ($name -notmatch '^[A-Za-z0-9_-]+\.codex$') { throw 'Invalid mod source name' }
            if (-not $files.Contains($name)) { $files[$name] = [IO.File]::ReadAllText((Join-Path $fj.DirectoryName $name)) -replace "`r`n", "`n" }
        }
    }
    $mods.Add([ordered]@{ game = $cat.game; title = $cat.title; target = $cat.target; entry = $cat.entry; entryChapter = $cat.entryChapter; features = @($cat.features); files = $files })
}

$data = [ordered]@{
    'codex-compiler.wasm' = [Convert]::ToBase64String([IO.File]::ReadAllBytes($CompilerWasm))
    'unity-stdio.wasm' = [Convert]::ToBase64String([IO.File]::ReadAllBytes($UnityWasm))
    'mods' = $mods.ToArray()
}
foreach ($img in @(Get-ChildItem (Join-Path $PSScriptRoot 'page/img') -Filter '*.jpg' -File | Sort-Object Name)) {
    $data['art-' + $img.BaseName] = 'data:image/jpeg;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($img.FullName))
}
$json = ConvertTo-Json -InputObject $data -Depth 8 -Compress -EscapeHandling EscapeHtml

& pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/html/build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The HTML plug could not be rebuilt from its source' }
$plugOut = Join-Path ([IO.Path]::GetTempPath()) ('modbuilder-plug-' + [guid]::NewGuid().ToString('N') + '.html')
try {
    & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/html/run.ps1') -Src (Join-Path $PSScriptRoot 'page/ModBuilderApp.codex') -Out $plugOut -Compiler $Kernel
    if ($LASTEXITCODE -ne 0) { throw "The html plug failed on page/ModBuilderApp.codex (exit $LASTEXITCODE)" }
    $html = [IO.File]::ReadAllText($plugOut)
} finally { Remove-Item -LiteralPath $plugOut -Force -ErrorAction SilentlyContinue }
if (([regex]::Matches($html, '</head>')).Count -ne 1) { throw 'The plug output must carry exactly one </head>' }
$html = $html.Replace('</head>', "<script>window.__DATA = $json;</script>`n</head>")
. (Join-Path $PSScriptRoot 'page/add-wizard.ps1')
$html = Add-ModBuilderWizard $html
[void](New-Item -ItemType Directory -Force -Path (Split-Path $OutFile))
[IO.File]::WriteAllText($OutFile, ($html -replace "`r`n", "`n" -replace "`n", "`r`n"), [Text.UTF8Encoding]::new($false))
Write-Host ("[modbuilder] {0}: {1:N0} bytes, {2} game(s), {3} feature(s), {4} illustration(s)" -f $OutFile, (Get-Item $OutFile).Length, $mods.Count, (@($mods | ForEach-Object { $_.features.Count }) | Measure-Object -Sum).Sum, @($data.Keys | Where-Object { $_ -like 'art-*' }).Count)
