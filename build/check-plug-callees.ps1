# check-plug-callees.ps1 -- find a call in a text plug's EMITTED source to a
# name that nothing defines (plugs-backlog 2.38).
#
# A text plug's prelude can call a helper it never emits, and nothing notices
# until a program reaches that call and dies. typescript shipped three of
# them; each surfaced one per run of plug-oracle-test.ps1, reading as a plug
# that "does not refuse" while the program was dying before the row under
# test. An undefined callee is decidable from the text: a name called, bound
# nowhere in the file, and absent from the language runtime's own globals.
# The runtime answers that last part itself, so there is no allowlist to rot.
#
# Python is read with its own ast module and exactly. JavaScript and
# TypeScript are scanned: comments and string text are blanked (template
# `${...}` code is kept), bindings are the declaration, parameter and catch
# forms, calls are `name(` not preceded by a dot, and node answers
# `typeof globalThis[name]`. A binding form the scanner misses reports a
# false callee, which fails loudly; it cannot hide a real one.
#
# The emitted files come from `build/plug-oracle-test.ps1 -KeepArtifacts`,
# which leaves `subject.<ext>` in build-output/plug-oracle. One row per
# language below; a plug joins by adding its row.
#
#   build/check-plug-callees.ps1                  # every row with a file present
#   build/check-plug-callees.ps1 -Dir <dir>
#   build/check-plug-callees.ps1 -SelfTest        # each analyzer must catch a planted callee
[CmdletBinding()]
param(
    [string]$Dir = '',
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Dir) { $Dir = Join-Path $Repo 'build-output\plug-oracle' }

$Rows = @(
    @{ Plug = 'python';     Ext = 'py'; Analyzer = 'python' }
    @{ Plug = 'javascript'; Ext = 'js'; Analyzer = 'node' }
    @{ Plug = 'typescript'; Ext = 'ts'; Analyzer = 'node' }
    @{ Plug = 'zig';        Ext = 'zig'; Analyzer = 'zig' }
    @{ Plug = 'csharp';     Ext = 'cs'; Analyzer = 'dotnet' }
)

# Zig analyses lazily: a helper nothing calls is never semantically checked,
# so `zig run` passes a prelude that calls a missing name from dead code.
# `zig ast-check` resolves every identifier in every function and names each
# undeclared one. Roslyn checks the whole file, so for C# the build itself is
# the analyzer and CS0103 names the missing callee; the project matches the
# one plug-oracle-test.ps1 builds the subject with.
$CsProj = @(
    '<Project Sdk="Microsoft.NET.Sdk">'
    '  <PropertyGroup>'
    '    <OutputType>Exe</OutputType>'
    '    <TargetFramework>net9.0</TargetFramework>'
    '    <Nullable>disable</Nullable>'
    '    <ImplicitUsings>disable</ImplicitUsings>'
    '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
    '    <AssemblyName>subject</AssemblyName>'
    '  </PropertyGroup>'
    '  <ItemGroup>'
    '    <Compile Include="subject.cs" />'
    '  </ItemGroup>'
    '</Project>'
)

function Get-CallCount([string]$file) {
    $text = [System.IO.File]::ReadAllText($file)
    return @([regex]::Matches($text, '(?<![\w.])([A-Za-z_]\w*)\s*\(') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique).Count
}

$PyAnalyzer = @'
import ast, builtins, json, sys
tree = ast.parse(open(sys.argv[1], encoding='utf-8').read())
bound, called = set(), set()
for n in ast.walk(tree):
    if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
        bound.add(n.name)
    elif isinstance(n, ast.arg):
        bound.add(n.arg)
    elif isinstance(n, ast.Name) and isinstance(n.ctx, (ast.Store, ast.Del)):
        bound.add(n.id)
    elif isinstance(n, (ast.Import, ast.ImportFrom)):
        for a in n.names:
            bound.add((a.asname or a.name).split('.')[0])
    elif isinstance(n, ast.ExceptHandler) and n.name:
        bound.add(n.name)
    elif isinstance(n, getattr(ast, 'MatchAs', ())) and n.name:
        bound.add(n.name)
    elif isinstance(n, getattr(ast, 'MatchStar', ())) and n.name:
        bound.add(n.name)
    if isinstance(n, ast.Call) and isinstance(n.func, ast.Name):
        called.add(n.func.id)
missing = sorted(c for c in called if c not in bound and not hasattr(builtins, c))
print(json.dumps({'called': len(called), 'missing': missing}))
'@

$JsAnalyzer = @'
const fs = require('fs');
const src = fs.readFileSync(process.argv[2], 'utf8');
// Blank comments and string text, keep template ${...} code, keep offsets.
let out = '', i = 0, depth = [];
function blank(s) { return s.replace(/[^\n]/g, ' '); }
while (i < src.length) {
  const c = src[i], d = src[i + 1];
  if (c === '/' && d === '/') { const j = src.indexOf('\n', i); const e = j < 0 ? src.length : j; out += blank(src.slice(i, e)); i = e; continue; }
  if (c === '/' && d === '*') { const j = src.indexOf('*/', i + 2); const e = j < 0 ? src.length : j + 2; out += blank(src.slice(i, e)); i = e; continue; }
  if (c === '"' || c === "'") { let j = i + 1; while (j < src.length && src[j] !== c) { if (src[j] === '\\') j++; j++; } out += c + blank(src.slice(i + 1, j)) + c; i = j + 1; continue; }
  if (c === '`') {
    let j = i + 1; out += '`';
    while (j < src.length && src[j] !== '`') {
      if (src[j] === '\\') { out += '  '; j += 2; continue; }
      if (src[j] === '$' && src[j + 1] === '{') {
        let k = j + 2, lvl = 1;
        while (k < src.length && lvl > 0) { if (src[k] === '{') lvl++; else if (src[k] === '}') lvl--; k++; }
        out += ' (' + src.slice(j + 2, k - 1) + ') '; j = k; continue;
      }
      out += src[j] === '\n' ? '\n' : ' '; j++;
    }
    out += '`'; i = j + 1; continue;
  }
  out += c; i++;
}
const id = '[A-Za-z_$][\\w$]*';
const bound = new Set();
const add = (s) => { for (const m of s.matchAll(new RegExp('(?:^|[,(\\[{]|\\.\\.\\.)\\s*(' + id + ')', 'g'))) bound.add(m[1]); };
for (const m of out.matchAll(new RegExp('\\b(?:function\\*?|class|const|let|var|interface|type|enum|namespace)\\s+(' + id + ')', 'g'))) bound.add(m[1]);
for (const m of out.matchAll(new RegExp('\\b(?:const|let|var)\\s*([\\[{][^=]*[\\]}])\\s*=', 'g'))) {
  add(m[1]);
  for (const r of m[1].matchAll(new RegExp(':\\s*(' + id + ')', 'g'))) bound.add(r[1]);
}
// node runs a .js or .ts file as a CommonJS module, whose wrapper function
// takes these five as parameters: module-scoped, and not on globalThis.
for (const w of ['exports', 'require', 'module', '__filename', '__dirname']) bound.add(w);
for (const m of out.matchAll(new RegExp('\\bfunction\\*?\\s*' + '(?:' + id + ')?\\s*(?:<[^()]*>)?\\s*\\(([^)]*)\\)', 'g'))) add('(' + m[1]);
for (const m of out.matchAll(/\(([^()]*)\)\s*(?::[^=;{}()]+)?=>/g)) add('(' + m[1]);
for (const m of out.matchAll(new RegExp('(' + id + ')\\s*=>', 'g'))) bound.add(m[1]);
for (const m of out.matchAll(new RegExp('\\bcatch\\s*\\(\\s*(' + id + ')', 'g'))) bound.add(m[1]);
for (const m of out.matchAll(new RegExp('\\bimport\\s+(?:\\*\\s+as\\s+)?(' + id + ')', 'g'))) bound.add(m[1]);
const kw = new Set(['if','for','while','switch','catch','function','return','typeof','void','delete','await','yield','super','import','in','of','do','else','with','new','throw','case','instanceof','async']);
const called = new Set();
for (const m of out.matchAll(new RegExp('(?<![\\w$.])(' + id + ')\\s*(?:<[^<>()]*>)?\\s*\\(', 'g'))) if (!kw.has(m[1])) called.add(m[1]);
const missing = [...called].filter(n => !bound.has(n) && typeof globalThis[n] === 'undefined').sort();
console.log(JSON.stringify({ called: called.size, missing }));
'@

$work = Join-Path ([System.IO.Path]::GetTempPath()) "plug-callees-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
$pyFile = Join-Path $work 'analyze.py'
$jsFile = Join-Path $work 'analyze.cjs'
[System.IO.File]::WriteAllText($pyFile, $PyAnalyzer, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($jsFile, $JsAnalyzer, [System.Text.UTF8Encoding]::new($false))

function Invoke-Analyzer([string]$analyzer, [string]$file) {
    if ($analyzer -eq 'zig') {
        if (-not (Get-Command zig -ErrorAction SilentlyContinue)) { return @{ Error = "'zig' is not on PATH" } }
        $raw = @(& zig ast-check $file 2>&1 | ForEach-Object { "$_" })
        $errs = @($raw | Where-Object { $_ -match ': error: ' })
        $missing = @($errs | ForEach-Object { if ($_ -match "use of undeclared identifier '([^']+)'") { $Matches[1] } } | Sort-Object -Unique)
        $other = @($errs | Where-Object { $_ -notmatch 'use of undeclared identifier' })
        if ($other.Count -gt 0) { return @{ Error = "zig ast-check: $($other[0])" } }
        return @{ Called = (Get-CallCount $file); Missing = $missing }
    }
    if ($analyzer -eq 'dotnet') {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return @{ Error = "'dotnet' is not on PATH" } }
        $d = Join-Path $work ('cs-' + [System.IO.Path]::GetRandomFileName())
        New-Item -ItemType Directory -Force $d | Out-Null
        Copy-Item $file (Join-Path $d 'subject.cs') -Force
        $proj = Join-Path $d 'subject.csproj'
        [System.IO.File]::WriteAllLines($proj, $CsProj, [System.Text.UTF8Encoding]::new($false))
        $raw = @(& dotnet build $proj -c Release --nologo -v quiet 2>&1 | ForEach-Object { "$_" })
        $errs = @($raw | Where-Object { $_ -match ': error CS\d+' })
        $missing = @($errs | ForEach-Object { if ($_ -match "error CS0103: The name '([^']+)' does not exist") { $Matches[1] } } | Sort-Object -Unique)
        $other = @($errs | Where-Object { $_ -notmatch 'error CS0103' })
        if ($other.Count -gt 0) { return @{ Error = "dotnet build: $($other[0])" } }
        if ($errs.Count -eq 0 -and $LASTEXITCODE -ne 0) { return @{ Error = "dotnet build failed: $(($raw | Select-Object -Last 3) -join ' ')" } }
        return @{ Called = (Get-CallCount $file); Missing = $missing }
    }
    $exe = if ($analyzer -eq 'python') { 'python' } else { 'node' }
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) { return @{ Error = "'$exe' is not on PATH" } }
    $script = if ($analyzer -eq 'python') { $pyFile } else { $jsFile }
    $raw = & $exe $script $file 2>&1
    if ($LASTEXITCODE -ne 0) { return @{ Error = "the $exe analyzer failed: $(($raw | Select-Object -Last 3) -join ' ')" } }
    $j = ($raw | Select-Object -Last 1) | ConvertFrom-Json
    return @{ Called = [int]$j.called; Missing = @($j.missing) }
}

try {
    if ($SelfTest) {
        # Each analyzer must name the planted callee and nothing else, or it is
        # an instrument that cannot fail.
        $plants = @(
            @{ Analyzer = 'python'; Ext = 'py'; Text = "def helper(x):`n    return len(x)`nprint(helper([1]))`nprint(codex_planted_missing(2))`n" }
            @{ Analyzer = 'node';   Ext = 'js'; Text = "function helper(x) { return x.length; }`nconst f = (a, b) => a + b;`nconsole.log(helper([1]), f(1, 2), Math.max(1, 2));`nconsole.log(codex_planted_missing(2));`nconst { Worker: RenamedW, isMainThread: isMain } = require('worker_threads');`nif (isMain) { new RenamedW(__filename); }`n// codex_in_comment(1)`nconst s = 'codex_in_string(1)';`n" }
            @{ Analyzer = 'node';   Ext = 'ts'; Text = "function helper<T>(x: T[]): number { return x.length; }`nconst g = (a: bigint): bigint => a;`nconsole.log(helper<number>([1]), g(1n), BigInt(3), ``v=`${String(codex_planted_missing(2))}``);`n" }
            @{ Analyzer = 'zig';    Ext = 'zig'; Text = "const std = @import(`"std`");`nfn helper(x: i64) i64 { return x + 1; }`nfn never_called() i64 { return codex_planted_missing(2); }`npub fn main() void { std.debug.print(`"{}\n`", .{helper(1)}); }`n" }
            @{ Analyzer = 'dotnet'; Ext = 'cs'; Text = "using System;`nstatic class P {`n  static long Helper(long x) { return x + 1; }`n  static long NeverCalled() { return codex_planted_missing(2); }`n  static void Main() { Console.WriteLine(Helper(1)); }`n}`n" }
        )
        $bad = 0
        foreach ($p in $plants) {
            $f = Join-Path $work "plant.$($p.Ext)"
            [System.IO.File]::WriteAllText($f, $p.Text, [System.Text.UTF8Encoding]::new($false))
            $r = Invoke-Analyzer $p.Analyzer $f
            $got = if ($r.ContainsKey('Error')) { "error: $($r.Error)" } else { ($r.Missing -join ',') }
            if ($got -eq 'codex_planted_missing') { Write-Host "  self-test $($p.Ext): PASS (the planted callee and nothing else)" }
            else { Write-Host "  self-test $($p.Ext): FAIL -- expected 'codex_planted_missing', got '$got'"; $bad++ }
        }
        if ($bad -gt 0) { exit 1 }
        exit 0
    }

    $fail = 0; $checked = 0
    foreach ($row in $Rows) {
        $f = Join-Path $Dir "subject.$($row.Ext)"
        if (-not (Test-Path -PathType Leaf $f)) {
            Write-Host "  $($row.Plug): NO FILE -- $f (run build/plug-oracle-test.ps1 -Only $($row.Plug) -KeepArtifacts)"
            continue
        }
        $r = Invoke-Analyzer $row.Analyzer $f
        if ($r.ContainsKey('Error')) { Write-Host "  $($row.Plug): FAIL -- $($r.Error)"; $fail++; continue }
        $checked++
        if ($r.Missing.Count -gt 0) {
            Write-Host "  $($row.Plug): FAIL -- $($r.Missing.Count) undefined callee(s) of $($r.Called): $($r.Missing -join ', ')"
            $fail++
        } else {
            Write-Host "  $($row.Plug): PASS -- $($r.Called) distinct callees, each defined in the file or a runtime global"
        }
    }
    Write-Host "plug-callees: $checked checked, $fail failed"
    if ($fail -gt 0) { exit 1 }
    if ($checked -eq 0) { Write-Host "plug-callees: no emitted file was present, so nothing was checked"; exit 1 }
    exit 0
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
