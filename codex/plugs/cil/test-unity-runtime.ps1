[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference='Stop'
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$out=Join-Path $PSScriptRoot 'build-output/fixture'
[void](New-Item -ItemType Directory -Force -Path $out)
$source=Join-Path $out 'PrismRuntime.cs'
$dll=Join-Path $out 'PrismRuntime.dll'
$code=@'
using System;
using System.Reflection;
using System.Collections.Generic;
[assembly:AssemblyVersion("1.0.0.0")]
namespace PrismGenerated {
    public sealed class StorageSlot {
        public long ss_index {get;} public long ss_quantity {get;} public long ss_capacity {get;} public bool ss_compatible {get;}
        public StorageSlot(long i,long q,long c,bool compatible){ss_index=i;ss_quantity=q;ss_capacity=c;ss_compatible=compatible;}
    }
    public sealed class StorageMove {
        public long sm_index {get;} public long sm_expected {get;} public long sm_amount {get;}
        public StorageMove(long i,long e,long a){sm_index=i;sm_expected=e;sm_amount=a;}
    }
    public sealed class StoragePlan {
        public bool sp_valid {get;} public List<StorageMove> sp_moves {get;} public long sp_remainder {get;}
        public StoragePlan(bool v,List<StorageMove> m,long r){sp_valid=v;sp_moves=m;sp_remainder=r;}
    }
    public static class PrismRuntimeEffects {
        public static bool Verified;
        public static bool StorageVerified;
        public static long Proof() { return Verified && StorageVerified ? 1L : 0L; }
        public static void unity_storage_start_cil(Func<List<StorageSlot>,Func<long,StoragePlan>> deposit,Func<List<StorageSlot>,Func<long,StoragePlan>> withdraw) {
            var slots=new List<StorageSlot>{new StorageSlot(0,8,10,true),new StorageSlot(1,0,10,true)};
            var plan=deposit(slots)(7);
            if(!plan.sp_valid || plan.sp_remainder!=0 || plan.sp_moves.Count!=2 || plan.sp_moves[0].sm_amount!=2 || plan.sp_moves[1].sm_amount!=5) throw new Exception("Storage deposit callback mismatch");
            plan=withdraw(slots)(9);
            if(plan.sp_valid || plan.sp_moves.Count!=0 || plan.sp_remainder!=9) throw new Exception("Storage withdrawal callback mismatch");
            StorageVerified=true;
        }
        public static void unity_hud_layout_start_cil(Func<long,long> slot, Func<long,Func<long,long>> x, Func<long,long> y) {
            if(slot(2)!=1 || x(2)(3)!=50 || y(1)!=62) throw new Exception("HUD callback mismatch");
            Verified=true;
        }
    }
    public static class PrismRuntimeModel {
        private static Assembly owner;
        public static void Initialize(Assembly model) {
            owner=model;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveRuntime;
            model.GetType("PrismGenerated.Codex_Program",true).GetMethod("opening").Invoke(null,null);
            if(!PrismRuntimeEffects.Verified || !PrismRuntimeEffects.StorageVerified) throw new Exception("Model did not call the adapter");
        }
        private static Assembly ResolveRuntime(object sender, ResolveEventArgs args) {
            return args.RequestingAssembly==owner && new AssemblyName(args.Name).Name=="PrismRuntime" ? typeof(PrismRuntimeModel).Assembly : null;
        }
    }
}
'@
[IO.File]::WriteAllText($source,$code,[Text.UTF8Encoding]::new($false))
$sdk=@(& dotnet --list-sdks)[-1]
if($LASTEXITCODE -ne 0 -or $sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Developer SDK missing'}
$compiler=Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore/csc.dll"
& dotnet $compiler -nologo -target:library -nostdlib+ -deterministic+ -optimize+ ('-out:'+$dll) ('-reference:'+(Join-Path $GameDir 'valheim_Data/Managed/mscorlib.dll')) $source
if($LASTEXITCODE -ne 0){throw 'Fixture compile failed'}
& node (Join-Path $PSScriptRoot 'test.mjs') --unity-runtime $dll --unity-fixture
if($LASTEXITCODE -ne 0){throw 'Browser fixture emission failed'}
$modelBytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'build-output/test/unity-fixture.dll'))
$model=[Reflection.Assembly]::Load($modelBytes)
$stream=$model.GetManifestResourceStream('PrismRuntime.dll')
if($null -eq $stream){throw 'Embedded adapter missing'}
try {
    $buffer=[IO.MemoryStream]::new();$stream.CopyTo($buffer)
    if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buffer.ToArray())) -ne (Get-FileHash $dll).Hash){throw 'Embedded adapter bytes differ'}
} finally {$stream.Dispose();if($buffer){$buffer.Dispose()}}
$model.GetType('PrismUnityEntry',$true).GetMethod('Initialize').Invoke($null,@())
$loaded=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object {$_.GetName().Name -eq 'PrismRuntime'})
if($loaded.Count -ne 1 -or $loaded[0].GetType('PrismGenerated.PrismRuntimeEffects',$true).GetMethod('Proof').Invoke($null,@()) -ne 1L){throw 'Embedded startup did not execute both model callbacks'}
Write-Host 'PASS CLR embedded adapter bootstrap, HUD and storage callbacks'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-mono.ps1') -GameDir $GameDir -Subject unity-fixture
if($LASTEXITCODE -ne 0){throw 'Mono embedded adapter bootstrap failed'}
