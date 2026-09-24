Set-StrictMode -Version Latest

function Write-PrismWindowsProxy([string]$OutDirectory, [string]$MsvcRoot) {
    $systemLibrary=Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'
    $dumpbin=Join-Path $MsvcRoot 'bin/Hostx64/x64/dumpbin.exe'
    $assembler=Join-Path $MsvcRoot 'bin/Hostx64/x64/ml64.exe'
    foreach($path in @($systemLibrary,$dumpbin,$assembler)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing Windows proxy dependency: $path"}}
    $table=@(& $dumpbin /exports $systemLibrary)
    if($LASTEXITCODE -ne 0){throw 'Cannot inspect system WinHTTP exports'}
    $exports=[Collections.Generic.List[object]]::new()
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ordinals=[Collections.Generic.HashSet[int]]::new()
    foreach($line in $table){
        if($line -match '^\s+(\d+)\s+[0-9A-F]+\s+[0-9A-F]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*$'){
            $ordinal=[int]$Matches[1];$name=$Matches[2]
            if($ordinal -lt 1 -or $ordinal -gt 65535 -or -not $names.Add($name) -or -not $ordinals.Add($ordinal)){throw 'Invalid system WinHTTP export table'}
            $exports.Add(@{name=$name;ordinal=$ordinal})
        }elseif($line -match '^\s+\d+\s+[0-9A-F]+\s+') {throw "Unsupported system WinHTTP export: $line"}
    }
    if($exports.Count -gt 4096 -or -not $names.Contains('WinHttpOpen') -or -not $names.Contains('WinHttpConnect') -or -not $names.Contains('WinHttpWebSocketSend')){throw 'Incomplete system WinHTTP export table'}
    $defs=[Collections.Generic.List[string]]::new();$defs.Add('LIBRARY winhttp');$defs.Add('EXPORTS')
    $asm=[Text.StringBuilder]::new();[void]$asm.AppendLine('EXTERN prism_winhttp_exports:QWORD');[void]$asm.AppendLine('EXTERN prism_resolve_winhttp:PROC');[void]$asm.AppendLine('.code')
    $header=[Text.StringBuilder]::new();$initial=[Collections.Generic.List[string]]::new();$exportNames=[Collections.Generic.List[string]]::new()
    for($i=0;$i -lt $exports.Count;$i++){
        $defs.Add(('    {0}=prism_export_{1} @{2} PRIVATE' -f $exports[$i].name,$i,$exports[$i].ordinal))
        [void]$header.AppendLine(('extern "C" void prism_lazy_{0}();' -f $i));$initial.Add('(FARPROC)prism_lazy_'+$i);$exportNames.Add('"'+$exports[$i].name+'"')
        # Win64: preserve argument registers and caller stack; 0x88 includes shadow space and alignment.
        [void]$asm.AppendLine(@"
prism_export_$i PROC
    jmp QWORD PTR [prism_winhttp_exports+$($i*8)]
prism_export_$i ENDP
prism_lazy_$i PROC FRAME
    sub rsp, 88h
    .allocstack 88h
    .endprolog
    mov [rsp+20h], rcx
    mov [rsp+28h], rdx
    mov [rsp+30h], r8
    mov [rsp+38h], r9
    movdqu [rsp+40h], xmm0
    movdqu [rsp+50h], xmm1
    movdqu [rsp+60h], xmm2
    movdqu [rsp+70h], xmm3
    mov ecx, $i
    call prism_resolve_winhttp
    mov r11, rax
    mov rcx, [rsp+20h]
    mov rdx, [rsp+28h]
    mov r8, [rsp+30h]
    mov r9, [rsp+38h]
    movdqu xmm0, [rsp+40h]
    movdqu xmm1, [rsp+50h]
    movdqu xmm2, [rsp+60h]
    movdqu xmm3, [rsp+70h]
    add rsp, 88h
    jmp r11
prism_lazy_$i ENDP
"@)
    }
    [void]$asm.AppendLine('END')
    [void]$header.AppendLine('extern "C" FARPROC prism_winhttp_exports[] = {'+($initial -join ',')+'};')
    [void]$header.AppendLine('static const char* prism_export_names[] = {'+($exportNames -join ',')+'};')
    $utf8=[Text.UTF8Encoding]::new($false)
    $def=Join-Path $OutDirectory 'winhttp.def';$asmPath=Join-Path $OutDirectory 'WinHttpProxy.asm';$object=Join-Path $OutDirectory 'WinHttpProxy.obj'
    [IO.File]::WriteAllLines($def,$defs,$utf8)
    [IO.File]::WriteAllText($asmPath,$asm.ToString(),$utf8)
    [IO.File]::WriteAllText((Join-Path $OutDirectory 'WinHttpProxy.h'),$header.ToString(),$utf8)
    & $assembler /nologo /c ('/Fo'+$object) $asmPath | Out-Host
    if($LASTEXITCODE -ne 0){throw 'Windows proxy assembly failed'}
    return @{definition=$def;object=$object;systemLibrary=$systemLibrary;systemHash=(Get-FileHash -LiteralPath $systemLibrary).Hash;assemblerHash=(Get-FileHash -LiteralPath $assembler).Hash;exports=$exports.ToArray()}
}

function Test-PrismWindowsProxy([string]$Artifact, [string]$MsvcRoot, $Proxy) {
    $dumpbin=Join-Path $MsvcRoot 'bin/Hostx64/x64/dumpbin.exe'
    $table=@(& $dumpbin /exports $Artifact)
    if($LASTEXITCODE -ne 0){throw 'Cannot inspect packaged Windows exports'}
    $actual=@(foreach($line in $table){
        if($line -match '^\s+(\d+)\s+[0-9A-F]+\s+[0-9A-F]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*$'){ $Matches[1]+':'+$Matches[2] }
        elseif($line -match '^\s+\d+\s+[0-9A-F]+\s+'){throw "Unexpected packaged export: $line"}
    })
    $expected=@($Proxy.exports | ForEach-Object {[string]$_.ordinal+':'+$_.name})
    if(-not $actual -or (Compare-Object $expected $actual)){throw 'Packaged WinHTTP exports differ from the system library'}
}
