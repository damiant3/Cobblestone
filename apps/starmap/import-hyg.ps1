# import-hyg.ps1 -- Import HYG v4.2 CSV + deep-sky + constellations
#
# Reads hyg_v42.csv, writes starmap.dat with three sections:
#   1. Stars (from HYG CSV)
#   2. Deep-sky objects (Messier + notable NGC, hardcoded)
#   3. Constellation stick figures: Stellarium's "modern" sky culture, 88 figures
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
# Data: HYG v4.2 by David Nash, CC BY-SA 4.0. Constellation lines: Stellarium's
#       skycultures/modern/index.json (HIP polylines split into pairs), CC BY-SA 4.0.
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

Add-Con "Aql" "Aquila" @(98036,97649,97649,97278,97649,95501,95501,97804,99473,97804,95501,93747,93747,93244,95501,93805)
Add-Con "And" "Andromeda" @(677,3092,3092,5447,9640,5447,5447,4436,4436,3881)
Add-Con "Scl" "Sculptor" @(116231,4577,4577,115102,115102,116231)
Add-Con "Ara" "Ara" @(88714,85792,85792,83081,83081,82363,82363,85727,85727,85267,85267,85258,85258,88714)
Add-Con "Lib" "Libra" @(77853,76333,76333,74785,74785,72622,72622,73714,73714,76333)
Add-Con "Cet" "Cetus" @(10324,11484,8102,3419,3419,1562,3419,5364,5364,6537,6537,8645,8645,11345,11345,12390,12390,12770,12770,11783,11783,8102,10826,12390,10826,12387,12387,12706,12706,14135,14135,13954,13954,12828,12828,11484,11484,12093,12093,12706)
Add-Con "Ari" "Aries" @(13209,9884,9884,8903,8903,8832)
Add-Con "Sct" "Scutum" @(92175,92202,92202,92814,92814,90595,90595,91117,91117,92175)
Add-Con "Pyx" "Pyxis" @(42515,42828,42828,43409)
Add-Con "Boo" "Bootes" @(71795,69673,69673,72105,72105,74666,74666,73555,73555,71075,71075,71053,71053,69673,69673,67927,67927,67459)
Add-Con "Cae" "Caelum" @(21060,21770,21770,21861)
Add-Con "Cha" "Chamaeleon" @(40702,51839,51839,60000)
Add-Con "Cnc" "Cancer" @(43103,42806,42806,40843,42806,42911,42911,40526,42911,44066)
Add-Con "Cap" "Capricornus" @(100064,100345,100345,104139,104139,105515,105515,106985,106985,107556,105515,105881,105881,104139,100345,102485,104139,102978)
Add-Con "Car" "Carina" @(45238,50099,50099,52419,52419,52468,52468,54463,54463,53253,53253,51232,51232,50371,50371,45556,42568,41037,41037,30438,45080,45556,45080,42568,30438,31685,41037,39429)
Add-Con "Cas" "Cassiopeia" @(8886,6686,6686,4427,4427,3179,3179,746)
Add-Con "Cen" "Centaurus" @(71683,68702,68702,66657,66657,68002,68002,68282,68282,67472,67472,67464,67464,65936,65936,65109,67464,68933,67472,71352,71352,73334,68002,61932,61932,60823,60823,59196,59196,56480,56480,56561)
Add-Con "Cep" "Cepheus" @(109492,112724,112724,106032,106032,105199,105199,109492,112724,116727,116727,106032)
Add-Con "Com" "Coma Berenices" @(64241,64394,64394,60742)
Add-Con "CVn" "Canes Venatici" @(61317,63125)
Add-Con "Aur" "Auriga" @(28380,28360,28360,24608,24608,23453,23453,23015,25428,23015,25428,28380)
Add-Con "Col" "Columba" @(30277,29807,29807,28199,28199,27628,27628,28328,27628,26634,26634,25859)
Add-Con "Cir" "Circinus" @(71908,75323,71908,74824)
Add-Con "Crt" "Crater" @(53740,54682,54682,55705,55705,55282,55282,53740,55282,55687,55687,56633,56633,58188,58188,57283,57283,55705)
Add-Con "CrA" "Corona Australis" @(91875,92989,92989,93174,93174,93825,93825,94114,94114,94160,94160,94005,94005,93542,93542,92953,91875,90887)
Add-Con "CrB" "Corona Borealis" @(76127,75695,75695,76267,76267,76952,76952,77512,77512,78159,78159,78493)
Add-Con "Crv" "Corvus" @(61174,60965,60965,59803,59803,59316,59316,59199,59316,61359,61359,60965)
Add-Con "Cru" "Crux" @(61084,60718,62434,59747)
Add-Con "Cyg" "Cygnus" @(94779,95853,95853,97165,97165,100453,100453,102098,100453,102488,102488,104732,104732,107310,100453,98110,98110,95947)
Add-Con "Del" "Delphinus" @(101421,101769,101769,101958,101958,102532,102532,102281,102281,101769)
Add-Con "Dor" "Dorado" @(27100,27890,27890,26069,26069,27100,26069,21281,21281,19893)
Add-Con "Dra" "Draco" @(87585,87833,87833,85670,85670,85829,85829,87585,87585,94376,94376,97433,97433,94648,94648,89937,89937,83895,83895,80331,80331,78527,78527,75458,75458,68756,68756,61281,61281,56211)
Add-Con "Nor" "Norma" @(79509,80000,80000,80582,80582,78639,78639,80000,78639,79509)
Add-Con "Eri" "Eridanus" @(7588,9007,9007,10602,10602,11407,11407,12413,12413,12486,12486,13847,13847,15510,15510,17797,17797,17874,17874,20042,20042,20535,20535,21393,21393,17651,17651,16611,16611,15474,15474,14146,14146,12843,12843,13701,13701,15197,15197,16537,16537,17378,17378,21444,21444,22109,22109,22701,22701,23875,23875,23972,23972,21594)
Add-Con "Sge" "Sagitta" @(96837,97365,97365,96757,97365,98337,98337,98920)
Add-Con "For" "Fornax" @(13147,14879)
Add-Con "Gem" "Gemini" @(31681,34088,34088,35550,35550,35350,35350,32362,35550,36962,36962,37740,36962,37826,36962,36046,36046,34693,34693,36850,34693,33018,34693,32246,32246,30883,32246,30343,30343,29655,29655,28734)
Add-Con "Cam" "Camelopardalis" @(16228,18505,18505,22783,16228,17959,17959,22783,17959,25110)
Add-Con "CMa" "Canis Major" @(33160,34045,34045,33347,33347,32349,32349,33977,33977,34444,34444,35037,35037,35904,33579,33856,33856,34444,33856,33165,33165,31592,31592,31416,31592,30324,31592,32349,33579,32759,30122,33579,33347,33160)
Add-Con "UMa" "Ursa Major" @(67301,65378,65378,62956,62956,59774,59774,54061,54061,53910,53910,58001,58001,59774,58001,57399,57399,54539,54539,50372,54539,50801,53910,48402,48402,46853,46853,44471,46853,44127,48402,48319,48319,41704,41704,46733,46733,54061)
Add-Con "Gru" "Grus" @(114131,110997,110997,109268,109268,112122,112122,114421,114421,114131,112122,113638,112122,112623,109268,109111,109111,108085)
Add-Con "Her" "Hercules" @(86414,87808,87808,85112,85112,84606,84606,84380,84380,81833,81833,81126,81126,79992,79992,77760,81833,81693,81693,80816,80816,80170,81693,83207,83207,85693,85693,84379,86974,87933,87933,88794,83207,84380,86974,85693)
Add-Con "Hor" "Horologium" @(19747,12484,12484,14240)
Add-Con "Hya" "Hydra" @(42799,42402,42402,42313,42313,43109,43109,43234,43234,42799,43234,43813,43813,45336,45336,46776,46776,46509,46509,46390,46390,45751,45751,47452,47452,48356,48356,49841,49841,51069,51069,52943,52943,54204,54204,56343,56343,57936,57936,64166,64166,64962,64962,68895,68895,69415,69415,70306,70306,72571)
Add-Con "Hyi" "Hydrus" @(2021,17678,17678,12394,12394,11001,11001,9236)
Add-Con "Ind" "Indus" @(105319,101772,101772,103227,103227,105319)
Add-Con "Lac" "Lacerta" @(109937,111104,111104,111022,111022,110609,110609,110538,110538,111169,111169,111022)
Add-Con "Mon" "Monoceros" @(29651,34769,30867,34769,34769,32533,32533,30419,30419,31216,31216,31978,32533,31978,31978,30665,34769,39211,39211,39863,39211,37447)
Add-Con "Lep" "Lepus" @(28910,28103,28103,27288,27288,25985,25985,24305,25985,27654,27654,27072,27072,25606,25606,23685,25985,25606,24305,24845,24305,24327,23685,24305,24327,24244,24845,24873)
Add-Con "Leo" "Leo" @(57632,54879,54879,49669,49669,49583,49583,50583,50583,54872,54872,57632,50583,50335,50335,48455,48455,47908,54872,54879)
Add-Con "Lup" "Lupus" @(77634,78970,78970,78384,78384,77634,78384,76297,76297,75141,75141,75177,75141,73273,76297,76552,76552,74395,74395,71860,74395,71536,71860,70576,71860,73273)
Add-Con "Lyn" "Lynx" @(45860,45688,45688,44700,44700,44248,44248,41075,41075,36145,36145,33449,33449,30060)
Add-Con "Lyr" "Lyra" @(91262,91971,91971,92420,92420,93194,93194,92791,92791,91971)
Add-Con "Ant" "Antlia" @(51172,48926)
Add-Con "Mic" "Microscopium" @(105140,103738,103738,102831)
Add-Con "Mus" "Musca" @(62322,57363,57363,61199,61199,61585,61585,62322)
Add-Con "Oct" "Octans" @(107089,112405,112405,70638,70638,107089)
Add-Con "Aps" "Apus" @(72370,81065,81065,81852)
Add-Con "Oph" "Ophiuchus" @(86032,86742,84012,86742,86032,83000,83000,79882,79882,81377,81377,84012,84012,85755)
Add-Con "Ori" "Orion" @(26727,26311,26311,25930,29434,29426,29434,28716,28716,27913,29426,29038,29038,27913,29426,28614,28614,27989,27989,26727,26727,27366,27366,24436,24436,25930,25930,25336,25336,26207,26207,27989,25336,22449,22449,22549,22549,22730,22730,22797,22797,23123,22449,22509,22509,22845,29038,28614)
Add-Con "Pav" "Pavo" @(100751,105858,105858,102395,102395,99240,99240,100751,99240,98495,98495,91792,91792,93015,93015,99240,93015,92609,92609,90098,90098,88866,88866,92609,88866,86929)
Add-Con "Peg" "Pegasus" @(1067,113963,113881,112158,112158,109352,113881,112748,112748,112440,112440,109176,109176,107354,113963,112447,112447,112029,112029,109427,109427,107315,677,113881,677,1067,113881,113963)
Add-Con "Pic" "Pictor" @(32607,27530,27530,27321)
Add-Con "Per" "Perseus" @(17448,18246,18246,18614,18614,18532,18532,17358,17358,15863,15863,14328,14328,13268,15863,14576,14576,14354,14354,13254)
Add-Con "Equ" "Equuleus" @(104521,104858,104858,105570,105570,104987,104987,104521)
Add-Con "CMi" "Canis Minor" @(37279,36188)
Add-Con "LMi" "Leo Minor" @(53229,51233,51233,49593,49593,46952,49593,53229)
Add-Con "Vul" "Vulpecula" @(95771,98543)
Add-Con "UMi" "Ursa Minor" @(11767,85822,85822,82080,82080,77055,77055,79822,79822,75097,75097,72607,72607,77055)
Add-Con "Phe" "Phoenix" @(5348,5165,5165,2072,2072,5348,5165,7083,7083,8837,8837,5165,5165,6867,6867,2072,2072,2081,2081,765,765,2072)
Add-Con "Psc" "Pisces" @(4889,5742,4889,6193,6193,5742,5742,7097,7097,8198,8198,9487,9487,8833,8833,7884,7884,7007,7007,4906,4906,3760,3760,1645,1645,118268,118268,116771,116771,117245,117245,116928,116928,115738,115738,114971,114971,115227,115227,115830,115830,116771)
Add-Con "PsA" "Piscis Austrinus" @(113368,111954,111954,108661,108661,107608,107608,109422,109422,111188,111188,113246)
Add-Con "Vol" "Volans" @(37504,34481,34481,39794,39794,37504,39794,35228,39794,41312,41312,44382,44382,39794)
Add-Con "Pup" "Puppis" @(39757,38146,38146,35264,35264,31685,31685,32768,32768,36377,36377,39429,39429,39757)
Add-Con "Ret" "Reticulum" @(19780,19921,19921,18597,18597,17440,17440,19780)
Add-Con "Sgr" "Sagittarius" @(89931,90496,89642,90185,90185,88635,88635,87072,88635,89931,89931,90185,90185,93506,93506,92041,92041,89931,92041,90496,90496,89341,93506,93864,93864,92855,92855,92041,92855,93085,93085,93683,93683,94820,94820,95168,93864,96406,96406,98688,98688,98412,98412,98032,98032,95347,98032,95294)
Add-Con "Sco" "Scorpius" @(85927,86670,86670,87073,87073,86228,86228,84143,84143,82671,82671,82514,82514,82396,82396,81266,81266,80763,80763,78401,80763,78265,80763,78820)
Add-Con "Ser" "Serpens" @(79593,77516,77516,77622,77622,77070,77070,76276,76276,77233,77233,78072,78072,77450,77450,77233,92946,90441,90441,89962,89962,88670,88670,88048,88048,86565,86565,86263,86263,84880)
Add-Con "Sex" "Sextans" @(51437,49641)
Add-Con "Men" "Mensa" @(25918,21949)
Add-Con "Tau" "Taurus" @(25428,21881,21881,20889,21421,26451,20205,20455,20205,18724,18724,15900,21421,20889,21421,20894,20894,20205,20889,20648,20648,20455,20455,17847)
Add-Con "Tel" "Telescopium" @(90568,90422)
Add-Con "Tuc" "Tucana" @(110130,114996,114996,1599,114996,2484)
Add-Con "Tri" "Triangulum" @(10670,10064,10064,8796,8796,10670)
Add-Con "TrA" "Triangulum Australe" @(82273,74946,74946,77952,77952,82273)
Add-Con "Aqr" "Aquarius" @(106278,109074,109074,110395,110395,110960,110960,111497,111497,112961,112961,114855,114855,115438,109074,110003,110003,109139,110003,111123,111123,112716,112716,113136,113136,114341,102618,106278)
Add-Con "Vir" "Virgo" @(57380,60129,60129,61941,61941,65474,65474,69427,69427,69701,69701,71957,65474,66249,66249,68520,68520,72220,66249,63090,63090,63608,63090,61941)
Add-Con "Vel" "Vela" @(39953,42536,42536,42913,42913,45941,45941,48774,48774,52727,52727,51986,51986,50191,50191,46651,46651,44816,44816,39953)

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
# 32, not 28: 28 is con_count's slot, and patching the offset there destroyed
# the count and left con_offset at 0, so the constellation section was
# unreachable by the documented layout in every file this script has written.
$conOff = [int]$ms.Position; $ms.Position = 32; $bw.Write([int]$conOff); $ms.Position = $ms.Length
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
