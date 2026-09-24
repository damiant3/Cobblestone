Set-StrictMode -Version Latest

function Get-FontPack {
    param([Parameter(Mandatory)][string]$Repo)
    $root=Join-Path $Repo 'fonts'
    $pack=Get-Content -LiteralPath (Join-Path $root 'font-pack.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if($pack.version -ne 1){throw 'Unsupported font pack version'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($face in $pack.faces){
        if(-not $ids.Add($face.id) -or -not $names.Add($face.imageName)){throw 'Duplicate font identity'}
        if($face.imageName -cnotmatch '^[A-Z0-9_]{1,8}\.TTF$'){throw "Invalid image font name: $($face.imageName)"}
        $path=Join-Path $root $face.path
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $face.sha256){throw "Font digest mismatch: $($face.id)"}
        $family=@($pack.families | Where-Object id -eq $face.family)
        if($family.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $root $family[0].license) -PathType Leaf)){throw "Missing font family/license: $($face.id)"}
    }
    if(-not $ids.Contains($pack.default) -or -not $ids.Contains($pack.mono)){throw 'Missing default font role'}
    foreach($notice in $pack.additionalNotices){if(-not (Test-Path -LiteralPath (Join-Path $root $notice.license) -PathType Leaf)){throw 'Missing fallback font notice'}}
    return $pack
}

function Get-FontFace {
    param([Parameter(Mandatory)]$Pack,[string]$Name='')
    if(-not $Name){$Name=$Pack.default}
    $faces=@($Pack.faces | Where-Object id -eq $Name)
    if($faces.Count -ne 1){throw "Unknown font '$Name'; choose $($Pack.faces.id -join ', ')"}
    return $faces[0]
}

function New-FontImageExtras {
    param([Parameter(Mandatory)][string]$Repo,[Parameter(Mandatory)][string]$OutDir,[string]$FontName='inter')
    $pack=Get-FontPack -Repo $Repo
    $selected=Get-FontFace -Pack $pack -Name $FontName
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $config=Join-Path $OutDir 'UIFONT.CFG'
    $bytes=[byte[]]::new(16)
    [Text.Encoding]::ASCII.GetBytes($selected.imageName).CopyTo($bytes,0)
    [IO.File]::WriteAllBytes($config,$bytes)
    $extra=[Collections.Generic.List[string]]::new()
    foreach($face in $pack.faces){$extra.Add($face.imageName+'='+(Join-Path (Join-Path $Repo 'fonts') $face.path))}
    $licenses=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($family in $pack.families){if($licenses.Add($family.licenseImage)){$extra.Add($family.licenseImage+'='+(Join-Path (Join-Path $Repo 'fonts') $family.license))}}
    foreach($notice in $pack.additionalNotices){$extra.Add($notice.imageName+'='+(Join-Path (Join-Path $Repo 'fonts') $notice.license))}
    $extra.Add('UIFONT.CFG='+$config)
    $extra.Add('FONTS.JSN='+(Join-Path $Repo 'fonts/font-pack.json'))
    return ,$extra.ToArray()
}

function Set-FontImageChoice {
    param([Parameter(Mandatory)][string]$Repo,[Parameter(Mandatory)][string]$Image,[Parameter(Mandatory)][string]$FontName)
    $pack=Get-FontPack -Repo $Repo
    $face=Get-FontFace -Pack $pack -Name $FontName
    $bytes=[IO.File]::ReadAllBytes($Image)
    if($bytes.Length -lt 4096 -or [Text.Encoding]::ASCII.GetString($bytes,512,8) -cne 'EFI PART'){throw 'Font preview requires a GPT image'}
    $part=[long][BitConverter]::ToUInt64($bytes,1056)*512
    if($part -lt 1024 -or $part+512 -gt $bytes.Length){throw 'Invalid ESP range'}
    $sector=[BitConverter]::ToUInt16($bytes,$part+11)
    $cluster=[int]$bytes[$part+13]
    $reserved=[BitConverter]::ToUInt16($bytes,$part+14)
    $fats=[int]$bytes[$part+16]
    $entries=[BitConverter]::ToUInt16($bytes,$part+17)
    $fatSize=[BitConverter]::ToUInt16($bytes,$part+22)
    if($sector -ne 512 -or $entries -eq 0 -or $fatSize -eq 0 -or $cluster -eq 0){throw 'Font preview selection requires the standard FAT16 image'}
    $root=$part+($reserved+$fats*$fatSize)*$sector
    $data=$root+[Math]::Ceiling($entries*32/$sector)*$sector
    if($root+$entries*32 -gt $bytes.Length){throw 'Invalid root directory'}
    $config=-1L
    $font=-1L
    $pieces=$face.imageName.Split('.')
    $fontName11=$pieces[0].PadRight(8)+$pieces[1].PadRight(3)
    for($i=0;$i -lt $entries;$i++){
        $at=$root+32*$i
        if($bytes[$at] -eq 0){break}
        if($bytes[$at] -eq 229 -or $bytes[$at+11] -eq 15){continue}
        $name=[Text.Encoding]::ASCII.GetString($bytes,$at,11)
        if($name -ceq 'UIFONT  CFG'){$config=$at}
        if($name -ceq $fontName11){$font=$at}
    }
    if($config -lt 0 -or $font -lt 0){throw 'Image lacks the font pack; rebuild with build-boot-img.ps1'}
    $start=[BitConverter]::ToUInt16($bytes,$config+26)
    $length=[BitConverter]::ToUInt32($bytes,$config+28)
    $dest=[long]$data+($start-2)*$cluster*$sector
    if($start -lt 2 -or $length -ne 16 -or $dest -lt $data -or $dest+16 -gt $bytes.Length){throw 'Invalid font selection record'}
    [Array]::Clear($bytes,$dest,16)
    [Text.Encoding]::ASCII.GetBytes($face.imageName).CopyTo($bytes,$dest)
    [IO.File]::WriteAllBytes($Image,$bytes)
}

function Add-WebFontPack {
    param([Parameter(Mandatory)][string]$Repo,[Parameter(Mandatory)][string]$Html,[string]$FontName='inter')
    $pack=Get-FontPack -Repo $Repo
    $selected=Get-FontFace -Pack $pack -Name $FontName
    $mono=Get-FontFace -Pack $pack -Name $pack.mono
    $css=[Text.StringBuilder]::new()
    $used=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $faces=@(@($pack.faces | Where-Object family -eq $selected.family)+@($mono) | Sort-Object id -Unique)
    foreach($face in $faces){
        $role=if($face.id -eq $mono.id){'Codex Mono'}else{'Codex UI'}
        if($selected.id -eq $mono.id){$role='Codex UI'}
        $encoded=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path (Join-Path $Repo 'fonts') $face.path)))
        [void]$css.Append("@font-face{font-family:'$role';font-style:normal;font-weight:$($face.weight);font-display:swap;src:url(data:font/ttf;base64,$encoded) format('truetype')}"+"`n")
        [void]$used.Add($face.family)
    }
    $monoRole=if($selected.id -eq $mono.id){'Codex UI'}else{'Codex Mono'}
    [void]$css.Append(":root{--codex-ui-font:'Codex UI';--codex-mono-font:'$monoRole'}html body{font-family:var(--codex-ui-font),sans-serif;font-weight:$($selected.weight)}button,input,select,textarea{font-family:inherit}pre,code,kbd,samp{font-family:var(--codex-mono-font),monospace}"+"`n")
    $notices=[ordered]@{}
    foreach($family in $pack.families){if($used.Contains($family.id)){$notices[$family.name]=[IO.File]::ReadAllText((Join-Path (Join-Path $Repo 'fonts') $family.license))}}
    $licenseJson=$notices | ConvertTo-Json -Compress -EscapeHandling EscapeHtml
    $insert='<style id="codex-font-pack">'+$css.ToString()+'</style><script type="application/json" id="codex-font-licenses">'+$licenseJson+'</script>'
    $head=[regex]::Match($Html,'</head>','IgnoreCase')
    if(-not $head.Success){throw 'HTML output has no head for the font pack'}
    return $Html.Insert($head.Index,$insert)
}
