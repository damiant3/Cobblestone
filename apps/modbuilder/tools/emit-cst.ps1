# C# source -> the CST chapter a ModBuilder binding holds (apps/modbuilder/bindings/<game>/*.codex).
#   pwsh apps/modbuilder/tools/emit-cst.ps1 -Source Loadout.cs -Out apps/modbuilder/bindings/valheim/Loadout.codex -Prefix loadout
# Needs a .NET SDK on the box (its Roslyn compiler is loaded from the SDK directory).
param([string]$Source,[string]$Out,[string]$Prefix)
$ErrorActionPreference='Stop'
$sdk=@(& dotnet --list-sdks)[-1]
if($sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'SDK unavailable'}
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
public static class CstLift {
 static string Q(string s){return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t")+"\"";}
 static string L(IEnumerable<string> xs){return "["+string.Join(", ",xs)+"]";}
 static string N(string n,params string[] xs){return "("+n+" "+string.Join(" ",xs)+")";}
 static string M(string s){return s==null?"None":N("Just",s);}
 static string Mods(SyntaxTokenList xs){return L(xs.Select(x=>"Cst"+char.ToUpperInvariant(x.Text[0])+x.Text.Substring(1)));}
 static Exception Bad(SyntaxNode n){return new Exception(n.Kind()+": "+n.ToString());}
 static string T(TypeSyntax t){
  if(t==null||t.ToString()=="var")return "CstInferredType";
  if(t is ArrayTypeSyntax a)return N("CstArrayType",T(a.ElementType),a.RankSpecifiers[0].Rank.ToString());
  if(t is NullableTypeSyntax z)return N("CstNullableType",T(z.ElementType));
  if(t is GenericNameSyntax g)return N("CstNamedType",Q(g.Identifier.Text),L(g.TypeArgumentList.Arguments.Select(T)));
  if(t is QualifiedNameSyntax q&&q.Right is GenericNameSyntax gg)return N("CstNamedType",Q(q.Left+"."+gg.Identifier.Text),L(gg.TypeArgumentList.Arguments.Select(T)));
  return N("CstNamedType",Q(t.ToString()),"[]");
 }
 static string Arg(ArgumentSyntax a){return "CstArgument { ca-mode = "+(a.RefKindKeyword.Text==""?"[]":"[Cst"+char.ToUpperInvariant(a.RefKindKeyword.Text[0])+a.RefKindKeyword.Text.Substring(1)+"]")+", ca-name = "+Q(a.NameColon==null?"":a.NameColon.Name.ToString())+", ca-value = "+E(a.Expression)+" }";}
 static string Param(ParameterSyntax p){return "CstParameter { cp-modifiers = "+Mods(p.Modifiers)+", cp-type = "+T(p.Type)+", cp-name = "+Q(p.Identifier.Text)+", cp-default = "+M(p.Default==null?null:E(p.Default.Value))+" }";}
 static string Vars(SeparatedSyntaxList<VariableDeclaratorSyntax> vs){return L(vs.Select(v=>"CstVariable { cv-name = "+Q(v.Identifier.Text)+", cv-value = "+M(v.Initializer==null?null:E(v.Initializer.Value))+" }"));}
 static string E(ExpressionSyntax e){
  if(e is LiteralExpressionSyntax l){if(l.IsKind(SyntaxKind.NullLiteralExpression))return "CstNull";if(l.IsKind(SyntaxKind.TrueLiteralExpression)||l.IsKind(SyntaxKind.FalseLiteralExpression))return N("CstBoolean",l.Token.ValueText=="true"?"True":"False");if(l.IsKind(SyntaxKind.StringLiteralExpression))return N("CstString",Q(l.Token.ValueText));if(l.IsKind(SyntaxKind.CharacterLiteralExpression))return N("CstCharacter",((int)(char)l.Token.Value).ToString());string k=l.Token.Value is float?"Float":l.Token.Value is double?"Double":l.Token.Value is long?"Long":l.Token.Value is uint?"UInt":l.Token.Value is ulong?"ULong":l.Token.Value is decimal?"Decimal":"Int";return N("CstNumber",Q(Convert.ToString(l.Token.Value,System.Globalization.CultureInfo.InvariantCulture)),"Cst"+k);}
  if(e is IdentifierNameSyntax id)return N("CstName",Q(id.Identifier.Text),"[]");
  if(e is AliasQualifiedNameSyntax alias)return N("CstTypeReference",N("CstAliasType",Q(alias.Alias.Identifier.Text),T(alias.Name)));
  if(e is GenericNameSyntax gn)return N("CstName",Q(gn.Identifier.Text),L(gn.TypeArgumentList.Arguments.Select(T)));
  if(e is ThisExpressionSyntax)return "CstThis";if(e is BaseExpressionSyntax)return "CstBase";
  if(e is MemberAccessExpressionSyntax ma)return N("CstMemberAccess",E(ma.Expression),Q(ma.Name.Identifier.Text),ma.Name is GenericNameSyntax mg?L(mg.TypeArgumentList.Arguments.Select(T)):"[]");
  if(e is InvocationExpressionSyntax call)return N("CstCall",E(call.Expression),L(call.ArgumentList.Arguments.Select(Arg)));
  if(e is ElementAccessExpressionSyntax ix)return N("CstIndex",E(ix.Expression),L(ix.ArgumentList.Arguments.Select(Arg)));
  if(e is ObjectCreationExpressionSyntax obj)return N("CstObjectNew",T(obj.Type),obj.ArgumentList==null?"[]":L(obj.ArgumentList.Arguments.Select(Arg)),obj.Initializer==null?"[]":L(obj.Initializer.Expressions.Select(E)));
  if(e is ArrayCreationExpressionSyntax arr)return N("CstArrayNew",T(arr.Type.ElementType),L(arr.Type.RankSpecifiers[0].Sizes.Where(s=>!s.IsKind(SyntaxKind.OmittedArraySizeExpression)).Select(E)),arr.Initializer==null?"[]":L(arr.Initializer.Expressions.Select(E)),arr.Initializer==null?"False":"True");
  if(e is ImplicitArrayCreationExpressionSyntax ia)return N("CstImplicitArray",L(ia.Initializer.Expressions.Select(E)));
  if(e is ParenthesizedExpressionSyntax pe)return N("CstParenthesized",E(pe.Expression));
  if(e is CastExpressionSyntax cast)return N("CstCast",T(cast.Type),E(cast.Expression));
  if(e is TypeOfExpressionSyntax type)return N("CstTypeOf",T(type.Type));
  if(e is DefaultExpressionSyntax def)return N("CstDefault",T(def.Type));
  if(e is ConditionalExpressionSyntax tern)return N("CstConditional",E(tern.Condition),E(tern.WhenTrue),E(tern.WhenFalse));
  if(e is BinaryExpressionSyntax bin){if(bin.IsKind(SyntaxKind.AsExpression))return N("CstAs",E(bin.Left),T((TypeSyntax)bin.Right));if(bin.IsKind(SyntaxKind.IsExpression))return N("CstIs",E(bin.Left),T((TypeSyntax)bin.Right));var map=new Dictionary<string,string>{{"+","Add"},{"-","Subtract"},{"*","Multiply"},{"/","Divide"},{"%","Remainder"},{"==","Equal"},{"!=","NotEqual"},{"<","Less"},{"<=","LessEqual"},{">","Greater"},{">=","GreaterEqual"},{"&&","And"},{"||","Or"},{"&","BitAnd"},{"|","BitOr"},{"^","BitXor"},{"<<","ShiftLeft"},{">>","ShiftRight"},{"??","Coalesce"}};return N("CstBinary","Cst"+map[bin.OperatorToken.Text],E(bin.Left),E(bin.Right));}
  if(e is AssignmentExpressionSyntax set){var map=new Dictionary<string,string>{{"=","Assign"},{"+=","AddAssign"},{"-=","SubtractAssign"},{"*=","MultiplyAssign"},{"/=","DivideAssign"},{"&=","AndAssign"},{"|=","OrAssign"},{"^=","XorAssign"}};return N("CstAssignment","Cst"+map[set.OperatorToken.Text],E(set.Left),E(set.Right));}
  if(e is PrefixUnaryExpressionSyntax pre){var map=new Dictionary<string,string>{{"!","Not"},{"-","Negate"},{"+","Positive"},{"~","Complement"},{"++","PreIncrement"},{"--","PreDecrement"}};return N("CstUnary","Cst"+map[pre.OperatorToken.Text],E(pre.Operand));}
  if(e is PostfixUnaryExpressionSyntax post)return N("CstUnary",post.OperatorToken.Text=="++"?"CstPostIncrement":"CstPostDecrement",E(post.Operand));
  if(e is SimpleLambdaExpressionSyntax lam)return lam.Body is BlockSyntax lb?N("CstLambdaBlock",L(new[]{Param(lam.Parameter)}),S(lb)):N("CstLambda",L(new[]{Param(lam.Parameter)}),E((ExpressionSyntax)lam.Body));
  if(e is ParenthesizedLambdaExpressionSyntax pl)return pl.Body is BlockSyntax pb?N("CstLambdaBlock",L(pl.ParameterList.Parameters.Select(Param)),S(pb)):N("CstLambda",L(pl.ParameterList.Parameters.Select(Param)),E((ExpressionSyntax)pl.Body));
  if(e is CheckedExpressionSyntax ce)return N("CstChecked",ce.IsKind(SyntaxKind.CheckedExpression)?"True":"False",E(ce.Expression));
  throw Bad(e);
 }
 static string S(StatementSyntax s){
  if(s is BlockSyntax b)return N("CstBlock",L(b.Statements.Select(S)));
  if(s is ExpressionStatementSyntax es)return N("CstExpression",E(es.Expression));
  if(s is LocalDeclarationStatementSyntax d)return N("CstVariables",Mods(d.Modifiers),T(d.Declaration.Type),Vars(d.Declaration.Variables));
  if(s is ReturnStatementSyntax r)return N("CstReturn",M(r.Expression==null?null:E(r.Expression)));
  if(s is ThrowStatementSyntax th)return N("CstThrow",M(th.Expression==null?null:E(th.Expression)));
  if(s is IfStatementSyntax i)return N("CstIf",E(i.Condition),S(i.Statement),M(i.Else==null?null:S(i.Else.Statement)));
  if(s is ForEachStatementSyntax fe)return N("CstForEach",T(fe.Type),Q(fe.Identifier.Text),E(fe.Expression),S(fe.Statement));
  if(s is ForStatementSyntax f)return N("CstFor",f.Declaration!=null?N("CstVariables","[]",T(f.Declaration.Type),Vars(f.Declaration.Variables)):f.Initializers.Count==0?"CstEmpty":N("CstExpression",E(f.Initializers[0])),M(f.Condition==null?null:E(f.Condition)),L(f.Incrementors.Select(E)),S(f.Statement));
  if(s is WhileStatementSyntax w)return N("CstWhile",E(w.Condition),S(w.Statement));
  if(s is BreakStatementSyntax)return "CstBreak";if(s is ContinueStatementSyntax)return "CstContinue";if(s is EmptyStatementSyntax)return "CstEmpty";
  if(s is TryStatementSyntax tr)return N("CstTry",S(tr.Block),L(tr.Catches.Select(c=>"CstCatch { cc-type = "+M(c.Declaration==null?null:T(c.Declaration.Type))+", cc-name = "+Q(c.Declaration==null?"":c.Declaration.Identifier.Text)+", cc-filter = "+M(c.Filter==null?null:E(c.Filter.FilterExpression))+", cc-body = "+S(c.Block)+" }")),M(tr.Finally==null?null:S(tr.Finally.Block)));
  if(s is SwitchStatementSyntax sw)return N("CstSwitch",E(sw.Expression),L(sw.Sections.Select(sec=>"CstSwitchSection { cs-labels = "+L(sec.Labels.Select(l=>l is CaseSwitchLabelSyntax cl?M(E(cl.Value)):"None"))+", cs-body = "+L(sec.Statements.Select(S))+" }")));
  throw Bad(s);
 }
 static string Head(ClassDeclarationSyntax c){return "CstClassHead { ch-modifiers = "+Mods(c.Modifiers)+", ch-name = "+Q(c.Identifier.Text)+", ch-bases = "+(c.BaseList==null?"[]":L(c.BaseList.Types.Select(t=>T(t.Type))))+" }";}
 static string Attrs(SyntaxList<AttributeListSyntax> lists){return L(lists.SelectMany(a=>a.Attributes).Select(a=>"CstAttribute { cat-type = "+T(a.Name)+", cat-args = "+(a.ArgumentList==null?"[]":L(a.ArgumentList.Arguments.Select(v=>E(v.Expression))))+" }"));}
 static string Member(MemberDeclarationSyntax m){
  if(m is FieldDeclarationSyntax f)return N("CstField",Attrs(f.AttributeLists),Mods(f.Modifiers),T(f.Declaration.Type),Vars(f.Declaration.Variables));
  if(m is MethodDeclarationSyntax method)return N("CstMethod","CstMethodSpec { cm-attributes = "+Attrs(method.AttributeLists)+", cm-modifiers = "+Mods(method.Modifiers)+", cm-result = "+T(method.ReturnType)+", cm-name = "+Q(method.Identifier.Text)+", cm-type-params = [], cm-params = "+L(method.ParameterList.Parameters.Select(Param))+", cm-body = "+M(method.Body==null?null:S(method.Body))+" }");
  if(m is DelegateDeclarationSyntax del)return N("CstDelegate","CstMethodSpec { cm-attributes = [], cm-modifiers = "+Mods(del.Modifiers)+", cm-result = "+T(del.ReturnType)+", cm-name = "+Q(del.Identifier.Text)+", cm-type-params = [], cm-params = "+L(del.ParameterList.Parameters.Select(Param))+", cm-body = None }");
  if(m is ConstructorDeclarationSyntax ctor)return N("CstConstructor","CstConstructorSpec { cctor-modifiers = "+Mods(ctor.Modifiers)+", cctor-name = "+Q(ctor.Identifier.Text)+", cctor-params = "+L(ctor.ParameterList.Parameters.Select(Param))+", cctor-body = "+S(ctor.Body)+" }");
  if(m is ClassDeclarationSyntax cls)return N("CstClass",Head(cls),L(cls.Members.Select(Member)));
  throw Bad(m);
 }
 public static string Emit(string source,string prefix){
  var tree=CSharpSyntaxTree.ParseText(source);var errors=tree.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(d=>d.ToString())));
  var root=tree.GetCompilationUnitRoot();var output=new System.Text.StringBuilder("Chapter: Unity "+prefix+"\n\nSection: Typed Interop\n\n");
  foreach(var cls in root.Members.OfType<ClassDeclarationSyntax>()){
   if(cls.Identifier.Text=="Codex_Program"){foreach(var method in cls.Members.OfType<MethodDeclarationSyntax>())output.Append("  unity-").Append(prefix).Append("-effect : Integer -> CstMember\n  unity-").Append(prefix).Append("-effect (unused) =\n    ").Append(Member(method)).Append("\n\n");continue;}
   var stem=prefix+"-"+cls.Identifier.Text.ToLowerInvariant();var names=new List<string>();int index=0;
   foreach(var member in cls.Members){var name=stem+"-member-"+index++;names.Add(name);output.Append("  ").Append(name).Append(" : Integer -> CstMember\n  ").Append(name).Append(" (unused) =\n    ").Append(Member(member)).Append("\n\n");}
   output.Append("  unity-").Append(prefix).Append("-emit : [Console] Nothing\n  unity-").Append(prefix).Append("-emit = cst-stream-class (").Append(Head(cls)).Append(") ").Append(L(names)).Append(" 0\n\n");
  }
  return output.ToString();
 }
}
'@
$result=[CstLift]::Emit([IO.File]::ReadAllText((Resolve-Path $Source).Path),$Prefix)
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Out),($result -replace '\r?\n',"`r`n"),[Text.UTF8Encoding]::new($false))
