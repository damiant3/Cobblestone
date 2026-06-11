# import-hyg.ps1 — Import HYG v4.2 CSV + deep-sky + constellations
#
# Reads hyg_v42.csv, writes starmap.dat with three sections:
#   1. Stars (from HYG CSV)
#   2. Deep-sky objects (Messier + notable NGC, hardcoded)
#   3. Constellation stick figures (IAU standard, hardcoded)
#
# Binary format (v2):
#
#   Header (64 bytes):
#     [0-3]   magic: "STAR" (0x53544152)
#     [4-7]   version: 2
#     [8-11]  star_count
#     [12-15] named_count
#     [16-19] name_table_offset
#     [20-23] dso_count          (deep-sky objects)
#     [24-27] dso_offset         (byte offset to DSO section)
#     [28-31] con_count          (constellation definitions)
#     [32-35] con_offset         (byte offset to constellation section)
#     [36-39] con_line_count     (total line segments)
#     [40-63] reserved
#
#   Stars at offset 64, 32 bytes each (same as v1)
#   Name table (length-prefixed UTF-8)
#
#   DSO section (at dso_offset), 80 bytes each:
#     [0-3]   id          (i32, 200000+)
#     [4-7]   x           (i32, parsecs * 1000)
#     [8-11]  y           (i32)
#     [12-15] z           (i32)
#     [16-17] mag         (i16, * 1000)
#     [18]    kind        (u8: 0=globular 1=open 2=planetary-neb 3=diffuse-neb
#                              4=galaxy 5=quasar 6=dark-neb 7=supernova-remnant)
#     [19-21] con         (3 bytes)
#     [22]    name_len    (u8)
#     [23-62] name        (40 bytes, UTF-8)
#     [63]    desc_len    (u8)
#     [64-79] desc        (16 bytes, truncated)
#
#   Constellation section (at con_offset):
#     Per constellation: [u8 abbr_len] [3 bytes abbr] [u8 name_len] [20 bytes name]
#                        [u16 line_count] then line_count * [i32 from_hyg_id, i32 to_hyg_id]
#
# Data: HYG v4.2 by David Nash, CC BY-SA 4.0
#       Hipparcos (ESA), Yale BSC, Gliese catalogs.

[CmdletBinding()]
param(
    [string]$CsvPath = (Join-Path $PSScriptRoot 'data\hyg_v42.csv'),
    [string]$OutPath = (Join-Path $PSScriptRoot 'data\starmap.dat'),
    [double]$MagCutoff = 12.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $CsvPath)) {
    Write-Error "CSV not found: $CsvPath. Download from https://codeberg.org/astronexus/hyg"
    return
}

Write-Host "[import-hyg] Reading $CsvPath ..." -ForegroundColor Cyan
$lines = [System.IO.File]::ReadAllLines($CsvPath)
Write-Host "  $($lines.Length - 1) rows in CSV"

$hdr = $lines[0] -replace '"','' -split ','
$colIdx = @{}
for ($i = 0; $i -lt $hdr.Length; $i++) { $colIdx[$hdr[$i]] = $i }

function Get-Field($fields, $name) {
    $idx = $colIdx[$name]; if ($null -eq $idx) { return '' }
    return ($fields[$idx] -replace '"','')
}
function Parse-Double($s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return 0.0 }
    $v = 0.0
    if ([double]::TryParse($s, [System.Globalization.NumberStyles]::Any,
        [System.Globalization.CultureInfo]::InvariantCulture, [ref]$v)) { return $v }
    return 0.0
}
function Spect-To-Byte($s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return 10 }
    switch ($s[0]) {
        'O'{return 0}'B'{return 1}'A'{return 2}'F'{return 3}'G'{return 4}
        'K'{return 5}'M'{return 6}'L'{return 7}'T'{return 8}'W'{return 9}
        default{return 10}
    }
}

# --- Build HIP-to-HYG-ID lookup for constellations ---
Write-Host "[import-hyg] Building HIP lookup ..." -ForegroundColor Cyan
$hipToId = @{}
for ($row = 1; $row -lt $lines.Length; $row++) {
    $line = $lines[$row]
    $fields = [System.Collections.Generic.List[string]]::new()
    $inQuote = $false; $cur = [System.Text.StringBuilder]::new()
    for ($ci = 0; $ci -lt $line.Length; $ci++) {
        $ch = $line[$ci]
        if ($ch -eq '"') { $inQuote = -not $inQuote }
        elseif ($ch -eq ',' -and -not $inQuote) { $fields.Add($cur.ToString()); $cur.Clear() | Out-Null }
        else { $cur.Append($ch) | Out-Null }
    }
    $fields.Add($cur.ToString())
    $hip = (Get-Field $fields 'hip').Trim()
    $id = (Get-Field $fields 'id').Trim()
    if ($hip -and $id) { $hipToId[$hip] = $id }
}
Write-Host "  $($hipToId.Count) HIP mappings"

# --- Parse stars ---
$stars = [System.Collections.Generic.List[object]]::new()
$names = [System.Collections.Generic.List[string]]::new()
$skipped = 0

for ($row = 1; $row -lt $lines.Length; $row++) {
    $line = $lines[$row]
    $fields = [System.Collections.Generic.List[string]]::new()
    $inQuote = $false; $cur = [System.Text.StringBuilder]::new()
    for ($ci = 0; $ci -lt $line.Length; $ci++) {
        $ch = $line[$ci]
        if ($ch -eq '"') { $inQuote = -not $inQuote }
        elseif ($ch -eq ',' -and -not $inQuote) { $fields.Add($cur.ToString()); $cur.Clear() | Out-Null }
        else { $cur.Append($ch) | Out-Null }
    }
    $fields.Add($cur.ToString())

    $mag = Parse-Double (Get-Field $fields 'mag')
    if ($mag -gt $MagCutoff) { $skipped++; continue }

    $id = [int](Parse-Double (Get-Field $fields 'id'))
    $x = [int]([Math]::Round((Parse-Double (Get-Field $fields 'x')) * 1000))
    $y = [int]([Math]::Round((Parse-Double (Get-Field $fields 'y')) * 1000))
    $z = [int]([Math]::Round((Parse-Double (Get-Field $fields 'z')) * 1000))
    $magI = [int]([Math]::Round($mag * 1000))
    $absmag = [int]([Math]::Round((Parse-Double (Get-Field $fields 'absmag')) * 1000))
    $bv = [int]([Math]::Round((Parse-Double (Get-Field $fields 'ci')) * 1000))
    $spect = Spect-To-Byte (Get-Field $fields 'spect')
    $con = (Get-Field $fields 'con').PadRight(3).Substring(0, 3)
    $proper = (Get-Field $fields 'proper').Trim()

    $flags = 0; $nameIdx = 0
    if ($proper.Length -gt 0) {
        $flags = $flags -bor 1; $nameIdx = $names.Count; $names.Add($proper)
    }
    if ((Get-Field $fields 'var').Length -gt 0) { $flags = $flags -bor 2 }
    if ((Parse-Double (Get-Field $fields 'comp')) -gt 1) { $flags = $flags -bor 4 }

    $stars.Add(@{Id=$id;X=$x;Y=$y;Z=$z;Mag=$magI;AbsMag=$absmag;BV=$bv;Spect=$spect;Con=$con;Flags=$flags;NameIdx=$nameIdx})
}
Write-Host "  $($stars.Count) stars, $($names.Count) named ($skipped skipped)"

# --- Deep-sky objects (Messier catalog + notable NGC/IC) ---
# Format: id, name, kind, ra_deg, dec_deg, dist_kly, mag, con, description
# Positions converted to XYZ parsecs*1000 using RA/Dec/Dist
# Kind: 0=globular 1=open 2=planetary-neb 3=diffuse-neb 4=galaxy 5=quasar 6=dark-neb 7=SNR

function ClampI32([double]$v) {
    if ($v -gt 2000000000) { return [int]2000000000 }
    if ($v -lt (-2000000000)) { return [int](-2000000000) }
    return [int]([Math]::Round($v))
}
function RaDecDist-ToXYZ($ra_deg, $dec_deg, $dist_kly) {
    $dist_pc = $dist_kly * 1000 / 3.262
    $ra_rad = $ra_deg * [Math]::PI / 180
    $dec_rad = $dec_deg * [Math]::PI / 180
    $x = ClampI32 ($dist_pc * [Math]::Cos($dec_rad) * [Math]::Cos($ra_rad) * 1000)
    $y = ClampI32 ($dist_pc * [Math]::Cos($dec_rad) * [Math]::Sin($ra_rad) * 1000)
    $z = ClampI32 ($dist_pc * [Math]::Sin($dec_rad) * 1000)
    return @($x, $y, $z)
}

$dsoData = [System.Collections.Generic.List[object[]]]::new()
function Add-Dso { param([int]$id,[string]$name,[int]$kind,[double]$ra,[double]$dec,[double]$dist,[int]$mag,[string]$con,[string]$desc)
    $dsoData.Add(@($id,$name,$kind,$ra,$dec,$dist,$mag,$con,$desc))
}
    # Messier catalog (all 110)
    Add-Dso 200001 "M1 Crab Nebula" 7 83.63 22.01 6.5 8400 "Tau" "Supernova remnant 1054 AD"
    Add-Dso 200002 "M2" 0 323.36 (-0.82) 33.0 6500 "Aqr" "Globular cluster"
    Add-Dso 200003 "M3" 0 205.55 28.38 33.9 6200 "CVn" "Globular cluster"
    Add-Dso 200004 "M4" 0 245.90 (-26.53) 7.2 5600 "Sco" "Nearest globular cluster"
    Add-Dso 200005 "M5" 0 229.64 2.08 24.5 5650 "Ser" "Globular cluster"
    Add-Dso 200006 "M6 Butterfly Cluster" 1 265.07 (-32.22) 1.6 4200 "Sco" "Open cluster"
    Add-Dso 200007 "M7 Ptolemy Cluster" 1 268.47 (-34.79) 0.98 3300 "Sco" "Open cluster"
    Add-Dso 200008 "M8 Lagoon Nebula" 3 270.92 (-24.38) 5.2 6000 "Sgr" "Star-forming region"
    Add-Dso 200009 "M9" 0 259.80 (-18.52) 25.8 7700 "Oph" "Globular cluster"
    Add-Dso 200010 "M10" 0 254.29 (-4.10) 14.3 6600 "Oph" "Globular cluster"
    Add-Dso 200011 "M11 Wild Duck Cluster" 1 282.77 (-6.27) 6.2 6300 "Sct" "Rich open cluster"
    Add-Dso 200012 "M12" 0 251.81 (-1.95) 15.7 6700 "Oph" "Globular cluster"
    Add-Dso 200013 "M13 Hercules Cluster" 0 250.42 36.46 22.2 5800 "Her" "Great Globular Cluster"
    Add-Dso 200014 "M14" 0 264.40 (-3.25) 30.3 7600 "Oph" "Globular cluster"
    Add-Dso 200015 "M15" 0 322.49 12.17 33.6 6200 "Peg" "Dense globular cluster"
    Add-Dso 200016 "M16 Eagle Nebula" 3 274.70 (-13.81) 7.0 6000 "Ser" "Pillars of Creation"
    Add-Dso 200017 "M17 Omega Nebula" 3 275.20 (-16.17) 5.5 6000 "Sgr" "Swan Nebula"
    Add-Dso 200018 "M18" 1 275.24 (-17.13) 4.9 7500 "Sgr" "Open cluster"
    Add-Dso 200019 "M19" 0 255.66 (-26.27) 28.7 6800 "Oph" "Globular cluster"
    Add-Dso 200020 "M20 Trifid Nebula" 3 270.62 (-23.03) 5.2 6300 "Sgr" "Emission+reflection nebula"
    Add-Dso 200021 "M21" 1 271.05 (-22.49) 4.25 6500 "Sgr" "Open cluster"
    Add-Dso 200022 "M22" 0 279.10 (-23.90) 10.6 5100 "Sgr" "Bright globular cluster"
    Add-Dso 200023 "M23" 1 269.27 (-18.99) 2.15 6900 "Sgr" "Open cluster"
    Add-Dso 200024 "M24 Sagittarius Star Cloud" 1 274.53 (-18.52) 10.0 4600 "Sgr" "Milky Way star cloud"
    Add-Dso 200025 "M25" 1 277.88 (-19.11) 2.0 6500 "Sgr" "Open cluster"
    Add-Dso 200026 "M26" 1 281.32 (-9.39) 5.0 8000 "Sct" "Open cluster"
    Add-Dso 200027 "M27 Dumbbell Nebula" 2 299.90 22.72 1.36 7400 "Vul" "Planetary nebula"
    Add-Dso 200028 "M28" 0 276.14 (-24.87) 17.9 6800 "Sgr" "Globular cluster"
    Add-Dso 200029 "M29" 1 305.97 38.51 4.0 7100 "Cyg" "Open cluster"
    Add-Dso 200030 "M30" 0 325.09 (-23.18) 26.1 7200 "Cap" "Globular cluster"
    Add-Dso 200031 "M31 Andromeda Galaxy" 4 10.68 41.27 2540.0 3440 "And" "Nearest large spiral galaxy"
    Add-Dso 200032 "M32" 4 10.67 40.87 2490.0 8100 "And" "Dwarf elliptical companion"
    Add-Dso 200033 "M33 Triangulum Galaxy" 4 23.46 30.66 2730.0 5720 "Tri" "Local Group spiral"
    Add-Dso 200034 "M34" 1 40.52 42.78 1.5 5500 "Per" "Open cluster"
    Add-Dso 200035 "M35" 1 92.25 24.33 2.8 5100 "Gem" "Open cluster"
    Add-Dso 200036 "M36" 1 84.07 34.13 4.1 6300 "Aur" "Open cluster"
    Add-Dso 200037 "M37" 1 88.07 32.55 4.5 6200 "Aur" "Richest Auriga cluster"
    Add-Dso 200038 "M38" 1 82.17 35.85 4.2 7400 "Aur" "Open cluster"
    Add-Dso 200039 "M39" 1 322.32 48.44 0.825 4600 "Cyg" "Sparse open cluster"
    Add-Dso 200040 "M40 Winnecke 4" 1 185.55 58.08 0.51 8400 "UMa" "Double star (not a DSO)"
    Add-Dso 200041 "M41" 1 101.51 (-20.76) 2.3 4500 "CMa" "Open cluster near Sirius"
    Add-Dso 200042 "M42 Orion Nebula" 3 83.82 (-5.39) 1.34 4000 "Ori" "Brightest nebula"
    Add-Dso 200043 "M43" 3 83.89 (-5.27) 1.6 9000 "Ori" "Part of Orion Nebula"
    Add-Dso 200044 "M44 Beehive Cluster" 1 130.03 19.67 0.577 3700 "Cnc" "Praesepe"
    Add-Dso 200045 "M45 Pleiades" 1 56.87 24.12 0.444 1600 "Tau" "Seven Sisters"
    Add-Dso 200046 "M46" 1 115.44 (-14.82) 5.4 6100 "Pup" "Open cluster"
    Add-Dso 200047 "M47" 1 114.15 (-14.49) 1.6 4400 "Pup" "Open cluster"
    Add-Dso 200048 "M48" 1 123.43 (-5.75) 2.5 5800 "Hya" "Open cluster"
    Add-Dso 200049 "M49" 4 187.44 8.00 55900.0 8400 "Vir" "Elliptical galaxy"
    Add-Dso 200050 "M50" 1 105.69 (-8.34) 3.2 5900 "Mon" "Open cluster"
    Add-Dso 200051 "M51 Whirlpool Galaxy" 4 202.47 47.20 23000.0 8400 "CVn" "Face-on spiral"
    Add-Dso 200052 "M52" 1 351.20 61.59 5.0 7300 "Cas" "Rich open cluster"
    Add-Dso 200053 "M53" 0 198.23 18.17 58.0 7600 "Com" "Globular cluster"
    Add-Dso 200054 "M54" 0 283.76 (-30.48) 87.4 7600 "Sgr" "Sagittarius Dwarf core"
    Add-Dso 200055 "M55" 0 294.99 (-30.96) 17.6 6300 "Sgr" "Globular cluster"
    Add-Dso 200056 "M56" 0 289.15 30.18 32.9 8300 "Lyr" "Globular cluster"
    Add-Dso 200057 "M57 Ring Nebula" 2 283.40 33.03 2.57 8800 "Lyr" "Classic planetary nebula"
    Add-Dso 200058 "M58" 4 189.43 11.82 62000.0 9700 "Vir" "Barred spiral galaxy"
    Add-Dso 200059 "M59" 4 190.51 11.65 60000.0 9600 "Vir" "Elliptical galaxy"
    Add-Dso 200060 "M60" 4 190.92 11.55 55000.0 8800 "Vir" "Giant elliptical"
    Add-Dso 200061 "M61" 4 185.48 4.47 52500.0 9700 "Vir" "Face-on spiral"
    Add-Dso 200062 "M62" 0 255.30 (-30.11) 22.5 6500 "Oph" "Globular cluster"
    Add-Dso 200063 "M63 Sunflower Galaxy" 4 198.96 42.03 29500.0 8600 "CVn" "Flocculent spiral"
    Add-Dso 200064 "M64 Black Eye Galaxy" 4 194.18 21.68 24000.0 8520 "Com" "Dark dust band"
    Add-Dso 200065 "M65" 4 169.73 13.09 35000.0 9300 "Leo" "Leo Triplet member"
    Add-Dso 200066 "M66" 4 170.06 12.99 36000.0 8900 "Leo" "Leo Triplet member"
    Add-Dso 200067 "M67" 1 132.85 11.81 2.61 6100 "Cnc" "Ancient open cluster"
    Add-Dso 200068 "M68" 0 189.87 (-26.74) 33.6 7800 "Hya" "Globular cluster"
    Add-Dso 200069 "M69" 0 277.85 (-32.35) 29.7 7600 "Sgr" "Globular cluster"
    Add-Dso 200070 "M70" 0 280.80 (-32.29) 29.4 7900 "Sgr" "Globular cluster"
    Add-Dso 200071 "M71" 0 298.44 18.78 13.0 8200 "Sge" "Loose globular cluster"
    Add-Dso 200072 "M72" 0 313.37 (-12.54) 55.4 9300 "Aqr" "Globular cluster"
    Add-Dso 200073 "M73" 1 314.75 (-12.63) 2.5 9000 "Aqr" "Asterism (4 stars)"
    Add-Dso 200074 "M74" 4 24.17 15.78 35000.0 9400 "Psc" "Face-on spiral"
    Add-Dso 200075 "M75" 0 301.52 (-21.92) 67.5 8500 "Sgr" "Remote globular cluster"
    Add-Dso 200076 "M76 Little Dumbbell" 2 25.58 51.58 3.4 10100 "Per" "Planetary nebula"
    Add-Dso 200077 "M77" 4 40.67 (-0.01) 47000.0 8900 "Cet" "Seyfert galaxy"
    Add-Dso 200078 "M78" 3 86.65 0.08 1.6 8300 "Ori" "Reflection nebula"
    Add-Dso 200079 "M79" 0 81.04 (-24.52) 41.0 7700 "Lep" "Globular cluster"
    Add-Dso 200080 "M80" 0 244.26 (-22.97) 32.6 7300 "Sco" "Dense globular cluster"
    Add-Dso 200081 "M81 Bode's Galaxy" 4 148.89 69.07 11800.0 6940 "UMa" "Grand-design spiral"
    Add-Dso 200082 "M82 Cigar Galaxy" 4 148.97 69.68 11400.0 8410 "UMa" "Starburst galaxy"
    Add-Dso 200083 "M83 Southern Pinwheel" 4 204.25 (-29.87) 14700.0 7600 "Hya" "Barred spiral"
    Add-Dso 200084 "M84" 4 186.27 12.89 60000.0 9100 "Vir" "Lenticular galaxy"
    Add-Dso 200085 "M85" 4 186.35 18.19 60000.0 9100 "Com" "Lenticular galaxy"
    Add-Dso 200086 "M86" 4 186.55 12.95 52000.0 8900 "Vir" "Elliptical/lenticular"
    Add-Dso 200087 "M87 Virgo A" 4 187.71 12.39 53500.0 8600 "Vir" "Giant elliptical, EHT target"
    Add-Dso 200088 "M88" 4 187.99 14.42 47000.0 9600 "Com" "Spiral galaxy"
    Add-Dso 200089 "M89" 4 188.92 12.56 50000.0 9800 "Vir" "Elliptical galaxy"
    Add-Dso 200090 "M90" 4 189.21 13.16 60000.0 9500 "Vir" "Spiral galaxy"
    Add-Dso 200091 "M91" 4 188.86 14.50 63000.0 10200 "Com" "Barred spiral"
    Add-Dso 200092 "M92" 0 259.28 43.14 26.7 6400 "Her" "Globular cluster"
    Add-Dso 200093 "M93" 1 116.13 (-23.86) 3.6 6200 "Pup" "Open cluster"
    Add-Dso 200094 "M94" 4 192.72 41.12 14500.0 8200 "CVn" "Starburst ring galaxy"
    Add-Dso 200095 "M95" 4 160.99 11.70 32600.0 9700 "Leo" "Barred spiral"
    Add-Dso 200096 "M96" 4 161.69 11.82 31000.0 9200 "Leo" "Spiral galaxy"
    Add-Dso 200097 "M97 Owl Nebula" 2 168.70 55.02 2.03 9900 "UMa" "Planetary nebula"
    Add-Dso 200098 "M98" 4 183.45 14.90 44400.0 10100 "Com" "Spiral galaxy"
    Add-Dso 200099 "M99" 4 184.71 14.42 44400.0 9900 "Com" "Spiral galaxy"
    Add-Dso 200100 "M100" 4 185.73 15.82 55000.0 9300 "Com" "Grand-design spiral"
    Add-Dso 200101 "M101 Pinwheel Galaxy" 4 210.80 54.35 20900.0 7860 "UMa" "Face-on spiral"
    Add-Dso 200102 "M102" 4 226.62 55.76 44400.0 9900 "Dra" "NGC 5866 Spindle Galaxy"
    Add-Dso 200103 "M103" 1 23.34 60.66 10.0 7400 "Cas" "Open cluster"
    Add-Dso 200104 "M104 Sombrero Galaxy" 4 189.99 (-11.62) 29300.0 8000 "Vir" "Edge-on with dust lane"
    Add-Dso 200105 "M105" 4 161.96 12.58 32000.0 9300 "Leo" "Elliptical galaxy"
    Add-Dso 200106 "M106" 4 184.74 47.30 23700.0 8400 "CVn" "Seyfert galaxy"
    Add-Dso 200107 "M107" 0 248.13 (-13.05) 20.9 7900 "Oph" "Globular cluster"
    Add-Dso 200108 "M108" 4 167.88 55.67 45000.0 10000 "UMa" "Edge-on spiral"
    Add-Dso 200109 "M109" 4 179.40 53.37 83500.0 9800 "UMa" "Barred spiral"
    Add-Dso 200110 "M110" 4 10.09 41.68 2690.0 8500 "And" "Dwarf elliptical companion"
    # Notable NGC/IC objects
    Add-Dso 300001 "NGC 253 Sculptor Galaxy" 4 11.89 (-25.29) 11400.0 8000 "Scl" "Starburst spiral"
    Add-Dso 300002 "NGC 2070 Tarantula" 3 84.68 (-69.10) 160.0 8200 "Dor" "In Large Magellanic Cloud"
    Add-Dso 300003 "NGC 3372 Carina Nebula" 3 160.99 (-59.87) 8.5 1000 "Car" "Largest bright nebula"
    Add-Dso 300004 "NGC 4565 Needle Galaxy" 4 189.09 25.99 42700.0 9600 "Com" "Perfect edge-on spiral"
    Add-Dso 300005 "NGC 5128 Centaurus A" 4 201.37 (-43.02) 12400.0 6840 "Cen" "Nearest radio galaxy"
    Add-Dso 300006 "NGC 6543 Cat's Eye" 2 269.64 66.63 3.3 8100 "Dra" "Planetary nebula"
    Add-Dso 300007 "NGC 7293 Helix Nebula" 2 337.41 (-20.84) 0.655 7600 "Aqr" "Nearest planetary nebula"
    Add-Dso 300008 "NGC 6960 Veil Nebula" 7 312.76 30.72 2.4 7000 "Cyg" "Supernova remnant"
    Add-Dso 300009 "NGC 2237 Rosette Nebula" 3 98.00 5.00 5.5 9000 "Mon" "Emission nebula"
    Add-Dso 300010 "NGC 7000 North America" 3 315.00 44.00 1.8 4000 "Cyg" "Large emission nebula"
    Add-Dso 300011 "IC 434 Horsehead Nebula" 6 85.24 (-2.46) 1.5 6800 "Ori" "Dark nebula silhouette"
    Add-Dso 300012 "NGC 869/884 Double Cluster" 1 34.75 57.13 7.5 4300 "Per" "Twin open clusters"
    Add-Dso 300013 "LMC" 4 80.89 (-69.76) 163.0 900 "Dor" "Large Magellanic Cloud"
    Add-Dso 300014 "SMC" 4 13.19 (-72.83) 200.0 2700 "Tuc" "Small Magellanic Cloud"
    Add-Dso 300015 "Sgr A*" 4 266.42 (-29.01) 26.7 0 "Sgr" "Milky Way central black hole"

$dsos = [System.Collections.Generic.List[object]]::new()
foreach ($d in $dsoData) {
    $xyz = RaDecDist-ToXYZ $d[3] $d[4] $d[5]
    $dsos.Add(@{Id=$d[0];Name=$d[1];Kind=$d[2];X=$xyz[0];Y=$xyz[1];Z=$xyz[2];Mag=[int]$d[6];Con=$d[7];Desc=$d[8]})
}
Write-Host "  $($dsos.Count) deep-sky objects"

# --- Constellation stick figures ---
# Each constellation: abbreviation, name, list of [HIP_from, HIP_to] pairs
# Using HIP IDs which map to HYG IDs via $hipToId

$constellationData = [System.Collections.Generic.List[hashtable]]::new()
function Add-Con($abbr, $name, [int[]]$hips) {
    $pairs = [System.Collections.Generic.List[int[]]]::new()
    for ($pi = 0; $pi -lt $hips.Length; $pi += 2) { $pairs.Add(@($hips[$pi], $hips[$pi+1])) }
    $constellationData.Add(@{Abbr=$abbr;Name=$name;Pairs=$pairs})
}

Add-Con "Ori" "Orion" @(26727,27989,27989,26311,26311,25336,25336,25930,25930,26727,27989,28691,26311,26207,24436,25336,24436,22449,22449,22509,22509,22845,22845,23123,28691,29426,29426,29038)
Add-Con "UMa" "Ursa Major" @(54061,53910,53910,58001,58001,59774,59774,62956,62956,65378,65378,67301,67301,54061,54061,48319,48319,46733,46733,46853,59774,59196,59196,54539,54539,53910)
Add-Con "CMa" "Canis Major" @(32349,31592,32349,33579,33579,34444,34444,35904,32349,30324,30324,33856,33856,33579,30324,31125)
Add-Con "Gem" "Gemini" @(36850,37826,37826,36962,36962,35550,35550,34693,34693,32362,36850,34088,34088,31681,31681,30883,30883,29655)
Add-Con "Tau" "Taurus" @(21421,20889,20889,20205,20205,18724,21421,20455,20455,17847,17847,16083,21421,26451,21421,25428,25428,25606)
Add-Con "Sco" "Scorpius" @(80763,78820,78820,78265,78265,77070,80763,82396,82396,82514,82514,83081,83081,84143,84143,86228,86228,87073,78820,78104,78104,78159)
Add-Con "Leo" "Leo" @(49669,50583,50583,54872,54872,57632,57632,49669,49669,48455,48455,47908,47908,46750,50583,50335)
Add-Con "Vir" "Virgo" @(65474,63608,63608,61941,61941,60129,65474,66249,66249,69427,69427,72220,65474,64238,64238,63090)
Add-Con "Cyg" "Cygnus" @(102098,100453,100453,97165,97165,94779,100453,95947,100453,104732,97165,98110)
Add-Con "Lyr" "Lyra" @(91262,91971,91971,92420,92420,91926,91926,91971,91262,91919,91919,92791,92791,92420)
Add-Con "Aql" "Aquila" @(97649,97278,97649,98036,97649,99473,99473,99655,97649,93747,93747,93244)
Add-Con "Cas" "Cassiopeia" @(3179,4427,4427,6686,6686,8886,8886,11407)
Add-Con "Cep" "Cepheus" @(105199,106032,106032,109492,109492,112724,112724,116727,116727,105199)
Add-Con "Per" "Perseus" @(14576,14354,14354,13268,13268,14328,14328,15863,15863,17448,17448,18246,14354,13531)
Add-Con "And" "Andromeda" @(677,5447,5447,9640,9640,14328,5447,4436,4436,3881)
Add-Con "Peg" "Pegasus" @(677,113963,113963,112158,112158,109410,109410,677,112158,107315,107315,109410,113963,113881)
Add-Con "Ari" "Aries" @(9884,8903,8903,8832)
Add-Con "Cnc" "Cancer" @(40526,42911,42911,44066,42911,43103)
Add-Con "Lib" "Libra" @(72622,74785,74785,76333,76333,72622,72622,73714)
Add-Con "Sgr" "Sagittarius" @(89931,90185,90185,92855,92855,93506,93506,89931,89931,88635,88635,89642,89642,90185,93506,95168,95168,95294,92855,93085)
Add-Con "Cap" "Capricornus" @(100064,100345,100345,104139,104139,105881,105881,107556,107556,100064)
Add-Con "Aqr" "Aquarius" @(109074,110395,110395,111497,111497,112961,109074,106278,106278,102618,110395,110672)
Add-Con "Psc" "Pisces" @(7097,8198,8198,9487,9487,5742,5742,3786,3786,4889,4889,5586,5586,7097)
Add-Con "Cru" "Crux" @(60718,62434,59747,61084)
Add-Con "Cen" "Centaurus" @(71683,68702,68702,67472,67472,66657,66657,61932,71683,68933,68933,56561)
Add-Con "Car" "Carina" @(30438,45238,45238,50099,50099,52419,52419,45556,45556,41037,41037,30438)
Add-Con "Vel" "Vela" @(44816,42913,42913,39953,39953,45941,45941,44816,42913,50191)
Add-Con "Pup" "Puppis" @(35264,36917,36917,38170,38170,39757,39757,38070,35264,31685)
Add-Con "CMi" "Canis Minor" @(37279,36188)
Add-Con "CrB" "Corona Borealis" @(76267,76952,76952,77512,77512,78159,78159,77233,77233,76267)
Add-Con "Boo" "Bootes" @(69673,72105,72105,74666,74666,73555,73555,69673,69673,67927,67927,67459)
Add-Con "Her" "Hercules" @(84345,85693,85693,87933,87933,86414,86414,84345,84345,83207,83207,80816,80816,81693,85693,84379,84379,81126)
Add-Con "Oph" "Ophiuchus" @(86032,84012,84012,80883,80883,80473,80473,79593,79593,80883,80473,80763)
Add-Con "Ser" "Serpens" @(77070,76852,76852,77622,77622,78072,78072,77233)
Add-Con "Dra" "Draco" @(87585,85670,85670,87833,87833,89908,89908,94376,94376,97433,97433,100029,100029,94648,94648,89937,89937,83895,83895,80331,80331,78527,78527,75458,75458,68756)
Add-Con "UMi" "Ursa Minor" @(11767,85822,85822,82080,82080,77055,77055,75097,75097,72607,72607,79822,79822,85822)

# Resolve HIP -> HYG ID for constellation lines
$conResolved = [System.Collections.Generic.List[object]]::new()
$totalLines = 0
foreach ($c in $constellationData) {
    $resolved = [System.Collections.Generic.List[object]]::new()
    foreach ($pair in $c.Pairs) {
        $fromHip = "$($pair[0])"; $toHip = "$($pair[1])"
        $fromId = $hipToId[$fromHip]; $toId = $hipToId[$toHip]
        if ($fromId -and $toId) { $resolved.Add(@([int]$fromId, [int]$toId)) }
    }
    if ($resolved.Count -gt 0) {
        $conResolved.Add(@{Abbr=$c.Abbr;Name=$c.Name;Lines=$resolved})
        $totalLines += $resolved.Count
    }
}
Write-Host "  $($conResolved.Count) constellations, $totalLines line segments"

# === Build binary ===
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

# Header (64 bytes)
$bw.Write([byte[]]@(0x53, 0x54, 0x41, 0x52))  # "STAR"
$bw.Write([int]2)                                # version
$bw.Write([int]$stars.Count)                     # star_count
$bw.Write([int]$names.Count)                     # named_count
$bw.Write([int]0)                                # name_table_offset (patch later)
$bw.Write([int]$dsos.Count)                      # dso_count
$bw.Write([int]0)                                # dso_offset (patch later)
$bw.Write([int]$conResolved.Count)               # con_count
$bw.Write([int]0)                                # con_offset (patch later)
$bw.Write([int]$totalLines)                      # con_line_count
$bw.Write([byte[]]::new(24))                     # reserved to 64

# Star records (32 bytes each)
foreach ($s in $stars) {
    $bw.Write([int]$s.Id)
    $bw.Write([int]$s.X); $bw.Write([int]$s.Y); $bw.Write([int]$s.Z)
    $bw.Write([int16]$s.Mag); $bw.Write([int16]$s.AbsMag); $bw.Write([int16]$s.BV)
    $bw.Write([byte]$s.Spect)
    $cb = [System.Text.Encoding]::ASCII.GetBytes($s.Con)
    $bw.Write($cb, 0, [Math]::Min(3, $cb.Length))
    if ($cb.Length -lt 3) { $bw.Write([byte[]]::new(3 - $cb.Length)) }
    $bw.Write([byte]$s.Flags)
    if ($s.Flags -band 1) { $bw.Write([int]$s.NameIdx) } else { $bw.Write([int]0) }
    $bw.Write([byte]0)
}

# Patch name_table_offset
$nameOff = [int]$ms.Position; $ms.Position = 16; $bw.Write([int]$nameOff); $ms.Position = $ms.Length
foreach ($n in $names) { $nb = [System.Text.Encoding]::UTF8.GetBytes($n); $bw.Write([byte]$nb.Length); $bw.Write($nb) }

# DSO section
$dsoOff = [int]$ms.Position; $ms.Position = 24; $bw.Write([int]$dsoOff); $ms.Position = $ms.Length
foreach ($d in $dsos) {
    $bw.Write([int]$d.Id)
    $bw.Write([int]$d.X); $bw.Write([int]$d.Y); $bw.Write([int]$d.Z)
    $bw.Write([int16]$d.Mag)
    $bw.Write([byte]$d.Kind)
    $dcb = [System.Text.Encoding]::ASCII.GetBytes($d.Con.PadRight(3).Substring(0,3))
    $bw.Write($dcb, 0, 3)
    $nameB = [System.Text.Encoding]::UTF8.GetBytes($d.Name)
    $bw.Write([byte][Math]::Min($nameB.Length, 40))
    $nameSlice = if ($nameB.Length -gt 40) { $nameB[0..39] } else { $nameB }
    $bw.Write([byte[]]$nameSlice)
    if ($nameSlice.Length -lt 40) { $bw.Write([byte[]]::new(40 - $nameSlice.Length)) }
    $descB = [System.Text.Encoding]::UTF8.GetBytes($d.Desc)
    $bw.Write([byte][Math]::Min($descB.Length, 16))
    $descSlice = if ($descB.Length -gt 16) { $descB[0..15] } else { $descB }
    $bw.Write([byte[]]$descSlice)
    if ($descSlice.Length -lt 16) { $bw.Write([byte[]]::new(16 - $descSlice.Length)) }
}

# Constellation section
$conOff = [int]$ms.Position; $ms.Position = 28; $bw.Write([int]$conOff); $ms.Position = $ms.Length
foreach ($c in $conResolved) {
    $abbrB = [System.Text.Encoding]::ASCII.GetBytes($c.Abbr.PadRight(3).Substring(0,3))
    $bw.Write([byte]$abbrB.Length); $bw.Write($abbrB)
    $cnameB = [System.Text.Encoding]::UTF8.GetBytes($c.Name)
    $nameLen = [Math]::Min($cnameB.Length, 20)
    $bw.Write([byte]$nameLen)
    $bw.Write($cnameB, 0, $nameLen)
    if ($nameLen -lt 20) { $bw.Write([byte[]]::new(20 - $nameLen)) }
    $bw.Write([uint16]$c.Lines.Count)
    foreach ($line in $c.Lines) { $bw.Write([int]$line[0]); $bw.Write([int]$line[1]) }
}

$bw.Flush(); $data = $ms.ToArray(); $bw.Close()
[System.IO.File]::WriteAllBytes($OutPath, $data)
$sizeMB = [Math]::Round($data.Length / 1048576.0, 2)
Write-Host "[import-hyg] Wrote $OutPath ($($data.Length) bytes, $sizeMB MB)" -ForegroundColor Green
Write-Host "  $($stars.Count) stars, $($names.Count) named" -ForegroundColor Green
Write-Host "  $($dsos.Count) deep-sky objects" -ForegroundColor Green
Write-Host "  $($conResolved.Count) constellations, $totalLines line segments" -ForegroundColor Green
Write-Host "  Credit: HYG v4.2 by David Nash, CC BY-SA 4.0" -ForegroundColor DarkGray
