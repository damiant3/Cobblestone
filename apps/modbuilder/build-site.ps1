[CmdletBinding()]
param([string]$OutDir = '', [string]$PrismPage = '', [string]$ForgePage = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'web' }
if (-not $PrismPage) { $PrismPage = Join-Path $repo 'apps/landing/web/compile/prism.html' }
if (-not $ForgePage) { $ForgePage = Join-Path $PSScriptRoot 'web/modbuilder.html' }
$prism = [IO.File]::ReadAllText($PrismPage)
$forge = [IO.File]::ReadAllText($ForgePage)
. (Join-Path $PSScriptRoot 'page/add-wizard.ps1')
$forge = Add-ModBuilderWizard $forge
$forge = [regex]::Replace($forge, '@import url\([^)]*\);', '')
$forge = [regex]::Replace($forge, '(?s)<!--MODBUILDER-NATIVE-->.*?<!--/MODBUILDER-NATIVE-->\s*', '')
$match = [regex]::Match($forge, 'window\.__DATA = (\{[\s\S]*?\});</script>')
if (-not $match.Success) { throw 'The forge page has no embedded data' }
$data = $match.Groups[1].Value | ConvertFrom-Json -AsHashtable
$unityPath=Join-Path $repo 'codex/plugs/unity/build-output/unity-stdio.wasm'
$data['unity-stdio.wasm']=[Convert]::ToBase64String([IO.File]::ReadAllBytes($unityPath))
$forge=$forge.Replace($match.Value,'window.__DATA = '+(ConvertTo-Json -InputObject $data -Depth 20 -Compress -EscapeHandling EscapeHtml)+';</script>')
$match=[regex]::Match($forge,'window\.__DATA = (\{[\s\S]*?\});</script>')
$embed = [ordered]@{}
foreach ($name in @('codex-compiler.wasm', 'csharp-stdio.wasm', 'pe-bytes.wasm', 'library.img.gz')) {
    $m = [regex]::Match($prism, '"' + [regex]::Escape($name) + '"\s*:\s*"([A-Za-z0-9+/=]+)"')
    if (-not $m.Success) { throw "Prism module missing: $name" }
    $embed[$name] = $m.Groups[1].Value
}
$embed['unity-stdio.wasm'] = $data['unity-stdio.wasm']
$embed['cil-stdio.wasm']=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repo 'codex/plugs/cil/build-output/cil-stdio.wasm')))
$embed['PrismRuntime.dll']=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repo 'codex/plugs/cil/build-output/runtime/PrismRuntime.dll')))
$lib = [regex]::Match($prism, 'window\.__LIBRARY = (\{[^\r\n]+\});')
if (-not $lib.Success) { throw 'Prism library index missing' }
$game = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mods/valheim/features.json') -Raw | ConvertFrom-Json -AsHashtable
if (-not $game) { throw 'Valheim catalogue missing' }
$templates = [ordered]@{}
foreach ($feature in $game.features) {
    $files = [ordered]@{}
    foreach ($source in $feature.sources) {
        $files[$source] = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "mods/valheim/$source")) -replace "`r`n", "`n"
    }
    $files[$game.entry] = "Chapter: $($game.entryChapter)`n`nSection: Engine Entry`n`n  effect Process where`n    $($feature.effect)`n`nSection: Entry`n`n  opening : [Process] Nothing = act`n    $($feature.start)`n  end`n"
    $templates[$feature.id] = [ordered]@{ title = $feature.title; main = $game.entry; files = $files }
}
$profile = [ordered]@{
    storage = 'modbuilder-valheim'
    autoEndpoints = $false
    plugs = [ordered]@{ unity = 'unity-stdio.wasm' }
    prefixes = [ordered]@{ unity = "UNITY $($game.target)`n" }
    initialProject = $templates['hud']
}
function Json($value) { ConvertTo-Json -InputObject $value -Depth 20 -Compress -EscapeHandling EscapeHtml }
$compilerSource=[ordered]@{}
foreach($file in @(Get-ChildItem (Join-Path $repo 'codex/plugs/cil') -File | Where-Object {$_.Extension -eq '.codex' -or $_.Name -in @('README.md','build.ps1','build-unity-runtime.ps1')} | Sort-Object Name)){$compilerSource['codex/plugs/cil/'+$file.Name]=[IO.File]::ReadAllText($file.FullName)}
$compilerSource['PrismRuntime.cs']=[IO.File]::ReadAllText((Join-Path $repo 'codex/plugs/cil/build-output/runtime/PrismRuntime.cs'))
$sources = [ordered]@{}
foreach ($file in @('WindowsHost.codex','Deploy.codex','Cleanup.codex','Helper.codex','winhost.js','browser.js','README.md','build-loader.ps1')) { $sources[$file] = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "native/$file")) -replace "`r`n", "`n" }
$runtimeSource=$compilerSource['PrismRuntime.cs']
foreach($pair in @(@('GameAssemblyHash','__MODBUILDER_GAME_HASH__'),@('UnityPlayerHash','__MODBUILDER_PLAYER_HASH__'))){$value=[regex]::Match($runtimeSource,$pair[0]+'\s*=\s*"([A-Fa-f0-9]{64})"');if(-not $value.Success){throw 'Adapter game certificate missing'};$sources['Deploy.codex']=$sources['Deploy.codex'].Replace($pair[1],$value.Groups[1].Value)}
foreach($file in @('codex/plugs/common/ByteHelpers.codex','codex/foreword/core/CCE.codex','codex/plugs/pe/PeExports.codex','codex/plugs/pe/PeWriter.codex','codex/plugs/pe/PeBootstrap.codex','codex/plugs/pe/PeBootstrapDriver.codex','codex/plugs/unity/package.ps1')){$sources['loader/'+$file]=[IO.File]::ReadAllText((Join-Path $repo $file))}
$native = [ordered]@{ sources = $sources; loader=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repo 'build-output/native-deploy/winhttp-template.dll'))) }
$nativeScript = '<!--MODBUILDER-NATIVE--><script>window.__NATIVE_HELPER=' + (Json $native) + ';</script><script>' + $sources['winhost.js'] + '</script><script>' + $sources['browser.js'] + '</script><!--/MODBUILDER-NATIVE-->'
$standaloneForge = $forge.Replace('</head>', $nativeScript + '</head>')
$forge = $forge.Replace($match.Value, 'window.__DATA = parent.__FORGE_DATA;</script>')
$forgeData = [ordered]@{ mods = $data.mods }
foreach ($key in @($data.Keys | Where-Object { $_ -like 'art-*' } | Sort-Object)) { $forgeData[$key] = $data[$key] }
$injected = '<script>window.__EMBED=' + (Json $embed) + ';window.__LIBRARY=' + $lib.Groups[1].Value + ';window.__TEMPLATES=' + (Json $templates) + ';window.__EXAMPLES=[];window.__PRISM_PROFILE=' + (Json $profile) + ';window.__VALHEIM=' + (Json $game) + ';window.__FORGE_DATA=Object.assign(' + (Json $forgeData) + ',window.__EMBED);window.__FORGE_HTML=' + (Json $forge) + ';</script>'
$injected+='<script>window.__MOD_COMPILER_SOURCE='+(Json $compilerSource)+';</script>'
$art=[ordered]@{landscape='data:image/png;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'page/art/nordic-forge.png')));logo='data:image/svg+xml;base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'page/art/valheim-logo.svg')))}
$injected+='<script>window.__MOD_ART='+(Json $art)+';</script>'
$html = [IO.File]::ReadAllText((Join-Path $repo 'codex/plugs/wasm/page/prism.html'))
if (-not $html.Contains('<!--EMBED-->')) { throw 'Prism embed marker missing' }
$html = $html.Replace('<!--EMBED-->', $injected)
$html = $html.Replace('</head>', '<style>' + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page/workspace.css')) + '</style></head>')
$html = $html.Replace('</body>', $nativeScript + '<script>' + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page/workspace.js')) + '</script><script>' + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page/managed-build.js')) + '</script><script>' + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page/deploy.js')) + '</script><script>' + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page/player-flow.js')) + '</script></body>')
$html = $html.Replace("url('roundabout.jpg')", 'none')
[void](New-Item -ItemType Directory -Force -Path $OutDir)
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutDir 'workspace.html'), ($html -replace "`r`n", "`n" -replace "`n", "`r`n"), $utf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'page/home.html') -Destination (Join-Path $OutDir 'index.html') -Force
[IO.File]::WriteAllText((Join-Path $OutDir 'modbuilder.html'), ($standaloneForge -replace "`r`n", "`n" -replace "`n", "`r`n"), $utf8)
Write-Host "ModBuilder website: $OutDir"
