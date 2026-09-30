$j = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $j.tool_input.file_path
if (-not $path) { $path = $j.tool_input.notebook_path }
if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { exit 0 }
$item = Get-Item -LiteralPath $path
if (-not $item.IsReadOnly) { exit 0 }

Set-Location -LiteralPath $item.DirectoryName
$have = p4 -ztag -F '%haveRev%' fstat $item.FullName 2>$null
if (-not $have) { exit 0 }

$client = p4 -ztag -F '%clientName%' info 2>$null
$pending = @(p4 -ztag -F '%change%' changes -s pending -c $client 2>$null | Where-Object { $_ })
if ($pending.Count -eq 1) {
    $out = p4 edit -c $pending[0] $item.FullName 2>&1
    $msg = "p4 edit hook: opened $($item.Name) in CL $($pending[0]), the only pending CL on $client. $out"
} else {
    $out = p4 edit $item.FullName 2>&1
    $msg = "p4 edit hook: opened $($item.Name) in the DEFAULT changelist because $client has $($pending.Count) pending numbered CLs. Move it with 'p4 reopen -c <CL> <file>' or create the CL by PerforceProcess.md 4.7 (P-DEFAULT). $out"
}
@{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; additionalContext = $msg } } | ConvertTo-Json -Compress -Depth 5
