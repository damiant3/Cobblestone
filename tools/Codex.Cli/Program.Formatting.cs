using Codex.Core;
using Codex.Syntax;
using Codex.Ast;

namespace Codex.Cli;

public static partial class Program
{
    static void RenderMembers(IReadOnlyList<DocumentMember> members, string indent)
    {
        foreach (DocumentMember member in members)
        {
            switch (member)
            {
                case ProseBlockNode prose:
                    foreach (string line in prose.Text.Split('\n'))
                    {
                        Console.WriteLine($"{indent}{line}");
                    }
                    Console.WriteLine();
                    break;

                case NotationBlockNode notation:
                    foreach (DefinitionNode def in notation.Definitions)
                    {
                        string typeStr = def.TypeAnnotation is not null
                            ? $" : {FormatType(def.TypeAnnotation.Type)}"
                            : "";
                        string paramsStr = def.Parameters.Count > 0
                            ? " (" + string.Join(") (",
                                def.Parameters.Select(p => p.Text)) + ")"
                            : "";
                        Console.WriteLine(
                            $"{indent}  {def.Name.Text}{paramsStr}{typeStr}");
                    }
                    Console.WriteLine();
                    break;

                case SectionNode section:
                    Console.WriteLine($"{indent}--- {section.Title} ---");
                    Console.WriteLine();
                    RenderMembers(section.Members, indent + "  ");
                    break;
            }
        }
    }

    static DocumentNode ParseSourceFile(
        SourceText source, string content, DiagnosticBag diagnostics)
    {
        if (ProseParser.IsProseDocument(content))
        {
            ProseParser proseParser = new(source, diagnostics);
            return PhaseTimer.Time("parse", () => proseParser.ParseDocument());
        }
        else
        {
            Lexer lexer = new(source, diagnostics);
            IReadOnlyList<Token> tokens = PhaseTimer.Time("lex", () => lexer.TokenizeAll());
            Parser parser = new(tokens, diagnostics);
            return PhaseTimer.Time("parse", () => parser.ParseDocument());
        }
    }

    static void PrintMembers(
        IReadOnlyList<DocumentMember> members, string indent)
    {
        foreach (DocumentMember member in members)
        {
            switch (member)
            {
                case ProseBlockNode prose:
                    Console.WriteLine(
                        $"{indent}[prose] {prose.Text.Split('\n')[0]}...");
                    break;
                case NotationBlockNode notation:
                    Console.WriteLine(
                        $"{indent}[notation] {notation.Definitions.Count} definition(s)");
                    foreach (DefinitionNode def in notation.Definitions)
                    {
                        Console.WriteLine($"{indent}  {def.Name.Text}");
                    }
                    break;
                case SectionNode section:
                    Console.WriteLine($"{indent}Section: {section.Title}");
                    PrintMembers(section.Members, indent + "  ");
                    break;
            }
        }
    }

    static string FormatType(TypeNode node) => node switch
    {
        NamedTypeNode n => n.Name.Text,
        FunctionTypeNode f =>
            $"{FormatType(f.Parameter)} \u2192 {FormatType(f.Return)}",
        ApplicationTypeNode a =>
            $"{FormatType(a.Constructor)} " +
            string.Join(" ", a.Arguments.Select(FormatType)),
        ParenthesizedTypeNode p => $"({FormatType(p.Inner)})",
        EffectfulTypeNode e =>
            $"[{string.Join(", ", e.Effects.Select(FormatType))}] " +
            FormatType(e.Return),
        LinearTypeNode l => $"linear {FormatType(l.Inner)}",
        DependentTypeNode d =>
            $"({d.ParamName.Text} : {FormatType(d.ParamType)}) " +
            $"\u2192 {FormatType(d.Body)}",
        IntegerTypeNode i => i.Literal.Text,
        BinaryTypeNode b =>
            $"({FormatType(b.Left)} {b.Operator.Text} " +
            $"{FormatType(b.Right)})",
        _ => "?"
    };

    static string FormatTypeExpr(TypeExpr node) => node switch
    {
        NamedTypeExpr n => n.Name.Value,
        FunctionTypeExpr f =>
            $"{FormatTypeExpr(f.Parameter)} \u2192 {FormatTypeExpr(f.Return)}",
        AppliedTypeExpr a =>
            $"{FormatTypeExpr(a.Constructor)} " +
            string.Join(" ", a.Arguments.Select(FormatTypeExpr)),
        EffectfulTypeExpr e =>
            $"[{string.Join(", ", e.Effects.Select(FormatTypeExpr))}] " +
            FormatTypeExpr(e.Return),
        LinearTypeExpr l => $"linear {FormatTypeExpr(l.Inner)}",
        DependentTypeExpr d =>
            $"({d.ParamName.Value} : {FormatTypeExpr(d.ParamType)}) " +
            $"\u2192 {FormatTypeExpr(d.Body)}",
        IntegerLiteralTypeExpr i => i.Value.ToString(),
        BinaryTypeExpr b =>
            $"({FormatTypeExpr(b.Left)} {b.Op} " +
            $"{FormatTypeExpr(b.Right)})",
        _ => "?"
    };
}
