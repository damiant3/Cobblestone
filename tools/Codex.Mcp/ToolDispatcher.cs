using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;
using Codex.IR;
using Codex.Emit.CSharp;

namespace Codex.Mcp;

sealed class ToolDispatcher
{
    public JsonNode Dispatch(JsonNode request)
    {
        JsonNode? paramsNode = request["params"];
        string? toolName = paramsNode?["name"]?.GetValue<string>();
        JsonNode? arguments = paramsNode?["arguments"];

        return toolName switch
        {
            "codex-check" => HandleCheck(arguments),
            "codex-build" => HandleBuild(arguments),
            "codex-hover" => HandleHover(arguments),
            "codex-parse" => HandleParse(arguments),
            _ => throw new McpException(-32602, $"Unknown tool: {toolName}"),
        };
    }

    public JsonNode ReadResource(JsonNode request)
    {
        string? uri = request["params"]?["uri"]?.GetValue<string>();

        return uri switch
        {
            "codex://builtins" => HandleBuiltinsResource(),
            _ => throw new McpException(-32602, $"Unknown resource: {uri}"),
        };
    }

    static JsonNode HandleCheck(JsonNode? args)
    {
        string file = GetRequiredArg(args, "file");
        McpAnalysisResult result = AnalyzeFile(file);

        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = FormatCheckResult(result, file),
                }
            ),
        };
    }

    static JsonNode HandleBuild(JsonNode? args)
    {
        string path = GetRequiredArg(args, "path");
        string targets = args?["targets"]?.GetValue<string>() ?? "cs";

        string file = path;
        if (Directory.Exists(path))
        {
            string[] codexFiles = Directory.GetFiles(path, "*.codex");
            if (codexFiles.Length == 0)
                throw new McpException(-32602, $"No .codex files found in {path}");
            file = codexFiles[0];
        }

        McpAnalysisResult result = AnalyzeFile(file);
        if (result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new JsonObject
            {
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Build failed with {result.Diagnostics.Length} diagnostic(s):\n" +
                                   FormatDiagnostics(result.Diagnostics),
                    }
                ),
                ["isError"] = true,
            };
        }

        string chapterName = Path.GetFileNameWithoutExtension(file);
        McpIRResult irResult = LowerToIR(file);

        List<string> outputs = new();
        foreach (string target in targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string outputDir = Path.GetDirectoryName(file) ?? ".";
            string output = EmitTarget(irResult, outputDir, chapterName, target);
            outputs.Add(output);
        }

        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"Build succeeded. Outputs:\n" + string.Join("\n", outputs.Select(o => $"  {o}")),
                }
            ),
        };
    }

    static JsonNode HandleHover(JsonNode? args)
    {
        string file = GetRequiredArg(args, "file");
        string name = GetRequiredArg(args, "name");

        McpAnalysisResult result = AnalyzeFile(file);
        CodexType? type = result.Types[name];

        if (type is not null)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"{name} : {type}",
                    }
                ),
            };
        }

        foreach (TypeDef td in result.TypeDefinitions)
        {
            if (td.Name.Value == name)
            {
                return new JsonObject
                {
                    ["content"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = $"type {name}" + (td is VariantTypeDef v
                                ? "\n" + string.Join("\n", v.Constructors.Select(c =>
                                    $"  | {c.Name.Value}" + (c.Fields.Count > 0
                                        ? " " + string.Join(" ", c.Fields.Select(f => $"({f.Type})"))
                                        : "")))
                                : ""),
                        }
                    ),
                };
            }
        }

        throw new McpException(-32602, $"Name not found: {name}");
    }

    static JsonNode HandleParse(JsonNode? args)
    {
        string file = GetRequiredArg(args, "file");
        string mode = args?["mode"]?.GetValue<string>() ?? "ast";

        string text = File.ReadAllText(file);
        SourceText source = new(file, text);
        DiagnosticBag bag = new();

        if (mode == "tokens")
        {
            Lexer lexer = new(source, bag);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            return new JsonObject
            {
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"{tokens.Count} tokens",
                    }
                ),
            };
        }

        Lexer lex = new(source, bag);
        IReadOnlyList<Token> toks = lex.TokenizeAll();
        Parser parser = new(toks, bag);
        DocumentNode doc = parser.ParseDocument();

        if (mode == "cst")
        {
            return new JsonObject
            {
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"CST: {doc.Definitions.Count} definition(s), {bag.ToImmutable().Length} diagnostic(s)",
                    }
                ),
            };
        }

        Desugarer desugarer = new(bag);
        string chapterName = Path.GetFileNameWithoutExtension(file);
        Chapter chapter = desugarer.Desugar(doc, chapterName);

        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"AST: {chapter.Definitions.Count} definition(s), {chapter.TypeDefinitions.Count} type(s), {bag.ToImmutable().Length} diagnostic(s)",
                }
            ),
        };
    }

    static JsonNode HandleBuiltinsResource()
    {
        // The builtins are well-known and static — enumerate them by looking up each
        // name against a fresh TypeEnvironment.WithBuiltins().
        string[] builtinNames =
        [
            "show", "negate", "read-line", "print-line",
            "open-file", "read-all", "close-file",
            "char-at", "text-length", "substring",
            "is-letter", "is-digit", "is-whitespace",
            "text-to-integer", "integer-to-text",
            "text-replace", "char-code", "char-code-at", "code-to-char",
            "list-length", "list-at", "list-tail", "map",
            "text-to-number", "number-to-text",
        ];

        TypeEnvironment env = TypeEnvironment.WithBuiltins();
        List<string> lines = new();
        foreach (string name in builtinNames)
        {
            CodexType? type = env.Lookup(name);
            if (type is not null)
                lines.Add($"{name} : {type}");
        }

        lines.Sort(StringComparer.Ordinal);

        return new JsonObject
        {
            ["contents"] = new JsonArray(
                new JsonObject
                {
                    ["uri"] = "codex://builtins",
                    ["mimeType"] = "text/plain",
                    ["text"] = string.Join("\n", lines),
                }
            ),
        };
    }

    static McpAnalysisResult AnalyzeFile(string file)
    {
        if (!File.Exists(file))
            throw new McpException(-32602, $"File not found: {file}");

        string text = File.ReadAllText(file);
        SourceText source = new(file, text);
        DiagnosticBag bag = new();

        DocumentNode document;
        IReadOnlyList<Token> tokens;
        if (ProseParser.IsProseDocument(text))
        {
            ProseParser proseParser = new(source, bag);
            document = proseParser.ParseDocument();
            tokens = [];
        }
        else
        {
            Lexer lexer = new(source, bag);
            tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, bag);
            document = parser.ParseDocument();
        }

        Desugarer desugarer = new(bag);
        string chapterName = Path.GetFileNameWithoutExtension(file);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        if (bag.HasErrors)
        {
            return new McpAnalysisResult
            {
                Diagnostics = bag.ToImmutable(),
                Types = Map<string, CodexType>.s_empty,
                Definitions = chapter.Definitions,
                TypeDefinitions = chapter.TypeDefinitions,
            };
        }

        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        if (bag.HasErrors)
        {
            return new McpAnalysisResult
            {
                Diagnostics = bag.ToImmutable(),
                Types = Map<string, CodexType>.s_empty,
                Definitions = chapter.Definitions,
                TypeDefinitions = chapter.TypeDefinitions,
            };
        }

        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        return new McpAnalysisResult
        {
            Diagnostics = bag.ToImmutable(),
            Types = types,
            Definitions = resolved.Chapter.Definitions,
            TypeDefinitions = resolved.Chapter.TypeDefinitions,
        };
    }

    static McpIRResult LowerToIR(string file)
    {
        string text = File.ReadAllText(file);
        SourceText source = new(file, text);
        DiagnosticBag bag = new();

        Lexer lexer = new(source, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode document = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        string chapterName = Path.GetFileNameWithoutExtension(file);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, bag);
        IRChapter irModule = lowering.Lower(resolved.Chapter);

        return new McpIRResult(irModule, types);
    }

    static string EmitTarget(McpIRResult irResult, string outputDir, string chapterName, string target)
    {
        if (target is "il" or "exe")
        {
            Codex.Emit.IL.ILEmitter emitter = new();
            byte[] assembly = emitter.EmitAssembly(irResult.Chapter, chapterName);
            string outputPath = Path.Combine(outputDir, chapterName + ".exe");
            File.WriteAllBytes(outputPath, assembly);
            return outputPath;
        }

        Codex.Emit.ICodeEmitter codeEmitter = target switch
        {
            "cs" or "csharp" => new CSharpEmitter(),
            _ => throw new McpException(-32602, $"Unsupported target: {target}. Supported: cs, il"),
        };

        string code = codeEmitter.Emit(irResult.Chapter);
        string ext = target switch
        {
            "cs" or "csharp" => ".cs",
            _ => ".txt",
        };
        string outPath = Path.Combine(outputDir, chapterName + ext);
        File.WriteAllText(outPath, code);
        return outPath;
    }

    static string FormatCheckResult(McpAnalysisResult result, string file)
    {
        int errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        int typeCount = 0;
        foreach (KeyValuePair<string, CodexType> _ in result.Types)
            typeCount++;

        string summary = errors == 0
            ? $"✓ {file}: {typeCount} definition(s), no errors"
            : $"✗ {file}: {errors} error(s), {warnings} warning(s)";

        if (result.Diagnostics.Length > 0)
            summary += "\n" + FormatDiagnostics(result.Diagnostics);

        return summary;
    }

    static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d =>
            $"  [{d.Severity.ToString().ToLowerInvariant()}] line {d.Span.Start.Line}:{d.Span.Start.Column}: {d.Message}"));
    }

    static string GetRequiredArg(JsonNode? args, string name)
    {
        string? value = args?[name]?.GetValue<string>();
        if (value is null)
            throw new McpException(-32602, $"Missing required argument: {name}");
        return value;
    }
}

sealed record McpAnalysisResult
{
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }
    public required Map<string, CodexType> Types { get; init; }
    public required IReadOnlyList<Definition> Definitions { get; init; }
    public required IReadOnlyList<TypeDef> TypeDefinitions { get; init; }
}

sealed record McpIRResult(IRChapter Chapter, Map<string, CodexType> Types);
