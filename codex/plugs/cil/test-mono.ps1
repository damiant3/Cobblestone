[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [ValidateSet('','scalar','tail','closure','callbacks','storage','list','record','unity-fixture','unity-storage','unity-meadows','unity-arc','unity-hover','unity-hud','unity-rows','unity-tools','unity-farming','unity-firekeeping','unity-camera','unity-loadout','unity-all')][string]$Subject = '',
    [string]$Runtime = '',
    [switch]$Prepare
)
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$subjects = [ordered]@{scalar=128L;tail=21L;closure=5L;callbacks=151L;storage=3L;list=33L;record=35L}
if (-not $Subject) {
    $mono = Join-Path $GameDir 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'
    Write-Host "Mono runtime SHA256: $((Get-FileHash -LiteralPath $mono).Hash)"
    foreach ($name in $subjects.Keys) {
        & pwsh -NoProfile -File $PSCommandPath -GameDir $GameDir -Subject $name
        if ($LASTEXITCODE -ne 0) { throw "Mono subject failed: $name (exit $LASTEXITCODE)" }
    }
    exit 0
}
$subjects['unity-fixture']=1L
if($Prepare){if(-not $Runtime){throw 'Preparing Unity methods requires the adapter path'};$Runtime=(Resolve-Path -LiteralPath $Runtime).Path;$subjects[$Subject]=0L}
elseif($Subject.StartsWith('unity-') -and $Subject -ne 'unity-fixture'){throw 'Game-facing entry points require an actual game acceptance run'}
$assembly = Join-Path $PSScriptRoot "build-output/test/$Subject.dll"
if (-not (Test-Path -LiteralPath $assembly)) { throw "Run test.mjs first: $assembly" }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class CilMonoProof {
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetDirs(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string assemblies,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Init(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PathSetting(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Named(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Open(
        IntPtr domain, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr One(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Class(
        IntPtr image, [MarshalAs(UnmanagedType.LPUTF8Str)] string space,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Method(
        IntPtr type, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Invoke(
        IntPtr method, IntPtr target, IntPtr args, out IntPtr exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Cleanup(IntPtr domain);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Table(IntPtr image, int table);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Rows(IntPtr table);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ClassToken(IntPtr image, int token);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Methods(IntPtr type, ref IntPtr iterator);
    static T Export<T>(IntPtr library, string name) where T : Delegate {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
    }
    static IntPtr Require(IntPtr value, string name) {
        if (value == IntPtr.Zero) throw new Exception("Mono returned null: " + name);
        return value;
    }
    public static long Run(string game, string assembly, bool bootstrap, string runtime, bool prepare) {
        SetErrorMode(3);
        var library = NativeLibrary.Load(Path.Combine(game,"MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll"));
        Export<SetDirs>(library,"mono_set_dirs")(
            Path.Combine(game,"valheim_Data/Managed"),Path.Combine(game,"MonoBleedingEdge/etc"));
        Export<PathSetting>(library,"mono_set_assemblies_path")(Path.Combine(game,"valheim_Data/Managed"));
        var domain = Require(Export<Init>(library,"mono_jit_init_version")("CilProof","v4.0.30319"),"domain");
        try {
            if(!string.IsNullOrEmpty(runtime)) Require(Export<Open>(library,"mono_domain_assembly_open")(domain,runtime),"adapter");
            var loaded = Require(Export<Open>(library,"mono_domain_assembly_open")(domain,assembly),"assembly");
            var image = Require(Export<One>(library,"mono_assembly_get_image")(loaded),"image");
            if(prepare) {
                var table=Require(Export<Table>(library,"mono_image_get_table_info")(image,2),"type table");
                int count=Export<Rows>(library,"mono_table_info_get_rows")(table);
                for(int row=1;row<=count;row++) {
                    var current=Require(Export<ClassToken>(library,"mono_class_get")(image,0x02000000+row),"class");
                    IntPtr iterator=IntPtr.Zero, currentMethod;
                    while((currentMethod=Export<Methods>(library,"mono_class_get_methods")(current,ref iterator))!=IntPtr.Zero)
                        Require(Export<One>(library,"mono_compile_method")(currentMethod),"compiled method");
                }
                return 0;
            }
            var type = Require(Export<Class>(library,"mono_class_from_name")(image,bootstrap?"":"PrismGenerated",bootstrap?"PrismUnityEntry":"Codex_Program"),"type");
            var method = Require(Export<Method>(library,"mono_class_get_method_from_name")(type,bootstrap?"Initialize":"opening",0),"entry");
            IntPtr exception;
            var value = Export<Invoke>(library,"mono_runtime_invoke")(method,IntPtr.Zero,IntPtr.Zero,out exception);
            if (exception != IntPtr.Zero) throw new Exception("Mono opening threw a managed exception");
            if (bootstrap) {
                var identity=Require(Export<Named>(library,"mono_assembly_name_new")("PrismRuntime, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"),"adapter identity");
                IntPtr adapter;
                try {adapter=Require(Export<One>(library,"mono_assembly_loaded")(identity),"loaded embedded adapter");}
                finally {Export<Cleanup>(library,"mono_assembly_name_free")(identity);}
                var adapterImage=Require(Export<One>(library,"mono_assembly_get_image")(adapter),"adapter image");
                var effects=Require(Export<Class>(library,"mono_class_from_name")(adapterImage,"PrismGenerated","PrismRuntimeEffects"),"adapter effects");
                var proof=Require(Export<Method>(library,"mono_class_get_method_from_name")(effects,"Proof",0),"callback witness");
                value=Export<Invoke>(library,"mono_runtime_invoke")(proof,IntPtr.Zero,IntPtr.Zero,out exception);
                if(exception!=IntPtr.Zero)throw new Exception("Callback witness threw");
            }
            var data = Require(Export<One>(library,"mono_object_unbox")(Require(value,"result")),"unboxed result");
            return Marshal.ReadInt64(data);
        } finally {
            Export<Cleanup>(library,"mono_jit_cleanup")(domain);
        }
    }
}
'@
$actual = [CilMonoProof]::Run($GameDir,$assembly,($Subject -eq 'unity-fixture'),$Runtime,$Prepare.IsPresent)
if ($actual -ne $subjects[$Subject]) { throw "$Subject returned $actual, expected $($subjects[$Subject])" }
if($Prepare){Write-Host "PASS Valheim Mono JIT $Subject"}else{Write-Host "PASS Valheim Mono $Subject = $actual"}
