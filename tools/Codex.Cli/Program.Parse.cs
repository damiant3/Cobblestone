using Codex.Core;
using Codex.Syntax;
using Codex.Ast;

namespace Codex.Cli;

public static partial class Program
{
    static int RunParse(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex parse <file.codex>");
            return 1;
        }

        string filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);
        DiagnosticBag diagnostics = new();

        DocumentNode document;
        if (ProseParser.IsProseDocument(content))
        {
            Console.WriteLine("(prose-mode document detected)");
            ProseParser proseParser = new(source, diagnostics);
            document = proseParser.ParseDocument();

            Console.WriteLine("\n=== Chapters ===");
            foreach (ChapterNode ch in document.Chapters)
            {
                Console.WriteLine($"  Chapter: {ch.Title}");
                PrintMembers(ch.Members, "    ");
            }
        }
        else
        {
            Lexer lexer = new(source, diagnostics);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();

            Console.WriteLine("=== Tokens ===");
            foreach (Token token in tokens)
            {
                if (token.Kind is TokenKind.Newline or TokenKind.Indent
                    or TokenKind.Dedent or TokenKind.EndOfFile)
                {
                    Console.WriteLine($"  {token.Kind}");
                }
                else
                {
                    Console.WriteLine($"  {token.Kind,-20} {token.Text}");
                }
            }

            Parser parser = new(tokens, diagnostics);
            document = parser.ParseDocument();
        }

        Console.WriteLine("\n=== Definitions ===");
        foreach (DefinitionNode def in document.Definitions)
        {
            string typeStr = def.TypeAnnotation is not null
                ? $" : {FormatType(def.TypeAnnotation.Type)}"
                : "";
            string paramsStr = def.Parameters.Count > 0
                ? " (" + string.Join(") (", def.Parameters.Select(p => p.Text)) + ")"
                : "";
            Console.WriteLine($"  {def.Name.Text}{paramsStr}{typeStr}");
        }

        Desugarer desugarer = new(diagnostics);
        string chapterName = Path.GetFileNameWithoutExtension(filePath);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        Console.WriteLine($"\n=== Chapter: {chapter.Name} ===");
        foreach (Definition def in chapter.Definitions)
        {
            string declType = def.DeclaredType is not null
                ? FormatTypeExpr(def.DeclaredType) : "?";
            Console.WriteLine($"  {def.Name} : {declType}");
        }

        PrintDiagnostics(diagnostics);

        return diagnostics.HasErrors ? 1 : 0;
    }

    static int RunRead(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex read <file.codex>");
            return 1;
        }

        string filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);
        DiagnosticBag diagnostics = new();

        if (!ProseParser.IsProseDocument(content))
        {
            Console.Error.WriteLine(
                "Not a prose-mode document. Use 'codex parse' for notation-only files.");
            return 1;
        }

        ProseParser proseParser = new(source, diagnostics);
        DocumentNode document = proseParser.ParseDocument();

        if (diagnostics.HasErrors)
        {
            PrintDiagnostics(diagnostics);
            return 1;
        }

        foreach (ChapterNode chapter in document.Chapters)
        {
            Console.WriteLine($"\u2550\u2550\u2550 {chapter.Title} \u2550\u2550\u2550");
            Console.WriteLine();
            RenderMembers(chapter.Members, "  ");
        }

        return 0;
    }
}
