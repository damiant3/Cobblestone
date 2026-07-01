# Convert a TTF file to a Codex List Integer literal for embedding in test files.
param(
    [Parameter(Mandatory=$true)][string]$Ttf,
    [string]$VarName = "test-font-bytes"
)
$bytes = [System.IO.File]::ReadAllBytes($Ttf)
$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append("  $VarName : List Integer`n")
[void]$sb.Append("  $VarName = [")
for ($i = 0; $i -lt $bytes.Length; $i++) {
    if ($i -gt 0) { [void]$sb.Append(", ") }
    if ($i % 20 -eq 0 -and $i -gt 0) { [void]$sb.Append("`n    ") }
    [void]$sb.Append($bytes[$i])
}
[void]$sb.Append("]`n")
$sb.ToString()
