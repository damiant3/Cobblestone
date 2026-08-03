<#
.SYNOPSIS
    Font AI training pipeline. Extracts polygon contour points from
    TTF fonts, trains an MLP to predict glyph outlines as 12-point
    polygons, and exports weights for bare-metal inference.

.PARAMETER FontDir
    Directory containing .ttf files. Default: fonts/cc0

.PARAMETER Epochs
    Training epochs. Default: 1000

.EXAMPLE
    pwsh apps/fontai/train.ps1
    pwsh apps/fontai/train.ps1 -FontDir ~/my-fonts -Epochs 2000
#>
[CmdletBinding()]
param(
    [string]$FontDir = "fonts/cc0",
    [int]$Epochs = 10000,
    [string]$OutDir = "apps/fontai/build-output"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# === TTF Parsing (same as before) ===
function Read-U16($b, $o) { $b[$o] * 256 + $b[$o+1] }
function Read-I16($b, $o) { $v = Read-U16 $b $o; if ($v -ge 32768) { $v - 65536 } else { $v } }
function Read-U32($b, $o) { $b[$o]*16777216 + $b[$o+1]*65536 + $b[$o+2]*256 + $b[$o+3] }
function Read-Tag($b, $o) { [char]$b[$o] + [char]$b[$o+1] + [char]$b[$o+2] + [char]$b[$o+3] }
function Find-Table($b, $n, $tag) { return Find-TableAt $b $n $tag 0 }
function Find-TableAt($b, $n, $tag, $base) {
    for ($i = 0; $i -lt $n; $i++) { $o = $base+12+$i*16; if ((Read-Tag $b $o) -eq $tag) { return Read-U32 $b ($o+8) } }; -1
}

function Read-SimpleGlyph($b, $off, $nContours) {
    $endPts = @(); $epOff = $off + 10
    for ($i = 0; $i -lt $nContours; $i++) { $endPts += Read-U16 $b ($epOff + $i*2) }
    $totalPts = if ($nContours -gt 0) { $endPts[$nContours-1] + 1 } else { 0 }
    $instrLen = Read-U16 $b ($epOff + $nContours*2)
    $flagsOff = $epOff + $nContours*2 + 2 + $instrLen
    $flags = @(); $fOff = $flagsOff
    while ($flags.Count -lt $totalPts) {
        $f = $b[$fOff]; $fOff++; $flags += $f
        if ($f -band 8) { $rep = $b[$fOff]; $fOff++; for ($r = 0; $r -lt $rep; $r++) { $flags += $f } }
    }
    $xs = @(); $prev = 0
    for ($i = 0; $i -lt $totalPts; $i++) {
        $f = $flags[$i]; $isShort = $f -band 2; $isSame = $f -band 16
        if ($isShort) { $d = $b[$fOff]; $fOff++; if (-not $isSame) { $d = -$d }; $prev += $d }
        elseif ($isSame) { }
        else { $d = Read-I16 $b $fOff; $fOff += 2; $prev += $d }
        $xs += $prev
    }
    $ys = @(); $prev = 0
    for ($i = 0; $i -lt $totalPts; $i++) {
        $f = $flags[$i]; $isShort = $f -band 4; $isSame = $f -band 32
        if ($isShort) { $d = $b[$fOff]; $fOff++; if (-not $isSame) { $d = -$d }; $prev += $d }
        elseif ($isSame) { }
        else { $d = Read-I16 $b $fOff; $fOff += 2; $prev += $d }
        $ys += $prev
    }
    # Return all points with on-curve flags and contour boundaries
    $contours = [System.Collections.Generic.List[object]]::new()
    $start = 0
    for ($ci = 0; $ci -lt $nContours; $ci++) {
        $end = $endPts[$ci]
        $pts = [System.Collections.Generic.List[hashtable]]::new()
        for ($pi = $start; $pi -le $end; $pi++) {
            $onCurve = [bool]($flags[$pi] -band 1)
            $pts.Add(@{x=$xs[$pi]; y=$ys[$pi]; on=$onCurve})
        }
        $contours.Add($pts)
        $start = $end + 1
    }
    return ,$contours
}

function Read-CompoundGlyph($b, $off, $glyfOff, $loca) {
    # Read first component of a compound glyph
    $cOff = $off + 10  # skip header (nContours=-1, bbox)
    $flags = Read-U16 $b $cOff; $cOff += 2
    $childGid = Read-U16 $b $cOff; $cOff += 2
    # Read offsets
    $dx = 0; $dy = 0
    if ($flags -band 1) { # ARG_1_AND_2_ARE_WORDS
        $dx = Read-I16 $b $cOff; $cOff += 2
        $dy = Read-I16 $b $cOff; $cOff += 2
    } else {
        $dx = if ($b[$cOff] -ge 128) { $b[$cOff] - 256 } else { $b[$cOff] }; $cOff++
        $dy = if ($b[$cOff] -ge 128) { $b[$cOff] - 256 } else { $b[$cOff] }; $cOff++
    }
    # Resolve child glyph
    if ($childGid -ge $loca.Count - 1) { return @() }
    $childOff = $glyfOff + $loca[$childGid]
    $childEnd = $glyfOff + $loca[$childGid + 1]
    if ($childOff -eq $childEnd) { return @() }
    $childNc = Read-I16 $b $childOff
    if ($childNc -le 0 -or $childNc -ge 20) { return @() }
    $contours = Read-SimpleGlyph $b $childOff $childNc
    # Apply offset to all points
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($c in $contours) {
        $shifted = [System.Collections.Generic.List[hashtable]]::new()
        foreach ($pt in $c) { $shifted.Add(@{x=($pt.x + $dx); y=($pt.y + $dy); on=$pt.on}) }
        $result.Add($shifted)
    }
    return ,$result
}

function Extract-Kerning($b, $base, $cmap, $upem) {
    # Extract GPOS pair positioning kerning values for ASCII pairs
    # Returns hashtable: "$leftCp,$rightCp" -> kern value normalized to UPM
    $nt = Read-U16 $b ($base+4)
    $gposOff = Find-TableAt $b $nt "GPOS" $base
    if ($gposOff -lt 0) { return @{} }
    $llOff = $gposOff + (Read-U16 $b ($gposOff+8))
    $nLookups = Read-U16 $b $llOff
    # Build reverse cmap: glyphID -> codepoint (for ASCII only)
    $gid2cp = @{}
    foreach ($k in $cmap.Keys) { if ([int]$k -ge 32 -and [int]$k -le 126) { $gid2cp[$cmap[$k]] = [int]$k } }
    $kernPairs = @{}
    for ($li = 0; $li -lt $nLookups; $li++) {
        $luOff = $llOff + (Read-U16 $b ($llOff+2+$li*2))
        $luType = Read-U16 $b $luOff; $nSubs = Read-U16 $b ($luOff+4)
        for ($si = 0; $si -lt $nSubs; $si++) {
            $stOff = $luOff + (Read-U16 $b ($luOff+6+$si*2))
            $actualType = $luType
            if ($luType -eq 9) {
                $actualType = Read-U16 $b ($stOff+2)
                $stOff = $stOff + (Read-U32 $b ($stOff+4))
            }
            if ($actualType -ne 2) { continue }
            $fmt = Read-U16 $b $stOff
            if ($fmt -eq 1) {
                # Format 1: individual glyph pairs
                $covOff = $stOff + (Read-U16 $b ($stOff+2))
                $vf1 = Read-U16 $b ($stOff+4); $vf2 = Read-U16 $b ($stOff+6)
                # Value record sizes
                $vrs1=0;$v=$vf1;while($v){$vrs1+=2;$v=$v-band($v-1)}
                $vrs2=0;$v=$vf2;while($v){$vrs2+=2;$v=$v-band($v-1)}
                $nPairSets = Read-U16 $b ($stOff+8)
                # Parse coverage to get first glyph IDs
                $covFmt = Read-U16 $b $covOff
                $covGids = @()
                if ($covFmt -eq 1) {
                    $covCount = Read-U16 $b ($covOff+2)
                    for ($ci=0;$ci-lt$covCount;$ci++) { $covGids += Read-U16 $b ($covOff+4+$ci*2) }
                } elseif ($covFmt -eq 2) {
                    $nRanges = Read-U16 $b ($covOff+2)
                    for ($ri=0;$ri-lt$nRanges;$ri++) {
                        $rs = Read-U16 $b ($covOff+4+$ri*6); $re = Read-U16 $b ($covOff+6+$ri*6)
                        for ($g=$rs;$g-le$re;$g++) { $covGids += $g }
                    }
                }
                for ($ps=0;$ps-lt[math]::Min($nPairSets,$covGids.Count);$ps++) {
                    $gid1 = $covGids[$ps]
                    if (-not $gid2cp.ContainsKey($gid1)) { continue }
                    $cp1 = $gid2cp[$gid1]
                    $psOff = $stOff + (Read-U16 $b ($stOff+10+$ps*2))
                    $nPairs = Read-U16 $b $psOff
                    for ($pi=0;$pi-lt$nPairs;$pi++) {
                        $pOff = $psOff + 2 + $pi*(2+$vrs1+$vrs2)
                        $gid2 = Read-U16 $b $pOff
                        if (-not $gid2cp.ContainsKey($gid2)) { continue }
                        $cp2 = $gid2cp[$gid2]
                        $xAdv = if ($vf1 -band 4) { Read-I16 $b ($pOff+2) } else { 0 }
                        if ($xAdv -ne 0) { $kernPairs["$cp1,$cp2"] = [int]$xAdv }
                    }
                }
            }
            # Skip format 2 (class-based) for now -- individual pairs are more precise
        }
    }
    return $kernPairs
}

function Is-TTC($b) {
    $b.Length -gt 12 -and $b[0] -eq 0x74 -and $b[1] -eq 0x74 -and $b[2] -eq 0x63 -and $b[3] -eq 0x66
}

function Get-TTC-Offsets($b) {
    $n = Read-U32 $b 8
    $offsets = @()
    for ($i = 0; $i -lt $n; $i++) { $offsets += Read-U32 $b (12 + $i * 4) }
    return $offsets
}

function Is-TextFont($b, $nt, $base) {
    if ($null -eq $base) { $base = 0 }
    # Reject non-text fonts: symbol, dingbat, decorative, icon
    # Check 1: sfVersion must be 00010000 (TrueType) not OTTO (CFF)
    if ($b[$base] -ne 0 -or $b[$base+1] -ne 1 -or $b[$base+2] -ne 0 -or $b[$base+3] -ne 0) { return $false }
    # Check 2: must have glyf table (not CFF-based)
    $hasGlyf = $false; $hasCmap = $false
    for ($i = 0; $i -lt $nt; $i++) {
        $o = $base+12+$i*16; $tag = Read-Tag $b $o
        if ($tag -eq "glyf") { $hasGlyf = $true }
        if ($tag -eq "cmap") { $hasCmap = $true }
    }
    if (-not $hasGlyf -or -not $hasCmap) { return $false }
    # Check 3: OS/2 sFamilyClass -- reject class 12 (Symbolic) and 14 (Pictorial)
    $os2o = Find-TableAt $b $nt "OS/2" $base
    if ($os2o -ge 0) {
        $familyClass = Read-I16 $b ($os2o+30)
        $classHi = [math]::Floor($familyClass / 256)
        if ($classHi -eq 12 -or $classHi -eq 14) { return $false }
    }
    # Check 4: must have a Unicode or Windows BMP cmap (platform 3 encoding 1)
    $co = Find-TableAt $b $nt "cmap" $base
    if ($co -lt 0) { return $false }
    $ns = Read-U16 $b ($co+2); $hasUnicode = $false
    for ($i = 0; $i -lt $ns; $i++) {
        $so = $co+4+$i*8
        $plat = Read-U16 $b $so; $enc = Read-U16 $b ($so+2)
        if ($plat -eq 3 -and $enc -eq 1) { $hasUnicode = $true }
        if ($plat -eq 0) { $hasUnicode = $true }
    }
    if (-not $hasUnicode) { return $false }
    # Check 5: must have at least 26 glyphs mapped in ASCII A-Z range
    # (weeds out icon/emoji fonts that only have a few mapped codepoints)
    $co2 = Find-TableAt $b $nt "cmap" $base
    $ns2 = Read-U16 $b ($co2+2); $c4 = -1
    for ($i = 0; $i -lt $ns2; $i++) {
        $so = $co2+4+$i*8; $sto = $co2+(Read-U32 $b ($so+4))
        if ((Read-U16 $b $sto) -eq 4) { $c4 = $sto; break }
    }
    if ($c4 -lt 0) { return $false }
    $sc = (Read-U16 $b ($c4+6))/2; $eo = $c4+14; $so2 = $eo+$sc*2+2; $do2 = $so2+$sc*2
    $mapped = 0
    for ($s = 0; $s -lt $sc; $s++) {
        $ec = Read-U16 $b ($eo+$s*2); $sc2 = Read-U16 $b ($so2+$s*2)
        if ($ec -eq 0xFFFF) { continue }
        for ($cp = [math]::Max($sc2, 65); $cp -le [math]::Min($ec, 90); $cp++) { $mapped++ }
    }
    if ($mapped -lt 20) { return $false }
    return $true
}

function Extract-FontFromBytes($b, $base) {
    $nt = Read-U16 $b ($base+4)
    if (-not (Is-TextFont $b $nt $base)) { return $null }
    $ho = Find-TableAt $b $nt "head" $base; $mo = Find-TableAt $b $nt "maxp" $base
    $hho = Find-TableAt $b $nt "hhea" $base; $hmo = Find-TableAt $b $nt "hmtx" $base
    $co = Find-TableAt $b $nt "cmap" $base; $lo = Find-TableAt $b $nt "loca" $base
    $go = Find-TableAt $b $nt "glyf" $base
    if ($ho -lt 0) { return $null }
    $upem = Read-U16 $b ($ho+18); $itl = Read-I16 $b ($ho+50)
    $ng = Read-U16 $b ($mo+4); $nh = Read-U16 $b ($hho+34)
    $loca = @(); for ($i = 0; $i -le $ng; $i++) {
        if ($itl -eq 0) { $loca += (Read-U16 $b ($lo+$i*2))*2 } else { $loca += Read-U32 $b ($lo+$i*4) }
    }
    $advs = @(); for ($i = 0; $i -lt $nh; $i++) { $advs += Read-U16 $b ($hmo+$i*4) }
    $ns = Read-U16 $b ($co+2); $c4 = -1
    for ($i = 0; $i -lt $ns; $i++) {
        $so = $co+4+$i*8; $sto = $co+(Read-U32 $b ($so+4))
        if ((Read-U16 $b $sto) -eq 4) { $c4 = $sto; break }
    }
    $cmap = @{}
    if ($c4 -ge 0) {
        $sc = (Read-U16 $b ($c4+6))/2; $eo = $c4+14; $so2 = $eo+$sc*2+2; $do2 = $so2+$sc*2
        $ro2 = $do2+$sc*2  # idRangeOffset array
        for ($s = 0; $s -lt $sc; $s++) {
            $ec = Read-U16 $b ($eo+$s*2); $sc2 = Read-U16 $b ($so2+$s*2)
            $d = Read-I16 $b ($do2+$s*2)
            $roff = Read-U16 $b ($ro2+$s*2)
            if ($ec -eq 0xFFFF) { continue }
            for ($cp = $sc2; $cp -le $ec; $cp++) {
                if ($roff -eq 0) {
                    $cmap[$cp] = ($cp+$d)%65536
                } else {
                    $glyphAddr = $ro2+$s*2 + $roff + ($cp - $sc2) * 2
                    $gid = Read-U16 $b $glyphAddr
                    if ($gid -ne 0) { $gid = ($gid + $d) % 65536 }
                    $cmap[$cp] = $gid
                }
            }
        }
    }
    # Read weight class from OS/2 table (400=regular, 700=bold)
    $os2o = Find-TableAt $b $nt "OS/2" $base
    $weightClass = if ($os2o -ge 0) { Read-U16 $b ($os2o+4) } else { 400 }
    $weightNorm = [math]::Min(1.0, [math]::Max(0.0, ($weightClass - 100) / 800.0))
    # Read italic from head macStyle (bit 1)
    $macStyle = Read-U16 $b ($ho+44)
    $italicFlag = if ($macStyle -band 2) { 1.0 } else { 0.0 }

    # Extract kerning
    $kernPairs = Extract-Kerning $b $base $cmap $upem

    $glyphs = @()
    for ($cp = 32; $cp -le 126; $cp++) {
        $gid = if ($cmap.ContainsKey($cp)) { $cmap[$cp] } else { 0 }
        $adv = if ($gid -lt $advs.Count) { $advs[$gid] } else { 0 }
        $gOff = $go + $loca[$gid]; $gEnd = $go + $loca[$gid+1]
        $contours = @()
        if ($gOff -ne $gEnd -and $gOff -lt $b.Count-10) {
            $nc = Read-I16 $b $gOff
            if ($nc -gt 0 -and $nc -lt 20) {
                try { $contours = Read-SimpleGlyph $b $gOff $nc } catch { $contours = @() }
            } elseif ($nc -eq -1) {
                # Compound glyph: resolve first component only
                try { $contours = Read-CompoundGlyph $b $gOff $go $loca } catch { $contours = @() }
            }
        }
        $glyphs += @{cp=$cp; adv=$adv; contours=$contours; upem=$upem; weight=$weightNorm; italic=$italicFlag}
    }
    return @{glyphs=$glyphs; kernPairs=$kernPairs; nKernPairs=$kernPairs.Count}
}

function Extract-Font($path) {
    $b = [System.IO.File]::ReadAllBytes($path)
    if (Is-TTC $b) { return $null }
    $result = Extract-FontFromBytes $b 0
    if ($null -eq $result) { return $null }
    return $result
}

function Extract-TTC($path) {
    $b = [System.IO.File]::ReadAllBytes($path)
    if (-not (Is-TTC $b)) { return @() }
    $offsets = Get-TTC-Offsets $b
    $results = @()
    foreach ($off in $offsets) {
        $glyphs = Extract-FontFromBytes $b $off
        if ($null -ne $glyphs) { $results += ,@($glyphs) }
    }
    return $results
}

# === Flatten quadratic Bezier curves into dense on-curve points ===
function Flatten-Contour($pts, $stepsPerCurve) {
    if ($pts.Count -lt 2) { return $pts }
    if ($null -eq $stepsPerCurve) { $stepsPerCurve = 8 }
    # TrueType quadratic Bezier rules:
    # - on-curve -> on-curve = straight line
    # - on-curve -> off-curve -> on-curve = quadratic Bezier
    # - off-curve -> off-curve = implied on-curve at midpoint between them
    # First, insert implied on-curve points between consecutive off-curve points
    $expanded = [System.Collections.Generic.List[hashtable]]::new()
    for ($i = 0; $i -lt $pts.Count; $i++) {
        $cur = $pts[$i]
        $nxt = $pts[($i + 1) % $pts.Count]
        $expanded.Add($cur)
        if (-not $cur.on -and -not $nxt.on) {
            $expanded.Add(@{x=[int](($cur.x + $nxt.x) / 2); y=[int](($cur.y + $nxt.y) / 2); on=$true})
        }
    }
    # Now walk the expanded list and flatten curves
    $flat = [System.Collections.Generic.List[hashtable]]::new()
    for ($i = 0; $i -lt $expanded.Count; $i++) {
        $cur = $expanded[$i]
        $nxt = $expanded[($i + 1) % $expanded.Count]
        if ($cur.on) {
            $flat.Add(@{x=$cur.x; y=$cur.y})
            if (-not $nxt.on) {
                # Quadratic Bezier: cur (on) -> nxt (off) -> next-next (on)
                $ctrl = $nxt
                $end = $expanded[($i + 2) % $expanded.Count]
                for ($s = 1; $s -le $stepsPerCurve; $s++) {
                    $t = [double]$s / $stepsPerCurve
                    $mt = 1.0 - $t
                    $bx = $mt * $mt * $cur.x + 2 * $mt * $t * $ctrl.x + $t * $t * $end.x
                    $by = $mt * $mt * $cur.y + 2 * $mt * $t * $ctrl.y + $t * $t * $end.y
                    $flat.Add(@{x=[int][math]::Round($bx); y=[int][math]::Round($by)})
                }
            }
        }
        # off-curve points are consumed by the on-curve before them
    }
    if ($flat.Count -lt 2) { return $pts }
    return @($flat)
}

# === Resample a contour to N evenly-spaced points ===
function Resample-Contour($pts, $n) {
    if ($pts.Count -lt 2) { return $null }
    # Compute cumulative arc lengths
    $lens = @(0.0)
    for ($i = 1; $i -le $pts.Count; $i++) {
        $p0 = $pts[($i-1) % $pts.Count]
        $p1 = $pts[$i % $pts.Count]
        $dx = $p1.x - $p0.x; $dy = $p1.y - $p0.y
        $lens += $lens[$i-1] + [math]::Sqrt($dx*$dx + $dy*$dy)
    }
    $total = $lens[$pts.Count]
    if ($total -lt 1) { return $null }
    $resampled = @()
    for ($i = 0; $i -lt $n; $i++) {
        $target = $i * $total / $n
        # Find segment
        $seg = 0
        for ($s = 1; $s -le $pts.Count; $s++) { if ($lens[$s] -ge $target) { $seg = $s - 1; break } }
        $segLen = $lens[$seg+1] - $lens[$seg]
        $t = if ($segLen -gt 0) { ($target - $lens[$seg]) / $segLen } else { 0 }
        $p0 = $pts[$seg % $pts.Count]; $p1 = $pts[($seg+1) % $pts.Count]
        $resampled += @{x=[int][math]::Round($p0.x + ($p1.x - $p0.x) * $t); y=[int][math]::Round($p0.y + ($p1.y - $p0.y) * $t)}
    }
    return $resampled
}

# === Extract ===
Write-Host "[fontai] Scanning $FontDir..."
$ttfFiles = @(Get-ChildItem -Path $FontDir -Recurse -Filter "*.ttf" | Sort-Object Name)
$ttcFiles = @(Get-ChildItem -Path $FontDir -Recurse -Filter "*.ttc" | Sort-Object Name)
$totalFiles = $ttfFiles.Count + $ttcFiles.Count
if ($totalFiles -eq 0) { Write-Error "No .ttf/.ttc files in $FontDir"; exit 1 }
Write-Host "[fontai] Found $($ttfFiles.Count) TTF + $($ttcFiles.Count) TTC files"

$NOuterPts = 32; $NInner1Pts = 16; $NInner2Pts = 8; $NPoints = $NOuterPts + $NInner1Pts + $NInner2Pts
$allSamples = [System.Collections.Generic.List[hashtable]]::new()
$allKernSamples = [System.Collections.Generic.List[hashtable]]::new()
$fi = 0; $skipped = 0; $withHoles = 0; $rejected = 0

# Process individual TTF files
foreach ($f in $ttfFiles) {
    $fontData = Extract-Font $f.FullName
    if ($null -eq $fontData) { Write-Host "  SKIP $($f.Name) (not a text font)"; $rejected++; continue }
    $glyphs = $fontData.glyphs; $fontKernPairs = $fontData.kernPairs
    $used = 0
    foreach ($g in $glyphs) {
        $u = [double]$g.upem; if ($u -eq 0) { $u = 1 }
        # Rank contours by perimeter (largest=outer, second=hole)
        $ranked = [System.Collections.Generic.List[hashtable]]::new()
        foreach ($c in $g.contours) {
            if ($null -eq $c -or $c.Count -lt 2) { continue }
            $perim = 0.0
            try {
                for ($pi = 0; $pi -lt $c.Count; $pi++) {
                    $p0 = $c[$pi]; $p1 = $c[($pi+1) % $c.Count]
                    $dx = $p1.x - $p0.x; $dy = $p1.y - $p0.y
                    $perim += [math]::Sqrt($dx*$dx + $dy*$dy)
                }
                $ranked.Add(@{contour=$c; perim=$perim})
            } catch { continue }
        }
        if ($ranked.Count -eq 0) { $skipped++; continue }
        $ranked = @($ranked | Sort-Object { -$_.perim })
        $outerC = $ranked[0].contour
        $innerC = if ($ranked.Count -ge 2) { $ranked[1].contour } else { $null }
        $inner2C = if ($ranked.Count -ge 3) { $ranked[2].contour } else { $null }
        # Flatten Bezier curves into dense on-curve points before resampling
        try { $outerC = Flatten-Contour $outerC 8 } catch { $skipped++; continue }
        try { $outerR = Resample-Contour $outerC $NOuterPts } catch { $skipped++; continue }
        if ($null -eq $outerR) { $skipped++; continue }
        # Flatten + resample inner contours if present
        $innerR = $null
        if ($NInner1Pts -gt 0 -and $null -ne $innerC -and $innerC.Count -ge 3) {
            try {
                $innerFlat = Flatten-Contour $innerC 8
                $innerR = Resample-Contour $innerFlat $NInner1Pts
            } catch {}
        }
        $inner2R = $null
        if ($NInner2Pts -gt 0 -and $null -ne $inner2C -and $inner2C.Count -ge 3) {
            try {
                $inner2Flat = Flatten-Contour $inner2C 8
                $inner2R = Resample-Contour $inner2Flat $NInner2Pts
            } catch {}
        }
        # Bbox from outer
        $bxMin = 1e9; $byMin = 1e9; $bxMax = -1e9; $byMax = -1e9
        foreach ($pt in $outerR) {
            $nx = $pt.x / $u; $ny = $pt.y / $u
            if ($nx -lt $bxMin) { $bxMin = $nx }; if ($nx -gt $bxMax) { $bxMax = $nx }
            if ($ny -lt $byMin) { $byMin = $ny }; if ($ny -gt $byMax) { $byMax = $ny }
        }
        $bw = $bxMax - $bxMin; $bh = $byMax - $byMin
        if ($bw -lt 0.01 -or $bh -lt 0.01) { $skipped++; continue }
        # Rotate outer to bottom-left start
        $bestIdx = 0; $bestScore = 1e9
        for ($pi = 0; $pi -lt $NOuterPts; $pi++) {
            $score = ($outerR[$pi].x / $u - $bxMin) / $bw + ($outerR[$pi].y / $u - $byMin) / $bh
            if ($score -lt $bestScore) { $bestScore = $score; $bestIdx = $pi }
        }
        # Output: bbox(4) + outer(NOuterPts*2) + has_hole1(1) + inner1(NInner1Pts*2) + has_hole2(1) + inner2(NInner2Pts*2)
        $nCoordVals = 4 + $NOuterPts * 2 + 1 + $NInner1Pts * 2 + 1 + $NInner2Pts * 2
        $coords = [double[]]::new($nCoordVals)
        $coords[0] = [math]::Round($bxMin * 1000); $coords[1] = [math]::Round($byMin * 1000)
        $coords[2] = [math]::Round($bw * 1000); $coords[3] = [math]::Round($bh * 1000)
        for ($pi = 0; $pi -lt $NOuterPts; $pi++) {
            $ri = ($pi + $bestIdx) % $NOuterPts
            $coords[4 + $pi*2]   = [math]::Round(($outerR[$ri].x / $u - $bxMin) / $bw * 1000)
            $coords[4 + $pi*2+1] = [math]::Round(($outerR[$ri].y / $u - $byMin) / $bh * 1000)
        }
        # Inner contour: direct points, normalized to outer bbox
        $hOff = 4 + $NOuterPts * 2
        if ($null -ne $innerR) {
            $coords[$hOff] = 1000  # has_hole = 1.0
            # Rotate inner to bottom-left start
            $bi2 = 0; $bs2 = 1e9
            for ($pi = 0; $pi -lt $NInner1Pts; $pi++) {
                $score = ($innerR[$pi].x / $u - $bxMin) / $bw + ($innerR[$pi].y / $u - $byMin) / $bh
                if ($score -lt $bs2) { $bs2 = $score; $bi2 = $pi }
            }
            for ($pi = 0; $pi -lt $NInner1Pts; $pi++) {
                $ri = ($pi + $bi2) % $NInner1Pts
                $coords[$hOff + 1 + $pi*2]   = [math]::Round(($innerR[$ri].x / $u - $bxMin) / $bw * 1000)
                $coords[$hOff + 1 + $pi*2+1] = [math]::Round(($innerR[$ri].y / $u - $byMin) / $bh * 1000)
            }
            $withHoles++
        } else {
            $coords[$hOff] = 0
            for ($pi = 0; $pi -lt $NInner1Pts; $pi++) {
                $coords[$hOff + 1 + $pi*2] = 500; $coords[$hOff + 1 + $pi*2+1] = 500
            }
        }
        # Second inner contour (third overall)
        $h2Off = $hOff + 1 + $NInner1Pts * 2
        if ($null -ne $inner2R) {
            $coords[$h2Off] = 1000  # has_hole2 = 1.0
            $bi3 = 0; $bs3 = 1e9
            for ($pi = 0; $pi -lt $NInner2Pts; $pi++) {
                $score = ($inner2R[$pi].x / $u - $bxMin) / $bw + ($inner2R[$pi].y / $u - $byMin) / $bh
                if ($score -lt $bs3) { $bs3 = $score; $bi3 = $pi }
            }
            for ($pi = 0; $pi -lt $NInner2Pts; $pi++) {
                $ri = ($pi + $bi3) % $NInner2Pts
                $coords[$h2Off + 1 + $pi*2]   = [math]::Round(($inner2R[$ri].x / $u - $bxMin) / $bw * 1000)
                $coords[$h2Off + 1 + $pi*2+1] = [math]::Round(($inner2R[$ri].y / $u - $byMin) / $bh * 1000)
            }
        } else {
            $coords[$h2Off] = 0
            for ($pi = 0; $pi -lt $NInner2Pts; $pi++) {
                $coords[$h2Off + 1 + $pi*2] = 500; $coords[$h2Off + 1 + $pi*2+1] = 500
            }
        }
        $allSamples.Add(@{ fi=[int]$fi; cp=[int]$g.cp; adv=[int][math]::Round($g.adv * 1000 / $u); weight=$g.weight; italic=$g.italic; coords=$coords })
        $used++
    }
    if ($used -lt 40) {
        # Reject: too few glyphs survived extraction, would poison training
        Write-Host "  REJECT $($f.Name): only $used/95 glyphs survived (need 40+)"
        # Remove the samples we just added
        while ($allSamples.Count -gt 0 -and $allSamples[$allSamples.Count-1].fi -eq $fi) { $allSamples.RemoveAt($allSamples.Count-1) }
        $rejected++; continue
    }
    # Collect kern pairs for this font
    $u = [double]$glyphs[0].upem; $nk = 0
    foreach ($key in $fontKernPairs.Keys) {
        $parts = $key -split ','
        $cp1 = [int]$parts[0]; $cp2 = [int]$parts[1]
        $kernVal = $fontKernPairs[$key] / $u  # normalize to UPM
        $allKernSamples.Add(@{fi=[int]$fi; cp1=$cp1; cp2=$cp2; kern=$kernVal; weight=$glyphs[0].weight; italic=$glyphs[0].italic})
        $nk++
    }
    $wc = if ($glyphs.Count -gt 0) { [int]($glyphs[0].weight * 800 + 100) } else { 400 }
    $it = if ($glyphs.Count -gt 0 -and $glyphs[0].italic -gt 0) { " italic" } else { "" }
    Write-Host "  $($f.Name): $used glyphs, $nk kern pairs, UPM=$($glyphs[0].upem), weight=$wc$it"
    $fi++
}
# Process TTC files (each contains multiple fonts)
foreach ($f in $ttcFiles) {
    $subFonts = Extract-TTC $f.FullName
    if ($subFonts.Count -eq 0) { Write-Host "  SKIP $($f.Name) (no text fonts in TTC)"; $rejected++; continue }
    $ttcIdx = 0
    foreach ($glyphs in $subFonts) {
        $ttcIdx++
        $used = 0
        foreach ($g in $glyphs) {
            $u = [double]$g.upem; if ($u -eq 0) { $u = 1 }
            $ranked = [System.Collections.Generic.List[hashtable]]::new()
            foreach ($c in $g.contours) {
                if ($null -eq $c -or $c.Count -lt 2) { continue }
                $perim = 0.0
                try {
                    for ($pi = 0; $pi -lt $c.Count; $pi++) {
                        $p0 = $c[$pi]; $p1 = $c[($pi+1) % $c.Count]
                        $dx = $p1.x - $p0.x; $dy = $p1.y - $p0.y
                        $perim += [math]::Sqrt($dx*$dx + $dy*$dy)
                    }
                    $ranked.Add(@{contour=$c; perim=$perim})
                } catch { continue }
            }
            if ($ranked.Count -eq 0) { $skipped++; continue }
            $ranked = @($ranked | Sort-Object { -$_.perim })
            $outerC = $ranked[0].contour
            try { $outerC = Flatten-Contour $outerC 8 } catch { $skipped++; continue }
            try { $outerR = Resample-Contour $outerC $NOuterPts } catch { $skipped++; continue }
            if ($null -eq $outerR) { $skipped++; continue }
            $bxMin = 1e9; $byMin = 1e9; $bxMax = -1e9; $byMax = -1e9
            foreach ($pt in $outerR) {
                $nx = $pt.x / $u; $ny = $pt.y / $u
                if ($nx -lt $bxMin) { $bxMin = $nx }; if ($nx -gt $bxMax) { $bxMax = $nx }
                if ($ny -lt $byMin) { $byMin = $ny }; if ($ny -gt $byMax) { $byMax = $ny }
            }
            $bw = $bxMax - $bxMin; $bh = $byMax - $byMin
            if ($bw -lt 0.01 -or $bh -lt 0.01) { $skipped++; continue }
            $bestIdx = 0; $bestScore = 1e9
            for ($pi = 0; $pi -lt $NOuterPts; $pi++) {
                $score = ($outerR[$pi].x / $u - $bxMin) / $bw + ($outerR[$pi].y / $u - $byMin) / $bh
                if ($score -lt $bestScore) { $bestScore = $score; $bestIdx = $pi }
            }
            $coords = [double[]]::new(4 + $NPoints * 2)
            $coords[0] = [math]::Round($bxMin * 1000); $coords[1] = [math]::Round($byMin * 1000)
            $coords[2] = [math]::Round($bw * 1000); $coords[3] = [math]::Round($bh * 1000)
            for ($pi = 0; $pi -lt $NOuterPts; $pi++) {
                $ri = ($pi + $bestIdx) % $NOuterPts
                $coords[4 + $pi*2]   = [math]::Round(($outerR[$ri].x / $u - $bxMin) / $bw * 1000)
                $coords[4 + $pi*2+1] = [math]::Round(($outerR[$ri].y / $u - $byMin) / $bh * 1000)
            }
            $allSamples.Add(@{ fi=[int]$fi; cp=[int]$g.cp; adv=[int][math]::Round($g.adv * 1000 / $u); weight=$g.weight; italic=$g.italic; coords=$coords })
            $used++
        }
        $wc = if ($glyphs.Count -gt 0) { [int]($glyphs[0].weight * 800 + 100) } else { 400 }
        $it = if ($glyphs.Count -gt 0 -and $glyphs[0].italic -gt 0) { " italic" } else { "" }
        Write-Host "  $($f.Name)[$ttcIdx]: $used glyphs, UPM=$($glyphs[0].upem), weight=$wc$it"
        $fi++
    }
}

Write-Host "[fontai] $($allSamples.Count) glyph samples, $($allKernSamples.Count) kern pairs from $fi fonts ($rejected rejected, $skipped glyphs skipped)"

# === Train ===
# Input: one-hot glyph(95) + weight(1) + italic(1) + char class(5) = 102
$inputDim = 102; $hidden1 = 256; $hidden2 = 128
$outputDim = 1 + 4 + $NOuterPts * 2 + 1 + $NInner1Pts * 2 + 1 + $NInner2Pts * 2  # advance(1) + bbox(4) + outer(64) + hole1(1+32) + hole2(1+16) = 119

Write-Host "[fontai] Training MLP ($inputDim -> $hidden1 -> $hidden2 -> $outputDim), $Epochs epochs, 8 threads..."

$trainerCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class FontTrainer5 {
    int I, H1, H2, O, NP, NThreads;
    double[] W1, W2, W3, b1, b2, b3;  // flat arrays for cache friendliness

    public FontTrainer5(int input, int h1, int h2, int output, int nPoints, int nThreads) {
        I=input; H1=h1; H2=h2; O=output; NP=nPoints; NThreads=nThreads;
        var rng=new Random(42);
        double s1=Math.Sqrt(6.0/(I+H1)), s2=Math.Sqrt(6.0/(H1+H2)), s3=Math.Sqrt(6.0/(H2+O));
        W1=new double[H1*I]; b1=new double[H1];
        W2=new double[H2*H1]; b2=new double[H2];
        W3=new double[O*H2]; b3=new double[O];
        for(int i=0;i<W1.Length;i++) W1[i]=(rng.NextDouble()*2-1)*s1;
        for(int i=0;i<W2.Length;i++) W2[i]=(rng.NextDouble()*2-1)*s2;
        for(int i=0;i<W3.Length;i++) W3[i]=(rng.NextDouble()*2-1)*s3;
    }

    // Adam moment buffers
    double[] mW1,vW1,mW2,vW2,mW3,vW3,mb1,vb1,mb2,vb2,mb3,vb3;
    void InitAdam() {
        mW1=new double[H1*I];vW1=new double[H1*I]; mb1=new double[H1];vb1=new double[H1];
        mW2=new double[H2*H1];vW2=new double[H2*H1]; mb2=new double[H2];vb2=new double[H2];
        mW3=new double[O*H2];vW3=new double[O*H2]; mb3=new double[O];vb3=new double[O];
    }

    // Per-thread gradient accumulators
    class ThreadGrads {
        public double[] gW1, gW2, gW3, gb1, gb2, gb3;
        public double loss;
    }

    void SampleGrad(int si, int[] cps, int[] fis, double[] wts, double[] its, int[] advs,
                    double[,] coords, int nFonts, ThreadGrads tg) {
        int gi=cps[si]-32; if(gi<0||gi>=95) return;
        double wt=wts[si], it=its[si];
        int cp=cps[si];
        double fU=(cp>=65&&cp<=90)?1:0, fL=(cp>=97&&cp<=122)?1:0;
        double fD=(cp>=48&&cp<=57)?1:0, fP=(fU==0&&fL==0&&fD==0&&cp!=32)?1:0;
        double fDe=(cp==103||cp==106||cp==112||cp==113||cp==121)?1:0;

        // Forward
        double[] h1=new double[H1], h2=new double[H2], o=new double[O];
        for(int r=0;r<H1;r++){
            double v=b1[r]; if(gi<95) v+=W1[r*I+gi];
            v+=W1[r*I+95]*wt+W1[r*I+96]*it+W1[r*I+97]*fU+W1[r*I+98]*fL+W1[r*I+99]*fD+W1[r*I+100]*fP+W1[r*I+101]*fDe;
            h1[r]=v>0?v:0;
        }
        for(int r=0;r<H2;r++){double v=b2[r]; for(int c=0;c<H1;c++) v+=W2[r*H1+c]*h1[c]; h2[r]=v>0?v:0;}
        for(int r=0;r<O;r++){double s=b3[r]; for(int c=0;c<H2;c++) s+=W3[r*H2+c]*h2[c]; o[r]=s;}

        // Loss + output gradient
        double[] dOut=new double[O];
        double[] target=new double[O];
        target[0]=advs[si]/1000.0;
        int NC=O-1; for(int ci=0;ci<NC;ci++) target[1+ci]=coords[si,ci]/1000.0;
        double loss=0;
        for(int j=0;j<O;j++){double e=o[j]-target[j]; loss+=e*e; dOut[j]=2*e/O;}
        tg.loss+=loss/O;

        // Backprop: output -> h2
        double[] dH2=new double[H2];
        for(int r=0;r<O;r++){double dr=dOut[r];
            for(int c=0;c<H2;c++){dH2[c]+=dr*W3[r*H2+c]; tg.gW3[r*H2+c]+=dr*h2[c];}
            tg.gb3[r]+=dr;
        }
        // Backprop: h2 -> h1
        double[] dH1=new double[H1];
        for(int r=0;r<H2;r++){double g=h2[r]>0?dH2[r]:0;
            for(int c=0;c<H1;c++){dH1[c]+=g*W2[r*H1+c]; tg.gW2[r*H1+c]+=g*h1[c];}
            tg.gb2[r]+=g;
        }
        // Backprop: h1 -> input (sparse)
        for(int r=0;r<H1;r++){double g=h1[r]>0?dH1[r]:0;
            if(gi<95) tg.gW1[r*I+gi]+=g;
            tg.gW1[r*I+95]+=g*wt; tg.gW1[r*I+96]+=g*it;
            tg.gW1[r*I+97]+=g*fU; tg.gW1[r*I+98]+=g*fL;
            tg.gW1[r*I+99]+=g*fD; tg.gW1[r*I+100]+=g*fP; tg.gW1[r*I+101]+=g*fDe;
            tg.gb1[r]+=g;
        }
    }

    public double Train(int[] cps, int[] fis, double[] wts, double[] its, int[] advs,
                        double[,] coords, int n, int nFonts, int epochs, double lr) {
        InitAdam();
        double bestLoss=double.MaxValue;
        int t=0;

        // Pre-allocate per-thread gradient buffers
        var tgrads=new ThreadGrads[NThreads];
        for(int ti=0;ti<NThreads;ti++){
            tgrads[ti]=new ThreadGrads();
            tgrads[ti].gW1=new double[H1*I]; tgrads[ti].gW2=new double[H2*H1]; tgrads[ti].gW3=new double[O*H2];
            tgrads[ti].gb1=new double[H1]; tgrads[ti].gb2=new double[H2]; tgrads[ti].gb3=new double[O];
        }

        int batchSize=64;
        int nBatches=(n+batchSize-1)/batchSize;
        for(int ep=0;ep<epochs;ep++){
            double totalLoss=0;
            for(int bi=0;bi<nBatches;bi++){
                int bStart=bi*batchSize, bEnd=Math.Min(bStart+batchSize,n);
                int bLen=bEnd-bStart;
                // Clear grads
                for(int ti=0;ti<NThreads;ti++){
                    Array.Clear(tgrads[ti].gW1,0,H1*I); Array.Clear(tgrads[ti].gW2,0,H2*H1);
                    Array.Clear(tgrads[ti].gW3,0,O*H2); Array.Clear(tgrads[ti].gb1,0,H1);
                    Array.Clear(tgrads[ti].gb2,0,H2); Array.Clear(tgrads[ti].gb3,0,O);
                    tgrads[ti].loss=0;
                }
                // Parallel forward+backward within batch
                Parallel.For(0, bLen, new ParallelOptions{MaxDegreeOfParallelism=NThreads}, j => {
                    int ti=j%NThreads;
                    SampleGrad(bStart+j,cps,fis,wts,its,advs,coords,nFonts,tgrads[ti]);
                });
                // Accumulate + Adam
                t++;
                double b1c=1.0-Math.Pow(0.9,t), b2c=1.0-Math.Pow(0.999,t);
                if(b1c<1e-10) b1c=1e-10; if(b2c<1e-10) b2c=1e-10;
                for(int ti=0;ti<NThreads;ti++) totalLoss+=tgrads[ti].loss;
                double scale=1.0/bLen;
                ApplyAdam(W1,mW1,vW1,tgrads,0,H1*I,scale,lr,b1c,b2c);
                ApplyAdam(b1,mb1,vb1,tgrads,1,H1,scale,lr,b1c,b2c);
                ApplyAdam(W2,mW2,vW2,tgrads,2,H2*H1,scale,lr,b1c,b2c);
                ApplyAdam(b2,mb2,vb2,tgrads,3,H2,scale,lr,b1c,b2c);
                ApplyAdam(W3,mW3,vW3,tgrads,4,O*H2,scale,lr,b1c,b2c);
                ApplyAdam(b3,mb3,vb3,tgrads,5,O,scale,lr,b1c,b2c);
            }
            double avg=totalLoss/n;
            if(avg<bestLoss) bestLoss=avg;
            if(ep%100==0||ep==epochs-1) Console.WriteLine("  epoch {0,5}: loss={1:F6}", ep, avg);
        }
        return bestLoss;
    }

    void ApplyAdam(double[] param, double[] m, double[] v, ThreadGrads[] tg, int gIdx, int len,
                   double scale, double lr, double b1c, double b2c) {
        for(int i=0;i<len;i++){
            double g=0;
            for(int ti=0;ti<NThreads;ti++){
                double[] src; switch(gIdx){case 0:src=tg[ti].gW1;break;case 1:src=tg[ti].gb1;break;
                    case 2:src=tg[ti].gW2;break;case 3:src=tg[ti].gb2;break;
                    case 4:src=tg[ti].gW3;break;default:src=tg[ti].gb3;break;}
                g+=src[i];
            }
            g*=scale;
            if(g>5)g=5; if(g<-5)g=-5;
            m[i]=0.9*m[i]+0.1*g; v[i]=0.999*v[i]+0.001*g*g;
            double mh=m[i]/b1c, vh=v[i]/b2c;
            param[i]-=lr*mh/(Math.Sqrt(vh)+1e-8);
        }
    }

    public double[] GetW1(){return(double[])W1.Clone();}
    public double[] GetB1(){return(double[])b1.Clone();}
    public double[] GetW2(){return(double[])W2.Clone();}
    public double[] GetB2(){return(double[])b2.Clone();}
    public double[] GetW3(){return(double[])W3.Clone();}
    public double[] GetB3(){return(double[])b3.Clone();}
}

public class KernTrainer {
    int I=192, H=64, O=1;
    double[] W1, b1, W2, b2;
    double[] mW1,vW1,mb1,vb1,mW2,vW2,mb2,vb2;

    public KernTrainer() {
        var rng=new Random(99);
        double s1=Math.Sqrt(6.0/(I+H)), s2=Math.Sqrt(6.0/(H+O));
        W1=new double[H*I]; b1=new double[H];
        W2=new double[O*H]; b2=new double[O];
        for(int i=0;i<W1.Length;i++) W1[i]=(rng.NextDouble()*2-1)*s1;
        for(int i=0;i<W2.Length;i++) W2[i]=(rng.NextDouble()*2-1)*s2;
        mW1=new double[H*I];vW1=new double[H*I];mb1=new double[H];vb1=new double[H];
        mW2=new double[O*H];vW2=new double[O*H];mb2=new double[O];vb2=new double[O];
    }

    public double Train(int[] cp1s, int[] cp2s, double[] wts, double[] its, double[] kerns, int n, int epochs, double lr) {
        double bestLoss=double.MaxValue; int t=0;
        for(int ep=0;ep<epochs;ep++){
            double totalLoss=0;
            for(int si=0;si<n;si++){
                t++;
                double b1c=1.0-Math.Pow(0.9,t), b2c=1.0-Math.Pow(0.999,t);
                if(b1c<1e-10)b1c=1e-10; if(b2c<1e-10)b2c=1e-10;
                int g1=cp1s[si]-32, g2=cp2s[si]-32;
                if(g1<0||g1>=95||g2<0||g2>=95) continue;
                // Forward: sparse input (two one-hots + 2 scalars)
                double[] h=new double[H];
                for(int r=0;r<H;r++){
                    double v=b1[r]+W1[r*I+g1]+W1[r*I+95+g2]+W1[r*I+190]*wts[si]+W1[r*I+191]*its[si];
                    h[r]=v>0?v:0;
                }
                double pred=b2[0]; for(int c=0;c<H;c++) pred+=W2[c]*h[c];
                double e=pred-kerns[si]; totalLoss+=e*e;
                double dOut=2*e;
                // Backprop
                for(int c=0;c<H;c++){
                    double dh=h[c]>0?dOut*W2[c]:0;
                    // Adam update W2
                    double g=dOut*h[c]; if(g>5)g=5;if(g<-5)g=-5;
                    mW2[c]=0.9*mW2[c]+0.1*g; vW2[c]=0.999*vW2[c]+0.001*g*g;
                    W2[c]-=lr*(mW2[c]/b1c)/(Math.Sqrt(vW2[c]/b2c)+1e-8);
                    // Adam update W1 (sparse)
                    if(dh!=0){
                        int idx1=c*I+g1; mW1[idx1]=0.9*mW1[idx1]+0.1*dh; vW1[idx1]=0.999*vW1[idx1]+0.001*dh*dh;
                        W1[idx1]-=lr*(mW1[idx1]/b1c)/(Math.Sqrt(vW1[idx1]/b2c)+1e-8);
                        int idx2=c*I+95+g2; mW1[idx2]=0.9*mW1[idx2]+0.1*dh; vW1[idx2]=0.999*vW1[idx2]+0.001*dh*dh;
                        W1[idx2]-=lr*(mW1[idx2]/b1c)/(Math.Sqrt(vW1[idx2]/b2c)+1e-8);
                        mb1[c]=0.9*mb1[c]+0.1*dh; vb1[c]=0.999*vb1[c]+0.001*dh*dh;
                        b1[c]-=lr*(mb1[c]/b1c)/(Math.Sqrt(vb1[c]/b2c)+1e-8);
                    }
                }
                mb2[0]=0.9*mb2[0]+0.1*dOut; vb2[0]=0.999*vb2[0]+0.001*dOut*dOut;
                b2[0]-=lr*(mb2[0]/b1c)/(Math.Sqrt(vb2[0]/b2c)+1e-8);
            }
            double avg=totalLoss/n;
            if(avg<bestLoss)bestLoss=avg;
            if(ep%200==0||ep==epochs-1) Console.WriteLine("  kern epoch {0,5}: loss={1:F6}", ep, avg);
        }
        return bestLoss;
    }
    public double[] GetW1(){return(double[])W1.Clone();}
    public double[] GetB1(){return(double[])b1.Clone();}
    public double[] GetW2(){return(double[])W2.Clone();}
    public double[] GetB2(){return(double[])b2.Clone();}
}
"@
Add-Type -TypeDefinition $trainerCode -Language CSharp

$trainer = [FontTrainer5]::new($inputDim, $hidden1, $hidden2, $outputDim, $NPoints, 8)
$n = $allSamples.Count
$cps = [int[]]::new($n); $fis = [int[]]::new($n); $advs = [int[]]::new($n)
$weights = [double[]]::new($n); $italics = [double[]]::new($n)
$nCoords = 4 + $NOuterPts * 2 + 1 + $NInner1Pts * 2 + 1 + $NInner2Pts * 2  # bbox + outer + hole1 + hole2
$coordData = [double[,]]::new($n, $nCoords)
for ($i = 0; $i -lt $n; $i++) {
    $s = $allSamples[$i]
    $cps[$i] = [int]$s['cp']; $fis[$i] = [int]$s['fi']; $advs[$i] = [int]$s['adv']
    $weights[$i] = [double]$s['weight']; $italics[$i] = [double]$s['italic']
    for ($j = 0; $j -lt $nCoords; $j++) { $coordData[$i,$j] = $s['coords'][$j] }
}

$bestLoss = $trainer.Train($cps, $fis, $weights, $italics, $advs, $coordData, $n, $fi, $Epochs, 0.001)
Write-Host "[fontai] Glyph training complete. Best loss: $($bestLoss.ToString('F6'))"

# === Train Kerning ===
$nk = $allKernSamples.Count
if ($nk -gt 0) {
    Write-Host "[fontai] Training kerning MLP (192 -> 64 -> 1), $nk samples..."
    $kernTrainer = [KernTrainer]::new()
    $kcp1s = [int[]]::new($nk); $kcp2s = [int[]]::new($nk)
    $kwts = [double[]]::new($nk); $kits = [double[]]::new($nk); $kkerns = [double[]]::new($nk)
    for ($i = 0; $i -lt $nk; $i++) {
        $s = $allKernSamples[$i]
        $kcp1s[$i] = [int]$s['cp1']; $kcp2s[$i] = [int]$s['cp2']
        $kwts[$i] = [double]$s['weight']; $kits[$i] = [double]$s['italic']
        $kkerns[$i] = [double]$s['kern']
    }
    $kernEpochs = [math]::Min($Epochs, 1000)
    $kernLoss = $kernTrainer.Train($kcp1s, $kcp2s, $kwts, $kits, $kkerns, $nk, $kernEpochs, 0.0001)
    Write-Host "[fontai] Kerning complete. Best loss: $($kernLoss.ToString('F6'))"
} else {
    Write-Host "[fontai] No kerning data found."
}

# === Export Weights ===
$W1flat = $trainer.GetW1(); $b1flat = $trainer.GetB1()
$W2flat = $trainer.GetW2(); $b2flat = $trainer.GetB2()
$W3flat = $trainer.GetW3(); $b3flat = $trainer.GetB3()

$weightsFile = Join-Path $OutDir "FontAiWeights.codex"
$sb = [System.Text.StringBuilder]::new()

function Emit-Array($sb, $name, $data, $perLine) {
    [void]$sb.AppendLine("  $name : List Integer")
    [void]$sb.AppendLine("  $name = [")
    for ($i = 0; $i -lt $data.Count; $i += $perLine) {
        $end = [math]::Min($i+$perLine-1, $data.Count-1)
        $vals = @(); for ($j = $i; $j -le $end; $j++) { $vals += [math]::Round($data[$j] * 1000) }
        $line = "    " + ($vals -join ", ")
        if ($end -lt $data.Count-1) { $line += "," }
        [void]$sb.AppendLine($line)
    }
    [void]$sb.AppendLine("  ]")
    [void]$sb.AppendLine("")
}

[void]$sb.AppendLine("Section: Model Config")
[void]$sb.AppendLine("  fai-input-dim : Integer = $inputDim")
[void]$sb.AppendLine("  fai-hidden1-dim : Integer = $hidden1")
[void]$sb.AppendLine("  fai-hidden2-dim : Integer = $hidden2")
[void]$sb.AppendLine("  fai-output-dim : Integer = $outputDim")
[void]$sb.AppendLine("  fai-n-points : Integer = $NPoints")
[void]$sb.AppendLine("  fai-n-outer-pts : Integer = $NOuterPts")
[void]$sb.AppendLine("  fai-n-inner1-pts : Integer = $NInner1Pts")
[void]$sb.AppendLine("  fai-n-inner2-pts : Integer = $NInner2Pts")
[void]$sb.AppendLine("  fai-has-bbox : Integer = 1")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Section: Trained Weights (generated by apps/fontai/train.ps1)")
Emit-Array $sb "fai-w1-data" $W1flat 16
Emit-Array $sb "fai-b1-data" $b1flat 16
Emit-Array $sb "fai-w2-data" $W2flat 16
Emit-Array $sb "fai-b2-data" $b2flat 16
Emit-Array $sb "fai-w3-data" $W3flat 16
Emit-Array $sb "fai-b3-data" $b3flat 16

if ($nk -gt 0) {
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("Section: Kerning Weights")
    [void]$sb.AppendLine("  fai-kern-trained : Integer = 1")
    $kW1 = $kernTrainer.GetW1(); $kb1 = $kernTrainer.GetB1()
    $kW2 = $kernTrainer.GetW2(); $kb2 = $kernTrainer.GetB2()
    Emit-Array $sb "fai-kern-w1-data" $kW1 16
    Emit-Array $sb "fai-kern-b1-data" $kb1 16
    Emit-Array $sb "fai-kern-w2-data" $kW2 16
    Emit-Array $sb "fai-kern-b2-data" $kb2 16
}

[System.IO.File]::WriteAllText($weightsFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

$totalParams = $hidden1*$inputDim + $hidden1 + $hidden2*$hidden1 + $hidden2 + $outputDim*$hidden2 + $outputDim
$kernParams = if ($nk -gt 0) { 192*64+64+64+1 } else { 0 }
Write-Host ""
Write-Host "[fontai] Weights: $weightsFile"
Write-Host "[fontai] Glyph: $inputDim -> $hidden1 -> $hidden2 -> $outputDim ($totalParams params)"
Write-Host "[fontai] Kern: 192 -> 64 -> 1 ($kernParams params, $nk training pairs)"
Write-Host "[fontai] Fonts trained on: $fi"
