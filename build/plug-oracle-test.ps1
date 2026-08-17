# plug-oracle-test.ps1 -- run a plug's OUTPUT, not just its exit code.
#
# Nothing in the tree executed a transpiler plug's emitted source. The IR
# wire is checked (`check-plug-types.ps1`) and the two NATIVE plugs are
# executed by the cross-arch battery, but for the ~45 language plugs the
# only thing ever asserted was that the plug produced bytes. An arm that
# emits syntactically valid and semantically wrong code was invisible.
#
# That is not hypothetical. Auditing the integer arms once (2026-07-22)
# found six plugs lowering `/` to a FLOORED quotient while the compiler
# truncates, and seven emitting a remainder that was floored or Euclidean
# where Codex means truncating. `-7 / 2` answered -4 through those plugs
# and -3 on x86-64. Every one compiled fine. Every one passed everything
# the tree could ask.
#
# This harness asks the only question that finds that class: compile ONE
# subject two ways, RUN BOTH, and require the answers to agree.
#
#     codex/test/plug-oracle-arith.codex
#         -> compile.ps1 (CDX)    -> codex-vm  -> the truth
#         -> compile.ps1 (-IrCce) -> the plug  -> that language's runtime
#
# x86-64 is the reference because it is the fixed point: the compiler is
# a fixed point of itself there, so its arithmetic is the definition.
#
# Usage:
#   build/plug-oracle-test.ps1                 # every wired plug
#   build/plug-oracle-test.ps1 -Only python
#   build/plug-oracle-test.ps1 -KeepArtifacts  # keep emitted source + output
#
# A plug is wired here only when BOTH its CDX and a runtime for its
# language exist on the box. Anything else is reported as SKIPPED with the
# reason, never as a pass -- a harness that silently covers nothing is the
# failure it exists to prevent.
[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$Kernel = '',
    [switch]$KeepArtifacts,
    [int]$TimeoutSec = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Subject = Join-Path $Repo 'codex\test\plug-oracle-arith.codex'
$Work    = Join-Path $Repo 'build-output\plug-oracle'
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

# Each entry: the plug CDX, and how to run what it emits. `Ext` is only
# cosmetic; `Exe` is the runtime that must be on PATH for the arm to run at
# all, and `Args` the argv it takes.
#
# Two optional hooks exist because not every plug speaks the same protocol
# and not every language runs from loose source:
#
#   Transpile ($ir, $srcOut)  overrides the default `run-plug.ps1` delivery.
#   Invoke    ($srcOut)       overrides "hand the file to `Exe`", and returns
#                             the program's output lines.
#
# The C# plug needs both. It does NOT answer over TCP -- it writes its output
# to the codex-vm output ring, which `codex/plugs/csharp/run.ps1` drains via
# `-output`, so `run-plug.ps1` waits for a reply that never comes and reports
# the plug as having emitted nothing. And a loose `.cs` is not runnable under
# the .NET 9 SDK on this box, so the arm scaffolds the same csproj that
# `codex/plugs/csharp/emit-app.ps1` generates and runs the built exe.
$Plugs = @(
    @{ Name = 'python'
       Cdx  = 'codex\plugs\python\build-output\python-plug.cdx'
       Ext  = 'py'
       Exe  = 'python'
       Args = { param($f) @($f) } }
    @{ Name = 'javascript'
       Cdx  = 'codex\plugs\javascript\build-output\javascript-plug.cdx'
       Ext  = 'js'
       Exe  = 'node'
       Args = { param($f) @($f) } }
    @{ Name = 'zig'
       Cdx  = 'codex\plugs\zig\build-output\zig-plug.cdx'
       Ext  = 'zig'
       Exe  = 'zig'
       Args = { param($f) @('run', $f) } }
    @{ Name = 'wasm'
       Cdx  = 'codex\plugs\wasm\build-output\wasm-plug.cdx'
       Ext  = 'wat'
       Exe  = 'wasmtime'
       # The wasm plug answers over the codex-vm output ring, not TCP, so it
       # needs its own run.ps1 the way csharp does; -Ir keeps it on the same
       # IR every other arm is graded against. Its output is WAT, which has to
       # be assembled before it can run: wat2wasm is a second toolchain this
       # entry depends on, and a missing one is reported loudly rather than
       # quietly passing.
       Transpile = {
           param($ir, $srcOut)
           & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\run.ps1') -Ir $ir -Out $srcOut | Out-Null
       }
       Invoke = {
           param($srcOut)
           if (-not (Get-Command wat2wasm -ErrorAction SilentlyContinue)) { throw "wat2wasm is not on PATH; the WAT cannot be assembled" }
           $mod = [System.IO.Path]::ChangeExtension($srcOut, '.wasm')
           Remove-Item $mod -Force -ErrorAction SilentlyContinue
           $asm = & wat2wasm $srcOut -o $mod 2>&1
           if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $mod)) { throw "wat2wasm failed:`n$($asm -join "`n")" }
           # Bare metal answers deep recursion with a multi-gigabyte arena, so a
           # host stack small enough to refuse it is grading the HOST and not the
           # plug. wasmtime's default max-wasm-stack cannot take the recursion
           # rows; the module is unchanged either way (measured: 6 of 6 with this
           # flag, 3 of 6 without).
           & wasmtime run -W max-wasm-stack=268435456 $mod 2>&1
       } }
    @{ Name = 'csharp'
       Cdx  = 'codex\plugs\csharp\build-output\csharp-plug.cdx'
       Ext  = 'cs'
       Exe  = 'dotnet'
       Transpile = {
           param($ir, $srcOut)
           & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\csharp\run.ps1') -Ir $ir -Out $srcOut | Out-Null
       }
       Invoke = {
           param($srcOut)
           $d = Join-Path $Work 'cs'
           New-Item -ItemType Directory -Force $d | Out-Null
           Copy-Item $srcOut (Join-Path $d 'subject.cs') -Force
           $proj = Join-Path $d 'subject.csproj'
           $lines = @(
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
           [System.IO.File]::WriteAllLines($proj, $lines, [System.Text.UTF8Encoding]::new($false))
           $buildLog = & dotnet build $proj -c Release --nologo -v quiet 2>&1
           if ($LASTEXITCODE -ne 0) { throw "dotnet build failed:`n$($buildLog -join "`n")" }
           $exe = Join-Path $d 'bin\Release\net9.0\subject.exe'
           if (-not (Test-Path -PathType Leaf $exe)) { throw "expected exe not found: $exe" }
           & $exe 2>&1
       } }
)

if (-not (Test-Path $Subject)) { Write-Host "MISSING subject: $Subject"; exit 2 }
if (-not (Test-Path $Kernel))  { Write-Host "MISSING kernel: $Kernel";   exit 2 }
New-Item -ItemType Directory -Force $Work | Out-Null

# ---------------------------------------------------------------------------
# The truth: the subject on x86-64.
# ---------------------------------------------------------------------------
$truthCdx = Join-Path $Work 'subject.cdx'
$truthOut = Join-Path $Work 'subject.x86.out'
$truthLog = Join-Path $Work 'subject.compile.log'

Write-Host "[oracle] compiling the subject for x86-64..."
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $truthCdx -Log $truthLog -Kernel $Kernel | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: subject did not compile to CDX; see $truthLog"; exit 3 }

& (Join-Path $Repo 'tools\codex-vm.exe') -kernel $truthCdx -headless -output $truthOut 2>&1 | Out-Null
$truth = @(Get-Content $truthOut -ErrorAction SilentlyContinue | Where-Object { $_.Trim().Length -gt 0 })
if ($truth.Count -eq 0) { Write-Host "FAIL: the subject produced no output on x86-64"; exit 3 }
Write-Host "[oracle] truth: $($truth.Count) values from x86-64"

# The subject must exercise negative operands, or every rounding rule on
# earth passes it. Guard the harness against its own subject being
# weakened later.
if (-not ($truth -match '^-')) {
    Write-Host "FAIL: no negative results in the truth set -- the subject cannot discriminate"
    exit 3
}

# ---------------------------------------------------------------------------
# The IR the plugs receive. CCE, because the wire is CCE, and passes=text-plug
# because that is what every source plug's run.ps1 sends in service. Scoring
# the DEFAULT pipeline scored a program no plug is ever handed. Measured
# 2026-08-16 on this subject through python: exactly one of the 20 sites
# moved, `dv 7 3`, the only call whose arguments are both literals -- it was
# emitted as the inlined division lambda instead of `dv(7, 3)`, and the other
# 19 kept their calls. So the gap is narrow here, not the whole call surface.
# It is still the wrong program to grade: the harness must score what the
# plugs are sent, and a subject that later leans harder on leaf calls would
# widen this silently.
# ---------------------------------------------------------------------------
$irFile = Join-Path $Work 'subject.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $irFile -Log (Join-Path $Work 'subject.ir.log') -Kernel $Kernel -IrCce -Passes 'text-plug' | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: subject did not compile to IR"; exit 3 }

# ---------------------------------------------------------------------------
# Each plug.
# ---------------------------------------------------------------------------
$pass = 0; $fail = 0; $skip = 0
foreach ($p in $Plugs) {
    if ($Only -and $p.Name -ne $Only) { continue }

    $cdx = Join-Path $Repo $p.Cdx
    if (-not (Test-Path $cdx)) {
        Write-Host "  $($p.Name): SKIPPED -- no plug binary at $($p.Cdx) (build it with codex/plugs/$($p.Name)/build.ps1)"
        $skip++; continue
    }
    $exe = Get-Command $p.Exe -ErrorAction SilentlyContinue
    if (-not $exe) {
        Write-Host "  $($p.Name): SKIPPED -- '$($p.Exe)' is not on PATH, so its output cannot be run"
        $skip++; continue
    }

    # Remove any previous emission first: a plug that silently fails would
    # otherwise be scored on the last run's source, which passes.
    $srcOut = Join-Path $Work "subject.$($p.Ext)"
    Remove-Item $srcOut -Force -ErrorAction SilentlyContinue

    if ($p.ContainsKey('Transpile')) {
        & $p.Transpile $irFile $srcOut
    } else {
        & pwsh -NoProfile -File (Join-Path $Repo 'build\run-plug.ps1') -Plug $cdx -InFile $irFile -Output $srcOut -TimeoutSec $TimeoutSec | Out-Null
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $srcOut) -or (Get-Item $srcOut).Length -eq 0) {
        Write-Host "  $($p.Name): FAIL -- the plug emitted nothing"
        $fail++; continue
    }

    try {
        $runOut = if ($p.ContainsKey('Invoke')) { & $p.Invoke $srcOut }
                  else { & $exe.Source @(& $p.Args $srcOut) 2>&1 }
    } catch {
        Write-Host "  $($p.Name): FAIL -- the emitted program could not be run: $_"
        $fail++; continue
    }
    $got = @($runOut | ForEach-Object { "$_" } | Where-Object { $_.Trim().Length -gt 0 })

    if ($got.Count -eq 0) {
        Write-Host "  $($p.Name): FAIL -- the emitted program produced no output"
        $fail++; continue
    }

    $diff = Compare-Object $truth $got -SyncWindow 0
    if ($diff) {
        Write-Host "  $($p.Name): FAIL -- $($diff.Count) line(s) differ from x86-64"
        for ($i = 0; $i -lt [Math]::Max($truth.Count, $got.Count); $i++) {
            $t = if ($i -lt $truth.Count) { $truth[$i] } else { '<none>' }
            $g = if ($i -lt $got.Count)   { $got[$i] }   else { '<none>' }
            if ($t -ne $g) { Write-Host "      line $($i + 1): x86-64 $t, $($p.Name) $g" }
        }
        $fail++
    } else {
        Write-Host "  $($p.Name): PASS -- $($got.Count) values match x86-64"
        $pass++
    }
}

if (-not $KeepArtifacts) {
    Remove-Item $truthCdx, $truthOut, $truthLog -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "plug-oracle: $pass passed, $fail failed, $skip skipped"
if ($fail -gt 0) { exit 1 }
if ($pass -eq 0) { Write-Host "plug-oracle: nothing was actually checked"; exit 1 }
exit 0
