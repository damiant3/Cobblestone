# Launch the Codex [Device] fireworks kernel on a real NVIDIA GPU.
# Loads FireworksKernel.ptx via the CUDA driver (nvcuda.dll), runs the
# fw_burst_spark_kernel entry as several bursts, reads the GPU-computed
# spark positions back, and composites them into a PNG. Pure PowerShell
# P/Invoke -- no C compiler, just the CUDA driver.
[CmdletBinding()]
param(
  [string]$Ptx = (Join-Path $PSScriptRoot 'FireworksKernel.ptx'),
  [string]$Out = (Join-Path $PSScriptRoot '..\..\build\output\fw-gpu.png')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Cuda {
  [DllImport("nvcuda")] public static extern int cuInit(uint f);
  [DllImport("nvcuda")] public static extern int cuDeviceGet(out int dev, int ord);
  [DllImport("nvcuda")] public static extern int cuDeviceGetName(byte[] name, int len, int dev);
  [DllImport("nvcuda")] public static extern int cuCtxCreate_v2(out IntPtr ctx, uint flags, int dev);
  [DllImport("nvcuda")] public static extern int cuModuleLoadData(out IntPtr m, IntPtr image);
  [DllImport("nvcuda")] public static extern int cuModuleGetFunction(out IntPtr f, IntPtr m, string name);
  [DllImport("nvcuda")] public static extern int cuMemAlloc_v2(out ulong dptr, UIntPtr bytes);
  [DllImport("nvcuda")] public static extern int cuMemsetD8_v2(ulong dptr, byte uc, UIntPtr n);
  [DllImport("nvcuda")] public static extern int cuMemcpyDtoH_v2(IntPtr dst, ulong src, UIntPtr bytes);
  [DllImport("nvcuda")] public static extern int cuLaunchKernel(IntPtr f, uint gx, uint gy, uint gz, uint bx, uint by, uint bz, uint sh, IntPtr stream, IntPtr kparams, IntPtr extra);
  [DllImport("nvcuda")] public static extern int cuCtxSynchronize();
  [DllImport("nvcuda")] public static extern int cuMemFree_v2(ulong dptr);
  [DllImport("nvcuda")] public static extern int cuGetErrorName(int err, out IntPtr s);
}
"@

function Check($code, $what) {
  if ($code -ne 0) {
    $p = [IntPtr]::Zero
    [void][Cuda]::cuGetErrorName($code, [ref]$p)
    $name = if ($p -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::PtrToStringAnsi($p) } else { "?" }
    throw "$what failed: CUresult $code ($name)"
  }
}

Check ([Cuda]::cuInit(0)) 'cuInit'
$dev = 0
Check ([Cuda]::cuDeviceGet([ref]$dev, 0)) 'cuDeviceGet'
$nameBuf = New-Object byte[] 128
Check ([Cuda]::cuDeviceGetName($nameBuf, 128, $dev)) 'cuDeviceGetName'
$gpuName = [Text.Encoding]::ASCII.GetString($nameBuf).TrimEnd([char]0)
Write-Host "[gpu] device: $gpuName"

$ctx = [IntPtr]::Zero
Check ([Cuda]::cuCtxCreate_v2([ref]$ctx, 0, $dev)) 'cuCtxCreate'

# Load PTX (null-terminated) and JIT it for this GPU
$ptxBytes = [IO.File]::ReadAllBytes((Resolve-Path $Ptx))
$ptxNull = New-Object byte[] ($ptxBytes.Length + 1)
[Array]::Copy($ptxBytes, $ptxNull, $ptxBytes.Length)
$ptxPin = [Runtime.InteropServices.GCHandle]::Alloc($ptxNull, 'Pinned')
$mod = [IntPtr]::Zero
try { Check ([Cuda]::cuModuleLoadData([ref]$mod, $ptxPin.AddrOfPinnedObject())) 'cuModuleLoadData (JIT)' }
finally { $ptxPin.Free() }
$func = [IntPtr]::Zero
Check ([Cuda]::cuModuleGetFunction([ref]$func, $mod, 'fw_burst_spark_kernel')) 'cuModuleGetFunction'
Write-Host "[gpu] kernel fw_burst_spark_kernel JIT-compiled and loaded"

$N = 3072
$bytes = [uint64]($N * 8)
$dptr = [uint64]0
Check ([Cuda]::cuMemAlloc_v2([ref]$dptr, [UIntPtr]::new($bytes))) 'cuMemAlloc'

# Bursts: cx, cy, frame (expansion), color R,G,B
$bursts = @(
  @(300,250,46, 0xFF,0x33,0x24),
  @(520,205,40, 0xFF,0xD0,0x28),
  @(720,300,52, 0x3A,0x6B,0xFF),
  @(455,380,60, 0x46,0xE0,0x70),
  @(628,335,34, 0xC6,0x4B,0xFF)
)

$bmp = New-Object System.Drawing.Bitmap 1024, 768
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(10,10,24))

$host32 = New-Object long[] $N
$hPin = [Runtime.InteropServices.GCHandle]::Alloc($host32, 'Pinned')
$grid = [uint32][math]::Ceiling($N / 256.0)
$totalPlotted = 0
try {
  foreach ($b in $bursts) {
    $cx=$b[0]; $cy=$b[1]; $frame=$b[2]; $cr=$b[3]; $cg=$b[4]; $cb=$b[5]
    Check ([Cuda]::cuMemsetD8_v2($dptr, 0, [UIntPtr]::new($bytes))) 'cuMemset'

    # kernel args: out, cx, cy, frame, nspark, pixel_count  (all .s64)
    $vals = [long[]]@([long]$dptr, $cx, $cy, $frame, $N, $N)
    $argPtrs = New-Object IntPtr[] 6
    for ($i=0; $i -lt 6; $i++) {
      $ap = [Runtime.InteropServices.Marshal]::AllocHGlobal(8)
      [Runtime.InteropServices.Marshal]::WriteInt64($ap, $vals[$i])
      $argPtrs[$i] = $ap
    }
    $kparams = [Runtime.InteropServices.Marshal]::AllocHGlobal(8 * 6)
    for ($i=0; $i -lt 6; $i++) { [Runtime.InteropServices.Marshal]::WriteIntPtr($kparams, $i*8, $argPtrs[$i]) }

    Check ([Cuda]::cuLaunchKernel($func, $grid,1,1, 256,1,1, 0, [IntPtr]::Zero, $kparams, [IntPtr]::Zero)) 'cuLaunchKernel'
    Check ([Cuda]::cuCtxSynchronize()) 'cuCtxSynchronize'
    Check ([Cuda]::cuMemcpyDtoH_v2($hPin.AddrOfPinnedObject(), $dptr, [UIntPtr]::new($bytes))) 'cuMemcpyDtoH'

    for ($i=0; $i -lt 6; $i++) { [Runtime.InteropServices.Marshal]::FreeHGlobal($argPtrs[$i]) }
    [Runtime.InteropServices.Marshal]::FreeHGlobal($kparams)

    # decode packed = bright<<24 | px<<12 | py  (computed on the GPU)
    for ($i=0; $i -lt $N; $i++) {
      $p = $host32[$i]
      if ($p -le 0) { continue }
      $py = [int]($p -band 0xFFF)
      $px = [int](($p -shr 12) -band 0xFFF)
      $bright = [int](($p -shr 24) -band 0xFF)
      if ($px -lt 1 -or $px -gt 1022 -or $py -lt 1 -or $py -gt 766) { continue }
      $sr = [int]($cr * $bright / 255); $sg = [int]($cg * $bright / 255); $sb = [int]($cb * $bright / 255)
      $col = [System.Drawing.Color]::FromArgb(255, $sr, $sg, $sb)
      $br = New-Object System.Drawing.SolidBrush $col
      $g.FillRectangle($br, $px-1, $py-1, 3, 3)
      $br.Dispose()
      $totalPlotted++
    }
  }
} finally { $hPin.Free() }

$g.Dispose()
$outFull = [IO.Path]::GetFullPath($Out)
$bmp.Save($outFull, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
[void][Cuda]::cuMemFree_v2($dptr)
Write-Host "[gpu] launched $($bursts.Count) bursts x $N threads on '$gpuName'; plotted $totalPlotted sparks"
Write-Host "[gpu] image: $outFull"
