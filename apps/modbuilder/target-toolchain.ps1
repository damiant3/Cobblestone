Set-StrictMode -Version Latest

function Invoke-PrismChild([string]$Exe, [string[]]$Arguments, [int]$Timeout = 180000) {
    $start=[Diagnostics.ProcessStartInfo]::new($Exe)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    $proc=[Diagnostics.Process]::Start($start)
    try {
        $out=$proc.StandardOutput.ReadToEndAsync();$err=$proc.StandardError.ReadToEndAsync()
        if(-not $proc.WaitForExit($Timeout)){$proc.Kill($true);$proc.WaitForExit();throw 'Target tool timed out'}
        return @{code=$proc.ExitCode;out=$out.GetAwaiter().GetResult();err=$err.GetAwaiter().GetResult()}
    } finally {$proc.Dispose()}
}

function Invoke-PrismTarget {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)]$Request)
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    if ($Request.operation -notin @('inspect', 'build', 'package', 'test')) { throw 'Unsupported target operation' }
    $profile = $Request.profile
    if ($profile.version -ne 1 -or $profile.kind -ne 'unity') { throw 'Unsupported target schema or family' }
    if ($profile.id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') { throw 'Invalid profile name' }
    $catalog = Get-Content -LiteralPath (Join-Path $repo 'codex/plugs/unity/targets.json') -Raw | ConvertFrom-Json
    $target = @($catalog.targets | Where-Object id -CEQ $profile.profile)
    if ($target.Count -ne 1) { throw 'Unsupported Unity target profile' }
    $target = $target[0]
    if (-not [IO.Path]::IsPathFullyQualified([string]$profile.gamePath)) { throw 'Game directory must be an absolute path' }
    $game = (Resolve-Path -LiteralPath $profile.gamePath -ErrorAction Stop).Path
    $managed = Join-Path $game $target.managedDirectory
    $player = Join-Path $game $target.playerLibrary
    $assembly = Join-Path $managed $target.gameAssembly
    foreach ($file in @((Join-Path $game $target.executable), $player, $assembly, (Join-Path $managed 'mscorlib.dll'), (Join-Path $game 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'))) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing game dependency: $file" }
    }
    $engine = ([Diagnostics.FileVersionInfo]::GetVersionInfo($player).ProductVersion -split ' ')[0]
    if ($engine -cne $target.engineVersion) { throw "Unsupported Unity version: expected $($target.engineVersion), found $engine" }
    if ($profile.groupLimit -lt 1 -or $profile.groupLimit -gt 4 -or [math]::Truncate($profile.groupLimit) -ne $profile.groupLimit) { throw 'Linked storage supports one to four containers per workbench' }
    $receipt = [ordered]@{
        ok = $true; operation = $Request.operation; profile = $profile; target = $target;
        gameAssembly = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;
        unityPlayer = (Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash;
        inspectedAt = [DateTime]::UtcNow.ToString('o');
        runtimeVerified = $false; installed = $false
    }
    if ($Request.operation -eq 'inspect') { return $receipt }
    $code = [string]$Request.code
    if ($code.Length -gt 16777216 -or -not $code.Contains('public static class PrismUnityEntry')) { throw 'Missing or oversized Unity source artifact' }
    $sourceHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($code)))
    if($Request.operation -eq 'test') {
        if(-not [IO.Path]::IsPathFullyQualified([string]$profile.testSavePath)){throw 'Configure an absolute test save directory'}
        $artifact=[IO.Path]::GetFullPath([string]$Request.artifact)
        $allowed=[IO.Path]::GetFullPath((Join-Path $Root 'prism-target-builds'))+[IO.Path]::DirectorySeparatorChar
        if(-not $artifact.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($artifact) -cne 'winhttp.dll'){throw 'Test artifact is outside the target build directory'}
        $nativeDir=Split-Path $artifact -Parent
        $prior=Get-Content -LiteralPath (Join-Path (Split-Path $nativeDir -Parent) 'receipt.json') -Raw|ConvertFrom-Json
        if(-not $prior.ok -or $prior.artifactHash -cne (Get-FileHash -LiteralPath $artifact).Hash -or $prior.inputSourceHash -cne $sourceHash -or $prior.gameAssembly -cne $receipt.gameAssembly -or $prior.unityPlayer -cne $receipt.unityPlayer){throw 'Artifact, source or game changed; build the extension again'}
        foreach($field in @('profile','groupLimit','workbenchLinks')){if($prior.profile.$field -cne $profile.$field){throw 'Target configuration changed; build the extension again'}}
        $testOut=Join-Path (Join-Path $Root 'prism-target-tests') ([guid]::NewGuid().ToString('N'))
        $probe=if($code.Contains('class PrismStorage')){'storage'}else{'startup'}
        $child=Invoke-PrismChild (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'test-game.ps1'),'-GamePath',$game,'-Package',$artifact,'-OutDirectory',$testOut,'-SaveRoot',$profile.testSavePath,'-Probe',$probe)
        $receipt.ok=$child.code -eq 0; $receipt.code=$child.code; $receipt.out=$child.out; $receipt.err=$child.err; $receipt.testReceipt=Join-Path $testOut 'run.json'
        return $receipt
    }
    $featureFile = Join-Path $PSScriptRoot ('mods/' + $target.game + '/features.json')
    $receipt.features = @()
    if (Test-Path -LiteralPath $featureFile -PathType Leaf) {
        $declared = @((Get-Content -LiteralPath $featureFile -Raw | ConvertFrom-Json).features)
        $present = @($declared | Where-Object { $code.Contains('class ' + $_.marker) })
        if ($Request.PSObject.Properties['features'] -and $null -ne $Request.features) {
            $asked = (@($Request.features | ForEach-Object { [string]$_ }) | Sort-Object) -join ', '
            $found = (@($present | ForEach-Object { $_.id }) | Sort-Object) -join ', '
            if ($asked -cne $found) { throw "Selected features ($asked) differ from the features in the source ($found)" }
        }
        $receipt.features = @(foreach ($f in $present) {
            [ordered]@{
                id = $f.id
                sources = @(foreach ($s in $f.sources) { [ordered]@{ path = 'apps/modbuilder/mods/' + $target.game + '/' + $s; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('mods/' + $target.game + '/' + $s))).Hash } })
                gameAssembly = $receipt.gameAssembly
                unityPlayer = $receipt.unityPlayer
            }
        })
    }
    $code = $code.Replace('"__PRISM_GAME_HASH__"', ('"' + $receipt.gameAssembly + '"'))
    $code = $code.Replace('"__PRISM_UNITY_HASH__"', ('"' + $receipt.unityPlayer + '"'))
    $receipt.buildNumber = [DateTime]::UtcNow.ToString('yyyyMMdd.HHmmss', [Globalization.CultureInfo]::InvariantCulture)
    $code = $code.Replace('__PRISM_BUILD_NUMBER__', $receipt.buildNumber)
    foreach ($setting in ,@('GROUP_LIMIT','groupLimit')) {
        $value = ([double]$profile.($setting[1])).ToString('0.################',[Globalization.CultureInfo]::InvariantCulture)
        $code = $code.Replace(('__PRISM_' + $setting[0] + '__'), $value)
    }
    if ($profile.workbenchLinks -isnot [bool]) { throw 'Workbench link selection must be Boolean' }
    $code = $code.Replace('__PRISM_WORKBENCH_LINKS__', ([string]$profile.workbenchLinks).ToLowerInvariant())
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $sdkLines = @(& $dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the .NET SDK' }
    $sdk = $sdkLines | Select-Object -Last 1
    if ($sdk -notmatch '^([^ ]+) \[(.+)\]$') { throw 'No Roslyn SDK found' }
    $compiler = Join-Path $matches[2] "$($matches[1])/Roslyn/bincore/csc.dll"
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'Roslyn compiler missing' }
    $outputRoot = Join-Path ([IO.Path]::GetFullPath($Root)) 'prism-target-builds'
    $out = Join-Path $outputRoot ([guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $out -Force)
    $source = Join-Path $out 'PrismMod.cs'
    $dll = Join-Path $out 'PrismMod.dll'
    [IO.File]::WriteAllText($source, $code, [Text.UTF8Encoding]::new($false))
    $argsList = [Collections.Generic.List[string]]::new()
    foreach ($arg in @('-nologo','-target:library','-langversion:latest','-nostdlib+','-deterministic+','-optimize+','-platform:x64',('-out:' + $dll))) { $argsList.Add($arg) }
    $refs = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Numerics.dll','UnityEngine.CoreModule.dll')
    if ($code.Contains('class PrismStorage') -or $code.Contains('class PrismMeadowsSpawns') -or $code.Contains('class PrismCircumhorizontalArc')) { $refs += @('assembly_valheim.dll','assembly_utils.dll','assembly_guiutils.dll','UnityEngine.PhysicsModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.UI.dll','Unity.TextMeshPro.dll') }
    if ($code.Contains('class PrismCircumhorizontalArc')) {
        $refs += 'UnityEngine.ImageConversionModule.dll'
        $mask = Join-Path $PSScriptRoot 'mods/valheim/cirrus-mask.png'
        if (-not (Test-Path -LiteralPath $mask -PathType Leaf)) { throw 'Missing cirrus mask asset' }
        $argsList.Add('-resource:' + $mask + ',PrismGenerated.CirrusMask.png')
        $receipt.resources = @(@{name='PrismGenerated.CirrusMask.png';path='apps/modbuilder/mods/valheim/cirrus-mask.png';sha256=(Get-FileHash -LiteralPath $mask).Hash})
    }
    foreach ($ref in $refs) {
        $path = Join-Path $managed $ref
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing managed reference: $ref" }
        $argsList.Add('-reference:' + $path)
    }
    $argsList.Add($source)
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.ArgumentList.Add($compiler)
    foreach ($arg in $argsList) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Unity library compile timed out' }
        $diagnostics = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $out 'compile.log'), $diagnostics)
        $receipt.ok = $process.ExitCode -eq 0 -and (Test-Path -LiteralPath $dll -PathType Leaf)
        $receipt.code = $process.ExitCode; $receipt.diagnostics = $diagnostics
    } finally { $process.Dispose() }
    $receipt.outputDirectory = $out
    $receipt.sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $receipt.inputSourceHash = $sourceHash
    $receipt.compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
    $receipt.references = @($refs | ForEach-Object { @{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $managed $_)).Hash } })
    if ($receipt.ok) { $receipt.artifact = $dll; $receipt.artifactHash = (Get-FileHash -LiteralPath $dll).Hash }
    if($receipt.ok -and $Request.operation -eq 'package') {
        foreach($field in @('msvcRoot','windowsSdkRoot','windowsSdkVersion')){if(-not $profile.PSObject.Properties[$field] -or -not $profile.$field){throw "Configure $field before native packaging"}}
        $native=Join-Path $out 'native'
        $child=Invoke-PrismChild (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $repo 'codex/plugs/unity/package.ps1'),'-ManagedLibrary',$dll,'-OutDirectory',$native,'-MsvcRoot',$profile.msvcRoot,'-WindowsSdkRoot',$profile.windowsSdkRoot,'-WindowsSdkVersion',$profile.windowsSdkVersion)
        $receipt.ok=$child.code -eq 0; $receipt.code=$child.code; $receipt.out=$child.out; $receipt.err=$child.err
        $receipt.managedArtifact=$dll
        if($receipt.ok){$receipt.artifact=Join-Path $native 'winhttp.dll';$receipt.artifactHash=(Get-FileHash -LiteralPath $receipt.artifact).Hash}
        else{$receipt.Remove('artifact');$receipt.Remove('artifactHash')}
    }
    [IO.File]::WriteAllText((Join-Path $out 'receipt.json'), ($receipt | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    return $receipt
}
