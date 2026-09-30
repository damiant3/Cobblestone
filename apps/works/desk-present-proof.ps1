[CmdletBinding()]
param(
    [ValidateSet('codex-vm','ovmf','both')][string]$Bed='both',
    [ValidateSet('unit','step')][string]$Unit='unit',
    [string]$Mode='all',
    [string]$Kernel='seed/Codex.cdx',
    [string]$OutDir=''
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
$allowed=if($Unit -eq 'unit'){@('positive','bound','over-copy','hook','order','target-lifetime','view-lifetime','callback-lifetime','source-pitch','dest-pitch')}else{@('positive','drain','rebuild','move','pitch','base','hide','shadow','gpu','redraw','chrome-stay','chrome-min','chrome-close')}
if($Mode -ne 'all' -and $Mode -notin $allowed){throw "Unknown $Unit control: $Mode"}
$subject="codex/test/apps/scene-present-$Unit"
$marker=if($Unit -eq 'unit'){'present-proof-end'}else{'scene-step-proof-end'}
$first=if($Unit -eq 'unit'){'stable='}else{'render completes before publication:'}
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/desk-present-'+[Guid]::NewGuid().ToString('N'))}
if(-not [IO.Path]::IsPathRooted($OutDir)){$OutDir=Join-Path $repo $OutDir}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'Choose a fresh proof output directory'}
New-Item -ItemType Directory -Path $OutDir|Out-Null
$originalKernel=(Resolve-Path $Kernel).Path
$kernelHash=(Get-FileHash $originalKernel).Hash
$kernelPath=Join-Path $OutDir 'kernel.cdx'
Copy-Item -LiteralPath $originalKernel -Destination $kernelPath
if((Get-FileHash $kernelPath).Hash -ne $kernelHash -or (Get-FileHash $originalKernel).Hash -ne $kernelHash){throw 'Compiler changed during snapshot'}
$vmHash=(Get-FileHash (Join-Path $repo 'tools/codex-vm.exe')).Hash
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$subject.codex" -Out "$OutDir/template.codex"
if($LASTEXITCODE -ne 0){throw 'Source snapshot failed'}
$template=[IO.File]::ReadAllText("$OutDir/template.codex")
$expected=[IO.File]::ReadAllText((Join-Path $repo "$subject.expected")).Replace("`r",'').Trim()
$modes=@(if($Mode -eq 'all'){$allowed}else{$Mode})
$beds=@(if($Bed -eq 'both'){'codex-vm';'ovmf'}else{$Bed})
$records=[Collections.Generic.List[object]]::new()
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    $m=Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
    "free KB=$free; committed=$($m.CommittedBytes); limit=$($m.CommitLimit); guests=1"
    if($free -le 1572864 -or $m.CommitLimit-$m.CommittedBytes -lt 3GB){throw 'Host memory admission refused'}
}
function Replace-One([string]$body,[string]$old,[string]$new){
    if($body.IndexOf($old) -lt 0 -or $body.IndexOf($old) -ne $body.LastIndexOf($old)){throw "Mutation is not unique: $old"}
    return $body.Replace($old,$new)
}
function Read-Live([string]$path){
    if(-not (Test-Path $path)){return ''}
    $file=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        if($file.Length -gt 1MB){throw 'Serial capture exceeds proof bound'}
        $reader=[IO.StreamReader]::new($file,[Text.Encoding]::ASCII)
        try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
    }finally{$file.Dispose()}
}
function Run-Ovmf([string]$cdx,[string]$run){
    & pwsh -NoProfile -File build/cdx-to-pe.ps1 -CdxInput $cdx -Out "$run/proof.efi" -HeapPages 32768 -ExitBootServices
    if($LASTEXITCODE -ne 0){throw 'PE conversion failed'}
    & pwsh -NoProfile -File build/build-img.ps1 -PeInput "$run/proof.efi" -Out "$run/boot.img"
    if($LASTEXITCODE -ne 0){throw 'Image build failed'}
    (Get-FileHash "$run/boot.img").Hash|Set-Content "$run/image.sha256"
    Copy-Item -LiteralPath 'D:/Program Files/qemu/share/edk2-x86_64-code.fd' -Destination "$run/code.fd"
    Copy-Item -LiteralPath 'D:/Program Files/qemu/share/edk2-i386-vars.fd' -Destination "$run/vars.fd"
    Set-ItemProperty -LiteralPath "$run/vars.fd" -Name IsReadOnly -Value $false
    $qargs=@('-accel','tcg','-m','2048','-machine','q35','-drive',"if=pflash,format=raw,unit=0,readonly=on,file=$run/code.fd",'-drive',"if=pflash,format=raw,unit=1,file=$run/vars.fd",'-drive',"format=raw,file=$run/boot.img",'-serial',"file:$run/trace.log",'-display','none','-vga','std','-no-reboot')
    $err=[IO.Path]::GetTempFileName()
    Admit
    $guest=Start-Process -FilePath 'D:/Program Files/qemu/qemu-system-x86_64.exe' -ArgumentList @($qargs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru -RedirectStandardError $err
    $guest.Id|Set-Content "$run/guest.pid"
    "Owned QEMU PID=$($guest.Id); trace=$run/trace.log"
    try {
        $watch=[Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 100
            $serial=Read-Live "$run/trace.log"
            if($guest.HasExited){throw 'QEMU exited before proof capture completed'}
        }until($serial.Contains($marker) -or $watch.Elapsed.TotalSeconds -gt 45)
        if(-not $serial.Contains($marker)){throw 'OVMF proof timed out'}
    }finally{
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
        if(-not $guest.WaitForExit(5000)){throw 'Owned QEMU did not stop'}
        $guest.WaitForExit();$guest.Dispose()
        Move-Item -LiteralPath $err -Destination "$run/qemu.err"
    }
}
foreach($arm in $modes){
    $source=$template
    $reason=''
    switch($arm){
        bound {$source=Replace-One $source 'gsc-present-pixels : Integer = 1024' 'gsc-present-pixels : Integer = 1000000000';$reason='bound=NO'}
        over-copy {
            $source=Replace-One $source 'in gsc-blit-unit-work dst stride tgt at (at + take)' 'in let copied = gsc-blit-unit-work dst stride tgt at (tgt.r3t-w * tgt.r3t-h) in at + take'
            $reason='bound=NO'
        }
        hook {$source=Replace-One $source 'else let service = (tgt.r3t-service) 0' 'else let service = 0';$reason='pump=NO'}
        order {
            $source=Replace-One $source 'else let service = (tgt.r3t-service) 0' 'else let service = 0'
            $source=Replace-One $source 'in gsc-blit-unit-work dst stride tgt at (at + take)' 'in let copied = gsc-blit-unit-work dst stride tgt at (at + take) in let service-after = (tgt.r3t-service) 0 in copied'
            $reason='order=NO'
        }
        target-lifetime {
            $source=Replace-One $source 'in let d = __record-set (sp.sp-tgt) "r3t-w" (gsc-fit w (sp.sp-bw))' 'in let d = __record-set sp "sp-tgt" (R3dTriState { r3t-base = sp.sp-px, r3t-depth = sp.sp-dp, r3t-w = gsc-fit w (sp.sp-bw), r3t-h = gsc-fit h (sp.sp-bh), r3t-stride = sp.sp-bw, r3t-service = r3d-no-service })'
            $reason='stable=NO'
        }
        view-lifetime {
            $source=Replace-One $source 'in let a = gsc-view-place gv x y w (h - gsc-label-band)' 'in let a = __record-set sp "sp-gv" (gv-new x y w (h - gsc-label-band))'
            $reason='stable=NO'
        }
        callback-lifetime {
            $source=Replace-One $source 'in let g = __record-set (sp.sp-tgt) "r3t-base" (sp.sp-px)' 'in let ephemeral-service = __record-set (sp.sp-tgt) "r3t-service" r3d-no-service in let g = __record-set (sp.sp-tgt) "r3t-base" (sp.sp-px)'
            $reason='stable=NO'
        }
        source-pitch {
            $source=Replace-One $source 'in let d = gsc-blit-span dst (y * stride) (tgt.r3t-base) (y * tgt.r3t-stride) (x + take) x' 'in let d = gsc-blit-span dst (y * stride) (tgt.r3t-base) (y * tgt.r3t-w) (x + take) x'
            $reason='pixels=NO'
        }
        dest-pitch {
            $source=Replace-One $source 'in let d = gsc-blit-span dst (y * stride) (tgt.r3t-base) (y * tgt.r3t-stride) (x + take) x' 'in let d = gsc-blit-span dst (y * tgt.r3t-w) (tgt.r3t-base) (y * tgt.r3t-stride) (x + take) x'
            $reason='pixels=NO'
        }
        drain {
            $source=Replace-One $source 'in let next = gsc-blit-unit (sp.sp-fb + gv.gv-x * 4) stride tgt (sp.sp-present)' 'in let full = gsc-blit-serviced (sp.sp-fb + gv.gv-x * 4) stride tgt 0 in let next = tgt.r3t-w * tgt.r3t-h'
            $reason='one step copies a bounded prefix: NO'
        }
        rebuild {
            $source=Replace-One $source 'f <- if software & sp.sp-present >= 0 then' 'f <- if False then'
            $reason='pending copy owns one completed frame: NO'
        }
        move {
            $source=Replace-One $source 'in let invalid = if same-view & same-rows & same-base then 0 else gsc-invalidate sp' 'in let invalid = 0'
            $reason='moved pane cancels stale publication: NO'
        }
        pitch {
            $source=Replace-One $source 'in let same-rows = gv.gv-h == h - gsc-label-band & sp.sp-stride == stride' 'in let same-rows = gv.gv-h == h - gsc-label-band'
            $reason='pitch and size changes invalidate: NO'
        }
        base {
            $source=Replace-One $source 'if same-view & same-rows & same-base then 0 else gsc-invalidate sp' 'if same-view & same-rows then 0 else gsc-invalidate sp'
            $reason='framebuffer replacement invalidates: NO'
        }
        hide {
            $source=Replace-One $source "let cancel = gsc-cancel sp`n      in gsc-step-hide" "let cancel = 0`n      in gsc-step-hide"
            $reason='hide cancels pending copy: NO'
        }
        shadow {
            $source=Replace-One $source "else if sc == 31 then`n      let cancel = gsc-invalidate sp" "else if sc == 31 then`n      let cancel = 0"
            $reason='render option changes invalidate: NO'
        }
        gpu {
            $source=Replace-One $source "else if sc == 34 then`n      let cancel = gsc-invalidate sp" "else if sc == 34 then`n      let cancel = 0"
            $reason='render option changes invalidate: NO'
        }
        redraw {
            $source=Replace-One $source "gsc-show (stride) (font) (build) (sp) = act`n    let cancel = gsc-cancel sp" "gsc-show (stride) (font) (build) (sp) = act`n    let cancel = 0"
            $reason='cached redraw cancels pending copy: NO'
        }
        chrome-stay {
            $source=Replace-One $source "else if ev == desk-wnd-ev-stay then act`n          let cancel = gsc-cancel sp" "else if ev == desk-wnd-ev-stay then act`n          let cancel = 0"
            $reason='chrome repaint cancels pending copy: NO'
        }
        chrome-min {
            $source=Replace-One $source "let cancel = gsc-cancel sp`n          in desk-step-hide" "let cancel = 0`n          in desk-step-hide"
            $reason='chrome minimize cancels pending copy: NO'
        }
        chrome-close {
            $source=Replace-One $source "v <- (gs-viewport-release)`n          gsc-cancel sp" "v <- (gs-viewport-release)`n          0"
            $reason='chrome close cancels pending copy: NO'
        }
    }
    $src="$OutDir/$arm.codex";$cdx="$OutDir/$arm.cdx"
    [IO.File]::WriteAllText($src,$source,[Text.UTF8Encoding]::new($false))
    Admit
    & pwsh -NoProfile -File build/compile.ps1 -Src $src -Out $cdx -Log "$OutDir/$arm-compile.log" -Kernel $kernelPath
    if($LASTEXITCODE -ne 0){throw "$arm compile failed"}
    $cdxHash=(Get-FileHash $cdx).Hash
    foreach($bedName in $beds){
        $run="$OutDir/$bedName-$arm"
        New-Item -ItemType Directory -Path $run|Out-Null
        if($bedName -eq 'codex-vm'){
            Admit
            & pwsh -NoProfile -File build/test-run.ps1 -Kernel $cdx -OutFile "$run/trace.log"
            if($LASTEXITCODE -ne 0){throw "$bedName $arm execution failed"}
        }else{Run-Ovmf $cdx $run}
        $trace=[IO.File]::ReadAllText("$run/trace.log").Replace("`r",'')
        if($trace.Contains('!EXC')){throw "$bedName $arm trapped"}
        $start=$trace.IndexOf($first)
        if($start -lt 0){throw "$bedName $arm produced no verdict"}
        $body=$trace.Substring($start).Trim()
        $shape='^'+[regex]::Escape($expected).Replace('yes','(?:yes|NO)')+'$'
        $valid=$body -match $shape
        $ok=$valid -and $(if($arm -eq 'positive'){[string]::Equals($body,$expected,[StringComparison]::Ordinal)}else{$body.Contains($reason) -and ($Unit -eq 'step' -or $body.Contains('accepted=NO'))})
        if((Get-FileHash $cdx).Hash -ne $cdxHash -or (Get-FileHash $kernelPath).Hash -ne $kernelHash -or (Get-FileHash (Join-Path $repo 'tools/codex-vm.exe')).Hash -ne $vmHash){throw 'Proof artifact changed during execution'}
        $record=[ordered]@{unit=$Unit;bed=$bedName;arm=$arm;passed=$ok;kernel=$kernelHash;vm=$vmHash;source=(Get-FileHash $src).Hash;cdx=$cdxHash;trace="$run/trace.log"}
        if($bedName -eq 'ovmf'){$record.image=(Get-Content "$run/image.sha256" -Raw).Trim()}
        $records.Add($record)
        $records|ConvertTo-Json -Depth 5|Set-Content "$OutDir/results.json" -Encoding utf8
        "$(if($ok){'PASS'}else{'FAIL'}) $bedName $arm"
        if(-not $ok){throw "$bedName $arm did not meet the required verdict"}
    }
}
"Presentation unit proof passed: $OutDir"
