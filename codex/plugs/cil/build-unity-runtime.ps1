[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference='Stop'
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
$sourcePath=Join-Path $PSScriptRoot 'build-output/test/unity-all.cs'
if(-not(Test-Path -LiteralPath $sourcePath)){throw 'Run node codex/plugs/cil/test.mjs --export-unity first'}
$sdk=@(& dotnet --list-sdks)[-1]
if($LASTEXITCODE -ne 0 -or $sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'A developer .NET SDK is required to build the bundled adapter'}
$roslyn=Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore"
$refs=@((Join-Path $roslyn 'Microsoft.CodeAnalysis.dll'),(Join-Path $roslyn 'Microsoft.CodeAnalysis.CSharp.dll'))
foreach($ref in $refs){[void][Reflection.Assembly]::LoadFrom($ref)}
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($refs+@('System.Runtime.dll','System.Collections.dll','System.Collections.Immutable.dll','System.Linq.dll')) -TypeDefinition @'
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
public sealed class PrismRuntimeSource : CSharpSyntaxRewriter {
    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node) {
        if(node.Parent is ClassDeclarationSyntax owner && owner.Identifier.ValueText=="PrismModIdentity" && node.Declaration.Variables.Count==1 && node.Declaration.Variables[0].Identifier.ValueText=="BuildNumber")
            return SyntaxFactory.ParseMemberDeclaration("internal static string BuildNumber { get { return PrismRuntimeModel.BuildNumber; } }");
        return base.VisitFieldDeclaration(node);
    }
    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) {
        if (node.Expression.ToString() == "Codex_Program.opening")
            return SyntaxFactory.ParseExpression("PrismRuntimeModel.Opening()");
        return base.VisitInvocationExpression(node);
    }
    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) {
        if (node.Identifier.ValueText != "Codex_Program") return base.VisitClassDeclaration(node);
        var members = new List<MemberDeclarationSyntax>();
        foreach (var method in node.Members.OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText.StartsWith("unity_",StringComparison.Ordinal))) {
            members.Add(method);
            var parameters = new List<string>();
            var call = method.Identifier.ValueText;
            foreach (var parameter in method.ParameterList.Parameters) {
                var name = "arg" + parameters.Count;
                parameters.Add(parameter.Type.ToString() + " " + name);
                call += "(" + name + ")";
            }
            var result = method.ReturnType;
            while (result is GenericNameSyntax generic && generic.Identifier.ValueText == "Func") {
                if (generic.TypeArgumentList.Arguments.Count != 2) throw new Exception("Unexpected effect delegate arity");
                var name = "arg" + parameters.Count;
                parameters.Add(generic.TypeArgumentList.Arguments[0].ToString() + " " + name);
                call += "(" + name + ")";
                result = generic.TypeArgumentList.Arguments[1];
            }
            if (result.ToString() != "object") throw new Exception("Unexpected effect return type");
            members.Add(SyntaxFactory.ParseMemberDeclaration("public static void " + method.Identifier.ValueText + "_cil(" + string.Join(",",parameters) + "){ " + call + "; }"));
        }
        if (members.Count == 0) throw new Exception("No Unity effect adapters found");
        members.Add(SyntaxFactory.ParseMemberDeclaration("public static string integer_to_text_cil(long value){return _Cce.FromUnicode(value.ToString(System.Globalization.CultureInfo.InvariantCulture));}"));
        return node.WithIdentifier(SyntaxFactory.Identifier("PrismRuntimeEffects")).WithMembers(SyntaxFactory.List(members));
    }
    public static string Build(string source) {
        var tree = CSharpSyntaxTree.ParseText(source);
        if (tree.GetDiagnostics().Any(d=>d.Severity==DiagnosticSeverity.Error)) throw new Exception("Invalid generated Unity source");
        var root = (CompilationUnitSyntax)new PrismRuntimeSource().Visit(tree.GetRoot());
        var attribute = SyntaxFactory.ParseCompilationUnit("[assembly:System.Reflection.AssemblyVersion(\"1.0.0.0\")]");
        root = root.AddAttributeLists(attribute.AttributeLists.ToArray());
        return root.NormalizeWhitespace().ToFullString();
    }
}
'@
$source=[PrismRuntimeSource]::Build([IO.File]::ReadAllText($sourcePath))
$source += @'

namespace PrismGenerated {
    public static class PrismRuntimeModel {
        private static System.Reflection.Assembly model;
        public static string BuildNumber { get { return model==null ? "uninitialized" : model.ManifestModule.ModuleVersionId.ToString("N"); } }
        public static void Initialize(System.Reflection.Assembly assembly) {
            if(model != null && model != assembly) throw new System.InvalidOperationException("A different Prism model is already active");
            if(model == null) System.AppDomain.CurrentDomain.AssemblyResolve += ResolveRuntime;
            model=assembly;
            PrismUnityEntry.Initialize();
        }
        private static System.Reflection.Assembly ResolveRuntime(object sender, System.ResolveEventArgs args) {
            if(args.RequestingAssembly == model && new System.Reflection.AssemblyName(args.Name).Name == "PrismRuntime") return typeof(PrismRuntimeModel).Assembly;
            return null;
        }
        public static void Opening() {
            model.GetType("PrismGenerated.Codex_Program",true).GetMethod("opening").Invoke(null,null);
        }
    }
}
'@
$managed=Join-Path $GameDir 'valheim_Data/Managed'
$source=$source.Replace('__PRISM_GAME_HASH__',(Get-FileHash (Join-Path $managed 'assembly_valheim.dll')).Hash)
$source=$source.Replace('__PRISM_UNITY_HASH__',(Get-FileHash (Join-Path $GameDir 'UnityPlayer.dll')).Hash)
$source=$source.Replace('__PRISM_GROUP_LIMIT__','4').Replace('__PRISM_WORKBENCH_LINKS__','true')
if($source -match '__PRISM_'){throw 'Unresolved adapter configuration placeholder'}
$out=Join-Path $PSScriptRoot 'build-output/runtime'
[void](New-Item -ItemType Directory -Force -Path $out)
$runtimeSource=Join-Path $out 'PrismRuntime.cs'
$runtimeDll=Join-Path $out 'PrismRuntime.dll'
[IO.File]::WriteAllText($runtimeSource,$source,[Text.UTF8Encoding]::new($false))
$arguments=@((Join-Path $roslyn 'csc.dll'),'-nologo','-target:library','-langversion:latest','-nostdlib+','-deterministic+','-optimize+','-platform:x64',('-out:'+$runtimeDll))
$names=@('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Numerics.dll','UnityEngine.CoreModule.dll','assembly_valheim.dll','assembly_utils.dll','assembly_guiutils.dll','UnityEngine.PhysicsModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.UI.dll','Unity.TextMeshPro.dll','Splatform.dll','UnityEngine.ImageConversionModule.dll')
foreach($name in $names){$path=Join-Path $managed $name;if(-not(Test-Path -LiteralPath $path)){throw "Missing runtime reference: $path"};$arguments+=('-reference:'+$path)}
$mask=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../apps/modbuilder/mods/valheim/cirrus-mask.png'))
$arguments+=('-resource:'+$mask+',PrismGenerated.CirrusMask.png')
$arguments+=$runtimeSource
& dotnet @arguments
if($LASTEXITCODE -ne 0){throw 'Unity runtime compilation failed'}
Write-Host "Built bundled adapter: $runtimeDll"
Write-Host "SHA256: $((Get-FileHash $runtimeDll).Hash)"
