function Add-ModBuilderWizard([string]$Html) {
    $Html = [regex]::Replace($Html, '(?s)<!--MODBUILDER-WIZARD-->.*?<!--/MODBUILDER-WIZARD-->\s*', '')
    $script = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'forge-wizard.js'))
    return $Html.Replace('</body>', "<!--MODBUILDER-WIZARD--><script>$script</script><!--/MODBUILDER-WIZARD-->`n</body>")
}
